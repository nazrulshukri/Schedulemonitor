using Atcbassemblyrecipe.Models;

namespace Atcbassemblyrecipe.ViewModels
{
    public class EngineeringViewModel
    {
        public IReadOnlyList<Engineering> Rows { get; set; } = [];

        // The columns this user may see: the shared ones plus their groups'.
        // The grid renders exactly these, in this order.
        public IReadOnlyList<EngineeringColumn> Columns { get; set; } = [];

        // SAWING / WIREBOND / MARKER - whichever of the three the user holds a
        // TBLACCESS module grant for. Named in the page heading so it is obvious
        // why two people see different columns on the same page.
        public IReadOnlyList<string> UserGroups { get; set; } = [];

        // False means ENGINEERINGCOLUMNGROUP could not be read and the built-in
        // mapping is standing in. The page says so rather than looking correct.
        public bool ColumnsFromDatabase { get; set; } = true;

        public string Search { get; set; } = string.Empty;
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 25;
        public int TotalRows { get; set; }
        public string SortBy { get; set; } = "lastupdate";
        public string SortDirection { get; set; } = "desc";
        public bool PromptAdd { get; set; }

        // LOTNUMBER of the row that was just inserted or updated. The grid
        // flashes it and scrolls to it, so a save visibly lands.
        public string Highlight { get; set; } = string.Empty;

        public int TotalPages => TotalRows == 0 ? 1 : (int)Math.Ceiling(TotalRows / (double)PageSize);

        public IEnumerable<EngineeringColumn> SharedColumns => Columns.Where(column => column.IsShared);

        public IEnumerable<EngineeringColumn> GroupColumns => Columns.Where(column => !column.IsShared);
    }
}
