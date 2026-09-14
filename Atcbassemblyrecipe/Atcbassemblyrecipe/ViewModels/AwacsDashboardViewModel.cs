using Atcbassemblyrecipe.Models;

namespace Atcbassemblyrecipe.ViewModels
{
    public class AwacsDashboardViewModel
    {
        public IReadOnlyList<AwacsWstype> Rows { get; set; } = [];
        public string Search { get; set; } = string.Empty;
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 25;
        public int TotalRows { get; set; }
        public string SortBy { get; set; } = "lastupdate";
        public string SortDirection { get; set; } = "desc";

        // WSDB values already in AWACSWSTYPE - SENSORS, POWER and whatever else
        // the plant uses - offered as the pick list on the add and edit rows.
        public IReadOnlyList<string> WsDbOptions { get; set; } = [];
        public int TotalPages => TotalRows == 0 ? 1 : (int)Math.Ceiling(TotalRows / (double)PageSize);
    }
}
