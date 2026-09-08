using System.ComponentModel.DataAnnotations;
using Atcbassemblyrecipe.Models;

namespace Atcbassemblyrecipe.ViewModels
{
    public class AccessManagementViewModel
    {
        public string? Search { get; set; }
        public string? SelectedUserId { get; set; }
        public AccessUserViewModel? SelectedUser { get; set; }
        public List<AccessUserViewModel> Users { get; set; } = [];
        public List<TableAccessViewModel> TableAccess { get; set; } = [];

        // Blank permission rows used to render the New User form's grid.
        public List<TableAccessViewModel> NewUserAccess { get; set; } = [];

        public int TotalTables => TableAccess.Count;
        public int WithAccess => TableAccess.Count(item => item.CanView);
        public int CanUpdateCount => TableAccess.Count(item => item.CanUpdate);
        public int CanDeleteCount => TableAccess.Count(item => item.CanDelete);

        public static string RoleLabel(string? role)
        {
            return role switch
            {
                AppRole.SuperAdmin => "Super Admin",
                AppRole.Admin => "Admin",
                _ => "User"
            };
        }
    }

    public class AccessUserViewModel
    {
        public string UserId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string Email => $"{UserId}@nexperia.com";
        public string Role { get; set; } = AppRole.User;
        public string Status { get; set; } = AccessStatus.Active;
        public bool IsActive => Status == AccessStatus.Active;
        public DateTime? GrantedDate { get; set; }

        // Not a TBLACCESS column - populated by AccessController from IAvatarStorage,
        // which keeps uploaded photos on disk under wwwroot/uploads/avatars.
        public string? AvatarUrl { get; set; }
    }

    public class TableAccessViewModel
    {
        public string Module { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool CanView { get; set; }
        public bool CanAdd { get; set; }
        public bool CanUpdate { get; set; }
        public bool CanDelete { get; set; }
    }

    // Posted back by the "Save Validation" form: chosen role plus the edited
    // View/Add/Update/Delete checkboxes for every module row in the grid.
    public class AccessSaveInputModel
    {
        [Required, StringLength(50)]
        public string UserId { get; set; } = string.Empty;

        [StringLength(100)]
        public string? UserName { get; set; }

        [Required, StringLength(30)]
        public string RoleName { get; set; } = AppRole.User;

        public List<TableAccessViewModel> TableAccess { get; set; } = [];
    }

    public class AddAccessUserInputModel
    {
        [Required, StringLength(50)]
        public string UserId { get; set; } = string.Empty;

        [StringLength(100)]
        public string? UserName { get; set; }

        [Required, StringLength(30)]
        public string RoleName { get; set; } = AppRole.User;

        // Module permissions chosen on the New User form, applied in the same
        // step as creating the user.
        public List<TableAccessViewModel> TableAccess { get; set; } = [];
    }
}
