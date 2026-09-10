using Atcbassemblyrecipe.Models;

namespace Atcbassemblyrecipe.ViewModels
{
    public class WireBondViewModel
    {
        public IReadOnlyList<WireBond> Rows { get; set; } = [];
        public string Search { get; set; } = string.Empty;
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 25;
        public int TotalRows { get; set; }
        public string SortBy { get; set; } = "lastupdate";
        public string SortDirection { get; set; } = "desc";
        public bool PromptAdd { get; set; }

        // WSID values from AWACSWSTYPE for WSTYPE = 'WIREBOND', offered as the
        // Machine pick list. Empty is fine - the field is free text either way.
        public IReadOnlyList<string> MachineOptions { get; set; } = [];

        // WBOCAPNO of the row that was just inserted or updated. The grid flashes
        // it and scrolls to it, so a save visibly lands instead of the user
        // having to look for it.
        public string Highlight { get; set; } = string.Empty;

        public int TotalPages => TotalRows == 0 ? 1 : (int)Math.Ceiling(TotalRows / (double)PageSize);
    }
}
