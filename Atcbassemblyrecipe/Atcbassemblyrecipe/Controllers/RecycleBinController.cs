using Atcbassemblyrecipe.Authorization;
using Atcbassemblyrecipe.Data;
using Atcbassemblyrecipe.Models;
using Atcbassemblyrecipe.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Oracle.ManagedDataAccess.Client;

namespace Atcbassemblyrecipe.Controllers
{
    // The two "undo" pages.
    //
    // Trash lists rows that were deleted and puts them back; Change History lists
    // updates and rolls one back to the values it had before. Both read the
    // snapshots written by the grids' own save paths, so nothing here has to know
    // what a row of AWACSWSTYPE, AWACSRECIPEBYWSTYPE or AWACSLF looks like.
    //
    // Restoring or reverting is an Update on this module; purging - the one action
    // that really destroys data - needs Delete, which defaults to Super Admin only.
    [Authorize]
    public class RecycleBinController : Controller
    {
        private readonly IRecipeAuditRepository _auditRepository;

        public RecycleBinController(IRecipeAuditRepository auditRepository)
        {
            _auditRepository = auditRepository;
        }

        [ModuleAccess(ModuleNames.RecycleBin, ModuleAction.View)]
        public async Task<IActionResult> Index(string? search, int page = 1, int pageSize = 25, bool includeRestored = false)
        {
            var model = new RecycleBinViewModel { IncludeRestored = includeRestored };

            try
            {
                var result = await _auditRepository.GetTrashAsync(search, page, pageSize, includeRestored);
                model.Rows = result.Rows;
                model.Search = result.Search;
                model.Page = result.Page;
                model.PageSize = result.PageSize;
                model.TotalRows = result.TotalRows;
            }
            catch (Exception ex) when (ex is OracleException or InvalidOperationException)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = DatabaseErrorMessage.Build(ex);
            }

            return View(model);
        }

        [ModuleAccess(ModuleNames.RecycleBin, ModuleAction.View)]
        public async Task<IActionResult> History(string? search, int page = 1, int pageSize = 25, bool includeReverted = false)
        {
            var model = new ChangeHistoryViewModel { IncludeReverted = includeReverted };

            try
            {
                var result = await _auditRepository.GetHistoryAsync(search, page, pageSize, includeReverted);
                model.Rows = result.Rows;
                model.Search = result.Search;
                model.Page = result.Page;
                model.PageSize = result.PageSize;
                model.TotalRows = result.TotalRows;
            }
            catch (Exception ex) when (ex is OracleException or InvalidOperationException)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = DatabaseErrorMessage.Build(ex);
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [ModuleAccess(ModuleNames.RecycleBin, ModuleAction.Update)]
        public async Task<IActionResult> Restore(long trashId, bool confirmed)
        {
            if (!confirmed)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = "Restore was blocked because it was not confirmed.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                var result = await _auditRepository.RestoreAsync(trashId, User.Identity?.Name ?? "unknown");
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

        [HttpPost]
        [ValidateAntiForgeryToken]
        [ModuleAccess(ModuleNames.RecycleBin, ModuleAction.Update)]
        public async Task<IActionResult> Revert(long historyId, bool confirmed)
        {
            if (!confirmed)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = "Revert was blocked because it was not confirmed.";
                return RedirectToAction(nameof(History));
            }

            try
            {
                var result = await _auditRepository.RevertAsync(historyId, User.Identity?.Name ?? "unknown");
                TempData["PopupType"] = result.Success ? "success" : "danger";
                TempData["PopupMessage"] = result.Message;
            }
            catch (Exception ex) when (ex is OracleException or InvalidOperationException)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = DatabaseErrorMessage.Build(ex);
            }

            return RedirectToAction(nameof(History));
        }

        // The only action here that actually destroys data.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [ModuleAccess(ModuleNames.RecycleBin, ModuleAction.Delete)]
        public async Task<IActionResult> Purge(long trashId, bool confirmed)
        {
            if (!confirmed)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = "Purge was blocked because validation confirmation was not checked.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                var result = await _auditRepository.PurgeAsync(trashId);
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
    }
}
