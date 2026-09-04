using System.Globalization;
using System.Net;
using System.Text;
using SchedulerMonitor.Models;

namespace SchedulerMonitor.Services;

/// <summary>
/// Fills {Placeholder} names in the alert subject and body. Unknown placeholders are left as they
/// are so a typo is visible in the email instead of silently deleting text.
/// </summary>
public static class AlertTemplate
{
    /// <summary>Every placeholder the templates accept, shown to the user in Configuration.</summary>
    public static readonly string[] Placeholders =
    [
        "{JobName}", "{TaskPath}", "{Server}", "{Host}", "{Status}", "{Interval}", "{StartTime}",
        "{ElapsedMinutes}", "{Elapsed}", "{EventId}", "{EventTime}", "{Events}", "{WindowsState}",
        "{LastResult}", "{NextRun}", "{Detail}", "{Now}"
    ];

    public static string Render(string template, TaskMonitorResult task)
    {
        if (string.IsNullOrWhiteSpace(template)) return "";

        var elapsed = task.RunningFor ?? (task.LastRunTime is { } started ? DateTime.Now - started : null);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["JobName"] = task.DisplayName,
            ["TaskPath"] = task.TaskPath,
            ["Server"] = task.ServerName,
            ["Host"] = task.Host,
            ["Status"] = task.StatusText,
            ["Interval"] = task.RepeatInterval is { } interval ? DescribeInterval(interval) : "its schedule",
            ["StartTime"] = task.LastRunTime?.ToString("MM/dd/yyyy HH:mm:ss", CultureInfo.InvariantCulture) ?? "-",
            ["ElapsedMinutes"] = elapsed is { } value
                ? value.TotalMinutes.ToString("0.0", CultureInfo.InvariantCulture) : "-",
            ["Elapsed"] = elapsed is { } span ? MonitoringService.Describe(span) : "-",
            ["EventId"] = (task.AbnormalEventId ?? task.LongRunningEventId)?.ToString(CultureInfo.InvariantCulture) ?? "-",
            ["EventTime"] = (task.AbnormalEventTime ?? task.LongRunningEventTime)?
                .ToString("MM/dd/yyyy HH:mm:ss", CultureInfo.InvariantCulture) ?? "-",
            ["Events"] = task.EventSummary,
            ["WindowsState"] = task.WindowsState,
            ["LastResult"] = string.IsNullOrWhiteSpace(task.LastResult) ? "-" : task.LastResult,
            ["NextRun"] = task.NextRunTime?.ToString("MM/dd/yyyy HH:mm:ss", CultureInfo.InvariantCulture) ?? "-",
            ["Detail"] = task.Detail,
            ["Now"] = DateTime.Now.ToString("MM/dd/yyyy HH:mm:ss", CultureInfo.InvariantCulture)
        };

        var result = new StringBuilder(template);
        foreach (var pair in values)
            result.Replace("{" + pair.Key + "}", pair.Value);
        return result.ToString();
    }

    /// <summary>Wraps the rendered plain-text body in minimal HTML, keeping the line breaks.</summary>
    public static string ToHtml(string body)
    {
        var encoded = WebUtility.HtmlEncode(body).Replace("\r\n", "\n").Replace("\n", "<br>");
        return "<html><body style=\"font-family:Segoe UI,Arial,sans-serif;font-size:14px;color:#263238\">"
               + encoded + "</body></html>";
    }

    private static string DescribeInterval(TimeSpan value)
    {
        if (value.TotalMinutes < 1) return $"{(int)value.TotalSeconds} seconds";
        if (value.TotalMinutes < 60) return $"{(int)value.TotalMinutes} minutes";
        if (value.TotalHours < 24) return $"{(int)value.TotalHours} hours";
        return $"{(int)value.TotalDays} days";
    }

    /// <summary>
    /// Example result for the template preview. It is built from the first task actually selected
    /// for monitoring, so the preview shows the real job name and server rather than an invented
    /// one; only the timings are simulated. A configuration with no selection falls back to a
    /// neutral placeholder job.
    /// </summary>
    public static TaskMonitorResult Sample(AppConfig? config = null,
        MonitorStatus status = MonitorStatus.LongRunning)
    {
        var selected = config?.MonitoredTasks.FirstOrDefault(task => task.Enabled);
        var server = selected is null
            ? null
            : config?.Servers.FirstOrDefault(item => item.Id == selected.ServerId);

        var name = string.IsNullOrWhiteSpace(selected?.DisplayName)
            ? LastSegment(selected?.TaskPath) ?? "Example Task"
            : selected!.DisplayName;

        return new TaskMonitorResult
        {
            ServerName = server?.ToString() ?? "Example Server",
            Host = server?.Host ?? Environment.MachineName,
            TaskPath = selected?.TaskPath ?? @"\Example Task",
            DisplayName = name,
            WindowsState = status == MonitorStatus.Abnormal ? "Ready" : "Running", Status = status,
            LastRunTime = DateTime.Now.AddMinutes(-103.6), LastResult = "267009",
            NextRunTime = DateTime.Now.AddMinutes(2), RepeatInterval = TimeSpan.FromMinutes(2),
            RunningFor = TimeSpan.FromMinutes(103.6),
            LongRunningEventId = status == MonitorStatus.LongRunning ? 322 : null,
            LongRunningEventTime = status == MonitorStatus.LongRunning ? DateTime.Now.AddMinutes(-101.6) : null,
            AbnormalEventId = status == MonitorStatus.Abnormal ? 103 : null,
            AbnormalEventTime = status == MonitorStatus.Abnormal ? DateTime.Now.AddMinutes(-101.6) : null,
            EventSummary = status == MonitorStatus.Abnormal
                ? "103 - action start failed"
                : "322 - start skipped, already running",
            Detail = status == MonitorStatus.Abnormal
                ? "Windows event 103: action start failed"
                : "Windows event 322: a scheduled start was skipped because this run is still going"
        };
    }

    private static string? LastSegment(string? taskPath) =>
        string.IsNullOrWhiteSpace(taskPath) ? null : taskPath.TrimEnd('\\').Split('\\').LastOrDefault();
}
