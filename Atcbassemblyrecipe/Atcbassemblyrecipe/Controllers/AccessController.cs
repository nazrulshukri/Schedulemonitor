using Atcbassemblyrecipe.Data;
using Atcbassemblyrecipe.Models;
using Atcbassemblyrecipe.Services;
using Atcbassemblyrecipe.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Oracle.ManagedDataAccess.Client;

namespace Atcbassemblyrecipe.Controllers
{
    [Authorize(Roles = AppRole.SuperAdmin)]
    public class AccessController : Controller
    {
        private readonly IAccessRepository _accessRepository;
        private readonly IAvatarStorage _avatarStorage;

        public AccessController(IAccessRepository accessRepository, IAvatarStorage avatarStorage)
        {
            _accessRepository = accessRepository;
            _avatarStorage = avatarStorage;
        }

        public async Task<IActionResult> Index(string? search, string? userId)
        {
            List<AccessProfile> profiles;
            try
            {
                profiles = await _accessRepository.GetAllProfilesAsync();
            }
            catch (Exception ex) when (ex is OracleException or InvalidOperationException)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = DatabaseErrorMessage.Build(ex);
                return View(new AccessManagementViewModel { Search = search });
            }

            var users = profiles.Select(ToUserViewModel).ToList();
            foreach (var user in users)
            {
                user.AvatarUrl = _avatarStorage.GetUrl(user.UserId);
            }

            var filteredUsers = FilterUsers(users, search).ToList();
            var selectedUser = users.FirstOrDefault(user =>
                string.Equals(user.UserId, userId, StringComparison.OrdinalIgnoreCase))
                ?? filteredUsers.FirstOrDefault()
                ?? users.FirstOrDefault();

            var tableAccess = new List<TableAccessViewModel>();
            if (selectedUser is not null)
            {
                try
                {
                    var profile = await _accessRepository.GetProfileAsync(selectedUser.UserId);
                    tableAccess = BuildTableAccess(profile);
                }
                catch (Exception ex) when (ex is OracleException or InvalidOperationException)
                {
                    TempData["PopupType"] = "danger";
                    TempData["PopupMessage"] = DatabaseErrorMessage.Build(ex);
                }
            }

