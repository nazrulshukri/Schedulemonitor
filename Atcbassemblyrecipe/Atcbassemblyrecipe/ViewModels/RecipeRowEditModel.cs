using System.ComponentModel.DataAnnotations;
using Atcbassemblyrecipe.Models;

namespace Atcbassemblyrecipe.ViewModels
{
    // An existing AWACSRECIPEBYWSTYPE row being edited in place.
    //
    // RECIPE used to be a read-only property computed from PRODUCT, which meant
    // typing a product silently rewrote the recipe and a recipe could never be
    // corrected on its own. It is a normal editable field now: PRODUCT edits
    // PRODUCT, RECIPE edits RECIPE. The add row still *offers* the product-derived
    // recipe as a starting value, but only while the field is untouched.
    public class RecipeRowEditModel : IValidatableObject
    {
        [Required]
        public string TblRowId { get; set; } = string.Empty;

        [Required, StringLength(16)]
        [Display(Name = "WSTYPE")]
        public string WsType { get; set; } = RecipeWsTypes.Sawing;

        [StringLength(64)]
        public string? Package { get; set; }

        [Required, StringLength(64)]
        public string Product { get; set; } = string.Empty;

        [Required, StringLength(16)]
        [Display(Name = "Leadframe 12NC")]
        public string Leadframe12Nc { get; set; } = string.Empty;

        [Required, StringLength(120)]
        public string Recipe { get; set; } = string.Empty;

        [Display(Name = "I validated this recipe update")]
        public bool Confirmed { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (string.IsNullOrWhiteSpace(WsType))
            {
                yield return new ValidationResult("WSTYPE is required.", [nameof(WsType)]);
            }
            else if (!RecipeWsTypes.IsSupported(WsType))
            {
                yield return new ValidationResult($"WSTYPE must be one of: {string.Join(", ", RecipeWsTypes.All)}.", [nameof(WsType)]);
            }

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
