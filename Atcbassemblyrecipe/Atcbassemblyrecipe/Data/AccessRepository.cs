using Atcbassemblyrecipe.Models;
using Oracle.ManagedDataAccess.Client;
using Atcbassemblyrecipe.Infrastructure;

namespace Atcbassemblyrecipe.Data
{
    public record ModuleAccessGrant(string ModuleName, bool CanView, bool CanAdd, bool CanUpdate, bool CanDelete);

    public interface IAccessRepository
    {
        Task<List<AccessProfile>> GetAllProfilesAsync();
        Task<List<string>> GetActiveUserIdsByRoleAsync(string roleName);
        Task<AccessProfile?> GetProfileAsync(string userId);
        Task<AccessProfile> ProvisionUserAsync(string userId, string userName, string grantedBy, string defaultRole = AppRole.User);
        Task<(bool Success, string Message)> AddUserAsync(string userId, string userName, string roleName, string grantedBy);
        Task<(bool Success, string Message)> SaveAccessAsync(string userId, string userName, string roleName, string grantedBy, IReadOnlyList<ModuleAccessGrant> modules);
        Task<(bool Success, string Message)> SetStatusAsync(string userId, string status, string updatedBy);
        Task<(bool Success, string Message)> DeleteUserAsync(string userId);
    }

    // TBLACCESS is a pre-existing MES/OCAP table (see Database/schema.sql for the assumed
    // column layout). One row per (USER_ID, MODULE_NAME). MODULE_NAME = 'ACCOUNT' is a
    // reserved row that carries the user's overall ROLE_NAME/STATUS; every other
    // MODULE_NAME row only carries that module's ACCESS_LEVEL (a comma list of
    // VIEW/ADD/UPDATE/DELETE, or NONE).
    public class AccessRepository : IAccessRepository
    {
        private readonly IOracleConnectionFactory _connectionFactory;

