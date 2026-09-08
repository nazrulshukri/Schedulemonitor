using Microsoft.VisualBasic.FileIO;
using System.Globalization;
using System.Text;

namespace Atcbassemblyrecipe.Infrastructure
{
    // Shared CSV reading for the four grid importers (AWACSWSTYPE, Table Sawing,
    // Table Marker, AWACSLF).
    //
    // Every one of them used to demand a header row spelled exactly like the
    // downloaded template: the first line was read as headers no matter what it
    // held, so a file typed by hand - or a template with its header row deleted -
    // was reported as "Missing required header(s)" and nothing was imported.
    //
    // Here the header row is optional. The first line is only treated as a header
    // when its own cells name known columns; otherwise it is data and the columns
    // are read by position, in the same order the template writes them.

    // One column the importer knows about. The order of the array handed to
    // CsvImportReader.Read is the template order, which is also the order a file
    // with no header row is read in.
    public sealed class CsvColumn
    {
        public CsvColumn(string name, bool required = false, params string[] aliases)
        {
            Name = name;
            Required = required;
            Aliases = aliases ?? Array.Empty<string>();
        }

        public string Name { get; }

        // Required columns decide whether a header row can be used at all: a
        // header that does not carry them is rejected rather than guessed at.
        public bool Required { get; }

        // Other spellings accepted for the same column ("Leadframe 12NC", "LF12NC").
        public IReadOnlyList<string> Aliases { get; }

        // False for a column the downloaded template does not write - it can be
        // named in a header row, but it is not part of the fixed order a file
        // with no header row is read in.
        public bool InTemplate { get; init; } = true;
    }

    // One data row, addressed by column name whether the file had headers or not.
    public sealed class CsvImportRow
    {
        private readonly string[] _fields;
        private readonly IReadOnlyDictionary<string, int> _columnIndexes;

        internal CsvImportRow(int lineNumber, string[] fields, IReadOnlyDictionary<string, int> columnIndexes)
        {
            LineNumber = lineNumber;
            _fields = fields;
            _columnIndexes = columnIndexes;
        }

        // The line in the uploaded file, so an error message can point at it.
        public int LineNumber { get; }

        public string this[string column]
        {
            get
            {
                var key = CsvImportReader.NormalizeHeader(column);
                return _columnIndexes.TryGetValue(key, out var index) && index >= 0 && index < _fields.Length
                    ? _fields[index]?.Trim() ?? string.Empty
                    : string.Empty;
            }
        }
    }

    public sealed class CsvImportResult
    {
        private CsvImportResult(bool success, string? error, bool hadHeaderRow, IReadOnlyList<CsvImportRow> rows)
        {
            Success = success;
            Error = error;
            HadHeaderRow = hadHeaderRow;
            Rows = rows;
        }

        public bool Success { get; }

        // Already worded for the popup; the caller prefixes it with its own
        // "CSV import cancelled. Inserted: 0." line.
        public string? Error { get; }

        public bool HadHeaderRow { get; }

        public IReadOnlyList<CsvImportRow> Rows { get; }

        internal static CsvImportResult Failed(string error)
        {
            return new CsvImportResult(false, error, false, []);
        }

        internal static CsvImportResult Loaded(bool hadHeaderRow, IReadOnlyList<CsvImportRow> rows)
        {
            return new CsvImportResult(true, null, hadHeaderRow, rows);
        }
    }

    public static class CsvImportReader
    {
        private static readonly string[] SupportedExtensions = [".csv", ".txt", ".tsv"];

