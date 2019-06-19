using Google.Apis.Auth.OAuth2;
using Google.Cloud.Speech.V1;
using Google.Cloud.Storage.V1;
using Grpc.Auth;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TrackdocDbEntityFramework;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Configuration;
using System.Data.Entity.SqlServer;
using System.Diagnostics;
using Microsoft.WindowsAPICodePack.Shell;
using Newtonsoft.Json.Linq;

namespace AudioConverterService
{
    class SpeechRecognition
    {
        private const int googleCredentialRefreshTimeInMinutes = 20;
        private const string googleCredentialScope = "https://www.googleapis.com/auth/cloud-platform";
        private readonly string bucketName;
        private readonly bool useOpus;
        private readonly string tempDirectoryPath;
        private readonly string googleServiceAccountKeyPath;
        private readonly string googleServiceAccountKeyJson;
        private readonly int maxAlternatives;
        private GoogleCredential googleCredential;
        private DateTime googleCredentialCreationTime;
        private EventLog eventLog;
        private Mail mail;

        public SpeechRecognition(EventLog eventLog, Mail mail, string tempDirectoryPath)
        {
            this.eventLog = eventLog;
            this.mail = mail;
            this.tempDirectoryPath = tempDirectoryPath;
            bool.TryParse(ConfigurationManager.AppSettings["compressToOpusForSpeechRec"], out useOpus);
            this.googleServiceAccountKeyPath = ConfigurationManager.AppSettings["googleServiceAccountKeyPath"];

            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;  // Make this service to support TLS-1.2, which is required by AutoPunctuation Flask API.

            if (string.IsNullOrWhiteSpace(this.googleServiceAccountKeyPath))
            {
                throw new Exception("The googleServiceAccountKeyPath app setting must be set in App.config.");
            }
            else if (!File.Exists(this.googleServiceAccountKeyPath))
            {
                throw new Exception("No file exists at the googleServiceAccountKeyPath.");
            }

            this.googleServiceAccountKeyJson = File.ReadAllText(googleServiceAccountKeyPath);
            this.bucketName = ConfigurationManager.AppSettings["googleStorageBucketName"];

            if (string.IsNullOrWhiteSpace(this.bucketName))
            {
                throw new Exception("The googleStorageBucketName app setting must be set in App.config.");
            }

            if (!int.TryParse(ConfigurationManager.AppSettings["speechRecognitionMaxAlternatives"], out this.maxAlternatives))
            {
                this.maxAlternatives = 5;// Default value.
            }
        }

