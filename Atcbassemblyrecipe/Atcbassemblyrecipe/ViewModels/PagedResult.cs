namespace Atcbassemblyrecipe.ViewModels
{
    public class PagedResult<T>
    {
        public IReadOnlyList<T> Rows { get; set; } = [];
        public string Search { get; set; } = string.Empty;
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 25;
        public int TotalRows { get; set; }

        public int TotalPages => TotalRows == 0 ? 1 : (int)Math.Ceiling(TotalRows / (double)PageSize);
    }
}
