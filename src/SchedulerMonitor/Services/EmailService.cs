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
            From = ParseAddress(config.Sender, "Sender email"), Subject = subject,
            Body = html, IsBodyHtml = true
        };
        foreach (var recipient in config.Recipients.Where(value => !string.IsNullOrWhiteSpace(value)))
            message.To.Add(ParseAddress(recipient, "Recipient"));

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
        try
        {
            await client.SendMailAsync(message, cancellationToken);
        }
        catch (SmtpException ex)
        {
            var detail = Explain(ex, config);
            _logger.Error($"SMTP rejected the message: {detail}", ex);
            throw new InvalidOperationException(detail, ex);
        }
        _logger.Info($"Email sent to {message.To.Count} recipient(s)");
    }

    public async Task SendTestAsync(EmailConfig config, CancellationToken cancellationToken = default)
    {
        var html = $"<h2>Scheduler Monitor Test Email</h2><p>SMTP configuration is working.</p><p>{DateTime.Now:F}</p>";
        await SendAsync(config, "[Scheduler Monitor] Test Email", html, cancellationToken);
    }

    /// <summary>
    /// Cleans one configured address before it reaches the SMTP conversation. Addresses pasted from
    /// Outlook or a browser often carry a non-breaking space or a zero-width character, and a
    /// trailing separator is easy to leave behind; the server answers those with a bare syntax
    /// error, so they are removed here and anything still invalid is named to the user.
    /// </summary>
    internal static MailAddress ParseAddress(string value, string field)
    {
        var cleaned = Clean(value);
        if (cleaned.Length == 0)
            throw new InvalidOperationException($"{field} is empty.");

        // Accept the "Display Name <someone@example.com>" form by taking the address inside <>.
        var open = cleaned.LastIndexOf('<');
        var close = cleaned.LastIndexOf('>');
        if (open >= 0 && close > open) cleaned = cleaned[(open + 1)..close].Trim();

        try
        {
            var address = new MailAddress(cleaned);
            if (!address.Address.Contains('@') || address.Address.Any(char.IsWhiteSpace))
                throw new FormatException();
            return address;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            throw new InvalidOperationException(
                $"{field} \"{value.Trim()}\" is not a valid email address. Use one plain address per line, "
                + "for example name@company.com, with no display name, quotes, comma or semicolon.");
        }
    }

    private static string Clean(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var ch in value)
        {
            // Zero-width and byte-order characters survive copy and paste but break the SMTP command.
            if (ch is '\u200b' or '\u200c' or '\u200d' or '\ufeff') continue;
            builder.Append(ch is '\u00a0' ? ' ' : ch);
        }
        return builder.ToString().Trim().Trim(',', ';').Trim();
    }

    /// <summary>Turns an SMTP failure into something an administrator can act on.</summary>
    private static string Explain(SmtpException exception, EmailConfig config)
    {
        var response = exception.Message.Trim();
        var status = exception.StatusCode;
        var hint = status switch
        {
            SmtpStatusCode.CommandUnrecognized or SmtpStatusCode.SyntaxError or SmtpStatusCode.CommandParameterNotImplemented =>
                $"The server at {config.SmtpServer}:{config.Port} rejected a command. This is usually the wrong "
                + "port or TLS combination: port 25 or 587 with TLS off for a plain relay, port 587 with "
                + "\"Use TLS / SSL\" on for STARTTLS. Port 465 (implicit SSL) is not supported by Windows "
                + "SmtpClient. It can also mean the relay expects no AUTH, so clear the username and password. "
                + "A strict relay may also reject the EHLO greeting when this computer's Windows name "
                + $"({Environment.MachineName}) contains a character it does not accept, such as an underscore.",
            SmtpStatusCode.MustIssueStartTlsFirst =>
                "The server requires TLS. Tick \"Use TLS / SSL\" and use port 587.",
            SmtpStatusCode.ClientNotPermitted or SmtpStatusCode.TransactionFailed =>
                $"The server refused the sender or a recipient. Confirm that {config.Sender} is allowed to relay "
                + "through this server from this machine.",
            SmtpStatusCode.MailboxUnavailable or SmtpStatusCode.MailboxNameNotAllowed =>
                "The server rejected a mailbox address. Check the sender and recipient addresses.",
            SmtpStatusCode.GeneralFailure =>
                $"No usable answer from {config.SmtpServer}:{config.Port}. Check the host name, the port, and "
                + "whether a firewall allows this machine to reach it.",
            _ => $"SMTP status: {status}."
        };

        var server = string.IsNullOrWhiteSpace(response) ? "The server sent no text with the error." : response;
        return $"{server}\r\n\r\n{hint}";
    }

    private static void Validate(EmailConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.SmtpServer)) throw new InvalidOperationException("SMTP server is required.");
        if (config.Port is < 1 or > 65535) throw new InvalidOperationException("SMTP port is invalid.");
        if (string.IsNullOrWhiteSpace(config.Sender)) throw new InvalidOperationException("Sender email is required.");
        if (config.Recipients.Count == 0) throw new InvalidOperationException("At least one recipient is required.");
    }
}
