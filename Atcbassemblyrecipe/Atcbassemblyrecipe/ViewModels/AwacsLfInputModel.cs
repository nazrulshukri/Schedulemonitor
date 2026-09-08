using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace Atcbassemblyrecipe.ViewModels
{
    // One AWACSLF leadframe row, used for both the add row and the inline edit row.
    // TblRowId is empty on an add and carries the row's ROWID on an edit.
    public partial class AwacsLfInputModel : IValidatableObject
    {
        public string? TblRowId { get; set; }

        [Required, StringLength(16)]
        [Display(Name = "LF 12NC")]
        public string Lf12Nc { get; set; } = string.Empty;

        // Keyed in as "<units across>,<units down>", e.g. 20,5 for a SOT669
        // leadframe. Stored exactly in that shape so the MES side keeps reading it.
        [Required, StringLength(16)]
        [Display(Name = "LFSIZE")]
        public string LfSize { get; set; } = string.Empty;

        [Display(Name = "Default WO Qty")]
        [Range(0, 999999999)]
        public decimal? DefaultWoQty { get; set; }

        [StringLength(64)]
        [Display(Name = "PACKAGE")]
        public string? Package { get; set; }

        [StringLength(120)]
        [Display(Name = "DEVICE")]
        public string? Device { get; set; }

        [Display(Name = "I validated this leadframe row")]
        public bool Confirmed { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (string.IsNullOrWhiteSpace(Lf12Nc))
            {
                yield return new ValidationResult("LF 12NC is required.", [nameof(Lf12Nc)]);
            }

            if (string.IsNullOrWhiteSpace(LfSize))
            {
                yield return new ValidationResult("LFSIZE is required.", [nameof(LfSize)]);
            }
            else if (!LfSizePattern().IsMatch(LfSize.Trim()))
            {
                yield return new ValidationResult(
                    "LFSIZE must be two numbers separated by a comma, for example 20,5. Known sizes: SOT669 20,5 - SOT669 HDLF 36,10 - SOT1210 30,6 - SOT1235 24,5.",
                    [nameof(LfSize)]);
            }

            if (!Confirmed)
            {
                yield return new ValidationResult("Validate and confirm before saving.", [nameof(Confirmed)]);
            }
        }

        // Collapses "20 , 5" and "20,5" to the single stored shape "20,5".
        public static string NormalizeLfSize(string? lfSize)
        {
            if (string.IsNullOrWhiteSpace(lfSize))
            {
                return string.Empty;
            }

            var match = LfSizePattern().Match(lfSize.Trim());
            return match.Success
                ? $"{match.Groups["across"].Value},{match.Groups["down"].Value}"
                : lfSize.Trim();
        }

        [GeneratedRegex(@"^(?<across>\d{1,4})\s*,\s*(?<down>\d{1,4})$")]
        private static partial Regex LfSizePattern();
    }
}
