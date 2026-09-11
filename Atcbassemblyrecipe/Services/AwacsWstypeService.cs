using Atcbassemblyrecipe.Data;
using Atcbassemblyrecipe.Models;
using Atcbassemblyrecipe.ViewModels;
using Oracle.ManagedDataAccess.Client;
using Atcbassemblyrecipe.Infrastructure;

namespace Atcbassemblyrecipe.Services
{
    public interface IAwacsWstypeService
    {
        Task<PagedResult<AwacsWstype>> GetAwacsWstypeAsync(string? search, int page, int pageSize, string? sortBy, string? sortDirection);
        Task<IReadOnlyList<AwacsWstype>> GetAwacsWstypeForExportAsync(string? search, string? sortBy, string? sortDirection);
        Task<PagedResult<AwacsRecipeByWstype>> GetRecipeByWstypeAsync(string wsType, string? search, int page, int pageSize, string? sortBy, string? sortDirection);
        Task<IReadOnlyList<AwacsRecipeByWstype>> GetRecipeByWstypeForExportAsync(string wsType, string? search, string? sortBy, string? sortDirection);
        Task<(bool Exists, string Product, string Recipe)> CheckSawingRecipeAsync(SawingRecipeInputModel model);
        Task<RecipeRowEditModel?> GetRecipeRowForEditAsync(string tblRowId);
        Task<(bool Success, string Message)> CreateSawingRecipeAsync(SawingRecipeInputModel model, string userName);
        Task<(bool Success, string Message)> CreateRecipeRowAsync(RecipeRowInputModel model, string userName);
        Task<(bool Success, int Inserted, string Message)> CreateManyRecipeRowsAsync(IReadOnlyList<RecipeRowInputModel> models, string userName);
        Task<(bool Success, string Message)> UpdateRecipeRowAsync(RecipeRowEditModel model, string userName);
        Task<(bool Success, string Message)> DeleteRecipeRowAsync(string tblRowId, string userName);
        Task<AwacsWstypeInputModel?> GetForEditAsync(string tblRowId);
        Task<int> GetTblSawingCountAsync();
        Task<(bool Success, string Message)> CreateAsync(AwacsWstypeInputModel model, string userName);
        Task<(bool Success, int Inserted, string Message)> CreateManyAsync(IReadOnlyList<AwacsWstypeInputModel> models, string userName);
        Task<(bool Success, string Message)> UpdateAsync(AwacsWstypeInputModel model, string userName);
        Task<(bool Success, string Message)> DeleteAsync(string tblRowId, string userName);
    }

    public class AwacsWstypeService : IAwacsWstypeService
    {
        private readonly IOracleConnectionFactory _connectionFactory;
        private readonly IRecipeAuditRepository _auditRepository;

        public AwacsWstypeService(IOracleConnectionFactory connectionFactory, IRecipeAuditRepository auditRepository)
        {
            _connectionFactory = connectionFactory;
            _auditRepository = auditRepository;
        }

        public async Task<PagedResult<AwacsWstype>> GetAwacsWstypeAsync(string? search, int page, int pageSize, string? sortBy, string? sortDirection)
        {
            var rows = new List<AwacsWstype>();
            page = NormalizePage(page);
            pageSize = NormalizePageSize(pageSize);
            var offset = (page - 1) * pageSize;
            var normalizedSearch = NormalizeSearch(search);
            var orderBy = BuildAwacsWstypeOrderBy(sortBy, sortDirection);

            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync();

            var totalRows = await CountAwacsWstypeRowsAsync(connection, normalizedSearch);

            await using var command = connection.CreateCommand();
            command.BindByName = true;
            command.CommandText = $"""
                SELECT tblrowid, lastupdate, lastupdatedby, wsid, wstype, wsdb
                FROM awacswstype
                WHERE wstype IN ('SAWING', 'WIREBOND')
                  AND (:search IS NULL
                       OR UPPER(tblrowid) LIKE :search
                       OR UPPER(lastupdatedby) LIKE :search
                       OR UPPER(wsid) LIKE :search
                       OR UPPER(wsdb) LIKE :search)
                ORDER BY {orderBy}
                OFFSET :offset ROWS FETCH NEXT :page_size ROWS ONLY
                """;
            AddSearchPagingParameters(command, normalizedSearch, offset, pageSize);

            await using var reader = await command.ExecuteReaderTracedAsync();
            while (await reader.ReadAsync())
            {
                rows.Add(ReadAwacsWstype(reader));
            }

            return new PagedResult<AwacsWstype>
            {
                Rows = rows,
                Search = search?.Trim() ?? string.Empty,
                Page = page,
                PageSize = pageSize,
                TotalRows = totalRows
            };
        }

