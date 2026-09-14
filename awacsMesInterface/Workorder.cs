using System;
using System.Collections.Generic;

namespace EWFM.AwacsMes
{
    /// <summary>
    /// Represents an Awacs workorder.
    /// </summary>
    public class Workorder
    {
        /// <summary>
        /// Gets or sets the workorder id.
        /// </summary>
        public string Woid;

        /// <summary>
        /// Gets or sets the workorder state.
        /// </summary>
        public string State;

        /// <summary>
        /// Gets or sets the operator id.
        /// </summary>
       // public string Operator;
        /// <summary>

        /// Gets or sets the Wafer id.
        /// </summary>



        /// <summary>
        /// Gets or sets the latest date when the workorder was updated.comment out by Andrew
        /// </summary>
        //    public DateTime Updated;

        /// <summary>
        /// Gets or sets the workorder state.comment out by Andrew
        /// </summary>
        ///     public WorkorderState State;

        /// <summary>
        /// Gets or sets the list of workorder attributes.
        /// </summary>
        public List<Attribute> Attributes;

        /// <summary>
        /// Gets or sets the list of workorder inputs.
        /// </summary>
        //    public List<Input> Inputs;

        /// <summary>
        /// Initializes a new instance of Workorder. comment out by Andrew
        /// </summary>
        public Workorder()
        {
            //this.State = WorkorderState.IDLE;
            this.Attributes = new List<Attribute>();
            // this.Inputs = new List<Input>();

            //foreach (var attrib in this.Attributes)
            //{
            //    if (attrib.Name == "OPERATOR") { Operator = attrib.Value.ToString(); }
            //    if (attrib.Name == "WAFERID") { Waferid = attrib.Value.ToString(); }
            //}




        }   

    }
}
