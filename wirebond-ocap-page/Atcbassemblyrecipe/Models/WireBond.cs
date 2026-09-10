namespace Atcbassemblyrecipe.Models
{
    // One row of OCAPSYS.TBLWIREBOND: a wire bond OCAP record - who raised it,
    // the machine and package it was raised against, the defect, the 4M1E
    // difference behind it, and the disposition that closed it.
    //
    // Not to be confused with the Wirebond *recipe* page, which is
    // AWACSRECIPEBYWSTYPE filtered to WSTYPE = 'WIREBOND'. Different table,
    // different module, different data - the name is the only thing they share.
    //
    // TBLWIREBOND has a TBLROWID column, but it is NULL on every row that was
    // keyed in by hand before this page existed. So a row is addressed by its
    // Oracle ROWID (handed to the page as TblRowId), the way AWACSLF and
    // AWACSRECIPEBYWSTYPE already are, and TBLROWID is still filled with
    // RAWTOHEX(SYS_GUID()) on insert for the MES side.
    public class WireBond
    {
        public string TblRowId { get; set; } = string.Empty;
        public DateTime? LastUpdate { get; set; }
        public string LastUpdatedBy { get; set; } = string.Empty;

        public string OcapNo { get; set; } = string.Empty;

        // Set by the BEFORE INSERT trigger OCAP_WIREBOND_WORKWEEK, never by the
        // app - the form shows it read-only.
        public string OcapWorkWeek { get; set; } = string.Empty;

        public string IssuedBy { get; set; } = string.Empty;
        public string Bfg { get; set; } = string.Empty;
        public DateTime? OcapDate { get; set; }
        public string OperatorId { get; set; } = string.Empty;
        public string Process { get; set; } = string.Empty;
        public string Machine { get; set; } = string.Empty;
        public string Package { get; set; } = string.Empty;
        public decimal? SoQty { get; set; }
        public string Defect { get; set; } = string.Empty;
        public string DefectCategory { get; set; } = string.Empty;
        public string DefectOthers { get; set; } = string.Empty;
        public string Diff4M1E { get; set; } = string.Empty;
        public string DiffAffected { get; set; } = string.Empty;
        public string DiffFabSite { get; set; } = string.Empty;
        public string DiffNo { get; set; } = string.Empty;
        public string DiffNotAffected { get; set; } = string.Empty;
        public decimal? DiffRejectQty { get; set; }
        public string DiffRemarks { get; set; } = string.Empty;
        public string VerifiedBy { get; set; } = string.Empty;
        public string ActionTaken { get; set; } = string.Empty;
        public string Disposition { get; set; } = string.Empty;
        public string Remarks { get; set; } = string.Empty;
        public string RcMachineError { get; set; } = string.Empty;
        public string MachineError { get; set; } = string.Empty;
    }

    // The values these columns are keyed in with today. TBLWIREBOND has no check
    // constraints, so these are offered as pick-list hints on the form and the
    // fields still accept anything - a new defect code does not need a code
    // change or a DBA.
    public static class WireBondOptions
    {
        public static readonly string[] Processes = ["WIREBOND"];

        public static readonly string[] DefectCategories =
        [
            "MAN", "MACHINE", "MATERIAL", "METHOD", "ENVIRONMENT", "PROCESS", "OTHERS"
        ];

        // The 4M1E leg the difference was traced to.
        public static readonly string[] Differences =
        [
            "MAN", "MACHINE", "MATERIAL", "METHOD", "ENVIRONMENT"
        ];

        public static readonly string[] Defects =
        [
            "NON STICK ON PAD",
            "NON STICK ON LEAD",
            "BOND LIFT",
            "WIRE SAG",
            "BALL SHEAR LOW",
            "WIRE SHORT",
            "MISSING WIRE",
            "OTHERS"
        ];
    }
}