        public async Task<IReadOnlyList<AwacsWstype>> GetAwacsWstypeForExportAsync(string? search, string? sortBy, string? sortDirection)
        {
            var rows = new List<AwacsWstype>();
            var normalizedSearch = NormalizeSearch(search);
            var orderBy = BuildAwacsWstypeOrderBy(sortBy, sortDirection);

            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.BindByName = true;
            command.CommandText = $"""
                SELECT tblrowid, lastupdate, lastupdatedby, wsid, wstype, wsdb
                FROM awacswstype
                WHERE wstype IN ('SAWING', 'WIREBOND')
                  AND (:search IS NULL
                       OR UPPER(tblrowid) LIKE :search
                       OR UPPER(lastupdatedby) LIKE :search
                       OR UPPER(wsid) LIKE :search
                       OR UPPER(wsdb) LIKE :search)
                ORDER BY {orderBy}
                """;
            AddSearchParameter(command, normalizedSearch);

            await using var reader = await command.ExecuteReaderTracedAsync();
            while (await reader.ReadAsync())
            {
                rows.Add(ReadAwacsWstype(reader));
            }

            return rows;
        }

        public async Task<(bool Exists, string Product, string Recipe)> CheckSawingRecipeAsync(SawingRecipeInputModel model)
        {
            Normalize(model);

            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync();

            var exists = await RecipeExistsAsync(connection, null, model.WsType, model.Product, model.Leadframe12Nc, model.Recipe);
            return (exists, model.Product, model.Recipe);
        }

        public async Task<(bool Success, string Message)> CreateSawingRecipeAsync(SawingRecipeInputModel model, string userName)
        {
            Normalize(model);

            return await InsertRecipeRowAsync(
                model.WsType,
                model.Package,
                model.Product,
                model.Leadframe12Nc,
                model.Recipe,
                userName);
        }

        public async Task<(bool Success, string Message)> CreateRecipeRowAsync(RecipeRowInputModel model, string userName)
        {
            Normalize(model);

            return await InsertRecipeRowAsync(
                model.WsType,
                model.Package,
                model.Product,
                model.Leadframe12Nc,
                model.Recipe,
                userName);
        }

