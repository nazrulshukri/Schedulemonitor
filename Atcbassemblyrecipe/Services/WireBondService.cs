using Atcbassemblyrecipe.Data;
using Atcbassemblyrecipe.Infrastructure;
using Atcbassemblyrecipe.Models;
using Atcbassemblyrecipe.ViewModels;
using Oracle.ManagedDataAccess.Client;

namespace Atcbassemblyrecipe.Services
{
    public interface IWireBondService
    {
        Task<PagedResult<WireBond>> GetAsync(string? search, int page, int pageSize, string? sortBy, string? sortDirection);
        Task<IReadOnlyList<WireBond>> GetForExportAsync(string? search, string? sortBy, string? sortDirection);
        Task<IReadOnlyList<string>> GetMachineOptionsAsync();
        Task<(bool Success, string Message)> CreateAsync(WireBondInputModel model, string userName);
        Task<(bool Success, int Inserted, string Message)> CreateManyAsync(IReadOnlyList<WireBondInputModel> models, string userName);
        Task<(bool Success, string Message)> UpdateAsync(WireBondInputModel model, string userName);
        Task<(bool Success, string Message)> DeleteAsync(string tblRowId, string userName);
    }

    // TBLWIREBOND: the wirebond recipe table. Package, product, leadframe 12NC
    // and recipe, plus TBLROWID / LASTUPDATE / LASTUPDATEDBY. Same shape as
    // AWACSRECIPEBYWSTYPE minus WSTYPE - every row here is wirebond, so there is
    // nothing to filter on and no WSTYPE to carry around.
    //
    // Two things worth knowing before reading further:
    //
    //   - PACKAGE is a reserved word in Oracle. It must be written "PACKAGE",
    //     double-quoted and uppercase, everywhere it appears in SQL. Unquoted or
    //     lowercase raises ORA-00904. The bind variable is :package, which is
    //     fine - the rule is about the identifier, not the placeholder.
    //   - Rows are addressed by Oracle ROWID, not by the TBLROWID column, so a
    //     row keyed in by hand with a NULL TBLROWID is still editable. New rows
    //     still get a RAWTOHEX(SYS_GUID()) TBLROWID for the MES side.
    public class WireBondService : IWireBondService
    {
        // Order matters: ReadRow reads by index.
        private const string SelectColumns = """
            ROWIDTOCHAR(ROWID), lastupdate, lastupdatedby, wsid, "PACKAGE", product,
            leadframe12nc, recipe
            """;

        private const string FilterSql = """
            WHERE (:search IS NULL
                   OR UPPER(wsid) LIKE :search
                   OR UPPER("PACKAGE") LIKE :search
                   OR UPPER(product) LIKE :search
                   OR UPPER(leadframe12nc) LIKE :search
                   OR UPPER(recipe) LIKE :search
                   OR UPPER(lastupdatedby) LIKE :search)
            """;

        private const string InsertSql = """
            INSERT INTO tblwirebond
                (tblrowid, lastupdate, lastupdatedby, wsid, "PACKAGE", product,
                 leadframe12nc, recipe)
            VALUES
                (RAWTOHEX(SYS_GUID()), SYSDATE, :lastupdatedby, :wsid, :package, :product,
                 :leadframe12nc, :recipe)
            """;

        // Every column the grid renders a sort header for. The value reaches an
        // ORDER BY that cannot be parameterized, so this array - not the caller -
        // decides what is allowed there. Adding a header in Index.cshtml without
        // adding the key here silently sorts by lastupdate instead.
        private static readonly string[] SortableColumns =
        [
            "wsid", "package", "product", "leadframe12nc", "recipe", "lastupdatedby", "lastupdate"
        ];

        private readonly IOracleConnectionFactory _connectionFactory;
        private readonly IRecipeAuditRepository _auditRepository;

        public WireBondService(IOracleConnectionFactory connectionFactory, IRecipeAuditRepository auditRepository)
        {
            _connectionFactory = connectionFactory;
            _auditRepository = auditRepository;
        }