            return View(new AccessManagementViewModel
            {
                Search = search,
                SelectedUserId = selectedUser?.UserId,
                SelectedUser = selectedUser,
                Users = filteredUsers,
                TableAccess = tableAccess,
                NewUserAccess = BuildTableAccess(null)
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save(AccessSaveInputModel model)
        {
            if (string.IsNullOrWhiteSpace(model.UserId))
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = "Missing user id.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                // Full name is set once when the user is created (or from their LDAP
                // display name on first login) and is not editable afterwards, so it is
                // read back from TBLACCESS rather than taken from the posted form.
                var profile = await _accessRepository.GetProfileAsync(model.UserId);
                var userName = string.IsNullOrWhiteSpace(profile?.UserName) ? model.UserId : profile.UserName;

                var modules = model.TableAccess
                    .Select(module => new ModuleAccessGrant(module.Module, module.CanView, module.CanAdd, module.CanUpdate, module.CanDelete))
                    .ToList();

                var result = await _accessRepository.SaveAccessAsync(
                    model.UserId,
                    userName,
                    model.RoleName,
                    User.Identity?.Name ?? "unknown",
                    modules);

                TempData["PopupType"] = result.Success ? "success" : "danger";
                TempData["PopupMessage"] = result.Message;
            }
            catch (Exception ex) when (ex is OracleException or InvalidOperationException)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = DatabaseErrorMessage.Build(ex);
            }

            return RedirectToAction(nameof(Index), new { userId = model.UserId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddUser(AddAccessUserInputModel model)
        {
            if (string.IsNullOrWhiteSpace(model.UserId))
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = "Enter an LDAP username to add.";
                return RedirectToAction(nameof(Index));
            }

            var userId = model.UserId.Trim();

            try
            {
                var grantedBy = User.Identity?.Name ?? "unknown";
                var userName = string.IsNullOrWhiteSpace(model.UserName) ? userId : model.UserName.Trim();

                var result = await _accessRepository.AddUserAsync(userId, userName, model.RoleName, grantedBy);

                // Apply the permissions ticked on the New User form in the same step,
                // so the user is created fully configured rather than needing a
                // second Save Validation pass.
                if (result.Success && model.TableAccess.Count > 0)
                {
                    var modules = model.TableAccess
                        .Select(module => new ModuleAccessGrant(module.Module, module.CanView, module.CanAdd, module.CanUpdate, module.CanDelete))
                        .ToList();

                    var accessResult = await _accessRepository.SaveAccessAsync(userId, userName, model.RoleName, grantedBy, modules);
                    if (!accessResult.Success)
                    {
                        TempData["PopupType"] = "warning";
                        TempData["PopupMessage"] = $"{result.Message} However the module access could not be saved: {accessResult.Message}";
                        return RedirectToAction(nameof(Index), new { userId });
                    }
                }

                TempData["PopupType"] = result.Success ? "success" : "danger";
                TempData["PopupMessage"] = result.Success
                    ? $"{result.Message} Module access applied."
                    : result.Message;
            }
            catch (Exception ex) when (ex is OracleException or InvalidOperationException)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = DatabaseErrorMessage.Build(ex);
            }

            return RedirectToAction(nameof(Index), new { userId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadAvatar(string userId, IFormFile? photo)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return RedirectToAction(nameof(Index));
            }

            var result = await _avatarStorage.SaveAsync(userId, photo);
            TempData["PopupType"] = result.Success ? "success" : "danger";
            TempData["PopupMessage"] = result.Message;

            return RedirectToAction(nameof(Index), new { userId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ChooseAvatar(string userId, string? presetId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return RedirectToAction(nameof(Index));
            }

            var result = _avatarStorage.SetPreset(userId, presetId);
            TempData["PopupType"] = result.Success ? "success" : "danger";
            TempData["PopupMessage"] = result.Message;

            return RedirectToAction(nameof(Index), new { userId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> BlockUser(string userId)
        {
            return SetStatusAsync(userId, AccessStatus.Blocked);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> UnblockUser(string userId)
        {
            return SetStatusAsync(userId, AccessStatus.Active);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteUser(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return RedirectToAction(nameof(Index));
            }

            if (string.Equals(userId, User.Identity?.Name, StringComparison.OrdinalIgnoreCase))
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = "You cannot delete your own account.";
                return RedirectToAction(nameof(Index), new { userId });
            }

            try
            {
                var result = await _accessRepository.DeleteUserAsync(userId);
                if (result.Success)
                {
                    _avatarStorage.Delete(userId);
                }

                TempData["PopupType"] = result.Success ? "success" : "danger";
                TempData["PopupMessage"] = result.Message;
            }
            catch (Exception ex) when (ex is OracleException or InvalidOperationException)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = DatabaseErrorMessage.Build(ex);
            }

            return RedirectToAction(nameof(Index));
        }

        private async Task<IActionResult> SetStatusAsync(string userId, string status)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return RedirectToAction(nameof(Index));
            }

            if (status == AccessStatus.Blocked && string.Equals(userId, User.Identity?.Name, StringComparison.OrdinalIgnoreCase))
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = "You cannot block your own account.";
                return RedirectToAction(nameof(Index), new { userId });
            }

            try
            {
                var result = await _accessRepository.SetStatusAsync(userId, status, User.Identity?.Name ?? "unknown");
                TempData["PopupType"] = result.Success ? "success" : "danger";
                TempData["PopupMessage"] = result.Message;
            }
            catch (Exception ex) when (ex is OracleException or InvalidOperationException)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = DatabaseErrorMessage.Build(ex);
            }

            return RedirectToAction(nameof(Index), new { userId });
        }

        private static AccessUserViewModel ToUserViewModel(AccessProfile profile)
        {
            return new AccessUserViewModel
            {
                UserId = profile.UserId,
                UserName = string.IsNullOrWhiteSpace(profile.UserName) ? profile.UserId : profile.UserName,
                Role = profile.RoleName,
                Status = profile.Status,
                GrantedDate = profile.GrantedDate
            };
        }

        private static IEnumerable<AccessUserViewModel> FilterUsers(IEnumerable<AccessUserViewModel> users, string? search)
        {
            if (string.IsNullOrWhiteSpace(search))
            {
                return users;
            }

            return users.Where(user =>
                Contains(user.UserId, search) ||
                Contains(user.UserName, search) ||
                Contains(user.Email, search) ||
                Contains(user.Role, search));
        }

        private static bool Contains(string value, string search)
        {
            return value.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static List<TableAccessViewModel> BuildTableAccess(AccessProfile? profile)
        {
            var grantsByModule = profile?.ModuleGrants
                .ToDictionary(grant => grant.ModuleName, grant => grant.AccessLevel, StringComparer.OrdinalIgnoreCase)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            return AppModules.All.Select(module =>
            {
                var (canView, canAdd, canUpdate, canDelete) = AccessLevel.Parse(grantsByModule.GetValueOrDefault(module.Name));
                return new TableAccessViewModel
                {
                    Module = module.Name,
                    Description = module.Description,
                    CanView = canView,
                    CanAdd = canAdd,
                    CanUpdate = canUpdate,
                    CanDelete = canDelete
                };
            }).ToList();
        }
    }
}
