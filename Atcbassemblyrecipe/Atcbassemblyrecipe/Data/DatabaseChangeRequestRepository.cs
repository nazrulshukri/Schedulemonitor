using Atcbassemblyrecipe.Models;
using Oracle.ManagedDataAccess.Client;
using Oracle.ManagedDataAccess.Types;
using Atcbassemblyrecipe.Infrastructure;

namespace Atcbassemblyrecipe.Data
{
    public interface IDatabaseChangeRequestRepository
    {
        Task<(bool Success, long RequestId, string Message)> CreateAsync(DatabaseChangeRequest request);
        Task<List<DatabaseChangeRequest>> GetRecentAsync(int maxRows = 50);
        Task<(bool Success, string Message)> SetStatusAsync(long requestId, string status, string reviewedBy, string? reviewNote);
    }

    // TBLDBREQUEST is created by Database/tbldbrequest.sql - it is a table this app
    // owns, unlike TBLACCESS/AWACSWSTYPE which pre-exist in the MES schema.
    public class DatabaseChangeRequestRepository : IDatabaseChangeRequestRepository
    {
        private readonly IOracleConnectionFactory _connectionFactory;

        public DatabaseChangeRequestRepository(IOracleConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<(bool Success, long RequestId, string Message)> CreateAsync(DatabaseChangeRequest request)
        {
            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync();
            await using var transaction = connection.BeginTransaction();

            try
            {
                await using var command = connection.CreateCommand();
                command.BindByName = true;
                command.Transaction = transaction;

                // REQUEST_ID is GENERATED ALWAYS AS IDENTITY, so it is never supplied -
                // it is read back through the RETURNING clause instead.
                command.CommandText = """
                    INSERT INTO tbldbrequest
                        (request_type, target_table, reason, requested_by, requested_date, status)
                    VALUES
                        (:request_type, :target_table, :reason, :requested_by, SYSDATE, :status)
                    RETURNING request_id INTO :request_id
                    """;
                command.Parameters.Add(new OracleParameter("request_type", request.RequestType));
                command.Parameters.Add(new OracleParameter("target_table", request.TargetTable));
                command.Parameters.Add(new OracleParameter("reason", request.Reason));
                command.Parameters.Add(new OracleParameter("requested_by", request.RequestedBy));
                command.Parameters.Add(new OracleParameter("status", RequestStatus.Pending));

                var idParameter = new OracleParameter("request_id", OracleDbType.Int64)
                {
                    Direction = System.Data.ParameterDirection.Output
                };
                command.Parameters.Add(idParameter);

                var inserted = await command.ExecuteNonQueryTracedAsync();
                if (inserted != 1)
                {
                    await transaction.RollbackAsync();
                    return (false, 0, "Request could not be saved. Nothing was recorded.");
                }

                await transaction.CommitAsync();

                var requestId = idParameter.Value is OracleDecimal value
                    ? (long)value
                    : Convert.ToInt64(idParameter.Value?.ToString() ?? "0");

                return (true, requestId, "Request recorded.");
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<List<DatabaseChangeRequest>> GetRecentAsync(int maxRows = 50)
        {
            var rows = new List<DatabaseChangeRequest>();

            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.BindByName = true;
            command.CommandText = """
                SELECT request_id, request_type, target_table, reason, requested_by,
                       requested_date, status, reviewed_by, reviewed_date, review_note
                FROM tbldbrequest
                ORDER BY CASE status WHEN 'PENDING' THEN 0 ELSE 1 END, requested_date DESC
                FETCH FIRST :max_rows ROWS ONLY
                """;
            command.Parameters.Add(new OracleParameter("max_rows", OracleDbType.Int32) { Value = maxRows });

            await using var reader = await command.ExecuteReaderTracedAsync();
            while (await reader.ReadAsync())
            {
                rows.Add(new DatabaseChangeRequest
                {
                    RequestId = reader.GetInt64(0),
                    RequestType = ReadString(reader, 1),
                    TargetTable = ReadString(reader, 2),
                    Reason = ReadString(reader, 3),
                    RequestedBy = ReadString(reader, 4),
                    RequestedDate = reader.IsDBNull(5) ? DateTime.MinValue : reader.GetDateTime(5),
                    Status = ReadString(reader, 6),
                    ReviewedBy = ReadString(reader, 7),
                    ReviewedDate = reader.IsDBNull(8) ? null : reader.GetDateTime(8),
                    ReviewNote = ReadString(reader, 9)
                });
            }

            return rows;
        }

        public async Task<(bool Success, string Message)> SetStatusAsync(long requestId, string status, string reviewedBy, string? reviewNote)
        {
            if (!RequestStatus.All.Contains(status))
            {
                return (false, "Unknown review status.");
            }

            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync();
            await using var transaction = connection.BeginTransaction();

            try
            {
                await using var command = connection.CreateCommand();
                command.BindByName = true;
                command.Transaction = transaction;
                command.CommandText = """
                    UPDATE tbldbrequest
                    SET status = :status,
                        reviewed_by = :reviewed_by,
                        reviewed_date = SYSDATE,
                        review_note = :review_note
                    WHERE request_id = :request_id
                      AND status = 'PENDING'
                    """;
                command.Parameters.Add(new OracleParameter("status", status));
                command.Parameters.Add(new OracleParameter("reviewed_by", reviewedBy));
                command.Parameters.Add(new OracleParameter("review_note", (object?)reviewNote ?? DBNull.Value));
                command.Parameters.Add(new OracleParameter("request_id", requestId));

                var updated = await command.ExecuteNonQueryTracedAsync();
                if (updated != 1)
                {
                    await transaction.RollbackAsync();
                    return (false, "That request was not found, or somebody already reviewed it.");
                }

                await transaction.CommitAsync();
                return (true, $"Request #{requestId} marked {status.ToLowerInvariant()}.");
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        private static string ReadString(OracleDataReader reader, int index)
        {
            return reader.IsDBNull(index) ? string.Empty : reader.GetString(index);
        }
    }
}
