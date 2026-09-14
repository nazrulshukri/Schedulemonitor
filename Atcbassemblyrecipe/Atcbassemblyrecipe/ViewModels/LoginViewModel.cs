using System.ComponentModel.DataAnnotations;

namespace Atcbassemblyrecipe.ViewModels
{
    public class LoginViewModel
    {
        [Required, StringLength(80)]
        [Display(Name = "Username")]
        public string UserName { get; set; } = string.Empty;

        [Required, DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Display(Name = "Remember me")]
        public bool RememberMe { get; set; }
    }
}
