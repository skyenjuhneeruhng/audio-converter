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
    }
}
