using System;
using System.Collections.Generic;

namespace EWFM.AwacsMes
{
    /// <summary>
    /// Represents an output of a workorder.
    /// </summary>
    public class Output
    {
        /// <summary>
        /// Gets or sets the output id.
        /// </summary>
        public string Id;

        /// <summary>
        /// Gets or sets the output amount.
        /// </summary>
        public int Amount;

        /// <summary>
        /// Gets or sets the id of the first device of this output.
        /// </summary>
        public string FirstDevId;
    }
}
