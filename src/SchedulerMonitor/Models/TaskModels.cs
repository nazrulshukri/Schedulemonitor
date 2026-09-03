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

    /// <summary>Repetition interval declared in Task Scheduler ("Repeat: Every"), when the task has one.</summary>
    public TimeSpan? RepeatInterval { get; init; }
}

public enum MonitorStatus
{
    Success,
    Running,
    LongRunning,
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

    /// <summary>How long the task has been running, when Task Scheduler reports it as running.</summary>
    public TimeSpan? RunningFor { get; init; }

    /// <summary>Runtime the task is expected to stay below before it counts as long running.</summary>
    public TimeSpan? LongRunningThreshold { get; init; }

    /// <summary>The Task Scheduler event that flagged this task, when the event log was the source.</summary>
    public int? LongRunningEventId { get; init; }

    /// <summary>When that event was logged.</summary>
    public DateTime? LongRunningEventTime { get; init; }

    /// <summary>Repetition interval declared in Task Scheduler, used by the alert template.</summary>
    public TimeSpan? RepeatInterval { get; init; }

    /// <summary>Short text for the Events column: the last relevant Task Scheduler event.</summary>
    public string EventSummary { get; init; } = "";

    public string StatusText => Status == MonitorStatus.LongRunning
        ? "LONG RUNNING"
        : Status.ToString().ToUpperInvariant();

    /// <summary>Single-word form of the status, used as an HTML class name in the report.</summary>
    public string StatusCode => Status.ToString().ToUpperInvariant();
}

public sealed class MonitorRunResult
{
    public DateTime StartedAt { get; init; }
    public DateTime CompletedAt { get; set; }
    public List<TaskMonitorResult> Tasks { get; } = [];
    public int ServerCount { get; set; }
}
