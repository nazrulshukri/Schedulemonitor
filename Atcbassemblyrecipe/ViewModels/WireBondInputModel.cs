using System.ComponentModel.DataAnnotations;

namespace Atcbassemblyrecipe.ViewModels
{
    // One TBLWIREBOND row, used for both the add row and the inline edit row.
    // TblRowId is empty on an add and carries the row's ROWID on an edit.
    //
    // StringLength on every field matches the column width in
    // Database/tblwirebond.sql. Keep the two in step - Oracle answers a value
    // that is one character too long with ORA-12899, which reaches the user as a
    // red popup rather than "that field is too long".
    public class WireBondInputModel : IValidatableObject
    {
        public string? TblRowId { get; set; }

        [StringLength(64)]
        [Display(Name = "Package")]
        public string? Package { get; set; }

        [Required, StringLength(64)]
        [Display(Name = "Product")]
        public string Product { get; set; } = string.Empty;

        [Required, StringLength(16)]
        [Display(Name = "Leadframe 12NC")]
        public string Leadframe12Nc { get; set; } = string.Empty;

        [Required, StringLength(120)]
        [Display(Name = "Recipe")]
        public string Recipe { get; set; } = string.Empty;

        [Display(Name = "I validated this wirebond recipe")]
        public bool Confirmed { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (string.IsNullOrWhiteSpace(Product))
            {
                yield return new ValidationResult("Product is required.", [nameof(Product)]);
            }

            if (string.IsNullOrWhiteSpace(Leadframe12Nc))
            {
                yield return new ValidationResult("Leadframe 12NC is required.", [nameof(Leadframe12Nc)]);
            }

            if (string.IsNullOrWhiteSpace(Recipe))
            {
                yield return new ValidationResult("Recipe is required.", [nameof(Recipe)]);
            }

            if (!Confirmed)
            {
                yield return new ValidationResult("Validate and confirm before saving.", [nameof(Confirmed)]);
            }
        }
    }
}
