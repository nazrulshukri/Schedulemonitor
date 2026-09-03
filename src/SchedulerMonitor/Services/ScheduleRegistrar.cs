using System.Globalization;

namespace SchedulerMonitor.Services;

public sealed class ScheduleRegistrar
{
    public const string TaskName = "SchedulerMonitor-Daily";

    public async Task RegisterAsync(string executablePath, TimeSpan runTime, int timeoutSeconds = 30)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Scheduling requires Windows.");
        var action = $"\"{executablePath}\" --run --silent";
        var arguments = new[]
        {
            "/Create", "/TN", TaskName, "/SC", "DAILY", "/ST",
            runTime.ToString(@"hh\:mm", CultureInfo.InvariantCulture),
            "/TR", action, "/RL", "HIGHEST", "/F"
        };
        var result = await ProcessRunner.RunAsync("schtasks.exe", arguments, timeoutSeconds);
        if (result.ExitCode != 0) throw new InvalidOperationException(CleanError(result));
    }

    public async Task RemoveAsync(int timeoutSeconds = 30)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Scheduling requires Windows.");
        var result = await ProcessRunner.RunAsync("schtasks.exe", ["/Delete", "/TN", TaskName, "/F"], timeoutSeconds);
        if (result.ExitCode != 0) throw new InvalidOperationException(CleanError(result));
    }

    public async Task<bool> ExistsAsync(int timeoutSeconds = 15)
    {
        if (!OperatingSystem.IsWindows()) return false;
        var result = await ProcessRunner.RunAsync("schtasks.exe", ["/Query", "/TN", TaskName], timeoutSeconds);
        return result.ExitCode == 0;
    }

    private static string CleanError(ProcessResult result) =>
        (string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError).Trim();
}