        public async Task<PagedResult<WireBond>> GetAsync(string? search, int page, int pageSize, string? sortBy, string? sortDirection)
        {
            var rows = new List<WireBond>();
            page = page < 1 ? 1 : page;
            pageSize = pageSize is < 10 or > 100 ? 25 : pageSize;
            var normalizedSearch = NormalizeSearch(search);
            var orderBy = BuildOrderBy(sortBy, sortDirection);

            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync();

            var totalRows = await CountAsync(connection, normalizedSearch);

            await using var command = connection.CreateCommand();
            command.BindByName = true;
            command.CommandText = $"""
                SELECT {SelectColumns}
                FROM tblwirebond
                {FilterSql}
                ORDER BY {orderBy}
                OFFSET :offset ROWS FETCH NEXT :page_size ROWS ONLY
                """;
            command.Parameters.Add(new OracleParameter("search", OracleDbType.Varchar2) { Value = (object?)normalizedSearch ?? DBNull.Value });
            command.Parameters.Add(new OracleParameter("offset", OracleDbType.Int32) { Value = (page - 1) * pageSize });
            command.Parameters.Add(new OracleParameter("page_size", OracleDbType.Int32) { Value = pageSize });

            await using var reader = await command.ExecuteReaderTracedAsync();
            while (await reader.ReadAsync())
            {
                rows.Add(ReadRow(reader));
            }

            return new PagedResult<WireBond>
            {
                Rows = rows,
                Search = search?.Trim() ?? string.Empty,
                Page = page,
                PageSize = pageSize,
                TotalRows = totalRows
            };
        }

        public async Task<IReadOnlyList<WireBond>> GetForExportAsync(string? search, string? sortBy, string? sortDirection)
        {
            var rows = new List<WireBond>();
            var normalizedSearch = NormalizeSearch(search);
            var orderBy = BuildOrderBy(sortBy, sortDirection);

            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.BindByName = true;
            command.CommandText = $"""
                SELECT {SelectColumns}
                FROM tblwirebond
                {FilterSql}
                ORDER BY {orderBy}
                """;
            command.Parameters.Add(new OracleParameter("search", OracleDbType.Varchar2) { Value = (object?)normalizedSearch ?? DBNull.Value });

            await using var reader = await command.ExecuteReaderTracedAsync();
            while (await reader.ReadAsync())
            {
                rows.Add(ReadRow(reader));
            }

            return rows;
        }

        // The wire bonders registered in AWACSWSTYPE. AWACSWSTYPE is the parent
        // table - one row per machine - and this is the list the Machine cell
        // offers.
        //
        // Swallows database errors: an unreadable AWACSWSTYPE means an empty list
        // and a grid that says so, rather than a page that will not load.
        public async Task<IReadOnlyList<string>> GetMachineOptionsAsync()
        {
            var values = new List<string>();

            try
            {
                await using var connection = _connectionFactory.CreateConnection();
                await connection.OpenAsync();

                await using var command = connection.CreateCommand();
                command.CommandText = """
                    SELECT DISTINCT wsid
                    FROM awacswstype
                    WHERE UPPER(wstype) = 'WIREBOND'
                      AND wsid IS NOT NULL
                    ORDER BY wsid
                    """;

                await using var reader = await command.ExecuteReaderTracedAsync();
                while (await reader.ReadAsync())
                {
                    if (!reader.IsDBNull(0))
                    {
                        values.Add(reader.GetString(0));
                    }
                }
            }
            catch (Exception ex) when (ex is OracleException or InvalidOperationException)
            {
                return [];
            }

            return values;
        }

        // The parent-child check. A recipe names a machine, so the machine has to
        // exist in AWACSWSTYPE as a wire bonder. Enforced here rather than only in
        // the dropdown: a POST that names an unregistered machine is refused too.
        //
        // The database has no foreign key doing this - AWACSWSTYPE has no unique
        // key on WSID to point one at. Database/tblwirebond-wsid.sql carries the
        // index and the constraint if you want the database enforcing it as well.
        private static async Task<bool> MachineIsRegisteredAsync(OracleConnection connection, OracleTransaction transaction, string wsId)
        {
            await using var command = connection.CreateCommand();
            command.BindByName = true;
            command.Transaction = transaction;
            command.CommandText = """
                SELECT COUNT(1)
                FROM awacswstype
                WHERE UPPER(wsid) = UPPER(:wsid)
                  AND UPPER(wstype) = 'WIREBOND'
                """;
            command.Parameters.Add(new OracleParameter("wsid", wsId));

            return Convert.ToInt32(await command.ExecuteScalarTracedAsync()) > 0;
        }

