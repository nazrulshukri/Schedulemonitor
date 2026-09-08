using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Oracle.ManagedDataAccess.Client;

namespace Atcbassemblyrecipe.Infrastructure
{
    // Traces every statement the app sends to Oracle to the console: the SQL, the
    // bind values, how many rows it touched, how long it took, and the ORA number
    // when it fails.
    //
    // The logger is static because these are extension methods called from static
    // helpers as well as from injected services - threading an ILogger through all
    // of them would mean changing every signature in Data and Services. Configure()
    // is called once at start-up, before any request is served.
    //
    // Levels: writes (INSERT/UPDATE/DELETE/MERGE/TRUNCATE/DDL) log at Information,
    // reads (SELECT) at Debug, failures at Error. So the default console shows
    // every change to the database and nothing else; set
    // Logging:LogLevel:Atcbassemblyrecipe.Sql to "Debug" to see reads too.
    public static class SqlTrace
    {
        public const string CategoryName = "Atcbassemblyrecipe.Sql";

        private const int MaxSqlLength = 400;
        private const int MaxValueLength = 80;

        private static ILogger _logger = NullLogger.Instance;

        public static void Configure(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger(CategoryName);
        }

        public static async Task<int> ExecuteNonQueryTracedAsync(
            this OracleCommand command,
            CancellationToken cancellationToken = default)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var rows = await command.ExecuteNonQueryAsync(cancellationToken);
                stopwatch.Stop();
                LogSuccess(command, stopwatch, $"{rows} row(s)");
                return rows;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                LogFailure(command, stopwatch, ex);
                throw;
            }
        }

        public static async Task<object?> ExecuteScalarTracedAsync(
            this OracleCommand command,
            CancellationToken cancellationToken = default)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var value = await command.ExecuteScalarAsync(cancellationToken);
                stopwatch.Stop();
                LogSuccess(command, stopwatch, $"scalar {Describe(value)}");
                return value;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                LogFailure(command, stopwatch, ex);
                throw;
            }
        }

        // Returns OracleDataReader rather than DbDataReader so existing call sites
        // that cast the result keep compiling.
        public static async Task<OracleDataReader> ExecuteReaderTracedAsync(
            this OracleCommand command,
            CancellationToken cancellationToken = default)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var reader = (OracleDataReader)await command.ExecuteReaderAsync(cancellationToken);
                stopwatch.Stop();
                LogSuccess(command, stopwatch, "reader opened");
                return reader;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                LogFailure(command, stopwatch, ex);
                throw;
            }
        }

        private static void LogSuccess(OracleCommand command, Stopwatch stopwatch, string outcome)
        {
            var verb = Verb(command.CommandText);
            var level = IsWrite(verb) ? LogLevel.Information : LogLevel.Debug;

            if (!_logger.IsEnabled(level))
            {
                return;
            }

            _logger.Log(
                level,
                "{Verb} {Sql} {Binds} -> {Outcome} in {Elapsed}ms",
                verb,
                Flatten(command.CommandText),
                Binds(command),
                outcome,
                stopwatch.ElapsedMilliseconds);
        }

        private static void LogFailure(OracleCommand command, Stopwatch stopwatch, Exception ex)
        {
            var oraNumber = ex is OracleException oracle ? $"ORA-{oracle.Number:00000}" : ex.GetType().Name;

            _logger.LogError(
                ex,
                "{Verb} FAILED {OraNumber} after {Elapsed}ms: {Sql} {Binds}",
                Verb(command.CommandText),
                oraNumber,
                stopwatch.ElapsedMilliseconds,
                Flatten(command.CommandText),
                Binds(command));
        }

        private static string Verb(string? sql)
        {
            if (string.IsNullOrWhiteSpace(sql))
            {
                return "SQL";
            }

            var first = sql.TrimStart().Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
            return first.Length == 0 ? "SQL" : first[0].ToUpperInvariant();
        }

        private static bool IsWrite(string verb) => verb is
            "INSERT" or "UPDATE" or "DELETE" or "MERGE" or "TRUNCATE"
            or "CREATE" or "DROP" or "ALTER" or "GRANT" or "REVOKE";

        // Multi-line SQL becomes one line so a statement stays one console row.
        private static string Flatten(string? sql)
        {
            if (string.IsNullOrWhiteSpace(sql))
            {
                return string.Empty;
            }

            var flattened = string.Join(' ', sql.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            return flattened.Length <= MaxSqlLength
                ? flattened
                : flattened[..MaxSqlLength] + " ...";
        }

        private static string Binds(OracleCommand command)
        {
            if (command.Parameters.Count == 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder("[");

            for (var i = 0; i < command.Parameters.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                var parameter = command.Parameters[i];
                builder.Append(parameter.ParameterName).Append('=').Append(Describe(parameter.Value));
            }

            return builder.Append(']').ToString();
        }

        // CLOB snapshots run to thousands of characters, so values are clipped.
        private static string Describe(object? value)
        {
            if (value is null || value is DBNull)
            {
                return "NULL";
            }

            var text = value.ToString() ?? string.Empty;
            return text.Length <= MaxValueLength ? text : text[..MaxValueLength] + "...";
        }
    }
}
