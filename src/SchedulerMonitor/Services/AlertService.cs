using SchedulerMonitor.Infrastructure;
using SchedulerMonitor.Models;

namespace SchedulerMonitor.Services;

/// <summary>
/// Sends one email per task that Task Scheduler reported as long running, using the configured
/// subject and body templates. Alerts are independent of the daily report: they go out as soon as a
/// check finds the overlap, and repeat only after the cooldown.
/// </summary>
public sealed class AlertService
{
    private readonly EmailService _email;
    private readonly AlertStateStore _state;
    private readonly FileLogger _logger;

    public AlertService(EmailService email, AlertStateStore state, FileLogger logger)
    {
        _email = email;
        _state = state;
        _logger = logger;
    }

    public async Task<int> SendAsync(AppConfig config, MonitorRunResult run,
        CancellationToken cancellationToken = default)
    {
        var alerts = config.Alerts;
        var candidates = new List<(TaskMonitorResult Task, string Kind, string Subject, string Body)>();

        if (alerts.Enabled)
        {
            candidates.AddRange(run.Tasks.Where(task => task.Status == MonitorStatus.LongRunning)
                .Select(task => (task, "long running", alerts.SubjectTemplate, alerts.BodyTemplate)));
        }
        if (alerts.AbnormalEnabled)
        {
            candidates.AddRange(run.Tasks.Where(task => task.Status == MonitorStatus.Abnormal)
                .Select(task => (task, "abnormal", alerts.AbnormalSubjectTemplate, alerts.AbnormalBodyTemplate)));
        }
        if (candidates.Count == 0) return 0;

        var email = RecipientsFor(config);
        if (email.Recipients.Count == 0)
        {
            _logger.Warn("Alert skipped: no recipients are configured");
            return 0;
        }

        var sent = 0;
        foreach (var (task, kind, subjectTemplate, bodyTemplate) in candidates)
        {
            var key = AlertStateStore.Key(task.Host, task.TaskPath, kind);
            if (!_state.ShouldAlert(key, task.LastRunTime, alerts.CooldownMinutes))
            {
                _logger.Info($"{kind} alert for {task.TaskPath} suppressed by the {alerts.CooldownMinutes} minute cooldown");
                continue;
            }

            var subject = AlertTemplate.Render(subjectTemplate, task);
            var body = AlertTemplate.Render(bodyTemplate, task);
            try
            {
                await _email.SendAsync(email, subject, AlertTemplate.ToHtml(body), cancellationToken);
                _state.Record(key, task.LastRunTime, task.AbnormalEventId ?? task.LongRunningEventId);
                sent++;
                _logger.Info($"{kind} alert sent for {task.TaskPath}");
            }
            catch (Exception ex)
            {
                // One unreachable mailbox must not stop the remaining alerts.
                _logger.Error($"Unable to send the {kind} alert for {task.TaskPath}", ex);
            }
        }

        _state.Save();
        return sent;
    }

    /// <summary>Sends the templates filled with example values, so the wording can be checked.</summary>
    public async Task SendPreviewAsync(AppConfig config, CancellationToken cancellationToken = default)
    {
        var sample = AlertTemplate.Sample(config);
        var subject = AlertTemplate.Render(config.Alerts.SubjectTemplate, sample);
        var body = AlertTemplate.Render(config.Alerts.BodyTemplate, sample);
        await _email.SendAsync(RecipientsFor(config), subject, AlertTemplate.ToHtml(body), cancellationToken);
    }

    /// <summary>Alert recipients fall back to the report recipients when none are configured.</summary>
    private static EmailConfig RecipientsFor(AppConfig config)
    {
        if (config.Alerts.Recipients.Count == 0) return config.Email;
        return new EmailConfig
        {
            Enabled = config.Email.Enabled, SmtpServer = config.Email.SmtpServer, Port = config.Email.Port,
            EnableTls = config.Email.EnableTls, Username = config.Email.Username,
            EncryptedPassword = config.Email.EncryptedPassword, Sender = config.Email.Sender,
            Recipients = config.Alerts.Recipients
        };
    }
}
