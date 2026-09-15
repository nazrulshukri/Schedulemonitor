using System;
using System.Collections.Generic;

namespace EWFM.AwacsMes
{
    /// <summary>
    /// Represents an input for a workorder.
    /// </summary>
    public class Input
    {
        /// <summary>
        /// Gets or sets the input id.
        /// </summary>
        public string Id;

        /// <summary>
        /// Gets or sets the list of outputs for this input.
        /// </summary>
        public List<Output> Outputs;

        /// <summary>
        /// Gets or sets the list of rejects for this input.
        /// </summary>
        public List<Reject> Reject;

        /// <summary>
        /// Initializes a new instance of Input.
        /// </summary>
        public Input()
        {
            this.Outputs = new List<Output>();
            this.Reject = new List<Reject>();
        }
    }
}
