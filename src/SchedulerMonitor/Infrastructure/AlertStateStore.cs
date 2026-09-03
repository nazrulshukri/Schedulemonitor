using System.Text.Json;

namespace SchedulerMonitor.Infrastructure;

/// <summary>The last alert sent for one task.</summary>
public sealed class AlertEntry
{
    public DateTime ExecutionStartedAt { get; set; }
    public DateTime LastAlertAt { get; set; }
    public int? EventId { get; set; }
}

/// <summary>
/// Remembers which execution already raised an alert, so a check that runs every few minutes does
/// not send the same long running warning again and again. State lives next to config.json.
/// </summary>
public sealed class AlertStateStore
{
    private readonly string _file;
    private readonly FileLogger _logger;
    private readonly JsonSerializerOptions _options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private Dictionary<string, AlertEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private bool _loaded;
    private bool _dirty;

    public AlertStateStore(AppPaths paths, FileLogger logger)
    {
        _file = Path.Combine(paths.BaseDirectory, "alertstate.json");
        _logger = logger;
    }

    public static string Key(string host, string taskPath) => $"{host}|{taskPath}";

    /// <summary>
    /// True when this task may alert now: a different execution than the one already reported, or
    /// the same one after the cooldown has passed.
    /// </summary>
    public bool ShouldAlert(string key, DateTime? executionStartedAt, int cooldownMinutes)
    {
        Load();
        if (!_entries.TryGetValue(key, out var entry)) return true;
        if (executionStartedAt is null || entry.ExecutionStartedAt != executionStartedAt.Value) return true;
        return cooldownMinutes > 0 && DateTime.Now - entry.LastAlertAt >= TimeSpan.FromMinutes(cooldownMinutes);
    }

    public void Record(string key, DateTime? executionStartedAt, int? eventId)
    {
        Load();
        _entries[key] = new AlertEntry
        {
            ExecutionStartedAt = executionStartedAt ?? DateTime.MinValue,
            LastAlertAt = DateTime.Now,
            EventId = eventId
        };
        _dirty = true;
    }

    public void Save()
    {
        if (!_dirty) return;
        try
        {
            var temp = _file + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_entries, _options));
            File.Move(temp, _file, true);
            _dirty = false;
        }
        catch (Exception ex)
        {
            _logger.Warn($"Unable to save alertstate.json: {ex.Message}");
        }
    }

    private void Load()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (!File.Exists(_file)) return;
            var loaded = JsonSerializer.Deserialize<Dictionary<string, AlertEntry>>(File.ReadAllText(_file), _options);
            if (loaded is not null) _entries = new Dictionary<string, AlertEntry>(loaded, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.Warn($"Unable to read alertstate.json, starting fresh: {ex.Message}");
        }
    }
}
