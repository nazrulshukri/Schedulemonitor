namespace Atcbassemblyrecipe.Models
{
    // One row of OCAPSYS.ENGINEERING - an engineering lot and the recipe it
    // uses at every process step it runs.
    //
    // The recipe columns are NOT properties. There are 23 of them, which
    // group owns which is decided in OCAPSYS.ENGINEERINGCOLUMNGROUP rather
    // than in code, and moving a column between groups must not need a
    // recompile - so the values arrive in a dictionary keyed by column name
    // and the page renders whichever columns the signed-in user's groups
    // entitle them to. The identity of the row (lot number, package,
    // product) is reached the same way, through the indexer.
    //
    // Rows are addressed by Oracle ROWID (handed to the page as TblRowId),
    // the way AWACSLF, AWACSRECIPEBYWSTYPE and TBLWIREBOND already are.
    // TBLROWID is still filled with RAWTOHEX(SYS_GUID()) on insert for the
    // MES side.
    public class Engineering
    {
        public string TblRowId { get; set; } = string.Empty;
        public DateTime? LastUpdate { get; set; }
        public string LastUpdatedBy { get; set; } = string.Empty;

        // Column name (uppercase, as Oracle spells it) -> the cell's text.
        public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

        // Missing is empty, never null: a column the user's group cannot see
        // is simply not in the dictionary, and an unfilled step is a NULL in
        // the database. Neither is an error worth throwing over.
        public string this[string columnName] => Values.GetValueOrDefault(columnName, string.Empty);

        // The lot number is the one column everything keys on - the MES side
        // matches a WOID against it - so it is worth naming.
        public string LotNumber => this[EngineeringColumns.LotNumber];
    }

    // The columns this app knows by name because it treats them specially,
    // rather than as "one more recipe cell".
    public static class EngineeringColumns
    {
        public const string No = "NO";
        public const string Requestor = "REQUESTOR";
        public const string LotNumber = "LOTNUMBER";
        public const string Package = "PACKAGE";
        public const string Product = "PRODUCT";

        // TBLROWID / LASTUPDATE / LASTUPDATEDBY are bookkeeping, not data.
        // They are never offered as a groupable column and never editable.
        public static readonly string[] Bookkeeping =
        [
            "TBLROWID", "LASTUPDATE", "LASTUPDATEDBY"
        ];

        // "NO" and "PACKAGE" are reserved words in Oracle: they have to be
        // written double-quoted and uppercase in every statement or the
        // query dies with ORA-00904. Everything else is written bare.
        private static readonly HashSet<string> NeedsQuoting =
            new(StringComparer.OrdinalIgnoreCase) { No, Package };

        public static string Quote(string columnName)
        {
            return NeedsQuoting.Contains(columnName)
                ? $"\"{columnName.ToUpperInvariant()}\""
                : columnName.ToUpperInvariant();
        }

        // NO is the only numeric column, so it is the only one that cannot be
        // bound as a string.
        public static bool IsNumeric(string columnName)
        {
            return string.Equals(columnName, No, StringComparison.OrdinalIgnoreCase);
        }
    }

    // One row of OCAPSYS.ENGINEERINGCOLUMNGROUP: an ENGINEERING column, the
    // user group that owns it, and the AWACSWSTYPE.WSTYPE it is the recipe
    // for on the MES side (null for a column no workstation asks for).
    public sealed record EngineeringColumn(
        string Name,
        string GroupName,
        string? WsType,
        string Label,
        int SortOrder)
    {
        public bool IsShared => string.Equals(GroupName, EngineeringGroups.Shared, StringComparison.OrdinalIgnoreCase);
    }

    // The user groups the Engineering page divides its columns into. A group
    // is not a new thing to administer: it is one of the module grants the
    // app already keeps in TBLACCESS, so granting somebody the Sawing module
    // is what puts the sawing recipe columns on their Engineering page.
    public static class EngineeringGroups
    {
        public const string Sawing = "SAWING";
        public const string Wirebond = "WIREBOND";
        public const string Marker = "MARKER";

        // Not a team - the row's identity (lot number, package, product).
        // Shown to everybody who can open the page at all.
        public const string Shared = "SHARED";

        public static readonly string[] All = [Sawing, Wirebond, Marker, Shared];

        // Which TBLACCESS module grant makes a group visible. The three
        // module names are the ones already on the sidebar, so the Engineering
        // page needs no separate permission bookkeeping.
        public static string? ModuleFor(string groupName)
        {
            return groupName?.Trim().ToUpperInvariant() switch
            {
                Sawing => ModuleNames.TableSawing,
                Wirebond => ModuleNames.TableWirebond,
                Marker => ModuleNames.TableMarker,
                _ => null
            };
        }
    }
}
