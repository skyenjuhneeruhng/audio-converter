using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace AudioConverterService
{
    public class Shared
    {
        public static readonly string[] ValidFfmpegExtensions = new string[] { "wav", "wma", "wmv", "m4a", "aac", "aif", "aiff", "ogg" };

        /// <summary>
        /// Returns the given text truncated to the given maximumLength if needed. Returns null if the given text is null.
        /// </summary>
        /// <param name="text"></param>
        /// <param name="maximumLength"></param>
        /// <returns></returns>
        public static string TruncateString(string text, int maximumLength)
        {
            if (text == null)
            {
                return null;
            }
            else
            {
                return new string(text.Take(maximumLength).ToArray());
            }
        }

        /// <summary>
        /// Creates an Event Log entry using the given parameters. Truncates the given message to 31839 characters first to prevent exception.
        /// </summary>
        /// <param name="eventLog"></param>
        /// <param name="message"></param>
        /// <param name="eventLogEntryType"></param>
        public static void WriteEventLogEntry(EventLog eventLog, string message, EventLogEntryType eventLogEntryType)
        {
            const int maxEventLogMessageLength = 31839;// 31839 maximum length for event log strings, even though error says limit is 32766.

            //// Could split the message into multiple entries instead of truncating:
            //var splitMessages = Enumerable.Range(0, message.Length / maxEventLogMessageLength)
            //    .Select(i => message.Substring(i * maxMessageLength, maxEventLogMessageLength));

            var truncatedMessage = TruncateString(message, maxEventLogMessageLength);
            eventLog.WriteEntry(truncatedMessage, eventLogEntryType);
        }

        /// <summary>
        /// Writes an Information type event log entry with the given message if verboseLoggingEnabled is set to true in App.config. The 
        /// message is preceded by "DEBUG:" in the log entry.
        /// </summary>
        /// <param name="eventLog"></param>
        /// <param name="message"></param>
        public static void WriteDebugEntry(EventLog eventLog, string message)
        {
            bool verboseLoggingEnabled;
            bool.TryParse(ConfigurationManager.AppSettings["verboseLoggingEnabled"], out verboseLoggingEnabled);

            if (verboseLoggingEnabled)
            {
                WriteEventLogEntry(eventLog, "DEBUG: " + Environment.NewLine + message, EventLogEntryType.Information);
            }
        }

        public static void FfmpegConvert(string ffmpegExePath, string sourcePath, string outputPath, EventLog eventLog, string additionalArguments = null)
        {
            WriteDebugEntry(eventLog, "Initiating FFmpeg conversion of " + sourcePath + " to " + outputPath + ".");

            using (var process = new Process())
            {
                process.StartInfo.FileName = ffmpegExePath;
                process.StartInfo.Arguments = string.Format("-y -ac 1 -i {0} {1} {2}", "\"" + sourcePath + "\"", additionalArguments, "\"" + outputPath + "\"");
                process.StartInfo.CreateNoWindow = true;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardError = true;
                process.Start();

                string standardError = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    throw new Exception(standardError);
                }
            }

            WriteEventLogEntry(eventLog, "Converted " + sourcePath + " to " + outputPath + " using FFmpeg.", EventLogEntryType.Information);
        }

        public static async Task StreamBlobToFile(string connectionString, int audioId, string outputPath, EventLog eventLog)
        {
            WriteDebugEntry(eventLog, "Initiating download of audio " + audioId + " from database to " + outputPath + ".");

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();

                using (SqlCommand command = new SqlCommand("SELECT audio_lob FROM audio WITH (NOLOCK) WHERE id = @id", connection))
                {
                    command.Parameters.AddWithValue("id", audioId);
                    command.CommandTimeout = 0;

                    // Executed with the SequentialAccess behavior to enable network streaming to prevent BLOB from going into memory:
                    using (SqlDataReader reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess))
                    {
                        if (await reader.ReadAsync())
                        {
                            if (!(await reader.IsDBNullAsync(0)))
                            {
                                using (FileStream file = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
                                {
                                    using (Stream data = reader.GetStream(0))
                                    {
                                        await data.CopyToAsync(file);// Asynchronously copy the stream from the server to the file.
                                        WriteDebugEntry(eventLog, "Successfully downloaded audio " + audioId + " from database to " + outputPath + ".");
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        //private static void StreamBlobToFile(string connectionString, int audioId, string outputPath)
        //{
        //    using (SqlConnection connection = new SqlConnection(connectionString))
        //    {
        //        connection.Open();
        //        int chunkFirstByteIndex = 0;
        //        int chunkSizeInBytes = 256000;// 256KB buffer.
        //        var lobLastByteIndex = GetAudioLengthInBytes(audioId - 1);// Subtracting 1 because indexes start at 0.
        //        const string commandText = @"SELECT SUBSTRING(audio_lob, @start, @length) FROM audio WITH (NOLOCK) WHERE id = @audioId";

        //        using (var outputStream = File.OpenWrite(outputPath))
        //        {
        //            while (chunkFirstByteIndex <= lobLastByteIndex)
        //            {
        //                int chunkLastByteIndex = chunkFirstByteIndex + (chunkSizeInBytes - 1);

        //                if (chunkLastByteIndex > lobLastByteIndex)
        //                {
        //                    chunkSizeInBytes = GetChunkSizeInBytes(chunkFirstByteIndex, lobLastByteIndex);
        //                }

        //                using (SqlCommand command = new SqlCommand(commandText, connection))
        //                {
        //                    command.Parameters.AddWithValue("@audioId", audioId);
        //                    command.Parameters.AddWithValue("@start", chunkFirstByteIndex + 1);// Adding 1 here because SQL SUBSTRING is 1-based, not 0-based.
        //                    command.Parameters.AddWithValue("@length", chunkSizeInBytes);

        //                    using (SqlDataReader reader = command.ExecuteReader())
        //                    {
        //                        if (reader.Read())// Only should be returning a single row, so only need to call Read() once.
        //                        {
        //                            byte[] buffer = new byte[chunkSizeInBytes];
        //                            reader.GetBytes(0, 0, buffer, 0, chunkSizeInBytes);// Only returning a single field.
        //                            outputStream.Write(buffer, chunkFirstByteIndex, chunkSizeInBytes);
        //                            outputStream.Flush();
        //                        }
        //                        else// No rows were returned.
        //                        {
        //                            throw new Exception("Couldn't read audio LOB data from database.");
        //                        }
        //                    }
        //                }

        //                chunkFirstByteIndex = chunkLastByteIndex + 1;
        //            }
        //        }
        //    }
        //}

        ///// <summary>
        ///// Returns the size in bytes of data starting at the given chunkFirstByteIndex and ending at the given chunkLastByteIndex.
        ///// </summary>
        ///// <param name="chunkFirstByteIndex"></param>
        ///// <param name="chunkLastByteIndex"></param>
        ///// <returns></returns>
        //private static int GetChunkSizeInBytes(int chunkFirstByteIndex, int chunkLastByteIndex)
        //{
        //    return chunkLastByteIndex - chunkFirstByteIndex + 1;// Adding 1 because it's an inclusive range.
        //}

        //private static int GetAudioLengthInBytes(int audioId)
        //{
        //    using (var db = new TrackdocDbEntityFramework.trackdocEntities())
        //    {
        //        return db.audios
        //            .Where(a => a.id == audioId)
        //            .Select(a => System.Data.Entity.SqlServer.SqlFunctions.DataLength(a.audio_lob).Value)
        //            .FirstOrDefault();
        //    }
        //}
    }
}
