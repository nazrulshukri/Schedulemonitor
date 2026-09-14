namespace Atcbassemblyrecipe.Models
{
    // One row of OCAPSYS.TBLWIREBOND.
    //
    // The table was overhauled to the recipe shape: package, product, leadframe
    // 12NC and recipe, plus the usual TBLROWID / LASTUPDATE / LASTUPDATEDBY
    // bookkeeping. Same columns as AWACSRECIPEBYWSTYPE minus WSTYPE, because
    // every row in this table is wirebond by definition - there is nothing to
    // filter on.
    //
    // Rows are addressed by Oracle ROWID (handed to the page as TblRowId), the
    // way AWACSLF and AWACSRECIPEBYWSTYPE already are. TBLROWID is still filled
    // with RAWTOHEX(SYS_GUID()) on insert for the MES side.
    public class WireBond
    {
        public string TblRowId { get; set; } = string.Empty;
        public DateTime? LastUpdate { get; set; }
        public string LastUpdatedBy { get; set; } = string.Empty;

        // The machine this recipe belongs to: AWACSWSTYPE.WSID. AWACSWSTYPE is
        // the parent - one row per machine - and this is the child's reference
        // back to it, the same way TBLSAWING.SAWMACHINE points at a WSID.
        public string WsId { get; set; } = string.Empty;
        public string Package { get; set; } = string.Empty;
        public string Product { get; set; } = string.Empty;
        public string Leadframe12Nc { get; set; } = string.Empty;
        public string Recipe { get; set; } = string.Empty;

        // True for a wire bonder that is registered in AWACSWSTYPE but has no
        // recipe in TBLWIREBOND yet. The grid shows it as an empty row ready to
        // be filled in; nothing is written to the table until it is saved, so
        // TBLWIREBOND never collects placeholder records.
        public bool IsPlaceholder { get; set; }
    }
}
