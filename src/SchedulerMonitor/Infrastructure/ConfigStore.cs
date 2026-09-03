using System.Text.Json;
using System.Text.Json.Serialization;
using SchedulerMonitor.Models;

namespace SchedulerMonitor.Infrastructure;

public sealed class ConfigStore
{
    private readonly AppPaths _paths;
    private readonly FileLogger _logger;
    private readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public ConfigStore(AppPaths paths, FileLogger logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public AppConfig Load()
    {
        try
        {
            if (!File.Exists(_paths.ConfigFile))
            {
                var initial = new AppConfig();
                Save(initial);
                return initial;
            }

            var json = File.ReadAllText(_paths.ConfigFile);
            return JsonSerializer.Deserialize<AppConfig>(json, _options) ?? new AppConfig();
        }
        catch (Exception ex)
        {
            _logger.Error("Unable to load config.json", ex);
            throw new InvalidOperationException("config.json could not be loaded. Check the JSON format and file permissions.", ex);
        }
    }

    public void Save(AppConfig config)
    {
        var json = JsonSerializer.Serialize(config, _options);
        var temp = _paths.ConfigFile + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, _paths.ConfigFile, true);
        _logger.Info("Configuration saved");
    }
}
