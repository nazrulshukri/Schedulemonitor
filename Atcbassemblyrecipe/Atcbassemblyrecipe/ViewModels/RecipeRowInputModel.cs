using System.ComponentModel.DataAnnotations;
using Atcbassemblyrecipe.Models;

namespace Atcbassemblyrecipe.ViewModels
{
    // A new AWACSRECIPEBYWSTYPE row typed straight into a grid's add row. The same
    // model backs the SAWING and the MARKER grid - only WSTYPE differs, and the
    // page always supplies it.
    public class RecipeRowInputModel : IValidatableObject
    {
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

        [Display(Name = "I validated this recipe does not already exist")]
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
