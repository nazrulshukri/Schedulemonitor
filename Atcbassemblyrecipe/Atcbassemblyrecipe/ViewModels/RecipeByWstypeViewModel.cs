using Atcbassemblyrecipe.Models;

namespace Atcbassemblyrecipe.ViewModels
{
    // Backs both recipe grids. AWACSRECIPEBYWSTYPE holds one recipe surface per
    // workstation type, so Table Sawing and Table Marker are the same page over a
    // different WSTYPE - the model carries which one, and which TBLACCESS module
    // guards it.
    public class RecipeByWstypeViewModel
    {
        public string WsType { get; set; } = RecipeWsTypes.Sawing;
        public string ModuleName { get; set; } = ModuleNames.TableSawing;
        public string PageTitle { get; set; } = "Table Sawing";
        public string PageDescription { get; set; } = string.Empty;
        public string GridAction { get; set; } = "TableSawing";
        public string StorageKey { get; set; } = "tablesawing";

        public IReadOnlyList<AwacsRecipeByWstype> Rows { get; set; } = [];
        public string Search { get; set; } = string.Empty;
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 25;
        public int TotalRows { get; set; }
        public string SortBy { get; set; } = "lastupdate";
        public string SortDirection { get; set; } = "desc";
        public bool PromptAdd { get; set; }
        public int TotalPages => TotalRows == 0 ? 1 : (int)Math.Ceiling(TotalRows / (double)PageSize);
    }
}
