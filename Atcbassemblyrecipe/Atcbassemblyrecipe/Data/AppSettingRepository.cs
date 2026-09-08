using Atcbassemblyrecipe.Infrastructure;
using Atcbassemblyrecipe.Models;
using Oracle.ManagedDataAccess.Client;

namespace Atcbassemblyrecipe.Data
{
    public interface IAppSettingRepository
    {
        Task<IReadOnlyList<AppSetting>> GetAllAsync(CancellationToken cancellationToken = default);
        Task<bool> TableExistsAsync(CancellationToken cancellationToken = default);
        Task SaveAsync(string module, string key, string? value, string updatedBy,
            CancellationToken cancellationToken = default);
    }

    // Reads TBLAPPSETTING, the one place the UI text, media and design tokens live.
    //
    // A missing table is not an error here: the pages fall back to
    // Models/AppSettingDefaults.cs, exactly as they rendered before this table
    // existed, and the caller logs the reason once instead of failing every request.
    public sealed class AppSettingRepository : IAppSettingRepository
    {
        internal const string TableName = "TBLAPPSETTING";
        private const int TableOrViewDoesNotExist = 942;

        private readonly IOracleConnectionFactory _connectionFactory;
        private readonly ILogger<AppSettingRepository> _logger;

        public AppSettingRepository(IOracleConnectionFactory connectionFactory,
            ILogger<AppSettingRepository> logger)
        {
            _connectionFactory = connectionFactory;
            _logger = logger;
        }

        public async Task<IReadOnlyList<AppSetting>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            const string sql = """
                SELECT module_name, setting_key, setting_value, setting_type, description
                FROM tblappsetting
                WHERE is_active = 'Y'
                ORDER BY module_name, setting_key
                """;

            var settings = new List<AppSetting>();

            try
            {
                await using var connection = _connectionFactory.CreateConnection();
                await connection.OpenAsync(cancellationToken);

                await using var command = new OracleCommand(sql, connection);
                await using var reader = await command.ExecuteReaderTracedAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    settings.Add(new AppSetting(
                        reader.GetString(0),
                        reader.GetString(1),
                        reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                        reader.IsDBNull(3) ? "TEXT" : reader.GetString(3),
                        reader.IsDBNull(4) ? null : reader.GetString(4)));
                }
            }
            catch (OracleException ex) when (ex.Number == TableOrViewDoesNotExist)
            {
                _logger.LogWarning(
                    "{Table} does not exist, so the built-in defaults are in use. Run Database/tblappsetting.sql to make the UI settings editable.",
                    TableName);
                return [];
            }

            return settings;
        }

        public async Task<bool> TableExistsAsync(CancellationToken cancellationToken = default)
        {
            const string sql = "SELECT COUNT(*) FROM user_tables WHERE table_name = :name";

            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken);

            await using var command = new OracleCommand(sql, connection);
            command.Parameters.Add(new OracleParameter("name", TableName));

            var count = await command.ExecuteScalarTracedAsync(cancellationToken);
            return Convert.ToInt32(count) > 0;
        }

        public async Task SaveAsync(string module, string key, string? value, string updatedBy,
            CancellationToken cancellationToken = default)
        {
            // MERGE keeps one row per (module, key) whether the setting is being
            // changed or added, which is what the unique constraint expects.
            const string sql = """
                MERGE INTO tblappsetting t
                USING (SELECT :module AS module_name, :key AS setting_key FROM dual) s
                ON (t.module_name = s.module_name AND t.setting_key = s.setting_key)
                WHEN MATCHED THEN
                    UPDATE SET t.setting_value = :value,
                               t.updated_by = :updatedBy,
                               t.updated_date = SYSDATE
                WHEN NOT MATCHED THEN
                    INSERT (module_name, setting_key, setting_value, updated_by)
                    VALUES (:module, :key, :value, :updatedBy)
                """;

            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken);

            await using var command = new OracleCommand(sql, connection) { BindByName = true };
            command.Parameters.Add(new OracleParameter("module", module));
            command.Parameters.Add(new OracleParameter("key", key));
            command.Parameters.Add(new OracleParameter("value", (object?)value ?? DBNull.Value));
            command.Parameters.Add(new OracleParameter("updatedBy", updatedBy));

            await command.ExecuteNonQueryTracedAsync(cancellationToken);
        }
    }
}
