namespace Atcbassemblyrecipe.Models
{
    public class AwacsWstype
    {
        public string TblRowId { get; set; } = string.Empty;
        public DateTime? LastUpdate { get; set; }
        public string LastUpdatedBy { get; set; } = string.Empty;
        public string WsId { get; set; } = string.Empty;
        public string WsType { get; set; } = string.Empty;
        public string WsDb { get; set; } = string.Empty;
    }
}
