using System;
using System.Collections.Generic;
using System.Configuration;
using System.Web.Services;
using System.Web.Services.Protocols;
using System.Xml;
using System.Xml.Serialization;
using System.Data.OracleClient;
using EWFM.AwacsMes.Diagnostics;

namespace EWFM.AwacsMes
{
    /// <summary>
    /// Defines the possible states of a workorder.
    /// </summary>
    public enum WorkorderState
    {
        /// <summary>
        /// The workorder is not being processed.
        /// </summary>
        IDLE = 0,

        /// <summary>
        /// The workorder is being processed.
        /// </summary>
        RUNNING = 1,

        /// <summary>
        /// The workorder is completely processed.
        /// </summary>
        FINISHED = 2
    }
}
