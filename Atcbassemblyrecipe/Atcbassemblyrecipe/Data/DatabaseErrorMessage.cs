using Oracle.ManagedDataAccess.Client;

namespace Atcbassemblyrecipe.Data
{
    public static class DatabaseErrorMessage
    {
        public static string Build(Exception exception)
        {
            if (exception is InvalidOperationException)
            {
                return exception.Message;
            }

            if (exception is OracleException oracleException)
            {
                return $"Oracle/OCAP connection failed ({oracleException.Number}). Check OCAP password, VPN/network access, service name QAAPPS, host atcbfaq-scan.ph-cub01.nexperia.com, and listener port 1521.";
            }

            return "Oracle/OCAP connection failed. Check OCAP password, VPN/network access, service name QAAPPS, host atcbfaq-scan.ph-cub01.nexperia.com, and listener port 1521.";
        }
    }
}
