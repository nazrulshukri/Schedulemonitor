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
using System.Globalization;
using System.Text;

namespace Atcbassemblyrecipe.Controllers
{
    // The AWACSLF leadframe master grid: one row per leadframe 12NC with its panel
    // size, default work-order quantity, package and device.
    [Authorize]
    public class AwacsLfController : Controller
    {
        private readonly IAwacsLfService _awacsLfService;

        public AwacsLfController(IAwacsLfService awacsLfService)
        {
            _awacsLfService = awacsLfService;
        }

        // The CSV columns this grid understands, in the order the downloaded
        // template writes them - which is also the order a file with no header
        // row is read in.
        private static readonly CsvColumn[] AwacsLfCsvColumns =
        [
            new CsvColumn("LF12NC", true, "LF 12NC", "LEADFRAME12NC", "LEADFRAME 12NC", "LEADFRAME", "12NC"),
            new CsvColumn("LFSIZE", true, "LF SIZE", "LEADFRAME SIZE"),
            new CsvColumn("DEFAULTWOQTY", false, "DEFAULT WO QTY", "DEFAULT QTY", "WOQTY"),
            new CsvColumn("PACKAGE", false, "PKG", "PACKAGE NAME"),
            new CsvColumn("DEVICE", false, "DEVICE NAME", "PRODUCT")
        ];

        [ModuleAccess(ModuleNames.AwacsLf, ModuleAction.View)]
        public async Task<IActionResult> Index(string? search, int page = 1, int pageSize = 25, string? sortBy = "lastupdate", string? sortDirection = "desc", bool promptAdd = false)
        {
            var normalizedSortBy = AwacsLfService.NormalizeSortBy(sortBy);
            var normalizedSortDirection = NormalizeSortDirection(sortDirection);
            var model = new AwacsLfViewModel
            {
                SortBy = normalizedSortBy,
                SortDirection = normalizedSortDirection,
                PromptAdd = promptAdd
            };

            try
            {
                var result = await _awacsLfService.GetAsync(search, page, pageSize, normalizedSortBy, normalizedSortDirection);
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

        public IActionResult DownloadTemplate()
        {
            const string csv = "LF12NC,LFSIZE,DEFAULTWOQTY,PACKAGE,DEVICE\r\n934661404115,\"20,5\",900000,SOT669,BUK9Y59-60E\r\n";
            return File(Encoding.UTF8.GetBytes(csv), "text/csv", "awacslf-template.csv");
        }

        [ModuleAccess(ModuleNames.AwacsLf, ModuleAction.View)]
        public async Task<IActionResult> Export(string? search, string? sortBy = "lastupdate", string? sortDirection = "desc")
        {
            try
            {
                var rows = await _awacsLfService.GetForExportAsync(search, sortBy, sortDirection);
                var builder = new StringBuilder();
                builder.AppendLine("LF12NC,LFSIZE,DEFAULTWOQTY,PACKAGE,DEVICE,Last Updated By,Timestamp");

                foreach (var row in rows)
                {
                    builder
                        .Append(EscapeCsv(row.Lf12Nc)).Append(',')
                        .Append(EscapeCsv(row.LfSize)).Append(',')
                        .Append(EscapeCsv(row.DefaultWoQty?.ToString(CultureInfo.InvariantCulture) ?? string.Empty)).Append(',')
                        .Append(EscapeCsv(row.Package)).Append(',')
                        .Append(EscapeCsv(row.Device)).Append(',')
                        .Append(EscapeCsv(row.LastUpdatedBy)).Append(',')
                        .Append(EscapeCsv(row.LastUpdate?.ToString("yyyy-MM-dd HH:mm") ?? string.Empty))
                        .AppendLine();
                }

                return File(Encoding.UTF8.GetBytes(builder.ToString()), "text/csv", "awacslf-export.csv");
            }
            catch (Exception ex) when (ex is OracleException or InvalidOperationException)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = DatabaseErrorMessage.Build(ex);
                return RedirectToAction(nameof(Index), new { search, sortBy, sortDirection });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [ModuleAccess(ModuleNames.AwacsLf, ModuleAction.Add)]
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
                read = CsvImportReader.Read(stream, AwacsLfCsvColumns);
            }

            if (!read.Success)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = $"CSV import cancelled. Inserted: 0. {read.Error}";
                return RedirectToAction(nameof(Index));
            }

            var importErrors = new List<string>();
            var pendingRows = new List<AwacsLfInputModel>();

            foreach (var row in read.Rows)
            {
                var lf12Nc = row["LF12NC"];
                if (CsvImportReader.LooksLikeExcelScientificNumber(lf12Nc))
                {
                    importErrors.Add($"Line {row.LineNumber}: LF12NC reads '{lf12Nc}'. Excel rounded the 12NC away - format that column as Text and save the file again.");
                    continue;
                }

                var quantityText = row["DEFAULTWOQTY"];
                decimal? quantity = null;
                if (!string.IsNullOrWhiteSpace(quantityText))
                {
                    if (!decimal.TryParse(quantityText, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
                    {
                        importErrors.Add($"Line {row.LineNumber}: DEFAULTWOQTY '{quantityText}' is not a number.");
                        continue;
                    }

                    quantity = parsed;
                }

                var model = new AwacsLfInputModel
                {
                    Lf12Nc = lf12Nc,
                    LfSize = row["LFSIZE"],
                    DefaultWoQty = quantity,
                    Package = row["PACKAGE"],
                    Device = row["DEVICE"],
                    Confirmed = true
                };

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
                var result = await _awacsLfService.CreateManyAsync(pendingRows, User.Identity?.Name ?? "unknown");
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

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [ModuleAccess(ModuleNames.AwacsLf, ModuleAction.Add)]
        public async Task<IActionResult> Create(AwacsLfInputModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = $"Please fill the required AWACSLF values: {BuildModelStateMessage()}";
                return RedirectToAction(nameof(Index), new { promptAdd = true });
            }

            (bool Success, string Message) result;
            try
            {
                result = await _awacsLfService.CreateAsync(model, User.Identity?.Name ?? "unknown");
            }
            catch (Exception ex) when (ex is OracleException or InvalidOperationException)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = DatabaseErrorMessage.Build(ex);
                return RedirectToAction(nameof(Index), new { promptAdd = true });
            }

            TempData["PopupType"] = result.Success ? "success" : "danger";
            TempData["PopupMessage"] = result.Message;

            return result.Success
                ? RedirectToAction(nameof(Index))
                : RedirectToAction(nameof(Index), new { promptAdd = true });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [ModuleAccess(ModuleNames.AwacsLf, ModuleAction.Update)]
        public async Task<IActionResult> Edit(AwacsLfInputModel model)
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
                result = await _awacsLfService.UpdateAsync(model, User.Identity?.Name ?? "unknown");
            }
            catch (Exception ex) when (ex is OracleException or InvalidOperationException)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = DatabaseErrorMessage.Build(ex);
                return RedirectToAction(nameof(Index));
            }

            TempData["PopupType"] = result.Success ? "success" : "danger";
            TempData["PopupMessage"] = result.Message;
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [ModuleAccess(ModuleNames.AwacsLf, ModuleAction.Delete)]
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
                result = await _awacsLfService.DeleteAsync(id, User.Identity?.Name ?? "unknown");
            }
            catch (Exception ex) when (ex is OracleException or InvalidOperationException)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = DatabaseErrorMessage.Build(ex);
                return RedirectToAction(nameof(Index));
            }

            TempData["PopupType"] = result.Success ? "success" : "danger";
            TempData["PopupMessage"] = result.Message;
            return RedirectToAction(nameof(Index));
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
