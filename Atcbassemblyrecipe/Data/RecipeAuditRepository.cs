using System.Globalization;
using System.Text;
using System.Text.Json;
using Atcbassemblyrecipe.Models;
using Atcbassemblyrecipe.ViewModels;
using Oracle.ManagedDataAccess.Client;
using Atcbassemblyrecipe.Infrastructure;

namespace Atcbassemblyrecipe.Data
{
    public interface IRecipeAuditRepository
    {
        Task<IReadOnlyDictionary<string, string?>> CaptureRowAsync(OracleConnection connection, OracleTransaction transaction, string tableName, string rowKey);

        Task WriteHistoryAsync(
            OracleConnection connection,
            OracleTransaction transaction,
            string tableName,
            string targetLabel,
            string rowKey,
            IReadOnlyDictionary<string, string?> oldValues,
            IReadOnlyDictionary<string, string?> newValues,
            string changedBy);

        Task WriteTrashAsync(
            OracleConnection connection,
            OracleTransaction transaction,
            string tableName,
            string targetLabel,
            IReadOnlyDictionary<string, string?> rowData,
            string deletedBy);

        Task<PagedResult<ChangeHistoryEntry>> GetHistoryAsync(string? search, int page, int pageSize, bool includeReverted);
        Task<PagedResult<TrashEntry>> GetTrashAsync(string? search, int page, int pageSize, bool includeRestored);
        Task<(bool Success, string Message)> RevertAsync(long historyId, string userName);
        Task<(bool Success, string Message)> RestoreAsync(long trashId, string userName);
        Task<(bool Success, string Message)> PurgeAsync(long trashId);
    }

    // Keeps the "undo" side of the app: TBLRECIPEHISTORY stores what a row looked
    // like before each update, TBLRECIPETRASH stores rows that were deleted.
    //
    // Both writes happen on the caller's own connection and transaction, so the
    // snapshot and the change it describes commit together or not at all - a row
    // can never be deleted without landing in the trash first.
    //
    // Both tables are created by Database/tblrecipeaudit.sql. Until that script has
    // been run, ORA-00942 is turned into a plain-language message instead of a
    // stack trace.
    public class RecipeAuditRepository : IRecipeAuditRepository
    {
        private const int MissingTableErrorNumber = 942;
        private const string SetupHint =
            "Run Database/tblrecipeaudit.sql once against the OCAP schema to create TBLRECIPEHISTORY and TBLRECIPETRASH.";

        private readonly IOracleConnectionFactory _connectionFactory;
        private readonly ITableSchemaProvider _schemaProvider;
        private readonly ILogger<RecipeAuditRepository> _logger;

        public RecipeAuditRepository(
            IOracleConnectionFactory connectionFactory,
            ITableSchemaProvider schemaProvider,
            ILogger<RecipeAuditRepository> logger)
        {
            _connectionFactory = connectionFactory;
            _schemaProvider = schemaProvider;
            _logger = logger;
        }

        public async Task<IReadOnlyDictionary<string, string?>> CaptureRowAsync(OracleConnection connection, OracleTransaction transaction, string tableName, string rowKey)
        {
            var table = AuditedTable.For(tableName);
            var columns = await _schemaProvider.GetColumnsAsync(connection, transaction, table.Name);

            await using var command = connection.CreateCommand();
            command.BindByName = true;
            command.Transaction = transaction;
            command.CommandText = $"""
                SELECT {string.Join(", ", columns.Select(column => Quote(column.Name)))}
                FROM {table.Name}
                WHERE {table.KeyPredicate}
                """;
            command.Parameters.Add(new OracleParameter("row_key", rowKey));

            await using var reader = await command.ExecuteReaderTracedAsync();
            if (!await reader.ReadAsync())
            {
                return new Dictionary<string, string?>();
            }

            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < columns.Count; index++)
            {
                values[columns[index].Name] = reader.IsDBNull(index)
                    ? null
                    : columns[index].Type switch
                    {
                        TableColumnType.Number => reader.GetDecimal(index).ToString(CultureInfo.InvariantCulture),
                        TableColumnType.Date => reader.GetDateTime(index).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                        _ => reader.GetString(index)
                    };
            }

