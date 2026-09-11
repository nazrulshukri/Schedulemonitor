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

        // The CSV columns this grid understands, in the order the downloaded
        // template writes them - which is also the order a file with no header
        // row is read in.
        private static readonly CsvColumn[] AwacsWstypeCsvColumns =
        [
            new CsvColumn("WSID", true, "WS ID", "WORKSTATION", "WORKSTATION ID"),
            new CsvColumn("WSTYPE", true, "WS TYPE", "WORKSTATION TYPE")
        ];

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

            if (!CsvImportReader.IsSupportedFileName(csvFile.FileName))
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = "Upload a text file only (.csv, .txt or .tsv). If Excel changed it to .xlsx, use Save As > CSV (Comma delimited) before uploading.";
                return RedirectToAction(nameof(Index));
            }

            CsvImportResult read;
            using (var stream = csvFile.OpenReadStream())
            {
                read = CsvImportReader.Read(stream, AwacsWstypeCsvColumns);
            }

            if (!read.Success)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = $"CSV import cancelled. Inserted: 0. {read.Error}";
                return RedirectToAction(nameof(Index));
            }

            var importErrors = new List<string>();
            var pendingRows = new List<AwacsWstypeInputModel>();

            foreach (var row in read.Rows)
            {
                var model = new AwacsWstypeInputModel
                {
                    WsId = row["WSID"],
                    WsType = row["WSTYPE"],
                    // WSDB is not a CSV column and is not set here: the service
                    // derives it from WSTYPE, so an uploaded WIREBOND row gets
                    // TBLWIREBOND and a SAWING row gets TBLSAWING.
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
                    ? $"{result.Message}{HeaderlessNote(read)}"
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

        // A file with no header row was mapped by column position. Say so, because
        // it is the one thing about the import the popup would otherwise hide.
        private static string HeaderlessNote(CsvImportResult read)
        {
            return read.HadHeaderRow
                ? string.Empty
                : " The file had no header row, so the columns were read in template order.";
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
