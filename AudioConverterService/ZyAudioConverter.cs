using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Configuration;
using interop.OlyAudCom;
using Microsoft.WindowsAPICodePack.Shell;
using System.Net;
using System.Xml;
using System.Net.Mail;
using DPMCONTROLLib;
using TrackdocDbEntityFramework;

namespace AudioConverterService
{
    public partial class ZyAudioConverter : ServiceBase
    {
        private System.Threading.Thread procThread;
        private volatile bool terminate = false;
        private string connectionString;
        private string ffmpegExe;
        private string tempDirectoryPath;
        private string outputPath;
        private string olympusKey;
        private int idleLoopsNo;
        private int audioPurgeCutoffInDays;
        private string quality;
        private string mailServer;
        int mailPort = 25;
        string mailSubject;
        string mailFrom;
        string mailTo;
        string mailIgnoreFilterDelimiter;
        string mailIgnoreFilters;
        bool outputPathIsRemote;
        private Exception _asyncException;
        private EventLog eventLog;
        private OAFileControl olympusConverter;
        private DPMControl philipsConverter;
        private Mail mail;

        public void StartConsole()
        {
            OnStart(null);
        }

        public void StopConsole()
        {
            OnStop();
        }

        private class AudioMetadata
        {
            public int audioId { get; set; }
            public string Filename { get; set; }
            public string TempWorkingFilename { get; set; }
            public string OriginalAudioType { get; set; }
            public string UnconvertedPath { get; set; }
            public string IntermediateWavPath { get; set; }
            public string FinalPath { get; set; }

            //public bool FinalPathIsRemote
            //{
            //    get
            //    {
            //        if (string.IsNullOrWhiteSpace(FinalPath))
            //        {
            //            throw new Exception("FinalPathIsRemote cannot be checked because FinalPath is null.");
            //        }
            //        else
            //        {
            //            return new Uri(this.FinalPath).IsUnc;
            //        }
            //    }
            //}
        }

        public ZyAudioConverter(EventLog eventLog)
        {
            InitializeComponent();
            this.eventLog = eventLog;
            ReadSettings();
            this.mail = new Mail(eventLog, mailServer, mailFrom, mailTo, mailPort, mailSubject, mailIgnoreFilters, mailIgnoreFilterDelimiter);

            //Assigning unhandled exception handler procedure
            AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(UnhandledExceptionHandler);

            this.olympusConverter = new OAFileControl();
            this.olympusConverter.FailIfExists = true;// This makes it throw an exception if the outputFile already exists instead of overwriting it.

            this.philipsConverter = new DPMControl();
            this.philipsConverter.Initialize(false);
        }


        /// <summary>
        /// Procedure for unhandled exceptionn handling. Any unhandled exception will be sent here.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void UnhandledExceptionHandler(object sender, UnhandledExceptionEventArgs e)
        {
            // Cache the unhandled excpetion and begin a shutdown of the service
            _asyncException = e.ExceptionObject as Exception;
            //invokes OnStop event
            Stop();
        }

        /// <summary>
        /// Read initial configuration details from settings xml file
        /// </summary>
        private void ReadSettings()
        {
            var appSettings = ConfigurationManager.AppSettings;

            ffmpegExe = appSettings["ffmpegPath"];
            tempDirectoryPath = appSettings["tempDirectoryPath"];
            outputPath = appSettings["outputPath"];
            connectionString = appSettings["connectionString"];
            olympusKey = appSettings["olympusKey"];
            quality = appSettings["encodingQuality"];
            mailServer = appSettings["mailServer"];
            mailSubject = appSettings["mailSubject"];
            mailFrom = appSettings["mailFrom"];
            mailTo = appSettings["mailTo"];
            mailIgnoreFilterDelimiter = appSettings["mailIgnoreFilterDelimiter"];
            mailIgnoreFilters = appSettings["mailIgnoreFilters"];
            bool.TryParse(appSettings["outputPathIsRemote"], out outputPathIsRemote);
            int.TryParse(appSettings["audioPurgeCutoffInDays"], out audioPurgeCutoffInDays);

            if (audioPurgeCutoffInDays > 0 && audioPurgeCutoffInDays <= 9)
            {
                throw new Exception("The audioPurgeCutoffInDays must either be 0 (to disable) or greater than 9.");
            }

            if (!String.IsNullOrEmpty(appSettings["mailPort"]))
            {
                mailPort = Int32.Parse(appSettings["mailPort"]);
            }

            int.TryParse(appSettings["idleLoopsNo"], out idleLoopsNo);
            if (idleLoopsNo == 0) idleLoopsNo = 30;
        }

