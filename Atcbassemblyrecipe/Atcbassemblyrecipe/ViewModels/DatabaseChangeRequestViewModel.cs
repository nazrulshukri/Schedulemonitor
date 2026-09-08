using System.ComponentModel.DataAnnotations;
using Atcbassemblyrecipe.Models;

namespace Atcbassemblyrecipe.ViewModels
{
    // Field lengths mirror TBLDBREQUEST (Database/tbldbrequest.sql) so oversized
    // input is rejected with a friendly message instead of an ORA-12899.
    public class DatabaseChangeRequestViewModel
    {
        [Required]
        [StringLength(30)]
        [Display(Name = "Request type")]
        public string RequestType { get; set; } = string.Empty;

        [Required, StringLength(80)]
        [Display(Name = "Target table")]
        public string TargetTable { get; set; } = string.Empty;

        [Required, StringLength(500)]
        [Display(Name = "Reason")]
        public string Reason { get; set; } = string.Empty;

        // Populated for display only; not posted back.
        public List<DatabaseChangeRequest> RecentRequests { get; set; } = [];
        public bool CanReview { get; set; }
        public bool EmailConfigured { get; set; } = true;
    }
}