        /// <summary>
        /// For any speechrec_jobs with Processing status, polls the status of the operation with Google and updates the speechrec_job accordingly if 
        /// it's completed.
        /// </summary>
        /// <param name="db"></param>
        public void HandleCompleted(trackdocEntities db)
        {
            try
            {
                WriteDebugEntry("Getting speechrec_jobs in Processing status.");

                var processingSpeechRecJobs = db.speechrec_job
                    .Where(s => s.delete_flag == false && s.status_id == EnumTable.speechrec_status.Processing && s.operation_name != null)
                    .ToList();// ToList() since we're updating incrementally and it would cause an error otherwise.

                WriteDebugEntry("Successfully retrieved " + processingSpeechRecJobs.Count + " speechrec_jobs in Processing status.");

                foreach (var speechRecJob in processingSpeechRecJobs)
                {
                    try
                    {
                        var longRunningRecognizePollResponse = GetLongRunningRecognizePollResponse(speechRecJob.operation_name);
                        WriteDebugEntry("Successfully retrieved long-running recognize poll response for operation " + speechRecJob.operation_name + ".");

                        if (longRunningRecognizePollResponse.IsFaulted || longRunningRecognizePollResponse.Exception != null) // Rarely gets in here for some reason.
                        {
                            speechRecJob.status_id = EnumTable.speechrec_status.Error;
                            speechRecJob.last_modified_by = 0;
                            speechRecJob.date_last_modified = DateTime.Now;
                            db.SaveChanges();
                            DeleteObjectFromGoogleStorage(speechRecJob.audio_id.ToString()); // Should we be deleting it here?
                            HandleError("Poll response completed with failure for speechRecJob " + speechRecJob.id + ": ", longRunningRecognizePollResponse?.Exception);
                        }
                        else if (longRunningRecognizePollResponse.IsCompleted)
                        {
                            speechRecJob.status_id = EnumTable.speechrec_status.Complete;
                            speechRecJob.output = longRunningRecognizePollResponse.Result.Results.ToString();
                            speechRecJob.last_modified_by = 0;
                            //speechRecJob.date_last_modified = DateTime.Now;

                            string inputScript = ConvertGoogleJsonStr(speechRecJob.output);
                            
                            AutoPunctuationFlask.InvokeRequestResponseService(inputScript).Wait();
                            if (AutoPunctuationFlask.IsSuccessfully) //AutoPunctuationFlask.Response.IsSuccessStatusCode) 
                            {
                                speechRecJob.postprocoutput = RemoveRedundantPunc(AutoPunctuationFlask.ScriptWithAutoPunc);
                            }
                            else
                                WriteEventLogEntry(AutoPunctuationFlask.ApiErrorMessage, EventLogEntryType.Error);

                            speechRecJob.date_last_modified = DateTime.Now;

                            db.SaveChanges();
                            WriteEventLogEntry("Updated speechrec_job " + speechRecJob?.id + " with output from Google.", EventLogEntryType.Information);
                            DeleteObjectFromGoogleStorage(speechRecJob.audio_id.ToString());
                        }
                    }
                    catch (Exception ex)
                    {
                        HandleError("Error with poll response for speechRecJob " + speechRecJob?.id + ": ", ex);
                    }
                }
            }
            catch (Exception ex)
            {
                HandleError("SpeechRecognition HandleCompleted() exception: ", ex);
            }
        }

        // Conver google speech-to-text result from Json format to string.
        public string ConvertGoogleJsonStr(string jsonStr)
        {
            var json = JArray.Parse(jsonStr);
            var topAlternatives = json.Select(j => j["alternatives"].FirstOrDefault());
            var transcripts = topAlternatives.Select(a => a["transcript"].ToString());
            var transcript = string.Join(" ", transcripts);

            /*
            // If the audio file was dictated with punctions, remove these punctuations.
            List<string> puncArr = new List<string> { ",", ".", ":", ";", "?", "!", "-"};
            foreach (var punc in puncArr)
                transcript = transcript.Replace(punc, "");
            */

            return transcript;
        }


        /// <summary>
        /// Remove the rudundant punctuation from the given string. If there are two adjacent punctuation, remove the second one.
        /// The google speech recognition result may contains punctuation, if punctuation was dictated during audio recording. In 
        /// this case, duplicated punctuation maybe introduced by the AutoPunctuation feature. Solution is remove the punctuation 
        /// introduced by AutoPunctuation.
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        public string RemoveRedundantPunc(string str)
        {
            if (str == null || str.Length < 2)
                return str;

            StringBuilder sb = new StringBuilder();

            List<char> puncList = new List<char> { ',', '.', ':', ';', '!', '?', '-' };

            int len = str.Length;
            for (int i = 0; i < len - 1; i++)
            {
                if (puncList.Contains(str[i]) && puncList.Contains(str[i + 1]))  // Two punctuation adjencent
                {
                    str = str.Remove(i + 1, 1); // Remove the second punctuation
                    len = str.Length; // Update lenth.
                }
            }

            return str;
        }

        /// <summary>
        /// Returns any non-deleted speechrec_jobs with Pending status from the database.
        /// </summary>
        /// <param name="db"></param>
        /// <returns></returns>
        public IQueryable<speechrec_job> GetPendingSpeechRecJobs(trackdocEntities db)
        {
            return db.speechrec_job.Where(s => s.delete_flag == false && s.status_id == EnumTable.speechrec_status.Pending);
        }

