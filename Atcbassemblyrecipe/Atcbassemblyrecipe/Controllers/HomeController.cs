using Atcbassemblyrecipe.Authorization;
using Atcbassemblyrecipe.Models;
using Atcbassemblyrecipe.Data;
using Atcbassemblyrecipe.Services;
using Atcbassemblyrecipe.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualBasic.FileIO;
using Oracle.ManagedDataAccess.Client;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Text;

namespace Atcbassemblyrecipe.Controllers
{
    public class HomeController : Controller
    {
        private readonly IAwacsWstypeService _awacsWstypeService;

        public HomeController(IAwacsWstypeService awacsWstypeService)
        {
            _awacsWstypeService = awacsWstypeService;
        }

        [ModuleAccess(ModuleNames.AwacsWstype, ModuleAction.View)]
        public async Task<IActionResult> Index(string? search, int page = 1, int pageSize = 25, string? sortBy = "lastupdate", string? sortDirection = "desc")
        {
            var normalizedSortBy = NormalizeAwacsSortBy(sortBy);
            var normalizedSortDirection = NormalizeSortDirection(sortDirection);

            try
            {
                var result = await _awacsWstypeService.GetAwacsWstypeAsync(search, page, pageSize, normalizedSortBy, normalizedSortDirection);
                var model = new AwacsDashboardViewModel
                {
                    Rows = result.Rows,
                    Search = result.Search,
                    Page = result.Page,
                    PageSize = result.PageSize,
                    TotalRows = result.TotalRows,
                    SortBy = normalizedSortBy,
                    SortDirection = normalizedSortDirection
                };

                return View(model);
            }
            catch (Exception ex) when (ex is OracleException or InvalidOperationException)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = DatabaseErrorMessage.Build(ex);

                return View(new AwacsDashboardViewModel());
            }
        }

        [Authorize]
        public IActionResult DownloadAwacsTemplate()
        {
            const string csv = "WSID,WSTYPE\r\nDB-AD3-014,SAWING\r\n";
            return File(Encoding.UTF8.GetBytes(csv), "text/csv", "awacswstype-template.csv");
        }

        [ModuleAccess(ModuleNames.AwacsWstype, ModuleAction.View)]
        public async Task<IActionResult> ExportAwacsWstype(string? search, string? sortBy = "lastupdate", string? sortDirection = "desc")
        {
            try
            {
                var rows = await _awacsWstypeService.GetAwacsWstypeForExportAsync(search, sortBy, sortDirection);
                var builder = new StringBuilder();
                builder.AppendLine("WSID,WSTYPE,Last Updated By,Timestamp");

                foreach (var row in rows)
                {
                    builder
                        .Append(EscapeCsv(row.WsId)).Append(',')
                        .Append(EscapeCsv(row.WsType)).Append(',')
                        .Append(EscapeCsv(row.LastUpdatedBy)).Append(',')
                        .Append(EscapeCsv(row.LastUpdate?.ToString("yyyy-MM-dd HH:mm") ?? string.Empty))
                        .AppendLine();
                }

                return File(Encoding.UTF8.GetBytes(builder.ToString()), "text/csv", "awacswstype-export.csv");
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
        [ModuleAccess(ModuleNames.AwacsWstype, ModuleAction.Add)]
        public async Task<IActionResult> ImportAwacsWstype(IFormFile? csvFile)
        {
            if (csvFile is null || csvFile.Length == 0)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = "Choose a CSV file before importing.";
                return RedirectToAction(nameof(Index));
            }

            var extension = Path.GetExtension(csvFile.FileName);
            var allowedExtensions = new[] { ".csv" };
            if (!allowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = "Upload a real .csv file only. If Excel changed it to .xlsx, use Save As > CSV (Comma delimited) before uploading.";
                return RedirectToAction(nameof(Index));
            }

            var lineNumber = 1;
            var importErrors = new List<string>();
            var pendingRows = new List<AwacsWstypeInputModel>();

            try
            {
                using var stream = csvFile.OpenReadStream();
                using var parser = new TextFieldParser(stream);
                parser.TextFieldType = FieldType.Delimited;
                parser.HasFieldsEnclosedInQuotes = true;
                parser.TrimWhiteSpace = true;
                parser.SetDelimiters(",", "\t", ";");

                if (!parser.EndOfData)
                {
                    var headers = parser.ReadFields() ?? [];
                    var fieldMap = BuildCsvFieldMap(headers);
                    var missingHeaders = MissingCsvHeaders(fieldMap, "WSID", "WSTYPE");
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

                        var model = new AwacsWstypeInputModel
                        {
                            WsId = GetCsvField(fields, fieldMap, "WSID"),
                            WsType = GetCsvField(fields, fieldMap, "WSTYPE"),
                            // WSDB is no longer a CSV column; AWACSWSTYPE still requires the value.
                            WsDb = AwacsWstypeInputModel.SawingWsDb,
                            Confirmed = true
                        };

                        var validationResults = new List<ValidationResult>();
                        var validationContext = new ValidationContext(model);
                        if (!Validator.TryValidateObject(model, validationContext, validationResults, true))
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
            catch (Exception ex) when (ex is OracleException or InvalidOperationException)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = DatabaseErrorMessage.Build(ex);
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
                var result = await _awacsWstypeService.CreateManyAsync(pendingRows, User.Identity?.Name ?? "unknown");
                TempData["PopupType"] = result.Success ? "success" : "danger";
                TempData["PopupMessage"] = result.Success
                    ? result.Message
                    : $"{result.Message} Inserted: 0.";
            }
            catch (Exception ex) when (ex is OracleException or InvalidOperationException)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = DatabaseErrorMessage.Build(ex);
                return RedirectToAction(nameof(Index));
            }

            return RedirectToAction(nameof(Index));
        }

        [Authorize]
        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
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
            return fieldMap.TryGetValue(key, out var index) && index >= 0 && index < fields.Length
                ? fields[index]?.Trim() ?? string.Empty
                : string.Empty;
        }

        private static string NormalizeCsvHeader(string header)
        {
            return new string(header.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        }

        private static string NormalizeAwacsSortBy(string? sortBy)
        {
            return sortBy?.Trim().ToLowerInvariant() switch
            {
                "sequence" => "sequence",
                "wsid" => "wsid",
                "wstype" => "wstype",
                "lastupdatedby" => "lastupdatedby",
                "lastupdate" => "lastupdate",
                _ => "lastupdate"
            };
        }

        private static string NormalizeSortDirection(string? sortDirection)
        {
            return string.Equals(sortDirection, "asc", StringComparison.OrdinalIgnoreCase) ? "asc" : "desc";
        }
    }
}
