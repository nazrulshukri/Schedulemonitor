using System.Collections.Concurrent;
using Oracle.ManagedDataAccess.Client;
using Atcbassemblyrecipe.Infrastructure;

namespace Atcbassemblyrecipe.Data
{
    public enum TableColumnType
    {
        Text,
        Number,
        Date
    }

    public sealed record TableColumn(string Name, TableColumnType Type);

    public interface ITableSchemaProvider
    {
        Task<IReadOnlyList<TableColumn>> GetColumnsAsync(OracleConnection connection, OracleTransaction? transaction, string tableName);
    }

    // Reads a table's real column list from the data dictionary so the recycle bin
    // and change history can snapshot and put back *every* column, not just the
    // handful a grid happens to show. AWACSWSTYPE / AWACSRECIPEBYWSTYPE / AWACSLF
    // are pre-existing MES tables this app does not own, so their shape is
    // discovered instead of hard-coded.
    //
    // Registered as a singleton: table shapes do not change while the app runs, and
    // one dictionary query per table per process is cheap enough to never repeat.
    public class TableSchemaProvider : ITableSchemaProvider
    {
        private readonly ConcurrentDictionary<string, IReadOnlyList<TableColumn>> _cache = new(StringComparer.OrdinalIgnoreCase);

        public async Task<IReadOnlyList<TableColumn>> GetColumnsAsync(OracleConnection connection, OracleTransaction? transaction, string tableName)
        {
            if (_cache.TryGetValue(tableName, out var cached))
            {
                return cached;
            }

            var columns = new List<TableColumn>();

            await using var command = connection.CreateCommand();
            command.BindByName = true;
            command.Transaction = transaction;
            // A table name can exist in more than one schema. Prefer the one this
            // session actually resolves unqualified names against, then fall back to
            // whichever other owner sorts first, so a synonym still works.
            command.CommandText = """
                SELECT column_name, data_type
                FROM (
                    SELECT column_name,
                           data_type,
                           column_id,
                           DENSE_RANK() OVER (
                               ORDER BY CASE WHEN owner = SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA') THEN 0 ELSE 1 END,
                                        owner) AS owner_rank
                    FROM all_tab_columns
                    WHERE table_name = :table_name
                )
                WHERE owner_rank = 1
                ORDER BY column_id
                """;
            command.Parameters.Add(new OracleParameter("table_name", tableName.ToUpperInvariant()));

            await using (var reader = await command.ExecuteReaderTracedAsync())
            {
                while (await reader.ReadAsync())
                {
                    var columnName = reader.GetString(0);
                    var dataType = reader.GetString(1);
                    var mapped = MapType(dataType);

                    // LOBs, RAW and the object types cannot be round-tripped through a
                    // text snapshot, so they are left out rather than restored wrong.
                    if (mapped is null)
                    {
                        continue;
                    }

                    columns.Add(new TableColumn(columnName, mapped.Value));
                }
            }

            if (columns.Count == 0)
            {
                throw new InvalidOperationException($"Table {tableName} was not found in the data dictionary, so its rows cannot be snapshotted for the recycle bin.");
            }

            _cache[tableName] = columns;
            return columns;
        }

        private static TableColumnType? MapType(string dataType)
        {
            if (dataType.StartsWith("TIMESTAMP", StringComparison.OrdinalIgnoreCase))
            {
                return TableColumnType.Date;
            }

            return dataType.ToUpperInvariant() switch
            {
                "VARCHAR2" or "NVARCHAR2" or "CHAR" or "NCHAR" => TableColumnType.Text,
                "NUMBER" or "FLOAT" or "BINARY_FLOAT" or "BINARY_DOUBLE" => TableColumnType.Number,
                "DATE" => TableColumnType.Date,
                _ => null
            };
        }
    }
}