            return values;
        }

        public async Task WriteHistoryAsync(
            OracleConnection connection,
            OracleTransaction transaction,
            string tableName,
            string targetLabel,
            string rowKey,
            IReadOnlyDictionary<string, string?> oldValues,
            IReadOnlyDictionary<string, string?> newValues,
            string changedBy)
        {
            await using var command = connection.CreateCommand();
            command.BindByName = true;
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO tblrecipehistory
                    (table_name, target_label, row_key, old_values, new_values, changed_by, changed_date)
                VALUES
                    (:table_name, :target_label, :row_key, :old_values, :new_values, :changed_by, SYSDATE)
                """;
            command.Parameters.Add(new OracleParameter("table_name", AuditedTable.For(tableName).Name));
            command.Parameters.Add(new OracleParameter("target_label", Truncate(targetLabel, 200)));
            command.Parameters.Add(new OracleParameter("row_key", Truncate(rowKey, 64)));
            command.Parameters.Add(new OracleParameter("old_values", OracleDbType.Clob) { Value = Serialize(oldValues) });
            command.Parameters.Add(new OracleParameter("new_values", OracleDbType.Clob) { Value = Serialize(newValues) });
            command.Parameters.Add(new OracleParameter("changed_by", Truncate(changedBy, 50)));

            await ExecuteWithSetupHintAsync(command);

            var changed = ChangedColumns(oldValues, newValues);
            _logger.LogInformation(
                "UPDATE {Table} [{Target}] by {User} - {ChangedCount} column(s) changed: {Columns}",
                AuditedTable.For(tableName).Name,
                targetLabel,
                changedBy,
                changed.Count,
                string.Join(", ", changed));
        }

        public async Task WriteTrashAsync(
            OracleConnection connection,
            OracleTransaction transaction,
            string tableName,
            string targetLabel,
            IReadOnlyDictionary<string, string?> rowData,
            string deletedBy)
        {
            await using var command = connection.CreateCommand();
            command.BindByName = true;
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO tblrecipetrash
                    (table_name, target_label, row_data, deleted_by, deleted_date)
                VALUES
                    (:table_name, :target_label, :row_data, :deleted_by, SYSDATE)
                """;
            command.Parameters.Add(new OracleParameter("table_name", AuditedTable.For(tableName).Name));
            command.Parameters.Add(new OracleParameter("target_label", Truncate(targetLabel, 200)));
            command.Parameters.Add(new OracleParameter("row_data", OracleDbType.Clob) { Value = Serialize(rowData) });
            command.Parameters.Add(new OracleParameter("deleted_by", Truncate(deletedBy, 50)));

            await ExecuteWithSetupHintAsync(command);

            _logger.LogInformation(
                "DELETE {Table} [{Target}] by {User} - row copied to TBLRECIPETRASH, restorable from Settings > Trash",
                AuditedTable.For(tableName).Name,
                targetLabel,
                deletedBy);
        }

        public async Task<PagedResult<ChangeHistoryEntry>> GetHistoryAsync(string? search, int page, int pageSize, bool includeReverted)
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize is < 10 or > 100 ? 25 : pageSize;
            var normalizedSearch = NormalizeSearch(search);
            var rows = new List<ChangeHistoryEntry>();

            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync();

            var filter = """
                WHERE (:search IS NULL
                       OR UPPER(table_name) LIKE :search
                       OR UPPER(target_label) LIKE :search
                       OR UPPER(changed_by) LIKE :search)
                  AND (:include_reverted = 1 OR reverted = 'N')
                """;

            var totalRows = await CountAsync(connection, $"SELECT COUNT(1) FROM tblrecipehistory {filter}", normalizedSearch, includeReverted);

