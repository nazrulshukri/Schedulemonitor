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
    // The TBLWIREBOND OCAP log: one row per wire bond OCAP record.
    //
    // Separate from AwacsController.TableWirebond, which is the WIREBOND *recipe*
    // grid over AWACSRECIPEBYWSTYPE. Two different tables that happen to share a
    // word, so they are two different modules with their own TBLACCESS grants.
    [Authorize]
    public class WireBondController : Controller
    {
        private readonly IWireBondService _wireBondService;

        public WireBondController(IWireBondService wireBondService)
        {
            _wireBondService = wireBondService;
        }

        // The CSV columns this grid understands. The first block is the template
        // order, which is also the order a file with no header row is read in; the
        // InTemplate = false block can still be named in a header row, so a full
        // export can be edited and uploaded again without trimming columns.
        //
        // WBOCAPWWK is not here at all - the database trigger owns it.
        private static readonly CsvColumn[] WireBondCsvColumns =
        [
            new CsvColumn("OCAPNO", true, "OCAP NO", "WBOCAPNO", "OCAP NUMBER"),
            new CsvColumn("WBDATE", true, "OCAP DATE", "DATE", "OCAPDATE"),
            new CsvColumn("ISSUEDBY", false, "ISSUED BY", "WBISSUEDBY"),
            new CsvColumn("BFG", false, "WBBFG"),
            new CsvColumn("OPERATORID", false, "OPERATOR ID", "WBOPERATORID", "OPERATOR"),
            new CsvColumn("PROCESS", false, "WBPROCESS"),
            new CsvColumn("MACHINE", false, "WBMACHINE"),
            new CsvColumn("PACKAGE", false, "WBPACKAGE", "PKG"),
            new CsvColumn("SOQTY", false, "SO QTY", "WBSOQTY", "QTY"),
            new CsvColumn("DEFECT", false, "WBDEFECT"),
            new CsvColumn("DEFECTCAT", false, "DEFECT CATEGORY", "WBDEFECTCAT", "CATEGORY"),
            new CsvColumn("DIFF4M1E", false, "4M1E", "WBDIFF4M1E"),
            new CsvColumn("DIFFNO", false, "DIFFERENCE NO", "WBDIFFNO"),
            new CsvColumn("DIFFREJECTQTY", false, "REJECT QTY", "WBDIFFREJECTQTY"),
            new CsvColumn("REMARKS", false, "WBREMARKS"),
            new CsvColumn("DEFECTOTHERS", false, "WBDEFECTOTHERS") { InTemplate = false },
            new CsvColumn("DIFFAFFECTED", false, "AFFECTED", "WBDIFFAFFECTED") { InTemplate = false },
            new CsvColumn("DIFFFABSITE", false, "FAB SITE", "WBDIFFFABSITE") { InTemplate = false },
            new CsvColumn("DIFFNOTAFFECTED", false, "NOT AFFECTED", "WBDIFFNOTAFFECTED") { InTemplate = false },
            new CsvColumn("DIFFREMARKS", false, "WBDIFFREMARKS") { InTemplate = false },
            new CsvColumn("VERIFIEDBY", false, "VERIFIED BY", "WBVERIFIEDBY") { InTemplate = false },
            new CsvColumn("ACTIONTAKEN", false, "ACTION TAKEN", "WBACTIONTAKEN") { InTemplate = false },
            new CsvColumn("DISPOSITION", false, "WBDISPOSITION") { InTemplate = false },
            new CsvColumn("RCMACHINEERROR", false, "WBRCMACHINEERROR", "MACHINE ERROR ROOT CAUSE") { InTemplate = false },
            new CsvColumn("MACHINEERROR", false, "WBMACHINEERROR", "MACHINE ERROR") { InTemplate = false }
        ];

        // The date shapes a plant CSV actually arrives in. Parsed exactly rather
        // than by the server locale, so 09/10/2026 cannot silently become
        // September on one machine and October on another.
        private static readonly string[] DateFormats =
        [
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-dd HH:mm",
            "yyyy-MM-dd",
            "yyyy/MM/dd HH:mm",
            "yyyy/MM/dd",
            "dd-MM-yyyy HH:mm",
            "dd-MM-yyyy",
            "dd/MM/yyyy HH:mm",
            "dd/MM/yyyy",
            "dd-MMM-yyyy HH:mm",
            "dd-MMM-yyyy"
        ];

        [ModuleAccess(ModuleNames.TableWirebond, ModuleAction.View)]
        public async Task<IActionResult> Index(string? search, int page = 1, int pageSize = 25, string? sortBy = "lastupdate", string? sortDirection = "desc", bool promptAdd = false)
        {
            var normalizedSortBy = WireBondService.NormalizeSortBy(sortBy);
            var normalizedSortDirection = NormalizeSortDirection(sortDirection);
            var model = new WireBondViewModel
            {
                SortBy = normalizedSortBy,
                SortDirection = normalizedSortDirection,
                PromptAdd = promptAdd
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
                "OCAPNO,WBDATE,ISSUEDBY,BFG,OPERATORID,PROCESS,MACHINE,PACKAGE,SOQTY,DEFECT,DEFECTCAT,DIFF4M1E,DIFFNO,DIFFREJECTQTY,REMARKS\r\n"
                + "OCAP-WB-2026-001,2026-09-10 14:30,NX487878,BFG1,OP1023,WIREBOND,WB-07,SOT669,3000,NON STICK ON PAD,PROCESS,MACHINE,DIFF-2026-0912,120,Raised on night shift\r\n";
            return File(Encoding.UTF8.GetBytes(csv), "text/csv", "tblwirebond-template.csv");
        }

        [ModuleAccess(ModuleNames.TableWirebond, ModuleAction.View)]
        public async Task<IActionResult> Export(string? search, string? sortBy = "lastupdate", string? sortDirection = "desc")
        {
            try
            {
                var rows = await _wireBondService.GetForExportAsync(search, sortBy, sortDirection);
                var builder = new StringBuilder();
                builder.AppendLine(
                    "OCAPNO,OCAPWWK,WBDATE,ISSUEDBY,BFG,OPERATORID,PROCESS,MACHINE,PACKAGE,SOQTY,"
                    + "DEFECT,DEFECTCAT,DEFECTOTHERS,DIFF4M1E,DIFFAFFECTED,DIFFFABSITE,DIFFNO,"
                    + "DIFFNOTAFFECTED,DIFFREJECTQTY,DIFFREMARKS,VERIFIEDBY,ACTIONTAKEN,DISPOSITION,"
                    + "REMARKS,RCMACHINEERROR,MACHINEERROR,Last Updated By,Timestamp");

                foreach (var row in rows)
                {
                    builder
                        .Append(EscapeCsv(row.OcapNo)).Append(',')
                        .Append(EscapeCsv(row.OcapWorkWeek)).Append(',')
                        .Append(EscapeCsv(row.OcapDate?.ToString("yyyy-MM-dd HH:mm") ?? string.Empty)).Append(',')
                        .Append(EscapeCsv(row.IssuedBy)).Append(',')
                        .Append(EscapeCsv(row.Bfg)).Append(',')
                        .Append(EscapeCsv(row.OperatorId)).Append(',')
                        .Append(EscapeCsv(row.Process)).Append(',')
                        .Append(EscapeCsv(row.Machine)).Append(',')
                        .Append(EscapeCsv(row.Package)).Append(',')
                        .Append(EscapeCsv(row.SoQty?.ToString(CultureInfo.InvariantCulture) ?? string.Empty)).Append(',')
                        .Append(EscapeCsv(row.Defect)).Append(',')
                        .Append(EscapeCsv(row.DefectCategory)).Append(',')
                        .Append(EscapeCsv(row.DefectOthers)).Append(',')
                        .Append(EscapeCsv(row.Diff4M1E)).Append(',')
                        .Append(EscapeCsv(row.DiffAffected)).Append(',')
                        .Append(EscapeCsv(row.DiffFabSite)).Append(',')
                        .Append(EscapeCsv(row.DiffNo)).Append(',')
                        .Append(EscapeCsv(row.DiffNotAffected)).Append(',')
                        .Append(EscapeCsv(row.DiffRejectQty?.ToString(CultureInfo.InvariantCulture) ?? string.Empty)).Append(',')
                        .Append(EscapeCsv(row.DiffRemarks)).Append(',')
                        .Append(EscapeCsv(row.VerifiedBy)).Append(',')
                        .Append(EscapeCsv(row.ActionTaken)).Append(',')
                        .Append(EscapeCsv(row.Disposition)).Append(',')
                        .Append(EscapeCsv(row.Remarks)).Append(',')
                        .Append(EscapeCsv(row.RcMachineError)).Append(',')
                        .Append(EscapeCsv(row.MachineError)).Append(',')
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
            var seenOcapNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in read.Rows)
            {
                var dateText = row["WBDATE"];
                if (!TryParseDate(dateText, out var ocapDate))
                {
                    importErrors.Add($"Line {row.LineNumber}: WBDATE '{dateText}' is not a date. Use 2026-09-10 14:30 or 10/09/2026.");
                    continue;
                }

                if (!TryParseNumber(row["SOQTY"], out var soQty))
                {
                    importErrors.Add($"Line {row.LineNumber}: SOQTY '{row["SOQTY"]}' is not a number.");
                    continue;
                }

                if (!TryParseNumber(row["DIFFREJECTQTY"], out var rejectQty))
                {
                    importErrors.Add($"Line {row.LineNumber}: DIFFREJECTQTY '{row["DIFFREJECTQTY"]}' is not a number.");
                    continue;
                }

                var ocapNo = row["OCAPNO"];

                // Two rows in one file carrying the same OCAP number would both
                // pass the database check (neither is committed yet) and land as a
                // duplicate pair. Caught here instead.
                if (!string.IsNullOrWhiteSpace(ocapNo) && !seenOcapNumbers.Add(InputText.CleanUpper(ocapNo)))
                {
                    importErrors.Add($"Line {row.LineNumber}: OCAP No {ocapNo} appears more than once in this file.");
                    continue;
                }

                var model = new WireBondInputModel
                {
                    OcapNo = ocapNo,
                    OcapDate = ocapDate,
                    IssuedBy = row["ISSUEDBY"],
                    Bfg = row["BFG"],
                    OperatorId = row["OPERATORID"],
                    Process = row["PROCESS"],
                    Machine = row["MACHINE"],
                    Package = row["PACKAGE"],
                    SoQty = soQty,
                    Defect = row["DEFECT"],
                    DefectCategory = row["DEFECTCAT"],
                    DefectOthers = row["DEFECTOTHERS"],
                    Diff4M1E = row["DIFF4M1E"],
                    DiffAffected = row["DIFFAFFECTED"],
                    DiffFabSite = row["DIFFFABSITE"],
                    DiffNo = row["DIFFNO"],
                    DiffNotAffected = row["DIFFNOTAFFECTED"],
                    DiffRejectQty = rejectQty,
                    DiffRemarks = row["DIFFREMARKS"],
                    VerifiedBy = row["VERIFIEDBY"],
                    ActionTaken = row["ACTIONTAKEN"],
                    Disposition = row["DISPOSITION"],
                    Remarks = row["REMARKS"],
                    RcMachineError = row["RCMACHINEERROR"],
                    MachineError = row["MACHINEERROR"],
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

            return result.Success
                ? RedirectToAction(nameof(Index))
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
            return RedirectToAction(nameof(Index));
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

        // Empty is allowed - most of these columns are optional. Only a value that
        // is present and unparseable is an error.
        private static bool TryParseDate(string? text, out DateTime? value)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                return true;
            }

            if (DateTime.TryParseExact(text.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                value = parsed;
                return true;
            }

            return false;
        }

        private static bool TryParseNumber(string? text, out decimal? value)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                return true;
            }

            if (CsvImportReader.LooksLikeExcelScientificNumber(text))
            {
                return false;
            }

            if (decimal.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            {
                value = parsed;
                return true;
            }

            return false;
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
        // ORA-00904 or ORA-00942 raised by this page's own SQL would read as if
        // the database were unreachable, and the grid would just look empty. Real
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
