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

    // TBLWIREBOND is the wire bond OCAP log: one row per OCAP record raised on a
    // wire bonder. It is a pre-existing OCAP table this app does not own, so its
    // shape is taken as given - no primary key, no NOT NULL, no check constraints.
    //
    // Two consequences worth knowing before reading further:
    //
    //   - Rows are addressed by Oracle ROWID, not by the TBLROWID column. TBLROWID
    //     is NULL on every row keyed in by hand before this page existed, so
    //     keying off it would leave those rows uneditable. New rows still get a
    //     RAWTOHEX(SYS_GUID()) TBLROWID for the MES side.
    //   - WBOCAPWWK is never written here. The BEFORE INSERT trigger
    //     OCAP_WIREBOND_WORKWEEK derives it from WBDATE. That trigger does not
    //     fire on UPDATE, so a saved edit that moves WBDATE leaves the work week
    //     on the week the record was first raised - which is the OCAP number's
    //     week, and what the reports key off.
    public class WireBondService : IWireBondService
    {
        // Order matters: ReadRow reads by index.
        private const string SelectColumns = """
            ROWIDTOCHAR(ROWID), lastupdate, lastupdatedby, wbocapno, wbocapwwk,
            wbissuedby, wbbfg, wbdate, wboperatorid, wbprocess, wbmachine,
            wbpackage, wbsoqty, wbdefect, wbdefectcat, wbdefectothers, wbdiff4m1e,
            wbdiffaffected, wbdifffabsite, wbdiffno, wbdiffnotaffected,
            wbdiffrejectqty, wbdiffremarks, wbverifiedby, wbactiontaken,
            wbdisposition, wbremarks, wbrcmachineerror, wbmachineerror
            """;

        private const string FilterSql = """
            WHERE (:search IS NULL
                   OR UPPER(wbocapno) LIKE :search
                   OR UPPER(wbocapwwk) LIKE :search
                   OR UPPER(wbmachine) LIKE :search
                   OR UPPER(wbpackage) LIKE :search
                   OR UPPER(wbdefect) LIKE :search
                   OR UPPER(wbdefectcat) LIKE :search
                   OR UPPER(wbdiffno) LIKE :search
                   OR UPPER(wbissuedby) LIKE :search
                   OR UPPER(wbverifiedby) LIKE :search
                   OR UPPER(wboperatorid) LIKE :search
                   OR UPPER(lastupdatedby) LIKE :search)
            """;

        private const string InsertSql = """
            INSERT INTO tblwirebond
                (tblrowid, lastupdate, lastupdatedby, wbocapno, wbissuedby, wbbfg,
                 wbdate, wboperatorid, wbprocess, wbmachine, wbpackage, wbsoqty,
                 wbdefect, wbdefectcat, wbdefectothers, wbdiff4m1e, wbdiffaffected,
                 wbdifffabsite, wbdiffno, wbdiffnotaffected, wbdiffrejectqty,
                 wbdiffremarks, wbverifiedby, wbactiontaken, wbdisposition,
                 wbremarks, wbrcmachineerror, wbmachineerror)
            VALUES
                (RAWTOHEX(SYS_GUID()), SYSDATE, :lastupdatedby, :wbocapno, :wbissuedby, :wbbfg,
                 :wbdate, :wboperatorid, :wbprocess, :wbmachine, :wbpackage, :wbsoqty,
                 :wbdefect, :wbdefectcat, :wbdefectothers, :wbdiff4m1e, :wbdiffaffected,
                 :wbdifffabsite, :wbdiffno, :wbdiffnotaffected, :wbdiffrejectqty,
                 :wbdiffremarks, :wbverifiedby, :wbactiontaken, :wbdisposition,
                 :wbremarks, :wbrcmachineerror, :wbmachineerror)
            """;

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

        // The wire bonders as AWACSWSTYPE knows them: WSID for WSTYPE 'WIREBOND'.
        // Offered as the Machine pick list on the add and edit panels so a machine
        // keyed in here matches the workstation master the recipe pages read.
        //
        // Deliberately swallows database errors instead of surfacing them. This is
        // a convenience list, and AWACSWSTYPE is a different table than the one
        // this page owns - if it is missing, empty, or not granted to the app
        // account, the Machine field simply stays free text. Failing the whole
        // grid over a dropdown would be a worse trade.
        public async Task<IReadOnlyList<string>> GetMachineOptionsAsync()
        {
            var values = new List<string>();

            try
            {
                await using var connection = _connectionFactory.CreateConnection();
                await connection.OpenAsync();

                await using var command = connection.CreateCommand();
                command.BindByName = true;
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

        public async Task<(bool Success, string Message)> CreateAsync(WireBondInputModel model, string userName)
        {
            Normalize(model);

            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync();
            await using var transaction = connection.BeginTransaction();

            try
            {
                if (await OcapNoExistsAsync(connection, transaction, model.OcapNo))
                {
                    await transaction.RollbackAsync();
                    return (false, $"TBLWIREBOND already has a record for OCAP No {model.OcapNo}. Edit that row instead of adding a second one.");
                }

                await using var command = connection.CreateCommand();
                command.BindByName = true;
                command.Transaction = transaction;
                command.CommandText = InsertSql;
                AddWriteParameters(command, model, userName);

                var inserted = await command.ExecuteNonQueryTracedAsync();
                if (inserted != 1 || !await OcapNoExistsAsync(connection, transaction, model.OcapNo))
                {
                    await transaction.RollbackAsync();
                    return (false, "Insert verification failed. Insert was rolled back.");
                }

                var workWeek = await ReadWorkWeekAsync(connection, transaction, model.OcapNo);

                await transaction.CommitAsync();
                return (true, string.IsNullOrWhiteSpace(workWeek)
                    ? $"TBLWIREBOND record {model.OcapNo} inserted and verified."
                    : $"TBLWIREBOND record {model.OcapNo} inserted and verified. Work week {workWeek} was set by the database.");
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

                    if (await OcapNoExistsAsync(connection, transaction, model.OcapNo))
                    {
                        await transaction.RollbackAsync();
                        return (false, 0, $"Import cancelled. OCAP No {model.OcapNo} is already in TBLWIREBOND. No rows were uploaded.");
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

                var oldValues = await _auditRepository.CaptureRowAsync(
                    connection, transaction, AuditedTableNames.WireBond, model.TblRowId);

                await using var command = connection.CreateCommand();
                command.BindByName = true;
                command.Transaction = transaction;
                // wbocapwwk is left alone on purpose - see the class comment.
                command.CommandText = """
                    UPDATE tblwirebond
                    SET lastupdate = SYSDATE,
                        lastupdatedby = :lastupdatedby,
                        wbocapno = :wbocapno,
                        wbissuedby = :wbissuedby,
                        wbbfg = :wbbfg,
                        wbdate = :wbdate,
                        wboperatorid = :wboperatorid,
                        wbprocess = :wbprocess,
                        wbmachine = :wbmachine,
                        wbpackage = :wbpackage,
                        wbsoqty = :wbsoqty,
                        wbdefect = :wbdefect,
                        wbdefectcat = :wbdefectcat,
                        wbdefectothers = :wbdefectothers,
                        wbdiff4m1e = :wbdiff4m1e,
                        wbdiffaffected = :wbdiffaffected,
                        wbdifffabsite = :wbdifffabsite,
                        wbdiffno = :wbdiffno,
                        wbdiffnotaffected = :wbdiffnotaffected,
                        wbdiffrejectqty = :wbdiffrejectqty,
                        wbdiffremarks = :wbdiffremarks,
                        wbverifiedby = :wbverifiedby,
                        wbactiontaken = :wbactiontaken,
                        wbdisposition = :wbdisposition,
                        wbremarks = :wbremarks,
                        wbrcmachineerror = :wbrcmachineerror,
                        wbmachineerror = :wbmachineerror
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
                    $"OCAP {model.OcapNo}",
                    model.TblRowId,
                    oldValues,
                    newValues,
                    userName);

                await transaction.CommitAsync();
                return (true, $"TBLWIREBOND record {model.OcapNo} updated and verified. The previous values are on the Change History page if this needs to be reverted.");
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
                    $"OCAP {rowData.GetValueOrDefault("WBOCAPNO")} ({rowData.GetValueOrDefault("WBMACHINE")})",
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
                return (true, "TBLWIREBOND record moved to Trash. Open Trash to restore it if this was a mistake.");
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

        // An OCAP number identifies one record, so a second row carrying the same
        // number is a duplicate. The table has no unique constraint to lean on -
        // this check is the only thing standing between a double-submit and two
        // rows nobody can tell apart.
        private static async Task<bool> OcapNoExistsAsync(OracleConnection connection, OracleTransaction transaction, string ocapNo)
        {
            await using var command = connection.CreateCommand();
            command.BindByName = true;
            command.Transaction = transaction;
            command.CommandText = "SELECT COUNT(1) FROM tblwirebond WHERE UPPER(wbocapno) = UPPER(:wbocapno)";
            command.Parameters.Add(new OracleParameter("wbocapno", ocapNo));

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

        // Reads back what the trigger put in WBOCAPWWK, so the success popup can
        // show it instead of leaving the user to refresh and look.
        private static async Task<string> ReadWorkWeekAsync(OracleConnection connection, OracleTransaction transaction, string ocapNo)
        {
            await using var command = connection.CreateCommand();
            command.BindByName = true;
            command.Transaction = transaction;
            command.CommandText = "SELECT MAX(wbocapwwk) FROM tblwirebond WHERE UPPER(wbocapno) = UPPER(:wbocapno)";
            command.Parameters.Add(new OracleParameter("wbocapno", ocapNo));

            var value = await command.ExecuteScalarTracedAsync();
            return value is null or DBNull ? string.Empty : value.ToString() ?? string.Empty;
        }

        private static void AddWriteParameters(OracleCommand command, WireBondInputModel model, string userName)
        {
            command.Parameters.Add(new OracleParameter("lastupdatedby", userName));
            command.Parameters.Add(new OracleParameter("wbocapno", model.OcapNo));
            command.Parameters.Add(Text("wbissuedby", model.IssuedBy));
            command.Parameters.Add(Text("wbbfg", model.Bfg));
            command.Parameters.Add(new OracleParameter("wbdate", OracleDbType.Date)
            {
                Value = model.OcapDate.HasValue ? (object)model.OcapDate.Value : DBNull.Value
            });
            command.Parameters.Add(Text("wboperatorid", model.OperatorId));
            command.Parameters.Add(Text("wbprocess", model.Process));
            command.Parameters.Add(Text("wbmachine", model.Machine));
            command.Parameters.Add(Text("wbpackage", model.Package));
            command.Parameters.Add(Number("wbsoqty", model.SoQty));
            command.Parameters.Add(Text("wbdefect", model.Defect));
            command.Parameters.Add(Text("wbdefectcat", model.DefectCategory));
            command.Parameters.Add(Text("wbdefectothers", model.DefectOthers));
            command.Parameters.Add(Text("wbdiff4m1e", model.Diff4M1E));
            command.Parameters.Add(Text("wbdiffaffected", model.DiffAffected));
            command.Parameters.Add(Text("wbdifffabsite", model.DiffFabSite));
            command.Parameters.Add(Text("wbdiffno", model.DiffNo));
            command.Parameters.Add(Text("wbdiffnotaffected", model.DiffNotAffected));
            command.Parameters.Add(Number("wbdiffrejectqty", model.DiffRejectQty));
            command.Parameters.Add(Text("wbdiffremarks", model.DiffRemarks));
            command.Parameters.Add(Text("wbverifiedby", model.VerifiedBy));
            command.Parameters.Add(Text("wbactiontaken", model.ActionTaken));
            command.Parameters.Add(Text("wbdisposition", model.Disposition));
            command.Parameters.Add(Text("wbremarks", model.Remarks));
            command.Parameters.Add(Text("wbrcmachineerror", model.RcMachineError));
            command.Parameters.Add(Text("wbmachineerror", model.MachineError));
        }

        private static OracleParameter Text(string name, string? value)
        {
            return new OracleParameter(name, OracleDbType.Varchar2)
            {
                Value = (object?)value ?? DBNull.Value
            };
        }

        // An empty number box posts as null, not as 0 - the column stays NULL
        // rather than claiming a quantity of zero was counted.
        private static OracleParameter Number(string name, decimal? value)
        {
            return new OracleParameter(name, OracleDbType.Decimal)
            {
                Value = value.HasValue ? (object)value.Value : DBNull.Value
            };
        }

        private static void Normalize(WireBondInputModel model)
        {
            model.TblRowId = model.TblRowId?.Trim();

            // Code-like columns are stored upper-cased so the duplicate check and
            // the search box cannot be defeated by "wb-07" vs "WB-07".
            model.OcapNo = InputText.CleanUpper(model.OcapNo);
            model.IssuedBy = InputText.CleanUpperOrNull(model.IssuedBy);
            model.Bfg = InputText.CleanUpperOrNull(model.Bfg);
            model.OperatorId = InputText.CleanUpperOrNull(model.OperatorId);
            model.Process = InputText.CleanUpperOrNull(model.Process);
            model.Machine = InputText.CleanUpperOrNull(model.Machine);
            model.Package = InputText.CleanUpperOrNull(model.Package);
            model.Defect = InputText.CleanUpperOrNull(model.Defect);
            model.DefectCategory = InputText.CleanUpperOrNull(model.DefectCategory);
            model.Diff4M1E = InputText.CleanUpperOrNull(model.Diff4M1E);
            model.DiffFabSite = InputText.CleanUpperOrNull(model.DiffFabSite);
            model.DiffNo = InputText.CleanUpperOrNull(model.DiffNo);
            model.DiffAffected = InputText.CleanUpperOrNull(model.DiffAffected);
            model.DiffNotAffected = InputText.CleanUpperOrNull(model.DiffNotAffected);
            model.VerifiedBy = InputText.CleanUpperOrNull(model.VerifiedBy);
            model.MachineError = InputText.CleanUpperOrNull(model.MachineError);

            // Free text keeps the case it was typed in - these are sentences a
            // person wrote, not codes anything matches on.
            model.DefectOthers = InputText.CleanOrNull(model.DefectOthers);
            model.DiffRemarks = InputText.CleanOrNull(model.DiffRemarks);
            model.ActionTaken = InputText.CleanOrNull(model.ActionTaken);
            model.Disposition = InputText.CleanOrNull(model.Disposition);
            model.Remarks = InputText.CleanOrNull(model.Remarks);
            model.RcMachineError = InputText.CleanOrNull(model.RcMachineError);
        }

        private static string BuildOrderBy(string? sortBy, string? sortDirection)
        {
            var direction = string.Equals(sortDirection, "asc", StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC";
            var nulls = direction == "ASC" ? "NULLS FIRST" : "NULLS LAST";

            return NormalizeSortBy(sortBy) switch
            {
                "sequence" => $"ROWIDTOCHAR(ROWID) {direction}",
                "wbocapno" => $"wbocapno {direction} {nulls}, wbdate DESC NULLS LAST",
                "wbocapwwk" => $"wbocapwwk {direction} {nulls}, wbdate DESC NULLS LAST",
                "wbdate" => $"wbdate {direction} {nulls}, wbocapno ASC",
                "wbmachine" => $"wbmachine {direction} {nulls}, wbdate DESC NULLS LAST",
                "wbpackage" => $"wbpackage {direction} {nulls}, wbdate DESC NULLS LAST",
                "wbdefect" => $"wbdefect {direction} {nulls}, wbdate DESC NULLS LAST",
                "lastupdatedby" => $"lastupdatedby {direction} {nulls}, wbdate DESC NULLS LAST",
                "lastupdate" => $"lastupdate {direction} {nulls}, wbocapno ASC",
                _ => "lastupdate DESC NULLS LAST, wbocapno ASC"
            };
        }

        // Whitelist, not string concatenation: the value reaches an ORDER BY that
        // cannot be parameterized, so anything unrecognized falls back.
        public static string NormalizeSortBy(string? sortBy)
        {
            return sortBy?.Trim().ToLowerInvariant() switch
            {
                "sequence" => "sequence",
                "wbocapno" => "wbocapno",
                "wbocapwwk" => "wbocapwwk",
                "wbdate" => "wbdate",
                "wbmachine" => "wbmachine",
                "wbpackage" => "wbpackage",
                "wbdefect" => "wbdefect",
                "lastupdatedby" => "lastupdatedby",
                "lastupdate" => "lastupdate",
                _ => "lastupdate"
            };
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
                OcapNo = ReadString(reader, 3),
                OcapWorkWeek = ReadString(reader, 4),
                IssuedBy = ReadString(reader, 5),
                Bfg = ReadString(reader, 6),
                OcapDate = ReadDate(reader, 7),
                OperatorId = ReadString(reader, 8),
                Process = ReadString(reader, 9),
                Machine = ReadString(reader, 10),
                Package = ReadString(reader, 11),
                SoQty = ReadNumber(reader, 12),
                Defect = ReadString(reader, 13),
                DefectCategory = ReadString(reader, 14),
                DefectOthers = ReadString(reader, 15),
                Diff4M1E = ReadString(reader, 16),
                DiffAffected = ReadString(reader, 17),
                DiffFabSite = ReadString(reader, 18),
                DiffNo = ReadString(reader, 19),
                DiffNotAffected = ReadString(reader, 20),
                DiffRejectQty = ReadNumber(reader, 21),
                DiffRemarks = ReadString(reader, 22),
                VerifiedBy = ReadString(reader, 23),
                ActionTaken = ReadString(reader, 24),
                Disposition = ReadString(reader, 25),
                Remarks = ReadString(reader, 26),
                RcMachineError = ReadString(reader, 27),
                MachineError = ReadString(reader, 28)
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

        private static decimal? ReadNumber(OracleDataReader reader, int index)
        {
            return reader.IsDBNull(index) ? null : reader.GetDecimal(index);
        }
    }
}
