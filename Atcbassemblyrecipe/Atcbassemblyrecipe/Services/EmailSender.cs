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
    //
    // Two delivery modes:
    //   Email:SmtpHost        - hand the message to a relay (normal operation)
    //   Email:PickupDirectory - write it to disk as a .eml file, contacting nothing
    //
    // Email:RecipientOverride redirects every message to a fixed list whatever the
    // caller computed, so a live relay can be exercised without mailing the real
    // Super Admins. Leave it out of production configuration.
    //
    // PickupDirectory wins when both are set. It exists because the corporate relay
    // only accepts registered servers, so a developer workstation is refused with
    // "5.7.1 Client host rejected" no matter how the rest is configured. Writing the
    // file instead lets the whole notification path - recipients, subject, rendered
    // body - be verified without a relay, and it works unchanged in any environment
    // that has no mail server at all.
    public class EmailSender : IEmailSender
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<EmailSender> _logger;

        public EmailSender(IConfiguration configuration, ILogger<EmailSender> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        private string? PickupDirectory => _configuration["Email:PickupDirectory"];

        public bool IsConfigured =>
            _configuration.GetValue("Email:Enabled", false) &&
            !string.IsNullOrWhiteSpace(_configuration["Email:From"]) &&
            (!string.IsNullOrWhiteSpace(_configuration["Email:SmtpHost"]) ||
             !string.IsNullOrWhiteSpace(PickupDirectory));

        public async Task<(bool Sent, string Message)> SendAsync(IReadOnlyCollection<string> recipients, string subject, string htmlBody)
        {
            if (!IsConfigured)
            {
                return (false, "Email is not configured. Set Email:Enabled with either Email:SmtpHost or Email:PickupDirectory, plus Email:From, in appsettings.json.");
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

            var pickupDirectory = PickupDirectory;

            // Redirect before anything is built, so no real address can reach the relay
            // by accident while testing.
            var intendedRecipients = validRecipients;
            var overrideList = (_configuration.GetSection("Email:RecipientOverride").Get<string[]>() ?? [])
                .Where(address => !string.IsNullOrWhiteSpace(address))
                .Select(address => address.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var redirected = overrideList.Count > 0;
            if (redirected)
            {
                _logger.LogWarning(
                    "Email:RecipientOverride is set. {Count} intended recipient(s) were replaced by {Override}.",
                    intendedRecipients.Count, string.Join(", ", overrideList));
                validRecipients = overrideList;
            }

            try
            {
                var body = redirected
                    ? htmlBody + $"""
                        <hr style="border:none;border-top:1px solid #d8e1e8;margin:18px 0">
                        <p style="color:#c93b18;font-size:12px;font-family:Segoe UI,Arial,sans-serif">
                        <strong>Redirected test message.</strong> Email:RecipientOverride is set, so this was
                        sent to you instead of the {intendedRecipients.Count} intended recipient(s):
                        {System.Net.WebUtility.HtmlEncode(string.Join(", ", intendedRecipients))}</p>
                        """
                    : htmlBody;

                using var message = new MailMessage
                {
                    From = new MailAddress(_configuration["Email:From"]!, _configuration["Email:FromName"] ?? "ATCB Assembly Recipe"),
                    Subject = redirected ? "[REDIRECTED] " + subject : subject,
                    Body = body,
                    IsBodyHtml = true
                };

                foreach (var recipient in validRecipients)
                {
                    message.To.Add(recipient);
                }

                using var client = new SmtpClient();

                if (!string.IsNullOrWhiteSpace(pickupDirectory))
                {
                    // Nothing is sent. SmtpClient serialises the message to a .eml file
                    // that Outlook opens directly, so the body can be reviewed as the
                    // recipient would see it.
                    Directory.CreateDirectory(pickupDirectory);
                    client.DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory;
                    client.PickupDirectoryLocation = Path.GetFullPath(pickupDirectory);
                }
                else
                {
                    client.DeliveryMethod = SmtpDeliveryMethod.Network;
                    client.Host = _configuration["Email:SmtpHost"]!;
                    client.Port = _configuration.GetValue("Email:SmtpPort", 25);
                    client.EnableSsl = _configuration.GetValue("Email:UseSsl", false);
                    client.Timeout = _configuration.GetValue("Email:TimeoutSeconds", 30) * 1000;

                    var userName = _configuration["Email:Username"];
                    if (!string.IsNullOrWhiteSpace(userName))
                    {
                        client.UseDefaultCredentials = false;
                        client.Credentials = new NetworkCredential(userName, _configuration["Email:Password"]);
                    }
                    else
                    {
                        // Truly anonymous. UseDefaultCredentials = true would make
                        // SmtpClient attempt NTLM as the process identity, which a
                        // relay that offers no AUTH rejects - turning a connection
                        // that should have worked into "Failure sending mail".
                        client.UseDefaultCredentials = false;
                        client.Credentials = null;
                    }
                }

                await client.SendMailAsync(message);

                if (!string.IsNullOrWhiteSpace(pickupDirectory))
                {
                    var location = Path.GetFullPath(pickupDirectory);
                    _logger.LogInformation(
                        "Notification for {Count} recipient(s) written to the pickup directory {Directory}; no mail server was contacted.",
                        validRecipients.Count, location);
                    return (true, $"Email:PickupDirectory is set, so nothing was sent. The notification for {validRecipients.Count} recipient(s) was written to {location} as a .eml file.");
                }

                return (true, redirected
                    ? $"Email:RecipientOverride is set, so the notification went to {string.Join(", ", validRecipients)} instead of the {intendedRecipients.Count} intended recipient(s)."
                    : $"Notification sent to {validRecipients.Count} recipient(s).");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send notification email to {Recipients}.", string.Join(", ", validRecipients));
                return (false, $"Notification email could not be sent ({Describe(ex)}). The request itself was saved.");
            }
        }

        // SmtpException says "Failure sending mail." and puts the reason that
        // actually matters - the socket error, the relay's refusal - in
        // InnerException. Walk the chain so the page shows something a person can
        // act on instead of the wrapper.
        private static string Describe(Exception exception)
        {
            var parts = new List<string>();

            for (Exception? ex = exception; ex is not null; ex = ex.InnerException)
            {
                var text = ex.Message.Trim();

                if (ex is SmtpFailedRecipientsException failed)
                {
                    var rejected = failed.InnerExceptions
                        .Select(inner => inner.FailedRecipient)
                        .Where(address => !string.IsNullOrWhiteSpace(address));
                    text = $"{text} rejected: {string.Join(", ", rejected)}";
                }
                else if (ex is SmtpException smtp && smtp.StatusCode != SmtpStatusCode.GeneralFailure)
                {
                    text = $"{text} [{smtp.StatusCode}]";
                }

                if (!parts.Contains(text, StringComparer.OrdinalIgnoreCase))
                {
                    parts.Add(text);
                }
            }

            return string.Join(" -> ", parts);
        }
    }
}