        public static CsvImportResult Read(Stream stream, IReadOnlyList<CsvColumn> columns)
        {
            var rows = new List<(int LineNumber, string[] Fields)>();

            // detectEncoding reads the byte-order mark, so a file Excel saved as
            // "CSV UTF-8" does not arrive with a stray BOM glued to its first cell.
            using var parser = new TextFieldParser(stream, Encoding.UTF8, true);
            parser.TextFieldType = FieldType.Delimited;
            parser.HasFieldsEnclosedInQuotes = true;
            parser.TrimWhiteSpace = true;
            // LFSIZE itself contains a comma, so a quoted "20,5" has to survive
            // the split - HasFieldsEnclosedInQuotes above is what does that.
            // Semicolon and tab are accepted because Excel writes those when the
            // Windows list separator is not a comma.
            parser.SetDelimiters(",", ";", "\t");

            while (!parser.EndOfData)
            {
                var lineNumber = (int)parser.LineNumber;
                string[] fields;
                try
                {
                    fields = parser.ReadFields() ?? [];
                }
                catch (MalformedLineException)
                {
                    return CsvImportResult.Failed($"Line {lineNumber} is not valid CSV. No rows were uploaded.");
                }

                if (fields.Length == 0 || fields.All(string.IsNullOrWhiteSpace))
                {
                    continue;
                }

                rows.Add((lineNumber, fields));
            }

            if (rows.Count == 0)
            {
                return CsvImportResult.Failed("No data rows were found.");
            }

            var columnIndexes = new Dictionary<string, int>(StringComparer.Ordinal);
            var firstRow = rows[0].Fields;
            var hadHeaderRow = LooksLikeHeaderRow(firstRow, columns);

            if (hadHeaderRow)
            {
                for (var index = 0; index < firstRow.Length; index++)
                {
                    var column = MatchColumn(firstRow[index], columns);
                    if (column is null)
                    {
                        continue;
                    }

                    var key = NormalizeHeader(column.Name);
                    if (!columnIndexes.ContainsKey(key))
                    {
                        columnIndexes[key] = index;
                    }
                }

                var missing = columns
                    .Where(column => column.Required && !columnIndexes.ContainsKey(NormalizeHeader(column.Name)))
                    .Select(column => column.Name)
                    .ToList();

                if (missing.Count > 0)
                {
                    // Mapping the columns by position here instead would happily
                    // load a file meant for another grid, so say what was read and
                    // what the two accepted shapes are.
                    var detected = string.Join(", ", firstRow
                        .Where(cell => !string.IsNullOrWhiteSpace(cell))
                        .Select(cell => cell.Trim()));

                    return CsvImportResult.Failed(
                        $"The header row is missing {string.Join(", ", missing)}. It reads: {detected}. " +
                        $"Rename the header(s) to {string.Join(", ", missing)}, or delete the header row " +
                        $"and leave the values in this order: {string.Join(", ", TemplateColumns(columns).Select(column => column.Name))}.");
                }

                rows.RemoveAt(0);

                if (rows.Count == 0)
                {
                    return CsvImportResult.Failed("The file carries only a header row. Add at least one data row and upload again.");
                }
            }
            else
            {
                // No header row: read the template's own columns by position, in
                // the order it writes them.
                var templateColumns = TemplateColumns(columns);
                for (var index = 0; index < templateColumns.Count; index++)
                {
                    columnIndexes[NormalizeHeader(templateColumns[index].Name)] = index;
                }
            }

            return CsvImportResult.Loaded(
                hadHeaderRow,
                rows.Select(row => new CsvImportRow(row.LineNumber, row.Fields, columnIndexes)).ToList());
        }

        // Excel turns a 12-digit leadframe id into 3.4E+11 as soon as its column is
        // not formatted as Text, and Save As writes that back into the file. The
        // digits are gone by then, so the row is rejected rather than stored as a
        // rounded number nobody would notice.
        public static bool LooksLikeExcelScientificNumber(string? value)
        {
            var text = (value ?? string.Empty).Trim();

            return text.Length > 0
                && text.Contains("E", StringComparison.OrdinalIgnoreCase)
                && decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
        }

        // Excel writes .txt for tab-delimited saves and .tsv on some locales. All
        // three are plain text and go through the parser above unchanged.
        public static bool IsSupportedFileName(string? fileName)
        {
            var extension = Path.GetExtension(fileName ?? string.Empty);
            return SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
        }

        internal static string NormalizeHeader(string? header)
        {
            return new string((header ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        }

        // A single cell naming a known column is enough to call the first line a
        // header. A data row - "MARKER,SOT1210,8BCP56G1,340007011059,MARK-PBS54032NX-Q"
        // - names none of them, which is what makes a headerless file safe to read
        // by position.
        //
        // The test leans towards "header" on purpose. Calling a header a data row
        // would write its own column names into the table as if they were values,
        // and a file meant for another grid - an AWACSLF export dropped on Table
        // Sawing, say - shares only some of these names, so it has to be caught by
        // the missing-required-column check above rather than read by position.
        private static bool LooksLikeHeaderRow(string[] row, IReadOnlyList<CsvColumn> columns)
        {
            return row.Any(cell => MatchColumn(cell, columns) is not null);
        }

        private static IReadOnlyList<CsvColumn> TemplateColumns(IReadOnlyList<CsvColumn> columns)
        {
            return columns.Where(column => column.InTemplate).ToList();
        }

        private static CsvColumn? MatchColumn(string? cell, IReadOnlyList<CsvColumn> columns)
        {
            var key = NormalizeHeader(cell);
            if (key.Length == 0)
            {
                return null;
            }

            return columns.FirstOrDefault(column =>
                string.Equals(NormalizeHeader(column.Name), key, StringComparison.Ordinal)
                || column.Aliases.Any(alias => string.Equals(NormalizeHeader(alias), key, StringComparison.Ordinal)));
        }
    }
}
