using System.Net;
using System.Net.Mail;

namespace Atcbassemblyrecipe.Services
{
    public interface IEmailSender
    {
        bool IsConfigured { get; }
        Task<(bool Sent, string Message)> SendAsync(IReadOnlyCollection<string> recipients, string subject, string htmlBody);
    }

    // Uses System.Net.Mail rather than a NuGet mail library on purpose: it ships with
    // .NET, so this needs no package restore on a locked-down corporate build server.
    //
    // Nothing here throws into the request pipeline. Callers persist their data first
    // and treat a failed send as a warning, never as a lost submission.
    public class EmailSender : IEmailSender
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<EmailSender> _logger;

        public EmailSender(IConfiguration configuration, ILogger<EmailSender> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public bool IsConfigured =>
            _configuration.GetValue("Email:Enabled", false) &&
            !string.IsNullOrWhiteSpace(_configuration["Email:SmtpHost"]) &&
            !string.IsNullOrWhiteSpace(_configuration["Email:From"]);

        public async Task<(bool Sent, string Message)> SendAsync(IReadOnlyCollection<string> recipients, string subject, string htmlBody)
        {
            if (!IsConfigured)
            {
                return (false, "Email is not configured. Set the Email section in appsettings.json to enable notifications.");
            }

            var validRecipients = recipients
                .Where(address => !string.IsNullOrWhiteSpace(address))
                .Select(address => address.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (validRecipients.Count == 0)
            {
                return (false, "No email recipients were found.");
            }

            try
            {
                using var message = new MailMessage
                {
                    From = new MailAddress(_configuration["Email:From"]!, _configuration["Email:FromName"] ?? "ATCB Assembly Recipe"),
                    Subject = subject,
                    Body = htmlBody,
                    IsBodyHtml = true
                };

                foreach (var recipient in validRecipients)
                {
                    message.To.Add(recipient);
                }

                using var client = new SmtpClient(
                    _configuration["Email:SmtpHost"],
                    _configuration.GetValue("Email:SmtpPort", 25))
                {
                    EnableSsl = _configuration.GetValue("Email:UseSsl", false)
                };

                var userName = _configuration["Email:Username"];
                if (!string.IsNullOrWhiteSpace(userName))
                {
                    client.Credentials = new NetworkCredential(userName, _configuration["Email:Password"]);
                }
                else
                {
                    // Most internal relays accept the app server anonymously.
                    client.UseDefaultCredentials = true;
                }

                await client.SendMailAsync(message);
                return (true, $"Notification sent to {validRecipients.Count} recipient(s).");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send notification email to {Recipients}.", string.Join(", ", validRecipients));
                return (false, $"Notification email could not be sent ({ex.Message}). The request itself was saved.");
            }
        }
    }
}
