using System.ComponentModel.DataAnnotations;

namespace Atcbassemblyrecipe.ViewModels
{
    public class SawingRecipeInputModel : IValidatableObject
    {
        [Required, StringLength(16)]
        [Display(Name = "WSTYPE")]
        public string WsType { get; set; } = "SAWING";

        [StringLength(64)]
        public string? Package { get; set; }

        [Required, StringLength(16)]
        [Display(Name = "Crystal / Reference 12NC")]
        public string Leadframe12Nc { get; set; } = string.Empty;

        [Required, StringLength(120)]
        [Display(Name = "CEPT Description")]
        public string CeptDescription { get; set; } = string.Empty;

        [Display(Name = "I validated this recipe does not already exist")]
        public bool Confirmed { get; set; }

        public string Product => ExtractProduct(CeptDescription);

        public string Recipe => Product.Replace("/", "-", StringComparison.Ordinal);

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (string.IsNullOrWhiteSpace(WsType))
            {
                yield return new ValidationResult("WSTYPE is required.", [nameof(WsType)]);
            }
            else if (!string.Equals(WsType.Trim(), "SAWING", StringComparison.OrdinalIgnoreCase))
            {
                yield return new ValidationResult("WSTYPE must be SAWING.", [nameof(WsType)]);
            }

            if (string.IsNullOrWhiteSpace(Leadframe12Nc))
            {
                yield return new ValidationResult("Leadframe 12NC is required.", [nameof(Leadframe12Nc)]);
            }

            if (string.IsNullOrWhiteSpace(CeptDescription))
            {
                yield return new ValidationResult("CEPT Description is required.", [nameof(CeptDescription)]);
            }

            if (!Confirmed)
            {
                yield return new ValidationResult("Validate and confirm before saving.", [nameof(Confirmed)]);
            }

            if (!string.IsNullOrWhiteSpace(CeptDescription) && string.IsNullOrWhiteSpace(Product))
            {
                yield return new ValidationResult("CEPT Description must contain a product value.", [nameof(CeptDescription)]);
            }
        }

        public static string ExtractProduct(string ceptDescription)
        {
            if (string.IsNullOrWhiteSpace(ceptDescription))
            {
                return string.Empty;
            }

            var cleanValue = ceptDescription.Trim().ToUpperInvariant();
            var lastSlash = cleanValue.LastIndexOf('/');
            return lastSlash <= 0 ? cleanValue : cleanValue[..lastSlash];
        }
    }
}