        private static string UnregisteredMachineMessage(string wsId)
        {
            return $"Machine {wsId} is not registered in AWACSWSTYPE as a WIREBOND workstation. "
                 + "Add it on the AWACSWSTYPE page first (Add Row, WSTYPE WIREBOND), then save this recipe.";
        }

        public async Task<(bool Success, string Message)> CreateAsync(WireBondInputModel model, string userName)
        {
            Normalize(model);

            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync();
            await using var transaction = connection.BeginTransaction();

            try
            {
                if (!await MachineIsRegisteredAsync(connection, transaction, model.WsId))
                {
                    await transaction.RollbackAsync();
                    return (false, UnregisteredMachineMessage(model.WsId));
                }

                if (await RecipeExistsAsync(connection, transaction, model))
                {
                    await transaction.RollbackAsync();
                    return (false, $"TBLWIREBOND already has {model.Product} / {model.Leadframe12Nc} / {model.Recipe}. Edit that row instead of adding a second one.");
                }

                await using var command = connection.CreateCommand();
                command.BindByName = true;
                command.Transaction = transaction;
                command.CommandText = InsertSql;
                AddWriteParameters(command, model, userName);

                var inserted = await command.ExecuteNonQueryTracedAsync();
                if (inserted != 1 || !await RecipeExistsAsync(connection, transaction, model))
                {
                    await transaction.RollbackAsync();
                    return (false, "Insert verification failed. Insert was rolled back.");
                }

                await transaction.CommitAsync();
                return (true, $"TBLWIREBOND row {model.Product} inserted and verified.");
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<(bool Success, int Inserted, string Message)> CreateManyAsync(IReadOnlyList<WireBondInputModel> models, string userName)
        {
            if (models.Count == 0)
            {
                return (false, 0, "No TBLWIREBOND rows were found to import.");
            }

            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync();
            await using var transaction = connection.BeginTransaction();

            try
            {
                var inserted = 0;
                foreach (var model in models)
                {
                    Normalize(model);

                    if (!await MachineIsRegisteredAsync(connection, transaction, model.WsId))
                    {
                        await transaction.RollbackAsync();
                        return (false, 0, $"Import cancelled. {UnregisteredMachineMessage(model.WsId)} No rows were uploaded.");
                    }

                    if (await RecipeExistsAsync(connection, transaction, model))
                    {
                        await transaction.RollbackAsync();
                        return (false, 0, $"Import cancelled. {model.Product} / {model.Leadframe12Nc} / {model.Recipe} is already in TBLWIREBOND. No rows were uploaded.");
                    }

                    await using var command = connection.CreateCommand();
                    command.BindByName = true;
                    command.Transaction = transaction;
                    command.CommandText = InsertSql;
                    AddWriteParameters(command, model, userName);

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

        public async Task<(bool Success, string Message)> UpdateAsync(WireBondInputModel model, string userName)
        {
            Normalize(model);

            if (string.IsNullOrWhiteSpace(model.TblRowId))
            {
                return (false, "Missing TBLWIREBOND row id.");
            }

            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync();
            await using var transaction = connection.BeginTransaction();

            try
            {
                if (!await RowIdExistsAsync(connection, transaction, model.TblRowId))
                {
                    await transaction.RollbackAsync();
                    return (false, "TBLWIREBOND row was not found. It may have been deleted - check Trash.");
                }

                if (!await MachineIsRegisteredAsync(connection, transaction, model.WsId))
                {
                    await transaction.RollbackAsync();
                    return (false, UnregisteredMachineMessage(model.WsId));
                }

                var oldValues = await _auditRepository.CaptureRowAsync(
                    connection, transaction, AuditedTableNames.WireBond, model.TblRowId);

                await using var command = connection.CreateCommand();
                command.BindByName = true;
                command.Transaction = transaction;
                command.CommandText = """
                    UPDATE tblwirebond
                    SET lastupdate = SYSDATE,
                        lastupdatedby = :lastupdatedby,
                        wsid = :wsid,
                        "PACKAGE" = :package,
                        product = :product,
                        leadframe12nc = :leadframe12nc,
                        recipe = :recipe
                    WHERE ROWID = CHARTOROWID(:tblrowid)
                    """;
                AddWriteParameters(command, model, userName);
                command.Parameters.Add(new OracleParameter("tblrowid", model.TblRowId));

                var updated = await command.ExecuteNonQueryTracedAsync();
                if (updated != 1 || !await RowIdExistsAsync(connection, transaction, model.TblRowId))
                {
                    await transaction.RollbackAsync();
                    return (false, "Update verification failed. Update was rolled back.");
                }

                var newValues = await _auditRepository.CaptureRowAsync(
                    connection, transaction, AuditedTableNames.WireBond, model.TblRowId);

                await _auditRepository.WriteHistoryAsync(
                    connection,
                    transaction,
                    AuditedTableNames.WireBond,
                    $"{model.Product} / {model.Leadframe12Nc}",
                    model.TblRowId,
                    oldValues,
                    newValues,
                    userName);

                await transaction.CommitAsync();
                return (true, $"TBLWIREBOND row {model.Product} updated and verified. The previous values are on the Change History page if this needs to be reverted.");
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
                    return (false, "TBLWIREBOND row was not found.");
                }

                var rowData = await _auditRepository.CaptureRowAsync(
                    connection, transaction, AuditedTableNames.WireBond, tblRowId);

                await _auditRepository.WriteTrashAsync(
                    connection,
                    transaction,
                    AuditedTableNames.WireBond,
                    $"{rowData.GetValueOrDefault("PRODUCT")} / {rowData.GetValueOrDefault("LEADFRAME12NC")}",
                    rowData,
                    userName);

                await using var command = connection.CreateCommand();
                command.BindByName = true;
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM tblwirebond WHERE ROWID = CHARTOROWID(:tblrowid)";
                command.Parameters.Add(new OracleParameter("tblrowid", tblRowId));

                var deleted = await command.ExecuteNonQueryTracedAsync();
                if (deleted != 1 || await RowIdExistsAsync(connection, transaction, tblRowId))
                {
                    await transaction.RollbackAsync();
                    return (false, "Delete verification failed. Delete was rolled back.");
                }

                await transaction.CommitAsync();
                return (true, "TBLWIREBOND row moved to Trash. Open Trash to restore it if this was a mistake.");
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        private static async Task<int> CountAsync(OracleConnection connection, string? search)
        {
            await using var command = connection.CreateCommand();
            command.BindByName = true;
            command.CommandText = $"""
                SELECT COUNT(1)
                FROM tblwirebond
                {FilterSql}
                """;
            command.Parameters.Add(new OracleParameter("search", OracleDbType.Varchar2) { Value = (object?)search ?? DBNull.Value });

            return Convert.ToInt32(await command.ExecuteScalarTracedAsync());
        }

        // Product + leadframe + recipe is what makes a row unique here. The table
        // has no unique constraint to lean on - this check is the only thing
        // standing between a double-submit and two rows nobody can tell apart.
        private static async Task<bool> RecipeExistsAsync(OracleConnection connection, OracleTransaction transaction, WireBondInputModel model)
        {
            await using var command = connection.CreateCommand();
            command.BindByName = true;
            command.Transaction = transaction;
            command.CommandText = """
                SELECT COUNT(1)
                FROM tblwirebond
                WHERE UPPER(product) = UPPER(:product)
                  AND UPPER(leadframe12nc) = UPPER(:leadframe12nc)
                  AND UPPER(recipe) = UPPER(:recipe)
                """;
            command.Parameters.Add(new OracleParameter("product", model.Product));
            command.Parameters.Add(new OracleParameter("leadframe12nc", model.Leadframe12Nc));
            command.Parameters.Add(new OracleParameter("recipe", model.Recipe));

            return Convert.ToInt32(await command.ExecuteScalarTracedAsync()) > 0;
        }

        private static async Task<bool> RowIdExistsAsync(OracleConnection connection, OracleTransaction transaction, string tblRowId)
        {
            await using var command = connection.CreateCommand();
            command.BindByName = true;
            command.Transaction = transaction;
            command.CommandText = "SELECT COUNT(1) FROM tblwirebond WHERE ROWID = CHARTOROWID(:tblrowid)";
            command.Parameters.Add(new OracleParameter("tblrowid", tblRowId));

            return Convert.ToInt32(await command.ExecuteScalarTracedAsync()) == 1;
        }

        private static void AddWriteParameters(OracleCommand command, WireBondInputModel model, string userName)
        {
            command.Parameters.Add(new OracleParameter("lastupdatedby", userName));
            command.Parameters.Add(new OracleParameter("wsid", model.WsId));
            command.Parameters.Add(Text("package", model.Package));
            command.Parameters.Add(new OracleParameter("product", model.Product));
            command.Parameters.Add(new OracleParameter("leadframe12nc", model.Leadframe12Nc));
            command.Parameters.Add(new OracleParameter("recipe", model.Recipe));
        }

        private static OracleParameter Text(string name, string? value)
        {
            return new OracleParameter(name, OracleDbType.Varchar2)
            {
                Value = (object?)value ?? DBNull.Value
            };
        }

        // Everything here is a code, not a sentence, so it is stored upper-cased:
        // the duplicate check and the search box cannot then be defeated by
        // "sot669" vs "SOT669".
        private static void Normalize(WireBondInputModel model)
        {
            model.TblRowId = model.TblRowId?.Trim();
            model.WsId = InputText.CleanUpper(model.WsId);
            model.Package = InputText.CleanUpperOrNull(model.Package);
            model.Product = InputText.CleanUpper(model.Product);
            model.Leadframe12Nc = InputText.CleanUpper(model.Leadframe12Nc);
            model.Recipe = InputText.CleanUpper(model.Recipe);
        }

        private static string BuildOrderBy(string? sortBy, string? sortDirection)
        {
            var direction = string.Equals(sortDirection, "asc", StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC";
            var nulls = direction == "ASC" ? "NULLS FIRST" : "NULLS LAST";
            var column = NormalizeSortBy(sortBy);

            return column switch
            {
                // The grid's own numbering: insertion order, as close as this table
                // gets to one without a sequence column.
                "sequence" => $"ROWIDTOCHAR(ROWID) {direction}",
                "lastupdate" => $"lastupdate {direction} {nulls}, product ASC",
                // PACKAGE stays quoted - it is a reserved word.
                "package" => $"\"PACKAGE\" {direction} {nulls}, product ASC",
                // Every other column ties on the timestamp, so equal values still
                // come back newest first instead of in whatever order Oracle chose.
                _ => $"{column} {direction} {nulls}, lastupdate DESC NULLS LAST"
            };
        }

        // Whitelist, not string concatenation - see SortableColumns.
        public static string NormalizeSortBy(string? sortBy)
        {
            var value = sortBy?.Trim().ToLowerInvariant() ?? string.Empty;

            if (value == "sequence")
            {
                return "sequence";
            }

            return Array.IndexOf(SortableColumns, value) >= 0 ? value : "lastupdate";
        }

        private static string? NormalizeSearch(string? search)
        {
            return string.IsNullOrWhiteSpace(search) ? null : $"%{search.Trim().ToUpperInvariant()}%";
        }

        private static WireBond ReadRow(OracleDataReader reader)
        {
            return new WireBond
            {
                TblRowId = ReadString(reader, 0),
                LastUpdate = ReadDate(reader, 1),
                LastUpdatedBy = ReadString(reader, 2),
                WsId = ReadString(reader, 3),
                Package = ReadString(reader, 4),
                Product = ReadString(reader, 5),
                Leadframe12Nc = ReadString(reader, 6),
                Recipe = ReadString(reader, 7)
            };
        }

        private static string ReadString(OracleDataReader reader, int index)
        {
            return reader.IsDBNull(index) ? string.Empty : reader.GetString(index);
        }

        private static DateTime? ReadDate(OracleDataReader reader, int index)
        {
            return reader.IsDBNull(index) ? null : reader.GetDateTime(index);
        }
    }
}
