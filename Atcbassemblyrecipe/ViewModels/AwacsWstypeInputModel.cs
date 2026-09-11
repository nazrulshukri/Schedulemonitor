using System.ComponentModel.DataAnnotations;
using Atcbassemblyrecipe.Models;

namespace Atcbassemblyrecipe.ViewModels
{
    public class AwacsWstypeInputModel : IValidatableObject
    {
        // The workstation types this page manages. The table holds others
        // (MOULD, DIEBOND, CLIPBOND, OVEN, TAPING ...) which have no recipe grid
        // here and are deliberately left alone.
        public static readonly string[] PageWsTypes = [RecipeWsTypes.Sawing, RecipeWsTypes.Wirebond];

        public string? TblRowId { get; set; }

        [Required, StringLength(16)]
        [Display(Name = "WSID")]
        public string WsId { get; set; } = string.Empty;

        [Required, StringLength(16)]
        [Display(Name = "WSTYPE")]
        public string WsType { get; set; } = RecipeWsTypes.Sawing;

        // The business group the machine belongs to - SENSORS, POWER and so on.
        // Plant data, typed in by the user and offered as a pick list built from
        // the values already in the table.
        //
        // This is NOT a table name. An earlier version of this page treated it as
        // one and wrote TBLSAWING into it, which is why some rows carry that
        // value; see Database/awacswstype-wsdb-repair.sql.
        [Required, StringLength(16)]
        [Display(Name = "WSDB")]
        public string WsDb { get; set; } = string.Empty;

        [Display(Name = "I verified this AWACSWSTYPE row")]
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
            else if (!PageWsTypes.Contains(WsType.Trim().ToUpperInvariant(), StringComparer.Ordinal))
            {
                yield return new ValidationResult(
                    $"This page manages {string.Join(" and ", PageWsTypes)} workstations only.",
                    [nameof(WsType)]);
            }

            if (string.IsNullOrWhiteSpace(WsDb))
            {
                yield return new ValidationResult("WSDB is required.", [nameof(WsDb)]);
            }

            if (!Confirmed)
            {
                yield return new ValidationResult("Confirm validation before saving.", [nameof(Confirmed)]);
            }
        }
    }
}
