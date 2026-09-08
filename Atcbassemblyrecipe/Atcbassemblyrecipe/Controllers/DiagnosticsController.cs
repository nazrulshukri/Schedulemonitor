using Atcbassemblyrecipe.Data;
using Atcbassemblyrecipe.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Oracle.ManagedDataAccess.Client;
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
