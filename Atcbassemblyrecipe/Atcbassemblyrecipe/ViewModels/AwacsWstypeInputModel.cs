using System.ComponentModel.DataAnnotations;

namespace Atcbassemblyrecipe.ViewModels
{
    public class AwacsWstypeInputModel : IValidatableObject
    {
        // AWACSWSTYPE.WSDB is NOT NULL and this page only ever writes TBLSAWING,
        // so the column was dropped from the grid and the CSV. The value is still
        // supplied on save from here.
        public const string SawingWsDb = "TBLSAWING";

        public string? TblRowId { get; set; }

        [Required, StringLength(16)]
        [Display(Name = "WSID")]
        public string WsId { get; set; } = string.Empty;

        [Required, StringLength(16)]
        [Display(Name = "WSTYPE")]
        public string WsType { get; set; } = "SAWING";

        [Required, StringLength(16)]
        [Display(Name = "WSDB")]
        public string WsDb { get; set; } = SawingWsDb;

        [Display(Name = "I verified this AWACSWSTYPE row maps to TBLSAWING")]
        public bool Confirmed { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (string.IsNullOrWhiteSpace(WsId))
            {
                yield return new ValidationResult("WSID is required.", [nameof(WsId)]);
            }
            else if (!WsId.Trim().All(character => char.IsLetterOrDigit(character) || character is '-' or '_'))
            {
                yield return new ValidationResult("WSID can only contain letters, numbers, hyphen, or underscore.", [nameof(WsId)]);
            }

            if (string.IsNullOrWhiteSpace(WsType))
            {
                yield return new ValidationResult("WSTYPE is required.", [nameof(WsType)]);
            }

            if (string.IsNullOrWhiteSpace(WsDb))
            {
                yield return new ValidationResult("WSDB is required.", [nameof(WsDb)]);
            }

            if (!Confirmed)
            {
                yield return new ValidationResult("Confirm validation before saving.", [nameof(Confirmed)]);
            }

            if (!string.IsNullOrWhiteSpace(WsType) && !string.Equals(WsType.Trim(), "SAWING", StringComparison.OrdinalIgnoreCase))
            {
                yield return new ValidationResult("Only WSTYPE SAWING is allowed for this page.", [nameof(WsType)]);
            }

            if (!string.IsNullOrWhiteSpace(WsDb) && !string.Equals(WsDb.Trim(), "TBLSAWING", StringComparison.OrdinalIgnoreCase))
            {
                yield return new ValidationResult("Only WSDB TBLSAWING is allowed for this page.", [nameof(WsDb)]);
            }
        }
    }
}
