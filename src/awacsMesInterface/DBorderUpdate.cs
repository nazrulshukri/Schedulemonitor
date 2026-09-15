using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace EWFM.AwacsMes
{
    /// <summary>
    /// Represents the request and response of a DBorderUpdate.
    /// </summary>
    [XmlRoot(ElementName = "DBorderUpdate")]
    public class DBorderUpdate
    {
        /// <summary>
        /// Gets or sets the workstation id.
        /// </summary>
        public string WsId;

        /// <summary>
        /// Gets or sets the OCRID id.
        /// </summary>
       // public string OCRID;

        /// <summary>
        /// Gets or sets the Operator id.
        /// </summary>
       public string Operator;

        /// <summary>
       /// Gets or sets the Waferid id.
        /// </summary>
        public string Waferid;

        /// <summary>
        /// Gets or sets the State.
        /// </summary>
        public string State;

        /// <summary>
        /// Gets or sets the workorder.
        /// </summary>
        public Workorder Workorder;
      //  public Workorder WorkorderState;

        //public Workorder CARRIERIDS;
        /// <summary>
        /// Gets or sets the CARRIERIDS.
        /// </summary>
        //public string MAT_LDISPID;
        /// <summary>
        /// Gets or sets the workorder.
        /// </summary>
        //public string MAT_RDISPID;
        ///// <summary>
        ///// Gets or sets the workorder.
        ///// </summary>
        //public string MAT_LEPOXYID;
        ///// <summary>
        ///// Gets or sets the workorder.
        ///// </summary>
        //public string MAT_REPOXYID;
        ///// <summary>
        ///// Gets or sets the workorder.
        ///// </summary>
        //public string MAT_BHCOLLID;
        ///// <summary>
        ///// Gets or sets the workorder.
        ///// </summary>
        //public string MAT_EJECTNDID;
        ///// <summary>
        ///// Gets or sets the workorder.
        ///// </summary>
        //public string MAT_SINGUID;
        ///// <summary>
        ///// Gets or sets the workorder.
        ///// </summary>
        //public string MAT_PHEADID;
        ///// <summary>
        ///// Gets or sets the workorder.
        ///// </summary>
        //public string MAT_CLIPFRID;
        ///// <summary>
        ///// Gets or sets the workorder.
        ///// </summary>
        //public string MAT_MAGID;
        ///// <summary>
        ///// Gets or sets the workorder.
        ///// </summary>        




        /// <summary>
        /// Initializes a new instance of DBorderUpdate.
        /// </summary>
        public DBorderUpdate()
        {
            this.Workorder = new Workorder();
                    
           
        }
    }
}
