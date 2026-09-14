using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace EWFM.AwacsMes
{
    /// <summary>
    /// Represents the request and response of Markingcode
    /// </summary>
    [XmlRoot(ElementName = "StripMarking")]
    public class StripMarking
    {
        public string Wsid;
        /// <summary>
        /// this Workorder has information woid used to get marking code from MES
        /// </summary>
        public Workorder Workorder;
        /// <summary>
        /// Initialize a new instance of StringMarking
        /// </summary>
        public void update()
        {
            this.Workorder = new Workorder();
        }

    }
}
