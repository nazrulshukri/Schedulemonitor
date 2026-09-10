namespace Atcbassemblyrecipe.Models
{
    // The tables whose rows this app edits and can therefore put back. The name
    // is stored on every history/trash row, so it is also what decides which
    // table a restore or revert writes back to.
    public static class AuditedTableNames
    {
        public const string AwacsWstype = "AWACSWSTYPE";
        public const string AwacsRecipeByWstype = "AWACSRECIPEBYWSTYPE";
        public const string AwacsLf = "AWACSLF";
        public const string WireBond = "TBLWIREBOND";

        public static readonly string[] All = [AwacsWstype, AwacsRecipeByWstype, AwacsLf, WireBond];

        public static bool IsSupported(string? tableName)
        {
            return tableName is not null
                && All.Contains(tableName.Trim().ToUpperInvariant(), StringComparer.Ordinal);
        }
    }

    // One saved "before" picture of a row, written inside the same transaction as
    // the UPDATE it describes. Reverting simply writes OldValues back over the
    // row, which is why the snapshot is taken before the change, not after.
    public class ChangeHistoryEntry
    {
        public long HistoryId { get; set; }
        public string TableName { get; set; } = string.Empty;
        public string TargetLabel { get; set; } = string.Empty;
        public string RowKey { get; set; } = string.Empty;
        public IReadOnlyDictionary<string, string?> OldValues { get; set; } = new Dictionary<string, string?>();
        public IReadOnlyDictionary<string, string?> NewValues { get; set; } = new Dictionary<string, string?>();
        public string ChangedBy { get; set; } = string.Empty;
        public DateTime ChangedDate { get; set; }
        public bool Reverted { get; set; }
        public string RevertedBy { get; set; } = string.Empty;
        public DateTime? RevertedDate { get; set; }

        // Only the columns whose value actually moved, so the History page can show
        // "PRODUCT: 8BCP56G1 -> 8BCP56G2" instead of the whole row every time.
        public IReadOnlyList<(string Column, string? From, string? To)> ChangedColumns
        {
            get
            {
                var changes = new List<(string, string?, string?)>();
                foreach (var (column, newValue) in NewValues)
                {
                    var oldValue = OldValues.GetValueOrDefault(column);
                    if (!string.Equals(oldValue ?? string.Empty, newValue ?? string.Empty, StringComparison.Ordinal))
                    {
                        changes.Add((column, oldValue, newValue));
                    }
                }

                return changes;
            }
        }
    }

    // A deleted row parked in the recycle bin. RowData is the whole row, so a
    // restore is a plain INSERT of exactly what was removed.
    public class TrashEntry
    {
        public long TrashId { get; set; }
        public string TableName { get; set; } = string.Empty;
        public string TargetLabel { get; set; } = string.Empty;
        public IReadOnlyDictionary<string, string?> RowData { get; set; } = new Dictionary<string, string?>();
        public string DeletedBy { get; set; } = string.Empty;
        public DateTime DeletedDate { get; set; }
        public bool Restored { get; set; }
        public string RestoredBy { get; set; } = string.Empty;
        public DateTime? RestoredDate { get; set; }
    }
}
