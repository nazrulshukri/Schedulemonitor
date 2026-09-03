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

    public int LogRetentionDays { get; set; } = 30;
    public int ReportRetentionDays { get; set; } = 30;
}
