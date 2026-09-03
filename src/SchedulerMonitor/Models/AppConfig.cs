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
    public int LogRetentionDays { get; set; } = 30;
    public int ReportRetentionDays { get; set; } = 30;
}
