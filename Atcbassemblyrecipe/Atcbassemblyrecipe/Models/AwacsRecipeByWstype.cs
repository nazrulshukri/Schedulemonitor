namespace Atcbassemblyrecipe.Models
{
    public class AwacsRecipeByWstype
    {
        public string TblRowId { get; set; } = string.Empty;
        public DateTime? LastUpdate { get; set; }
        public string LastUpdatedBy { get; set; } = string.Empty;
        public string WsType { get; set; } = string.Empty;
        public string Package { get; set; } = string.Empty;
        public string Product { get; set; } = string.Empty;
        public string Leadframe12Nc { get; set; } = string.Empty;
        public string Recipe { get; set; } = string.Empty;
    }

    // AWACSRECIPEBYWSTYPE holds one recipe surface per workstation type. The app
    // exposes one grid per WSTYPE value, so the value is never free text - it is
    // always one of these, checked before it reaches a query.
    public static class RecipeWsTypes
    {
        public const string Sawing = "SAWING";
        public const string Marker = "MARKER";

        public static readonly string[] All = [Sawing, Marker];

        public static bool IsSupported(string? wsType)
        {
            return wsType is not null
                && All.Contains(wsType.Trim().ToUpperInvariant(), StringComparer.Ordinal);
        }

        public static string Normalize(string? wsType)
        {
            var value = wsType?.Trim().ToUpperInvariant() ?? string.Empty;
            return IsSupported(value) ? value : Sawing;
        }
    }
}
