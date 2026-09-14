namespace Atcbassemblyrecipe.Models
{
    // One row of the AWACSLF leadframe master table.
    public class AwacsLf
    {
        public string TblRowId { get; set; } = string.Empty;
        public DateTime? LastUpdate { get; set; }
        public string LastUpdatedBy { get; set; } = string.Empty;
        public string Lf12Nc { get; set; } = string.Empty;
        public string LfSize { get; set; } = string.Empty;
        public decimal? DefaultWoQty { get; set; }
        public string Package { get; set; } = string.Empty;
        public string Device { get; set; } = string.Empty;
    }

    // LFSIZE is keyed in as "<units across>,<units down>". Production only uses a
    // handful of leadframes, so the known pairs are offered as pick-list hints on
    // the AWACSLF page - the field itself still accepts any "number,number" value
    // so a new leadframe does not need a code change.
    public static class LeadframeSizes
    {
        public static readonly (string Package, string LfSize)[] Known =
        [
            ("SOT669", "20,5"),
            ("SOT669 HDLF", "36,10"),
            ("SOT1210", "30,6"),
            ("SOT1235", "24,5")
        ];

        public static string? SuggestFor(string? package)
        {
            if (string.IsNullOrWhiteSpace(package))
            {
                return null;
            }

            var value = package.Trim().ToUpperInvariant();
            foreach (var (knownPackage, lfSize) in Known)
            {
                if (string.Equals(knownPackage, value, StringComparison.OrdinalIgnoreCase))
                {
                    return lfSize;
                }
            }

            return null;
        }
    }
}
