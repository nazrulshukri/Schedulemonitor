using System.Text.RegularExpressions;

namespace Atcbassemblyrecipe.Infrastructure
{
    // One place for the whitespace rule every grid and form shares, so a value
    // typed as " SOT  89 " can never reach the database as anything other than
    // "SOT 89". Two rows that differ only by stray spacing would otherwise slip
    // past the duplicate checks, which compare the stored text.
    //
    // The rule: no leading or trailing spaces, and a run of inner whitespace
    // collapses to a single space. Inner single spaces are preserved.
    public static partial class InputText
    {
        [GeneratedRegex(@"\s+")]
        private static partial Regex WhitespaceRuns();

        // Trims the ends and collapses inner whitespace runs to one space.
        public static string Clean(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : WhitespaceRuns().Replace(value.Trim(), " ");
        }

        // Clean plus upper-case, for the columns stored upper-cased.
        public static string CleanUpper(string? value)
        {
            return Clean(value).ToUpperInvariant();
        }

        // Clean, but an empty result becomes null for nullable columns.
        public static string? CleanOrNull(string? value)
        {
            var cleaned = Clean(value);
            return cleaned.Length == 0 ? null : cleaned;
        }

        public static string? CleanUpperOrNull(string? value)
        {
            var cleaned = CleanUpper(value);
            return cleaned.Length == 0 ? null : cleaned;
        }

        // True when the raw value carries spacing the Clean rule would change.
        // Used by the input models to tell the user rather than silently rewrite.
        public static bool NeedsCleaning(string? value)
        {
            return value is not null && value != Clean(value);
        }
    }
}
