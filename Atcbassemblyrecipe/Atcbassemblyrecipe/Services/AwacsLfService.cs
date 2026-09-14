using Atcbassemblyrecipe.Data;
using Atcbassemblyrecipe.Models;
using Atcbassemblyrecipe.ViewModels;
using Oracle.ManagedDataAccess.Client;
using Atcbassemblyrecipe.Infrastructure;

namespace Atcbassemblyrecipe.Services
{
    public interface IAwacsLfService
    {
        Task<PagedResult<AwacsLf>> GetAsync(string? search, int page, int pageSize, string? sortBy, string? sortDirection);
        Task<IReadOnlyList<AwacsLf>> GetForExportAsync(string? search, string? sortBy, string? sortDirection);
        Task<AwacsLfInputModel?> GetForEditAsync(string tblRowId);
        Task<(bool Success, string Message)> CreateAsync(AwacsLfInputModel model, string userName);
        Task<(bool Success, int Inserted, string Message)> CreateManyAsync(IReadOnlyList<AwacsLfInputModel> models, string userName);
        Task<(bool Success, string Message)> UpdateAsync(AwacsLfInputModel model, string userName);
        Task<(bool Success, string Message)> DeleteAsync(string tblRowId, string userName);
    }

    // AWACSLF is the leadframe master: one row per leadframe 12NC with the panel
    // size, the default work-order quantity, and the package/device it belongs to.
    //
    // Like AWACSRECIPEBYWSTYPE it has no key column the app can rely on, so a row
    // is addressed by its Oracle ROWID, handed to the page as TblRowId.
    public class AwacsLfService : IAwacsLfService
    {
        private const string SelectColumns =
            "ROWIDTOCHAR(ROWID), lastupdate, lastupdatedby, lf12nc, lfsize, defaultwoqty, \"PACKAGE\", device";

        private const string FilterSql = """
            WHERE (:search IS NULL
                   OR UPPER(lf12nc) LIKE :search
                   OR UPPER(lfsize) LIKE :search
                   OR UPPER("PACKAGE") LIKE :search
                   OR UPPER(device) LIKE :search
                   OR UPPER(lastupdatedby) LIKE :search)
            """;

        private readonly IOracleConnectionFactory _connectionFactory;
        private readonly IRecipeAuditRepository _auditRepository;

        public AwacsLfService(IOracleConnectionFactory connectionFactory, IRecipeAuditRepository auditRepository)
        {
            _connectionFactory = connectionFactory;
            _auditRepository = auditRepository;
        }

        public async Task<PagedResult<AwacsLf>> GetAsync(string? search, int page, int pageSize, string? sortBy, string? sortDirection)
        {
            var rows = new List<AwacsLf>();
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
                FROM awacslf
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

            return new PagedResult<AwacsLf>
            {
                Rows = rows,
                Search = search?.Trim() ?? string.Empty,
                Page = page,
                PageSize = pageSize,
                TotalRows = totalRows
            };
        }

