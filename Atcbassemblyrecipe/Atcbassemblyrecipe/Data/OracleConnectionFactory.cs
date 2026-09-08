using Oracle.ManagedDataAccess.Client;

namespace Atcbassemblyrecipe.Data
{
    public interface IOracleConnectionFactory
    {
        OracleConnection CreateConnection();
    }

    public class OracleConnectionFactory : IOracleConnectionFactory
    {
        private readonly string _connectionString;
        private readonly string _connectionName;

        public OracleConnectionFactory(IConfiguration configuration)
        {
            _connectionName = configuration.GetConnectionString("MES") is not null ? "MES" : "OCAP";
            _connectionString = configuration.GetConnectionString(_connectionName)
                ?? throw new InvalidOperationException("Connection string 'MES' or 'OCAP' is missing.");
        }

        public OracleConnection CreateConnection()
        {
            if (_connectionString.Contains("YOUR_USERNAME", StringComparison.OrdinalIgnoreCase)
                || _connectionString.Contains("YOUR_PASSWORD", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Connection string '{_connectionName}' still contains placeholder username or password. LDAP login does not need this, but AWACSWSTYPE/TBLSAWING pages need a valid OCAP connection.");
            }

            return new OracleConnection(_connectionString);
        }
    }
}
