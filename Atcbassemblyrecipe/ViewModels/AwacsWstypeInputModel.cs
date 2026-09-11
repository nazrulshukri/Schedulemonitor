using System.ComponentModel.DataAnnotations;
using Atcbassemblyrecipe.Models;

namespace Atcbassemblyrecipe.ViewModels
{
    public class AwacsWstypeInputModel : IValidatableObject
    {
        // AWACSWSTYPE.WSDB names the table that holds a workstation's recipes.
        // It is NOT typed in: it follows from WSTYPE, so the two can never
        // disagree and nobody can point a SAWING workstation at TBLWIREBOND.
        // AwacsWstypeService.Normalize sets it from WSTYPE on every write.
        public const string SawingWsDb = "TBLSAWING";
        public const string WirebondWsDb = "TBLWIREBOND";

        // The workstation types this page manages. Add a pair here and the
        // grid's WSTYPE list, the validation and the derived WSDB all follow.
        public static readonly IReadOnlyDictionary<string, string> WsDbByWsType =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [RecipeWsTypes.Sawing] = SawingWsDb,
                [RecipeWsTypes.Wirebond] = WirebondWsDb
            };

        public static string WsDbFor(string? wsType)
        {
            return WsDbByWsType.TryGetValue((wsType ?? string.Empty).Trim(), out var wsDb)
                ? wsDb
                : string.Empty;
        }

        public string? TblRowId { get; set; }

        [Required, StringLength(16)]
        [Display(Name = "WSID")]
        public string WsId { get; set; } = string.Empty;

        [Required, StringLength(16)]
        [Display(Name = "WSTYPE")]
        public string WsType { get; set; } = RecipeWsTypes.Sawing;

        // Derived, never posted. Kept as a property because the service writes
        // it to the column and the duplicate check reads it.
        [StringLength(16)]
        [Display(Name = "WSDB")]
        public string WsDb { get; set; } = SawingWsDb;

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
            else if (string.IsNullOrEmpty(WsDbFor(WsType)))
            {
                yield return new ValidationResult(
                    $"WSTYPE must be one of: {string.Join(", ", WsDbByWsType.Keys)}.",
                    [nameof(WsType)]);
            }

            if (!Confirmed)
            {
                yield return new ValidationResult("Confirm validation before saving.", [nameof(Confirmed)]);
            }
        }
    }
}