        public async Task<(bool Success, int Inserted, string Message)> CreateManyRecipeRowsAsync(IReadOnlyList<RecipeRowInputModel> models, string userName)
        {
            if (models.Count == 0)
            {
                return (false, 0, "No recipe rows were found to import.");
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

                    if (await RecipeExistsAsync(connection, transaction, model.WsType, model.Product, model.Leadframe12Nc, model.Recipe))
                    {
                        await transaction.RollbackAsync();
                        return (false, 0, $"Import cancelled. Duplicate {model.WsType} recipe found: {model.Product} / {model.Leadframe12Nc} / {model.Recipe}. No rows were uploaded.");
                    }

                    await using var command = connection.CreateCommand();
                    command.BindByName = true;
                    command.Transaction = transaction;
                    command.CommandText = InsertRecipeSql;
                    AddRecipeWriteParameters(command, model.WsType, model.Package, model.Product, model.Leadframe12Nc, model.Recipe, userName);

                    var affectedRows = await command.ExecuteNonQueryTracedAsync();
                    var verified = await RecipeExistsAsync(connection, transaction, model.WsType, model.Product, model.Leadframe12Nc, model.Recipe);
                    if (affectedRows != 1 || !verified)
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

        public async Task<RecipeRowEditModel?> GetRecipeRowForEditAsync(string tblRowId)
        {
            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.BindByName = true;
            command.CommandText = """
                SELECT ROWIDTOCHAR(ROWID), wstype, "PACKAGE", product, leadframe12nc, recipe
                FROM awacsrecipebywstype
                WHERE ROWID = CHARTOROWID(:tblrowid)
                """;
            command.Parameters.Add(new OracleParameter("tblrowid", tblRowId));

            await using var reader = await command.ExecuteReaderTracedAsync();
            if (!await reader.ReadAsync())
            {
                return null;
            }

            return new RecipeRowEditModel
            {
                TblRowId = ReadString(reader, 0),
                WsType = ReadString(reader, 1),
                Package = ReadString(reader, 2),
                Product = ReadString(reader, 3),
                Leadframe12Nc = ReadString(reader, 4),
                Recipe = ReadString(reader, 5)
            };
        }

        // Every column the user can change is written from the model, RECIPE
        // included - it is no longer re-derived from PRODUCT behind their back.
        // The row as it was before this update is copied into TBLRECIPEHISTORY in
        // the same transaction, which is what makes Revert possible afterwards.
        public async Task<(bool Success, string Message)> UpdateRecipeRowAsync(RecipeRowEditModel model, string userName)
        {
            Normalize(model);

            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync();
            await using var transaction = connection.BeginTransaction();

            try
            {
                if (!await RecipeRowIdExistsAsync(connection, transaction, model.TblRowId))
                {
                    await transaction.RollbackAsync();
                    return (false, $"{model.WsType} recipe row was not found. It may have been deleted - check Trash.");
                }

                var oldValues = await _auditRepository.CaptureRowAsync(
                    connection, transaction, AuditedTableNames.AwacsRecipeByWstype, model.TblRowId);

                await using var command = connection.CreateCommand();
                command.BindByName = true;
                command.Transaction = transaction;
                command.CommandText = """
                    UPDATE awacsrecipebywstype
                    SET lastupdate = SYSDATE,
                        lastupdatedby = :lastupdatedby,
                        wstype = :wstype,
                        "PACKAGE" = :package,
                        product = :product,
                        leadframe12nc = :leadframe12nc,
                        recipe = :recipe
                    WHERE ROWID = CHARTOROWID(:tblrowid)
                    """;
                AddRecipeWriteParameters(command, model.WsType, model.Package, model.Product, model.Leadframe12Nc, model.Recipe, userName);
                command.Parameters.Add(new OracleParameter("tblrowid", model.TblRowId));

                var updated = await command.ExecuteNonQueryTracedAsync();
                var verified = await RecipeRowIdExistsAsync(connection, transaction, model.TblRowId);

                if (updated != 1 || !verified)
                {
                    await transaction.RollbackAsync();
                    return (false, "Update verification failed. Update was rolled back.");
                }

                var newValues = await _auditRepository.CaptureRowAsync(
                    connection, transaction, AuditedTableNames.AwacsRecipeByWstype, model.TblRowId);

                await _auditRepository.WriteHistoryAsync(
                    connection,
                    transaction,
                    AuditedTableNames.AwacsRecipeByWstype,
                    $"{model.WsType} {model.Product} / {model.Leadframe12Nc}",
                    model.TblRowId,
                    oldValues,
                    newValues,
                    userName);

                await transaction.CommitAsync();
                return (true, $"{model.WsType} recipe updated and verified. The previous values are on the Change History page if this needs to be reverted.");
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        // Delete is really "move to trash": the whole row is copied into
        // TBLRECIPETRASH first, then removed, both in one transaction. If the trash
        // table is missing the copy throws and the delete rolls back, so a row is
        // never removed without a way back.
        public async Task<(bool Success, string Message)> DeleteRecipeRowAsync(string tblRowId, string userName)
        {
            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync();
            await using var transaction = connection.BeginTransaction();

            try
            {
                if (!await RecipeRowIdExistsAsync(connection, transaction, tblRowId))
                {
                    await transaction.RollbackAsync();
                    return (false, "Recipe row was not found.");
                }

                var rowData = await _auditRepository.CaptureRowAsync(
                    connection, transaction, AuditedTableNames.AwacsRecipeByWstype, tblRowId);

                await _auditRepository.WriteTrashAsync(
                    connection,
                    transaction,
                    AuditedTableNames.AwacsRecipeByWstype,
                    BuildRecipeLabel(rowData),
                    rowData,
                    userName);

                await using var command = connection.CreateCommand();
                command.BindByName = true;
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM awacsrecipebywstype WHERE ROWID = CHARTOROWID(:tblrowid)";
                command.Parameters.Add(new OracleParameter("tblrowid", tblRowId));

                var deleted = await command.ExecuteNonQueryTracedAsync();
                var stillExists = await RecipeRowIdExistsAsync(connection, transaction, tblRowId);

                if (deleted != 1 || stillExists)
                {
                    await transaction.RollbackAsync();
                    return (false, "Delete verification failed. Delete was rolled back.");
                }

                await transaction.CommitAsync();
                return (true, "Recipe row moved to Trash. Open Trash to restore it if this was a mistake.");
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<PagedResult<AwacsRecipeByWstype>> GetRecipeByWstypeAsync(string wsType, string? search, int page, int pageSize, string? sortBy, string? sortDirection)
        {
            var rows = new List<AwacsRecipeByWstype>();
            wsType = RecipeWsTypes.Normalize(wsType);
            page = NormalizePage(page);
            pageSize = NormalizePageSize(pageSize);
            var offset = (page - 1) * pageSize;
            var normalizedSearch = NormalizeSearch(search);
            var orderBy = BuildRecipeByWstypeOrderBy(sortBy, sortDirection);

            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync();

            var totalRows = await CountRecipeByWstypeRowsAsync(connection, wsType, normalizedSearch);

            await using var command = connection.CreateCommand();
            command.BindByName = true;
            command.CommandText = $"""
                SELECT ROWIDTOCHAR(ROWID), lastupdate, lastupdatedby, wstype, "PACKAGE", product, leadframe12nc, recipe
                FROM awacsrecipebywstype
                {RecipeFilterSql}
                ORDER BY {orderBy}
                OFFSET :offset ROWS FETCH NEXT :page_size ROWS ONLY
                """;
            command.Parameters.Add(new OracleParameter("wstype", wsType));
            AddSearchPagingParameters(command, normalizedSearch, offset, pageSize);

            await using var reader = await command.ExecuteReaderTracedAsync();
            while (await reader.ReadAsync())
            {
                rows.Add(ReadRecipeRow(reader));
            }

            return new PagedResult<AwacsRecipeByWstype>
            {
                Rows = rows,
                Search = search?.Trim() ?? string.Empty,
                Page = page,
                PageSize = pageSize,
                TotalRows = totalRows
            };
        }

        public async Task<IReadOnlyList<AwacsRecipeByWstype>> GetRecipeByWstypeForExportAsync(string wsType, string? search, string? sortBy, string? sortDirection)
        {
            var rows = new List<AwacsRecipeByWstype>();
            wsType = RecipeWsTypes.Normalize(wsType);
            var normalizedSearch = NormalizeSearch(search);
            var orderBy = BuildRecipeByWstypeOrderBy(sortBy, sortDirection);

            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.BindByName = true;
            command.CommandText = $"""
                SELECT ROWIDTOCHAR(ROWID), lastupdate, lastupdatedby, wstype, "PACKAGE", product, leadframe12nc, recipe
                FROM awacsrecipebywstype
                {RecipeFilterSql}
                ORDER BY {orderBy}
                """;
            command.Parameters.Add(new OracleParameter("wstype", wsType));
            AddSearchParameter(command, normalizedSearch);

            await using var reader = await command.ExecuteReaderTracedAsync();
            while (await reader.ReadAsync())
            {
                rows.Add(ReadRecipeRow(reader));
            }

            return rows;
        }

        public async Task<AwacsWstypeInputModel?> GetForEditAsync(string tblRowId)
        {
            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.BindByName = true;
            command.CommandText = """
                SELECT tblrowid, wsid, wstype, wsdb
                FROM awacswstype
                WHERE tblrowid = :tblrowid
                """;
            command.Parameters.Add(new OracleParameter("tblrowid", tblRowId));

            await using var reader = await command.ExecuteReaderTracedAsync();
            if (!await reader.ReadAsync())
            {
                return null;
            }

            return new AwacsWstypeInputModel
            {
                TblRowId = ReadString(reader, 0),
                WsId = ReadString(reader, 1),
                WsType = ReadString(reader, 2),
                WsDb = ReadString(reader, 3)
            };
        }

        public async Task<int> GetTblSawingCountAsync()
        {
            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(1) FROM tblsawing";
            return Convert.ToInt32(await command.ExecuteScalarTracedAsync());
        }

        public async Task<(bool Success, string Message)> CreateAsync(AwacsWstypeInputModel model, string userName)
        {
            Normalize(model);

            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync();
            await using var transaction = connection.BeginTransaction();

            try
            {
                if (await AwacsRowExistsAsync(connection, transaction, model.WsId, model.WsType, model.WsDb))
                {
                    await transaction.RollbackAsync();
                    return (false, "AWACSWSTYPE already has this WSID/WSTYPE/WSDB.");
                }

                await using var command = connection.CreateCommand();
                command.BindByName = true;
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO awacswstype (tblrowid, lastupdate, lastupdatedby, wsid, wstype, wsdb)
                    VALUES (RAWTOHEX(SYS_GUID()), SYSDATE, :lastupdatedby, :wsid, :wstype, :wsdb)
                    """;
                AddWriteParameters(command, model, userName);

                var inserted = await command.ExecuteNonQueryTracedAsync();
                var verified = await CountAwacsRowsAsync(connection, transaction, model.WsId, model.WsType, model.WsDb);

                if (inserted != 1 || verified != 1)
                {
                    await transaction.RollbackAsync();
                    return (false, "Insert verification failed. Insert was rolled back.");
                }

                await transaction.CommitAsync();
                return (true, "AWACSWSTYPE row inserted and verified.");
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<(bool Success, int Inserted, string Message)> CreateManyAsync(IReadOnlyList<AwacsWstypeInputModel> models, string userName)
        {
            if (models.Count == 0)
            {
                return (false, 0, "No AWACSWSTYPE rows were found to import.");
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

                    if (await AwacsRowExistsAsync(connection, transaction, model.WsId, model.WsType, model.WsDb))
                    {
                        await transaction.RollbackAsync();
                        return (false, 0, $"Import cancelled. Duplicate AWACSWSTYPE row found: {model.WsId} / {model.WsType} / {model.WsDb}. No rows were uploaded.");
                    }

                    await using var command = connection.CreateCommand();
                    command.BindByName = true;
                    command.Transaction = transaction;
                    command.CommandText = """
                        INSERT INTO awacswstype (tblrowid, lastupdate, lastupdatedby, wsid, wstype, wsdb)
                        VALUES (RAWTOHEX(SYS_GUID()), SYSDATE, :lastupdatedby, :wsid, :wstype, :wsdb)
                        """;
                    AddWriteParameters(command, model, userName);

                    var affectedRows = await command.ExecuteNonQueryTracedAsync();
                    var verified = await CountAwacsRowsAsync(connection, transaction, model.WsId, model.WsType, model.WsDb);
                    if (affectedRows != 1 || verified != 1)
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

        public async Task<(bool Success, string Message)> UpdateAsync(AwacsWstypeInputModel model, string userName)
        {
            Normalize(model);

            if (string.IsNullOrWhiteSpace(model.TblRowId))
            {
                return (false, "Missing AWACSWSTYPE row id.");
            }

            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync();
            await using var transaction = connection.BeginTransaction();

            try
            {
                if (!await AwacsRowIdExistsAsync(connection, transaction, model.TblRowId))
                {
                    await transaction.RollbackAsync();
                    return (false, "AWACSWSTYPE row was not found. It may have been deleted - check Trash.");
                }

                var oldValues = await _auditRepository.CaptureRowAsync(
                    connection, transaction, AuditedTableNames.AwacsWstype, model.TblRowId);

                await using var command = connection.CreateCommand();
                command.BindByName = true;
                command.Transaction = transaction;
                command.CommandText = """
                    UPDATE awacswstype
                    SET lastupdate = SYSDATE,
                        lastupdatedby = :lastupdatedby,
                        wsid = :wsid,
                        wstype = :wstype,
                        wsdb = :wsdb
                    WHERE tblrowid = :tblrowid
                    """;
                AddWriteParameters(command, model, userName);
                command.Parameters.Add(new OracleParameter("tblrowid", model.TblRowId));

                var updated = await command.ExecuteNonQueryTracedAsync();
                var verified = await AwacsRowIdExistsAsync(connection, transaction, model.TblRowId);

                if (updated != 1 || !verified)
                {
                    await transaction.RollbackAsync();
                    return (false, "Update verification failed. Update was rolled back.");
                }

                var newValues = await _auditRepository.CaptureRowAsync(
                    connection, transaction, AuditedTableNames.AwacsWstype, model.TblRowId);

                await _auditRepository.WriteHistoryAsync(
                    connection,
                    transaction,
                    AuditedTableNames.AwacsWstype,
                    $"{model.WsId} / {model.WsType}",
                    model.TblRowId,
                    oldValues,
                    newValues,
                    userName);

                await transaction.CommitAsync();
                return (true, "AWACSWSTYPE row updated and verified. The previous values are on the Change History page if this needs to be reverted.");
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
                if (!await AwacsRowIdExistsAsync(connection, transaction, tblRowId))
                {
                    await transaction.RollbackAsync();
                    return (false, "AWACSWSTYPE row was not found.");
                }

                var rowData = await _auditRepository.CaptureRowAsync(
                    connection, transaction, AuditedTableNames.AwacsWstype, tblRowId);

                await _auditRepository.WriteTrashAsync(
                    connection,
                    transaction,
                    AuditedTableNames.AwacsWstype,
                    $"{rowData.GetValueOrDefault("WSID")} / {rowData.GetValueOrDefault("WSTYPE")}",
                    rowData,
                    userName);

                await using var command = connection.CreateCommand();
                command.BindByName = true;
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM awacswstype WHERE tblrowid = :tblrowid";
                command.Parameters.Add(new OracleParameter("tblrowid", tblRowId));

                var deleted = await command.ExecuteNonQueryTracedAsync();
                var stillExists = await AwacsRowIdExistsAsync(connection, transaction, tblRowId);

                if (deleted != 1 || stillExists)
                {
                    await transaction.RollbackAsync();
                    return (false, "Delete verification failed. Delete was rolled back.");
                }

                await transaction.CommitAsync();
                return (true, "AWACSWSTYPE row moved to Trash. Open Trash to restore it if this was a mistake.");
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        private async Task<(bool Success, string Message)> InsertRecipeRowAsync(
            string wsType,
            string? package,
            string product,
            string leadframe12Nc,
            string recipe,
            string userName)
        {
            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync();
            await using var transaction = connection.BeginTransaction();

            try
            {
                if (await RecipeExistsAsync(connection, transaction, wsType, product, leadframe12Nc, recipe))
                {
                    await transaction.RollbackAsync();
                    return (false, $"That {wsType} recipe already exists in AWACSRECIPEBYWSTYPE.");
                }

                await using var command = connection.CreateCommand();
                command.BindByName = true;
                command.Transaction = transaction;
                command.CommandText = InsertRecipeSql;
                AddRecipeWriteParameters(command, wsType, package, product, leadframe12Nc, recipe, userName);

                var inserted = await command.ExecuteNonQueryTracedAsync();
                var verified = await RecipeExistsAsync(connection, transaction, wsType, product, leadframe12Nc, recipe);

                if (inserted != 1 || !verified)
                {
                    await transaction.RollbackAsync();
                    return (false, "Insert verification failed. Insert was rolled back.");
                }

                await transaction.CommitAsync();
                return (true, $"{wsType} recipe inserted and verified.");
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        private const string InsertRecipeSql = """
            INSERT INTO awacsrecipebywstype
                (tblrowid, lastupdate, lastupdatedby, wstype, "PACKAGE", product, leadframe12nc, recipe)
            VALUES
                (RAWTOHEX(SYS_GUID()), SYSDATE, :lastupdatedby, :wstype, :package, :product, :leadframe12nc, :recipe)
            """;

        // Shared by the paged read, the export and the count so a search can never
        // mean one thing on screen and another in the CSV.
        private const string RecipeFilterSql = """
            WHERE wstype = :wstype
              AND (:search IS NULL
                   OR UPPER(product) LIKE :search
                   OR UPPER(leadframe12nc) LIKE :search
                   OR UPPER(recipe) LIKE :search
                   OR UPPER("PACKAGE") LIKE :search)
            """;

        private static async Task<bool> AwacsRowExistsAsync(OracleConnection connection, OracleTransaction transaction, string wsId, string wsType, string wsDb)
        {
            return await CountAwacsRowsAsync(connection, transaction, wsId, wsType, wsDb) > 0;
        }

        private static async Task<int> CountAwacsWstypeRowsAsync(OracleConnection connection, string? search)
        {
            await using var command = connection.CreateCommand();
            command.BindByName = true;
            command.CommandText = """
                SELECT COUNT(1)
                FROM awacswstype
                WHERE wstype IN ('SAWING', 'WIREBOND')
                  AND (:search IS NULL
                       OR UPPER(tblrowid) LIKE :search
                       OR UPPER(lastupdatedby) LIKE :search
                       OR UPPER(wsid) LIKE :search
                       OR UPPER(wsdb) LIKE :search)
                """;
            AddSearchParameter(command, search);

            return Convert.ToInt32(await command.ExecuteScalarTracedAsync());
        }

        private static async Task<int> CountRecipeByWstypeRowsAsync(OracleConnection connection, string wsType, string? search)
        {
            await using var command = connection.CreateCommand();
            command.BindByName = true;
            command.CommandText = $"""
                SELECT COUNT(1)
                FROM awacsrecipebywstype
                {RecipeFilterSql}
                """;
            command.Parameters.Add(new OracleParameter("wstype", wsType));
            AddSearchParameter(command, search);

            return Convert.ToInt32(await command.ExecuteScalarTracedAsync());
        }

        private static async Task<bool> RecipeExistsAsync(OracleConnection connection, OracleTransaction? transaction, string wsType, string product, string leadframe12Nc, string recipe)
        {
            await using var command = connection.CreateCommand();
            command.BindByName = true;
            command.Transaction = transaction;
            command.CommandText = """
                SELECT COUNT(1)
                FROM awacsrecipebywstype
                WHERE wstype = :wstype
                  AND product = :product
                  AND leadframe12nc = :leadframe12nc
                  AND recipe = :recipe
                """;
            command.Parameters.Add(new OracleParameter("wstype", wsType));
            command.Parameters.Add(new OracleParameter("product", product));
            command.Parameters.Add(new OracleParameter("leadframe12nc", leadframe12Nc));
            command.Parameters.Add(new OracleParameter("recipe", recipe));

            return Convert.ToInt32(await command.ExecuteScalarTracedAsync()) > 0;
        }

        private static async Task<bool> RecipeRowIdExistsAsync(OracleConnection connection, OracleTransaction transaction, string tblRowId)
        {
            await using var command = connection.CreateCommand();
            command.BindByName = true;
            command.Transaction = transaction;
            command.CommandText = """
                SELECT COUNT(1)
                FROM awacsrecipebywstype
                WHERE ROWID = CHARTOROWID(:tblrowid)
                """;
            command.Parameters.Add(new OracleParameter("tblrowid", tblRowId));

            return Convert.ToInt32(await command.ExecuteScalarTracedAsync()) == 1;
        }

        private static async Task<int> CountAwacsRowsAsync(OracleConnection connection, OracleTransaction transaction, string wsId, string wsType, string wsDb)
        {
            await using var command = connection.CreateCommand();
            command.BindByName = true;
            command.Transaction = transaction;
            command.CommandText = """
                SELECT COUNT(1)
                FROM awacswstype
                WHERE wsid = :wsid
                  AND wstype = :wstype
                  AND wsdb = :wsdb
                """;
            command.Parameters.Add(new OracleParameter("wsid", wsId));
            command.Parameters.Add(new OracleParameter("wstype", wsType));
            command.Parameters.Add(new OracleParameter("wsdb", wsDb));

            return Convert.ToInt32(await command.ExecuteScalarTracedAsync());
        }

        private static async Task<bool> AwacsRowIdExistsAsync(OracleConnection connection, OracleTransaction transaction, string tblRowId)
        {
            await using var command = connection.CreateCommand();
            command.BindByName = true;
            command.Transaction = transaction;
            command.CommandText = """
                SELECT COUNT(1)
                FROM awacswstype
                WHERE tblrowid = :tblrowid
                """;
            command.Parameters.Add(new OracleParameter("tblrowid", tblRowId));

            return Convert.ToInt32(await command.ExecuteScalarTracedAsync()) == 1;
        }

        private static void AddWriteParameters(OracleCommand command, AwacsWstypeInputModel model, string userName)
        {
            command.Parameters.Add(new OracleParameter("lastupdatedby", userName));
            command.Parameters.Add(new OracleParameter("wsid", model.WsId));
            command.Parameters.Add(new OracleParameter("wstype", model.WsType));
            command.Parameters.Add(new OracleParameter("wsdb", model.WsDb));
        }

        private static void AddRecipeWriteParameters(
            OracleCommand command,
            string wsType,
            string? package,
            string product,
            string leadframe12Nc,
            string recipe,
            string userName)
        {
            command.Parameters.Add(new OracleParameter("lastupdatedby", userName));
            command.Parameters.Add(new OracleParameter("wstype", wsType));
            command.Parameters.Add(new OracleParameter("package", (object?)package ?? DBNull.Value));
            command.Parameters.Add(new OracleParameter("product", product));
            command.Parameters.Add(new OracleParameter("leadframe12nc", leadframe12Nc));
            command.Parameters.Add(new OracleParameter("recipe", recipe));
        }

        private static string BuildRecipeLabel(IReadOnlyDictionary<string, string?> rowData)
        {
            return $"{rowData.GetValueOrDefault("WSTYPE")} {rowData.GetValueOrDefault("PRODUCT")} / {rowData.GetValueOrDefault("LEADFRAME12NC")}";
        }

        private static void Normalize(AwacsWstypeInputModel model)
        {
            model.TblRowId = model.TblRowId?.Trim();
            model.WsId = InputText.CleanUpper(model.WsId);
            model.WsType = InputText.CleanUpper(model.WsType);

            // Never taken from the form: WSTYPE decides which table holds that
            // workstation's recipes. An unknown WSTYPE leaves whatever was
            // there, so validation reports the WSTYPE rather than a confusing
            // empty WSDB.
            var derived = AwacsWstypeInputModel.WsDbFor(model.WsType);
            model.WsDb = string.IsNullOrEmpty(derived) ? InputText.CleanUpper(model.WsDb) : derived;
        }

        private static void Normalize(SawingRecipeInputModel model)
        {
            model.WsType = RecipeWsTypes.Sawing;
            model.Package = InputText.CleanUpperOrNull(model.Package);
            model.Leadframe12Nc = InputText.Clean(model.Leadframe12Nc);
            model.CeptDescription = InputText.CleanUpper(model.CeptDescription);
        }

        private static void Normalize(RecipeRowInputModel model)
        {
            model.WsType = RecipeWsTypes.Normalize(model.WsType);
            model.Package = InputText.CleanUpperOrNull(model.Package);
            model.Product = InputText.CleanUpper(model.Product);
            model.Leadframe12Nc = InputText.Clean(model.Leadframe12Nc);
            model.Recipe = InputText.CleanUpper(model.Recipe);
        }

        private static void Normalize(RecipeRowEditModel model)
        {
            model.TblRowId = model.TblRowId.Trim();
            model.WsType = RecipeWsTypes.Normalize(model.WsType);
            model.Package = InputText.CleanUpperOrNull(model.Package);
            model.Product = InputText.CleanUpper(model.Product);
            model.Leadframe12Nc = InputText.Clean(model.Leadframe12Nc);
            model.Recipe = InputText.CleanUpper(model.Recipe);
        }

        private static int NormalizePage(int page)
        {
            return page < 1 ? 1 : page;
        }

        private static int NormalizePageSize(int pageSize)
        {
            return pageSize is < 10 or > 100 ? 25 : pageSize;
        }

        private static string BuildAwacsWstypeOrderBy(string? sortBy, string? sortDirection)
        {
            var direction = string.Equals(sortDirection, "asc", StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC";
            var nulls = direction == "ASC" ? "NULLS FIRST" : "NULLS LAST";

            return NormalizeAwacsWstypeSortBy(sortBy) switch
            {
                "sequence" => $"tblrowid {direction}",
                "wsid" => $"wsid {direction}, tblrowid ASC",
                "wstype" => $"wstype {direction}, wsid ASC, tblrowid ASC",
                "lastupdatedby" => $"lastupdatedby {direction}, wsid ASC, tblrowid ASC",
                "lastupdate" => $"lastupdate {direction} {nulls}, wsid ASC, tblrowid ASC",
                "wsdb" => $"wsdb {direction}, wsid ASC, tblrowid ASC",
                _ => "lastupdate DESC NULLS LAST, wsid ASC, tblrowid ASC"
            };
        }

        private static string BuildRecipeByWstypeOrderBy(string? sortBy, string? sortDirection)
        {
            var direction = string.Equals(sortDirection, "asc", StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC";
            var nulls = direction == "ASC" ? "NULLS FIRST" : "NULLS LAST";

            return NormalizeRecipeByWstypeSortBy(sortBy) switch
            {
                "sequence" => $"ROWIDTOCHAR(ROWID) {direction}",
                "wstype" => $"wstype {direction}, product ASC, leadframe12nc ASC",
                "package" => $"\"PACKAGE\" {direction} {nulls}, product ASC, leadframe12nc ASC",
                "product" => $"product {direction}, leadframe12nc ASC, recipe ASC",
                "leadframe12nc" => $"leadframe12nc {direction}, product ASC, recipe ASC",
                "recipe" => $"recipe {direction}, product ASC, leadframe12nc ASC",
                "lastupdatedby" => $"lastupdatedby {direction}, product ASC, leadframe12nc ASC",
                "lastupdate" => $"lastupdate {direction} {nulls}, product ASC, leadframe12nc ASC",
                _ => "lastupdate DESC NULLS LAST, product ASC, leadframe12nc ASC"
            };
        }

        private static string NormalizeAwacsWstypeSortBy(string? sortBy)
        {
            return sortBy?.Trim().ToLowerInvariant() switch
            {
                "sequence" => "sequence",
                "wsid" => "wsid",
                "wstype" => "wstype",
                "lastupdatedby" => "lastupdatedby",
                "lastupdate" => "lastupdate",
                "wsdb" => "wsdb",
                _ => "lastupdate"
            };
        }

        private static string NormalizeRecipeByWstypeSortBy(string? sortBy)
        {
            return sortBy?.Trim().ToLowerInvariant() switch
            {
                "sequence" => "sequence",
                "wstype" => "wstype",
                "package" => "package",
                "product" => "product",
                "leadframe12nc" => "leadframe12nc",
                "recipe" => "recipe",
                "lastupdatedby" => "lastupdatedby",
                "lastupdate" => "lastupdate",
                _ => "lastupdate"
            };
        }

        private static string? NormalizeSearch(string? search)
        {
            return string.IsNullOrWhiteSpace(search)
                ? null
                : $"%{search.Trim().ToUpperInvariant()}%";
        }

        private static void AddSearchPagingParameters(OracleCommand command, string? search, int offset, int pageSize)
        {
            AddSearchParameter(command, search);
            command.Parameters.Add(new OracleParameter("offset", OracleDbType.Int32) { Value = offset });
            command.Parameters.Add(new OracleParameter("page_size", OracleDbType.Int32) { Value = pageSize });
        }

        private static void AddSearchParameter(OracleCommand command, string? search)
        {
            command.Parameters.Add(new OracleParameter("search", OracleDbType.Varchar2) { Value = (object?)search ?? DBNull.Value });
        }

        private static AwacsWstype ReadAwacsWstype(OracleDataReader reader)
        {
            return new AwacsWstype
            {
                TblRowId = ReadString(reader, 0),
                LastUpdate = reader.IsDBNull(1) ? null : reader.GetDateTime(1),
                LastUpdatedBy = ReadString(reader, 2),
                WsId = ReadString(reader, 3),
                WsType = ReadString(reader, 4),
                WsDb = ReadString(reader, 5)
            };
        }

        private static AwacsRecipeByWstype ReadRecipeRow(OracleDataReader reader)
        {
            return new AwacsRecipeByWstype
            {
                TblRowId = ReadString(reader, 0),
                LastUpdate = reader.IsDBNull(1) ? null : reader.GetDateTime(1),
                LastUpdatedBy = ReadString(reader, 2),
                WsType = ReadString(reader, 3),
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
    }
}