        /// <summary>
        /// watcher thread method. Here all the processing happens.
        /// </summary>
        public async void watcherThread()
        {
            try
            {
                int Counter = 0;
                //ThreadPool.SetMaxThreads(6, 6); //set the max number of concurrent threads
                CheckConfig();//Check prerequsites. (ffmpeg.exe particularly)
                var speechRecognition = new SpeechRecognition(eventLog, mail, tempDirectoryPath);

                //starting the continious loop to watch for the new jobs. Use volatile boolean variable terminate to stop gracefully
                while (!terminate)
                {
                    if (Counter >= idleLoopsNo)
                    {
                        Counter = 0;

                        try
                        {
                            using (var db = new trackdocEntities())
                            {
                                speechRecognition.HandleCompleted(db);
                                var audioIdsToConvert = GetJobList().OrderByDescending(x => x).ToList();
                                var pendingSpeechRecognitionJobs = speechRecognition.GetPendingSpeechRecJobs(db);
                                var pendingSpeechRecAudioIds = speechRecognition.GetPendingSpeechRecJobIds(pendingSpeechRecognitionJobs);
                                var audioIdsToDownload = audioIdsToConvert.Union(pendingSpeechRecAudioIds).Distinct().OrderBy(a => a);
                                var audioMetadatas = GetAudioMetadatas(audioIdsToDownload, db);

                                foreach (var audioId in audioIdsToDownload)
                                {
                                    AudioMetadata audioMetadata = null;

                                    try
                                    {
                                        audioMetadata = audioMetadatas.First(a => a.audioId == audioId);
                                        await Shared.StreamBlobToFile(connectionString, audioId, audioMetadata.UnconvertedPath, eventLog);

                                        if (audioMetadata.OriginalAudioType == "dss" || audioMetadata.OriginalAudioType == "ds2")
                                        {
                                            ConvertToWav(audioMetadata);
                                        }

                                        if (pendingSpeechRecAudioIds.Contains(audioId))
                                        {
                                            var speechRecJob = pendingSpeechRecognitionJobs.First(s => s.audio_id == audioId);
                                            speechRecognition.InitiateSpeechRecProcessing(speechRecJob, audioMetadata.UnconvertedPath, db);
                                        }

                                        if (audioIdsToConvert.Contains(audioId))
                                        {
                                            ConvertAudio(audioMetadata);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        SetErrorStatus(audioId);
                                        Elmah.CreateLogEntry(ex, eventLog);
                                        WriteEventLogEntry("Processing of audio " + audioId + " failed: " + ex.ToString(), EventLogEntryType.Error);
                                        mail.SendMail("Error processing audio " + audioId + ": " + ex.Message);
                                    }
                                    finally
                                    {
                                        DeleteIfExists(audioMetadata?.UnconvertedPath);
                                        DeleteIfExists(audioMetadata?.IntermediateWavPath);
                                        DeleteIfExists(audioMetadata?.FinalPath);
                                    }
                                }
                            }

                            PurgeOldAudios();
                        }
                        catch (Exception ex)
                        {
                            Elmah.CreateLogEntry(ex, eventLog);
                            WriteEventLogEntry(ex.ToString(), EventLogEntryType.Error);
                            mail.SendMail("Audio Processor Service Error: " + ex.Message);
                        }
                    }
                    else
                    {
                        ++Counter;
                        Thread.Sleep(1000);
                    }
                }
            }
            catch (Exception ex1)
            {
                Elmah.CreateLogEntry(ex1, eventLog);
                WriteEventLogEntry(ex1.ToString(), EventLogEntryType.Error);
                mail.SendMail("Audio Processor Service Error: " + ex1.Message);
                //Environment.Exit(1);
                throw new InvalidOperationException("Exception in service", ex1); // this cause the service to stop with status as failed
            }

            WriteEventLogEntry("Service stopped gracefully.", EventLogEntryType.Information);
            mail.SendMail("Audio Processor Service Stopped...");
            terminate = false;
        }

        /// <summary>
        /// Calls Shared.WriteEventLogEntry with this.eventLog, the given message, and the given eventLogEntryType.
        /// </summary>
        /// <param name="message"></param>
        /// <param name="eventLogEntryType"></param>
        private void WriteEventLogEntry(string message, EventLogEntryType eventLogEntryType)
        {
            Shared.WriteEventLogEntry(this.eventLog, message, eventLogEntryType);
        }

        /// <summary>
        /// Calls Shared.WriteDebugEntry with this.eventLog and the given message.
        /// </summary>
        /// <param name="message"></param>
        private void WriteDebugEntry(string message)
        {
            Shared.WriteDebugEntry(this.eventLog, message);
        }

        /// <summary>
        /// Sets the audio_lob field to empty for any audio records older than this.audioPurgeCutoffInDays. Processes a maximum 
        /// of 10,000 records in a single run to prevent delaying audio conversion.
        /// </summary>
        private void PurgeOldAudios()
        {
            if (this.audioPurgeCutoffInDays > 0)// 0 or negative value disables audio purging.
            {
                const int maxNumberToPurge = 10000;// So a single run never delays converting for too long (10000 should take less than 2 minutes). 
                var db = new trackdocEntities();

                try
                {
                    WriteDebugEntry("Initiating retrieval of IDs of audios to purge from database.");
                    var unpurgedAudioIds = AudioPurge.Shared.GetUnpurgedAudioIds(db, this.audioPurgeCutoffInDays);
                    WriteDebugEntry("Successfully retrieved IDs of " + unpurgedAudioIds.Length + " audios to purge from database.");
                    int? lastPurgedAudioId = null;
                    int numberPurged = 0;

                    while (unpurgedAudioIds.Any() && numberPurged < maxNumberToPurge)
                    {
                        foreach (var unpurgedAudioId in unpurgedAudioIds)
                        {
                            WriteDebugEntry("Initiating purge of audio " + unpurgedAudioId + ".");
                            AudioPurge.Shared.PurgeAudioRecord(unpurgedAudioId, db);
                            WriteEventLogEntry("Purged audio " + unpurgedAudioId + ".", EventLogEntryType.Information);
                            lastPurgedAudioId = unpurgedAudioId;
                            numberPurged++;
                        }

                        db.Dispose();
                        db = new trackdocEntities();
                        WriteDebugEntry("Initiating retrieval of IDs of audios to purge from database.");
                        unpurgedAudioIds = AudioPurge.Shared.GetUnpurgedAudioIds(db, this.audioPurgeCutoffInDays, lastPurgedAudioId);
                        WriteDebugEntry("Successfully retrieved IDs of " + unpurgedAudioIds.Length + " audios to purge from database.");
                    }

                    WriteDebugEntry("Successfully purged " + numberPurged + " audios.");
                }
                finally
                {
                    db?.Dispose();
                }
            }
        }

        private void ConvertAudio(AudioMetadata audioMetadata)
        {
            if (Shared.ValidFfmpegExtensions.Contains(audioMetadata.OriginalAudioType))
            {
                ConvertToMp3(audioMetadata, false);
            }
            else if (audioMetadata.OriginalAudioType == "dss" || audioMetadata.OriginalAudioType == "ds2")
            {
                //ConvertToWav(audioMetadata);// Converted WAV should already have been created.
                ConvertToMp3(audioMetadata, true);
            }
            else
            {
                throw new Exception("Unsupported audio format.");
            }

            UpdateDB(audioMetadata);
        }

        /// <summary>
        /// Checks if the ffmpeg executable exists
        /// </summary>
        /// <returns></returns>
        private bool CheckConfig()
        {
            var exist = File.Exists(ffmpegExe);
            if (!exist)
            {
                throw new Exception("Can not locate ffmpeg.exe");
            };

            return exist;
        }

        /// <summary>
        /// Sets the job status to the Error state so it is not processed again
        /// </summary>
        /// <param name="audioId"></param>
        private void SetErrorStatus(int audioId)
        {
            using (var conn = new SqlConnection(connectionString))
            using (var command = new SqlCommand("UPDATE job SET status_id = (SELECT id FROM job_status WHERE name = 'Audio Error') FROM job INNER JOIN job_audio ON job_audio.job_id = job.id WHERE audio_id = @AudioId", conn))
            {

                command.Parameters.AddWithValue("AudioId", audioId);

                conn.Open();

                var rows = command.ExecuteNonQuery();

            }

        }

        private void UpdateDB(AudioMetadata audioMetadata)
        {
            using (var conn = new SqlConnection(connectionString))
            {
                using (var command = new SqlCommand("[dbo].[usp_update_audio_and_job_new]", conn))
                {
                    command.CommandType = CommandType.StoredProcedure;

                    command.Parameters.Add(new SqlParameter("@audioid", audioMetadata.audioId));
                    command.Parameters.Add(new SqlParameter("@audiofilepath", audioMetadata.FinalPath));
                    command.Parameters.Add(new SqlParameter("@audio_type", "mp3"));
                    command.Parameters.Add(new SqlParameter("@bitrate", GetMp3Bitrate(audioMetadata.FinalPath)));
                    command.Parameters.Add(new SqlParameter("@length", GetMp3DurationInSeconds(audioMetadata.FinalPath)));
                    command.Parameters.Add(new SqlParameter("@filename", audioMetadata.Filename + ".mp3"));

                    command.Parameters.Add(new SqlParameter("@RET_VAL", SqlDbType.Int));
                    command.Parameters["@RET_VAL"].Direction = ParameterDirection.ReturnValue;

                    command.CommandTimeout = 0;// Disable command timeout.
                    conn.Open();

                    command.ExecuteScalar();
                    var retVal = (int)command.Parameters["@RET_VAL"].Value;
                    conn.Close();

                    if ((retVal != 0)) throw new Exception("Error while updating db records...");
                }
            }

            WriteEventLogEntry("Updated audio " + audioMetadata.audioId + " in database with converted file at " + audioMetadata.FinalPath + ".", EventLogEntryType.Information);
            File.Delete(audioMetadata.FinalPath);
        }

        private static string GetMp3Bitrate(string filePath)
        {
            using (var shellFile = ShellFile.FromFilePath(filePath))
            {
                var rawBitrate = shellFile.Properties.System.Audio.EncodingBitrate.Value;

                if (rawBitrate == null)
                {
                    throw new Exception("MP3 bitrate could not be determined.");
                }

                var bitrateInt = int.Parse(rawBitrate.ToString()) / 1000;
                return bitrateInt + " kbps";
            }
        }

        private static int GetMp3DurationInSeconds(string filePath)
        {
            using (var shellFile = ShellFile.FromFilePath(filePath))
            {
                var rawDuration = shellFile.Properties.System.Media.Duration.Value;// Unit is 100ns.

                if (rawDuration == null)
                {
                    throw new Exception("MP3 duration could not be determined.");
                }

                var seconds = rawDuration / 10000000;
                return int.Parse(seconds.ToString());
            }        
        }

        private IEnumerable<AudioMetadata> GetAudioMetadatas(IEnumerable<int> audioIds, TrackdocDbEntityFramework.trackdocEntities db)
        {
            WriteDebugEntry("Initiating retrieval of audio metadata from database.");
            var audioMetadatas = new List<AudioMetadata>();

            // Not doing a single select here since there could be a lot and it won't work as an IQueryable anyway since some of the properties are set manually:
            foreach (var audioId in audioIds)
            {
                var audioInfo = db.audios
                    .Where(a => a.id == audioId)
                    .Select(a => new { Filename = a.filename, OriginalAudioType = a.audio_type.name.ToLower() })
                    .FirstOrDefault();

                if (audioInfo != null)
                {
                    var audioMetadata = new AudioMetadata();

                    audioMetadata.audioId = audioId;
                    audioMetadata.TempWorkingFilename = audioId.ToString();
                    audioMetadata.Filename = audioInfo.Filename;
                    audioMetadata.OriginalAudioType = audioInfo.OriginalAudioType;
                    audioMetadata.UnconvertedPath = Path.Combine(tempDirectoryPath, audioId + "." + audioMetadata.OriginalAudioType);
                    audioMetadata.IntermediateWavPath = Path.Combine(tempDirectoryPath, audioId + ".wav");
                    audioMetadata.FinalPath = outputPath + audioId + ".mp3";

                    audioMetadatas.Add(audioMetadata);
                }
            }

            WriteDebugEntry("Successfully retrieved metatdata of " + audioMetadatas.Count + " audios from database.");
            return audioMetadatas;
        }

        private void OlympusConvert(string sourcePath, string outputFile)
        {
            WriteDebugEntry("Initiating Olympus conversion of " + sourcePath + " to " + outputFile);
            var dssFile = this.olympusConverter.GetDssFileInterface(sourcePath);
            this.olympusConverter.Convert(dssFile, outputFile, oaPcmFormat.OA_PCM_FORMAT_16kHz_16bit_MONO, null, olympusKey, oaProcessingMode.OA_FILECONTROL_PROCESSING_MODE_SYNC, null);
        }

        /// <summary>
        /// Converts the DSS or DS2 file at the given sourcePath to a WAV file at the given outputFile path using the Philips DPMControl converter.
        /// </summary>
        /// <param name="sourcePath"></param>
        /// <param name="outputFile"></param>
        /// <returns></returns>
        private void PhilipsConvert(string sourcePath, string outputFile)
        {
            WriteDebugEntry("Initiating Philips conversion of " + sourcePath + " to " + outputFile);
            this.philipsConverter.Convert(sourcePath, outputFile, dpmAudioFormat.dpmAudioFormat_PCM_16kHz_16bit_mono, false);
        }

        private void ConvertToWav(AudioMetadata audioMetadata)
        {
            string converter;
            DeleteIfExists(audioMetadata.IntermediateWavPath);

            try
            {
                PhilipsConvert(audioMetadata.UnconvertedPath, audioMetadata.IntermediateWavPath);
                converter = "Philips";
            }
            catch (Exception philipsEx)
            {
                var error = "Philips exception:" + Environment.NewLine + philipsEx.ToString();
                WriteEventLogEntry("Philips converter could not convert " + audioMetadata.UnconvertedPath + " to " + audioMetadata.IntermediateWavPath + ". Will fallback to Olympus converter. " + error, EventLogEntryType.Warning);

                try
                {
                    OlympusConvert(audioMetadata.UnconvertedPath, audioMetadata.IntermediateWavPath);
                    converter = "Olympus";
                }
                catch (Exception olympusEx)
                {
                    error += Environment.NewLine + Environment.NewLine + "Olympus exception:" + Environment.NewLine + olympusEx.ToString();
                    throw new Exception("Failure converting " + audioMetadata.UnconvertedPath + " to " + audioMetadata.IntermediateWavPath + Environment.NewLine + Environment.NewLine + error);
                }
            }

            WriteEventLogEntry("Converted " + audioMetadata.UnconvertedPath + " to " + audioMetadata.IntermediateWavPath + " using " + converter + " converter.", EventLogEntryType.Information);
        }

        private void ConvertToMp3(AudioMetadata audioMetadata, bool useIntermediateWavAsSource)
        {
            string workingOutputPath;
            string sourcePath;

            if (useIntermediateWavAsSource)
            {
                sourcePath = audioMetadata.IntermediateWavPath;
            }
            else
            {
                sourcePath = audioMetadata.UnconvertedPath;
            }
                
            //var finalPathIsRemote = audioMetadata.FinalPathIsRemote;
            //var finalPathIsRemote = true;// Hard-coding to true for now in case network drive is mapped to a local path.

            if (outputPathIsRemote)
            {
                Directory.CreateDirectory(tempDirectoryPath);
                workingOutputPath = Path.Combine(tempDirectoryPath, audioMetadata.TempWorkingFilename + ".mp3");
            }
            else
            {
                workingOutputPath = audioMetadata.FinalPath;
            }

            try
            {
                Shared.FfmpegConvert(ffmpegExe, sourcePath, workingOutputPath, eventLog, quality);

                if (outputPathIsRemote && workingOutputPath != audioMetadata.FinalPath)
                {
                    File.Copy(workingOutputPath, audioMetadata.FinalPath);
                    WriteEventLogEntry("Copied " + workingOutputPath + " to " + audioMetadata.FinalPath, EventLogEntryType.Information);
                }
            }
            finally
            {
                if (outputPathIsRemote && workingOutputPath != audioMetadata.FinalPath)
                {
                    DeleteIfExists(workingOutputPath);
                }
            }
        }

        /// <summary>
        /// Retrives the list of audioid to convert
        /// by executing the stored procedure
        /// </summary>
        /// <returns></returns>
        private List<int> GetJobList()
        {
            WriteDebugEntry("Initiating retrieval of IDs of jobs in Converting status from database.");

            using (var conn = new SqlConnection(connectionString))
            using (var command = new SqlCommand("[dbo].[usp_get_converting_jobs]", conn)
            {
                CommandType = CommandType.StoredProcedure
            })
            {

                conn.Open();

                var jobList = new List<int>();

                var reader = command.ExecuteReader();

                while (reader.Read())
                {
                    jobList.Add((int)reader[0]);
                }

                WriteDebugEntry("Successfully retrieved IDs of " + jobList.Count + " jobs in Converting status from database.");
                return jobList;
            }

        }

        private void DeleteIfExists(string filePath)
        {
            if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }

        /// <summary>
        /// This is the startup method for debugging.
        ///  It is called automatically only in debug mode. Put here any method you want to debug.
        /// </summary>
        public void OnDebug()
        {

            //var bitRate = GetDSSBitRate(@"E:\DPM 0027.DSS");

            //try
            //{

            //    throw new Exception("Test Audio Conversion Failure exception...");

            //}
            //catch (Exception ex)
            //{
            //    elmaErrorLog(ex);
            //}

            mail.SendMail("Test mail message from Audio Processor Service.");
            //SetErrorStatus(65603);
            //watcherThread();
            //OnStart(null);
            //Thread.Sleep(5000);
            //OnStop();
        }


        /// <summary>
        /// Method called when the service is being started. It is overriden to start processing thread.
        /// </summary>
        /// <param name="args"></param>
        protected override void OnStart(string[] args)
        {
            procThread = new System.Threading.Thread(new ThreadStart(watcherThread)); //create processing thread
            procThread.Start(); //start processing thread
            base.OnStart(args);
            WriteEventLogEntry("Service started.", EventLogEntryType.Information);
            mail.SendMail("Audio Processor Service started."); // Send mail to notify that service started
        }


        /// <summary>
        /// Method called when the service is being stopped.
        ///  Overriden to check for unhandled exceptions and gracefully terminating the processing thread.
        /// </summary>
        protected override void OnStop()
        {

            if (_asyncException != null)
                throw new InvalidOperationException("Unhandled exception in service", _asyncException);

            terminate = true; // notify the processing thread to exit the loop
            procThread.Join(); // waiting the processing thread stops
            base.OnStop();
            WriteEventLogEntry("Service stopped.", EventLogEntryType.Information);
        }
    }
}
