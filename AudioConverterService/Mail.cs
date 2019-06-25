using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;

namespace AudioConverterService
{
    public class Mail
    {
        private readonly EventLog eventLog;
        private readonly string mailServer;
        private readonly string mailFrom;
        private readonly string mailTo;
        private readonly int mailPort;
        private readonly string mailSubject;
        private readonly IEnumerable<string> mailIgnoreFilters;

        public Mail(EventLog eventLog, string mailServer, string mailFrom, string mailTo, int mailPort, string mailSubject, string mailIgnoreFilters, string mailIgnoreFilterDelimiter)
        {
            this.eventLog = eventLog;
            this.mailServer = mailServer;
            this.mailFrom = mailFrom;
            this.mailTo = mailTo;
            this.mailPort = mailPort;
            this.mailSubject = mailSubject;

            if (string.IsNullOrWhiteSpace(mailIgnoreFilterDelimiter))
            {
                throw new Exception("The mailIgnoreFilterDelimiter cannot be blank.");
            }

            this.mailIgnoreFilters = mailIgnoreFilters?.Split(new string[] { mailIgnoreFilterDelimiter }, StringSplitOptions.RemoveEmptyEntries)
                .Select(f => f.Trim())
                .Where(f => !string.IsNullOrWhiteSpace(f));
        }

        /// <summary>
        /// If the mail values are set in the config file and the given message doesn't match any ignore filters, it sends an email with the given message 
        /// as the body. If there's a failure sending the email, it sends the exception to WriteEventLog.
        /// </summary>
        /// <param name="message"></param>
        public void SendMail(string message)
        {
            if (ShouldSendMail(message))
            {
                try
                {
                    using (MailMessage mail = new MailMessage(mailFrom, mailTo))
                    {
                        using (SmtpClient client = new SmtpClient())
                        {
                            client.Port = mailPort;
                            client.DeliveryMethod = SmtpDeliveryMethod.Network;
                            client.UseDefaultCredentials = false;
                            client.Host = mailServer;
                            mail.Subject = mailSubject;
                            mail.Body = message;
                            client.Send(mail);
                        }
                    }
                        
                }
                catch (Exception ex)
                {
                    Shared.WriteEventLogEntry(eventLog, "Failure sending mail: " + ex.ToString(), EventLogEntryType.Error);
                }
            }
        }

        /// <summary>
        /// Returns true if the message shouldn't be ignored (baes on this.mailIgnoreFilters) and if the mail values are all set in App.config.
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        private bool ShouldSendMail(string message)
        {
            return
                !MessageShouldBeIgnored(message)
                && !String.IsNullOrWhiteSpace(this.mailServer)
                && !String.IsNullOrWhiteSpace(this.mailFrom)
                && !String.IsNullOrWhiteSpace(this.mailTo)
                && !String.IsNullOrWhiteSpace(this.mailSubject);
        }

        /// <summary>
        /// Retrurns true if this.mailIgnoreFilters is not null and the given message contains one of its filters.
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        private bool MessageShouldBeIgnored(string message)
        {
            return this.mailIgnoreFilters?.Any(f => message.Contains(f)) == true;
        }
    }
}
