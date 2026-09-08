using Atcbassemblyrecipe.Authorization;
using Atcbassemblyrecipe.Data;
using Atcbassemblyrecipe.Models;
using Atcbassemblyrecipe.Services;
using Atcbassemblyrecipe.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualBasic.FileIO;
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

            if (!string.Equals(Path.GetExtension(csvFile.FileName), ".csv", StringComparison.OrdinalIgnoreCase))
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = "Upload a real .csv file only. If Excel changed it to .xlsx, use Save As > CSV (Comma delimited) before uploading.";
                return RedirectToAction(nameof(Index));
            }

            var lineNumber = 1;
            var importErrors = new List<string>();
            var pendingRows = new List<AwacsLfInputModel>();

            try
            {
                using var stream = csvFile.OpenReadStream();
                using var parser = new TextFieldParser(stream);
                parser.TextFieldType = FieldType.Delimited;
                parser.HasFieldsEnclosedInQuotes = true;
                parser.TrimWhiteSpace = true;
                // LFSIZE itself contains a comma, so a quoted "20,5" has to survive
                // the split - HasFieldsEnclosedInQuotes above is what does that.
                parser.SetDelimiters(",", "\t", ";");

                if (!parser.EndOfData)
                {
                    var headers = parser.ReadFields() ?? [];
                    var fieldMap = BuildCsvFieldMap(headers);
                    var missingHeaders = MissingCsvHeaders(fieldMap, "LF12NC", "LFSIZE");
                    if (missingHeaders.Count > 0)
                    {
                        TempData["PopupType"] = "danger";
                        TempData["PopupMessage"] = $"CSV import cancelled. Inserted: 0. Missing required header(s): {string.Join(", ", missingHeaders)}.";
                        return RedirectToAction(nameof(Index));
                    }

                    while (!parser.EndOfData)
                    {
                        lineNumber++;
                        var fields = parser.ReadFields() ?? [];
                        if (fields.Length == 0 || fields.All(string.IsNullOrWhiteSpace))
                        {
                            continue;
                        }

                        var quantityText = GetCsvField(fields, fieldMap, "DEFAULTWOQTY");
                        decimal? quantity = null;
                        if (!string.IsNullOrWhiteSpace(quantityText))
                        {
                            if (!decimal.TryParse(quantityText, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
                            {
                                importErrors.Add($"Line {lineNumber}: DEFAULTWOQTY '{quantityText}' is not a number.");
                                continue;
                            }

                            quantity = parsed;
                        }

                        var model = new AwacsLfInputModel
                        {
                            Lf12Nc = GetCsvField(fields, fieldMap, "LF12NC"),
                            LfSize = GetCsvField(fields, fieldMap, "LFSIZE"),
                            DefaultWoQty = quantity,
                            Package = GetCsvField(fields, fieldMap, "PACKAGE"),
                            Device = GetCsvField(fields, fieldMap, "DEVICE"),
                            Confirmed = true
                        };

                        var validationResults = new List<ValidationResult>();
                        if (!Validator.TryValidateObject(model, new ValidationContext(model), validationResults, true))
                        {
                            importErrors.Add($"Line {lineNumber}: {string.Join("; ", validationResults.Select(result => result.ErrorMessage))}");
                            continue;
                        }

                        pendingRows.Add(model);
                    }
                }
            }
            catch (MalformedLineException)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = $"CSV import cancelled. Inserted: 0. Line {lineNumber} is not valid CSV. No rows were uploaded.";
                return RedirectToAction(nameof(Index));
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
                TempData["PopupMessage"] = result.Success ? result.Message : $"{result.Message} Inserted: 0.";
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

        private static Dictionary<string, int> BuildCsvFieldMap(string[] headers)
        {
            var fieldMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < headers.Length; index++)
            {
                var key = NormalizeCsvHeader(headers[index]);
                if (!string.IsNullOrWhiteSpace(key) && !fieldMap.ContainsKey(key))
                {
                    fieldMap[key] = index;
                }
            }

            return fieldMap;
        }

        private static List<string> MissingCsvHeaders(IReadOnlyDictionary<string, int> fieldMap, params string[] requiredHeaders)
        {
            return requiredHeaders
                .Where(header => !fieldMap.ContainsKey(NormalizeCsvHeader(header)))
                .ToList();
        }

        private static string GetCsvField(string[] fields, IReadOnlyDictionary<string, int> fieldMap, string key)
        {
            return fieldMap.TryGetValue(NormalizeCsvHeader(key), out var index) && index >= 0 && index < fields.Length
                ? fields[index]?.Trim() ?? string.Empty
                : string.Empty;
        }

        private static string NormalizeCsvHeader(string header)
        {
            return new string(header.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
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
