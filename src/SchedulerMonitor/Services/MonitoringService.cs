using SchedulerMonitor.Infrastructure;
using SchedulerMonitor.Models;

namespace SchedulerMonitor.Services;

public sealed class MonitoringService
{
    private readonly RemoteTaskQuery _query;
    private readonly FileLogger _logger;

    public MonitoringService(RemoteTaskQuery query, FileLogger logger)
    {
        _query = query;
        _logger = logger;
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

                    var item = Classify(server, task);
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

    internal static TaskMonitorResult Classify(ServerConfig server, DiscoveredTask task)
    {
        var state = task.WindowsState.Trim();
        MonitorStatus status;
        string detail;

        if (!task.Enabled || state.Contains("Disabled", StringComparison.OrdinalIgnoreCase))
        {
            status = MonitorStatus.Disabled;
            detail = "Task is disabled";
        }
        else if (state.Contains("Running", StringComparison.OrdinalIgnoreCase))
        {
            status = MonitorStatus.Running;
            detail = task.LastRunTime is null ? "Task is running" : $"Running since {task.LastRunTime:g}";
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
            CheckedAt = DateTime.Now
        };
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
