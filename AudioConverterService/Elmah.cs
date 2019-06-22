using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Net;
using System.Security;
using TrackdocDbEntityFramework;

namespace AudioConverterService
{
    class Elmah
    {
        public static void CreateLogEntry(Exception ex, EventLog eventLog)
        {
            try
            {
                using (trackdocEntities trackdocEntities = new trackdocEntities())
                {
                    ELMAH_Error elmahError = new ELMAH_Error();

                    var errorId = Guid.NewGuid();
                    var host = Dns.GetHostName();
                    DateTime dateTime = DateTime.UtcNow;

                    elmahError.ErrorId = errorId;
                    elmahError.Host = Shared.TruncateString(host, 50);
                    elmahError.Application = "TrackDoc";

                    elmahError.Type = Shared.TruncateString(ex.GetType().ToString(), 100);
                    elmahError.Source = Shared.TruncateString(ex.Source, 60) ?? string.Empty;
                    elmahError.Message = Shared.TruncateString("Audio Processor Service: " + ex.Message, 500);
                    elmahError.User = string.Empty;
                    elmahError.StatusCode = 0;
                    elmahError.TimeUtc = dateTime.ToUniversalTime();
                    var xml = string.Format("<error application=\"{0}\" host=\"{1}\" message=\"{2}\" source=\"{3}\" detail=\"{4}\" user=\"{5}\" time=\"{6}\" statusCode=\"{7}\"> </error>",
                                   "TrackDoc", host, XmlEscape(ex.Message), XmlEscape(ex.Source), XmlEscape(ex.ToString()), "", dateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ"), 0);

                    elmahError.AllXml = xml;

                    trackdocEntities.ELMAH_Error.Add(elmahError);
                    trackdocEntities.SaveChanges();
                }
            }
            catch (Exception metaException)
            {
                Shared.WriteEventLogEntry(eventLog, "Error writing ELMAH log entry: " + metaException.ToString(), EventLogEntryType.Error);
            }

        }

        private static string XmlEscape(string unescapedString)
        {
            return SecurityElement.Escape(unescapedString);

            //// Doesn't escape quotes:
            //XmlDocument doc = new XmlDocument();
            //XmlNode node = doc.CreateElement("root");
            //node.InnerText = unescapedString;
            //return node.InnerXml;
        }
    }
}
