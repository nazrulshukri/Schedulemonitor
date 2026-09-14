using Atcbassemblyrecipe.Data;
using Atcbassemblyrecipe.Models;
using Microsoft.AspNetCore.Http;
using Oracle.ManagedDataAccess.Client;

namespace Atcbassemblyrecipe.Services
{
    public record ModulePermission(bool CanView, bool CanAdd, bool CanUpdate, bool CanDelete)
    {
        public static readonly ModulePermission None = new(false, false, false, false);
        public static readonly ModulePermission Full = new(true, true, true, true);
    }

    public interface IAccessEvaluator
    {
        Task<ModulePermission> GetAsync(string moduleName);
    }

    // Answers "what may the *currently signed-in* user do in this module?" by reading
    // their TBLACCESS module grants. Registered scoped and caches the profile for the
    // lifetime of one request, so a page render costs at most one extra query.
    //
    // Before this existed the app gated every Add/Edit/Delete control on role alone
    // (SuperAdmin/Admin), which meant the per-module checkboxes saved in Access
    // Management were written to TBLACCESS and displayed back, but never actually
    // consulted - granting a User row Update/Delete had no visible effect.
    public class AccessEvaluator : IAccessEvaluator
    {
        private readonly IAccessRepository _accessRepository;
        private readonly IHttpContextAccessor _httpContextAccessor;

        private Dictionary<string, string>? _grantsByModule;
        private bool _loaded;

        public AccessEvaluator(IAccessRepository accessRepository, IHttpContextAccessor httpContextAccessor)
        {
            _accessRepository = accessRepository;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<ModulePermission> GetAsync(string moduleName)
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true)
            {
                return ModulePermission.None;
            }

            // A Super Admin always keeps full access. Without this, revoking your own
            // Access Management grant (or an Oracle outage) would leave nobody able to
            // reach the page that hands permissions back out.
            if (user.IsInRole(AppRole.SuperAdmin))
            {
                return ModulePermission.Full;
            }

            var grants = await LoadGrantsAsync(user.Identity.Name);
            var (canView, canAdd, canUpdate, canDelete) =
                AccessLevel.Parse(grants.GetValueOrDefault(moduleName));

            return new ModulePermission(canView, canAdd, canUpdate, canDelete);
        }

        private async Task<Dictionary<string, string>> LoadGrantsAsync(string? userId)
        {
            if (_loaded)
            {
                return _grantsByModule!;
            }

            _grantsByModule = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _loaded = true;

            if (string.IsNullOrWhiteSpace(userId))
            {
                return _grantsByModule;
            }

            try
            {
                var profile = await _accessRepository.GetProfileAsync(userId);
                if (profile is null)
                {
                    return _grantsByModule;
                }

                foreach (var grant in profile.ModuleGrants)
                {
                    _grantsByModule[grant.ModuleName] = grant.AccessLevel;
                }
            }
            catch (Exception ex) when (ex is OracleException or InvalidOperationException)
            {
                // Fail closed: if TBLACCESS cannot be read we grant nothing rather than
                // guessing. Super Admins already returned Full above, so the console
                // remains reachable to fix the outage.
            }

            return _grantsByModule;
        }
    }
}