            await using var command = connection.CreateCommand();
            command.BindByName = true;
            command.CommandText = $"""
                SELECT history_id, table_name, target_label, row_key, old_values, new_values,
                       changed_by, changed_date, reverted, reverted_by, reverted_date
                FROM tblrecipehistory
                {filter}
                ORDER BY changed_date DESC, history_id DESC
                OFFSET :offset ROWS FETCH NEXT :page_size ROWS ONLY
                """;
            AddListParameters(command, normalizedSearch, includeReverted, (page - 1) * pageSize, pageSize);

            await using (var reader = await ExecuteReaderWithSetupHintAsync(command))
            {
                while (await reader.ReadAsync())
                {
                    rows.Add(new ChangeHistoryEntry
                    {
                        HistoryId = reader.GetInt64(0),
                        TableName = ReadString(reader, 1),
                        TargetLabel = ReadString(reader, 2),
                        RowKey = ReadString(reader, 3),
                        OldValues = Deserialize(ReadClob(reader, 4)),
                        NewValues = Deserialize(ReadClob(reader, 5)),
                        ChangedBy = ReadString(reader, 6),
                        ChangedDate = reader.GetDateTime(7),
                        Reverted = string.Equals(ReadString(reader, 8), "Y", StringComparison.OrdinalIgnoreCase),
                        RevertedBy = ReadString(reader, 9),
                        RevertedDate = reader.IsDBNull(10) ? null : reader.GetDateTime(10)
                    });
                }
            }

