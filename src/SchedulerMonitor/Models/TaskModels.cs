namespace SchedulerMonitor.Models;

public sealed class DiscoveredTask
{
    public string TaskPath { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string WindowsState { get; init; } = "Unknown";
    public bool Enabled { get; init; } = true;
    public DateTime? LastRunTime { get; init; }
    public string LastResult { get; init; } = "";
    public DateTime? NextRunTime { get; init; }
}

public enum MonitorStatus
{
    Success,
    Running,
    Pending,
    Failed,
    Overdue,
    Disabled,
    Unreachable
}

public sealed class TaskMonitorResult
{
    public string ServerName { get; init; } = "";
    public string Host { get; init; } = "";
    public string TaskPath { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string WindowsState { get; init; } = "";
    public MonitorStatus Status { get; init; }
    public DateTime? LastRunTime { get; init; }
    public string LastResult { get; init; } = "";
    public DateTime? NextRunTime { get; init; }
    public DateTime CheckedAt { get; init; } = DateTime.Now;
    public string Detail { get; init; } = "";

    public string StatusText => Status.ToString().ToUpperInvariant();
}

public sealed class MonitorRunResult
{
    public DateTime StartedAt { get; init; }
    public DateTime CompletedAt { get; set; }
    public List<TaskMonitorResult> Tasks { get; } = [];
    public int ServerCount { get; set; }
}
