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
    // The ENGINEERING grid: one row per engineering lot, one column per process
    // step's recipe.
    //
    // Two permissions, not one:
    //
    //   * ModuleNames.Engineering ("Engineering") decides whether the page opens
    //     at all, and whether Add / Edit / Delete are offered - the same
    //     [ModuleAccess] gate every other page uses.
    //   * The Sawing / Wirebond / Marker module grants decide WHICH RECIPE
    //     COLUMN the user sees inside it. ENGINEERING carries one recipe per
    //     group, so a user granted Sawing gets RECIPESAWING and neither sees
    //     nor can write the other two groups' columns.
    //
    // The second one is enforced in EngineeringService, not here and not in the
    // view: hiding a column in Razor is presentation only, so the service drops
    // any posted cell outside the user's groups before it builds a statement.
    [Authorize]
    public class EngineeringController : Controller
    {
        private readonly IEngineeringService _engineeringService;
        private readonly IEngineeringColumnGroupProvider _columnGroups;

        public EngineeringController(
            IEngineeringService engineeringService,
            IEngineeringColumnGroupProvider columnGroups)
        {
            _engineeringService = engineeringService;
            _columnGroups = columnGroups;
        }

        [ModuleAccess(ModuleNames.Engineering, ModuleAction.View)]
        public async Task<IActionResult> Index(string? search, int page = 1, int pageSize = 25, string? sortBy = "lastupdate", string? sortDirection = "desc", bool promptAdd = false, string? highlight = null)
        {
            var model = new EngineeringViewModel
            {
                SortBy = await _engineeringService.NormalizeSortByAsync(sortBy),
                SortDirection = NormalizeSortDirection(sortDirection),
                PromptAdd = promptAdd,
                Highlight = highlight?.Trim() ?? string.Empty
            };

            try
            {
                model.Columns = await _columnGroups.GetVisibleAsync();
                model.UserGroups = await _columnGroups.GetUserGroupsAsync();
                model.ColumnsFromDatabase = await _columnGroups.IsFromDatabaseAsync();

                var result = await _engineeringService.GetAsync(search, page, pageSize, model.SortBy, model.SortDirection);
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

        // The template is the user's own columns, so somebody in the sawing
        // group downloads a sawing-shaped file rather than one carrying two
        // recipe columns they may not fill in.
        [ModuleAccess(ModuleNames.Engineering, ModuleAction.View)]
        public async Task<IActionResult> DownloadTemplate()
        {
            var columns = await _columnGroups.GetVisibleAsync();
            var header = string.Join(",", columns.Select(column => column.Name));
            var example = string.Join(",", columns.Select(ExampleValue));

            return File(Encoding.UTF8.GetBytes($"{header}\r\n{example}\r\n"), "text/csv", "engineering-template.csv");
        }

        [ModuleAccess(ModuleNames.Engineering, ModuleAction.View)]
        public async Task<IActionResult> Export(string? search, string? sortBy = "lastupdate", string? sortDirection = "desc")
        {
            try
            {
                var columns = await _columnGroups.GetVisibleAsync();
                var rows = await _engineeringService.GetForExportAsync(search, sortBy, sortDirection);

                var builder = new StringBuilder();
                builder.AppendLine(string.Join(",", columns.Select(column => column.Name).Concat(["Last Updated By", "Timestamp"])));

                foreach (var row in rows)
                {
                    var cells = columns
                        .Select(column => EscapeCsv(row[column.Name]))
                        .Concat([EscapeCsv(row.LastUpdatedBy), EscapeCsv(row.LastUpdate?.ToString("yyyy-MM-dd HH:mm") ?? string.Empty)]);

                    builder.AppendLine(string.Join(",", cells));
                }

                return File(Encoding.UTF8.GetBytes(builder.ToString()), "text/csv", "engineering-export.csv");
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
        [ModuleAccess(ModuleNames.Engineering, ModuleAction.Add)]
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

            var columns = await _columnGroups.GetVisibleAsync();

            // Only the user's own columns are offered to the reader, so a file
            // exported by somebody in another group loads the cells they have in
            // common and ignores the rest rather than being rejected outright.
            var csvColumns = columns
                .Select(column => new CsvColumn(
                    column.Name,
                    string.Equals(column.Name, EngineeringColumns.LotNumber, StringComparison.OrdinalIgnoreCase),
                    column.Label))
                .ToList();

            CsvImportResult read;
            using (var stream = csvFile.OpenReadStream())
            {
                read = CsvImportReader.Read(stream, csvColumns);
            }

            if (!read.Success)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = $"CSV import cancelled. Inserted: 0. {read.Error}";
                return RedirectToAction(nameof(Index));
            }

            var importErrors = new List<string>();
            var pendingRows = new List<EngineeringInputModel>();
            var seenLots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in read.Rows)
            {
                var model = new EngineeringInputModel { Confirmed = true };
                foreach (var column in columns)
                {
                    model.Values[column.Name] = row[column.Name];
                }

                var lotNumber = model.Get(EngineeringColumns.LotNumber) ?? string.Empty;

                // Two rows in one file carrying the same lot would both pass the
                // database check (neither is committed yet) and land as a pair
                // nobody can tell apart - and the MES lookup keys on this value.
                if (!seenLots.Add(InputText.CleanUpper(lotNumber)))
                {
                    importErrors.Add($"Line {row.LineNumber}: lot {lotNumber} appears more than once in this file.");
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
                var result = await _engineeringService.CreateManyAsync(pendingRows, User.Identity?.Name ?? "unknown");
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
        [ModuleAccess(ModuleNames.Engineering, ModuleAction.Add)]
        public async Task<IActionResult> Create(EngineeringInputModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = $"Please fill the required ENGINEERING values: {BuildModelStateMessage()}";
                return RedirectToAction(nameof(Index), new { promptAdd = true });
            }

            (bool Success, string Message) result;
            try
            {
                result = await _engineeringService.CreateAsync(model, User.Identity?.Name ?? "unknown");
            }
            catch (Exception ex) when (ex is OracleException or InvalidOperationException)
            {
                TempData["PopupType"] = "danger";
                TempData["PopupMessage"] = Describe(ex);
                return RedirectToAction(nameof(Index), new { promptAdd = true });
            }

            TempData["PopupType"] = result.Success ? "success" : "danger";
            TempData["PopupMessage"] = result.Message;

            return result.Success
                ? RedirectToAction(nameof(Index), new { highlight = InputText.CleanUpper(model.Get(EngineeringColumns.LotNumber)) })
                : RedirectToAction(nameof(Index), new { promptAdd = true });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [ModuleAccess(ModuleNames.Engineering, ModuleAction.Update)]
        public async Task<IActionResult> Edit(EngineeringInputModel model)
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
                result = await _engineeringService.UpdateAsync(model, User.Identity?.Name ?? "unknown");
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
                ? RedirectToAction(nameof(Index), new { highlight = InputText.CleanUpper(model.Get(EngineeringColumns.LotNumber)) })
                : RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [ModuleAccess(ModuleNames.Engineering, ModuleAction.Delete)]
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
                result = await _engineeringService.DeleteAsync(id, User.Identity?.Name ?? "unknown");
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

        private static string ExampleValue(EngineeringColumn column)
        {
            return column.Name.ToUpperInvariant() switch
            {
                EngineeringColumns.No => "1",
                EngineeringColumns.Requestor => "Abdullah",
                EngineeringColumns.LotNumber => "ENGXTA54780B",
                EngineeringColumns.Package => "SOT1235",
                EngineeringColumns.Product => "PSMNR50-40SSH",
                _ => string.Empty
            };
        }

        private static string HeaderlessNote(CsvImportResult read)
        {
            return read.HadHeaderRow
                ? string.Empty
                : " The file had no header row, so the columns were read in template order.";
        }

        // DatabaseErrorMessage.Build answers every OracleException with the same
        // "check the password, VPN and service name" text, which is right for a
        // connection failure and misleading for anything else - an ORA-00904 or
        // ORA-02290 raised by this page's own SQL would read as if the database
        // were unreachable.
        private static string Describe(Exception ex)
        {
            if (ex is OracleException oracle && !IsConnectionError(oracle.Number))
            {
                if (oracle.Number == 942)
                {
                    return "ORA-00942: ENGINEERING does not exist yet. Run Database/engineering.sql against the OCAPSYS schema, then Database/engineeringcolumngroup.sql and Database/engineeringwstype.sql.";
                }

                // ORA-00904 here means a column this page asked for is not on the
                // table - so the table and the mapping disagree. Almost always an
                // ENGINEERING left over from an earlier shape.
                if (oracle.Number == 904)
                {
                    return $"{oracle.Message.Trim()} - that column is not on the ENGINEERING table. "
                         + "The table and ENGINEERINGCOLUMNGROUP disagree: ENGINEERING should carry \"NO\", REQUESTOR, "
                         + "LOTNUMBER, \"PACKAGE\", PRODUCT, RECIPESAWING, RECIPEWIREBOND and RECIPEMARKER. "
                         + "Run Database/engineering-check.sql to see what it actually has, then Database/engineering.sql to rebuild it.";
                }

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