        /// <summary>
        /// Returns a set of enumerated IDs from the given pendingSpeechRecJobs.
        /// </summary>
        /// <param name="pendingSpeechRecJobs"></param>
        /// <returns></returns>
        public IEnumerable<int> GetPendingSpeechRecJobIds(IQueryable<speechrec_job> pendingSpeechRecJobs)
        {
            var speechRecJobIds = pendingSpeechRecJobs.Select(s => s.audio_id).ToList();
            WriteDebugEntry("Successfully retrieved IDs of " + speechRecJobIds.Count + " speechrec_jobs in Pending status from database.");
            return speechRecJobIds;
        }

        /// <summary>
        /// Initiates a long-running speech recognition operation with Google for the given speechRecJob and unconvertedPath.
        /// </summary>
        /// <param name="speechRecJob"></param>
        /// <param name="unconvertedPath"></param>
        /// <param name="db"></param>
        public void InitiateSpeechRecProcessing(speechrec_job speechRecJob, string unconvertedPath, trackdocEntities db)
        {
            try
            {
                WriteDebugEntry("Initiating speech rec processing for speechrec_job " + speechRecJob.id + ".");
                string pathToUploadFrom = null;
                var originalFormat = Path.GetExtension(unconvertedPath).TrimStart('.').ToLower();
                var objectName = Path.GetFileNameWithoutExtension(unconvertedPath);

                try
                {
                    pathToUploadFrom = useOpus ? GetOpusPathToUploadFrom(unconvertedPath, originalFormat, objectName) : GetWavPathToUploadFrom(unconvertedPath, originalFormat, objectName);
                    UploadFile(objectName, pathToUploadFrom);
                    var operationName = GetOperationName(objectName);
                    speechRecJob.operation_name = operationName;
                    speechRecJob.status_id = EnumTable.speechrec_status.Processing;
                    db.SaveChanges();
                    WriteDebugEntry("Updated speechRecJob " + speechRecJob.id + " operation_name field and set status to Processing in database.");
                }
                finally
                {
                    if (pathToUploadFrom?.Contains(".speechRec.") == true && File.Exists(pathToUploadFrom)) { File.Delete(pathToUploadFrom); }
                }
            }
            catch (Exception ex)
            {
                speechRecJob.status_id = EnumTable.speechrec_status.Error;
                db.SaveChanges();
                HandleError("InitiateSpeechRecProcessing exception for speechRecJob " + speechRecJob?.id + ": ", ex);
            }
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
        /// Writes to the event log, writes to ELMAH, and sends an email.
        /// </summary>
        /// <param name="errorMessage"></param>
        /// <param name="ex"></param>
        private void HandleError(string errorMessage, Exception ex)
        {
            WriteEventLogEntry(errorMessage + Environment.NewLine + ex?.ToString(), EventLogEntryType.Error);
            if (ex != null) { Elmah.CreateLogEntry(ex, eventLog); }
            mail.SendMail(errorMessage + ex?.Message);
        }

        /// <summary>
        /// Converts the given unconvertedPath to a WAV file if it doesn't already exist, named with the given object name.
        /// </summary>
        /// <param name="unconvertedPath"></param>
        /// <param name="originalFormat"></param>
        /// <param name="objectName"></param>
        /// <returns></returns>
        private string GetWavPathToUploadFrom(string unconvertedPath, string originalFormat, string objectName)
        {
            string pathToUploadFrom;

            if (originalFormat == "dss" || originalFormat == "ds2")
            {
                pathToUploadFrom = Path.Combine(tempDirectoryPath, objectName + ".wav");// Already should be converted.
                if (!File.Exists(pathToUploadFrom)) { throw new Exception("WAV is expected to already exist but doesn't."); }
            }
            else if (originalFormat == "wav" && WavIsValid(unconvertedPath))// Use original given WAV:
            {
                pathToUploadFrom = unconvertedPath;
            }
            else if (Shared.ValidFfmpegExtensions.Contains(originalFormat) || originalFormat == "mp3")// Convert to WAV:
            {
                pathToUploadFrom = Path.Combine(tempDirectoryPath, objectName + ".speechRec.wav");
                var quality = "-ac 1 -ar 16000";// Specifies mono and 16000Hz sample rate as required by Google.
                Shared.FfmpegConvert(ConfigurationManager.AppSettings["ffmpegPath"], unconvertedPath, pathToUploadFrom, eventLog, quality);
            }
            else
            {
                throw new Exception("Unsupported audio format.");
            }

            return pathToUploadFrom;
        }

        /// <summary>
        /// Converts the given unconvertedPath to an OGG Opus file, named with the given object name and .speechRec.opus.
        /// </summary>
        /// <param name="unconvertedPath"></param>
        /// <param name="originalFormat"></param>
        /// <param name="objectName"></param>
        /// <returns></returns>
        private string GetOpusPathToUploadFrom(string unconvertedPath, string originalFormat, string objectName)
        {
            string conversionSourcePath;

            if (originalFormat == "dss" || originalFormat == "ds2")
            {
                conversionSourcePath = Path.Combine(tempDirectoryPath, objectName + ".wav");// Should already exist.
            }
            else if (Shared.ValidFfmpegExtensions.Contains(originalFormat) || originalFormat == "mp3")
            {
                conversionSourcePath = unconvertedPath;
            }
            else
            {
                throw new Exception("Unsupported audio format.");
            }

            var pathToUploadFrom = Path.Combine(tempDirectoryPath, objectName + ".speechRec.opus");
            var quality = "-ac 1 -ar 16000 -acodec libopus";// Specifies opus format, mono, and 16000Hz sample rate, as required by Google.
            Shared.FfmpegConvert(ConfigurationManager.AppSettings["ffmpegPath"], conversionSourcePath, pathToUploadFrom, eventLog, quality);
            return pathToUploadFrom;
        }

        /// <summary>
        /// Returns true if WAV at given filePath is mono and has a 16000Hz sample rate, as required by Google.
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        private bool WavIsValid(string filePath)
        {
            using (var shellFile = ShellFile.FromFilePath(filePath))
            {
                var audioProperties = shellFile?.Properties?.System?.Audio;
                var sampleRate = audioProperties?.SampleRate?.Value;
                var channels = audioProperties?.ChannelCount?.Value;
                var sampleRateIsValid = sampleRate == 16000;
                var isMono = channels == 1;
                return sampleRateIsValid && isMono;
            }
        }

        /// <summary>
        /// Initiates long-running recognize operation with Google and returns it. The audio file is from Google storage using the given objectName.
        /// </summary>
        /// <param name="bucketName"></param>
        /// <param name="objectName"></param>
        /// <returns></returns>
        private Google.LongRunning.Operation<LongRunningRecognizeResponse, LongRunningRecognizeMetadata> GetLongRunningRecognizeOperation(string objectName)
        {
            var uri = "gs://" + bucketName + "/" + objectName;
            var channel = new Grpc.Core.Channel(SpeechClient.DefaultEndpoint.Host, GetCredential().ToChannelCredentials());
            var speechClient = SpeechClient.Create(channel);

            var recognizeConfig = new RecognitionConfig()
            {
                Encoding = useOpus ? RecognitionConfig.Types.AudioEncoding.OggOpus : RecognitionConfig.Types.AudioEncoding.Linear16,
                SampleRateHertz = 16000,
                LanguageCode = "en-US",
                EnableWordTimeOffsets = true,
                MaxAlternatives = maxAlternatives,
            };

            WriteDebugEntry("Initiating long-running speech recognition of object " + objectName + " in bucket " + bucketName + ".");
            return speechClient.LongRunningRecognize(recognizeConfig, RecognitionAudio.FromStorageUri(uri));
        }

        /// <summary>
        /// Deletes the object corresponding to the given objectName from Google Storage and creates an event log entry.
        /// </summary>
        /// <param name="objectName"></param>
        private void DeleteObjectFromGoogleStorage(string objectName)
        {
            var storage = StorageClient.Create(GetCredential());
            WriteDebugEntry("Initiating Google Storage deletion of object " + objectName + ".");
            storage.DeleteObject(bucketName, objectName);
            WriteEventLogEntry("Deleted object " + objectName + " in bucket " + bucketName + " from Google Storage.", EventLogEntryType.Information);
        }

        /// <summary>
        /// Uploads the file at the given localPath to Google Storage with the given objectName and creates an event log entry.
        /// </summary>
        /// <param name="objectName"></param>
        /// <param name="localPath"></param>
        private void UploadFile(string objectName, string localPath)
        {
            var storage = StorageClient.Create(GetCredential());

            using (var fileStream = File.OpenRead(localPath))
            {
                WriteDebugEntry("Initiating upload of " + localPath + " to object " + objectName + " in bucket " + bucketName + " in Google Storage.");
                storage.UploadObject(bucketName, objectName, "audio/wav", fileStream);
            }

            WriteEventLogEntry("Uploaded " + localPath + " to object " + objectName + " in bucket " + bucketName + " in Google Storage.", EventLogEntryType.Information);
        }

        /// <summary>
        /// Initiates long-running recognize operation with Google and returns its name. The audio file is from Google storage using the given objectName.
        /// </summary>
        /// <param name="objectName"></param>
        /// <returns></returns>
        private string GetOperationName(string objectName)
        {
            var longRunnignOperationName = GetLongRunningRecognizeOperation(objectName).Name;
            WriteDebugEntry("Retrieved long-running speech recognition operation name for object " + objectName + ".");
            return longRunnignOperationName;
        }

        /// <summary>
        /// Returns a poll response for the given operationName corresponding to a long-running recognize operation.
        /// </summary>
        /// <param name="operationName"></param>
        /// <returns></returns>
        private Google.LongRunning.Operation<LongRunningRecognizeResponse, LongRunningRecognizeMetadata> GetLongRunningRecognizePollResponse(string operationName)
        {
            var channel = new Grpc.Core.Channel(SpeechClient.DefaultEndpoint.Host, GetCredential().ToChannelCredentials());
            var speechClient = SpeechClient.Create(channel);
            WriteDebugEntry("Initiating polling of long-running recognize operation " + operationName + ".");
            return speechClient.PollOnceLongRunningRecognize(operationName);
        }

        /// <summary>
        /// Returns the existing this.googleCredential if it's less than 20 minutes old. Otherwsie creates a new one, sets it to this.googleCredential and 
        /// returns it. Creates an event log entry if a new credential is created.
        /// </summary>
        /// <returns></returns>
        private GoogleCredential GetCredential()
        {
            if (this.googleCredential == null || this.googleCredentialCreationTime == null || DateTime.Now - this.googleCredentialCreationTime > TimeSpan.FromMinutes(googleCredentialRefreshTimeInMinutes))
            {
                WriteDebugEntry("Initiating retrieval of new Google API Cloud Platform Credential.");
                this.googleCredential = GoogleCredential.FromJson(this.googleServiceAccountKeyJson).CreateScoped(googleCredentialScope);

                //using (var stream = new FileStream(googleServiceAccountKeyPath, FileMode.Open, FileAccess.Read))
                //{
                //    //var serviceAccountCredential = ServiceAccountCredential.FromServiceAccountData(stream);
                //    this.googleCredential = GoogleCredential.FromStream(stream).CreateScoped(googleCredentialScope);
                //}

                this.googleCredentialCreationTime = DateTime.Now;
                WriteEventLogEntry("Created/retrieved new Google API Cloud Platform Credential.", EventLogEntryType.Information);
            }

            return this.googleCredential;
        }
    }
}
