using System.Net;
using System.Net.Mail;
using SchedulerMonitor.Infrastructure;
using SchedulerMonitor.Models;

namespace SchedulerMonitor.Services;

public sealed class EmailService
{
    private readonly FileLogger _logger;

    public EmailService(FileLogger logger) => _logger = logger;

    public async Task SendAsync(EmailConfig config, string subject, string html,
        CancellationToken cancellationToken = default)
    {
        Validate(config);
        using var message = new MailMessage
        {
            From = new MailAddress(config.Sender), Subject = subject,
            Body = html, IsBodyHtml = true
        };
        foreach (var recipient in config.Recipients.Where(value => !string.IsNullOrWhiteSpace(value)))
            message.To.Add(recipient.Trim());

        using var client = new SmtpClient(config.SmtpServer, config.Port)
        {
            EnableSsl = config.EnableTls,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Timeout = 30000
        };
        if (!string.IsNullOrWhiteSpace(config.Username))
            client.Credentials = new NetworkCredential(config.Username,
                DpapiProtector.Unprotect(config.EncryptedPassword));

        cancellationToken.ThrowIfCancellationRequested();
        await client.SendMailAsync(message, cancellationToken);
        _logger.Info($"Email sent to {message.To.Count} recipient(s)");
    }

    public async Task SendTestAsync(EmailConfig config, CancellationToken cancellationToken = default)
    {
        var html = $"<h2>Scheduler Monitor Test Email</h2><p>SMTP configuration is working.</p><p>{DateTime.Now:F}</p>";
        await SendAsync(config, "[Scheduler Monitor] Test Email", html, cancellationToken);
    }

    private static void Validate(EmailConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.SmtpServer)) throw new InvalidOperationException("SMTP server is required.");
        if (config.Port is < 1 or > 65535) throw new InvalidOperationException("SMTP port is invalid.");
        if (string.IsNullOrWhiteSpace(config.Sender)) throw new InvalidOperationException("Sender email is required.");
        if (config.Recipients.Count == 0) throw new InvalidOperationException("At least one recipient is required.");
    }
}
