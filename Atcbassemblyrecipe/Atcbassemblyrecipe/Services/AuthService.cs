using Atcbassemblyrecipe.Data;
using Atcbassemblyrecipe.Models;
using System.DirectoryServices;
using System.Runtime.Versioning;
using System.Text;

namespace Atcbassemblyrecipe.Services
{
    public interface IAuthService
    {
        Task<AppUser?> ValidateLoginAsync(string userName, string password);
        Task<(bool Success, string Message)> RegisterAsync(AppUser user, string password);
    }

    [SupportedOSPlatform("windows")]
    public class AuthService : IAuthService
    {
        private const string DefaultDomainPath = "LDAP://nws.nexperia.com/DC=NWS,DC=NEXPERIA,DC=COM";
        private const string AutoProvisionedBy = "LDAP-AutoProvision";
        private readonly IConfiguration _configuration;
        private readonly IAccessRepository _accessRepository;

        public AuthService(IConfiguration configuration, IAccessRepository accessRepository)
        {
            _configuration = configuration;
            _accessRepository = accessRepository;
        }

        public async Task<AppUser?> ValidateLoginAsync(string userName, string password)
        {
            if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
            {
                return null;
            }

            var ldapAccount = ValidateLdapUser(userName.Trim(), password);
            if (ldapAccount is null)
            {
                return null;
            }

            // First successful LDAP login for an account creates its TBLACCESS row so a
            // Super Admin can see it and adjust role/module access from the Access
            // Management page. Once that row exists, TBLACCESS is always the source of
            // truth; appsettings Security:SuperAdmins/Admins only seeds the very first
            // provision, so there is always a way to bootstrap the first Super Admin
            // even though Access Management itself requires the Super Admin role to open.
            var profile = await _accessRepository.GetProfileAsync(ldapAccount.Account)
                ?? await _accessRepository.ProvisionUserAsync(ldapAccount.Account, ldapAccount.Name, AutoProvisionedBy, ResolveBootstrapRole(ldapAccount.Account));

            if (profile.Status == AccessStatus.Blocked)
            {
                throw new InvalidOperationException($"{ldapAccount.Account} is blocked. Contact your Super Admin to restore access.");
            }

            return new AppUser
            {
                Id = StablePositiveId(ldapAccount.Account),
                UserName = ldapAccount.Account,
                Email = ldapAccount.Email,
                Role = profile.RoleName,
                IsActive = true
            };
        }

        public Task<(bool Success, string Message)> RegisterAsync(AppUser user, string password)
        {
            return Task.FromResult((false, "Register is disabled. Use your Nexperia LDAP account to login."));
        }

        private LdapAccount? ValidateLdapUser(string account, string password)
        {
            var domainPath = _configuration["Ldap:DomainPath"];
            if (string.IsNullOrWhiteSpace(domainPath))
            {
                domainPath = DefaultDomainPath;
            }

            var samAccountName = ExtractSamAccountName(account);

            try
            {
                using var entry = new DirectoryEntry(domainPath, account, password);
                using var searcher = new DirectorySearcher(entry)
                {
                    Filter = $"(&(objectClass=user)(sAMAccountName={EscapeLdapFilterValue(samAccountName)}))"
                };
                searcher.PropertiesToLoad.Add("mail");
                searcher.PropertiesToLoad.Add("displayName");

                var result = searcher.FindOne();
                if (result is null)
                {
                    return null;
                }

                using var loggedInEntry = result.GetDirectoryEntry();
                return new LdapAccount
                {
                    Account = samAccountName,
                    Email = GetProperty(loggedInEntry, "mail", $"{samAccountName}@nexperia.com"),
                    Name = GetProperty(loggedInEntry, "displayName", samAccountName)
                };
            }
            catch (DirectoryServicesCOMException)
            {
                return null;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("LDAP validation failed. Check Nexperia network/VPN access and LDAP domain path.", ex);
            }
        }

        private string ResolveBootstrapRole(string account)
        {
            if (IsConfiguredRoleMember("Security:SuperAdmins", account))
            {
                return AppRole.SuperAdmin;
            }

            if (IsConfiguredRoleMember("Security:Admins", account))
            {
                return AppRole.Admin;
            }

            return AppRole.User;
        }

        private bool IsConfiguredRoleMember(string sectionName, string account)
        {
            return _configuration
                .GetSection(sectionName)
                .GetChildren()
                .Select(item => item.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Any(value => string.Equals(value, account, StringComparison.OrdinalIgnoreCase));
        }

        private static string ExtractSamAccountName(string account)
        {
            var slashIndex = account.LastIndexOf('\\');
            if (slashIndex >= 0 && slashIndex < account.Length - 1)
            {
                return account[(slashIndex + 1)..];
            }

            var atIndex = account.IndexOf('@');
            return atIndex > 0 ? account[..atIndex] : account;
        }

        private static string GetProperty(DirectoryEntry entry, string propertyName, string fallback)
        {
            return entry.Properties[propertyName].Count > 0
                ? entry.Properties[propertyName][0]?.ToString() ?? fallback
                : fallback;
        }

        private static int StablePositiveId(string value)
        {
            return Math.Abs(StringComparer.OrdinalIgnoreCase.GetHashCode(value));
        }

        private static string EscapeLdapFilterValue(string value)
        {
            var builder = new StringBuilder();
            foreach (var character in value)
            {
                builder.Append(character switch
                {
                    '\\' => "\\5c",
                    '*' => "\\2a",
                    '(' => "\\28",
                    ')' => "\\29",
                    '\0' => "\\00",
                    _ => character
                });
            }

            return builder.ToString();
        }
    }
}
