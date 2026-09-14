namespace Atcbassemblyrecipe.Models
{
    // One row in TBLACCESS. MODULE_NAME = AccessGrant.AccountModule is the reserved
    // "account" row that carries the user's overall ROLE_NAME/STATUS; every other
    // MODULE_NAME row only carries that module's ACCESS_LEVEL grant.
    public class AccessGrant
    {
        public const string AccountModule = "ACCOUNT";

        public long AccessId { get; set; }
        public string UserId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string RoleName { get; set; } = AppRole.User;
        public string ModuleName { get; set; } = string.Empty;
        public string AccessLevel { get; set; } = string.Empty;
        public string GrantedBy { get; set; } = string.Empty;
        public DateTime GrantedDate { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public string Status { get; set; } = AccessStatus.Active;
        public DateTime CreatedDate { get; set; }
        public DateTime UpdatedDate { get; set; }
    }

    // A user's overall account row (MODULE_NAME = ACCOUNT) plus the derived
    // per-module permissions used to render/edit the Access Management grid.
    public class AccessProfile
    {
        public string UserId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string RoleName { get; set; } = AppRole.User;
        public string Status { get; set; } = AccessStatus.Active;
        public DateTime GrantedDate { get; set; }
        public List<AccessGrant> ModuleGrants { get; set; } = [];
    }

    // TBLACCESS.STATUS has a check constraint: status IN ('ACTIVE','INACTIVE','REVOKED').
    // "Blocked" in the app UI is stored as REVOKED - there is no literal BLOCKED value.
    public static class AccessStatus
    {
        public const string Active = "ACTIVE";
        public const string Blocked = "REVOKED";

        public static readonly string[] All = [Active, Blocked];
    }

    // TBLACCESS.ACCESS_LEVEL has a check constraint:
    // access_level IN ('READ','WRITE','ADMIN','NONE') - it is one tier per row, not a
    // combinable list of flags. The Access Management grid's View/Add/Update/Delete
    // checkboxes are quantized down to these four tiers on save and re-expanded on load:
    //   NONE  -> nothing
    //   READ  -> View only
    //   WRITE -> View, Add, Update (no Delete)
    //   ADMIN -> View, Add, Update, Delete
    public static class AccessLevel
    {
        public const string None = "NONE";
        public const string Read = "READ";
        public const string Write = "WRITE";
        public const string Admin = "ADMIN";

        public static readonly string[] All = [Read, Write, Admin];

        public static string Build(bool canView, bool canAdd, bool canUpdate, bool canDelete)
        {
            if (canDelete)
            {
                return Admin;
            }

            if (canAdd || canUpdate)
            {
                return Write;
            }

            return canView ? Read : None;
        }

        public static (bool CanView, bool CanAdd, bool CanUpdate, bool CanDelete) Parse(string? accessLevel)
        {
            return accessLevel?.Trim().ToUpperInvariant() switch
            {
                Admin => (true, true, true, true),
                Write => (true, true, true, false),
                Read => (true, false, false, false),
                _ => (false, false, false, false)
            };
        }
    }
}
