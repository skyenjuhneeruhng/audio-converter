using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace AudioConverterService
{
    class Elmah
    {
        public static void CreateLogEntry(Exception ex, EventLog eventLog)
        {
            try
            {
                using (var conn = new SqlConnection(ConfigurationManager.AppSettings["connectionString"]))
                {
                    using (var command = new SqlCommand())
                    {
                        command.Connection = conn;
                        command.CommandType = CommandType.Text;

                        command.CommandText = "INSERT INTO ELMAH_Error (ErrorId, Application, Host, Type, Source, Message, [User], StatusCode, TimeUtc, AllXml)" +
                            " VALUES (@id, @application, @host, @type, @source, @message, @user, @statuscode, @timeUtc, @allxml)";

                        var errorId = Guid.NewGuid();
                        var host = Dns.GetHostName();
                        DateTime dateTime = DateTime.UtcNow;

                        command.Parameters.AddWithValue("@id", errorId);
                        command.Parameters.AddWithValue("@application", "TrackDoc");
                        command.Parameters.AddWithValue("@host", Shared.TruncateString(host, 50));
                        command.Parameters.AddWithValue("@type", Shared.TruncateString(ex.GetType().ToString(), 100));
                        command.Parameters.AddWithValue("@source", Shared.TruncateString(ex.Source, 60) ?? string.Empty);
                        command.Parameters.AddWithValue("@message", Shared.TruncateString("Audio Processor Service: " + ex.Message, 500));
                        command.Parameters.AddWithValue("@user", string.Empty);
                        command.Parameters.AddWithValue("@statuscode", 0);
                        command.Parameters.AddWithValue("@timeUtc", dateTime.ToString("yyyy-MM-dd HH:mm:ss.fff"));

                        var xml = string.Format("<error application=\"{0}\" host=\"{1}\" message=\"{2}\" source=\"{3}\" detail=\"{4}\" user=\"{5}\" time=\"{6}\" statusCode=\"{7}\"> </error>",
                            "TrackDoc", host, XmlEscape(ex.Message), XmlEscape(ex.Source), XmlEscape(ex.ToString()), "", dateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ"), 0);

                        command.Parameters.AddWithValue("@allxml", xml);

                        conn.Open();
                        command.ExecuteNonQuery();
                    }
                }
            }
            catch(Exception metaException)
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
