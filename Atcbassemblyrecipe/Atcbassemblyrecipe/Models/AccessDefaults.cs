namespace Atcbassemblyrecipe.Models
{
    // Starting module permissions applied when a user is first provisioned
    // (either auto-provisioned on their first LDAP login, or manually added
    // by a Super Admin). A Super Admin can always change these afterwards
    // from the Access Management grid.
    public static class AccessDefaults
    {
        public static Dictionary<string, string> BuildModuleAccessLevels(string roleName)
        {
            var isSuperAdmin = roleName == AppRole.SuperAdmin;
            var isAdmin = roleName == AppRole.Admin;
            var canChange = isSuperAdmin || isAdmin;

            var levels = new Dictionary<string, string>();
            foreach (var module in AppModules.All)
            {
                var restricted = module.Name is "DB Validation" or "Access Management";
                var canView = restricted ? isSuperAdmin : true;
                var canAdd = restricted ? isSuperAdmin : canChange;
                var canUpdate = restricted ? isSuperAdmin : canChange;
                var canDelete = isSuperAdmin;

                levels[module.Name] = AccessLevel.Build(canView, canAdd, canUpdate, canDelete);
            }

            return levels;
        }
    }
}
