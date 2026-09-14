using System.Text.RegularExpressions;
using Atcbassemblyrecipe.Data;
using Atcbassemblyrecipe.Infrastructure;
using Atcbassemblyrecipe.Models;
using Microsoft.Extensions.Caching.Memory;
using Oracle.ManagedDataAccess.Client;

namespace Atcbassemblyrecipe.Services
{
    public interface IEngineeringColumnGroupProvider
    {
        // Every configured column, shared first then by group and sort order.
        Task<IReadOnlyList<EngineeringColumn>> GetAllAsync();

        // The columns the signed-in user is entitled to: the shared ones, plus
        // every group whose TBLACCESS module they can view. This is what the
        // page renders and what the service selects - a column outside the
        // user's groups is never read, never written and never exported.
        Task<IReadOnlyList<EngineeringColumn>> GetVisibleAsync();

        // The group names the signed-in user belongs to, for the page to name
        // in its heading ("showing the SAWING columns").
        Task<IReadOnlyList<string>> GetUserGroupsAsync();

        // True when the mapping came from OCAPSYS.ENGINEERINGCOLUMNGROUP, false
        // when the table is missing or unreadable and the built-in seed is
        // standing in for it.
        Task<bool> IsFromDatabaseAsync();
    }

    // Reads OCAPSYS.ENGINEERINGCOLUMNGROUP - which ENGINEERING column belongs to
    // which user group - and answers what the current user may see.
    //
    // Why a table and not a C# array: which team owns which recipe column is a
    // production decision, not a code one. Moving WIREBOND to the marker
    // group, or adding a fourth column for a fourth group, is an UPDATE and a
    // cache expiry rather than a redeploy.
    //
    // Two safety rules, because these names reach an Oracle statement as
    // identifiers and identifiers cannot be bound as parameters:
    //
    //   1. A name that is not plain A-Z, 0-9 and underscore is dropped outright.
    //   2. A name that is not a real column of ENGINEERING is dropped, checked
    //      against the data dictionary through ITableSchemaProvider.
    //
    // So the worst a bad row in the config table can do is hide a column, never
    // inject SQL.
    //
    // The mapping is cached for CacheSeconds (the same AppSettings:CacheSeconds
    // the UI settings use, default 60) rather than for the process lifetime, so
    // an UPDATE to the table shows up without a restart.
    public sealed partial class EngineeringColumnGroupProvider : IEngineeringColumnGroupProvider
    {
        private const string CacheKey = "engineering-column-groups";

        [GeneratedRegex("^[A-Z][A-Z0-9_]*$")]
        private static partial Regex SafeIdentifier();

        private readonly IOracleConnectionFactory _connectionFactory;
        private readonly ITableSchemaProvider _schemaProvider;
        private readonly IAccessEvaluator _accessEvaluator;
        private readonly IMemoryCache _cache;
        private readonly IConfiguration _configuration;
        private readonly ILogger<EngineeringColumnGroupProvider> _logger;

        private IReadOnlyList<EngineeringColumn>? _visible;
        private IReadOnlyList<string>? _userGroups;

