using SchedulerMonitor.Infrastructure;
using SchedulerMonitor.Models;

namespace SchedulerMonitor.Services;

public sealed class MonitoringService
{
    private readonly RemoteTaskQuery _query;
    private readonly TaskEventQuery _events;
    private readonly FileLogger _logger;

    public MonitoringService(RemoteTaskQuery query, FileLogger logger, TaskEventQuery? events = null)
    {
        _query = query;
        _logger = logger;
        _events = events ?? new TaskEventQuery(logger);
    }

    public async Task<MonitorRunResult> RunAsync(AppConfig config,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var run = new MonitorRunResult { StartedAt = DateTime.Now };
        var enabledServers = config.Servers.Where(server => server.Enabled).ToList();
        run.ServerCount = enabledServers.Count;
        _logger.Info($"Monitoring started for {enabledServers.Count} server(s)");

        foreach (var server in enabledServers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var selected = config.MonitoredTasks
                .Where(task => task.Enabled && task.ServerId == server.Id)
                .ToList();
            if (selected.Count == 0) continue;

            progress?.Report($"Checking {server}...");
            try
            {
                var discovered = await _query.ScanAsync(server.Host, config.Monitoring.TimeoutSeconds,
                    cancellationToken);
                var lookup = discovered.ToDictionary(task => task.TaskPath, StringComparer.OrdinalIgnoreCase);
                var overlapEvents = await ReadEventsAsync(server, config.Monitoring,
                    config.Monitoring.LongRunningEventIds, cancellationToken);
                var statusEvents = await ReadEventsAsync(server, config.Monitoring,
                    config.Monitoring.StatusEventIds, cancellationToken);

                foreach (var selectedTask in selected)
                {
                    if (!lookup.TryGetValue(selectedTask.TaskPath, out var task))
                    {
                        run.Tasks.Add(new TaskMonitorResult
                        {
                            ServerName = server.ToString(), Host = server.Host,
                            TaskPath = selectedTask.TaskPath, DisplayName = selectedTask.DisplayName,
                            Status = MonitorStatus.Failed, Detail = "Task not found",
                            WindowsState = "Not Found"
                        });
                        _logger.Warn($"{server}: {selectedTask.TaskPath} not found");
                        continue;
                    }

                    overlapEvents.TryGetValue(task.TaskPath, out var overlap);
                    statusEvents.TryGetValue(task.TaskPath, out var statusEvent);
                    var item = Classify(server, task, config.Monitoring, selectedTask, overlap, statusEvent);
                    run.Tasks.Add(item);
                    _logger.Info($"{server}: {task.TaskPath} = {item.StatusText} ({item.Detail})");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Unable to query {server.Host}", ex);
                foreach (var task in selected)
                {
                    run.Tasks.Add(new TaskMonitorResult
                    {
                        ServerName = server.ToString(), Host = server.Host,
                        TaskPath = task.TaskPath, DisplayName = task.DisplayName,
                        Status = MonitorStatus.Unreachable, Detail = ex.Message,
                        WindowsState = "Unavailable"
                    });
                }
            }
        }

        run.CompletedAt = DateTime.Now;
        _logger.Info($"Monitoring completed: {run.Tasks.Count} task(s)");
        return run;
    }

    /// <summary>
    /// Reads the overlap events for one server, keeping the newest event per task. A server that
    /// refuses the event log is not a monitoring failure: the elapsed-time rule still applies.
    /// </summary>
    private async Task<Dictionary<string, TaskEvent>> ReadEventsAsync(ServerConfig server,
        MonitoringConfig monitoring, IReadOnlyCollection<int> eventIds, CancellationToken cancellationToken)
    {
        if (!monitoring.UseEventLog || eventIds.Count == 0)
            return new Dictionary<string, TaskEvent>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var events = await _events.QueryAsync(server.Host, eventIds,
                monitoring.EventLookbackMinutes, monitoring.TimeoutSeconds, cancellationToken);
            return events
                .GroupBy(item => item.TaskPath, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.OrderByDescending(item => item.TimeCreated).First(),
                    StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.Warn($"{server}: event log unavailable, using elapsed time only ({ex.Message})");
            return new Dictionary<string, TaskEvent>(StringComparer.OrdinalIgnoreCase);
        }
    }

    internal static TaskMonitorResult Classify(ServerConfig server, DiscoveredTask task,
        MonitoringConfig? monitoring = null, MonitoredTaskConfig? selected = null,
        TaskEvent? overlapEvent = null, TaskEvent? statusEvent = null)
    {
        monitoring ??= new MonitoringConfig();
        var state = task.WindowsState.Trim();
        var threshold = ResolveThreshold(task, monitoring, selected);
        TimeSpan? runningFor = null;
        MonitorStatus status;
        string detail;

        if (!task.Enabled || state.Contains("Disabled", StringComparison.OrdinalIgnoreCase))
        {
            status = MonitorStatus.Disabled;
            detail = "Task is disabled";
        }
        else if (state.Contains("Running", StringComparison.OrdinalIgnoreCase))
        {
            if (task.LastRunTime is not null)
            {
                var elapsed = DateTime.Now - task.LastRunTime.Value;
                if (elapsed > TimeSpan.Zero) runningFor = elapsed;
            }

            // Windows logging a skipped start is proof the current execution outlived its schedule.
            // The event must belong to this execution, not to an earlier one inside the lookback window.
            var currentOverlap = overlapEvent is not null
                                 && (task.LastRunTime is null || overlapEvent.TimeCreated >= task.LastRunTime.Value)
                ? overlapEvent
                : null;

            if (currentOverlap is not null)
            {
                status = MonitorStatus.LongRunning;
                detail = $"Windows event {currentOverlap.EventId} at {currentOverlap.TimeCreated:g}: "
                         + "a scheduled start was skipped because this run is still going";
            }
            else if (monitoring.UseElapsedTimeLimit && runningFor is not null && threshold is not null
                     && runningFor > threshold)
            {
                status = MonitorStatus.LongRunning;
                detail = $"Running for {Describe(runningFor.Value)}, expected to finish within {Describe(threshold.Value)}";
            }
            else
            {
                status = MonitorStatus.Running;
                detail = runningFor is not null
                    ? $"Running for {Describe(runningFor.Value)} since {task.LastRunTime:g}"
                    : task.LastRunTime is null ? "Task is running" : $"Running since {task.LastRunTime:g}";
            }
        }
        else if (state.Contains("Queued", StringComparison.OrdinalIgnoreCase))
        {
            status = MonitorStatus.Pending;
            detail = "Task is queued";
        }
        else if (task.NextRunTime is not null && task.NextRunTime < DateTime.Now.AddMinutes(-5))
        {
            status = MonitorStatus.Overdue;
            detail = $"Next run time passed at {task.NextRunTime:g}";
        }
        else if (task.LastRunTime is null)
        {
            status = MonitorStatus.Pending;
            detail = "Task has not run yet";
        }
        else if (IsSuccessResult(task.LastResult))
        {
            status = MonitorStatus.Success;
            detail = "Last execution succeeded";
        }
        else
        {
            status = MonitorStatus.Failed;
            detail = string.IsNullOrWhiteSpace(task.LastResult)
                ? "Last execution result is unavailable"
                : $"Last result: {task.LastResult}";
        }

        return new TaskMonitorResult
        {
            ServerName = server.ToString(), Host = server.Host,
            TaskPath = task.TaskPath, DisplayName = task.DisplayName,
            WindowsState = state, Status = status,
            LastRunTime = task.LastRunTime, LastResult = task.LastResult,
            NextRunTime = task.NextRunTime, Detail = detail,
            RunningFor = runningFor, LongRunningThreshold = threshold,
            LongRunningEventId = overlapEvent?.EventId, LongRunningEventTime = overlapEvent?.TimeCreated,
            EventSummary = SummariseEvent(overlapEvent, statusEvent),
            CheckedAt = DateTime.Now
        };
    }

    /// <summary>
    /// Text for the Events column: the overlap event when there is one, otherwise the last
    /// informational event Windows logged for the task.
    /// </summary>
    internal static string SummariseEvent(TaskEvent? overlapEvent, TaskEvent? statusEvent)
    {
        var newest = overlapEvent is null ? statusEvent
            : statusEvent is null || overlapEvent.TimeCreated >= statusEvent.TimeCreated ? overlapEvent
            : statusEvent;
        return newest is null ? "" : $"{TaskEventQuery.Describe(newest.EventId)} • {newest.TimeCreated:dd-MMM HH:mm}";
    }

    /// <summary>
    /// Picks the runtime budget for a task: an explicit per-task limit wins, then the repetition
    /// interval declared in Task Scheduler, then the global default.
    /// </summary>
    internal static TimeSpan? ResolveThreshold(DiscoveredTask task, MonitoringConfig monitoring,
        MonitoredTaskConfig? selected)
    {
        if (!monitoring.UseElapsedTimeLimit) return null;

        if (selected is { LongRunningMinutes: > 0 })
            return TimeSpan.FromMinutes(selected.LongRunningMinutes);

        if (monitoring.UseRepeatIntervalAsLimit && task.RepeatInterval is { } interval && interval > TimeSpan.Zero)
            return interval;

        return monitoring.LongRunningMinutes > 0
            ? TimeSpan.FromMinutes(monitoring.LongRunningMinutes)
            : null;
    }

    internal static string Describe(TimeSpan value)
    {
        if (value.TotalMinutes < 1) return $"{Math.Max(1, (int)value.TotalSeconds)}s";
        if (value.TotalHours < 1) return $"{(int)value.TotalMinutes}m";
        if (value.TotalDays < 1) return $"{(int)value.TotalHours}h {value.Minutes}m";
        return $"{(int)value.TotalDays}d {value.Hours}h";
    }

    private static bool IsSuccessResult(string value)
    {
        value = value.Trim();
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (value.Equals("0x0", StringComparison.OrdinalIgnoreCase)) return true;
        if (long.TryParse(value, out var decimalValue)) return decimalValue == 0;
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && long.TryParse(value[2..], System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var hexValue)) return hexValue == 0;
        return false;
    }
}
