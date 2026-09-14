using Atcbassemblyrecipe.Data;
using Atcbassemblyrecipe.Infrastructure;
using Atcbassemblyrecipe.Models;
using Atcbassemblyrecipe.ViewModels;
using Oracle.ManagedDataAccess.Client;

namespace Atcbassemblyrecipe.Services
{
    public interface IEngineeringService
    {
        Task<PagedResult<Engineering>> GetAsync(string? search, int page, int pageSize, string? sortBy, string? sortDirection);
        Task<IReadOnlyList<Engineering>> GetForExportAsync(string? search, string? sortBy, string? sortDirection);
        Task<(bool Success, string Message)> CreateAsync(EngineeringInputModel model, string userName);
        Task<(bool Success, int Inserted, string Message)> CreateManyAsync(IReadOnlyList<EngineeringInputModel> models, string userName);
        Task<(bool Success, string Message)> UpdateAsync(EngineeringInputModel model, string userName);
        Task<(bool Success, string Message)> DeleteAsync(string tblRowId, string userName);
        Task<string> NormalizeSortByAsync(string? sortBy);
    }

    // OCAPSYS.ENGINEERING: one row per engineering lot, one column per process
    // step's recipe.
    //
    // Unlike every other grid in this app, the column list is not fixed in code.
    // It comes from IEngineeringColumnGroupProvider, which reads
    // ENGINEERINGCOLUMNGROUP and then narrows the result to the groups the
    // *signed-in user* holds a TBLACCESS grant for. That one decision runs
    // through everything here:
    //
    //   * SELECT lists only the user's columns, so a sawing engineer's page
    //     never even fetches the marker recipes.
    //   * Search matches only the user's columns - searching cannot be used to
    //     probe a column the page will not show.
    //   * INSERT and UPDATE write only the user's columns. A posted cell naming
    //     a column outside their groups is dropped, so a hand-made POST cannot
    //     reach past the page, and an UPDATE leaves the other groups' recipes
    //     exactly as they were.
    //
    // Column names reach the SQL as identifiers, which cannot be bound as
    // parameters. They are safe because they never come from the request: the
    // provider has already checked every one of them against the real shape of
    // ENGINEERING in the data dictionary and dropped anything that is not a
    // plain identifier. Values are always bound.
    //
    // "NO" and "PACKAGE" are reserved words in Oracle and are written
    // double-quoted and uppercase throughout - see EngineeringColumns.Quote.
    // Rows are addressed by Oracle ROWID, like AWACSLF and TBLWIREBOND.
    public class EngineeringService : IEngineeringService
    {
        private const string Table = "engineering";

        // Sort keys that are not one of the data columns.
        private static readonly string[] FixedSortableColumns =
        [
            "lastupdatedby", "lastupdate"
        ];

        private readonly IOracleConnectionFactory _connectionFactory;
        private readonly IRecipeAuditRepository _auditRepository;
        private readonly IEngineeringColumnGroupProvider _columnGroups;

        public EngineeringService(
            IOracleConnectionFactory connectionFactory,
            IRecipeAuditRepository auditRepository,
            IEngineeringColumnGroupProvider columnGroups)
        {
            _connectionFactory = connectionFactory;
            _auditRepository = auditRepository;
            _columnGroups = columnGroups;
        }

        public async Task<PagedResult<Engineering>> GetAsync(string? search, int page, int pageSize, string? sortBy, string? sortDirection)
        {
            var columns = await _columnGroups.GetVisibleAsync();
            var rows = new List<Engineering>();
            page = page < 1 ? 1 : page;
            pageSize = pageSize is < 10 or > 100 ? 25 : pageSize;
            var normalizedSearch = NormalizeSearch(search);
            var orderBy = BuildOrderBy(columns, sortBy, sortDirection);

            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync();

            var totalRows = await CountAsync(connection, columns, normalizedSearch);

            await using var command = connection.CreateCommand();
            command.BindByName = true;
            command.CommandText = $"""
                SELECT {SelectList(columns)}
                FROM   {Table}
                {FilterSql(columns)}
                ORDER BY {orderBy}
                OFFSET :offset ROWS FETCH NEXT :page_size ROWS ONLY
                """;
            command.Parameters.Add(new OracleParameter("search", OracleDbType.Varchar2) { Value = (object?)normalizedSearch ?? DBNull.Value });
            command.Parameters.Add(new OracleParameter("offset", OracleDbType.Int32) { Value = (page - 1) * pageSize });
            command.Parameters.Add(new OracleParameter("page_size", OracleDbType.Int32) { Value = pageSize });

            await using var reader = await command.ExecuteReaderTracedAsync();
            while (await reader.ReadAsync())
            {
                rows.Add(ReadRow(reader, columns));
            }

            return new PagedResult<Engineering>
            {
                Rows = rows,
                Search = search?.Trim() ?? string.Empty,
                Page = page,
                PageSize = pageSize,
                TotalRows = totalRows
            };
        }