        public EngineeringColumnGroupProvider(
            IOracleConnectionFactory connectionFactory,
            ITableSchemaProvider schemaProvider,
            IAccessEvaluator accessEvaluator,
            IMemoryCache cache,
            IConfiguration configuration,
            ILogger<EngineeringColumnGroupProvider> logger)
        {
            _connectionFactory = connectionFactory;
            _schemaProvider = schemaProvider;
            _accessEvaluator = accessEvaluator;
            _cache = cache;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<IReadOnlyList<EngineeringColumn>> GetAllAsync()
        {
            return (await LoadAsync()).Columns;
        }

        public async Task<bool> IsFromDatabaseAsync()
        {
            return (await LoadAsync()).FromDatabase;
        }

        public async Task<IReadOnlyList<string>> GetUserGroupsAsync()
        {
            if (_userGroups is not null)
            {
                return _userGroups;
            }

            var groups = new List<string>();
            foreach (var group in new[] { EngineeringGroups.Sawing, EngineeringGroups.Wirebond, EngineeringGroups.Marker })
            {
                var module = EngineeringGroups.ModuleFor(group);
                if (module is not null && (await _accessEvaluator.GetAsync(module)).CanView)
                {
                    groups.Add(group);
                }
            }

            _userGroups = groups;
            return groups;
        }

        public async Task<IReadOnlyList<EngineeringColumn>> GetVisibleAsync()
        {
            if (_visible is not null)
            {
                return _visible;
            }

            var all = await GetAllAsync();
            var groups = await GetUserGroupsAsync();

            // Shared columns are the row's identity - lot number, package,
            // product. Without them a user in one group would be looking at a
            // page of recipes with nothing saying which lot they belong to.
            var visible = all
                .Where(column => column.IsShared || groups.Contains(column.GroupName, StringComparer.OrdinalIgnoreCase))
                .ToList();

            _visible = visible;
            return visible;
        }

        private async Task<Mapping> LoadAsync()
        {
            if (_cache.TryGetValue<Mapping>(CacheKey, out var cached) && cached is not null)
            {
                return cached;
            }

            var mapping = await ReadAsync();

            var seconds = _configuration.GetValue("AppSettings:CacheSeconds", 60);
            _cache.Set(CacheKey, mapping, TimeSpan.FromSeconds(seconds <= 0 ? 60 : seconds));

            return mapping;
        }

        private async Task<Mapping> ReadAsync()
        {
            try
            {
                await using var connection = _connectionFactory.CreateConnection();
                await connection.OpenAsync();

                // The real shape of ENGINEERING. NOTHING is handed back that is
                // not in this set - not a row of ENGINEERINGCOLUMNGROUP, and not
                // the built-in fallback either. A column name that is not really
                // there reaches the SELECT as an identifier and comes back as
                // ORA-00904, which reads like a broken app rather than what it
                // is: the table not matching the mapping.
                //
                // GetColumnsAsync throws rather than answering an empty list when
                // the table is not in the data dictionary at all.
                HashSet<string> actualColumns;
                try
                {
                    actualColumns = (await _schemaProvider.GetColumnsAsync(connection, null, "ENGINEERING"))
                        .Select(column => column.Name)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                }
                catch (InvalidOperationException)
                {
                    // No table, so no verified column exists and there is nothing
                    // safe to guess. An empty mapping renders a page that says so.
                    _logger.LogWarning(
                        "ENGINEERING is not in the data dictionary. Run Database/engineering.sql against the OCAPSYS schema.");
                    return Mapping.Missing;
                }

                var columns = new List<EngineeringColumn>();

                await using (var command = connection.CreateCommand())
                {
                    command.CommandText = """
                        SELECT column_name, group_name, display_label, sort_order
                        FROM   engineeringcolumngroup
                        ORDER  BY CASE UPPER(group_name)
                                    WHEN 'SHARED'   THEN 0
                                    WHEN 'SAWING'   THEN 1
                                    WHEN 'WIREBOND' THEN 2
                                    WHEN 'MARKER'   THEN 3
                                    ELSE 4
                                  END,
                                  sort_order,
                                  column_name
                        """;

                    await using var reader = await command.ExecuteReaderTracedAsync();
                    while (await reader.ReadAsync())
                    {
                        var name = (reader.IsDBNull(0) ? string.Empty : reader.GetString(0)).Trim().ToUpperInvariant();
                        var group = (reader.IsDBNull(1) ? string.Empty : reader.GetString(1)).Trim().ToUpperInvariant();

                        if (!IsUsable(name, group, actualColumns, "ENGINEERINGCOLUMNGROUP"))
                        {
                            continue;
                        }

                        var label = reader.IsDBNull(2) ? name : reader.GetString(2).Trim();
                        var sortOrder = reader.IsDBNull(3) ? 0 : Convert.ToInt32(reader.GetValue(3));

                        columns.Add(new EngineeringColumn(
                            name,
                            group,
                            string.IsNullOrWhiteSpace(label) ? name : label,
                            sortOrder));
                    }
                }

                if (columns.Count > 0)
                {
                    return new Mapping(columns, true);
                }

                _logger.LogWarning(
                    "ENGINEERINGCOLUMNGROUP is empty, or none of its rows name a real ENGINEERING column, so the built-in mapping is being used. Run Database/engineeringcolumngroup.sql.");

                return Fallback(actualColumns);
            }
            catch (Exception ex) when (ex is OracleException or InvalidOperationException)
            {
                // The config table is missing or unreadable. The shape of
                // ENGINEERING is unknown from here, so there is nothing that can
                // be verified and nothing is guessed.
                _logger.LogWarning(
                    ex,
                    "Could not read ENGINEERINGCOLUMNGROUP. Run Database/engineeringcolumngroup.sql against the OCAPSYS schema.");
                return Mapping.Missing;
            }
        }

        // The built-in mapping, narrowed to the columns ENGINEERING actually has.
        // A default naming a column the table does not carry is dropped and named
        // in the log - which is the difference between a page that says "run the
        // script" and one that answers every request with ORA-00904.
        private Mapping Fallback(HashSet<string> actualColumns)
        {
            var usable = Defaults()
                .Where(column => IsUsable(column.Name, column.GroupName, actualColumns, "the built-in mapping"))
                .ToList();

            if (usable.Count == 0)
            {
                _logger.LogWarning(
                    "ENGINEERING carries none of the columns this app knows about. It is probably still the old per-step shape - run Database/engineering.sql to rebuild it as the three-recipe one.");
            }

            return new Mapping(usable, false);
        }

        // One gate for both sources: a plain identifier, a real column of
        // ENGINEERING, not bookkeeping, and a group the app understands.
        private bool IsUsable(string name, string group, HashSet<string> actualColumns, string source)
        {
            if (!SafeIdentifier().IsMatch(name))
            {
                _logger.LogWarning("{Source} row '{Column}' is not a valid column name and was ignored.", source, name);
                return false;
            }

            if (EngineeringColumns.Bookkeeping.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!actualColumns.Contains(name))
            {
                _logger.LogWarning(
                    "{Source} names {Column}, which is not a column of ENGINEERING. Ignored - check the table against Database/engineering.sql.",
                    source, name);
                return false;
            }

            if (!EngineeringGroups.All.Contains(group, StringComparer.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "{Source} gives {Column} the group '{Group}', which is not one of SAWING, WIREBOND, MARKER or SHARED. Ignored.",
                    source, name, group);
                return false;
            }

            return true;
        }

        // The same mapping Database/engineeringcolumngroup.sql seeds. Kept here
        // so the page works the moment ENGINEERING exists, before anybody has
        // run the second script - and so the two can be compared when they
        // disagree.
        // The same mapping Database/engineeringcolumngroup.sql seeds. Kept here
        // so the page works the moment ENGINEERING exists, before anybody has
        // run the second script - and so the two can be compared when they
        // disagree.
        private static List<EngineeringColumn> Defaults()
        {
            return
            [
                new("NO", EngineeringGroups.Shared, "No", 10),
                new("REQUESTOR", EngineeringGroups.Shared, "Requestor", 20),
                new("LOTNUMBER", EngineeringGroups.Shared, "Lot Number", 30),
                new("PACKAGE", EngineeringGroups.Shared, "Package", 40),
                new("PRODUCT", EngineeringGroups.Shared, "Product", 50),

                new("SAWING", EngineeringGroups.Sawing, "Sawing Recipe", 10),
                new("WIREBOND", EngineeringGroups.Wirebond, "Wirebond Recipe", 10),
                new("MARKER", EngineeringGroups.Marker, "Marker Recipe", 10)
            ];
        }

        private sealed record Mapping(IReadOnlyList<EngineeringColumn> Columns, bool FromDatabase)
        {
            // Nothing could be verified against the data dictionary, so no column
            // is offered at all. The page says the table is missing rather than
            // asking Oracle for columns that may not be there.
            public static readonly Mapping Missing = new([], false);
        }
    }
}
