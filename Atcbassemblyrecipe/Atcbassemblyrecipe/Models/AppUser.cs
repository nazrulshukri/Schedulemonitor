using System.ComponentModel.DataAnnotations;

namespace Atcbassemblyrecipe.Models
{
    public class AppUser
    {
        public int Id { get; set; }

        [Required, StringLength(80)]
        public string UserName { get; set; } = string.Empty;

        [Required, EmailAddress, StringLength(160)]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string PasswordHash { get; set; } = string.Empty;

        [Required, StringLength(30)]
        public string Role { get; set; } = AppRole.User;

        public bool IsActive { get; set; } = true;
    }
}
