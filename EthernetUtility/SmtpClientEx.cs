using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;

namespace NetUtility
{

    public interface ISmtpClient : IDisposable
    {
        Task SendMailAsync(MailMessage message);
    }

    public class DefaultSmtpClient : ISmtpClient
    {
        private readonly SmtpClient _client;

        public DefaultSmtpClient(string host, int port, bool enableSsl, NetworkCredential credential, int timeoutSeconds = 30)
        {
            _client = new SmtpClient(host, port)
            {
                EnableSsl = enableSsl,
                Credentials = credential,
                Timeout = timeoutSeconds * 1000
            };
        }

        public Task SendMailAsync(MailMessage message)
        {
            return _client.SendMailAsync(message);
        }

        public void Dispose()
        {
            _client?.Dispose();
        }
    }

    public class SmtpClientEx : IDisposable
    {
        private readonly ISmtpClient _client;

        public SmtpClientEx(string host, int port, bool enableSsl, string username, string password, int timeoutSeconds = 30)
        {
            var cred = new NetworkCredential(username, password);
            _client = new DefaultSmtpClient(host, port, enableSsl, cred, timeoutSeconds);
        }

        public SmtpClientEx(ISmtpClient client)
        {
            _client = client;
        }

        public async Task SendAsync(
            string from,
            IEnumerable<string> to,
            string subject,
            string body,
            bool isHtml = false,
            IEnumerable<string> cc = null,
            IEnumerable<string> bcc = null,
            Dictionary<string, string> headers = null,
            IEnumerable<Attachment> attachments = null)
        {
            var message = new MailMessage
            {
                From = new MailAddress(from),
                Subject = subject,
                Body = body,
                IsBodyHtml = isHtml
            };

            foreach (var addr in to)
            {
                if (!string.IsNullOrWhiteSpace(addr)) message.To.Add(addr);
            }

            if (cc != null)
            {
                foreach (var addr in cc)
                {
                    if (!string.IsNullOrWhiteSpace(addr)) message.CC.Add(addr);
                }
            }

            if (bcc != null)
            {
                foreach (var addr in bcc)
                {
                    if (!string.IsNullOrWhiteSpace(addr)) message.Bcc.Add(addr);
                }
            }

            if (headers != null)
            {
                foreach (var kv in headers)
                {
                    if (!string.IsNullOrWhiteSpace(kv.Key) && kv.Value != null)
                    {
                        message.Headers.Add(kv.Key, kv.Value);
                    }
                }
            }

            if (attachments != null)
            {
                foreach (var att in attachments)
                {
                    if (att != null) message.Attachments.Add(att);
                }
            }

            try
            {
                await _client.SendMailAsync(message);
            }
            catch (Exception ex)
            {
                throw new SmtpException($"Error sending email: {ex.Message}", ex);
            }
        }

        public void Dispose()
        {
            _client?.Dispose();
        }
    }
}