        public async Task<IReadOnlyList<Engineering>> GetForExportAsync(string? search, string? sortBy, string? sortDirection)
        {
            var columns = await _columnGroups.GetVisibleAsync();
            var rows = new List<Engineering>();
            var normalizedSearch = NormalizeSearch(search);

            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.BindByName = true;
            command.CommandText = $"""
                SELECT {SelectList(columns)}
                FROM   {Table}
                {FilterSql(columns)}
                ORDER BY {BuildOrderBy(columns, sortBy, sortDirection)}
                """;
            command.Parameters.Add(new OracleParameter("search", OracleDbType.Varchar2) { Value = (object?)normalizedSearch ?? DBNull.Value });

            await using var reader = await command.ExecuteReaderTracedAsync();
            while (await reader.ReadAsync())
            {
                rows.Add(ReadRow(reader, columns));
            }

            return rows;
        }

        public async Task<(bool Success, string Message)> CreateAsync(EngineeringInputModel model, string userName)
        {
            var columns = await WritableColumnsAsync();
            var values = Normalize(model, columns);

            if (!values.TryGetValue(EngineeringColumns.LotNumber, out var lotNumber) || string.IsNullOrWhiteSpace(lotNumber))
            {
                return (false, "Lot Number is required.");
            }

            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync();
            await using var transaction = connection.BeginTransaction();

            try
            {
                if (await LotExistsAsync(connection, transaction, lotNumber!, null))
                {
                    await transaction.RollbackAsync();
                    return (false, $"ENGINEERING already has lot {lotNumber}. Edit that row instead of adding a second one.");
                }

                await using var command = connection.CreateCommand();
                command.BindByName = true;
                command.Transaction = transaction;

                var names = values.Keys.ToList();
                var insertColumns = string.Join(", ", names.Select(EngineeringColumns.Quote));
                var insertBinds = string.Join(", ", names.Select(name => ":" + Bind(name)));

                command.CommandText = $"""
                    INSERT INTO {Table}
                        (tblrowid, lastupdate, lastupdatedby{(names.Count == 0 ? string.Empty : ", " + insertColumns)})
                    VALUES
                        (RAWTOHEX(SYS_GUID()), SYSDATE, :lastupdatedby{(names.Count == 0 ? string.Empty : ", " + insertBinds)})
                    """;
                command.Parameters.Add(new OracleParameter("lastupdatedby", userName));
                AddValueParameters(command, values);

                var inserted = await command.ExecuteNonQueryTracedAsync();
                if (inserted != 1 || !await LotExistsAsync(connection, transaction, lotNumber!, null))
                {
                    await transaction.RollbackAsync();
                    return (false, "Insert verification failed. Insert was rolled back.");
                }

                await transaction.CommitAsync();
                return (true, $"ENGINEERING lot {lotNumber} inserted and verified.");
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<(bool Success, int Inserted, string Message)> CreateManyAsync(IReadOnlyList<EngineeringInputModel> models, string userName)
        {
            if (models.Count == 0)
            {
                return (false, 0, "No ENGINEERING rows were found to import.");
            }

            var columns = await WritableColumnsAsync();

            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync();
            await using var transaction = connection.BeginTransaction();

            try
            {
                var inserted = 0;
                foreach (var model in models)
                {
                    var values = Normalize(model, columns);
                    var lotNumber = values.GetValueOrDefault(EngineeringColumns.LotNumber);

                    if (string.IsNullOrWhiteSpace(lotNumber))
                    {
                        await transaction.RollbackAsync();
                        return (false, 0, "Import cancelled. A row has no LOTNUMBER. No rows were uploaded.");
                    }

                    if (await LotExistsAsync(connection, transaction, lotNumber!, null))
                    {
                        await transaction.RollbackAsync();
                        return (false, 0, $"Import cancelled. Lot {lotNumber} is already in ENGINEERING. No rows were uploaded.");
                    }

                    await using var command = connection.CreateCommand();
                    command.BindByName = true;
                    command.Transaction = transaction;

                    var names = values.Keys.ToList();
                    command.CommandText = $"""
                        INSERT INTO {Table}
                            (tblrowid, lastupdate, lastupdatedby, {string.Join(", ", names.Select(EngineeringColumns.Quote))})
                        VALUES
                            (RAWTOHEX(SYS_GUID()), SYSDATE, :lastupdatedby, {string.Join(", ", names.Select(name => ":" + Bind(name)))})
                        """;
                    command.Parameters.Add(new OracleParameter("lastupdatedby", userName));
                    AddValueParameters(command, values);

                    if (await command.ExecuteNonQueryTracedAsync() != 1)
                    {
                        await transaction.RollbackAsync();
                        return (false, 0, "Import cancelled. Insert verification failed. No rows were uploaded.");
                    }

                    inserted++;
                }

                await transaction.CommitAsync();
                return (true, inserted, $"CSV import complete. Inserted: {inserted}. Skipped/failed: 0.");
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<(bool Success, string Message)> UpdateAsync(EngineeringInputModel model, string userName)
        {
            var columns = await WritableColumnsAsync();
            var values = Normalize(model, columns);

            if (string.IsNullOrWhiteSpace(model.TblRowId))
            {
                return (false, "Missing ENGINEERING row id.");
            }

            if (values.Count == 0)
            {
                return (false, "Nothing to save: none of the posted cells belong to a column your groups can edit.");
            }

            var tblRowId = model.TblRowId.Trim();

            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync();
            await using var transaction = connection.BeginTransaction();

            try
            {
                if (!await RowIdExistsAsync(connection, transaction, tblRowId))
                {
                    await transaction.RollbackAsync();
                    return (false, "ENGINEERING row was not found. It may have been deleted - check Trash.");
                }

                var lotNumber = values.GetValueOrDefault(EngineeringColumns.LotNumber);
                if (!string.IsNullOrWhiteSpace(lotNumber)
                    && await LotExistsAsync(connection, transaction, lotNumber!, tblRowId))
                {
                    await transaction.RollbackAsync();
                    return (false, $"Another ENGINEERING row already carries lot {lotNumber}.");
                }

                // The whole row, before the change - every column, not just the
                // ones this user can see, so a revert from Change History puts
                // back the other groups' recipes untouched.
                var oldValues = await _auditRepository.CaptureRowAsync(
                    connection, transaction, AuditedTableNames.Engineering, tblRowId);

                await using var command = connection.CreateCommand();
                command.BindByName = true;
                command.Transaction = transaction;

                var assignments = string.Join(",\n                            ",
                    values.Keys.Select(name => $"{EngineeringColumns.Quote(name)} = :{Bind(name)}"));

                command.CommandText = $"""
                    UPDATE {Table}
                       SET lastupdate = SYSDATE,
                           lastupdatedby = :lastupdatedby,
                           {assignments}
                     WHERE ROWID = CHARTOROWID(:tblrowid)
                    """;
                command.Parameters.Add(new OracleParameter("lastupdatedby", userName));
                AddValueParameters(command, values);
                command.Parameters.Add(new OracleParameter("tblrowid", tblRowId));

                var updated = await command.ExecuteNonQueryTracedAsync();
                if (updated != 1 || !await RowIdExistsAsync(connection, transaction, tblRowId))
                {
                    await transaction.RollbackAsync();
                    return (false, "Update verification failed. Update was rolled back.");
                }

                var newValues = await _auditRepository.CaptureRowAsync(
                    connection, transaction, AuditedTableNames.Engineering, tblRowId);

                await _auditRepository.WriteHistoryAsync(
                    connection,
                    transaction,
                    AuditedTableNames.Engineering,
                    oldValues.GetValueOrDefault(EngineeringColumns.LotNumber) ?? lotNumber ?? tblRowId,
                    tblRowId,
                    oldValues,
                    newValues,
                    userName);

                await transaction.CommitAsync();
                return (true, $"ENGINEERING lot {lotNumber ?? oldValues.GetValueOrDefault(EngineeringColumns.LotNumber)} updated and verified. The previous values are on the Change History page if this needs to be reverted.");
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<(bool Success, string Message)> DeleteAsync(string tblRowId, string userName)
        {
            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync();
            await using var transaction = connection.BeginTransaction();

            try
            {
                if (!await RowIdExistsAsync(connection, transaction, tblRowId))
                {
                    await transaction.RollbackAsync();
                    return (false, "ENGINEERING row was not found.");
                }

                var rowData = await _auditRepository.CaptureRowAsync(
                    connection, transaction, AuditedTableNames.Engineering, tblRowId);

                await _auditRepository.WriteTrashAsync(
                    connection,
                    transaction,
                    AuditedTableNames.Engineering,
                    rowData.GetValueOrDefault(EngineeringColumns.LotNumber) ?? tblRowId,
                    rowData,
                    userName);

                await using var command = connection.CreateCommand();
                command.BindByName = true;
                command.Transaction = transaction;
                command.CommandText = $"DELETE FROM {Table} WHERE ROWID = CHARTOROWID(:tblrowid)";
                command.Parameters.Add(new OracleParameter("tblrowid", tblRowId));

                var deleted = await command.ExecuteNonQueryTracedAsync();
                if (deleted != 1 || await RowIdExistsAsync(connection, transaction, tblRowId))
                {
                    await transaction.RollbackAsync();
                    return (false, "Delete verification failed. Delete was rolled back.");
                }

                await transaction.CommitAsync();
                return (true, "ENGINEERING row moved to Trash. Open Trash to restore it if this was a mistake.");
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        // The sort key the grid asked for, if it is one this user may sort by.
        // Anything else falls back to lastupdate - the value reaches an ORDER BY
        // that cannot be parameterized, so it is never taken on trust.
        public async Task<string> NormalizeSortByAsync(string? sortBy)
        {
            var columns = await _columnGroups.GetVisibleAsync();
            return NormalizeSortBy(columns, sortBy);
        }

        private async Task<IReadOnlyList<EngineeringColumn>> WritableColumnsAsync()
        {
            return await _columnGroups.GetVisibleAsync();
        }

        // Index 0 is the ROWID, 1 the timestamp, 2 the user; the data columns
        // follow in the order the provider handed them over, and ReadRow reads
        // them back by the same order.
        private static string SelectList(IReadOnlyList<EngineeringColumn> columns)
        {
            var names = columns.Select(column => EngineeringColumns.Quote(column.Name));
            return string.Join(", ", new[] { "ROWIDTOCHAR(ROWID)", "lastupdate", "lastupdatedby" }.Concat(names));
        }

        private static string FilterSql(IReadOnlyList<EngineeringColumn> columns)
        {
            // TO_CHAR so the numeric NO column can be searched with the same
            // LIKE as everything else instead of raising ORA-01722.
            var terms = columns
                .Select(column => $"UPPER(TO_CHAR({EngineeringColumns.Quote(column.Name)})) LIKE :search")
                .Append("UPPER(lastupdatedby) LIKE :search");

            return $"""
                WHERE (:search IS NULL
                       OR {string.Join("\n                       OR ", terms)})
                """;
        }

        private static async Task<int> CountAsync(OracleConnection connection, IReadOnlyList<EngineeringColumn> columns, string? search)
        {
            await using var command = connection.CreateCommand();
            command.BindByName = true;
            command.CommandText = $"""
                SELECT COUNT(1)
                FROM   {Table}
                {FilterSql(columns)}
                """;
            command.Parameters.Add(new OracleParameter("search", OracleDbType.Varchar2) { Value = (object?)search ?? DBNull.Value });

            return Convert.ToInt32(await command.ExecuteScalarTracedAsync());
        }

        // The lot number is what the MES side matches a WOID against, so two
        // rows carrying the same one would make the recipe lookup ambiguous -
        // whichever Oracle returned first would win. excludeRowId lets an edit
        // keep its own lot number without tripping over itself.
        private static async Task<bool> LotExistsAsync(
            OracleConnection connection,
            OracleTransaction transaction,
            string lotNumber,
            string? excludeRowId)
        {
            await using var command = connection.CreateCommand();
            command.BindByName = true;
            command.Transaction = transaction;
            command.CommandText = $"""
                SELECT COUNT(1)
                FROM   {Table}
                WHERE  UPPER(lotnumber) = UPPER(:lotnumber)
                {(excludeRowId is null ? string.Empty : "AND ROWID <> CHARTOROWID(:exclude_row_id)")}
                """;
            command.Parameters.Add(new OracleParameter("lotnumber", lotNumber));
            if (excludeRowId is not null)
            {
                command.Parameters.Add(new OracleParameter("exclude_row_id", excludeRowId));
            }

            return Convert.ToInt32(await command.ExecuteScalarTracedAsync()) > 0;
        }

        private static async Task<bool> RowIdExistsAsync(OracleConnection connection, OracleTransaction transaction, string tblRowId)
        {
            await using var command = connection.CreateCommand();
            command.BindByName = true;
            command.Transaction = transaction;
            command.CommandText = $"SELECT COUNT(1) FROM {Table} WHERE ROWID = CHARTOROWID(:tblrowid)";
            command.Parameters.Add(new OracleParameter("tblrowid", tblRowId));

            return Convert.ToInt32(await command.ExecuteScalarTracedAsync()) == 1;
        }

        // The posted cells, narrowed to the columns this user's groups own and
        // cleaned the way every other grid cleans its input. A key the user has
        // no group for is dropped here and nowhere else - this is the check that
        // makes the page's column hiding an actual permission rather than a
        // decoration.
        private static Dictionary<string, string?> Normalize(EngineeringInputModel model, IReadOnlyList<EngineeringColumn> columns)
        {
            var allowed = columns.ToDictionary(column => column.Name, StringComparer.OrdinalIgnoreCase);
            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            foreach (var pair in model.Values)
            {
                if (!allowed.TryGetValue(pair.Key.Trim(), out var column))
                {
                    continue;
                }

                // Requestor is a person's name, so it keeps its capitals.
                // Everything else is a code and is stored upper-cased, so the
                // duplicate check and the search box cannot be defeated by
                // "engxta54780b" vs "ENGXTA54780B".
                values[column.Name] = string.Equals(column.Name, EngineeringColumns.Requestor, StringComparison.OrdinalIgnoreCase)
                    ? InputText.CleanOrNull(pair.Value)
                    : InputText.CleanUpperOrNull(pair.Value);
            }

            return values;
        }

        private static void AddValueParameters(OracleCommand command, Dictionary<string, string?> values)
        {
            foreach (var pair in values)
            {
                if (EngineeringColumns.IsNumeric(pair.Key))
                {
                    command.Parameters.Add(new OracleParameter(Bind(pair.Key), OracleDbType.Int64)
                    {
                        Value = long.TryParse(pair.Value, out var number) ? number : DBNull.Value
                    });
                    continue;
                }

                command.Parameters.Add(new OracleParameter(Bind(pair.Key), OracleDbType.Varchar2)
                {
                    Value = (object?)pair.Value ?? DBNull.Value
                });
            }
        }

        // A bind variable cannot be named after an Oracle reserved word without
        // being quoted, and :"NO" is not worth the trouble - so every bind gets
        // a prefix instead. The identifier in the statement is still the real
        // column name.
        private static string Bind(string columnName) => "v_" + columnName.ToLowerInvariant();

        private static string BuildOrderBy(IReadOnlyList<EngineeringColumn> columns, string? sortBy, string? sortDirection)
        {
            var direction = string.Equals(sortDirection, "asc", StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC";
            var nulls = direction == "ASC" ? "NULLS FIRST" : "NULLS LAST";
            var column = NormalizeSortBy(columns, sortBy);

            if (column == "sequence")
            {
                return $"ROWIDTOCHAR(ROWID) {direction}";
            }

            if (column == "lastupdate")
            {
                return $"lastupdate {direction} {nulls}, lotnumber ASC";
            }

            if (column == "lastupdatedby")
            {
                return $"lastupdatedby {direction} {nulls}, lastupdate DESC NULLS LAST";
            }

            var match = columns.First(candidate => string.Equals(candidate.Name, column, StringComparison.OrdinalIgnoreCase));
            return $"{EngineeringColumns.Quote(match.Name)} {direction} {nulls}, lastupdate DESC NULLS LAST";
        }

        private static string NormalizeSortBy(IReadOnlyList<EngineeringColumn> columns, string? sortBy)
        {
            var value = sortBy?.Trim().ToLowerInvariant() ?? string.Empty;

            if (value == "sequence" || FixedSortableColumns.Contains(value))
            {
                return value;
            }

            var match = columns.FirstOrDefault(column => string.Equals(column.Name, value, StringComparison.OrdinalIgnoreCase));
            return match is null ? "lastupdate" : match.Name;
        }

        private static string? NormalizeSearch(string? search)
        {
            return string.IsNullOrWhiteSpace(search) ? null : $"%{search.Trim().ToUpperInvariant()}%";
        }

        private static Engineering ReadRow(OracleDataReader reader, IReadOnlyList<EngineeringColumn> columns)
        {
            var row = new Engineering
            {
                TblRowId = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                LastUpdate = reader.IsDBNull(1) ? null : reader.GetDateTime(1),
                LastUpdatedBy = reader.IsDBNull(2) ? string.Empty : reader.GetString(2)
            };

            for (var index = 0; index < columns.Count; index++)
            {
                var ordinal = index + 3;
                row.Values[columns[index].Name] = reader.IsDBNull(ordinal)
                    ? string.Empty
                    : Convert.ToString(reader.GetValue(ordinal))?.Trim() ?? string.Empty;
            }

            return row;
        }
    }
}
