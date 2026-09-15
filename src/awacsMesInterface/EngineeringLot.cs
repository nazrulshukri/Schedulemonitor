using System;

namespace EWFM.AwacsMes
{
    /// <summary>
    /// One row of the OCAP ENGINEERING table, looked up by lot number.
    ///
    /// An engineering lot is not in MES, so the usual RMS/MES lookup
    /// (getFAMESInfo / GetLotDetailsFromRms) answers nothing for it. Everything
    /// the workstation needs - package, product and the recipe for the step it
    /// is about to run - is keyed in on the Engineering page of the ATCB
    /// assembly recipe app instead, and read back from here.
    /// </summary>
    public class EngineeringLot
    {
        /// <summary>
        /// ENGINEERING.LOTNUMBER, as stored. This is what the WOID was matched
        /// against.
        /// </summary>
        public string lotnumber;

        /// <summary>
        /// ENGINEERING.REQUESTOR - who asked for the lot. Not sent to the
        /// workstation; useful in the trace when a lookup goes wrong.
        /// </summary>
        public string requestor;

        /// <summary>
        /// ENGINEERING."PACKAGE".
        /// </summary>
        public string package;

        /// <summary>
        /// ENGINEERING.PRODUCT.
        /// </summary>
        public string product;

        /// <summary>
        /// The value of the one recipe column that matches the workstation's
        /// WSTYPE. Empty when the lot does not run that step.
        /// </summary>
        public string recipe;

        /// <summary>
        /// The name of the column <see cref="recipe"/> was read from, e.g.
        /// RECIPES1. Reported in the RESULT attribute when the cell is empty,
        /// so the message says which cell to fill in.
        /// </summary>
        public string recipeColumn;
    }
}
