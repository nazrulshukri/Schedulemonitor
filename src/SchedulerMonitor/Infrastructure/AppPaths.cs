namespace SchedulerMonitor.Infrastructure;

public sealed class AppPaths
{
    public AppPaths(string baseDirectory)
    {
        BaseDirectory = Path.GetFullPath(baseDirectory);
        ConfigFile = Path.Combine(BaseDirectory, "config.json");
        LogDirectory = Path.Combine(BaseDirectory, "Logs");
        ReportDirectory = Path.Combine(BaseDirectory, "Reports");

        Directory.CreateDirectory(LogDirectory);
        Directory.CreateDirectory(ReportDirectory);
    }

    public string BaseDirectory { get; }
    public string ConfigFile { get; }
    public string LogDirectory { get; }
    public string ReportDirectory { get; }
}
