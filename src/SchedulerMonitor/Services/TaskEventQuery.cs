using System.Globalization;
using System.Xml.Linq;
using SchedulerMonitor.Infrastructure;

namespace SchedulerMonitor.Services;

/// <summary>One Task Scheduler operational event that matters for monitoring.</summary>
public sealed record TaskEvent(string TaskPath, int EventId, DateTime TimeCreated);

/// <summary>
/// Reads Microsoft-Windows-TaskScheduler/Operational through wevtutil.exe.
/// Event 322 ("launch request ignored, instance already running") is the scheduler's own proof
/// that a run overlapped its next start, which is the strongest long running signal available.
/// </summary>
public sealed class TaskEventQuery
{
    private const string Channel = "Microsoft-Windows-TaskScheduler/Operational";

    private readonly FileLogger _logger;

    public TaskEventQuery(FileLogger logger) => _logger = logger;

    public async Task<IReadOnlyList<TaskEvent>> QueryAsync(string host, IReadOnlyCollection<int> eventIds,
        int lookbackMinutes, int timeoutSeconds, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Event log reading requires Windows.");
        if (eventIds.Count == 0 || lookbackMinutes <= 0) return [];

        var idFilter = string.Join(" or ", eventIds.Select(id => $"EventID={id}"));
        var window = (long)TimeSpan.FromMinutes(lookbackMinutes).TotalMilliseconds;
        var arguments = new List<string>
        {
            "qe", Channel,
            $"/q:*[System[({idFilter}) and TimeCreated[timediff(@SystemTime)<={window}]]]",
            "/f:XML", "/e:Events", "/rd:true", "/c:500"
        };
        // No credentials are passed: the remote read uses the Windows identity running the tool,
        // exactly like the schtasks query does.
        if (!IsLocalHost(host)) arguments.Add($"/r:{host}");

        _logger.Info($"Reading Task Scheduler events on {host}");
        var result = await ProcessRunner.RunAsync("wevtutil.exe", arguments, timeoutSeconds, cancellationToken);
        if (result.ExitCode != 0)
        {
            var detail = result.StandardError.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0) ?? $"exit code {result.ExitCode}";
            throw new InvalidOperationException($"Unable to read the Task Scheduler event log on {host}: {detail}");
        }

        return ParseEvents(result.StandardOutput);
    }

    internal static IReadOnlyList<TaskEvent> ParseEvents(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return [];

        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException)
        {
            return [];
        }

        var events = new List<TaskEvent>();
        foreach (var element in document.Descendants().Where(node => node.Name.LocalName == "Event"))
        {
            var system = element.Elements().FirstOrDefault(node => node.Name.LocalName == "System");
            if (system is null) continue;

            var idText = system.Elements().FirstOrDefault(node => node.Name.LocalName == "EventID")?.Value;
            if (!int.TryParse(idText, out var eventId)) continue;

            var timeText = system.Elements().FirstOrDefault(node => node.Name.LocalName == "TimeCreated")
                ?.Attribute("SystemTime")?.Value;
            if (!DateTime.TryParse(timeText, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var utc)) continue;

            var taskPath = element.Descendants().FirstOrDefault(node =>
                    node.Name.LocalName == "Data"
                    && string.Equals(node.Attribute("Name")?.Value, "TaskName", StringComparison.OrdinalIgnoreCase))
                ?.Value.Trim();
            if (string.IsNullOrWhiteSpace(taskPath)) continue;

            events.Add(new TaskEvent(EnsureLeadingSlash(taskPath), eventId, utc.ToLocalTime()));
        }

        return events;
    }

    /// <summary>
    /// Short label for the Events column. The ID stays in the text because it is the authoritative
    /// value to look up in Event Viewer; the wording is only a reminder of what it means.
    /// </summary>
    public static string Describe(int eventId) => eventId switch
    {
        100 => "100 - task started",
        101 => "101 - task start failed",
        102 => "102 - task completed",
        103 => "103 - action start failed",
        111 => "111 - task terminated",
        200 => "200 - action started",
        201 => "201 - action completed",
        202 => "202 - action failed",
        203 => "203 - action failed to start",
        322 => "322 - start skipped, already running",
        324 => "324 - start blocked, already running",
        329 => "329 - terminated, time limit exceeded",
        _ => $"{eventId} - Task Scheduler event"
    };

    private static string EnsureLeadingSlash(string value) => value.StartsWith('\\') ? value : "\\" + value;

    private static bool IsLocalHost(string host) =>
        string.IsNullOrWhiteSpace(host)
        || host.Equals(".", StringComparison.OrdinalIgnoreCase)
        || host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || host.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase);
}