            return new PagedResult<ChangeHistoryEntry>
            {
                Rows = rows,
                Search = search?.Trim() ?? string.Empty,
                Page = page,
                PageSize = pageSize,
                TotalRows = totalRows
            };
        }

        public async Task<PagedResult<TrashEntry>> GetTrashAsync(string? search, int page, int pageSize, bool includeRestored)
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize is < 10 or > 100 ? 25 : pageSize;
            var normalizedSearch = NormalizeSearch(search);
            var rows = new List<TrashEntry>();

            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync();

            var filter = """
                WHERE (:search IS NULL
                       OR UPPER(table_name) LIKE :search
                       OR UPPER(target_label) LIKE :search
                       OR UPPER(deleted_by) LIKE :search)
                  AND (:include_reverted = 1 OR restored = 'N')
                """;

            var totalRows = await CountAsync(connection, $"SELECT COUNT(1) FROM tblrecipetrash {filter}", normalizedSearch, includeRestored);

            await using var command = connection.CreateCommand();
            command.BindByName = true;
            command.CommandText = $"""
                SELECT trash_id, table_name, target_label, row_data,
                       deleted_by, deleted_date, restored, restored_by, restored_date
                FROM tblrecipetrash
                {filter}
                ORDER BY deleted_date DESC, trash_id DESC
                OFFSET :offset ROWS FETCH NEXT :page_size ROWS ONLY
                """;
            AddListParameters(command, normalizedSearch, includeRestored, (page - 1) * pageSize, pageSize);

            await using (var reader = await ExecuteReaderWithSetupHintAsync(command))
            {
                while (await reader.ReadAsync())
                {
                    rows.Add(new TrashEntry
                    {
                        TrashId = reader.GetInt64(0),
                        TableName = ReadString(reader, 1),
                        TargetLabel = ReadString(reader, 2),
                        RowData = Deserialize(ReadClob(reader, 3)),
                        DeletedBy = ReadString(reader, 4),
                        DeletedDate = reader.GetDateTime(5),
                        Restored = string.Equals(ReadString(reader, 6), "Y", StringComparison.OrdinalIgnoreCase),
                        RestoredBy = ReadString(reader, 7),
                        RestoredDate = reader.IsDBNull(8) ? null : reader.GetDateTime(8)
                    });
                }
            }

            return new PagedResult<TrashEntry>
            {
                Rows = rows,
                Search = search?.Trim() ?? string.Empty,
                Page = page,
                PageSize = pageSize,
                TotalRows = totalRows
            };
        }

        // Writes the saved "before" values back over the row. The row has to still
        // be there - if it was deleted after the update, the recycle bin is the
        // place to bring it back from, and the message says so.
        public async Task<(bool Success, string Message)> RevertAsync(long historyId, string userName)
        {
            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync();
            await using var transaction = connection.BeginTransaction();

            try
            {
                var entry = await ReadHistoryEntryAsync(connection, transaction, historyId);
                if (entry is null)
                {
                    await transaction.RollbackAsync();
                    return (false, "That change history entry was not found.");
                }

                if (entry.Reverted)
                {
                    await transaction.RollbackAsync();
                    return (false, $"That change was already reverted by {entry.RevertedBy}.");
                }

                var table = AuditedTable.For(entry.TableName);
                var columns = await _schemaProvider.GetColumnsAsync(connection, transaction, table.Name);
                var restorable = BuildAssignments(columns, entry.OldValues);

                if (restorable.Count == 0)
                {
                    await transaction.RollbackAsync();
                    return (false, "The saved snapshot has no columns that still exist on the table.");
                }

                await using var command = connection.CreateCommand();
                command.BindByName = true;
                command.Transaction = transaction;

                var setClause = new StringBuilder();
                for (var index = 0; index < restorable.Count; index++)
                {
                    if (index > 0)
                    {
                        setClause.Append(", ");
                    }

                    setClause.Append(Quote(restorable[index].Column.Name))
                        .Append(" = ")
                        .Append(ValueExpression(restorable[index].Column, $"p{index}"));
                    command.Parameters.Add(BuildParameter($"p{index}", restorable[index].Value));
                }

                command.CommandText = $"""
                    UPDATE {table.Name}
                    SET {setClause}
                    WHERE {table.KeyPredicate}
                    """;
                command.Parameters.Add(new OracleParameter("row_key", entry.RowKey));

                var updated = await command.ExecuteNonQueryTracedAsync();
                if (updated != 1)
                {
                    await transaction.RollbackAsync();
                    return (false, "The original row is no longer there, so nothing was reverted. If it was deleted, restore it from Trash instead.");
                }

                await MarkRevertedAsync(connection, transaction, historyId, userName);
                await transaction.CommitAsync();
                return (true, $"Reverted {entry.TargetLabel} in {entry.TableName} back to the values from {entry.ChangedDate:yyyy-MM-dd HH:mm}.");
            }
            catch (OracleException ex) when (ex.Number == MissingTableErrorNumber)
            {
                await transaction.RollbackAsync();
                throw new InvalidOperationException($"The change history table is missing. {SetupHint}", ex);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        // Puts a deleted row back with a plain INSERT of the whole saved row.
        public async Task<(bool Success, string Message)> RestoreAsync(long trashId, string userName)
        {
            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync();
            await using var transaction = connection.BeginTransaction();

            try
            {
                var entry = await ReadTrashEntryAsync(connection, transaction, trashId);
                if (entry is null)
                {
                    await transaction.RollbackAsync();
                    return (false, "That trash entry was not found.");
                }

                if (entry.Restored)
                {
                    await transaction.RollbackAsync();
                    return (false, $"That row was already restored by {entry.RestoredBy}.");
                }

                var table = AuditedTable.For(entry.TableName);
                var columns = await _schemaProvider.GetColumnsAsync(connection, transaction, table.Name);
                var restorable = BuildAssignments(columns, entry.RowData);

                if (restorable.Count == 0)
                {
                    await transaction.RollbackAsync();
                    return (false, "The saved snapshot has no columns that still exist on the table.");
                }

                await using var command = connection.CreateCommand();
                command.BindByName = true;
                command.Transaction = transaction;

                var columnList = new StringBuilder();
                var valueList = new StringBuilder();
                for (var index = 0; index < restorable.Count; index++)
                {
                    if (index > 0)
                    {
                        columnList.Append(", ");
                        valueList.Append(", ");
                    }

                    columnList.Append(Quote(restorable[index].Column.Name));
                    valueList.Append(ValueExpression(restorable[index].Column, $"p{index}"));
                    command.Parameters.Add(BuildParameter($"p{index}", restorable[index].Value));
                }

                command.CommandText = $"INSERT INTO {table.Name} ({columnList}) VALUES ({valueList})";

                var inserted = await command.ExecuteNonQueryTracedAsync();
                if (inserted != 1)
                {
                    await transaction.RollbackAsync();
                    return (false, "Restore verification failed. Nothing was written back.");
                }

                await MarkRestoredAsync(connection, transaction, trashId, userName);
                await transaction.CommitAsync();
                return (true, $"Restored {entry.TargetLabel} back into {entry.TableName}.");
            }
            catch (OracleException ex) when (ex.Number == MissingTableErrorNumber)
            {
                await transaction.RollbackAsync();
                throw new InvalidOperationException($"The recycle bin table is missing. {SetupHint}", ex);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<(bool Success, string Message)> PurgeAsync(long trashId)
        {
            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.BindByName = true;
            command.CommandText = "DELETE FROM tblrecipetrash WHERE trash_id = :trash_id";
            command.Parameters.Add(new OracleParameter("trash_id", OracleDbType.Int64) { Value = trashId });

            var deleted = await ExecuteWithSetupHintAsync(command);
            return deleted == 1
                ? (true, "Trash entry purged. That row can no longer be restored.")
                : (false, "That trash entry was not found.");
        }

        private async Task<ChangeHistoryEntry?> ReadHistoryEntryAsync(OracleConnection connection, OracleTransaction transaction, long historyId)
        {
            await using var command = connection.CreateCommand();
            command.BindByName = true;
            command.Transaction = transaction;
            command.CommandText = """
                SELECT history_id, table_name, target_label, row_key, old_values, new_values,
                       changed_by, changed_date, reverted, reverted_by, reverted_date
                FROM tblrecipehistory
                WHERE history_id = :history_id
                FOR UPDATE
                """;
            command.Parameters.Add(new OracleParameter("history_id", OracleDbType.Int64) { Value = historyId });

            await using var reader = await command.ExecuteReaderTracedAsync();
            if (!await reader.ReadAsync())
            {
                return null;
            }

            return new ChangeHistoryEntry
            {
                HistoryId = reader.GetInt64(0),
                TableName = ReadString(reader, 1),
                TargetLabel = ReadString(reader, 2),
                RowKey = ReadString(reader, 3),
                OldValues = Deserialize(ReadClob(reader, 4)),
                NewValues = Deserialize(ReadClob(reader, 5)),
                ChangedBy = ReadString(reader, 6),
                ChangedDate = reader.GetDateTime(7),
                Reverted = string.Equals(ReadString(reader, 8), "Y", StringComparison.OrdinalIgnoreCase),
                RevertedBy = ReadString(reader, 9),
                RevertedDate = reader.IsDBNull(10) ? null : reader.GetDateTime(10)
            };
        }

        private async Task<TrashEntry?> ReadTrashEntryAsync(OracleConnection connection, OracleTransaction transaction, long trashId)
        {
            await using var command = connection.CreateCommand();
            command.BindByName = true;
            command.Transaction = transaction;
            command.CommandText = """
                SELECT trash_id, table_name, target_label, row_data,
                       deleted_by, deleted_date, restored, restored_by, restored_date
                FROM tblrecipetrash
                WHERE trash_id = :trash_id
                FOR UPDATE
                """;
            command.Parameters.Add(new OracleParameter("trash_id", OracleDbType.Int64) { Value = trashId });

            await using var reader = await command.ExecuteReaderTracedAsync();
            if (!await reader.ReadAsync())
            {
                return null;
            }

            return new TrashEntry
            {
                TrashId = reader.GetInt64(0),
                TableName = ReadString(reader, 1),
                TargetLabel = ReadString(reader, 2),
                RowData = Deserialize(ReadClob(reader, 3)),
                DeletedBy = ReadString(reader, 4),
                DeletedDate = reader.GetDateTime(5),
                Restored = string.Equals(ReadString(reader, 6), "Y", StringComparison.OrdinalIgnoreCase),
                RestoredBy = ReadString(reader, 7),
                RestoredDate = reader.IsDBNull(8) ? null : reader.GetDateTime(8)
            };
        }

        private static async Task MarkRevertedAsync(OracleConnection connection, OracleTransaction transaction, long historyId, string userName)
        {
            await using var command = connection.CreateCommand();
            command.BindByName = true;
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE tblrecipehistory
                SET reverted = 'Y', reverted_by = :reverted_by, reverted_date = SYSDATE
                WHERE history_id = :history_id
                """;
            command.Parameters.Add(new OracleParameter("reverted_by", Truncate(userName, 50)));
            command.Parameters.Add(new OracleParameter("history_id", OracleDbType.Int64) { Value = historyId });

            await command.ExecuteNonQueryTracedAsync();
        }

        private static async Task MarkRestoredAsync(OracleConnection connection, OracleTransaction transaction, long trashId, string userName)
        {
            await using var command = connection.CreateCommand();
            command.BindByName = true;
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE tblrecipetrash
                SET restored = 'Y', restored_by = :restored_by, restored_date = SYSDATE
                WHERE trash_id = :trash_id
                """;
            command.Parameters.Add(new OracleParameter("restored_by", Truncate(userName, 50)));
            command.Parameters.Add(new OracleParameter("trash_id", OracleDbType.Int64) { Value = trashId });

            await command.ExecuteNonQueryTracedAsync();
        }

        // Only columns the table still has are written back. The snapshot is data
        // read out of a table, so intersecting it with the live column list is also
        // what keeps a hand-edited snapshot from reaching the generated SQL.
        private static List<(TableColumn Column, string? Value)> BuildAssignments(
            IReadOnlyList<TableColumn> columns,
            IReadOnlyDictionary<string, string?> snapshot)
        {
            var assignments = new List<(TableColumn, string?)>();
            foreach (var column in columns)
            {
                if (snapshot.TryGetValue(column.Name, out var value))
                {
                    assignments.Add((column, value));
                }
            }

            return assignments;
        }

        private static string ValueExpression(TableColumn column, string parameterName)
        {
            return column.Type switch
            {
                TableColumnType.Number => $"TO_NUMBER(:{parameterName})",
                TableColumnType.Date => $"TO_DATE(:{parameterName}, 'YYYY-MM-DD HH24:MI:SS')",
                _ => $":{parameterName}"
            };
        }

        private static OracleParameter BuildParameter(string name, string? value)
        {
            return new OracleParameter(name, OracleDbType.Varchar2)
            {
                Value = string.IsNullOrEmpty(value) ? DBNull.Value : value
            };
        }

        // Only the columns whose value actually moved, so the terminal line stays
        // readable on tables with 30+ columns.
        private static IReadOnlyList<string> ChangedColumns(
            IReadOnlyDictionary<string, string?> oldValues,
            IReadOnlyDictionary<string, string?> newValues)
        {
            return newValues
                .Where(pair => !string.Equals(
                    oldValues.GetValueOrDefault(pair.Key),
                    pair.Value,
                    StringComparison.Ordinal))
                .Select(pair => pair.Key)
                .ToList();
        }

        private static async Task<int> ExecuteWithSetupHintAsync(OracleCommand command)
        {
            try
            {
                return await command.ExecuteNonQueryTracedAsync();
            }
            catch (OracleException ex) when (ex.Number == MissingTableErrorNumber)
            {
                throw new InvalidOperationException($"The trash and change history tables are missing, so this change was cancelled rather than losing the old values. {SetupHint}", ex);
            }
        }

        private static async Task<OracleDataReader> ExecuteReaderWithSetupHintAsync(OracleCommand command)
        {
            try
            {
                return (OracleDataReader)await command.ExecuteReaderTracedAsync();
            }
            catch (OracleException ex) when (ex.Number == MissingTableErrorNumber)
            {
                throw new InvalidOperationException($"The trash and change history tables are missing. {SetupHint}", ex);
            }
        }

        private static async Task<int> CountAsync(OracleConnection connection, string commandText, string? search, bool includeAll)
        {
            await using var command = connection.CreateCommand();
            command.BindByName = true;
            command.CommandText = commandText;
            command.Parameters.Add(new OracleParameter("search", OracleDbType.Varchar2) { Value = (object?)search ?? DBNull.Value });
            command.Parameters.Add(new OracleParameter("include_reverted", OracleDbType.Int32) { Value = includeAll ? 1 : 0 });

            try
            {
                return Convert.ToInt32(await command.ExecuteScalarTracedAsync());
            }
            catch (OracleException ex) when (ex.Number == MissingTableErrorNumber)
            {
                throw new InvalidOperationException($"The trash and change history tables are missing. {SetupHint}", ex);
            }
        }

        private static void AddListParameters(OracleCommand command, string? search, bool includeAll, int offset, int pageSize)
        {
            command.Parameters.Add(new OracleParameter("search", OracleDbType.Varchar2) { Value = (object?)search ?? DBNull.Value });
            command.Parameters.Add(new OracleParameter("include_reverted", OracleDbType.Int32) { Value = includeAll ? 1 : 0 });
            command.Parameters.Add(new OracleParameter("offset", OracleDbType.Int32) { Value = offset });
            command.Parameters.Add(new OracleParameter("page_size", OracleDbType.Int32) { Value = pageSize });
        }

        private static string Serialize(IReadOnlyDictionary<string, string?> values)
        {
            return JsonSerializer.Serialize(values);
        }

        private static IReadOnlyDictionary<string, string?> Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new Dictionary<string, string?>();
            }

            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, string?>>(json)
                    ?? new Dictionary<string, string?>();
            }
            catch (JsonException)
            {
                return new Dictionary<string, string?>();
            }
        }

        private static string Quote(string columnName)
        {
            return $"\"{columnName.ToUpperInvariant()}\"";
        }

        private static string Truncate(string value, int maxLength)
        {
            return value.Length <= maxLength ? value : value[..maxLength];
        }

        private static string? NormalizeSearch(string? search)
        {
            return string.IsNullOrWhiteSpace(search) ? null : $"%{search.Trim().ToUpperInvariant()}%";
        }

        private static string ReadString(OracleDataReader reader, int index)
        {
            return reader.IsDBNull(index) ? string.Empty : reader.GetString(index);
        }

        // CLOB columns do not come back through GetString on every ODP.NET build,
        // so the LOB is read explicitly.
        private static string ReadClob(OracleDataReader reader, int index)
        {
            if (reader.IsDBNull(index))
            {
                return string.Empty;
            }

            using var clob = reader.GetOracleClob(index);
            return clob.IsNull ? string.Empty : clob.Value;
        }
    }

    // The three MES tables the app edits, and how a single row of each is
    // addressed. AWACSWSTYPE carries its own TBLROWID key column; the other two
    // have no natural key the app can rely on, so they are addressed by ROWID -
    // the same handle the grids already pass around.
    internal sealed record AuditedTable(string Name, string KeyPredicate)
    {
        private static readonly AuditedTable AwacsWstype =
            new(AuditedTableNames.AwacsWstype, "tblrowid = :row_key");

        private static readonly AuditedTable AwacsRecipeByWstype =
            new(AuditedTableNames.AwacsRecipeByWstype, "ROWID = CHARTOROWID(:row_key)");

        private static readonly AuditedTable AwacsLf =
            new(AuditedTableNames.AwacsLf, "ROWID = CHARTOROWID(:row_key)");

        // TBLWIREBOND does have a TBLROWID column, but it is NULL on every row
        // keyed in before the Wirebond page existed, so rows are addressed
        // by ROWID like the two above rather than by a key half the table lacks.
        private static readonly AuditedTable WireBond =
            new(AuditedTableNames.WireBond, "ROWID = CHARTOROWID(:row_key)");

        public static AuditedTable For(string tableName)
        {
            return tableName.Trim().ToUpperInvariant() switch
            {
                AuditedTableNames.AwacsWstype => AwacsWstype,
                AuditedTableNames.AwacsRecipeByWstype => AwacsRecipeByWstype,
                AuditedTableNames.AwacsLf => AwacsLf,
                AuditedTableNames.WireBond => WireBond,
                _ => throw new InvalidOperationException($"{tableName} is not one of the tables this app can restore rows into.")
            };
        }
    }
}
