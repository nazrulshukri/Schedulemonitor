using Atcbassemblyrecipe.Authorization;
using Atcbassemblyrecipe.Models;
using Atcbassemblyrecipe.Data;
using Atcbassemblyrecipe.Infrastructure;
using Atcbassemblyrecipe.Services;
using Atcbassemblyrecipe.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Oracle.ManagedDataAccess.Client;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace Atcbassemblyrecipe.Controllers
{
    [Authorize]
    public class AwacsController : Controller
    {
        private readonly IAwacsWstypeService _awacsWstypeService;
        private readonly IDatabaseChangeRequestRepository _requestRepository;
        private readonly IAccessRepository _accessRepository;
        private readonly IAccessEvaluator _accessEvaluator;
        private readonly IEmailSender _emailSender;
        private readonly IConfiguration _configuration;
        private readonly IAppSettings _appSettings;

        public AwacsController(
            IAwacsWstypeService awacsWstypeService,
            IDatabaseChangeRequestRepository requestRepository,
            IAccessRepository accessRepository,
            IAccessEvaluator accessEvaluator,
            IEmailSender emailSender,
            IConfiguration configuration,
            IAppSettings appSettings)
        {
            _awacsWstypeService = awacsWstypeService;
            _requestRepository = requestRepository;
            _accessRepository = accessRepository;
            _accessEvaluator = accessEvaluator;
            _emailSender = emailSender;
            _configuration = configuration;
            _appSettings = appSettings;
        }

        // The CSV columns this grid understands, in the order the downloaded
        // template writes them - which is also the order a file with no header
        // row is read in.
        private static readonly CsvColumn[] RecipeCsvColumns =
        [
            new CsvColumn("WSTYPE", false, "WS TYPE", "WORKSTATION TYPE"),
            new CsvColumn("PACKAGE", false, "PKG", "PACKAGE NAME"),
            new CsvColumn("PRODUCT", true, "PRODUCT NAME", "PRODUCT ID"),
            new CsvColumn("LEADFRAME12NC", true, "LEADFRAME 12NC", "LF12NC", "LF 12NC", "LEADFRAME", "12NC"),
            new CsvColumn("RECIPE", true, "RECIPE NAME"),
            // Accepted in a header row, but not a template column: a file with no
            // header row is the five columns above and nothing else.
            new CsvColumn("CEPTDESCRIPTION", false, "CEPT DESCRIPTION") { InTemplate = false }
        ];

        [ModuleAccess(ModuleNames.AwacsWstype, ModuleAction.Add)]
        public IActionResult Create()
        {
            return View(new AwacsWstypeInputModel());
        }

        [ModuleAccess(ModuleNames.TableSawing, ModuleAction.View)]
        public Task<IActionResult> TableSawing(string? search, int page = 1, int pageSize = 25, string? sortBy = "lastupdate", string? sortDirection = "desc", bool promptAdd = false)
        {
            return ShowRecipeGridAsync(RecipeWsTypes.Sawing, search, page, pageSize, sortBy, sortDirection, promptAdd);
        }

        [ModuleAccess(ModuleNames.TableMarker, ModuleAction.View)]
        public Task<IActionResult> TableMarker(string? search, int page = 1, int pageSize = 25, string? sortBy = "lastupdate", string? sortDirection = "desc", bool promptAdd = false)
        {
            return ShowRecipeGridAsync(RecipeWsTypes.Marker, search, page, pageSize, sortBy, sortDirection, promptAdd);
        }

        [ModuleAccess(ModuleNames.TableWirebond, ModuleAction.View)]
        public Task<IActionResult> TableWirebond(string? search, int page = 1, int pageSize = 25, string? sortBy = "lastupdate", string? sortDirection = "desc", bool promptAdd = false)
        {
            return ShowRecipeGridAsync(RecipeWsTypes.Wirebond, search, page, pageSize, sortBy, sortDirection, promptAdd);
        }

        private async Task<IActionResult> ShowRecipeGridAsync(string wsType, string? search, int page, int pageSize, string? sortBy, string? sortDirection, bool promptAdd)
        {
            var normalizedSortBy = NormalizeRecipeSortBy(sortBy);
            var normalizedSortDirection = NormalizeSortDirection(sortDirection);
            var model = BuildRecipeGridModel(wsType);
            model.SortBy = normalizedSortBy;
            model.SortDirection = normalizedSortDirection;
            model.PromptAdd = promptAdd;

            try
            {
                var result = await _awacsWstypeService.GetRecipeByWstypeAsync(wsType, search, page, pageSize, normalizedSortBy, normalizedSortDirection);
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

            return View("RecipeGrid", model);
        }

        [Authorize]
        public IActionResult DownloadRecipeTemplate(string wsType = RecipeWsTypes.Sawing)
        {
            var normalized = RecipeWsTypes.Normalize(wsType);
            var sample = normalized == RecipeWsTypes.Marker
                ? "MARKER,SOT1210,8BCP56G1,340007011059,MARK-PBS54032NX-Q"
                : "SAWING,SOT363,8BCP56G1,340007011059,SOT89-PBS54032NX-Q";
            var csv = $"WSTYPE,PACKAGE,PRODUCT,LEADFRAME12NC,RECIPE\r\n{sample}\r\n";

            return File(Encoding.UTF8.GetBytes(csv), "text/csv", $"table-{normalized.ToLowerInvariant()}-template.csv");
        }

        public async Task<IActionResult> ExportRecipeRows(string wsType = RecipeWsTypes.Sawing, string? search = null, string? sortBy = "lastupdate", string? sortDirection = "desc")
        {
            var normalized = RecipeWsTypes.Normalize(wsType);
            if (!await HasRecipeAccessAsync(normalized, ModuleAction.View))
            {
                return Forbid();
            }

            try
            {
                var rows = await _awacsWstypeService.GetRecipeByWstypeForExportAsync(normalized, search, sortBy, sortDirection);
                var builder = new StringBuilder();
                builder.AppendLine("WSTYPE,PACKAGE,Product,Leadframe 12NC,Recipe,Last Updated By,Timestamp");

                foreach (var row in rows)
                {
                    builder
                        .Append(EscapeCsv(row.WsType)).Append(',')
                        .Append(EscapeCsv(row.Package)).Append(',')
                        .Append(EscapeCsv(row.Product)).Append(',')
                        .Append(EscapeCsv(row.Leadframe12Nc)).Append(',')
                        .Append(EscapeCsv(row.Recipe)).Append(',')
                        .Append(EscapeCsv(row.LastUpdatedBy)).Append(',')
                        .Append(EscapeCsv(row.LastUpdate?.ToString("yyyy-MM-dd HH:mm") ?? string.Empty))
                        .AppendLine();
                }

                return File(Encoding.UTF8.GetBytes(builder.ToString()), "text/csv", $"table-{normalized.ToLowerInvariant()}-export.csv");
            }
            catch (Exception ex) when (ex is OracleException or InvalidOperationException)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = DatabaseErrorMessage.Build(ex);
                return RedirectToGrid(normalized, new { search, sortBy, sortDirection });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ImportRecipeRows(string wsType, IFormFile? csvFile)
        {
            var normalized = RecipeWsTypes.Normalize(wsType);
            if (!await HasRecipeAccessAsync(normalized, ModuleAction.Add))
            {
                return Forbid();
            }

            if (csvFile is null || csvFile.Length == 0)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = "Choose a CSV file before importing.";
                return RedirectToGrid(normalized);
            }

            if (!CsvImportReader.IsSupportedFileName(csvFile.FileName))
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = "Upload a text file only (.csv, .txt or .tsv). If Excel changed it to .xlsx, use Save As > CSV (Comma delimited) before uploading.";
                return RedirectToGrid(normalized);
            }

            CsvImportResult read;
            using (var stream = csvFile.OpenReadStream())
            {
                read = CsvImportReader.Read(stream, RecipeCsvColumns);
            }

            if (!read.Success)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = $"CSV import cancelled. Inserted: 0. {read.Error}";
                return RedirectToGrid(normalized);
            }

            var importErrors = new List<string>();
            var pendingRows = new List<RecipeRowInputModel>();

            foreach (var row in read.Rows)
            {
                var product = row["PRODUCT"];
                var ceptDescription = row["CEPTDESCRIPTION"];

                if (string.IsNullOrWhiteSpace(product) && !string.IsNullOrWhiteSpace(ceptDescription))
                {
                    product = SawingRecipeInputModel.ExtractProduct(ceptDescription);
                }

                var leadframe12Nc = row["LEADFRAME12NC"];
                if (CsvImportReader.LooksLikeExcelScientificNumber(leadframe12Nc))
                {
                    importErrors.Add($"Line {row.LineNumber}: LEADFRAME12NC reads '{leadframe12Nc}'. Excel rounded the 12NC away - format that column as Text and save the file again.");
                    continue;
                }

                var model = new RecipeRowInputModel
                {
                    // A file exported from one grid must never land in the
                    // other, so the page's WSTYPE wins over the column.
                    WsType = normalized,
                    Package = row["PACKAGE"],
                    Product = product,
                    Leadframe12Nc = leadframe12Nc,
                    Recipe = row["RECIPE"],
                    Confirmed = true
                };

                var validationResults = new List<ValidationResult>();
                var validationContext = new ValidationContext(model);
                if (!Validator.TryValidateObject(model, validationContext, validationResults, true))
                {
                    importErrors.Add($"Line {row.LineNumber}: {string.Join("; ", validationResults.Select(result => result.ErrorMessage))}");
                    continue;
                }

                pendingRows.Add(model);
            }

            if (importErrors.Count > 0)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = $"CSV import cancelled. Inserted: 0. Fix {importErrors.Count} error(s) and upload again. Details: {string.Join(" | ", importErrors.Take(5))}";
                return RedirectToGrid(normalized);
            }

            if (pendingRows.Count == 0)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = "CSV import cancelled. Inserted: 0. No data rows were found.";
                return RedirectToGrid(normalized);
            }

            try
            {
                var result = await _awacsWstypeService.CreateManyRecipeRowsAsync(pendingRows, User.Identity?.Name ?? "unknown");
                TempData["PopupType"] = result.Success ? "success" : "danger";
                TempData["PopupMessage"] = result.Success
                    ? $"{result.Message}{HeaderlessNote(read)}"
                    : $"{result.Message} Inserted: 0.";
            }
            catch (Exception ex) when (ex is OracleException or InvalidOperationException)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = DatabaseErrorMessage.Build(ex);
            }

            return RedirectToGrid(normalized);
        }

        [ModuleAccess(ModuleNames.TableSawing, ModuleAction.Add)]
        public IActionResult CreateSawingRecipe()
        {
            return View(new SawingRecipeInputModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [ModuleAccess(ModuleNames.TableSawing, ModuleAction.Add)]
        public async Task<IActionResult> CreateSawingRecipe(SawingRecipeInputModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            (bool Success, string Message) result;
            try
            {
                result = await _awacsWstypeService.CreateSawingRecipeAsync(model, User.Identity?.Name ?? "unknown");
            }
            catch (Exception ex) when (ex is OracleException or InvalidOperationException)
            {
                ModelState.AddModelError(string.Empty, DatabaseErrorMessage.Build(ex));
                return View(model);
            }

            TempData["PopupType"] = result.Success ? "success" : "danger";
            TempData["PopupMessage"] = result.Message;

            if (!result.Success)
            {
                return View(model);
            }

            return RedirectToAction(nameof(TableSawing));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateRecipeRow(RecipeRowInputModel model)
        {
            var normalized = RecipeWsTypes.Normalize(model.WsType);
            if (!await HasRecipeAccessAsync(normalized, ModuleAction.Add))
            {
                return Forbid();
            }

            if (!ModelState.IsValid)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = $"Please fill the required values: {BuildModelStateMessage()}";
                return RedirectToGrid(normalized, new { promptAdd = true });
            }

            (bool Success, string Message) result;
            try
            {
                result = await _awacsWstypeService.CreateRecipeRowAsync(model, User.Identity?.Name ?? "unknown");
            }
            catch (Exception ex) when (ex is OracleException or InvalidOperationException)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = DatabaseErrorMessage.Build(ex);
                return RedirectToGrid(normalized, new { promptAdd = true });
            }

            TempData["PopupType"] = result.Success ? "success" : "danger";
            TempData["PopupMessage"] = result.Message;

            return result.Success
                ? RedirectToGrid(normalized)
                : RedirectToGrid(normalized, new { promptAdd = true });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditRecipeRow(RecipeRowEditModel model)
        {
            var normalized = RecipeWsTypes.Normalize(model.WsType);
            if (!await HasRecipeAccessAsync(normalized, ModuleAction.Update))
            {
                return Forbid();
            }

            if (!ModelState.IsValid)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = $"Nothing was saved: {BuildModelStateMessage()}";
                return RedirectToGrid(normalized);
            }

            (bool Success, string Message) result;
            try
            {
                result = await _awacsWstypeService.UpdateRecipeRowAsync(model, User.Identity?.Name ?? "unknown");
            }
            catch (Exception ex) when (ex is OracleException or InvalidOperationException)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = DatabaseErrorMessage.Build(ex);
                return RedirectToGrid(normalized);
            }

            TempData["PopupType"] = result.Success ? "success" : "danger";
            TempData["PopupMessage"] = result.Message;
            return RedirectToGrid(normalized);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteRecipeRow(string id, string wsType, bool confirmed)
        {
            var normalized = RecipeWsTypes.Normalize(wsType);
            if (!await HasRecipeAccessAsync(normalized, ModuleAction.Delete))
            {
                return Forbid();
            }

            if (!confirmed)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = "Delete was blocked because validation confirmation was not checked.";
                return RedirectToGrid(normalized);
            }

            (bool Success, string Message) result;
            try
            {
                result = await _awacsWstypeService.DeleteRecipeRowAsync(id, User.Identity?.Name ?? "unknown");
            }
            catch (Exception ex) when (ex is OracleException or InvalidOperationException)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = DatabaseErrorMessage.Build(ex);
                return RedirectToGrid(normalized);
            }

            TempData["PopupType"] = result.Success ? "success" : "danger";
            TempData["PopupMessage"] = result.Message;
            return RedirectToGrid(normalized);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [ModuleAccess(ModuleNames.AwacsWstype, ModuleAction.Add)]
        // Saving a machine leaves you on the AWACSWSTYPE grid, where the row you
        // just added is now visible - the same as Edit and Delete already do.
        //
        // It used to redirect to the Table Sawing grid with promptAdd, from back
        // when this page only held SAWING machines and the natural next step was
        // that grid. It now holds WIREBOND machines too, so that jump was both
        // unasked for and, for a wire bonder, the wrong grid entirely.
        public async Task<IActionResult> Create(AwacsWstypeInputModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = $"Nothing was saved: {BuildModelStateMessage()}";
                return RedirectToAction("Index", "Home");
            }

            (bool Success, string Message) result;
            try
            {
                result = await _awacsWstypeService.CreateAsync(model, User.Identity?.Name ?? "unknown");
            }
            catch (Exception ex) when (ex is OracleException or InvalidOperationException)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = DatabaseErrorMessage.Build(ex);
                return RedirectToAction("Index", "Home");
            }

            TempData["PopupType"] = result.Success ? "success" : "danger";

            // Only the Wirebond grid is machine-driven, so only there can we
            // promise the machine is already waiting with a row of its own.
            // A new SAWING machine does not appear on the Sawing grid by itself -
            // that grid is AWACSRECIPEBYWSTYPE, one row per recipe.
            var isWirebond = string.Equals(model.WsType?.Trim(), RecipeWsTypes.Wirebond, StringComparison.OrdinalIgnoreCase);
            TempData["PopupMessage"] = result.Success && isWirebond
                ? $"{result.Message} {model.WsId} is on the Wirebond page now, as an empty row waiting for its recipe."
                : result.Message;

            return RedirectToAction("Index", "Home");
        }

        [ModuleAccess(ModuleNames.AwacsWstype, ModuleAction.Update)]
        public async Task<IActionResult> Edit(string id)
        {
            AwacsWstypeInputModel? model;
            try
            {
                model = await _awacsWstypeService.GetForEditAsync(id);
            }
            catch (Exception ex) when (ex is OracleException or InvalidOperationException)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = DatabaseErrorMessage.Build(ex);
                return RedirectToAction("Index", "Home");
            }

            if (model is null)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = "AWACSWSTYPE row was not found.";
                return RedirectToAction("Index", "Home");
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [ModuleAccess(ModuleNames.AwacsWstype, ModuleAction.Update)]
        public async Task<IActionResult> Edit(AwacsWstypeInputModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = $"Nothing was saved: {BuildModelStateMessage()}";
                return RedirectToAction("Index", "Home");
            }

            (bool Success, string Message) result;
            try
            {
                result = await _awacsWstypeService.UpdateAsync(model, User.Identity?.Name ?? "unknown");
            }
            catch (Exception ex) when (ex is OracleException or InvalidOperationException)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = DatabaseErrorMessage.Build(ex);
                return RedirectToAction("Index", "Home");
            }

            TempData["PopupType"] = result.Success ? "success" : "danger";
            TempData["PopupMessage"] = result.Message;
            return RedirectToAction("Index", "Home");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [ModuleAccess(ModuleNames.AwacsWstype, ModuleAction.Delete)]
        public async Task<IActionResult> Delete(string id, bool confirmed)
        {
            if (!confirmed)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = "Delete was blocked because validation confirmation was not checked.";
                return RedirectToAction("Index", "Home");
            }

            (bool Success, string Message) result;
            try
            {
                result = await _awacsWstypeService.DeleteAsync(id, User.Identity?.Name ?? "unknown");
            }
            catch (Exception ex) when (ex is OracleException or InvalidOperationException)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = DatabaseErrorMessage.Build(ex);
                return RedirectToAction("Index", "Home");
            }

            TempData["PopupType"] = result.Success ? "success" : "danger";
            TempData["PopupMessage"] = result.Message;
            return RedirectToAction("Index", "Home");
        }

        [HttpGet]
        [ModuleAccess(ModuleNames.TableSawing, ModuleAction.Add)]
        public async Task<IActionResult> ValidateSawingRecipe(string leadframe12Nc, string ceptDescription)
        {
            var model = new SawingRecipeInputModel
            {
                Leadframe12Nc = leadframe12Nc ?? string.Empty,
                CeptDescription = ceptDescription ?? string.Empty,
                Confirmed = true
            };

            if (string.IsNullOrWhiteSpace(model.Leadframe12Nc) || string.IsNullOrWhiteSpace(model.CeptDescription))
            {
                return BadRequest(new { success = false, message = "Reference 12NC and CEPT Description are required." });
            }

            try
            {
                var result = await _awacsWstypeService.CheckSawingRecipeAsync(model);
                return Json(new
                {
                    success = true,
                    exists = result.Exists,
                    product = result.Product,
                    recipe = result.Recipe,
                    message = result.Exists
                        ? "Recipe already exists in AWACSRECIPEBYWSTYPE."
                        : "Recipe is not found yet. You may proceed to add it."
                });
            }
            catch (Exception ex) when (ex is OracleException or InvalidOperationException)
            {
                return StatusCode(503, new { success = false, message = DatabaseErrorMessage.Build(ex) });
            }
        }

        [HttpGet]
        [ModuleAccess(ModuleNames.AwacsWstype, ModuleAction.Add)]
        public async Task<IActionResult> ValidateWsDb()
        {
            try
            {
                var count = await _awacsWstypeService.GetTblSawingCountAsync();
                return Json(new
                {
                    success = true,
                    message = $"TBLSAWING is reachable. Current row count: {count}."
                });
            }
            catch (Exception ex) when (ex is OracleException or InvalidOperationException)
            {
                return StatusCode(503, new { success = false, message = DatabaseErrorMessage.Build(ex) });
            }
        }

        [ModuleAccess(ModuleNames.DbValidation, ModuleAction.View)]
        public async Task<IActionResult> RequestDatabaseChange()
        {
            return View(await BuildRequestViewModelAsync(new DatabaseChangeRequestViewModel()));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [ModuleAccess(ModuleNames.DbValidation, ModuleAction.Update)]
        public async Task<IActionResult> RequestDatabaseChange(DatabaseChangeRequestViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(await BuildRequestViewModelAsync(model));
            }

            var request = new DatabaseChangeRequest
            {
                RequestType = model.RequestType.Trim(),
                TargetTable = model.TargetTable.Trim().ToUpperInvariant(),
                Reason = model.Reason.Trim(),
                RequestedBy = User.Identity?.Name ?? "unknown"
            };

            if (!RequestTypes.All.Contains(request.RequestType))
            {
                ModelState.AddModelError(nameof(model.RequestType), "Choose a valid request type.");
                return View(await BuildRequestViewModelAsync(model));
            }

            (bool Success, long RequestId, string Message) saved;
            try
            {
                // Saved BEFORE any email is attempted: a mail outage must never lose
                // somebody's request.
                saved = await _requestRepository.CreateAsync(request);
            }
            catch (Exception ex) when (ex is OracleException or InvalidOperationException)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = DatabaseErrorMessage.Build(ex);
                return RedirectToAction(nameof(RequestDatabaseChange));
            }

            if (!saved.Success)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = saved.Message;
                return RedirectToAction(nameof(RequestDatabaseChange));
            }

            var emailResult = await NotifyReviewersAsync(request, saved.RequestId);

            TempData["PopupType"] = emailResult.Sent ? "success" : "warning";
            TempData["PopupMessage"] =
                $"Request #{saved.RequestId} recorded for validation. Structural actions like TRUNCATE, ADD COLUMN, and CREATE TABLE are still not executed from this web app. {emailResult.Message}";

            return RedirectToAction(nameof(RequestDatabaseChange));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [ModuleAccess(ModuleNames.DbValidation, ModuleAction.Update)]
        public async Task<IActionResult> ReviewDatabaseChange(long requestId, string decision, string? reviewNote)
        {
            var status = string.Equals(decision, "approve", StringComparison.OrdinalIgnoreCase)
                ? RequestStatus.Approved
                : RequestStatus.Rejected;

            try
            {
                var result = await _requestRepository.SetStatusAsync(requestId, status, User.Identity?.Name ?? "unknown", reviewNote);
                TempData["PopupType"] = result.Success ? "success" : "danger";
                TempData["PopupMessage"] = result.Message;
            }
            catch (Exception ex) when (ex is OracleException or InvalidOperationException)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = DatabaseErrorMessage.Build(ex);
            }

            return RedirectToAction(nameof(RequestDatabaseChange));
        }

        private static RecipeByWstypeViewModel BuildRecipeGridModel(string wsType) => wsType switch
        {
            RecipeWsTypes.Marker => new RecipeByWstypeViewModel
            {
                WsType = RecipeWsTypes.Marker,
                ModuleName = ModuleNames.TableMarker,
                PageTitle = "Marker",
                PageDescription = "MARKER recipe columns from AWACSRECIPEBYWSTYPE.",
                GridAction = nameof(TableMarker),
                StorageKey = "tablemarker"
            },
            RecipeWsTypes.Wirebond => new RecipeByWstypeViewModel
            {
                WsType = RecipeWsTypes.Wirebond,
                ModuleName = ModuleNames.TableWirebond,
                PageTitle = "Wirebond",
                PageDescription = "WIREBOND recipe columns from AWACSRECIPEBYWSTYPE.",
                GridAction = nameof(TableWirebond),
                StorageKey = "tablewirebond"
            },
            _ => new RecipeByWstypeViewModel
            {
                WsType = RecipeWsTypes.Sawing,
                ModuleName = ModuleNames.TableSawing,
                PageTitle = "Table Sawing",
                PageDescription = "SAWING recipe columns from AWACSRECIPEBYWSTYPE.",
                GridAction = nameof(TableSawing),
                StorageKey = "tablesawing"
            }
        };

        // Table Sawing and Table Marker are separate TBLACCESS modules but share one
        // set of write actions, so which module to check is only known once WSTYPE
        // has been read off the request. [ModuleAccess] is fixed at compile time and
        // cannot express that, so these actions run the same check here instead -
        // still server-side, still before anything is written.
        private async Task<bool> HasRecipeAccessAsync(string wsType, ModuleAction action)
        {
            var moduleName = wsType switch
            {
                RecipeWsTypes.Marker => ModuleNames.TableMarker,
                RecipeWsTypes.Wirebond => ModuleNames.TableWirebond,
                _ => ModuleNames.TableSawing
            };
            var permission = await _accessEvaluator.GetAsync(moduleName);

            return action switch
            {
                ModuleAction.View => permission.CanView,
                ModuleAction.Add => permission.CanAdd,
                ModuleAction.Update => permission.CanUpdate,
                ModuleAction.Delete => permission.CanDelete,
                _ => false
            };
        }

        private IActionResult RedirectToGrid(string wsType, object? routeValues = null)
        {
            var action = wsType switch
            {
                RecipeWsTypes.Marker => nameof(TableMarker),
                RecipeWsTypes.Wirebond => nameof(TableWirebond),
                _ => nameof(TableSawing)
            };
            return routeValues is null
                ? RedirectToAction(action)
                : RedirectToAction(action, routeValues);
        }

        private async Task<DatabaseChangeRequestViewModel> BuildRequestViewModelAsync(DatabaseChangeRequestViewModel model)
        {
            model.CanReview = true;
            model.EmailConfigured = _emailSender.IsConfigured;

            try
            {
                model.RecentRequests = await _requestRepository.GetRecentAsync();
            }
            catch (Exception ex) when (ex is OracleException or InvalidOperationException)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = $"{DatabaseErrorMessage.Build(ex)} If TBLDBREQUEST does not exist yet, run Database/tbldbrequest.sql once against the OCAP schema.";
            }

            return model;
        }

        // Recipients are every active Super Admin in TBLACCESS plus any fixed
        // addresses configured under Email:AdditionalRecipients.
        // Recipients are every active Super Admin in TBLACCESS plus any fixed
        // addresses configured under Email:AdditionalRecipients.
        private async Task<(bool Sent, string Message)> NotifyReviewersAsync(DatabaseChangeRequest request, long requestId)
        {
            if (!_emailSender.IsConfigured)
            {
                return (false, "Email notifications are switched off, so nobody was emailed - configure the Email section in appsettings.json to enable them.");
            }

            var recipients = new List<string>();

            try
            {
                var domain = _configuration["Email:UserDomain"] ?? "nexperia.com";
                var superAdmins = await _accessRepository.GetActiveUserIdsByRoleAsync(AppRole.SuperAdmin);
                recipients.AddRange(superAdmins.Select(userId => $"{userId}@{domain}"));
            }
            catch (Exception ex) when (ex is OracleException or InvalidOperationException)
            {
                // Fall through to the configured fixed list - a DB hiccup should not
                // silently stop the notification entirely.
            }

            recipients.AddRange(_configuration.GetSection("Email:AdditionalRecipients").Get<string[]>() ?? []);

            var ui = await _appSettings.GetAsync();
            var subjectPrefix = ui[SettingModules.App, "EmailSubjectPrefix"];

            var subject = $"{subjectPrefix} DB change request #{requestId} - {request.RequestType} on {request.TargetTable}";

            // Absolute link back to the review page, so the button in the mail works.
            var reviewUrl = Url.Action(nameof(RequestDatabaseChange), "Awacs",
                                       null, Request.Scheme, Request.Host.Value);

            var body = NotificationEmail.DatabaseChangeRequest(request, requestId, ui, reviewUrl);

            return await _emailSender.SendAsync(recipients, subject, body);
        }

        private static string EscapeCsv(string value)
        {
            if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
            {
                return $"\"{value.Replace("\"", "\"\"")}\"";
            }

            return value;
        }

        private static string NormalizeRecipeSortBy(string? sortBy)
        {
            return sortBy?.Trim().ToLowerInvariant() switch
            {
                "sequence" => "sequence",
                "wstype" => "wstype",
                "package" => "package",
                "product" => "product",
                "leadframe12nc" => "leadframe12nc",
                "recipe" => "recipe",
                "lastupdatedby" => "lastupdatedby",
                "lastupdate" => "lastupdate",
                _ => "lastupdate"
            };
        }

        // A file with no header row was mapped by column position. Say so, because
        // it is the one thing about the import the popup would otherwise hide.
        private static string HeaderlessNote(CsvImportResult read)
        {
            return read.HadHeaderRow
                ? string.Empty
                : " The file had no header row, so the columns were read in template order.";
        }

        private static string NormalizeSortDirection(string? sortDirection)
        {
            return string.Equals(sortDirection, "asc", StringComparison.OrdinalIgnoreCase) ? "asc" : "desc";
        }

        private string BuildModelStateMessage()
        {
            var messages = ModelState.Values
                .SelectMany(value => value.Errors)
                .Select(error => error.ErrorMessage)
                .Where(message => !string.IsNullOrWhiteSpace(message))
                .Distinct()
                .Take(5);

            return string.Join(" ", messages);
        }
    }
}
