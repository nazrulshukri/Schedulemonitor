using System.Globalization;
using System.Text.RegularExpressions;
using SchedulerMonitor.Infrastructure;
using SchedulerMonitor.Models;

namespace SchedulerMonitor.Services;

public sealed class RemoteTaskQuery
{
    private readonly FileLogger _logger;

    public RemoteTaskQuery(FileLogger logger) => _logger = logger;

    public async Task<IReadOnlyList<DiscoveredTask>> ScanAsync(string host, int timeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Task scanning requires Windows.");

        var arguments = new List<string> { "/Query" };
        if (!IsLocalHost(host))
        {
            arguments.Add("/S");
            arguments.Add(host);
        }
        arguments.AddRange(["/FO", "CSV", "/V"]);

        _logger.Info($"Scanning scheduled tasks on {host}");
        var result = await ProcessRunner.RunAsync("schtasks.exe", arguments, timeoutSeconds, cancellationToken);
        if (result.ExitCode != 0)
        {
            var detail = FirstUsefulLine(result.StandardError, result.StandardOutput);
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(detail)
                ? $"Unable to query {host}. Exit code: {result.ExitCode}."
                : detail);
        }

        return ParseCsv(result.StandardOutput);
    }

    internal static IReadOnlyList<DiscoveredTask> ParseCsv(string csv)
    {
        var rows = CsvParser.Parse(csv);
        if (rows.Count < 2) return [];

        var headers = rows[0]
            .Select((value, index) => new { Name = Normalize(value), Index = index })
            .GroupBy(item => item.Name)
            .ToDictionary(group => group.Key, group => group.First().Index);

        var tasks = new List<DiscoveredTask>();
        foreach (var row in rows.Skip(1))
        {
            var taskPath = Value(row, headers, "taskname");
            if (string.IsNullOrWhiteSpace(taskPath)) continue;

            var state = FirstValue(row, headers, "status", "scheduledtaskstate");
            var enabledText = FirstValue(row, headers, "scheduledtaskenabled", "enabled");
            var enabled = !state.Contains("Disabled", StringComparison.OrdinalIgnoreCase)
                          && !enabledText.Equals("No", StringComparison.OrdinalIgnoreCase)
                          && !enabledText.Equals("False", StringComparison.OrdinalIgnoreCase);

            tasks.Add(new DiscoveredTask
            {
                TaskPath = EnsureLeadingSlash(taskPath.Trim()),
                DisplayName = taskPath.Trim().TrimEnd('\\').Split('\\').LastOrDefault() ?? taskPath.Trim(),
                WindowsState = string.IsNullOrWhiteSpace(state) ? "Unknown" : state.Trim(),
                Enabled = enabled,
                LastRunTime = ParseDate(FirstValue(row, headers, "lastruntime")),
                LastResult = FirstValue(row, headers, "lastresult").Trim(),
                NextRunTime = ParseDate(FirstValue(row, headers, "nextruntime"))
            });
        }

        return tasks
            .GroupBy(task => task.TaskPath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(task => task.TaskPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string FirstValue(string[] row, IReadOnlyDictionary<string, int> headers,
        params string[] names)
    {
        foreach (var name in names)
        {
            var value = Value(row, headers, name);
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return "";
    }

    private static string Value(string[] row, IReadOnlyDictionary<string, int> headers, string header)
    {
        return headers.TryGetValue(header, out var index) && index < row.Length ? row[index] : "";
    }

    private static string Normalize(string value) =>
        Regex.Replace(value.Trim().ToLowerInvariant(), "[^a-z0-9]", "");

    private static DateTime? ParseDate(string value)
    {
        value = value.Trim();
        if (string.IsNullOrWhiteSpace(value)
            || value.Equals("N/A", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Never", StringComparison.OrdinalIgnoreCase)) return null;

        if (DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out var current))
            return current;
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var invariant))
            return invariant;
        return null;
    }

    private static string EnsureLeadingSlash(string value) => value.StartsWith('\\') ? value : "\\" + value;

    private static bool IsLocalHost(string host) =>
        string.IsNullOrWhiteSpace(host)
        || host.Equals(".", StringComparison.OrdinalIgnoreCase)
        || host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || host.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase);

    private static string FirstUsefulLine(params string[] values) =>
        values.SelectMany(value => value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            .Select(value => value.Trim()).FirstOrDefault(value => value.Length > 0) ?? "";
}