        public AccessRepository(IOracleConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<List<AccessProfile>> GetAllProfilesAsync()
        {
            var profiles = new List<AccessProfile>();

            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.BindByName = true;
            command.CommandText = """
                SELECT user_id, user_name, role_name, status, granted_date
                FROM tblaccess
                WHERE module_name = :module_name
                ORDER BY
                    CASE role_name WHEN 'SuperAdmin' THEN 0 WHEN 'Admin' THEN 1 ELSE 2 END,
                    user_name
                """;
            command.Parameters.Add(new OracleParameter("module_name", AccessGrant.AccountModule));

            await using var reader = await command.ExecuteReaderTracedAsync();
            while (await reader.ReadAsync())
            {
                profiles.Add(new AccessProfile
                {
                    UserId = ReadString(reader, 0),
                    UserName = ReadString(reader, 1),
                    RoleName = ReadString(reader, 2),
                    Status = ReadString(reader, 3),
                    GrantedDate = reader.IsDBNull(4) ? DateTime.MinValue : reader.GetDateTime(4)
                });
            }

            return profiles;
        }

        // Used to work out who should be notified about DB change requests.
        // Blocked/revoked accounts are excluded so they stop receiving mail.
        public async Task<List<string>> GetActiveUserIdsByRoleAsync(string roleName)
        {
            var userIds = new List<string>();

            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.BindByName = true;
            command.CommandText = """
                SELECT user_id
                FROM tblaccess
                WHERE module_name = :module_name
                  AND role_name = :role_name
                  AND status = :status
                ORDER BY user_id
                """;
            command.Parameters.Add(new OracleParameter("module_name", AccessGrant.AccountModule));
            command.Parameters.Add(new OracleParameter("role_name", roleName));
            command.Parameters.Add(new OracleParameter("status", AccessStatus.Active));

            await using var reader = await command.ExecuteReaderTracedAsync();
            while (await reader.ReadAsync())
            {
                userIds.Add(ReadString(reader, 0));
            }

            return userIds;
        }

        public async Task<AccessProfile?> GetProfileAsync(string userId)
        {
            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync();

            return await GetProfileAsync(connection, null, userId);
        }

        private static async Task<AccessProfile?> GetProfileAsync(OracleConnection connection, OracleTransaction? transaction, string userId)
        {
            await using var command = connection.CreateCommand();
            command.BindByName = true;
            command.Transaction = transaction;
            command.CommandText = """
                SELECT user_id, user_name, role_name, module_name, access_level, granted_by,
                       granted_date, expiry_date, status, created_date, updated_date
                FROM tblaccess
                WHERE user_id = :user_id
                """;
            command.Parameters.Add(new OracleParameter("user_id", userId));

            AccessProfile? profile = null;
            var moduleGrants = new List<AccessGrant>();

            await using var reader = await command.ExecuteReaderTracedAsync();
            while (await reader.ReadAsync())
            {
                var grant = new AccessGrant
                {
                    UserId = ReadString(reader, 0),
                    UserName = ReadString(reader, 1),
                    RoleName = ReadString(reader, 2),
                    ModuleName = ReadString(reader, 3),
                    AccessLevel = ReadString(reader, 4),
                    GrantedBy = ReadString(reader, 5),
                    GrantedDate = reader.IsDBNull(6) ? DateTime.MinValue : reader.GetDateTime(6),
                    ExpiryDate = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                    Status = ReadString(reader, 8),
                    CreatedDate = reader.IsDBNull(9) ? DateTime.MinValue : reader.GetDateTime(9),
                    UpdatedDate = reader.IsDBNull(10) ? DateTime.MinValue : reader.GetDateTime(10)
                };

                if (grant.ModuleName == AccessGrant.AccountModule)
                {
                    profile = new AccessProfile
                    {
                        UserId = grant.UserId,
                        UserName = grant.UserName,
                        RoleName = grant.RoleName,
                        Status = grant.Status,
                        GrantedDate = grant.GrantedDate
                    };
                }
                else
                {
                    moduleGrants.Add(grant);
                }
            }

            if (profile is null)
            {
                return null;
            }

            profile.ModuleGrants = moduleGrants;
            return profile;
        }

        public async Task<AccessProfile> ProvisionUserAsync(string userId, string userName, string grantedBy, string defaultRole = AppRole.User)
        {
            var existing = await GetProfileAsync(userId);
            if (existing is not null)
            {
                return existing;
            }

            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync();
            await using var transaction = connection.BeginTransaction();

            try
            {
                await InsertUserAsync(connection, transaction, userId, userName, defaultRole, grantedBy);
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }

            return await GetProfileAsync(userId)
                ?? throw new InvalidOperationException($"Failed to provision TBLACCESS row for '{userId}'.");
        }

        public async Task<(bool Success, string Message)> AddUserAsync(string userId, string userName, string roleName, string grantedBy)
        {
            userId = userId.Trim();
            userName = string.IsNullOrWhiteSpace(userName) ? userId : userName.Trim();

            var existing = await GetProfileAsync(userId);
            if (existing is not null)
            {
                return (false, $"{userId} already has TBLACCESS entries.");
            }

            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync();
            await using var transaction = connection.BeginTransaction();

            try
            {
                await InsertUserAsync(connection, transaction, userId, userName, roleName, grantedBy);

                var verified = await GetProfileAsync(connection, transaction, userId);
                if (verified is null)
                {
                    await transaction.RollbackAsync();
                    return (false, "Insert verification failed. Insert was rolled back.");
                }

                await transaction.CommitAsync();
                return (true, $"{userId} added with role {roleName}.");
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<(bool Success, string Message)> SaveAccessAsync(string userId, string userName, string roleName, string grantedBy, IReadOnlyList<ModuleAccessGrant> modules)
        {
            userId = userId.Trim();
            userName = string.IsNullOrWhiteSpace(userName) ? userId : userName.Trim();

            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync();
            await using var transaction = connection.BeginTransaction();

            try
            {
                var existing = await GetProfileAsync(connection, transaction, userId);
                if (existing is null)
                {
                    await InsertUserAsync(connection, transaction, userId, userName, roleName, grantedBy, includeDefaultModules: false);
                }
                else
                {
                    await using var updateAccount = connection.CreateCommand();
                    updateAccount.BindByName = true;
                    updateAccount.Transaction = transaction;
                    updateAccount.CommandText = """
                        UPDATE tblaccess
                        SET role_name = :role_name,
                            user_name = :user_name,
                            granted_by = :granted_by,
                            granted_date = SYSDATE,
                            updated_date = SYSDATE
                        WHERE user_id = :user_id
                          AND module_name = :module_name
                        """;
                    updateAccount.Parameters.Add(new OracleParameter("role_name", roleName));
                    updateAccount.Parameters.Add(new OracleParameter("user_name", userName));
                    updateAccount.Parameters.Add(new OracleParameter("granted_by", grantedBy));
                    updateAccount.Parameters.Add(new OracleParameter("user_id", userId));
                    updateAccount.Parameters.Add(new OracleParameter("module_name", AccessGrant.AccountModule));
                    await updateAccount.ExecuteNonQueryTracedAsync();
                }

                foreach (var module in modules)
                {
                    var accessLevel = AccessLevel.Build(module.CanView, module.CanAdd, module.CanUpdate, module.CanDelete);
                    await UpsertModuleGrantAsync(connection, transaction, userId, userName, roleName, module.ModuleName, accessLevel, grantedBy);
                }

                var verified = await GetProfileAsync(connection, transaction, userId);
                if (verified is null || verified.RoleName != roleName)
                {
                    await transaction.RollbackAsync();
                    return (false, "Access save verification failed. Changes were rolled back.");
                }

                await transaction.CommitAsync();
                return (true, $"Access validation saved for {userId}.");
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<(bool Success, string Message)> SetStatusAsync(string userId, string status, string updatedBy)
        {
            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync();
            await using var transaction = connection.BeginTransaction();

            try
            {
                await using var command = connection.CreateCommand();
                command.BindByName = true;
                command.Transaction = transaction;
                command.CommandText = """
                    UPDATE tblaccess
                    SET status = :status,
                        updated_date = SYSDATE
                    WHERE user_id = :user_id
                      AND module_name = :module_name
                    """;
                command.Parameters.Add(new OracleParameter("status", status));
                command.Parameters.Add(new OracleParameter("user_id", userId));
                command.Parameters.Add(new OracleParameter("module_name", AccessGrant.AccountModule));

                var updated = await command.ExecuteNonQueryTracedAsync();
                if (updated != 1)
                {
                    await transaction.RollbackAsync();
                    return (false, $"{userId} was not found in TBLACCESS.");
                }

                await transaction.CommitAsync();
                return (true, status == AccessStatus.Blocked
                    ? $"{userId} is now blocked and cannot log in."
                    : $"{userId} is now active.");
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<(bool Success, string Message)> DeleteUserAsync(string userId)
        {
            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync();
            await using var transaction = connection.BeginTransaction();

            try
            {
                await using var command = connection.CreateCommand();
                command.BindByName = true;
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM tblaccess WHERE user_id = :user_id";
                command.Parameters.Add(new OracleParameter("user_id", userId));

                var deleted = await command.ExecuteNonQueryTracedAsync();
                if (deleted == 0)
                {
                    await transaction.RollbackAsync();
                    return (false, $"{userId} was not found in TBLACCESS.");
                }

                var stillExists = await GetProfileAsync(connection, transaction, userId);
                if (stillExists is not null)
                {
                    await transaction.RollbackAsync();
                    return (false, "Delete verification failed. Delete was rolled back.");
                }

                await transaction.CommitAsync();
                return (true, $"{userId} removed from TBLACCESS.");
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        private static async Task InsertUserAsync(OracleConnection connection, OracleTransaction transaction, string userId, string userName, string roleName, string grantedBy, bool includeDefaultModules = true)
        {
            await InsertGrantRowAsync(connection, transaction, userId, userName, roleName, AccessGrant.AccountModule, AccessLevel.None, grantedBy, AccessStatus.Active);

            if (!includeDefaultModules)
            {
                return;
            }

            foreach (var (moduleName, accessLevel) in AccessDefaults.BuildModuleAccessLevels(roleName))
            {
                if (accessLevel == AccessLevel.None)
                {
                    continue;
                }

                await InsertGrantRowAsync(connection, transaction, userId, userName, roleName, moduleName, accessLevel, grantedBy, AccessStatus.Active);
            }
        }

        private static async Task UpsertModuleGrantAsync(OracleConnection connection, OracleTransaction transaction, string userId, string userName, string roleName, string moduleName, string accessLevel, string grantedBy)
        {
            await using var existsCommand = connection.CreateCommand();
            existsCommand.BindByName = true;
            existsCommand.Transaction = transaction;
            existsCommand.CommandText = "SELECT COUNT(1) FROM tblaccess WHERE user_id = :user_id AND module_name = :module_name";
            existsCommand.Parameters.Add(new OracleParameter("user_id", userId));
            existsCommand.Parameters.Add(new OracleParameter("module_name", moduleName));
            var exists = Convert.ToInt32(await existsCommand.ExecuteScalarTracedAsync()) > 0;

            if (exists)
            {
                await using var updateCommand = connection.CreateCommand();
                updateCommand.BindByName = true;
                updateCommand.Transaction = transaction;
                updateCommand.CommandText = """
                    UPDATE tblaccess
                    SET access_level = :access_level,
                        role_name = :role_name,
                        user_name = :user_name,
                        granted_by = :granted_by,
                        granted_date = SYSDATE,
                        updated_date = SYSDATE
                    WHERE user_id = :user_id
                      AND module_name = :module_name
                    """;
                updateCommand.Parameters.Add(new OracleParameter("access_level", accessLevel));
                updateCommand.Parameters.Add(new OracleParameter("role_name", roleName));
                updateCommand.Parameters.Add(new OracleParameter("user_name", userName));
                updateCommand.Parameters.Add(new OracleParameter("granted_by", grantedBy));
                updateCommand.Parameters.Add(new OracleParameter("user_id", userId));
                updateCommand.Parameters.Add(new OracleParameter("module_name", moduleName));
                await updateCommand.ExecuteNonQueryTracedAsync();
                return;
            }

            if (accessLevel == AccessLevel.None)
            {
                return;
            }

            await InsertGrantRowAsync(connection, transaction, userId, userName, roleName, moduleName, accessLevel, grantedBy, AccessStatus.Active);
        }

        // ACCESS_ID is a GENERATED ALWAYS identity column in the real TBLACCESS table, so it
        // must never appear in the INSERT column/value list - Oracle raises ORA-32795 if it does.
        private static async Task InsertGrantRowAsync(OracleConnection connection, OracleTransaction transaction, string userId, string userName, string roleName, string moduleName, string accessLevel, string grantedBy, string status)
        {
            await using var command = connection.CreateCommand();
            command.BindByName = true;
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO tblaccess
                    (user_id, user_name, role_name, module_name, access_level,
                     granted_by, granted_date, expiry_date, status, created_date, updated_date)
                VALUES
                    (:user_id, :user_name, :role_name, :module_name, :access_level,
                     :granted_by, SYSDATE, NULL, :status, SYSDATE, SYSDATE)
                """;
            command.Parameters.Add(new OracleParameter("user_id", userId));
            command.Parameters.Add(new OracleParameter("user_name", userName));
            command.Parameters.Add(new OracleParameter("role_name", roleName));
            command.Parameters.Add(new OracleParameter("module_name", moduleName));
            command.Parameters.Add(new OracleParameter("access_level", accessLevel));
            command.Parameters.Add(new OracleParameter("granted_by", grantedBy));
            command.Parameters.Add(new OracleParameter("status", status));

            await command.ExecuteNonQueryTracedAsync();
        }

        private static string ReadString(OracleDataReader reader, int index)
        {
            return reader.IsDBNull(index) ? string.Empty : reader.GetString(index);
        }
    }
}