        public async Task<IReadOnlyList<AwacsLf>> GetForExportAsync(string? search, string? sortBy, string? sortDirection)
        {
            var rows = new List<AwacsLf>();
            var normalizedSearch = NormalizeSearch(search);
            var orderBy = BuildOrderBy(sortBy, sortDirection);

            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.BindByName = true;
            command.CommandText = $"""
                SELECT {SelectColumns}
                FROM awacslf
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

        public async Task<AwacsLfInputModel?> GetForEditAsync(string tblRowId)
        {
            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.BindByName = true;
            command.CommandText = """
                SELECT ROWIDTOCHAR(ROWID), lf12nc, lfsize, defaultwoqty, "PACKAGE", device
                FROM awacslf
                WHERE ROWID = CHARTOROWID(:tblrowid)
                """;
            command.Parameters.Add(new OracleParameter("tblrowid", tblRowId));

            await using var reader = await command.ExecuteReaderTracedAsync();
            if (!await reader.ReadAsync())
            {
                return null;
            }

            return new AwacsLfInputModel
            {
                TblRowId = ReadString(reader, 0),
                Lf12Nc = ReadString(reader, 1),
                LfSize = ReadString(reader, 2),
                DefaultWoQty = reader.IsDBNull(3) ? null : reader.GetDecimal(3),
                Package = ReadString(reader, 4),
                Device = ReadString(reader, 5)
            };
        }

        public async Task<(bool Success, string Message)> CreateAsync(AwacsLfInputModel model, string userName)
        {
            Normalize(model);

            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync();
            await using var transaction = connection.BeginTransaction();

            try
            {
                if (await RowExistsAsync(connection, transaction, model))
                {
                    await transaction.RollbackAsync();
                    return (false, $"AWACSLF already has an identical row for LF 12NC {model.Lf12Nc}.");
                }

                await using var command = connection.CreateCommand();
                command.BindByName = true;
                command.Transaction = transaction;
                command.CommandText = InsertSql;
                AddWriteParameters(command, model, userName);

                var inserted = await command.ExecuteNonQueryTracedAsync();
                if (inserted != 1 || !await RowExistsAsync(connection, transaction, model))
                {
                    await transaction.RollbackAsync();
                    return (false, "Insert verification failed. Insert was rolled back.");
                }

                await transaction.CommitAsync();
                return (true, $"AWACSLF row inserted and verified. LFSIZE saved as {model.LfSize}.");
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<(bool Success, int Inserted, string Message)> CreateManyAsync(IReadOnlyList<AwacsLfInputModel> models, string userName)
        {
            if (models.Count == 0)
            {
                return (false, 0, "No AWACSLF rows were found to import.");
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

                    if (await RowExistsAsync(connection, transaction, model))
                    {
                        await transaction.RollbackAsync();
                        return (false, 0, $"Import cancelled. Duplicate AWACSLF row found: {model.Lf12Nc} / {model.LfSize}. No rows were uploaded.");
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

        public async Task<(bool Success, string Message)> UpdateAsync(AwacsLfInputModel model, string userName)
        {
            Normalize(model);

            if (string.IsNullOrWhiteSpace(model.TblRowId))
            {
                return (false, "Missing AWACSLF row id.");
            }

            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync();
            await using var transaction = connection.BeginTransaction();

            try
            {
                if (!await RowIdExistsAsync(connection, transaction, model.TblRowId))
                {
                    await transaction.RollbackAsync();
                    return (false, "AWACSLF row was not found. It may have been deleted - check Trash.");
                }

                var oldValues = await _auditRepository.CaptureRowAsync(
                    connection, transaction, AuditedTableNames.AwacsLf, model.TblRowId);

                await using var command = connection.CreateCommand();
                command.BindByName = true;
                command.Transaction = transaction;
                command.CommandText = """
                    UPDATE awacslf
                    SET lastupdate = SYSDATE,
                        lastupdatedby = :lastupdatedby,
                        lf12nc = :lf12nc,
                        lfsize = :lfsize,
                        defaultwoqty = :defaultwoqty,
                        "PACKAGE" = :package,
                        device = :device
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
                    connection, transaction, AuditedTableNames.AwacsLf, model.TblRowId);

                await _auditRepository.WriteHistoryAsync(
                    connection,
                    transaction,
                    AuditedTableNames.AwacsLf,
                    $"LF {model.Lf12Nc} ({model.LfSize})",
                    model.TblRowId,
                    oldValues,
                    newValues,
                    userName);

                await transaction.CommitAsync();
                return (true, "AWACSLF row updated and verified. The previous values are on the Change History page if this needs to be reverted.");
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
                    return (false, "AWACSLF row was not found.");
                }

                var rowData = await _auditRepository.CaptureRowAsync(
                    connection, transaction, AuditedTableNames.AwacsLf, tblRowId);

                await _auditRepository.WriteTrashAsync(
                    connection,
                    transaction,
                    AuditedTableNames.AwacsLf,
                    $"LF {rowData.GetValueOrDefault("LF12NC")} ({rowData.GetValueOrDefault("LFSIZE")})",
                    rowData,
                    userName);

                await using var command = connection.CreateCommand();
                command.BindByName = true;
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM awacslf WHERE ROWID = CHARTOROWID(:tblrowid)";
                command.Parameters.Add(new OracleParameter("tblrowid", tblRowId));

                var deleted = await command.ExecuteNonQueryTracedAsync();
                if (deleted != 1 || await RowIdExistsAsync(connection, transaction, tblRowId))
                {
                    await transaction.RollbackAsync();
                    return (false, "Delete verification failed. Delete was rolled back.");
                }

                await transaction.CommitAsync();
                return (true, "AWACSLF row moved to Trash. Open Trash to restore it if this was a mistake.");
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        private const string InsertSql = """
            INSERT INTO awacslf (lastupdate, lastupdatedby, lf12nc, lfsize, defaultwoqty, "PACKAGE", device)
            VALUES (SYSDATE, :lastupdatedby, :lf12nc, :lfsize, :defaultwoqty, :package, :device)
            """;

        private static async Task<int> CountAsync(OracleConnection connection, string? search)
        {
            await using var command = connection.CreateCommand();
            command.BindByName = true;
            command.CommandText = $"""
                SELECT COUNT(1)
                FROM awacslf
                {FilterSql}
                """;
            command.Parameters.Add(new OracleParameter("search", OracleDbType.Varchar2) { Value = (object?)search ?? DBNull.Value });

            return Convert.ToInt32(await command.ExecuteScalarTracedAsync());
        }

