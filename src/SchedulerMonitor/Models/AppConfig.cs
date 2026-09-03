namespace SchedulerMonitor.Models;

public sealed class AppConfig
{
    public List<ServerConfig> Servers { get; set; } = [];
    public List<MonitoredTaskConfig> MonitoredTasks { get; set; } = [];
    public EmailConfig Email { get; set; } = new();
    public ScheduleConfig Schedule { get; set; } = new();
    public MonitoringConfig Monitoring { get; set; } = new();
}

public sealed class ServerConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public bool Enabled { get; set; } = true;

    public override string ToString() => string.IsNullOrWhiteSpace(Name) ? Host : Name;
}

public sealed class MonitoredTaskConfig
{
    public string ServerId { get; set; } = "";
    public string TaskPath { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Maximum runtime in minutes for this task before it is reported as LONG RUNNING.
    /// Zero means "use the interval declared in Task Scheduler, otherwise the global default".
    /// </summary>
    public int LongRunningMinutes { get; set; }
}

public sealed class EmailConfig
{
    public bool Enabled { get; set; }
    public string SmtpServer { get; set; } = "";
    public int Port { get; set; } = 25;
    public bool EnableTls { get; set; }
    public string Username { get; set; } = "";
    public string EncryptedPassword { get; set; } = "";
    public string Sender { get; set; } = "";
    public List<string> Recipients { get; set; } = [];
}

public sealed class ScheduleConfig
{
    public bool Enabled { get; set; }
    public string RunTime { get; set; } = "07:00";
}

public sealed class MonitoringConfig
{
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Default runtime budget, in minutes, for tasks without their own limit.</summary>
    public int LongRunningMinutes { get; set; } = 5;

    /// <summary>
    /// When true, a task that repeats every N minutes is expected to finish inside that window,
    /// so an execution still running after N minutes is reported as LONG RUNNING.
    /// </summary>
    public bool UseRepeatIntervalAsLimit { get; set; } = true;

    /// <summary>
    /// When true, the monitor also reads Microsoft-Windows-TaskScheduler/Operational and reports a
    /// task as LONG RUNNING when Windows itself logged one of <see cref="LongRunningEventIds"/>.
    /// </summary>
    public bool UseEventLog { get; set; } = true;

    /// <summary>
    /// Event IDs that prove an execution outlived its schedule. 322 is "launch request ignored,
    /// instance already running"; 324 is the same refusal under the "queue" instance policy.
    /// </summary>
    public List<int> LongRunningEventIds { get; set; } = [322, 324, 329];

    /// <summary>
    /// Informational events shown in the Events column so a healthy task also states what Windows
    /// last logged for it, instead of leaving the column empty.
    /// </summary>
    public List<int> StatusEventIds { get; set; } = [101, 102, 103, 111, 201, 202, 203];


    /// <summary>How far back the event log is read, in minutes.</summary>
    public int EventLookbackMinutes { get; set; } = 720;

    public int LogRetentionDays { get; set; } = 30;
    public int ReportRetentionDays { get; set; } = 30;
}
