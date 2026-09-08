using Oracle.ManagedDataAccess.Client;
using Atcbassemblyrecipe.Infrastructure;

namespace Atcbassemblyrecipe.Data
{
    // Whether the two undo tables from Database/tblrecipeaudit.sql are present in
    // the schema the app actually connects as.
    public sealed record AuditSchemaStatus(bool HistoryExists, bool TrashExists)
    {
        public bool IsComplete => HistoryExists && TrashExists;

        public IEnumerable<string> MissingTables
        {
            get
            {
                if (!HistoryExists)
                {
                    yield return AuditSchemaInstaller.HistoryTable;
                }

                if (!TrashExists)
                {
                    yield return AuditSchemaInstaller.TrashTable;
                }
            }
        }
    }

    public interface IAuditSchemaInstaller
    {
        Task<AuditSchemaStatus> GetStatusAsync(CancellationToken cancellationToken = default);
        Task<IReadOnlyList<string>> InstallAsync(CancellationToken cancellationToken = default);
    }

    // Runs Database/tblrecipeaudit.sql from inside the app, as the same Oracle user
    // the app connects as, so the undo tables cannot end up in the wrong schema.
    //
    // Re-running is safe: a CREATE for something that already exists comes back as
    // ORA-00955 and is reported as "already present" instead of failing the run.
    public sealed class AuditSchemaInstaller : IAuditSchemaInstaller
    {
        internal const string HistoryTable = "TBLRECIPEHISTORY";
        internal const string TrashTable = "TBLRECIPETRASH";

        private const int NameAlreadyUsedErrorNumber = 955;
        private const string ScriptFileName = "tblrecipeaudit.sql";

        private readonly IOracleConnectionFactory _connectionFactory;
        private readonly ILogger<AuditSchemaInstaller> _logger;
        private readonly IWebHostEnvironment _environment;

        public AuditSchemaInstaller(
            IOracleConnectionFactory connectionFactory,
            ILogger<AuditSchemaInstaller> logger,
            IWebHostEnvironment environment)
        {
            _connectionFactory = connectionFactory;
            _logger = logger;
            _environment = environment;
        }

        public async Task<AuditSchemaStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.BindByName = true;
            command.CommandText =
                "SELECT table_name FROM user_tables WHERE table_name IN (:history, :trash)";
            command.Parameters.Add(new OracleParameter("history", HistoryTable));
            command.Parameters.Add(new OracleParameter("trash", TrashTable));

            var history = false;
            var trash = false;

            await using var reader = await command.ExecuteReaderTracedAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var name = reader.GetString(0);
                history |= string.Equals(name, HistoryTable, StringComparison.OrdinalIgnoreCase);
                trash |= string.Equals(name, TrashTable, StringComparison.OrdinalIgnoreCase);
            }

            return new AuditSchemaStatus(history, trash);
        }

        public async Task<IReadOnlyList<string>> InstallAsync(CancellationToken cancellationToken = default)
        {
            var scriptPath = ResolveScriptPath();
            var statements = ParseStatements(await File.ReadAllTextAsync(scriptPath, cancellationToken));

            if (statements.Count == 0)
            {
                throw new InvalidOperationException($"No SQL statements were found in {scriptPath}.");
            }

            var log = new List<string> { $"Reading {scriptPath} ({statements.Count} statements)." };

            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken);
            log.Add($"Connected as {await GetCurrentUserAsync(connection, cancellationToken)}.");

            foreach (var statement in statements)
            {
                var summary = Summarize(statement);

                await using var command = connection.CreateCommand();
                command.CommandText = statement;

                try
                {
                    await command.ExecuteNonQueryTracedAsync(cancellationToken);
                    log.Add($"OK       {summary}");
                    _logger.LogInformation("Audit schema: created {Object}.", summary);
                }
                catch (OracleException ex) when (ex.Number == NameAlreadyUsedErrorNumber)
                {
                    log.Add($"SKIPPED  {summary} (already exists)");
                    _logger.LogInformation("Audit schema: {Object} already exists.", summary);
                }
            }

            return log;
        }

        private string ResolveScriptPath()
        {
            var candidates = new[]
            {
                Path.Combine(_environment.ContentRootPath, "Database", ScriptFileName),
                Path.Combine(AppContext.BaseDirectory, "Database", ScriptFileName)
            };

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            throw new FileNotFoundException(
                $"Could not find Database/{ScriptFileName}. Looked in: {string.Join("; ", candidates)}");
        }

        private static async Task<string> GetCurrentUserAsync(OracleConnection connection, CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT USER FROM dual";
            return (await command.ExecuteScalarTracedAsync(cancellationToken))?.ToString() ?? "unknown";
        }

        // The script is plain DDL - CREATE TABLE and CREATE INDEX, with "--" comments
        // and no PL/SQL blocks - so stripping comment lines and splitting on ";" is
        // enough. Anything more (a trigger, a package) would need a real parser.
        internal static IReadOnlyList<string> ParseStatements(string script)
        {
            var lines = script
                .Split('\n')
                .Select(line => line.TrimEnd('\r'))
                .Where(line => !line.TrimStart().StartsWith("--", StringComparison.Ordinal));

            return string.Join('\n', lines)
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(statement => statement.Trim())
                .Where(statement => statement.Length > 0)
                .ToList();
        }

        // "CREATE TABLE tblrecipehistory (..." -> "CREATE TABLE tblrecipehistory"
        private static string Summarize(string statement)
        {
            var words = statement.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var taken = words.Take(3).Select(word => word.TrimEnd('('));
            return string.Join(' ', taken);
        }
    }
}
