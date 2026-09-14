//------------------------------------------------------------------------------
// <copyright file="Service.asmx.cs" company="NXP Semiconductors">
// Copyright (C) NXP Semiconductors 2013. All rights reserved.
// </copyright>
// <project>Awacs MES interface.</project>
// <author>Marc Klerx</author>
// <email>marc.klerx@nxp.com</email>
// <date>2013-11-14</date>
// <summary>Service to exchange diebond workorder information between Awacs and MES.</summary>
//
// $URL: $
// $Rev: $
// $Author: marc $
// $Date: 2013-11-22 15:31:44 +0700 (Fri, 22 Nov 2013) $
//
//------------------------------------------------------------------------------
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
    /// Represents a reject of a workorder.
    /// </summary>
    public class Reject
    {
        /// <summary>
        /// Gets or sets the reject category.
        /// </summary>
        public string Category;

        /// <summary>
        /// Gets or sets the reject amount.
        /// </summary>
        public int Amount;
    }
}
