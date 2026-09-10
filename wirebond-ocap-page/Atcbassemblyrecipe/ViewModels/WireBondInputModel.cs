using System.ComponentModel.DataAnnotations;

namespace Atcbassemblyrecipe.ViewModels
{
    // One TBLWIREBOND row, used for both the add panel and the inline edit panel.
    // TblRowId is empty on an add and carries the row's ROWID on an edit.
    //
    // WBOCAPWWK is deliberately absent: the BEFORE INSERT trigger
    // OCAP_WIREBOND_WORKWEEK computes the work week from WBDATE, so anything the
    // form posted would be overwritten. The grid and the panel show it read-only.
    //
    // StringLength on every field matches the column width in
    // Database/tblwirebond.sql. Keep the two in step - Oracle answers a value
    // that is one character too long with ORA-12899, which reaches the user as a
    // connection-failure popup rather than "that field is too long".
    public class WireBondInputModel : IValidatableObject
    {
        public string? TblRowId { get; set; }

        [Required, StringLength(100)]
        [Display(Name = "OCAP No")]
        public string OcapNo { get; set; } = string.Empty;

        [StringLength(100)]
        [Display(Name = "Issued By")]
        public string? IssuedBy { get; set; }

        [StringLength(100)]
        [Display(Name = "BFG")]
        public string? Bfg { get; set; }

        [Display(Name = "OCAP Date")]
        [DataType(DataType.DateTime)]
        public DateTime? OcapDate { get; set; }

        [StringLength(50)]
        [Display(Name = "Operator ID")]
        public string? OperatorId { get; set; }

        [StringLength(100)]
        [Display(Name = "Process")]
        public string? Process { get; set; }

        [StringLength(100)]
        [Display(Name = "Machine")]
        public string? Machine { get; set; }

        [StringLength(100)]
        [Display(Name = "Package")]
        public string? Package { get; set; }

        [Display(Name = "SO Qty")]
        [Range(0, 999999999)]
        public decimal? SoQty { get; set; }

        [StringLength(100)]
        [Display(Name = "Defect")]
        public string? Defect { get; set; }

        [StringLength(100)]
        [Display(Name = "Defect Category")]
        public string? DefectCategory { get; set; }

        [StringLength(100)]
        [Display(Name = "Defect (Others)")]
        public string? DefectOthers { get; set; }

        [StringLength(100)]
        [Display(Name = "4M1E")]
        public string? Diff4M1E { get; set; }

        [StringLength(100)]
        [Display(Name = "Affected")]
        public string? DiffAffected { get; set; }

        [StringLength(100)]
        [Display(Name = "Fab Site")]
        public string? DiffFabSite { get; set; }

        [StringLength(100)]
        [Display(Name = "Difference No")]
        public string? DiffNo { get; set; }

        [StringLength(100)]
        [Display(Name = "Not Affected")]
        public string? DiffNotAffected { get; set; }

        [Display(Name = "Reject Qty")]
        [Range(0, 999999999)]
        public decimal? DiffRejectQty { get; set; }

        [StringLength(2000)]
        [Display(Name = "Difference Remarks")]
        public string? DiffRemarks { get; set; }

        [StringLength(100)]
        [Display(Name = "Verified By")]
        public string? VerifiedBy { get; set; }

        [StringLength(2000)]
        [Display(Name = "Action Taken")]
        public string? ActionTaken { get; set; }

        [StringLength(2000)]
        [Display(Name = "Disposition")]
        public string? Disposition { get; set; }

        [StringLength(500)]
        [Display(Name = "Remarks")]
        public string? Remarks { get; set; }

        [StringLength(1000)]
        [Display(Name = "Machine Error - Root Cause")]
        public string? RcMachineError { get; set; }

        [StringLength(100)]
        [Display(Name = "Machine Error")]
        public string? MachineError { get; set; }

        [Display(Name = "I validated this OCAP record")]
        public bool Confirmed { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (string.IsNullOrWhiteSpace(OcapNo))
            {
                yield return new ValidationResult("OCAP No is required.", [nameof(OcapNo)]);
            }

            // The work week is derived from this date by the database trigger, so a
            // row without one lands in the week it was keyed in rather than the
            // week it happened. Cheap to insist on it at the point of entry.
            if (OcapDate is null)
            {
                yield return new ValidationResult(
                    "OCAP Date is required - the work week is calculated from it.",
                    [nameof(OcapDate)]);
            }
            else if (OcapDate > DateTime.Now.AddDays(1))
            {
                yield return new ValidationResult(
                    "OCAP Date cannot be in the future.",
                    [nameof(OcapDate)]);
            }

            // Only meaningful when the defect itself is OTHERS, and the other way
            // round a free-text defect with no code is unsearchable.
            if (string.Equals(Defect?.Trim(), "OTHERS", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(DefectOthers))
            {
                yield return new ValidationResult(
                    "Defect is OTHERS, so say what the defect was in Defect (Others).",
                    [nameof(DefectOthers)]);
            }

            if (!Confirmed)
            {
                yield return new ValidationResult("Validate and confirm before saving.", [nameof(Confirmed)]);
            }
        }
    }
}
