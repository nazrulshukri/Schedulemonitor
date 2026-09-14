using Atcbassemblyrecipe.Data;
using Atcbassemblyrecipe.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Oracle.ManagedDataAccess.Client;
using System.Text;
using Atcbassemblyrecipe.Infrastructure;

namespace Atcbassemblyrecipe.Controllers
{
    [Authorize(Roles = AppRole.SuperAdmin)]
    public class DiagnosticsController : Controller
    {
        private readonly IOracleConnectionFactory _connectionFactory;

        public DiagnosticsController(IOracleConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        // /Diagnostics/WireBond - what the APP's own Oracle session can see in
        // TBLWIREBOND, as plain text. This exists because "the grid is empty" has
        // several causes that look identical in the browser: the rows are still
        // uncommitted in somebody's SQL client, the app is signed in to a
        // different schema or service than the client was, or the query itself
        // failed. This says which.
        public async Task<IActionResult> WireBond()
        {
            var report = new StringBuilder();

            try
            {
                await using var connection = _connectionFactory.CreateConnection();
                await connection.OpenAsync();

                report.AppendLine("=== Who the app is connected as ===");
                report.AppendLine(await ScalarAsync(connection,
                    """
                    SELECT 'user=' || USER
                           || '  db=' || SYS_CONTEXT('USERENV', 'DB_NAME')
                           || '  service=' || SYS_CONTEXT('USERENV', 'SERVICE_NAME')
                           || '  schema=' || SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA')
                    FROM dual
                    """));

                report.AppendLine();
                report.AppendLine("=== Which TBLWIREBOND the name resolves to ===");
                report.AppendLine(await ScalarAsync(connection,
                    """
                    SELECT NVL(MAX('owner=' || owner || '  type=' || object_type), 'NOT VISIBLE to this user')
                    FROM all_objects
                    WHERE object_name = 'TBLWIREBOND'
                      AND object_type IN ('TABLE', 'VIEW', 'SYNONYM')
                    """));

                report.AppendLine();
                report.AppendLine("=== Rows this session can see ===");
                report.AppendLine("committed rows = " + await ScalarAsync(connection, "SELECT COUNT(*) FROM tblwirebond"));
                report.AppendLine();
                report.AppendLine("If your SQL client shows rows and this says 0, the rows are not");
                report.AppendLine("committed yet. Run COMMIT in that client and reload this page.");

                report.AppendLine();
                report.AppendLine("=== Newest 5 ===");
                await using (var command = connection.CreateCommand())
                {
                    command.CommandText = """
                        SELECT wbocapno, wbocapwwk,
                               TO_CHAR(wbdate, 'YYYY-MM-DD HH24:MI') AS wbdate,
                               wbmachine, lastupdatedby,
                               TO_CHAR(lastupdate, 'YYYY-MM-DD HH24:MI:SS') AS lastupdate
                        FROM tblwirebond
                        ORDER BY lastupdate DESC NULLS LAST
                        FETCH FIRST 5 ROWS ONLY
                        """;

                    await using var reader = await command.ExecuteReaderTracedAsync();
                    var any = false;
                    while (await reader.ReadAsync())
                    {
                        any = true;
                        report.AppendLine(string.Join(" | ", Enumerable
                            .Range(0, reader.FieldCount)
                            .Select(index => reader.IsDBNull(index) ? "(null)" : reader.GetString(index))));
                    }

                    if (!any)
                    {
                        report.AppendLine("(none)");
                    }
                }

                report.AppendLine();
                report.AppendLine("=== Work-week trigger ===");
                report.AppendLine(await ScalarAsync(connection,
                    """
                    SELECT NVL(MAX('status=' || status || '  enabled=' || DECODE(status, 'ENABLED', 'yes', 'no')),
                               'OCAP_WIREBOND_WORKWEEK NOT FOUND')
                    FROM all_triggers
                    WHERE trigger_name = 'OCAP_WIREBOND_WORKWEEK'
                    """));

                report.AppendLine();
                report.AppendLine("=== get_wwk_app_cutoff (the trigger calls it) ===");
                report.AppendLine(await ScalarAsync(connection,
                    """
                    SELECT NVL(MAX('owner=' || owner || '  status=' || status), 'GET_WWK_APP_CUTOFF NOT VISIBLE')
                    FROM all_objects
                    WHERE object_name = 'GET_WWK_APP_CUTOFF'
                    """));

                return Content(report.ToString(), "text/plain");
            }
            catch (OracleException ex)
            {
                report.AppendLine();
                report.AppendLine($"FAILED with ORA-{ex.Number:00000}: {ex.Message.Trim()}");
                return Content(report.ToString(), "text/plain");
            }
            catch (InvalidOperationException ex)
            {
                report.AppendLine();
                report.AppendLine($"FAILED: {ex.Message}");
                return Content(report.ToString(), "text/plain");
            }
        }

        private static async Task<string> ScalarAsync(OracleConnection connection, string sql)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            var value = await command.ExecuteScalarTracedAsync();
            return value is null or DBNull ? "(null)" : value.ToString() ?? "(null)";
        }

        public async Task<IActionResult> Ocap()
        {
            try
            {
                await using var connection = _connectionFactory.CreateConnection();
                await connection.OpenAsync();

                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT 1 FROM dual";
                var result = await command.ExecuteScalarTracedAsync();

                return Content($"OCAP connection OK. Test query returned {result}.");
            }
            catch (Exception ex) when (ex is OracleException or InvalidOperationException)
            {
                return Content(DatabaseErrorMessage.Build(ex));
            }
        }
    }
}