        // Blocks only a row that is identical on every column the page writes. A
        // leadframe 12NC can legitimately appear more than once (different device or
        // package), so matching on LF12NC alone would refuse valid data.
        private static async Task<bool> RowExistsAsync(OracleConnection connection, OracleTransaction transaction, AwacsLfInputModel model)
        {
            await using var command = connection.CreateCommand();
            command.BindByName = true;
            command.Transaction = transaction;
            command.CommandText = """
                SELECT COUNT(1)
                FROM awacslf
                WHERE lf12nc = :lf12nc
                  AND lfsize = :lfsize
                  AND NVL("PACKAGE", '~') = NVL(:package, '~')
                  AND NVL(device, '~') = NVL(:device, '~')
                """;
            command.Parameters.Add(new OracleParameter("lf12nc", model.Lf12Nc));
            command.Parameters.Add(new OracleParameter("lfsize", model.LfSize));
            command.Parameters.Add(new OracleParameter("package", (object?)model.Package ?? DBNull.Value));
            command.Parameters.Add(new OracleParameter("device", (object?)model.Device ?? DBNull.Value));

            return Convert.ToInt32(await command.ExecuteScalarTracedAsync()) > 0;
        }

        private static async Task<bool> RowIdExistsAsync(OracleConnection connection, OracleTransaction transaction, string tblRowId)
        {
            await using var command = connection.CreateCommand();
            command.BindByName = true;
            command.Transaction = transaction;
            command.CommandText = "SELECT COUNT(1) FROM awacslf WHERE ROWID = CHARTOROWID(:tblrowid)";
            command.Parameters.Add(new OracleParameter("tblrowid", tblRowId));

            return Convert.ToInt32(await command.ExecuteScalarTracedAsync()) == 1;
        }

        private static void AddWriteParameters(OracleCommand command, AwacsLfInputModel model, string userName)
        {
            command.Parameters.Add(new OracleParameter("lastupdatedby", userName));
            command.Parameters.Add(new OracleParameter("lf12nc", model.Lf12Nc));
            command.Parameters.Add(new OracleParameter("lfsize", model.LfSize));
            command.Parameters.Add(new OracleParameter("defaultwoqty", OracleDbType.Decimal)
            {
                Value = model.DefaultWoQty.HasValue ? (object)model.DefaultWoQty.Value : DBNull.Value
            });
            command.Parameters.Add(new OracleParameter("package", (object?)model.Package ?? DBNull.Value));
            command.Parameters.Add(new OracleParameter("device", (object?)model.Device ?? DBNull.Value));
        }

        private static void Normalize(AwacsLfInputModel model)
        {
            model.TblRowId = model.TblRowId?.Trim();
            model.Lf12Nc = InputText.CleanUpper(model.Lf12Nc);
            model.LfSize = AwacsLfInputModel.NormalizeLfSize(model.LfSize);
            model.Package = InputText.CleanUpperOrNull(model.Package);
            model.Device = InputText.CleanUpperOrNull(model.Device);
        }

        private static string BuildOrderBy(string? sortBy, string? sortDirection)
        {
            var direction = string.Equals(sortDirection, "asc", StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC";
            var nulls = direction == "ASC" ? "NULLS FIRST" : "NULLS LAST";

            return NormalizeSortBy(sortBy) switch
            {
                "sequence" => $"ROWIDTOCHAR(ROWID) {direction}",
                "lf12nc" => $"lf12nc {direction}, lastupdate DESC NULLS LAST",
                "lfsize" => $"lfsize {direction}, lf12nc ASC",
                "defaultwoqty" => $"defaultwoqty {direction} {nulls}, lf12nc ASC",
                "package" => $"\"PACKAGE\" {direction} {nulls}, lf12nc ASC",
                "device" => $"device {direction} {nulls}, lf12nc ASC",
                "lastupdatedby" => $"lastupdatedby {direction}, lf12nc ASC",
                "lastupdate" => $"lastupdate {direction} {nulls}, lf12nc ASC",
                _ => "lastupdate DESC NULLS LAST, lf12nc ASC"
            };
        }

        public static string NormalizeSortBy(string? sortBy)
        {
            return sortBy?.Trim().ToLowerInvariant() switch
            {
                "sequence" => "sequence",
                "lf12nc" => "lf12nc",
                "lfsize" => "lfsize",
                "defaultwoqty" => "defaultwoqty",
                "package" => "package",
                "device" => "device",
                "lastupdatedby" => "lastupdatedby",
                "lastupdate" => "lastupdate",
                _ => "lastupdate"
            };
        }

        private static string? NormalizeSearch(string? search)
        {
            return string.IsNullOrWhiteSpace(search) ? null : $"%{search.Trim().ToUpperInvariant()}%";
        }

        private static AwacsLf ReadRow(OracleDataReader reader)
        {
            return new AwacsLf
            {
                TblRowId = ReadString(reader, 0),
                LastUpdate = reader.IsDBNull(1) ? null : reader.GetDateTime(1),
                LastUpdatedBy = ReadString(reader, 2),
                Lf12Nc = ReadString(reader, 3),
                LfSize = ReadString(reader, 4),
                DefaultWoQty = reader.IsDBNull(5) ? null : reader.GetDecimal(5),
                Package = ReadString(reader, 6),
                Device = ReadString(reader, 7)
            };
        }

        private static string ReadString(OracleDataReader reader, int index)
        {
            return reader.IsDBNull(index) ? string.Empty : reader.GetString(index);
        }
    }
}
