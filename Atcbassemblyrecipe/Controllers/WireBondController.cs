using Atcbassemblyrecipe.Authorization;
using Atcbassemblyrecipe.Data;
using Atcbassemblyrecipe.Infrastructure;
using Atcbassemblyrecipe.Models;
using Atcbassemblyrecipe.Services;
using Atcbassemblyrecipe.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Oracle.ManagedDataAccess.Client;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace Atcbassemblyrecipe.Controllers
{
    // The TBLWIREBOND grid: package, product, leadframe 12NC, recipe. This is THE
    // Wirebond page - the one in the sidebar, gated by the single
    // ModuleNames.TableWirebond ("Wirebond") grant in TBLACCESS.
    //
    // AwacsController.TableWirebond still exists as a route over
    // AWACSRECIPEBYWSTYPE but is no longer in the menu, and it is gated by the
    // same "Wirebond" grant. There is no separate "Wirebond OCAP" module.
    [Authorize]
    public class WireBondController : Controller
    {
        private readonly IWireBondService _wireBondService;

        public WireBondController(IWireBondService wireBondService)
        {
            _wireBondService = wireBondService;
        }

        // The CSV columns this grid understands, in template order - which is also
        // the order a file with no header row is read in.
        private static readonly CsvColumn[] WireBondCsvColumns =
        [
            // Read so that an Export CSV can be edited and uploaded again, then
            // ignored: every row in TBLWIREBOND is a wirebond row whatever this
            // column says.
            new CsvColumn("WSTYPE", false, "WS TYPE"),
            new CsvColumn("PACKAGE", false, "PKG"),
            new CsvColumn("PRODUCT", true, "DEVICE"),
            new CsvColumn("LEADFRAME12NC", true, "LEADFRAME 12NC", "LF12NC", "LEADFRAME"),
            new CsvColumn("RECIPE", true, "RECIPE NAME")
        ];

        [ModuleAccess(ModuleNames.TableWirebond, ModuleAction.View)]
        public async Task<IActionResult> Index(string? search, int page = 1, int pageSize = 25, string? sortBy = "lastupdate", string? sortDirection = "desc", bool promptAdd = false, string? highlight = null)
        {
            var normalizedSortBy = WireBondService.NormalizeSortBy(sortBy);
            var normalizedSortDirection = NormalizeSortDirection(sortDirection);
            var model = new WireBondViewModel
            {
                SortBy = normalizedSortBy,
                SortDirection = normalizedSortDirection,
                PromptAdd = promptAdd,
                Highlight = highlight?.Trim() ?? string.Empty
            };

            try
            {
                var result = await _wireBondService.GetAsync(search, page, pageSize, normalizedSortBy, normalizedSortDirection);
                model.Rows = result.Rows;
                model.Search = result.Search;
                model.Page = result.Page;
                model.PageSize = result.PageSize;
                model.TotalRows = result.TotalRows;
            }
            catch (Exception ex) when (ex is OracleException or InvalidOperationException)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = Describe(ex);
            }

            return View(model);
        }

        public IActionResult DownloadTemplate()
        {
            const string csv =
                "WSTYPE,PACKAGE,PRODUCT,LEADFRAME12NC,RECIPE\r\n"
                + "WIREBOND,SOT669,BUK9K6-40E,934123456789,WB_SOT669_STD\r\n";
            return File(Encoding.UTF8.GetBytes(csv), "text/csv", "tblwirebond-template.csv");
        }

        [ModuleAccess(ModuleNames.TableWirebond, ModuleAction.View)]
        public async Task<IActionResult> Export(string? search, string? sortBy = "lastupdate", string? sortDirection = "desc")
        {
            try
            {
                var rows = await _wireBondService.GetForExportAsync(search, sortBy, sortDirection);
                var builder = new StringBuilder();
                builder.AppendLine("WSTYPE,PACKAGE,PRODUCT,LEADFRAME12NC,RECIPE,Last Updated By,Timestamp");

                foreach (var row in rows)
                {
                    builder
                        .Append(RecipeWsTypes.Wirebond).Append(',')
                        .Append(EscapeCsv(row.Package)).Append(',')
                        .Append(EscapeCsv(row.Product)).Append(',')
                        .Append(EscapeCsv(row.Leadframe12Nc)).Append(',')
                        .Append(EscapeCsv(row.Recipe)).Append(',')
                        .Append(EscapeCsv(row.LastUpdatedBy)).Append(',')
                        .Append(EscapeCsv(row.LastUpdate?.ToString("yyyy-MM-dd HH:mm") ?? string.Empty))
                        .AppendLine();
                }

                return File(Encoding.UTF8.GetBytes(builder.ToString()), "text/csv", "tblwirebond-export.csv");
            }
            catch (Exception ex) when (ex is OracleException or InvalidOperationException)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = Describe(ex);
                return RedirectToAction(nameof(Index), new { search, sortBy, sortDirection });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [ModuleAccess(ModuleNames.TableWirebond, ModuleAction.Add)]
        public async Task<IActionResult> Import(IFormFile? csvFile)
        {
            if (csvFile is null || csvFile.Length == 0)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = "Choose a CSV file before importing.";
                return RedirectToAction(nameof(Index));
            }

            if (!CsvImportReader.IsSupportedFileName(csvFile.FileName))
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = "Upload a text file only (.csv, .txt or .tsv). If Excel changed it to .xlsx, use Save As > CSV (Comma delimited) before uploading.";
                return RedirectToAction(nameof(Index));
            }

            CsvImportResult read;
            using (var stream = csvFile.OpenReadStream())
            {
                read = CsvImportReader.Read(stream, WireBondCsvColumns);
            }

            if (!read.Success)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = $"CSV import cancelled. Inserted: 0. {read.Error}";
                return RedirectToAction(nameof(Index));
            }

            var importErrors = new List<string>();
            var pendingRows = new List<WireBondInputModel>();
            var seenRecipes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in read.Rows)
            {
                var leadframe = row["LEADFRAME12NC"];

                // Excel turns a long numeric 12NC into 3.4E+11 on save. Caught here
                // rather than stored as the literal text "3.4E+11".
                if (CsvImportReader.LooksLikeExcelScientificNumber(leadframe))
                {
                    importErrors.Add($"Line {row.LineNumber}: LEADFRAME12NC '{leadframe}' was saved by Excel as a number. Format that column as Text and save again.");
                    continue;
                }

                var model = new WireBondInputModel
                {
                    Package = row["PACKAGE"],
                    Product = row["PRODUCT"],
                    Leadframe12Nc = leadframe,
                    Recipe = row["RECIPE"],
                    Confirmed = true
                };

                // Two rows in one file carrying the same recipe would both pass the
                // database check (neither is committed yet) and land as a duplicate
                // pair. Caught here instead.
                var key = $"{InputText.CleanUpper(model.Product)}|{InputText.CleanUpper(model.Leadframe12Nc)}|{InputText.CleanUpper(model.Recipe)}";
                if (!seenRecipes.Add(key))
                {
                    importErrors.Add($"Line {row.LineNumber}: {model.Product} / {model.Leadframe12Nc} / {model.Recipe} appears more than once in this file.");
                    continue;
                }

                var validationResults = new List<ValidationResult>();
                if (!Validator.TryValidateObject(model, new ValidationContext(model), validationResults, true))
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
                return RedirectToAction(nameof(Index));
            }

            if (pendingRows.Count == 0)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = "CSV import cancelled. Inserted: 0. No data rows were found.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                var result = await _wireBondService.CreateManyAsync(pendingRows, User.Identity?.Name ?? "unknown");
                TempData["PopupType"] = result.Success ? "success" : "danger";
                TempData["PopupMessage"] = result.Success
                    ? $"{result.Message}{HeaderlessNote(read)}"
                    : $"{result.Message} Inserted: 0.";
            }
            catch (Exception ex) when (ex is OracleException or InvalidOperationException)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = Describe(ex);
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [ModuleAccess(ModuleNames.TableWirebond, ModuleAction.Add)]
        public async Task<IActionResult> Create(WireBondInputModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = $"Please fill the required TBLWIREBOND values: {BuildModelStateMessage()}";
                return RedirectToAction(nameof(Index), new { promptAdd = true });
            }

            (bool Success, string Message) result;
            try
            {
                result = await _wireBondService.CreateAsync(model, User.Identity?.Name ?? "unknown");
            }
            catch (Exception ex) when (ex is OracleException or InvalidOperationException)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = Describe(ex);
                return RedirectToAction(nameof(Index), new { promptAdd = true });
            }

            TempData["PopupType"] = result.Success ? "success" : "danger";
            TempData["PopupMessage"] = result.Message;

            // No search, no page number and the default sort (lastupdate desc), so
            // the row that was just inserted is the first row of the grid. highlight
            // flashes it and scrolls to it. model.Product is the value the service
            // actually stored - CreateAsync normalizes the model in place.
            return result.Success
                ? RedirectToAction(nameof(Index), new { highlight = model.Product })
                : RedirectToAction(nameof(Index), new { promptAdd = true });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [ModuleAccess(ModuleNames.TableWirebond, ModuleAction.Update)]
        public async Task<IActionResult> Edit(WireBondInputModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = $"Nothing was saved: {BuildModelStateMessage()}";
                return RedirectToAction(nameof(Index));
            }

            (bool Success, string Message) result;
            try
            {
                result = await _wireBondService.UpdateAsync(model, User.Identity?.Name ?? "unknown");
            }
            catch (Exception ex) when (ex is OracleException or InvalidOperationException)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = Describe(ex);
                return RedirectToAction(nameof(Index));
            }

            TempData["PopupType"] = result.Success ? "success" : "danger";
            TempData["PopupMessage"] = result.Message;
            return result.Success
                ? RedirectToAction(nameof(Index), new { highlight = model.Product })
                : RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [ModuleAccess(ModuleNames.TableWirebond, ModuleAction.Delete)]
        public async Task<IActionResult> Delete(string id, bool confirmed)
        {
            if (!confirmed)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = "Delete was blocked because validation confirmation was not checked.";
                return RedirectToAction(nameof(Index));
            }

            (bool Success, string Message) result;
            try
            {
                result = await _wireBondService.DeleteAsync(id, User.Identity?.Name ?? "unknown");
            }
            catch (Exception ex) when (ex is OracleException or InvalidOperationException)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = Describe(ex);
                return RedirectToAction(nameof(Index));
            }

            TempData["PopupType"] = result.Success ? "success" : "danger";
            TempData["PopupMessage"] = result.Message;
            return RedirectToAction(nameof(Index));
        }

        private static string HeaderlessNote(CsvImportResult read)
        {
            return read.HadHeaderRow
                ? string.Empty
                : " The file had no header row, so the columns were read in template order.";
        }

        // DatabaseErrorMessage.Build answers every OracleException with the same
        // "check the password, VPN and service name" text. That is right for a
        // connection failure and actively misleading for anything else: an
        // ORA-00904 or ORA-02290 raised by this page's own SQL would read as if the
        // database were unreachable, and the grid would just look empty. Real
        // connection errors keep the original wording; everything else says what
        // Oracle actually said.
        private static string Describe(Exception ex)
        {
            if (ex is OracleException oracle && !IsConnectionError(oracle.Number))
            {
                return $"ORA-{oracle.Number:00000}: {oracle.Message.Trim()}";
            }

            return DatabaseErrorMessage.Build(ex);
        }

        private static bool IsConnectionError(int oraNumber)
        {
            return oraNumber is 1017 or 1005 or 12154 or 12170 or 12505 or 12514 or 12541 or 12545 or 28000 or 28001;
        }

        private static string NormalizeSortDirection(string? sortDirection)
        {
            return string.Equals(sortDirection, "asc", StringComparison.OrdinalIgnoreCase) ? "asc" : "desc";
        }

        private static string EscapeCsv(string value)
        {
            if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
            {
                return $"\"{value.Replace("\"", "\"\"")}\"";
            }

            return value;
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
