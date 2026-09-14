using System.Text;
using Atcbassemblyrecipe.Data;
using Atcbassemblyrecipe.Models;
using Microsoft.Extensions.Caching.Memory;

namespace Atcbassemblyrecipe.Services
{
    // One read of TBLAPPSETTING, ready to be asked for values. Rows found in the
    // table win; anything the table does not carry falls back to
    // AppSettingDefaults, so a half-filled table renders a whole page.
    public sealed class AppSettingsSnapshot
    {
        private readonly IReadOnlyDictionary<string, string> _values;

        internal AppSettingsSnapshot(IReadOnlyDictionary<string, string> values, bool fromDatabase)
        {
            _values = values;
            FromDatabase = fromDatabase;
        }

        // False means the table was missing or unreadable and the built-in
        // defaults are being shown. The start-up log says so once.
        public bool FromDatabase { get; }

        public string this[string module, string key] => Get(module, key);

        public string Get(string module, string key, string? fallback = null)
        {
            if (_values.TryGetValue($"{module}:{key}", out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            return fallback ?? AppSettingDefaults.Get(module, key);
        }

        public bool Has(string module, string key) => !string.IsNullOrWhiteSpace(Get(module, key));

        // The THEME rows as a :root block, written into the page head so the
        // colours and animation timings in the database override the defaults in
        // wwwroot/css/site.css without anyone editing the stylesheet.
        public string ThemeCss(string? backgroundImageUrl = null)
        {
            var css = new StringBuilder(":root{");

            foreach (var pair in _values.Where(pair => pair.Key.StartsWith(SettingModules.Theme + ":", StringComparison.OrdinalIgnoreCase)))
            {
                var name = pair.Key[(SettingModules.Theme.Length + 1)..];
                if (!IsSafeToken(name) || !IsSafeValue(pair.Value)) continue;
                css.Append("--").Append(name).Append(':').Append(pair.Value).Append(';');
            }

            if (!string.IsNullOrWhiteSpace(backgroundImageUrl) && IsSafeValue(backgroundImageUrl))
            {
                css.Append("--app-bg-image:url(\"").Append(backgroundImageUrl).Append("\");");
            }

            return css.Append('}').ToString();
        }

        // These values are written straight into a <style> block, so they are
        // checked here rather than trusted: a stray brace or angle bracket in a
        // settings row must not be able to close the block and inject markup.
        private static bool IsSafeToken(string value) =>
            value.Length > 0 && value.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_');

        private static bool IsSafeValue(string value) =>
            value.Length > 0
            && value.Length <= 200
            && !value.Contains('<')
            && !value.Contains('>')
            && !value.Contains('{')
            && !value.Contains('}')
            && !value.Contains(';')
            && !value.Contains('"')
            && !value.Contains('\'')
            && !value.Contains("</", StringComparison.Ordinal);
    }

    public interface IAppSettings
    {
        Task<AppSettingsSnapshot> GetAsync(CancellationToken cancellationToken = default);
        void Invalidate();
    }

    // Serves the UI settings to the pages and keeps them out of the database on
    // every request: one read is cached for AppSettings:CacheSeconds (default 60),
    // so editing a row shows up on the next reload without a restart, and a busy
    // grid does not re-read the table for every partial.
    public sealed class AppSettingsProvider : IAppSettings
    {
        private const string CacheKey = "app-settings-snapshot";
        private const int DefaultCacheSeconds = 60;

        private readonly IAppSettingRepository _repository;
        private readonly IMemoryCache _cache;
        private readonly IConfiguration _configuration;

        public AppSettingsProvider(IAppSettingRepository repository, IMemoryCache cache,
            IConfiguration configuration)
        {
            _repository = repository;
            _cache = cache;
            _configuration = configuration;
        }

        public async Task<AppSettingsSnapshot> GetAsync(CancellationToken cancellationToken = default)
        {
            if (_cache.TryGetValue<AppSettingsSnapshot>(CacheKey, out var cached) && cached is not null)
            {
                return cached;
            }

            var snapshot = await LoadAsync(cancellationToken);
            var seconds = _configuration.GetValue("AppSettings:CacheSeconds", DefaultCacheSeconds);
            _cache.Set(CacheKey, snapshot, TimeSpan.FromSeconds(Math.Max(1, seconds)));
            return snapshot;
        }

        public void Invalidate() => _cache.Remove(CacheKey);

        private async Task<AppSettingsSnapshot> LoadAsync(CancellationToken cancellationToken)
        {
            var values = new Dictionary<string, string>(AppSettingDefaults.Values, StringComparer.OrdinalIgnoreCase);

            IReadOnlyList<AppSetting> rows;
            try
            {
                rows = await _repository.GetAllAsync(cancellationToken);
            }
            catch (Exception)
            {
                // The database being unreachable must not take the login page with
                // it - that is the page people need in order to report the outage.
                return new AppSettingsSnapshot(values, fromDatabase: false);
            }

            foreach (var row in rows)
            {
                values[$"{row.ModuleName}:{row.SettingKey}"] = row.Value;
            }

            return new AppSettingsSnapshot(values, fromDatabase: rows.Count > 0);
        }
    }
}
