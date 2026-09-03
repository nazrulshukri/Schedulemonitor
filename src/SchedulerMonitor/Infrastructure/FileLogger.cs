namespace SchedulerMonitor.Infrastructure;

public sealed class FileLogger
{
    private readonly AppPaths _paths;
    private readonly object _sync = new();

    public FileLogger(AppPaths paths) => _paths = paths;

    public void Info(string message) => Write("INFO", message, null);
    public void Warn(string message) => Write("WARN", message, null);
    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private void Write(string level, string message, Exception? exception)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level,-5} {message}";
        if (exception is not null)
            line += $" | {exception.GetType().Name}: {exception.Message}";

        lock (_sync)
        {
            try
            {
                var path = Path.Combine(_paths.LogDirectory, $"{DateTime.Now:yyyy-MM-dd}.log");
                File.AppendAllText(path, line + Environment.NewLine);
            }
            catch
            {
                // Logging must never stop monitoring.
            }
        }
    }

    public void ApplyRetention(int logDays, int reportDays)
    {
        if (logDays > 0)
            DeleteOlderThan(_paths.LogDirectory, "*.log", DateTime.Now.AddDays(-logDays));
        if (reportDays > 0)
            DeleteOlderThan(_paths.ReportDirectory, "*.html", DateTime.Now.AddDays(-reportDays));
    }

    private void DeleteOlderThan(string directory, string pattern, DateTime cutoff)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, pattern))
            {
                if (File.GetLastWriteTime(file) < cutoff)
                    File.Delete(file);
            }
        }
        catch (Exception ex)
        {
            Error($"Unable to apply retention in {directory}", ex);
        }
    }
}
