using System.ComponentModel.DataAnnotations;
using Atcbassemblyrecipe.Models;

namespace Atcbassemblyrecipe.ViewModels
{
    // One ENGINEERING row, used for both the add row and the inline edit row.
    // TblRowId is empty on an add and carries the row's ROWID on an edit.
    //
    // The cells arrive as a dictionary rather than as properties because the
    // column list is configuration, not code - see
    // OCAPSYS.ENGINEERINGCOLUMNGROUP. The form posts them as
    // Values[LOTNUMBER], Values[RECIPES1] and so on, which is what the default
    // model binder reads a Dictionary<string, string> from.
    //
    // What is NOT here is a whitelist of which keys are allowed. That check
    // belongs to the service, which compares every posted key against the
    // columns the *signed-in user's groups* entitle them to and drops the rest
    // - so a hand-made POST naming a column outside the user's groups writes
    // nothing, exactly as if the cell were not on the page.
    public class EngineeringInputModel : IValidatableObject
    {
        public string? TblRowId { get; set; }

        public Dictionary<string, string?> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        [Display(Name = "I validated this engineering recipe")]
        public bool Confirmed { get; set; }

        public string? Get(string columnName) => Values.GetValueOrDefault(columnName);

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            var lotNumber = Get(EngineeringColumns.LotNumber);

            if (string.IsNullOrWhiteSpace(lotNumber))
            {
                yield return new ValidationResult(
                    "Lot Number is required - it is what the MES side matches a WOID against.",
                    [EngineeringColumns.LotNumber]);
            }
            else if (lotNumber.Trim().Length > 64)
            {
                yield return new ValidationResult("Lot Number is longer than 64 characters.", [EngineeringColumns.LotNumber]);
            }

            // NO is the one numeric column. An unparseable value would reach
            // Oracle as ORA-01722 and come back to the user as a red popup with
            // no idea which cell caused it.
            var number = Get(EngineeringColumns.No);
            if (!string.IsNullOrWhiteSpace(number) && !long.TryParse(number.Trim(), out _))
            {
                yield return new ValidationResult($"No must be a whole number - '{number.Trim()}' is not.", [EngineeringColumns.No]);
            }

            foreach (var pair in Values)
            {
                if (!string.IsNullOrWhiteSpace(pair.Value)
                    && !EngineeringColumns.IsNumeric(pair.Key)
                    && pair.Value.Trim().Length > 120)
                {
                    yield return new ValidationResult($"{pair.Key} is longer than 120 characters.", [pair.Key]);
                }
            }

            if (!Confirmed)
            {
                yield return new ValidationResult("Validate and confirm before saving.", [nameof(Confirmed)]);
            }
        }
    }
}
