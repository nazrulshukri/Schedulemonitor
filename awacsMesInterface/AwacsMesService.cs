using System;
using System.Collections.Generic;
using System.Configuration;
using System.Web.Services;
using System.Web.Services.Protocols;
using System.Xml;
using System.Xml.Serialization;
using Oracle.ManagedDataAccess.Client;
using System.Data.SqlClient;
using EWFM.AwacsMes.Diagnostics;
using System.Data;
using System.Text.RegularExpressions;
using System.Linq;
using Microsoft.VisualBasic;
using System.Linq;
using System.Xml.Linq;



namespace EWFM.AwacsMes
{
    /// <summary>
    /// Provides a service to exchange diebond workorder information between Awacs and MES.
    /// </summary>
    [WebService(Namespace = AwacsMesService.NameSpaceURI)]
    [WebServiceBinding(ConformsTo = WsiProfiles.BasicProfile1_1)]
    [System.ComponentModel.ToolboxItem(false)]
    public class AwacsMesService : System.Web.Services.WebService
    {
        const string NameSpaceURI = "http://mes.awacs.nxp.com/";

        /// <summary>
        /// A WOID starting with this is an engineering lot: it is not in MES and
        /// its recipes come from the OCAP ENGINEERING table instead. See
        /// dbOrderUpdateByEngineering.
        /// </summary>
        const string EngineeringWoPrefix = "ENG";

        FAMESInfo p_MesData;

        /// <summary>
        /// Initializes the static AwacsMesService.
        /// </summary>
        static AwacsMesService()
        {
            TraceLog.Initialize();
        }

        /// <summary>
        /// Gets the version of the AwacsMesService.
        /// </summary>
        /// <returns>The version string.</returns>
        [WebMethod, SoapDocumentMethod(ParameterStyle = SoapParameterStyle.Bare)]
        [return: XmlElement("Version")]
        public string GetVersion()
        {
            string result = string.Empty;
            using (TraceLog traceLog = TraceLog.Create("AwacsMesService.GetVersion"))
            {
                traceLog.EnterMethod();
                try
                {
                    System.Reflection.Assembly assembly = System.Reflection.Assembly.GetExecutingAssembly();
                    result = "Version " + assembly.GetName().Version.ToString();
                }
                catch (Exception ex)
                {
                    traceLog.LogException(ex);
                }
                traceLog.ExitMethod((object)result);
            }
            return result;
        }

        [WebMethod]
        public string TestWO(string strWOID, string strWSID)
        {
            DBorderUpdate dbOrderUpdate1 = new DBorderUpdate();
            dbOrderUpdate1.WsId = strWSID;
            dbOrderUpdate1.Workorder.Woid = strWOID;
            dbOrderUpdate1.Workorder.State = "idle";
            DBorderUpdate DBorderUpdateResult =DBorderUpdate(dbOrderUpdate1);
            return "1-TBD";
        }

        /// <summary>
        /// Exchanges the diebond workorder information between Awacs and MES.
        /// </summary>
        /// <param name="dbOrderUpdate">The DBorderUpdate object from Awacs.</param>
        /// <returns>The DBorderUpdate object updated by MES.</returns>
        [WebMethod, SoapDocumentMethod(ParameterStyle = SoapParameterStyle.Bare)]
        public DBorderUpdate DBorderUpdate(DBorderUpdate dbOrderUpdate)
        {

           
            using (TraceLog traceLog = TraceLog.Create("AwacsMesService.DBorderUpdate"))
            {
                traceLog.EnterMethod(dbOrderUpdate);
                string state = dbOrderUpdate.Workorder.State.ToString();
                if (dbOrderUpdate.Workorder.Woid.ToString().Length < 1)
                { 
                    return dbOrderUpdate; 
                }
                
                //string woid = GetRealWO(dbOrderUpdate.Workorder.Woid,dbOrderUpdate.WsId); // Derrick
                string woid = dbOrderUpdate.Workorder.Woid.ToString();

                string wstype = getWSType(dbOrderUpdate.WsId);
                string WsID = dbOrderUpdate.WsId;

                // Engineering lots. A WOID that starts with ENG is not an MES
                // workorder at all: it is keyed in on the Engineering page of the
                // ATCB assembly recipe app and lives in the OCAP ENGINEERING
                // table. getFAMESInfo and GetLotDetailsFromRms below both answer
                // nothing for it, which used to leave every ENG lot with
                // "WSID:... not exist in recipe!" whatever the workstation asked
                // for. Handled here instead, before those two calls, so an
                // engineering lot costs one Oracle lookup rather than two web
                // service round trips that cannot succeed.
                if (IsEngineeringWorkorder(woid))
                {
                    // Its own try/catch because this returns before reaching the
                    // one below: an unreadable ENGINEERING table has to come back
                    // as a RESULT attribute the workstation can display, the same
                    // as every other failure here, not as a SOAP fault.
                    try
                    {
                        dbOrderUpdateByEngineering(traceLog, dbOrderUpdate, woid, WsID, wstype);
                    }
                    catch (Exception ex)
                    {
                        traceLog.LogException(ex);
                        dbOrderUpdate.Workorder.Attributes.Add(new Attribute("RESULT", ex.Message));
                    }

                    traceLog.ExitMethod(dbOrderUpdate);
                    return dbOrderUpdate;
                }

                string Operatorid = "";
                string Waferid= "";
                string OCRid = "none";

                // Modified by Derrick 2018-01-12  -->
                //string packageName = getPackageName(woid);
                //string ProductName = getProductName(woid);
                string packageName = "";
                string ProductName = "";
                string Crystal12NC = "";
                string DeviceName =  "";

             

                p_MesData = getFAMESInfo(woid);
                if (p_MesData != null)
                {
                    packageName = p_MesData.package;
                    ProductName = p_MesData.product;
                    Crystal12NC = p_MesData.Crystal12NC;
                }
                // <-- end of modified code

                GetLotDetailsFromRms(woid, out DeviceName, out Crystal12NC);
                ProductName = DeviceName;

                try
                {
                    //if (WorkorderVerify(woid) || wstype.ToString().Length > 0) //|| packageName.StartsWith("SOD882") //== "2DMARKER"  || wstype == "TRIMFORM" || wstype == "MOULD" || wstype == "DIEBOND")
                    if (p_MesData != null && wstype.ToString().Length > 0)
                    {
                        switch (wstype)
                        {
                            // Case MARKER added by Derrick
                            case "MARKER":
                                dbOrderUpdateByMES(traceLog, dbOrderUpdate, woid, WsID);
                                break;
                            case "SAWING":

                                    string strLF = Crystal12NC;
                                    string strProduct = ProductName;
                                    string strDevice = DeviceName;
                                    string strRecipe = getSawingRecipeNameFA(wstype, strProduct, Crystal12NC);

                                   
                                    if (string.IsNullOrEmpty(strLF) || string.IsNullOrEmpty(strProduct) || string.IsNullOrEmpty(strRecipe))
                                    {
                                        dbOrderUpdate.Workorder.Attributes.Add(new Attribute("RESULT", string.Format("WSID:{0} not exist in recipe!", dbOrderUpdate.WsId)));
                                    }
                                    else
                                    {
                                            setWorkOrderAttribute(dbOrderUpdate, "DEVICE", strDevice);
                                            setWorkOrderAttribute(dbOrderUpdate, "RECIPE", strRecipe);
                                            setWorkOrderAttribute(dbOrderUpdate, "LEADFRAME12NC", strLF);
                                            setWorkOrderAttribute(dbOrderUpdate, "PRODUCT", strProduct);
                                    }
                                    
                                    break;

                            case "WAOI":
                                {
                                     strLF = Crystal12NC;
                                    strProduct = ProductName;
                                    strDevice = DeviceName;
                                    strRecipe = getSawingRecipeNameFA(wstype, strProduct, strLF);

                                    if (string.IsNullOrEmpty(strLF) || string.IsNullOrEmpty(strProduct) || string.IsNullOrEmpty(strRecipe))
                                    {
                                        dbOrderUpdate.Workorder.Attributes.Add(new Attribute("RESULT",
                                            string.Format("WSID:{0} not exist in recipe!", dbOrderUpdate.WsId)));
                                    }
                                    else
                                    {
                                        setWorkOrderAttribute(dbOrderUpdate, "DEVICE", strDevice);
                                        setWorkOrderAttribute(dbOrderUpdate, "RECIPE", strRecipe);
                                        setWorkOrderAttribute(dbOrderUpdate, "LEADFRAME12NC", strLF);
                                        setWorkOrderAttribute(dbOrderUpdate, "PRODUCT", strProduct);
                                    }
                                    break;
                                }

                            case "2DMARKER":
                                // if (string.IsNullOrEmpty(dbOrderUpdate.Workorder.Woid))   
                                try
                                {
                                    var validInput = woid.Contains("-");
                                    if (validInput == true)
                                    {
                                        string[] Pkg = woid.Split('-');
                                        var NC12 = Pkg[0].Substring(0, 6);
                                        var NC12of = Pkg[1];   //.Substring(Pkg[0].Length - 1);

                                        string SO = Regex.Replace(Pkg[0], "[^0-9]", "");
                                        // int numValue;
                                        // bool parsed = Int32.TryParse(SO, out numValue);
                                        var LastDigit = Int32.Parse(SO.Substring(SO.Length - 1));
                                        var first2Char = Pkg[0].Substring(0, 2);

                                        //   if ((NC12 == "GG0000") || (NC12 == "G0000" ))

                                        if ((first2Char == "2D") && (LastDigit <= 2))
                                        { dbOrderUpdateByNonMES(traceLog, dbOrderUpdate, woid); }
                                        else { dbOrderUpdateByMES(traceLog, dbOrderUpdate, woid, WsID); }
                                        //  String NC12 = woid;
                                    }

                                }
                                catch (Exception ex)
                                { }


                                break;
                            case "TRIMFORM":
                                dbOrderUpdateByMES(traceLog, dbOrderUpdate, woid, WsID); //stripMarkingUpdate(traceLog, dbOrderUpdate);
                                break;
                            case "MOULD":
                                dbOrderUpdateByMES(traceLog, dbOrderUpdate, woid, WsID);
                                break;
                            case "DIEBOND":
                                dbOrderUpdateByMES(traceLog, dbOrderUpdate, woid, WsID); //stripMarkingUpdate(traceLog, dbOrderUpdate);
                                break;
                            case "ADAT":

                                string[] so = woid.Split('-');
                                string soid = so[0].ToString();
                                string FlagData = FlagVerify(soid);    //To validate Bosch Product
                                string FlagDataBimLine = FlagVerifyBimLine(soid);
                                string Bim = WsID.Substring(2, WsID.Length - 3);
                                var Fab = GetFab(woid);
                                string bs = "";
                                string bsFWM = "";
                                string myWaferBatch = "";
                                string MyOCRID = "";
                                List<string> MyOCRIDList = new List<string>();
                                string TATC = "";

                                if (Fab[0].ToString().Contains("DHAM") == true)
                                {
                                    //W-W7XC61-038  W7XC6W38-A3
                                    //Waferid = "W-" + Fab[1].Substring(1, 5);
                                    //Waferslice = 
                                    //    //WaferAttr.Add(so + "," + wfr + "," + xtal + "," + fab);                                        
                                    //    "Word1 Text Word2".ContainsAll("Word1", "Word2"); // true
                                    //var MyVal = Waferid.Split('-');
                                    //bool matchFound = myList.Any(s => s.Contains("Mdd LH"));
                                    for (int i = 0; i < Fab.Count; i++)
                                    {
                                        string[] myWaferInfo = Fab[i].ToString().Split(',');
                                        string[] myWaferID = myWaferInfo[1].ToString().Split('-');

                                        myWaferBatch = myWaferID[1].Substring(0, 5) + "W" + myWaferID[2].Substring(1, 2) + "-";

                                        MyOCRID = GenerateCheckChar(myWaferBatch);
                                        MyOCRIDList.Add(myWaferInfo[1].ToString() + "," + MyOCRID + "," + myWaferInfo[2].ToString());

                                    }
                                }

                                string[] myData;
                                for (int i = 0; i < MyOCRIDList.Count; i++)
                                {
                                    myData = MyOCRIDList[i].ToString().Split(',');
                                    if (myData[1].ToString() == Waferid)
                                    {
                                        Waferid = myData[0].ToString();  //lotfi waferid
                                        OCRid = myData[1].ToString();  //lotfi waferid
                                        if (state == "RUNNING" || state == "IDLE")
                                        {
                                            TATC = myData[2].ToString();
                                        }
                                        //  TATC = myData[2].ToString();  //WaferVerifyTATC(Waferid, woid);
                                    }


                                }
                                //IEnumerable<int> allIndices = MyOCRIDList.Select((s, i) => new { Str = s, Index = i })
                                //    .Where(x => x.Str == Waferid)
                                //    .Select(x => x.Index);

                                //foreach(int matchingIndex in allIndices)
                                //{
                                //    string[] LookupID = MyOCRIDList[matchingIndex].ToString().Split(',');
                                //    Waferid = LookupID[1].ToString();

                                //}

                                //if (MyOCRIDList.Contains(Waferid) == true) 
                                //{


                                //}
                                if (!string.IsNullOrEmpty(Waferid) && !string.IsNullOrEmpty(TATC))// && Fab[0].ToString().Contains("DHAM"))
                                {

                                    try
                                    {
                                        //    if (STATE == "RUNNING")
                                        //    {
                                        // lotfi  remove state
                                        if ((TATC.Substring(0, 2) == "TC") || (TATC.Substring(0, 2) == "TA"))
                                        {
                                            if (WsID.Substring(0, WsID.Length - 1) == "AD45C")
                                            {
                                                if (TATC.Substring(0, 2) == "TA")
                                                {
                                                    if ((WsID.Substring(WsID.Length - 1, 1) == "1") || (WsID.Substring(WsID.Length - 1, 1) == "2"))
                                                    { }
                                                    else
                                                    {
                                                        dbOrderUpdate.Workorder.Attributes.Add(new Attribute("RESULT", string.Format("TATC Wafer:{0} is not belong to WSID:{1}", dbOrderUpdate.Waferid, WsID)));
                                                    }
                                                }
                                                if (TATC.Substring(0, 2) == "TC")
                                                {
                                                    if ((WsID.Substring(WsID.Length - 1, 1) == "3") || (WsID.Substring(WsID.Length - 1, 1) == "4"))
                                                    { }
                                                    else
                                                    {
                                                        dbOrderUpdate.Workorder.Attributes.Add(new Attribute("RESULT", string.Format("TATC Wafer:{0} is not belong to WSID:{1}", dbOrderUpdate.Waferid, WsID)));

                                                    }
                                                }

                                            }

                                            else
                                            {
                                                if (TATC.Substring(0, 2) == "TA")
                                                {
                                                    if ((WsID.Substring(WsID.Length - 1, 1) == "1") || (WsID.Substring(WsID.Length - 1, 1) == "3"))
                                                    { }
                                                    else
                                                    {
                                                        dbOrderUpdate.Workorder.Attributes.Add(new Attribute("RESULT", string.Format("TATC Wafer:{0} is not belong to WSID:{1}", dbOrderUpdate.Waferid, WsID)));
                                                    }
                                                }
                                                if (TATC.Substring(0, 2) == "TC")
                                                {
                                                    if ((WsID.Substring(WsID.Length - 1, 1) == "2") || (WsID.Substring(WsID.Length - 1, 1) == "4"))
                                                    { }
                                                    else
                                                    {
                                                        dbOrderUpdate.Workorder.Attributes.Add(new Attribute("RESULT", string.Format("TATC Wafer:{0} is not belong to WSID:{1}", dbOrderUpdate.Waferid, WsID)));

                                                    }
                                                }

                                                //      }
                                            }
                                        }
                                    }
                                    catch
                                    { }
                                } //tatc end


                                if (FlagData.ToUpper() == "BOSCH")
                                {

                                    //To verify cerified operator
                                    if (OperatorVerify(Operatorid) != "INVALID")
                                    {
                                        if (FlagDataBimLine.Contains(Bim) == true)
                                        {
                                            dbOrderUpdateByMESADAT(traceLog, dbOrderUpdate, woid, WsID, Waferid, OCRid); //stripMarkingUpdate(traceLog, dbOrderUpdate);

                                        }
                                        else
                                        {

                                            if (string.IsNullOrEmpty(FlagDataBimLine.ToString()))
                                            {
                                                dbOrderUpdate.Workorder.Attributes.Add(new Attribute("RESULT", string.Format("SOID {0} is wrongly assigned in MES to non Bosch line. Check MES again ", soid)));
                                            }
                                            else
                                            {
                                                dbOrderUpdate.Workorder.Attributes.Add(new Attribute("RESULT", string.Format("Machine {1} is NOT for MES Schedule {0} for Bosch. Remove and use correct machine", FlagDataBimLine.ToString(), WsID)));
                                            }
                                        }
                                    }
                                    else
                                    {
                                        dbOrderUpdate.Workorder.Attributes.Add(new Attribute("RESULT", string.Format("Badge:{0} not is not certified!", Operatorid)));
                                    }

                                }

                                else
                                {
                                    dbOrderUpdateByMESADAT(traceLog, dbOrderUpdate, woid, WsID, Waferid, OCRid); //stripMarkingUpdate(traceLog, dbOrderUpdate);                                                                
                                }

                                //string [] Mat =  { "NEEDLE", "COLLET", "GLUE", "CARRIERIDS" };
                                //if (Mat.Contains(MaterialID)== true) {
                                //if (MaterialVerify(MaterialID, WsID) == true)
                                //{}
                                //}

                                //  dbOrderUpdateByMESADAT(traceLog, dbOrderUpdate, woid, WsID, WAFERID, machineState); //stripMarkingUpdate(traceLog, dbOrderUpdate);
                                // lotfi-20161026    dbOrderUpdateByMESADAT(traceLog, dbOrderUpdate, woid, WsID, Waferid); //stripMarkingUpdate(traceLog, dbOrderUpdate);

                                //OperatorVerify(operatorid);
                                //if (machineState == "RUNNING" && WorkorderVerify(woid) == false)
                                //{
                                //    dbOrderUpdate.Workorder.Attributes.Add(new Attribute("RESULT", string.Format("WOID:{0} not exist in MES!", dbOrderUpdate.Workorder.Woid)));
                                //}
                                //else
                                //    if (machineState == "RUNNING" && WaferVerifyOCRID(WAFERID, woid) == false)
                                //    {
                                //        dbOrderUpdate.Workorder.Attributes.Add(new Attribute("RESULT", string.Format("Wafer {0} not belong to {1}", WAFERID, woid)));
                                //    }

                                if (WorkorderVerify(woid) == false)
                                {
                                    dbOrderUpdate.Workorder.Attributes.Add(new Attribute("RESULT", string.Format("WOID:{0} not exist in MES!", dbOrderUpdate.Workorder.Woid)));
                                }

                                //lotfi remove state
                                //if (STATE == "RUNNING")
                                //{
                                //    if (!string.IsNullOrEmpty(dbOrderUpdate.Waferid) && WaferVerifyOCRID(Waferid, woid) == false && Waferid != "")
                                //    {
                                //        //lotfi add
                                //        dbOrderUpdate.Workorder.Attributes.Add(new Attribute("RESULT", string.Format("Wafer {0} not belong to {1}", Waferid, woid)));
                                //    }
                                //}

                                break;
                            case "ASMWB":
                                dbOrderUpdateByMES(traceLog, dbOrderUpdate, woid, WsID); //stripMarkingUpdate(traceLog, dbOrderUpdate);
                                break;
                            case "ASMWBM":
                                dbOrderUpdateByMES(traceLog, dbOrderUpdate, woid, WsID); //stripMarkingUpdate(traceLog, dbOrderUpdate);
                                break;
                            case "ASMWBD":
                                dbOrderUpdateByMES(traceLog, dbOrderUpdate, woid, WsID); //stripMarkingUpdate(traceLog, dbOrderUpdate);
                                break;
                            case "PLATING":
                                dbOrderUpdateByMES(traceLog, dbOrderUpdate, woid, WsID); //stripMarkingUpdate(traceLog, dbOrderUpdate);
                                break;
                            default:
                                dbOrderUpdate.Workorder.Attributes.Add(new Attribute("RESULT", string.Format("WSID:{0} not exist in recipe!", dbOrderUpdate.WsId)));
                                break;
                        }
                        //if (WorkorderVerify(woid) == false)
                        //{
                        //    dbOrderUpdate.Workorder.Attributes.Add(new Attribute("RESULT", string.Format("WOID:{0} not exist in MES!", dbOrderUpdate.Workorder.Woid)));
                        //}
                        //if (wstype == "2DMARKER")
                        //{

                        //    // if (getServiceName(packageName).Trim().ToUpper() == "FP" || wstype == "2DMARKER")
                        //    //   if (wstype == "2DMARKER")

                        //    // dbOrderUpdateB  yDP(traceLog, dbOrderUpdate);
                        //    dbOrderUpdateByMES(traceLog, dbOrderUpdate, woid);
                        //}
                        //if (wstype == "Trimform")
                        //{
                        //    stripMarkingUpdate(traceLog, dbOrderUpdate);
                        //}
                        //if (wstype == "MOULD")
                        //{
                        //    stripMarkingUpdate(traceLog, dbOrderUpdate);
                        //}
                        //if (wstype == "Diebond")
                        //{
                        //    stripMarkingUpdate(traceLog, dbOrderUpdate);
                        //}
                        //else
                        //{
                        //    dbOrderUpdate.Workorder.Attributes.Add(new Attribute("RESULT", string.Format("WOID:{0} not exist in MES!", dbOrderUpdate.Workorder.Woid)));
                        //}
                    }
                    else
                    {
                        dbOrderUpdate.Workorder.Attributes.Add(new Attribute("RESULT", string.Format("WSID:{0} not exist in recipe!", dbOrderUpdate.WsId)));
                    }
                }
                catch (Exception ex)
                {
                    traceLog.LogException(ex);
                   // dbOrderUpdate.Workorder.Attributes.Clear();
                    dbOrderUpdate.Workorder.Attributes.Add(new Attribute("RESULT", ex.Message));
                }
                traceLog.ExitMethod(dbOrderUpdate);
            }
            return dbOrderUpdate;
        }


        [WebMethod]
        public string RecipeName(string Wsid, string package, string productdecription, string lf12nc, string wsid, string woid)
        {
            var result = getRecipeName(Wsid, package, productdecription, lf12nc, wsid, woid);
            return result;
        }

        [WebMethod]
        public string PackageName(string woid)
        {
            return getPackageName(woid);        
        }
        [WebMethod]
        public bool WOVerify(string woid)
        {
            return WorkorderVerify(woid);
        }
        [WebMethod]
        public string WAFER(string waferid,string woid)
        {
            return WaferVerify(waferid, woid);
        }

        [WebMethod]
        public string RecipeNameFA(string WsID, string woid)
        {
            FAMESInfo val = getFAMESInfo(woid);
            return getRecipeNameFA(WsID, val.package, val.product, val.nc12, val.woid);
        }


        /// <summary>
        /// Answers the engineering recipe a workstation would be given for an ENG
        /// lot, without running a full DBorderUpdate. Same lookup the service
        /// performs for real - use it from the .asmx test page to check a lot that
        /// was just keyed in on the Engineering page.
        /// </summary>
        /// <param name="wsid">The workstation, e.g. 2OIF-002.</param>
        /// <param name="woid">The engineering lot number, e.g. ENGXTA54780B.</param>
        [WebMethod]
        public string EngineeringRecipe(string wsid, string woid)
        {
            if (!IsEngineeringWorkorder(woid))
            {
                return string.Format("WOID:{0} is not an engineering lot - it does not start with {1}.", woid, EngineeringWoPrefix);
            }

            string wstype = getWSType(wsid);
            string column = getEngineeringRecipeColumn(wstype);

            if (string.IsNullOrEmpty(column))
            {
                return string.Format("WSTYPE:{0} (WSID:{1}) has no ENGINEERING column mapped in ENGINEERINGWSTYPE.", wstype, wsid);
            }

            EngineeringLot lot = getEngineeringLot(woid, column);

            if (lot == null)
            {
                return string.Format("Lot {0} is not in the ENGINEERING table.", woid);
            }

            return string.Format(
                "WSTYPE={0}; COLUMN={1}; RECIPE={2}; PACKAGE={3}; PRODUCT={4}",
                wstype, column, lot.recipe, lot.package, lot.product);
        }

        /// <summary>
        /// For StripMarkig to get markigcode
        /// </summary>
        /// <param name="stripMarking"></param>
        /// <returns></returns>
        [WebMethod, SoapDocumentMethod(ParameterStyle = SoapParameterStyle.Bare)]
        public StripMarking MarkingCode(StripMarking stripMarking)
        {
            using (TraceLog traceLog = TraceLog.Create("AwacsMesService.MarkingCode"))
            {
                traceLog.EnterMethod(stripMarking);
                try 
                {
                    stripMarkingUpdate(traceLog, stripMarking);
                }
                catch(Exception ex) 
                {
                    traceLog.LogException(ex);
                    stripMarking.Workorder.Attributes.Clear();
                    stripMarking.Workorder.Attributes.Add(new Attribute("Result",ex.Message));

                }
                traceLog.ExitMethod(stripMarking);
                
                return stripMarking; 
            
            }          
        }

        private static void stripMarkingUpdate(TraceLog tracelog, StripMarking stripMarking)
        {
            //change stripMarking.Workorder.Woid = StripMark : Lotfi
            String StripMark = stripMarking.Workorder.Woid;

            if (StripMark.Substring(0, 1) == "%") { StripMark = StripMark.Substring(2, StripMark.Length - 2); }
            if (StripMark.Substring(0, 1) == "A") { StripMark = StripMark.Substring(1, StripMark.Length - 1); }

            if (string.IsNullOrEmpty(StripMark))
            {
                throw new ArgumentException("No workorder id specified!");
            }
            else
            {
                tracelog.EnterMethod(new string[]{"workOrderId"},StripMark);
                using(OracleConnection mesDbConn = new OracleConnection(ConfigurationManager.ConnectionStrings["MES"].ConnectionString))
                {
                    mesDbConn.Open();
                    using(OracleCommand cmd = mesDbConn.CreateCommand())
                    {
                        cmd.CommandText = string.Format(@"SELECT FOS.ACNAME,
                                                                PK.MES_PACKAGENAME PACKAGE,
                                                                FOS.MARKINGCODE MARKINGCODE
                                                             FROM VW_BEM_FGORDERLINES_SOREQ FOS, PACKAGE PK, PRODUCTFAMILY PF
                                                             WHERE PK.MES_PACKAGEID = PF.MES_PACKAGEID
                                                                AND FOS.PRODUCTFAMILYNAME = PF.PRODUCTFAMILYNAME
                                                                AND ACName = '{0}'", StripMark);
                       using (OracleDataReader reader = cmd.ExecuteReader())
                       {
                           while (reader.Read())
                           {
                               setWorkOrderAttribute(stripMarking,"Top_Line_1",reader["MarkingCode"].ToString());
                               setWorkOrderAttribute(stripMarking, "Top_Line_2", null);
                               setWorkOrderAttribute(stripMarking, "Top_Line_3", null);
                               setWorkOrderAttribute(stripMarking, "Top_Line_4", null);
                               setWorkOrderAttribute(stripMarking, "Top_Line_5", null);
                               setWorkOrderAttribute(stripMarking, "Top_Line_6", null);
                               setWorkOrderAttribute(stripMarking, "Top_Line_7", null);
                               setWorkOrderAttribute(stripMarking, "Top_Line_8", null);
                               setWorkOrderAttribute(stripMarking, "Top_Line_9", null);
                               setWorkOrderAttribute(stripMarking, "Top_Line_10", null);
                               setWorkOrderAttribute(stripMarking, "RECIPE", reader["PACKAGE"].ToString());
                           }
                       }

                    }
                    mesDbConn.Close();
                
                }
            }
        
        
        }


        private static void stripMarkingUpdate(TraceLog tracelog, DBorderUpdate stripMarking)
        {
            //change stripMarking.Workorder.Woid = StripMark : Lotfi
            String StripMark = stripMarking.Workorder.Woid;

            if (StripMark.Substring(0, 1) == "%") { StripMark = StripMark.Substring(2, StripMark.Length - 2); }
            if (StripMark.Substring(0, 1) == "A") { StripMark = StripMark.Substring(1, StripMark.Length - 1); }

            if (string.IsNullOrEmpty(StripMark))
            {
                throw new ArgumentException("No workorder id specified!");
            }
            else
            {
                tracelog.EnterMethod(new string[] { "workOrderId" }, StripMark);
                using (OracleConnection mesDbConn = new OracleConnection(ConfigurationManager.ConnectionStrings["MES"].ConnectionString))
                {
                    mesDbConn.Open();
                    using (OracleCommand cmd = mesDbConn.CreateCommand())
                    {
                        cmd.CommandText = string.Format(@"SELECT FOS.ACNAME,
                                                                PK.MES_PACKAGENAME PACKAGE,
                                                                FOS.MARKINGCODE MARKINGCODE
                                                             FROM VW_BEM_FGORDERLINES_SOREQ FOS, PACKAGE PK, PRODUCTFAMILY PF
                                                             WHERE PK.MES_PACKAGEID = PF.MES_PACKAGEID
                                                                AND FOS.PRODUCTFAMILYNAME = PF.PRODUCTFAMILYNAME
                                                                AND ACName = '{0}'", StripMark);
                        using (OracleDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                setWorkOrderAttribute(stripMarking, "Top_Line_1", reader["MarkingCode"].ToString());
                                setWorkOrderAttribute(stripMarking, "Top_Line_2", "");
                                setWorkOrderAttribute(stripMarking, "Top_Line_3", "");
                                setWorkOrderAttribute(stripMarking, "Top_Line_4", "");
                                setWorkOrderAttribute(stripMarking, "Top_Line_5", "");
                                setWorkOrderAttribute(stripMarking, "Top_Line_6", "");
                                setWorkOrderAttribute(stripMarking, "Top_Line_7", "");
                                setWorkOrderAttribute(stripMarking, "Top_Line_8", "");
                                setWorkOrderAttribute(stripMarking, "Top_Line_9", "");
                                setWorkOrderAttribute(stripMarking, "Top_Line_10", "");
                                //setWorkOrderAttribute(stripMarking, "Top_Line_2".ToUpper(), "");
                                //setWorkOrderAttribute(stripMarking, "Top_Line_3".ToUpper(), "");
                                //setWorkOrderAttribute(stripMarking, "Top_Line_4".ToUpper(), "");
                                //setWorkOrderAttribute(stripMarking, "Top_Line_5".ToUpper(), "");
                                //setWorkOrderAttribute(stripMarking, "Top_Line_6".ToUpper(), "");
                                //setWorkOrderAttribute(stripMarking, "Top_Line_7".ToUpper(), "");
                                //setWorkOrderAttribute(stripMarking, "Top_Line_8".ToUpper(), "");
                                //setWorkOrderAttribute(stripMarking, "Top_Line_9".ToUpper(), "");
                                //setWorkOrderAttribute(stripMarking, "Top_Line_10".ToUpper(), "");
                                setWorkOrderAttribute(stripMarking, "RECIPE", reader["PACKAGE"].ToString());
                            }
                        }

                    }
                    mesDbConn.Close();

                }
            }


        }

        private static void dbOrderUpdateByMES(TraceLog tracelog, DBorderUpdate dbOrderUpdate, string woid, string Wsid)
        {
            // if (string.IsNullOrEmpty(dbOrderUpdate.Workorder.Woid))
            if (string.IsNullOrEmpty(woid))
            {
                throw new ArgumentException("No workorder ID specified.");
            }
            else
            {
                tracelog.EnterMethod(new string[] { "workOrderId" }, dbOrderUpdate.Workorder.Woid);
                string wstype = getWSType(wsid: dbOrderUpdate.WsId);

                // Removed by Derrick 2018-02-05 -- not suitable for modular line
                //List<string> attributes = null; // getMaterialList(Wsid);
                FAMESInfo mesInfo = null;
                if (woid != string.Empty)
                {
                    mesInfo = getFAMESInfo(woid);
                }
                //string materialID = dbOrderUpdate.MAT_LDISPID.ToString();

                // Added by Derrick 2018-02-05
                if (wstype == "MARKER")
                {
                    string lfSize = string.Empty;
                    string requested = string.Empty;

                    List<string> lfAttr = getLFAttribute(mesInfo.nc12, mesInfo.package, mesInfo.product);
                    if (lfAttr.Count == 2)
                    {
                        lfSize = lfAttr[0];
                        int defaultqty = Convert.ToInt32(lfAttr[1]);
                        requested = lfAttr[1];
                        if (!string.IsNullOrEmpty(lfSize))
                        {
                            setWorkOrderAttribute(dbOrderUpdate, "LFSIZE", lfSize);
                            setWorkOrderAttribute(dbOrderUpdate, "REQUESTED", requested);
                            setWorkOrderAttribute(dbOrderUpdate, "PACKAGE", mesInfo.package);
                        }
                    }

                    string recipename = getRecipeNameFA(wstype, mesInfo.package, mesInfo.product, mesInfo.nc12, woid);
                    if (recipename.Length > 1)
                    {
                        setWorkOrderAttribute(dbOrderUpdate, "RECIPE", recipename);
                    }

                    //setWorkOrderAttribute(dbOrderUpdate, "LFSIZE", lfSize);
                    //setWorkOrderAttribute(dbOrderUpdate, "REQUESTED", requested);

                    setWorkOrderAttribute(dbOrderUpdate, "PACKAGE", getFAPackageName(mesInfo.product, mesInfo.package));

                }

                if (wstype == "TRIMFORM")
                {

                    string lfSize = "30,6";
                    string requested = "4000";

                    string recipename = getRecipeNameFA(wstype, mesInfo.package, mesInfo.product, mesInfo.nc12, woid);
                    if (recipename.Length > 1)
                    {
                        setWorkOrderAttribute(dbOrderUpdate, "RECIPE", recipename);
                    }

                    setWorkOrderAttribute(dbOrderUpdate, "LFSIZE", lfSize);
                    setWorkOrderAttribute(dbOrderUpdate, "REQUESTED", requested);

                    setWorkOrderAttribute(dbOrderUpdate, "PACKAGE", getFAPackageName(mesInfo.product, mesInfo.package));
                }
                /*
                string materialName = "MAT_LDISPID";
                var materialInfo = getMaterialInfo(woid, mesInfo[0], Wsid, materialName, materialID);  // lotfi for material check
                setWorkOrderAttribute(dbOrderUpdate, materialName, materialInfo.ToString());
                string Material = "";
                string LF = "";
                if (mesInfo != null)
                {

                    foreach (string name in attributes)
                    {
                        string value = string.Empty;
                        if (name == "PACKAGE")
                        {
                            value = mesInfo[0];
                        }
                        else if (name == "DESCRIPTION")
                        {
                            value = mesInfo[1];
                        }
                        else if (name == "LF12NC")
                        {
                            if (wstype.Contains("ASMWB") == true) 
                            {
                                LF = "1234";
                                value = LF;
                            } 
                            else
                            {
                                LF = getLeadFrame12NCByWo(woid);
                                value = LF;
                                
                            }
                        }

                        else if (name == "MAT_LDISPID" && (Wsid.ToString().Contains("-DA")==true || Wsid.ToString().Contains("-CB")==true))
                        {
                            value = getMaterialInfo(woid, mesInfo[0], Wsid, name, materialID);
                        }
                        else if (name == "MAT_RDISPID" && (Wsid.ToString().Contains("-DA") == true || Wsid.ToString().Contains("-CB") == true))
                        {
                            value = getMaterialInfo(woid, mesInfo[0], Wsid, name, materialID);
                        }
                        else if (name == "MAT_LEPOXYID" && (Wsid.ToString().Contains("-DA") == true || Wsid.ToString().Contains("-CB") == true))
                        {
                            value = getMaterialInfo(woid, mesInfo[0], Wsid, name, materialID);
                        }
                        else if (name == "MAT_REPOXYID" && (Wsid.ToString().Contains("-DA") == true || Wsid.ToString().Contains("-CB") == true))
                        {
                            value = getMaterialInfo(woid, mesInfo[0], Wsid, name, materialID);  
                        }

                        else if (name == "MAT_BHCOLLID" && (Wsid.ToString().Contains("-DA") == true))
                        {
                            value = getMaterialInfo(woid, mesInfo[0], Wsid, name, materialID);
                        }
                        else if (name == "MAT_EJECTNDID" && (Wsid.ToString().Contains("-DA") == true))
                        {
                            value = getMaterialInfo(woid, mesInfo[0], Wsid, name, materialID);
                        }
                        else if (name == "MAT_SINGUID" && Wsid.ToString().Contains("-CB") == true)
                        {
                            value = getMaterialInfo(woid, mesInfo[0], Wsid, name, materialID);
                        }
                        else if (name == "MAT_PHEADID" && Wsid.ToString().Contains("-CB") == true)
                        {
                            value = getMaterialInfo(woid, mesInfo[0], Wsid, name, materialID);
                        }
                        else if (name == "MAT_CLIPFRID" && Wsid.ToString().Contains("-CB") == true)
                        {
                            value = getMaterialInfo(woid, mesInfo[0], Wsid, name, materialID);
                        }
                        else if (name == "MAT_MAGID" && Wsid.ToString().Contains("-RO") == true)
                        {
                            value = getMaterialInfo(woid, mesInfo[0], Wsid, name, materialID);
                        }  
                        setWorkOrderAttribute(dbOrderUpdate, name, value);
                    }
                 //   string recipename = getRecipeName(dbOrderUpdate.WsId.ToUpper(), mesInfo[0], mesInfo[1],LF);
                //    string recipename = getRecipeName(wstype, mesInfo[0], mesInfo[1], LF, Wsid, woid);
                    string recipename = getRecipeNameFA(wstype, mesInfo.package, mesInfo.product, mesInfo.nc12, woid);
                    if (recipename.Length > 1)
                    {
                        setWorkOrderAttribute(dbOrderUpdate, "RECIPE", recipename);
                    }
                }
                else
                    if (woid.StartsWith("PKGTEST"))
                    {
                        setWorkOrderAttribute(dbOrderUpdate, "RECIPE", "SOT1232A");                        
                    }
                    else if (woid.StartsWith("SOT1262"))
                    {
                        setWorkOrderAttribute(dbOrderUpdate, "RECIPE", "SOT1262A");
                    }
                if (wstype == "2DMARKER")
                {
                    string lfSize = "";
                    string requested = "";

                    if (mesInfo.package == "SOT1232" || mesInfo.package == "SOT1262")
                    {
                        lfSize = "128,48";
                        requested = "43008";
                        setWorkOrderAttribute(dbOrderUpdate, "LFSIZE", "128,48");
                        setWorkOrderAttribute(dbOrderUpdate, "REQUESTED", "43008");
                    }
                    else 
                   {
                      List<string> lfAttr =  getLFAttribute(LF, mesInfo.package);
                      if (lfAttr.Count == 2)
                      {
                          lfSize = lfAttr[0];
                          int defaultqty = Convert.ToInt32(lfAttr[1]);
                        //  requested = Convert.ToString(GetRequestedQty(dbOrderUpdate.Workorder.Woid, defaultqty));
                          requested = Convert.ToString(GetRequestedQty(woid, defaultqty, wstype, mesInfo.package));
                          if (!string.IsNullOrEmpty(lfSize))
                          {
                              setWorkOrderAttribute(dbOrderUpdate, "LFSIZE", lfSize);
                              string[] size = lfSize.Split(new char[] { ',' });
                              // Lotfi remove for APM 
                              //int rows = Convert.ToInt32(size[0]);
                              //int clums = Convert.ToInt32(size[1]);
                              //if (Convert.ToInt32(requested) == defaultqty)
                              //    requested = (Convert.ToInt32(requested) - rows * clums).ToString();
                              setWorkOrderAttribute(dbOrderUpdate, "LFSIZE", lfSize);
                              setWorkOrderAttribute(dbOrderUpdate, "REQUESTED", requested);
                          }  
                      }
                                
                    }
                    //if (!string.IsNullOrEmpty(lfSize))
                    //{ 
                    //    setWorkOrderAttribute(dbOrderUpdate, "LFSIZE", lfSize);
                    //    string[] size = lfSize.Split(new char[] {','});
                    //    int rows= Convert.ToInt32(size[0]);
                    //    int clums = Convert.ToInt32(size[1]);
                    //    if (Convert.ToInt32(requested) > rows * clums)
                    //        requested = (Convert.ToInt32(requested) - rows * clums).ToString();
                    //    setWorkOrderAttribute(dbOrderUpdate, "REQUESTED", requested);
                    //}
                }
                if (wstype == "AOI")
                {
                    setWorkOrderAttribute(dbOrderUpdate, "XRAY_REQUIRED", IsThisContainerHaveSI(woid.ToUpper()));
                }
                */
               
                if (wstype == "DIEBOND")
                {
                    string lfSize = "";
                    string requested = "";
                    string strPackage = "";

                    string recipename = getRecipeNameFA(wstype, mesInfo.package, mesInfo.product, mesInfo.nc12, woid);
                    if (recipename.Length > 1)
                    {
                        setWorkOrderAttribute(dbOrderUpdate, "RECIPE", recipename);
                    }

                    List<string> lfAttr = getLFAttribute(mesInfo.nc12, mesInfo.package);
                    if (lfAttr.Count == 2)
                    {
                        lfSize = lfAttr[0];
                        int defaultqty = Convert.ToInt32(lfAttr[1]);
                        //  requested = Convert.ToString(GetRequestedQty(dbOrderUpdate.Workorder.Woid, defaultqty));
                        //requested = Convert.ToString(GetRequestedQty(woid, defaultqty, wstype, mesInfo.package));
                        requested = lfAttr[1];
                        if (!string.IsNullOrEmpty(lfSize))
                        {
                            /*
                            setWorkOrderAttribute(dbOrderUpdate, "LFSIZE", lfSize);
                            string[] size = lfSize.Split(new char[] { ',' });
                            int rows = Convert.ToInt32(size[0]);
                            int clums = Convert.ToInt32(size[1]);
                            if (Convert.ToInt32(requested) == defaultqty)
                                requested = (Convert.ToInt32(requested) - rows * clums).ToString();
                            */
                            setWorkOrderAttribute(dbOrderUpdate, "LFSIZE", lfSize);
                            setWorkOrderAttribute(dbOrderUpdate, "REQUESTED", requested);
                            setWorkOrderAttribute(dbOrderUpdate, "PACKAGE", getFAPackageName(mesInfo.product, mesInfo.package));
                        }
                    }
                }

                if (wstype == "PLATING")
                {

                    string lfSize = "30,6";
                    string requested = "4000";

                    string recipename = getRecipeNameFA(wstype, mesInfo.package, mesInfo.product, mesInfo.nc12, woid);
                    if (recipename.Length > 1)
                    {
                        setWorkOrderAttribute(dbOrderUpdate, "RECIPE", recipename);
                    }

                    setWorkOrderAttribute(dbOrderUpdate, "LFSIZE", lfSize);
                    setWorkOrderAttribute(dbOrderUpdate, "REQUESTED", requested);

                    //setWorkOrderAttribute(dbOrderUpdate, "PACKAGE", getFAPackageName(mesInfo.product, mesInfo.package));
                }

                // comment out for waferID confirm, not all waferID have same batch between MES and SAP
                //if (wstype == "ADAT")
                //{
                //   var waferID= dbOrderUpdate.Workorder.Attributes.Find(x => x.Name.ToUpper()== "WAFERID");
                //   if (waferID!=null&&!string.IsNullOrEmpty(waferID.Value))
                //   {
                //       setWorkOrderAttribute(dbOrderUpdate, "RESULT", WaferVerify(waferID.Value.ToString(), dbOrderUpdate.Workorder.Woid));
                //   }
                //}                
            }
        }
        private static void dbOrderUpdateByMESADAT(TraceLog tracelog, DBorderUpdate dbOrderUpdate, string woid, string Wsid, string WAFERID, string OCRid) //, string machineState)
        {
            // if (string.IsNullOrEmpty(dbOrderUpdate.Workorder.Woid))
            if (string.IsNullOrEmpty(woid))
            {
                throw new ArgumentException("No workorder ID specified.");
            }
            else
            {
                string[] so = woid.Split('-');
                string soid = so[0].ToString();
               // string FlagData =  FlagVerify(soid);
                tracelog.EnterMethod(new string[] { "workOrderId" }, dbOrderUpdate.Workorder.Woid);
                string wstype = getWSType(wsid: dbOrderUpdate.WsId);
                if (dbOrderUpdate.WsId.Substring(0, 2) == "AD")
                { wstype = "ADAT"; }
                List<string> attributes = new List<string>(new[] { "PACKAGE", "DESCRIPTION", "LF12NC", "WAFERID" });
                var mesInfo = getMESInfo(woid);
                string LF = "";
    
                if (mesInfo.Count == 2)
                {

                    foreach (string name in attributes)
                    {
                        string value = string.Empty;
                        if (name == "PACKAGE")
                        {
                            value = mesInfo[0];
                        }
                        else if (name == "DESCRIPTION")
                        {
                            value = mesInfo[1];
                        }
                        else if (name == "LF12NC")
                        {
                             LF = getLeadFrame12NCByWo(woid);
                             value = LF;
                        }
                        else if (name == "WAFERID")
                        {
                            if (!string.IsNullOrEmpty(WAFERID))
                            {
                                if (WaferVerifyOCRID(WAFERID, woid) == true)
                                {
                                    if (OCRid != "none")
                                    { value = OCRid; }
                                    else
                                    { value = WAFERID; }
                                
                                }
                                

                                // lotfi remove state
                                //else
                                //{  //lotfi add
                                //   // value = string.Format("Wafer {0} not belong to {1}", WAFERID, woid); //lotfi temporary remove
                                //    if (dbOrderUpdate.Workorder.State.ToString() == "RUNNING")
                                //    { value = string.Format("Wafer {0} not belong to {1}", WAFERID, woid); }
                                //    else
                                //    {
                                //        value = WAFERID;
                                //    }
                                //}

                                //if (testing.ToString() == "0")
                                //    {
                                //        dbOrderUpdate.Workorder.Attributes.Add(new Attribute("RESULT", string.Format("WOID:{0} not exist in MES!", dbOrderUpdate.Workorder.Woid)));
                                //    }

                                //   dbOrderUpdate.Workorder.Attributes.Add(new Attribute("RESULT", string.Format("OCRID:{0} not match in MES!", OCRID)));                               
                            }
                            else
                            {
                               // value = "Empty";
                            }

                        }
                        setWorkOrderAttribute(dbOrderUpdate, name, value);
                    }
                    //   string recipename = getRecipeName(dbOrderUpdate.WsId.ToUpper(), mesInfo[0], mesInfo[1],LF);
                    string recipename = getRecipeName(wstype, mesInfo[0], mesInfo[1], LF, Wsid, woid);
                    if (recipename.Length > 1)
                    {
                        setWorkOrderAttribute(dbOrderUpdate, "RECIPE", recipename);
                    }
                }
                else
                    if (woid.StartsWith("PKGTEST"))
                    {
                        setWorkOrderAttribute(dbOrderUpdate, "RECIPE", "SOT1232A");
                    }
                    else if (woid.StartsWith("SOT1262"))
                    {
                        setWorkOrderAttribute(dbOrderUpdate, "RECIPE", "SOT1262A");
                    }
                if (wstype == "2DMARKER")
                { 
                    string lfSize = "";
                    string requested = "";

                    if (mesInfo[0] == "SOT1232" || mesInfo[0] == "SOT1262")
                    {
                        lfSize = "128,48";
                        requested = "43008";
                        setWorkOrderAttribute(dbOrderUpdate, "LFSIZE", "128,48");
                        setWorkOrderAttribute(dbOrderUpdate, "REQUESTED", "43008");
                    }
                    else
                    {
                        List<string> lfAttr = getLFAttribute(LF, mesInfo[0]);
                        if (lfAttr.Count == 2)
                        {
                            lfSize = lfAttr[0];
                            int defaultqty = Convert.ToInt32(lfAttr[1]);
                            //  requested = Convert.ToString(GetRequestedQty(dbOrderUpdate.Workorder.Woid, defaultqty));
                            requested = Convert.ToString(GetRequestedQty(woid, defaultqty, wstype, mesInfo[0]));
                            if (!string.IsNullOrEmpty(lfSize))
                            {
                                setWorkOrderAttribute(dbOrderUpdate, "LFSIZE", lfSize);
                                string[] size = lfSize.Split(new char[] { ',' });
                                // Lotfi remove for APM 
                                //int rows = Convert.ToInt32(size[0]);
                                //int clums = Convert.ToInt32(size[1]);
                                //if (Convert.ToInt32(requested) == defaultqty)
                                //    requested = (Convert.ToInt32(requested) - rows * clums).ToString();
                                setWorkOrderAttribute(dbOrderUpdate, "LFSIZE", lfSize);
                                setWorkOrderAttribute(dbOrderUpdate, "REQUESTED", requested);
                            }
                        }

                    }
                    //if (!string.IsNullOrEmpty(lfSize))
                    //{ 
                    //    setWorkOrderAttribute(dbOrderUpdate, "LFSIZE", lfSize);
                    //    string[] size = lfSize.Split(new char[] {','});
                    //    int rows= Convert.ToInt32(size[0]);
                    //    int clums = Convert.ToInt32(size[1]);
                    //    if (Convert.ToInt32(requested) > rows * clums)
                    //        requested = (Convert.ToInt32(requested) - rows * clums).ToString();
                    //    setWorkOrderAttribute(dbOrderUpdate, "REQUESTED", requested);
                    //}
                }

                if (wstype == "DIEBOND")
                {
                    string lfSize = "";
                    string requested = "";
                    List<string> lfAttr = getLFAttribute(LF, mesInfo[0]);
                    if (lfAttr.Count == 2)
                    {
                        lfSize = lfAttr[0];
                        int defaultqty = Convert.ToInt32(lfAttr[1]);
                        //  requested = Convert.ToString(GetRequestedQty(dbOrderUpdate.Workorder.Woid, defaultqty));
                        requested = Convert.ToString(GetRequestedQty(woid, defaultqty, wstype, mesInfo[0]));
                        if (!string.IsNullOrEmpty(lfSize))
                        {
                            setWorkOrderAttribute(dbOrderUpdate, "LFSIZE", lfSize);
                            string[] size = lfSize.Split(new char[] { ',' });
                            int rows = Convert.ToInt32(size[0]);
                            int clums = Convert.ToInt32(size[1]);
                            if (Convert.ToInt32(requested) == defaultqty)
                                requested = (Convert.ToInt32(requested) - rows * clums).ToString();
                            setWorkOrderAttribute(dbOrderUpdate, "LFSIZE", lfSize);
                            setWorkOrderAttribute(dbOrderUpdate, "REQUESTED", requested);
                        }
                    }
                }
                // comment out for waferID confirm, not all waferID have same batch between MES and SAP
                //if (wstype == "ADAT")
                //{
                //   var waferID= dbOrderUpdate.Workorder.Attributes.Find(x => x.Name.ToUpper()== "WAFERID");
                //   if (waferID!=null&&!string.IsNullOrEmpty(waferID.Value))
                //   {
                //       setWorkOrderAttribute(dbOrderUpdate, "RESULT", WaferVerify(waferID.Value.ToString(), dbOrderUpdate.Workorder.Woid));
                //   }
                //}


            }
        }

        private static void dbOrderUpdateByNonMES(TraceLog tracelog, DBorderUpdate dbOrderUpdate, string woid)
        {
            //Accepted WOID is "AGGxxxx1W-xx" 
            string[] Pkg = woid.Split('-');
            var MyPrefix = Pkg[0].Substring(0, 2);
            var MyPkg = Pkg[0].Substring(Pkg[0].Length - 2);

 
            if (string.IsNullOrEmpty(MyPrefix))
            {
                throw new ArgumentException("LF 12NC is not specified.");
            }
            else
            {

                if (((MyPrefix == "2D")) && (MyPkg == "1U"))// "G00001U"              
                {
                    setWorkOrderAttribute(dbOrderUpdate, "PACKAGE", "SOD128");
                    setWorkOrderAttribute(dbOrderUpdate, "DESCRIPTION", "Generic");
                    setWorkOrderAttribute(dbOrderUpdate, "LF12NC", "992405510339");
                    setWorkOrderAttribute(dbOrderUpdate, "RECIPE", "SOD128WB");
                    setWorkOrderAttribute(dbOrderUpdate, "LFSIZE", "50,6");
                    setWorkOrderAttribute(dbOrderUpdate, "REQUESTED", "150000");                               
                }
                if (((MyPrefix == "2D")) && (MyPkg == "1T")) // "G00001T"
                {
                    setWorkOrderAttribute(dbOrderUpdate, "PACKAGE", "SOD123W");
                    setWorkOrderAttribute(dbOrderUpdate, "DESCRIPTION", "Generic");
                    setWorkOrderAttribute(dbOrderUpdate, "LF12NC", "992405518059");
                    setWorkOrderAttribute(dbOrderUpdate, "RECIPE", "SOD123WC");
                    setWorkOrderAttribute(dbOrderUpdate, "LFSIZE", "60,12");
                    setWorkOrderAttribute(dbOrderUpdate, "REQUESTED", "324000");
                }
                if (((MyPrefix == "2D")) && (MyPkg == "2T")) //"G00002T"
                {
                    setWorkOrderAttribute(dbOrderUpdate, "PACKAGE", "SOD123W");
                    setWorkOrderAttribute(dbOrderUpdate, "DESCRIPTION", "Generic");
                    setWorkOrderAttribute(dbOrderUpdate, "LF12NC", "992405510319");
                    setWorkOrderAttribute(dbOrderUpdate, "RECIPE", "SOD123WB");
                    setWorkOrderAttribute(dbOrderUpdate, "LFSIZE", "50,6");
                    setWorkOrderAttribute(dbOrderUpdate, "REQUESTED", "120000");
                }
                if (((MyPrefix == "2D")) && (MyPkg == "1V")) //"G00001V"
                {
                    setWorkOrderAttribute(dbOrderUpdate, "PACKAGE", "SOT1289");
                    setWorkOrderAttribute(dbOrderUpdate, "DESCRIPTION", "Generic");
                    setWorkOrderAttribute(dbOrderUpdate, "LF12NC", "992405518089");
                    setWorkOrderAttribute(dbOrderUpdate, "RECIPE", "SOT1289");
                    setWorkOrderAttribute(dbOrderUpdate, "LFSIZE", "30,8");
                    setWorkOrderAttribute(dbOrderUpdate, "REQUESTED", "120000");
                }
                if (((MyPrefix == "2D")) && (MyPkg == "1W")) //"G00001W"
                {
                    setWorkOrderAttribute(dbOrderUpdate, "PACKAGE", "SOT669");
                    setWorkOrderAttribute(dbOrderUpdate, "DESCRIPTION", "Generic");
                    setWorkOrderAttribute(dbOrderUpdate, "LF12NC", "339921032241");
                    setWorkOrderAttribute(dbOrderUpdate, "RECIPE", "LFPAK");
                    setWorkOrderAttribute(dbOrderUpdate, "LFSIZE", "20,5");
                    setWorkOrderAttribute(dbOrderUpdate, "REQUESTED", "56000");
                }
                if (((MyPrefix == "2D") ) && (MyPkg == "2W")) //"G00002W"
                {
                    setWorkOrderAttribute(dbOrderUpdate, "PACKAGE", "SOT1205");
                    setWorkOrderAttribute(dbOrderUpdate, "DESCRIPTION", "Generic");
                    setWorkOrderAttribute(dbOrderUpdate, "LF12NC", "339921036691");
                    setWorkOrderAttribute(dbOrderUpdate, "RECIPE", "SOT1205");
                    setWorkOrderAttribute(dbOrderUpdate, "LFSIZE", "20,5");
                    setWorkOrderAttribute(dbOrderUpdate, "REQUESTED", "40000");
                }
            }
        }

        private static string getPackageName(string woid)
        {
            if (string.IsNullOrEmpty(woid))
            {
                throw new ArgumentException("No workorder ID specified.");
            }
            string package = null;
            using (OracleConnection mesDbConn = new OracleConnection(ConfigurationManager.ConnectionStrings["MES"].ConnectionString))
            {
                mesDbConn.Open();
                using (OracleCommand cmd = mesDbConn.CreateCommand())
                {
                    cmd.CommandText = string.Format(@"SELECT pk.Mes_packagename package
                                                          FROM container ac,
                                                               product pd,
                                                               productfamily pf,
                                                               package pk
                                                         WHERE     ac.productid = pd.productid
                                                               AND pd.productfamilyid = pf.productfamilyid
                                                               AND pf.mes_packageid = pk.mes_packageid
                                                               AND ac.containername = '{0}'",woid.Substring(0,woid.Length));
                    using (OracleDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            package = reader["package"].ToString().Trim();
                        }
                    }
                }
                mesDbConn.Close();
            }
            if (woid.StartsWith("PKGTEST") || woid.StartsWith("SOT1262"))
            { package = "SOT1232"; }
            return package;                   
        }

        private static string getProductName(string woid)
        {
            if (string.IsNullOrEmpty(woid))
            {
                throw new ArgumentException("No workorder ID specified.");
            }
            string product = null;
            using (OracleConnection mesDbConn = new OracleConnection(ConfigurationManager.ConnectionStrings["MES"].ConnectionString))
            {
                mesDbConn.Open();
                using (OracleCommand cmd = mesDbConn.CreateCommand())
                {
                    cmd.CommandText = string.Format(@"SELECT PD.DESCRIPTION product
                                                          FROM container ac,
                                                               product pd,
                                                               productfamily pf,
                                                               package pk
                                                         WHERE     ac.productid = pd.productid
                                                               AND pd.productfamilyid = pf.productfamilyid
                                                               AND pf.mes_packageid = pk.mes_packageid
                                                               AND ac.containername = '{0}'", woid.Substring(0, woid.Length));
                    using (OracleDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            product = reader["product"].ToString().Trim();
                        }
                    }
                }
                mesDbConn.Close();
            }
            return product;
        }

        private static string getConsumableStatus(string wsid, string package, string product, string consumableItemHeader, string consumableItemInput)
        {
            string sql_recipe_consumable_adat = "";
            string consumableStatus = "";
            string consumableItemInDb = "";

            if (wsid.Contains("AD"))
            {
                using (SqlConnection repDbConn = new SqlConnection(ConfigurationManager.ConnectionStrings["RECIPE"].ConnectionString))
                {
                    repDbConn.Open();
                    sql_recipe_consumable_adat = " SELECT * FROM ADATPPRRECIPE WHERE upper(package) = @package AND upper(Product) = @product;"; // upper(wstype) =@wstype AND package = @package AND leadframe12nc = @lf12nc;";
                    SqlParameter pPackage = new SqlParameter("@package", package) { SqlDbType = SqlDbType.VarChar };
                    SqlParameter pProduct = new SqlParameter("@product", product) { SqlDbType = SqlDbType.VarChar };


                    using (SqlCommand cmd = repDbConn.CreateCommand())
                    {
                        cmd.CommandText = sql_recipe_consumable_adat;
                        cmd.Parameters.Add(pPackage);
                        cmd.Parameters.Add(pProduct); using (SqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                consumableItemInDb = reader[consumableItemHeader].ToString().Trim().ToUpper();
                            }
                        }
                    }
                    repDbConn.Close();
                }
            }



            if (consumableItemInDb == consumableItemInput) { consumableStatus = "Pass"; }
            else
            { consumableStatus = "Fail"; }
            return consumableStatus;
        }

        private static string getServiceName(string package)
        {
            if (string.IsNullOrEmpty(package))
            {
                throw new ArgumentException("No Package info specified.");
            }
            string servicename = "NA";
            using (SqlConnection repDbConn = new SqlConnection(ConfigurationManager.ConnectionStrings["RECIPE"].ConnectionString))
            {
                repDbConn.Open();
                using (SqlCommand cmd = repDbConn.CreateCommand())
                {
                    cmd.CommandText = string.Format(@"select ams.servicename from awacsmes ams where ams.packagename = '{0}'", package);
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            servicename = reader["servicename"].ToString().Trim().ToUpper();
                        }
                    }
                }
                repDbConn.Close();
            }
            return servicename;         
        }
        /// <summary>
        /// get PackageName and Description info from MES with Wo
        /// if no data respond try with SO=wo.substring(0,wo.length-2)
        /// </summary>
        /// <param name="woid"></param>
        /// <returns></returns>
        private static List<string> getMESInfo(string woid)
        {
            string[] result1 = new string[2];
            List<string> result = new List<string> { };
            
       

            const string sql_Description_WO = @"SELECT DISTINCT SO.CONTAINERNAME SO,
                                            so.containerid,
                                            WO.CONTAINERID ACID,
                                            WO.CONTAINERNAME WO,
                                            PK.MES_PACKAGENAME PACKAGE,
                                            PFSO.PRODUCTFAMILYNAME PRODUCTFAMILY,
                                            PDSO.DESCRIPTION DESCRIPTION
                                FROM container so,
                                    container wo,
                                    productbase pbso,
                                    product pdso,
                                    productfamily pfso,
                                    package pk,
                                    containermes_fginso cf,
                                    mes_fginso fis
                                WHERE     wo.MES_SHOPORDERCONTAINERID = SO.CONTAINERID
                                    AND PK.MES_PACKAGEID = PFSO.MES_PACKAGEID
                                    AND SO.CONTAINERID = CF.INSTANCEID
                                    AND CF.MES_FGINSOID = FIS.MES_FGINSOID
                                    AND PDSO.PRODUCTFAMILYID = PFSO.PRODUCTFAMILYID
                                    AND PBSO.PRODUCTBASEID =
                                            DECODE (FIS.MES_FINISHEDGOODBASEID,
                                                    '0000000000000000', PDso.PRODUCTBASEID,
                                                    FIS.MES_FINISHEDGOODBASEID)
                                    AND PDSO.PRODUCTID =
                                            DECODE (FIS.MES_FINISHEDGOODID,
                                                    '0000000000000000', PBso.REVOFRCDID,
                                                    FIS.MES_FINISHEDGOODID)
                                    AND WO.CONTAINERNAME = :wo";
            const string sql_Description_SO = @"SELECT DISTINCT SO.CONTAINERNAME SO,
                                                            so.containerid,
                                                            PK.MES_PACKAGENAME PACKAGE,
                                                            PFSO.PRODUCTFAMILYNAME PRODUCTFAMILY,
                                                            PDSO.DESCRIPTION DESCRIPTION
                                              FROM container so,
                                                   productbase pbso,
                                                   product pdso,
                                                   productfamily pfso,
                                                   package pk,
                                                   containermes_fginso cf,
                                                   mes_fginso fis
                                             WHERE    PK.MES_PACKAGEID = PFSO.MES_PACKAGEID
                                                   AND SO.CONTAINERID = CF.INSTANCEID
                                                   AND CF.MES_FGINSOID = FIS.MES_FGINSOID
                                                   AND PDSO.PRODUCTFAMILYID = PFSO.PRODUCTFAMILYID
                                                   AND PBSO.PRODUCTBASEID =
                                                          DECODE (FIS.MES_FINISHEDGOODBASEID,
                                                                  '0000000000000000', PDso.PRODUCTBASEID,
                                                                  FIS.MES_FINISHEDGOODBASEID)
                                                   AND PDSO.PRODUCTID =
                                                          DECODE (FIS.MES_FINISHEDGOODID,
                                                                  '0000000000000000', PBso.REVOFRCDID,
                                                                  FIS.MES_FINISHEDGOODID)
                                                AND so.CONTAINERNAME = :so";
            using (OracleConnection mesDbConn = new OracleConnection(ConfigurationManager.ConnectionStrings["MES"].ConnectionString))
            {
                mesDbConn.Open();
                //OracleParameter pWO = new OracleParameter(":wo", woid) { OracleType = OracleType.Char };
                OracleParameter pWO =
    new OracleParameter(":wo", OracleDbType.Char)
    {
        Value = woid
    };
                using (OracleCommand cmd = mesDbConn.CreateCommand())
                {
                    cmd.CommandText = sql_Description_WO;
                    cmd.Parameters.Add(pWO);
                    using (OracleDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        { 
                            //result1[0]=reader["PACKAGE"].ToString();
                            //result1[1] = reader["DESCRIPTION"].ToString();
                            result.Add(reader["PACKAGE"].ToString());
                            result.Add(reader["DESCRIPTION"].ToString());
                        }
                    }
                    //if (string.IsNullOrEmpty(result1[0]))
                    if(result.Count!=2)
                    {
                        if (woid.Length> 8) {woid = woid.Substring(0, woid.Length - 3);}
                        cmd.CommandText = sql_Description_SO;
                        //OracleParameter pSO = new OracleParameter(":so", woid) { OracleType = OracleType.Char };
                        OracleParameter pSO =
    new OracleParameter(":so", OracleDbType.Char)
    {
        Value = woid
    };
                        cmd.Parameters.Clear();
                        cmd.Parameters.Add(pSO);
                        using (OracleDataReader reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                //result1[0] = reader["PACKAGE"].ToString();
                                //result1[1] = reader["DESCRIPTION"].ToString();
                                result.Add(reader["PACKAGE"].ToString());
                                result.Add(reader["DESCRIPTION"].ToString());
                            }
                        }
                    }                   
                }
                mesDbConn.Close();            
            }
            return result;
        }
        /// <summary>
        /// GET LeadFrame 12NC by Wo
        /// If no data retrieved by wo, then try with SO= wo.substring(0,wo.length-2)
        /// </summary>
        /// <param name="woid">woid</param>
        /// <returns>LF12NC</returns>
        private static string getLeadFrameDescByWo(string woid)
        {
            string strLF = "";
            string sql_LF = @"SELECT DISTINCT WO.CONTAINERNAME WO, REPLACE (PB.PRODUCTNAME, ' ', '') LF12NC, PD.DESCRIPTION LFDESC
                                  FROM container wo,
                                       containermateriallistitem cmi,
                                       product pd,
                                       productbase pb,
                                       productfamily pf
                                 WHERE     cmi.containerid = wo.containerid(+)
                                       AND cmi.productid = pd.productid
                                       AND pd.productbaseid = pb.productbaseid
                                       AND pf.productfamilyid = pd.productfamilyid
                                       AND pf.productfamilyname = 'LeadFrame'
                                       AND WO.CONTAINERNAME = :wo";

            string sql_RawMaterialbySO = @"SELECT c.RawMaterials
                                              FROM CONTAINER C
                                             WHERE C.CONTAINERNAME = :so ";
            using (OracleConnection mesDbcon = new OracleConnection(ConfigurationManager.ConnectionStrings["MES"].ConnectionString))
            {
                mesDbcon.Open();
                //OracleParameter pWO = new OracleParameter(":wo", woid) { OracleType = OracleType.Char };
                OracleParameter pWO =
     new OracleParameter(":wo", OracleDbType.Varchar2)
     {
         Value = woid
     };
                using (OracleCommand cmd = mesDbcon.CreateCommand())
                {
                    cmd.CommandText = sql_LF;
                    cmd.Parameters.Add(pWO);
                    using (OracleDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            strLF = reader["LFDESC"].ToString();
                        }
                    }
                    if (strLF.Length == 0)
                    {
                        string soid = woid.Substring(0, woid.Length - 3);
                        //OracleParameter pSO = new OracleParameter(":so", soid) { OracleType = OracleType.Char };
                        OracleParameter pSO =
    new OracleParameter(":so", OracleDbType.Varchar2)
    {
        Value = soid
    };
                        cmd.CommandText = sql_RawMaterialbySO;
                        cmd.Parameters.Clear();
                        cmd.Parameters.Add(pSO);
                        string rawMaterials = "";
                        using (OracleDataReader reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                rawMaterials = reader["RawMaterials"].ToString();
                                strLF = getLFFromRaw(rawMaterials);
                            }
                        }
                    }
                }
                mesDbcon.Close();
            }

            return strLF;
        }
        
        private static string getLeadFrame12NCByWo(string woid)
        {
            string strLF = "";
            string sql_LF = @"SELECT DISTINCT WO.CONTAINERNAME WO, REPLACE (PB.PRODUCTNAME, ' ', '') LF12NC
                                  FROM container wo,
                                       containermateriallistitem cmi,
                                       product pd,
                                       productbase pb,
                                       productfamily pf
                                 WHERE     cmi.containerid = wo.containerid(+)
                                       AND cmi.productid = pd.productid
                                       AND pd.productbaseid = pb.productbaseid
                                       AND pf.productfamilyid = pd.productfamilyid
                                       AND pf.productfamilyname = 'LeadFrame'
                                       AND WO.CONTAINERNAME = :wo";

            string sql_RawMaterialbySO = @"SELECT c.RawMaterials
                                              FROM CONTAINER C
                                             WHERE C.CONTAINERNAME = :so ";
            using (OracleConnection mesDbcon = new OracleConnection(ConfigurationManager.ConnectionStrings["MES"].ConnectionString))
            {
                mesDbcon.Open();
                //OracleParameter pWO = new OracleParameter(":wo", woid) { OracleType = OracleType.Char };
                OracleParameter pWO =
    new OracleParameter(":wo", OracleDbType.Varchar2)
    {
        Value = woid
    };
                using (OracleCommand cmd = mesDbcon.CreateCommand())
                {
                    cmd.CommandText = sql_LF;
                    cmd.Parameters.Add(pWO);
                    using (OracleDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            strLF = reader["LF12NC"].ToString();
                        }
                    }
                    if (strLF.Length == 0)
                    {
                        string soid = woid.Substring(0, woid.Length - 3);
                        //OracleParameter pSO = new OracleParameter(":so",soid) { OracleType = OracleType.Char };
                        OracleParameter pSO =
    new OracleParameter(":so", OracleDbType.Varchar2)
    {
        Value = soid
    };
                        cmd.CommandText = sql_RawMaterialbySO;
                        cmd.Parameters.Clear();
                        cmd.Parameters.Add(pSO);
                        string rawMaterials = "";
                        using (OracleDataReader reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                rawMaterials = reader["RawMaterials"].ToString();
                                strLF = getLFFromRaw(rawMaterials);
                            }
                        }
                    }
                }
                mesDbcon.Close();
            }

            return strLF;
        }

        /// <summary>
        /// Get LeadFrame 12NC with SO in field rawMaterials
        /// Verify it in MES and return if exist
        /// </summary>
        /// <param name="raw">container.rawMaterials</param>
        /// <returns> leadframe12NC</returns>
        private static string getLFFromRaw(string raw)
        {
            string strRawLF = "";
            string tmp ="";
            string[] raws = raw.Split(new Char[] { '\n', '-' });
            try
            {

                foreach (string item in raws)
                {
                    tmp = item.Trim();
                    if (tmp.Substring(0, 1) == "L")
                    {
                        strRawLF = tmp.Substring(1, tmp.Length - 1);
                        break;
                    }                 
                }
            }
            catch 
            {
                strRawLF = "";
            }
            return strRawLF.Replace(" ","");
                    
        }        
        

        ///// <summary>
        ///// Get recipe from SMS DB, another DB here to get recipe 
        ///// </summary>
        ///// <param name="WSid">wsid</param>
        ///// <param name="package">PackageName</param>
        ///// <param name="lf12nc">LF12NC</param>
        ///// <returns>RecipeName</returns>
        //private static string getRecipeName(string Wsid, string package, string productdecription, string lf12nc)
        //{
        //    string recipeName = "";
        //    using (OracleConnection repDbConn = new OracleConnection(ConfigurationManager.ConnectionStrings["RECIPE"].ConnectionString))
        //    {
        //        repDbConn.Open();
        //        using (OracleCommand cmd = repDbConn.CreateCommand())
        //        {
        //            cmd.CommandText = string.Format("SELECT RECIPE FROM AWACSRECIPE WHERE upper(wsid) = '{0}'  AND package = '{1}'  AND product = '{2}'  AND leadframe12nc = '{3}' ", wstype, package, productdecription, lf12nc);
        //            using (OracleDataReader reader = cmd.ExecuteReader())
        //            {
        //                while (reader.Read())
        //                {
        //                    try
        //                    {
        //                        recipeName = reader["RECIPE"].ToString().Trim();
        //                    }
        //                    catch
        //                    {
        //                        recipeName = "";
        //                    }
        //                }
        //            }
        //        }
        //        repDbConn.Close();
        //    }
        //    return recipeName;

        //}


        /// <summary>
        /// Get recipe from SMS DB, another DB here to get recipe 
        /// </summary>
        /// <param name="WStype">wstype</param>
        /// <param name="package">PackageName</param>
        /// <param name="lf12nc">LF12NC</param>
        /// <returns>RecipeName</returns>
        private static string getRecipeName(string wstype, string package, string productdecription, string lf12nc, string wsid, string woid)
        {
            string recipeName = "";
            string sql_recipe_wstype = "";
            string sql_recipe_package = "";
            string sql_recipe_rms = "";
            string sql_recipe_SOT669 = "";
            string sql_recipe_rmsup = "";
            string sql_recipe_wstype_wsid = "";
            string sql_awacs_attrib = "";
            string sql_adat_rms = "";
            string WSID_WSID = "";
            string WSID_Model ="";
            string WSID_Speed = "";
            string WaferDesc = "";
            string lfdesc = "";
            string MikropakBackMetal = "NONE";



          //  string p = package;
            
            using (SqlConnection repDbConn = new SqlConnection(ConfigurationManager.ConnectionStrings["RECIPE"].ConnectionString))

            {
                string FlagDataBimLine = FlagVerifyBimLine(woid);
                repDbConn.Open();

                if (wstype.Contains("ASMWB") == true)
                {
                    //  string sql_recipe_product = " SELECT RECIPE FROM AWACSRECIPEBYWSTYPE WHERE upper(wstype) =@wstype AND upper(package) = @package AND product = @productdecription;";
                    sql_recipe_wstype = " SELECT RECIPE FROM ASMRECIPE WHERE upper(package) = @package AND leadframe12nc = @lf12nc;"; // upper(wstype) =@wstype AND package = @package AND leadframe12nc = @lf12nc;";
                    sql_recipe_rmsup = " SELECT * FROM ASMRECIPE WHERE upper(package) = @package;";
                    sql_recipe_rms = " SELECT * FROM ASMRECIPE WHERE upper(package) = @package AND upper(product) = @productdecription;";

                    //SqlParameter pWstype = new SqlParameter("@wstype", wstype) { SqlDbType = SqlDbType.VarChar };
                    //SqlParameter pPackage = new SqlParameter("@package", package) { SqlDbType = SqlDbType.VarChar };
                    //SqlParameter pProduct = new SqlParameter("@productdecription", productdecription) { SqlDbType = SqlDbType.VarChar };
                }
                else if (wstype.ToUpper().Contains("DIEBOND") == true &&  package.Contains("SOT669") == true) 
                {
                    sql_recipe_SOT669 = " SELECT * FROM AWACSRECIPEBYWSTYPE WHERE upper(wstype) =@wstype AND upper(package) = @package AND upper(product) = @productdecription;";
                }
                else
                {
                    //  string sql_recipe_product = " SELECT RECIPE FROM AWACSRECIPEBYWSTYPE WHERE upper(wstype) =@wstype AND upper(package) = @package AND product = @productdecription;";
                    sql_recipe_wstype = " SELECT RECIPE FROM AWACSRECIPEBYWSTYPE WHERE upper(wstype) =@wstype  AND upper(package) = @package AND leadframe12nc = @lf12nc;"; // upper(wstype) =@wstype AND package = @package AND leadframe12nc = @lf12nc;";
                    sql_recipe_package = " SELECT RECIPE FROM AWACSRECIPEBYWSTYPE WHERE upper(wstype) =@wstype AND upper(package) = @package;";
                    sql_recipe_wstype_wsid = " SELECT RECIPE FROM AWACSRECIPEBYWSTYPE WHERE upper(wstype) =@wstype  AND upper(package) = @package AND machine = @wsid;"; // upper(wstype) =@wstype AND package = @package AND leadframe12nc = @lf12nc;";

                    //SqlParameter pWstype = new SqlParameter("@wstype", wstype) { SqlDbType = SqlDbType.VarChar };
                    //SqlParameter pPackage = new SqlParameter("@package", package) { SqlDbType = SqlDbType.VarChar };
                    //SqlParameter pProduct = new SqlParameter("@productdecription", productdecription) { SqlDbType = SqlDbType.VarChar };
                    //SqlParameter pLF = new SqlParameter("@lf12nc", lf12nc) { SqlDbType = SqlDbType.VarChar };
                    //SqlParameter pWsid = new SqlParameter("@wsid", wsid) { SqlDbType = SqlDbType.VarChar };
                }

                SqlParameter pWstype = new SqlParameter("@wstype", wstype) { SqlDbType = SqlDbType.VarChar };
                SqlParameter pPackage = new SqlParameter("@package", package) { SqlDbType = SqlDbType.VarChar };
                SqlParameter pProduct = new SqlParameter("@productdecription", productdecription) { SqlDbType = SqlDbType.VarChar };
                SqlParameter pLF = new SqlParameter("@lf12nc", lf12nc) { SqlDbType = SqlDbType.VarChar };
                SqlParameter pWsid = new SqlParameter("@wsid", wsid) { SqlDbType = SqlDbType.VarChar };


                using (SqlCommand cmd = repDbConn.CreateCommand())
                {
                    if ((package == "SOD128") || (package == "SOD123W") || (package =="SOD123WD") || (package=="SOD128D") ||(package == "SOT1289") || (package == "SOT669") || (package == "SOT323C") || (package == "SOT323")) //(wstype == "2DMARKER")
                    {
                        cmd.CommandText = sql_recipe_package;
                      //remove. Not use for now
                      // cmd.Parameters.Add(pLF);                               
                    }
                    if ((wstype.ToUpper() == "DIEBOND") && package.Contains("SOT669") == true) //(wstype == "DIEBOND")
                    {
                        cmd.CommandText = sql_recipe_SOT669;
                        cmd.Parameters.Add(pProduct);
           
                    }
                    if ((wstype == "2DMARKER")) //(wstype == "ADAT")
                    {
                        cmd.CommandText = sql_recipe_wstype;
                        cmd.Parameters.Add(pLF);

                    }
                    if ((wstype == "TRIMFORM")) //(wstype == "ADAT")
                    {
                        cmd.CommandText = sql_recipe_wstype_wsid;
                        cmd.Parameters.Add(pWsid);

                    }
                    if (wstype.Contains("ASMWB") == true) //(wstype == "ADAT")
                    {
                        bool wstypecheck;
                        wstypecheck = (package == "SOT323C") || (package == "SOT323") || (package == "SOT886") || (package == "SOT891");
                        if (wstypecheck == true)
                        {
                            cmd.CommandText = sql_recipe_rms;
                            cmd.Parameters.Add(pProduct);
                        }
                        else
                        {
                            cmd.CommandText = sql_recipe_rmsup;
                        }
                    }
                    if (wstype == "ADAT")
                    {
                        sql_awacs_attrib = " SELECT * FROM AWACSATTRIB WHERE upper(WSID) =@WSID;"; // upper(wstype) =@wstype AND package = @package AND leadframe12nc = @lf12nc;";
                        cmd.CommandText = sql_awacs_attrib;
                        cmd.Parameters.Add(pWsid);
                         using (SqlDataReader reader = cmd.ExecuteReader())
                        {
                         
                            if (reader.Read())
                            {
                                try
                                {
                                    WSID_WSID = reader["WSID"].ToString().Trim();
                                    WSID_Model = reader["MODEL"].ToString().Trim();
                                    WSID_Speed = reader["SPEED"].ToString().Trim();
                                    WaferDesc = getCrystalDesc(woid);
                                    lfdesc = getLeadFrameDescByWo(woid);
                                    string[] lfdesc1 = woid.Split(',');
                                    string soid = lfdesc1[0].ToString();
                                }
                                catch
                                {
                                
                                }
                             }

                         }
//lotfi 27-10-2016 remove speed and append it from awacs attribute
//                         sql_adat_rms = " SELECT Recipe FROM ADATPPRRECIPE WHERE upper(Speed) =@WSID_Speed AND upper(CrystalName) =@WaferDesc;"; // upper(wstype) =@wstype AND package = @package AND leadframe12nc = @lf12nc;";
                         sql_adat_rms = " SELECT * FROM ADATPPRRECIPE WHERE upper(CrystalName) =@WaferDesc;"; // upper(wstype) =@wstype AND package = @package AND leadframe12nc = @lf12nc;";

                        //for farah
                        int ii = 0;
                        Boolean pCheck = false;
                         if (package != "NULL") {
                             sql_adat_rms = " SELECT TOP 1 * FROM ADATPPRRECIPE WHERE Package =@package;";
                             string[] micropak = { "SOT1203:DAF", "SOT1209:EPOXY", "SOT1226:DAF", "SOT1226C:DAF", "SOT1230:WBC", "SOT1233:DAF", "SOT1234:DAF", "SOT1255:DAF", "SOT1260C:DAF", "SOT833:WBC", "SOT883C:EPOXY", "SOT886:WBC", "SOT886F:EPOXY", "SOT891:WBC", "SOT1089:WBC", "SOT1234:DAF", "SOT883F:DAF", "SOT1115:DAF", "SOT1116:DAF", "SOT1122:EPOXY", "SOT353PC:EPOXY", "SOT1202:DAF" };                       
                             for (ii = 0; ii < micropak.Length; ii++)
                             {
                                 string [] pkg = micropak[ii].ToString().Split(':');
                                 if (pkg[0].ToString() == package && FlagDataBimLine.Contains("BIM")!=true)
                                 {
                                    MikropakBackMetal = pkg[1].ToString();
                                    pCheck = true;
                                }
                             }
                             if (pCheck == false)
                             { 
                                 //Temporary off 2017-7-18 Lotfi
                                //  sql_adat_rms = " SELECT * FROM ADATPPRRECIPE WHERE upper(CrystalName) =@WaferDesc AND Package =@package;";
                                  sql_adat_rms = " SELECT * FROM ADATPPRRECIPE WHERE upper(CrystalName) =@WaferDesc;";
                                 //     SqlParameter ppackage = new SqlParameter("@package", package) { SqlDbType = SqlDbType.VarChar };
                              //    cmd.Parameters.Add(pPackage);                                                                                          
                             }
                             }
                         

                         SqlParameter pWSID_Speed = new SqlParameter("@WSID_Speed", WSID_Speed) { SqlDbType = SqlDbType.VarChar };
                         SqlParameter pWaferDesc = new SqlParameter("@WaferDesc", WaferDesc) { SqlDbType = SqlDbType.VarChar };

                         cmd.CommandText = sql_adat_rms;
                         cmd.Parameters.Add(pWSID_Speed);
                         cmd.Parameters.Add(pWaferDesc);
                            //cmd.CommandText = sql_recipe_rms;
                            //cmd.Parameters.Add(pProduct);
                    }
                    else
                    {
                        //remove. Not use for now
                      //  cmd.CommandText = sql_recipe_product;
                       // cmd.Parameters.Add(pProduct);

                    }

                    cmd.Parameters.Add(pWstype); 
                    cmd.Parameters.Add(pPackage);

                    string mystring = wsid;   //dbOrderUpdate.WsId; //jy - grab machine id
                    string last1 = mystring.Substring(mystring.Length - 1, 1);

                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            try
                            {
                                if (wstype == "ASMWBM")
                                {
                                    if(last1 == "1")
                                    {
                                        recipeName = reader["RECIPEM1"].ToString().Trim();
                                    }
                                    else if (last1 == "2")
                                    {
                                        recipeName = reader["RECIPEM2"].ToString().Trim();
                                    }
                                    else if (last1 == "3")
                                    {
                                        recipeName = reader["RECIPEM3"].ToString().Trim();
                                    }
                                    else if (last1 == "4")
                                    {
                                        recipeName = reader["RECIPEM4"].ToString().Trim();
                                    }
                                }
                                else if (wstype == "ASMWBD")
                                {
                                    if (last1 == "1")
                                    {
                                        recipeName = reader["RECIPED1"].ToString().Trim();
                                    }
                                    else if (last1 == "2")
                                    {
                                        recipeName = reader["RECIPED2"].ToString().Trim();
                                    }
                                }
                                else if (wstype == "ADAT")
                                {
                                    bool Adat3 = WSID_Model == "DBRE" || WSID_Model == "DBRG" || WSID_Model == "DBSG";
                                    if (!Adat3)
                                    {  //this is for adat2
                                        if (reader["BackMetal"].ToString().Trim() == "EUT")
                                        { string WSID_SPEED1 = "_" + WSID_Speed.Substring(0,WSID_Speed.Length - 3) + "K";
                                        recipeName = reader["RECIPE"].ToString().Trim() + WSID_SPEED1; // +"_" + WSID_Model + "_Awacs";  
                                        }
                                        if (reader["BackMetal"].ToString().Trim() == "EPX")
                                        {
                                            recipeName = reader["BackMetal"].ToString().Trim() + "_RT"; // +"_" + WSID_Model + "_Awacs";  
                                        }                                                                         
                                    }
                                    else
                                    {  //this is for adat3

                                        if (package != "NULL" && wsid.Contains("AD") != true)
                                        {
                                            string[] lfdesc1 = woid.Split(',');
                                            string soid = lfdesc1[0].ToString();
                                            string CS = GetCrystalSizeStatus(soid);
                                            if (package=="SOT886"){
                                                recipeName = package + "_" + MikropakBackMetal + "_" + CS;
                                            }
                                            else{
                                                recipeName = package + "_" + MikropakBackMetal;
                                            }                                                                                                                         
                                        }
                                        else
                                        {
                                            string rmsPkg = package.Substring(3, package.Length - 3);
                                            if (reader["BackMetal"].ToString().Trim() == "EPX")
                                            {
                                                recipeName = reader["BackMetal"].ToString().Trim() + "_" + rmsPkg + "_" + lf12nc.Substring(6, lf12nc.Length - 6) + "_" + wsid.Substring(2, wsid.Length - 2);
                                            }
                                            if (reader["BackMetal"].ToString().Trim() == "WBC" || reader["BackMetal"].ToString().Trim() == "DAF" || reader["BackMetal"].ToString().Trim() == "EUT")
                                            {
                                                recipeName = reader["BackMetal"].ToString().Trim() + "_" + reader["Temperature"].ToString().Trim() + "_" + lf12nc.Substring(6, lf12nc.Length - 6) + "_" + wsid.Substring(2, wsid.Length - 2);
                                            }
                                        }

                                    }

                                }

                                else { recipeName = reader["RECIPE"].ToString().Trim(); }
                            }
                            catch
                            {
                                recipeName = "";
                            }
                        }
                    }
                }
                repDbConn.Close();
            }
            return recipeName;

        }

        private static string getMaterialInfo(string woid, string package, string wsid, string materialName, string materialID)
        {
            string die_size = "";
            string sql_recipe_material = "";
            string recipeName = "";

            die_size = getCrystalSize(woid);

            using (SqlConnection repDbConn = new SqlConnection(ConfigurationManager.ConnectionStrings["RECIPE"].ConnectionString))
            {
                repDbConn.Open();

                if (wsid.ToString().Contains("-DA") == true)
                {
                     sql_recipe_material = " SELECT * FROM ASMDA WHERE upper(die_size) =@die_size AND upper(package) = @package AND upper(wsid) = @wsid ;";
                }
                if (wsid.ToString().Contains("-CB") == true)
                {
                     sql_recipe_material = " SELECT * FROM ASMCB WHERE upper(die_size) =@die_size AND upper(package) = @package AND upper(wsid) = @wsid ;";
                }
                if (wsid.ToString().Contains("-RO") == true)
                {
                     sql_recipe_material = " SELECT * FROM ASMRO WHERE upper(die_size) =@die_size AND upper(package) = @package AND upper(wsid) = @wsid ;";
                }

                SqlParameter pdie_size = new SqlParameter("@die_size", die_size) { SqlDbType = SqlDbType.VarChar };
                SqlParameter pPackage = new SqlParameter("@package", package) { SqlDbType = SqlDbType.VarChar };
                SqlParameter pWsid = new SqlParameter("@wsid", wsid) { SqlDbType = SqlDbType.VarChar };


                using (SqlCommand cmd = repDbConn.CreateCommand())
                {

                    cmd.CommandText = sql_recipe_material;
                    cmd.Parameters.Add(pdie_size);
                    cmd.Parameters.Add(pPackage);
                    cmd.Parameters.Add(pWsid);

                    string mystring = wsid;   
                    string last1 = mystring.Substring(mystring.Length - 1, 1);

                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            try
                            {
                                if (reader[materialName].ToString().Trim() == materialID.ToString().Trim())
                                { recipeName = reader[materialName].ToString().Trim(); }
                                
                            }
                            catch
                            {
                                recipeName = "";
                            }
                        }
                        if (recipeName.Length < 1)
                        {
                         recipeName = materialID.ToString().Trim() + "is not in MES "; }      
                    }
                }
                repDbConn.Close();
            }
            return recipeName;

        }
        private static List<string> getMaterialList(string wsid)
        {
            string sql_material_header = "";

            List<string> MyMterialList = new List<string>();

            using (SqlConnection repDbConn = new SqlConnection(ConfigurationManager.ConnectionStrings["RECIPE"].ConnectionString))
            {
                repDbConn.Open();

                if (wsid.ToString().Contains("-DA") == true)
                {
                    sql_material_header = " select name MyMAT from syscolumns where id=object_id('awacs.dbo.ASMDA');";
                }
                if (wsid.ToString().Contains("-CB") == true)
                {
                    sql_material_header = " select name MyMAT from syscolumns where id=object_id('awacs.dbo.ASMCB');";
                }
                if (wsid.ToString().Contains("-RO") == true)
                {
                    sql_material_header = " select name MyMAT from syscolumns where id=object_id('awacs.dbo.ASMRO');";
                }

                using (SqlCommand cmd = repDbConn.CreateCommand())
                {
                    cmd.CommandText = sql_material_header;

                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            try
                            {
                                {
                                    if (reader[0].ToString().Trim().Contains("MAT_")) { MyMterialList.Add(reader[0].ToString().Trim()); }
                                }
                            }
                            catch
                            {
                               
                            }
                        }
                    }
                }
                repDbConn.Close();
            }
            return MyMterialList;

        }

        /// <summary>
        /// Here to verify if this container has the specified SI on containerlevel, and return YES or NO as result.
        /// </summary>
        /// <param name="container"></param>
        /// <returns></returns>
        private static string IsThisContainerHaveSI(string container)
        {
            string result = "";
            string sql_SIVerification = @"SELECT DECODE (sign(COUNT (*)-0),1, 'YES', 'NO') RESULT
                                                FROM container c,
                                                    CONTAINERMES_SPECIALINSTRUCTIO CSI,
                                                    mes_specialinstruction SI
                                                WHERE     C.CONTAINERID = CSI.INSTANCEID
                                                    AND CSI.MES_SPECIALINSTRUCTIONID = SI.MES_SPECIALINSTRUCTIONID
                                                    AND SI.MES_SPECIALINSTRUCTIONNAME ='XRayRequired'
                                                    AND C.CONTAINERNAME = :container ";
            using (OracleConnection mesDbcon = new OracleConnection(ConfigurationManager.ConnectionStrings["MES"].ConnectionString))
            {
                mesDbcon.Open();
                //OracleParameter pContainer = new OracleParameter(":container", container) { OracleType = OracleType.Char };
                //OracleParameter pSO =
   OracleParameter pContainer =
    new OracleParameter(
        "container",
        OracleDbType.Varchar2
    )
    {
        Value = container
    };
                using (OracleCommand cmd = mesDbcon.CreateCommand())
                {
                    cmd.CommandText = sql_SIVerification;
                    cmd.Parameters.Add(pContainer);
                    using (OracleDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            result = reader["RESULT"].ToString();
                        }
                    }
                }
                mesDbcon.Close();            
            }
            return result;        
        }


        /// <summary>
        /// Inserts or updates the workorder attribute with the specified name and the specified value.
        /// </summary>
        /// <param name="dbOrderUpdate">The DBorderUpdate object.</param>
        /// <param name="name">The atttribute name.</param>
        /// <param name="value">The attribute value.</param>
        private static void setWorkOrderAttribute(DBorderUpdate dbOrderUpdate, string name, string value)
        {
            Attribute attr = new Attribute(name, value);
            int iAttr = dbOrderUpdate.Workorder.Attributes.IndexOf(attr);
            if (iAttr >= 0)
            {
                dbOrderUpdate.Workorder.Attributes[iAttr] = attr;
            }
            else
            {
                dbOrderUpdate.Workorder.Attributes.Add(attr);
            }
        }
        private static void setWorkOrderAttribute(StripMarking stripMarking, string name, string value)
        {
            Attribute attr = new Attribute(name, value);
            int iAttr = stripMarking.Workorder.Attributes.IndexOf(attr);
            if (iAttr >= 0)
            {
                stripMarking.Workorder.Attributes[iAttr] = attr;
            }
            else
            {
                stripMarking.Workorder.Attributes.Add(attr);
            }
        }

        /// <summary>
        /// Gets the inner text of the node with the specified XPath expression in the specified DataProvider result.
        /// </summary>
        /// <param name="xdDataProvider">The XML document containing the DataProvider result.</param>
        /// <param name="xpath">The XPath expression to the XML node.</param>
        private static string getDPNodeValue(XmlDocument xdDataProvider, string xpath)
        {
            XmlNode xnEdField = xdDataProvider.SelectSingleNode(xpath);
            if (xnEdField == null)
            {
                throw new Exception(String.Format("Awacs MES service exception. Unable to retrieve '{0}' from the Insite DataProvider.", xpath));
            }
            return xnEdField.InnerText;
        }

        /// <summary>
        /// Gets the list of wafer IDs from IBIS for the specified workorder id.
        /// </summary>
        /// <param name="traceLog">The TraceLog instance to write the logging to.</param>
        /// <param name="workOrderId">The workorder id for which to get the wafer list.</param>
        /// <returns>The list of wafer IDs.</returns>
        private List<string> getIBISWaferList(TraceLog traceLog, string workOrderId)
        {
            traceLog.EnterMethod(new string[] { "workOrderId" }, workOrderId);
            List<string> waferList = new List<string>();
            using (OracleConnection ibisDbConn = new OracleConnection(ConfigurationManager.ConnectionStrings["IBIS"].ConnectionString))
            {
                ibisDbConn.Open();
                using (OracleCommand cmd = ibisDbConn.CreateCommand())
                {
                    cmd.CommandText = String.Format("SELECT WAFER_OCR_ID FROM IBIS_WAFER_INDEX WHERE WAFER_BATCH_ID='{0}'", workOrderId);
                    using (OracleDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            waferList.Add(reader["WAFER_OCR_ID"].ToString());
                        }
                    }
                }
                ibisDbConn.Close();
            }
            traceLog.ExitMethod(waferList);
            return waferList;
        }

        /// <summary>
        /// Gets the Workstation Equipment Type from local Database. Use Equipment value from AWACS as reference.
        /// </summary>
        /// <param name="wsid"></param>
        /// <returns>String: Equipment Type of the machine</returns>
        /// <exception cref="ArgumentException"></exception>
        public static string getWSType(string wsid)
        {
            if (string.IsNullOrEmpty(wsid))
            {
                throw new ArgumentException("No Work station ID specified.");
            }
            string WSType = "NA";
            using (OracleConnection repDbConn = new OracleConnection(ConfigurationManager.ConnectionStrings["OCAP"].ConnectionString))
                        
            {
                repDbConn.Open();
                using (OracleCommand cmd = repDbConn.CreateCommand())
                {
                    cmd.CommandText = "SELECT TP.WSTYPE FROM AWACSWSTYPE TP  WHERE TP.WSID = '" + wsid + "'";
                    using (OracleDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string result = reader["WSTYPE"].ToString().Trim();
                            if (result.Length > 1)
                                WSType = result.ToUpper();
                        }
                    }
                }
                repDbConn.Close();
            }
            return WSType;      
        }
        public static string OperatorVerify(string OperatorID)
        {
            if (string.IsNullOrEmpty(OperatorID))
            {
                throw new ArgumentException("No OperatorID specified.");
            }
            string result = "INVALID";
            using (SqlConnection repDbConn = new SqlConnection(ConfigurationManager.ConnectionStrings["RECIPE"].ConnectionString))
            {
                repDbConn.Open();
                using (SqlCommand cmd = repDbConn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM CertifiedOperator TP  WHERE TP.Badge = @POperatorID ;";
                    SqlParameter pOperatorID = new SqlParameter("@POperatorID", OperatorID) { SqlDbType = SqlDbType.VarChar };
                    cmd.Parameters.Add(pOperatorID);
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            result = reader["Name"].ToString().Trim();
                        }
                    }
                }
                repDbConn.Close();
            }
            return result;
        }
        public static string WorkstationVerify(string Wsid)
        {
            if (string.IsNullOrEmpty(Wsid))
            {
                throw new ArgumentException("No Workstation specified.");
            }
            string result = "INVALID";
            using (SqlConnection repDbConn = new SqlConnection(ConfigurationManager.ConnectionStrings["RECIPE"].ConnectionString))
            {
                repDbConn.Open();
                using (SqlCommand cmd = repDbConn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM CertifiedBim TP  WHERE TP.Bim = @PWsid ;";
                    SqlParameter pWsid = new SqlParameter("@PWsid", Wsid) { SqlDbType = SqlDbType.VarChar };
                    cmd.Parameters.Add(pWsid);
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            result = reader["BIM"].ToString().Trim();
                        }
                    }
                }
                repDbConn.Close();
            }
            return result;
        }
        public static bool WorkorderVerify(string woid)
        {
            bool result = false;
            using (OracleConnection mesDbcon = new OracleConnection(ConfigurationManager.ConnectionStrings["MES"].ConnectionString))
            {
                mesDbcon.Open();
                
                using (OracleCommand cmd = mesDbcon.CreateCommand())
                {
                    cmd.CommandText = @"SELECT COUNT(*) NUM FROM CONTAINER WHERE CONTAINERNAME = :pWOid";
                    //OracleParameter pContainer = new OracleParameter(":pWOid", woid) { OracleType = OracleType.VarChar };
                    OracleParameter pContainer =
    new OracleParameter(
        "container",
        OracleDbType.Varchar2
    )
    {
        Value = woid
    };
                    cmd.Parameters.Add(pContainer);
                    //cmd.CommandText = string.Format("SELECT COUNT(*) NUM FROM CONTAINER WHERE CONTAINERNAME ='{0}'",woid);
                    using (OracleDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            if (Convert.ToInt32(reader["NUM"].ToString()) > 0)
                                result = true;
                        }
                        else
                            result = false;
                    }
                }
                mesDbcon.Close();                
            }
            return result;        
        }
        public static bool MaterialVerify(string MaterialID, string WSID)
        {
            bool result = false;
            using (SqlConnection repDbConn = new SqlConnection(ConfigurationManager.ConnectionStrings["RECIPE"].ConnectionString))
            {
                repDbConn.Open();
                string WSTYPE = "";
                if (WSID.ToString().Contains("CB") == true) { WSTYPE = "ASMCB"; }
                if (WSID.ToString().Contains("DA") == true) { WSTYPE = "ASMDA"; }
                if (WSID.ToString().Contains("RO") == true) { WSTYPE = "ASMRO"; }

                using (SqlCommand cmd = repDbConn.CreateCommand())
                {
                    cmd.CommandText = string.Format(@"SELECT *
                                            FROM ASMDB asm
                                            WHERE " + "asm." + WSTYPE + " = '{0}'", MaterialID);
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            if (Convert.ToInt32(reader["NO"].ToString()) > 0)
                                result = true;
                        }
                        else
                            result = false;
                    }
                }
                repDbConn.Close();
            }
            return result;
        }
        public static string getCrystalDesc(string woid)
        {
            string result = "";
            using (OracleConnection mesDbcon = new OracleConnection(ConfigurationManager.ConnectionStrings["MES"].ConnectionString))
            {
                mesDbcon.Open();

                using (OracleCommand cmd = mesDbcon.CreateCommand())
                {

//                    cmd.CommandText = @" select distinct so.containername AS ShopOrder, pb.PRODUCTNAME Product12NC, p.description CrystalDesc
//                            from container so, container w, mes_wafersusedbyso wso,product p, productbase pb
//                            where wso.mes_shopordercontainerid(+)= so.containerid
//                            and wso.mes_wafercontainerid = w.containerid(+)
//                            and w.productid=p.productid
//                            and p.productbaseid=pb.productbaseid
//                            and p.productid=pb.REVOFRCDID
//                            and so.containername = :pWOid ";

                    cmd.CommandText = @" select so.containername AS ShopOrder, w.containername AS MESWaferBatch, pb.PRODUCTNAME AS Wafer12NC, p.description CrystalDesc, 
                            w.DIFFUSIONBATCHCODE AS DiffusionBatch
                            from container so, container w, mes_wafersusedbyso wso,product p, productbase pb
                            where wso.mes_shopordercontainerid(+)= so.containerid
                            and wso.mes_wafercontainerid = w.containerid(+)
                            and w.productid=p.productid
                            and p.productbaseid=pb.productbaseid
                            and p.productid=pb.REVOFRCDID
                            -- and so.containername = :pWOid
                            and so.containername = :pWOid
                            order by 1,2 ";



                    OracleParameter pWO =
    new OracleParameter(":pWOid", OracleDbType.Varchar2)
    {
        Value = woid
    };
                    //OracleParameter pWO = new OracleParameter(":pWOid", woid) { OracleType = OracleType.Char };
                    cmd.Parameters.Add(pWO);


                    using (OracleDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
   
                          result = reader["CRYSTALDESC"].ToString(); 

                        }
                        else
                            result = string.Format("CrystalDesc {0} not present in {1}", woid);
                    }
                }
                mesDbcon.Close();
            }

            return result;

        }
        public static string getCrystalSize(string woid)
        {
            string result = "";
            var MySOID = woid.ToString().Split('-');
            string soid = MySOID[0].ToString(); 

            using (OracleConnection mesDbcon = new OracleConnection(ConfigurationManager.ConnectionStrings["MES"].ConnectionString))
            {
                mesDbcon.Open();

                using (OracleCommand cmd = mesDbcon.CreateCommand())
                {

                    cmd.CommandText = @" select so.containername AS ShopOrder, w.containername AS MESWaferBatch, pb.PRODUCTNAME AS Wafer12NC, p.description CrystalDesc, P.CRYSTALXDIMENSION xdim, P.CRYSTALYDIMENSION ydim,
                            w.DIFFUSIONBATCHCODE AS DiffusionBatch
                            from container so, container w, mes_wafersusedbyso wso,product p, productbase pb
                            where wso.mes_shopordercontainerid(+)= so.containerid
                            and wso.mes_wafercontainerid = w.containerid(+)
                            and w.productid=p.productid
                            and p.productbaseid=pb.productbaseid
                            and p.productid=pb.REVOFRCDID
                            -- and so.containername = :pSOid
                            and so.containername = :pSOid
                            order by 1,2 ";




                    //OracleParameter pSO = new OracleParameter(":pSOid", soid) { OracleType = OracleType.Char };
                    OracleParameter pSO =
    new OracleParameter("so", OracleDbType.Varchar2)
    {
        Value = soid
    };
                    cmd.Parameters.Add(pSO);


                    using (OracleDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {

                            result = reader["xdim"].ToString() + " X " + reader["ydim"].ToString();

                        }
                        else
                            result = string.Format("CrystalDesc {0} not present in {1}", woid);
                    }
                }
                mesDbcon.Close();
            }

            return result;

        }
         public static string WaferVerify(string waferid,string woid)
         {
             string result = "";
             using (OracleConnection mesDbcon = new OracleConnection(ConfigurationManager.ConnectionStrings["MES"].ConnectionString))
             {
                 mesDbcon.Open();

                 using (OracleCommand cmd = mesDbcon.CreateCommand())
                 {
                     cmd.CommandText = @" SELECT COUNT(*) NO
                                             FROM CONTAINER w, MES_WAFERSUSEDBYSO wso, CONTAINER WO
                                          WHERE  wso.mes_shopordercontainerid =WO.MES_SHOPORDERCONTAINERID 
                                               AND wso.mes_wafercontainerid = w.containerid
                                               AND WO.containername = :pWOid
                                               AND  W.CONTAINERNAME = :pWaferid ";
                    OracleParameter pWO =
      new OracleParameter(":pWOid", OracleDbType.Varchar2)
      {
          Value = woid
      };
                    //OracleParameter pWO = new OracleParameter(":pWOid", woid) { OracleType = OracleType.Char };
                    //OracleParameter pWafer = new OracleParameter(":pWaferid",waferid){ OracleType = OracleType.Char};
                    OracleParameter pWafer =
   new OracleParameter(":pWaferid", OracleDbType.Varchar2)
   {
       Value = waferid
   };
                    cmd.Parameters.Add(pWO);
                     cmd.Parameters.Add(pWafer);

                     using (OracleDataReader reader = cmd.ExecuteReader())
                     {
                         if (reader.Read())
                         {
                             if (Convert.ToInt32(reader["NO"].ToString()) > 0)
                             { result = ""; }
                             else
                             {
                                 result = string.Format("Wafer {0} not belong to {1}", waferid, woid);
                             }
                         }
                         //else
                         //    result = string.Format("Wafer {0} not belong to {1}",waferid,woid); //lotfi remove
                     }
                 }
                 mesDbcon.Close();
             }

             return result;        
         
         }
         public static string FlagVerify(string soid) 
         {
             string result = "";
             using (OracleConnection mesDbcon = new OracleConnection(ConfigurationManager.ConnectionStrings["MES"].ConnectionString))
             {
                 mesDbcon.Open();

                 using (OracleCommand cmd = mesDbcon.CreateCommand())
                 {
                     cmd.CommandText = @" select  so.containername, NVL (sord.resourcename, '-') BIMLine, 
               (select case when SP.MES_SPECIALINSTRUCTIONNAME = 'Bosch Product' THEN '1' 
                            else ''
                       END 
                from   PRODUCTMES_SPECIALINSTRUCTION ps, MES_SPECIALINSTRUCTION sp, product px,productbase pbx
                where  pb.PRODUCTBASEID = pbx.PRODUCTBASEID
                and    ps.INSTANCEID = px.PRODUCTID
                and    ps.MES_SPECIALINSTRUCTIONID = sp.MES_SPECIALINSTRUCTIONID
                and     px.PRODUCTBASEID = pbx.PRODUCTBASEID
                and    pbx.REVOFRCDID = px.PRODUCTID 
                and    SP.MES_SPECIALINSTRUCTIONNAME = 'Bosch Product'
               ) AS CCBox, pb.PRODUCTNAME, p.DESCRIPTION
from container so,containermes_fginso cf,mes_fginso fis,product p,productbase pb, resourcedef sord
where cf.instanceid = so.containerid
and fis.MES_fgInSoId = cf.MES_FGinSOId
and SO.MES_EQUIPMENTID = SORD.RESOURCEID(+)
and p.PRODUCTBASEID=pb.PRODUCTBASEID
and p.PRODUCTID=fis.MES_FINISHEDGOODID
AND SO.CONTAINERNAME =  :psoid";

                    //OracleParameter pWO = new OracleParameter(":pSOid", soid) { OracleType = OracleType.Char };
                    OracleParameter pWO =
   new OracleParameter(":pSOid", OracleDbType.Varchar2)
   {
       Value = soid
   };
                    cmd.Parameters.Add(pWO);
                     string[] arr;

                     using (OracleDataReader reader = cmd.ExecuteReader())
                     {
                         result = "NONE";
                         if (reader.HasRows)
                         {
                             
                             while (reader.Read())
                             {                       
                                 //int pos = Array.IndexOf(reader["CCBOX"].ToString(), value);
                                 try
                                 {
                                     if (reader["CCBOX"].ToString().Length > 0) {
                                         if (Convert.ToInt32(reader["CCBOX"].ToString()) == 1)
                                         { result = "BOSCH"; }                                                                                                             
                                     }
                                 }
                                 catch
                                 {
                                 
                                 }
                             }
                         }
                     }
                 }
                 mesDbcon.Close();
             }

             return result;

         }
         public static string FlagVerifyBimLine(string soid)
         {
             string result = "";
             using (OracleConnection mesDbcon = new OracleConnection(ConfigurationManager.ConnectionStrings["MES"].ConnectionString))
             {
                 mesDbcon.Open();

                 using (OracleCommand cmd = mesDbcon.CreateCommand())
                 {
                     cmd.CommandText = @" select  so.containername, NVL (sord.resourcename, '-') BIMLine, 
               (select case when SP.MES_SPECIALINSTRUCTIONNAME = 'Bosch Product' THEN '1' 
                            else ''
                       END 
                from   PRODUCTMES_SPECIALINSTRUCTION ps, MES_SPECIALINSTRUCTION sp, product px,productbase pbx
                where  pb.PRODUCTBASEID = pbx.PRODUCTBASEID
                and    ps.INSTANCEID = px.PRODUCTID
                and    ps.MES_SPECIALINSTRUCTIONID = sp.MES_SPECIALINSTRUCTIONID
                and     px.PRODUCTBASEID = pbx.PRODUCTBASEID
                and    pbx.REVOFRCDID = px.PRODUCTID 
                and    SP.MES_SPECIALINSTRUCTIONNAME = 'Bosch Product'
               ) AS CCBox, pb.PRODUCTNAME, p.DESCRIPTION
from container so,containermes_fginso cf,mes_fginso fis,product p,productbase pb, resourcedef sord
where cf.instanceid = so.containerid
and fis.MES_fgInSoId = cf.MES_FGinSOId
and SO.MES_EQUIPMENTID = SORD.RESOURCEID(+)
and p.PRODUCTBASEID=pb.PRODUCTBASEID
and p.PRODUCTID=fis.MES_FINISHEDGOODID
AND SO.CONTAINERNAME =  :psoid";

                    //OracleParameter pWO = new OracleParameter(":pSOid", soid) { OracleType = OracleType.Char };
                    OracleParameter pWO =
   new OracleParameter(":pSOid", OracleDbType.Varchar2)
   {
       Value = soid
   };
                    cmd.Parameters.Add(pWO);
                     using (OracleDataReader reader = cmd.ExecuteReader())
                     {
                         result = "NONE";
                         if (reader.HasRows)
                         {
                             while (reader.Read())
                             {
                                 //int pos = Array.IndexOf(reader["CCBOX"].ToString(), value);
                                 try
                                 {
                                     { result = reader["BIMLINE"].ToString(); }
                                 }
                                 catch
                                 {

                                 }
                             }
                         }
                     }
                 }
                 mesDbcon.Close();
             }

             return result;

         }

         public static String WaferVerifyTATC(string waferid, string woid)
         {
             String result = "NA";
             using (OracleConnection mesDbcon = new OracleConnection(ConfigurationManager.ConnectionStrings["MES"].ConnectionString))
             {
                 mesDbcon.Open();

                 using (OracleCommand cmd = mesDbcon.CreateCommand())
                 {


                     cmd.CommandText = @"SELECT so.containername AS ShopOrder, w.containername AS MESWaferBatch, pb.PRODUCTNAME AS Wafer12NC, p.description CrystalDesc, 
                            w.DIFFUSIONBATCHCODE AS DiffusionBatch
                from container so, container w, mes_wafersusedbyso wso,product p, productbase pb
                where wso.mes_shopordercontainerid(+)= so.containerid
                and wso.mes_wafercontainerid = w.containerid(+)
                and w.productid=p.productid
                and p.productbaseid=pb.productbaseid
                and p.productid=pb.REVOFRCDID
                and so.containername = :pWOid
                AND  W.CONTAINERNAME = :pWaferid";



                    //OracleParameter pWO = new OracleParameter(":pWOid", woid) { OracleType = OracleType.Char };
                    OracleParameter pWO =
   new OracleParameter(":pWOid", OracleDbType.Varchar2)
   {
       Value = woid
   };
                    //OracleParameter pWafer = new OracleParameter(":pWaferid", waferid) { OracleType = OracleType.Char };
                    OracleParameter pWafer =
    new OracleParameter("so", OracleDbType.Varchar2)
    {
        Value = waferid
    };
                    cmd.Parameters.Add(pWO);
                     cmd.Parameters.Add(pWafer);

                     using (OracleDataReader reader = cmd.ExecuteReader())
                     {
                         if (reader.Read())
                         {                             
                                 result = reader["CRYSTALDESC"].ToString();
                         }
                         else
                             result = "NA";
                     }
                 }
                 mesDbcon.Close();
             }

             return result;

         }
         public static Boolean WaferVerifyOCRID(string waferid, string woid)
         {
             Boolean result = false;
             using (OracleConnection mesDbcon = new OracleConnection(ConfigurationManager.ConnectionStrings["MES"].ConnectionString))
             {
                 mesDbcon.Open();

                 using (OracleCommand cmd = mesDbcon.CreateCommand())
                 {
//                     cmd.CommandText = @" SELECT COUNT(*) NO
//                                             FROM CONTAINER w, MES_WAFERSUSEDBYSO wso, CONTAINER WO
//                                          WHERE  wso.mes_shopordercontainerid =WO.MES_SHOPORDERCONTAINERID 
//                                               AND wso.mes_wafercontainerid = w.containerid
//                                               AND WO.containername = :pWOid
//                                               AND  W.CONTAINERNAME = :pWaferid ";

////                     cmd.CommandText = @" SELECT COUNT(*) NO 
////                        from container so, container w, mes_wafersusedbyso wso,product p, productbase pb
////                        where wso.mes_shopordercontainerid(+)= so.containerid
////                        and wso.mes_wafercontainerid = w.containerid(+)
////                        and w.productid=p.productid
////                        and p.productbaseid=pb.productbaseid
////                        and p.productid=pb.REVOFRCDID
////                        and so.containername = :pWOid
////                        AND  W.CONTAINERNAME = :pWaferid";

                     cmd.CommandText = @"SELECT COUNT(*) NO -- so.containername AS ShopOrder, w.containername AS MESWaferBatch, pb.PRODUCTNAME AS Wafer12NC, p.description CrystalDesc, 
from container so, container w, mes_wafersusedbyso wso,product p, productbase pb
where wso.mes_shopordercontainerid(+)= so.containerid
and wso.mes_wafercontainerid = w.containerid(+)
and w.productid=p.productid
and p.productbaseid=pb.productbaseid
and p.productid=pb.REVOFRCDID
and so.containername = :pWOid
AND  W.CONTAINERNAME = :pWaferid";



                    //OracleParameter pWO = new OracleParameter(":pWOid", woid) { OracleType = OracleType.Char };
                    OracleParameter pWO =
   new OracleParameter(":pWOid", OracleDbType.Varchar2)
   {
       Value = woid
   };
                    //OracleParameter pWafer = new OracleParameter(":pWaferid", waferid) { OracleType = OracleType.Char };
                    OracleParameter pWafer =
    new OracleParameter(":pWaferid", OracleDbType.Varchar2)
    {
        Value = waferid
    };
                    cmd.Parameters.Add(pWO);
                     cmd.Parameters.Add(pWafer);

                     using (OracleDataReader reader = cmd.ExecuteReader())
                     {
                         if (reader.Read())
 
                         {
                             if (reader["NO"].ToString() != "0")
                                 result = true;
                         }
                         else
                             result = false;
                     }
                 }
                 mesDbcon.Close();
             }

             return result;

         }
         public static string GetRealWO(string woid,string wsid)
         {
             string realWO = woid;
             string wstype = getWSType(wsid);

             if (woid.Substring(0, 1).ToUpper() == "D")
             {
                 realWO = woid.Substring(1,woid.Length-1);             
             }
             if (wstype == "2DMARKER" || wstype == "ADAT" || wstype == "TRIMFORM" || wstype == "MOULD" || wstype == "DIEBOND" || wstype == "ASMWB" || wstype == "ASMWBM" || wstype == "ASMWBD")
             {
                 //realWO = realWO.Substring(0, realWO.Length - 2) + "10";
                 if (realWO.Substring(0,1) == "%") { realWO = realWO.Substring(2, realWO.Length - 2); }
                 if (realWO.Substring(0,1) == "A") { realWO = realWO.Substring(1, realWO.Length - 1); }  
                // realWO = realWO.Substring(0, realWO.Length - 2);
             }            
             return realWO;
         
         }


       /// <summary>
       /// get leadFrame size and default qty by package and leadframe 12NC
       /// </summary>
       /// <param name="LF12NC"></param>
       /// <param name="package"></param>
       /// <returns></returns>
         private static List<string> getLFAttribute(string LF12NC, string package)
         {
             List<string> lfatrr = new List<string>();
             //using (SqlConnection repDbConn = new SqlConnection(ConfigurationManager.ConnectionStrings["RECIPE"].ConnectionString))
             using (OracleConnection repDbConn = new OracleConnection(ConfigurationManager.ConnectionStrings["OCAP"].ConnectionString))
             {
                 repDbConn.Open();
                 using (OracleCommand cmd = repDbConn.CreateCommand())
                 {
//                     cmd.CommandText =@"SELECT LF.LFSIZE, LF.DEFAULTWOQTY
//                                            FROM AWACSLF lf
//                                            WHERE LF.PACKAGE = :pPkName AND LF.LF12NC = :pLF12NC";
                     //OracleParameter pPackage = new OracleParameter(":pPkName", package) { OracleType = OracleType.Char };
                     //OracleParameter pLF = new OracleParameter("pLF12NC", LF12NC) { OracleType = OracleType.Char };
                     cmd.CommandText = string.Format(@"SELECT LF.LFSIZE, LF.DEFAULTWOQTY
                                            FROM AWACSLF lf
                                            WHERE LF.PACKAGE = '{0}'  AND LF.LF12NC = '{1}'",package,LF12NC);
                     //cmd.Parameters.Add(pPackage);
                     //cmd.Parameters.Add(pLF);
                     using (OracleDataReader reader = cmd.ExecuteReader())
                     {
                         if (reader.Read())
                         {
                             try
                             {
                                 lfatrr.Add(reader["LFSIZE"].ToString().Trim());
                                 lfatrr.Add(reader["DEFAULTWOQTY"].ToString());
                             }
                             catch 
                             { }
                         }
                     }
                 }
                 repDbConn.Close();
             }
             return lfatrr;
         }

        /// <summary>
        /// getLFAttribute override - 12nc, package and device as criteria for getting the correct LFSize
        /// </summary>
        /// <param name="LF12NC"></param>
        /// <param name="package"></param>
        /// <param name="device"></param>
        /// <returns></returns>
        private static List<string> getLFAttribute(string LF12NC, string package, string device)
        {

            //used to handle different device name identified by (VIS), (DMA), etc with same 12NC for Power only
            string[] dev = device.Split(' ');

            List<string> lfatrr = new List<string>();
            using (OracleConnection repDbConn = new OracleConnection(ConfigurationManager.ConnectionStrings["OCAP"].ConnectionString))
            {
                repDbConn.Open();
                using (OracleCommand cmd = repDbConn.CreateCommand())
                {
                    cmd.CommandText = string.Format(@"SELECT LF.LFSIZE, LF.DEFAULTWOQTY
                                            FROM AWACSLF lf
                                            WHERE LF.PACKAGE = '{0}' AND LF.LF12NC = '{1}' AND LF.DEVICE = '{2}'", package, LF12NC, dev[0]);
                    //cmd.Parameters.Add(pPackage);
                    //cmd.Parameters.Add(pLF);
                    using (OracleDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            try
                            {
                                lfatrr.Add(reader["LFSIZE"].ToString().Trim());
                                lfatrr.Add(reader["DEFAULTWOQTY"].ToString());
                            }
                            catch
                            { }
                        }
                    }
                }
                repDbConn.Close();
            }
            return lfatrr;
        }

        /// <summary>
        /// getSOInput = waferQtyIn/numberofDies
        /// </summary>
        /// <param name="so"></param>
        /// <returns></returns>
        private static int getSOInput(string so)
         {

             int request=0;
             using (OracleConnection mesDbcon = new OracleConnection(ConfigurationManager.ConnectionStrings["MES"].ConnectionString))
             {
                 mesDbcon.Open();

                 using (OracleCommand cmd = mesDbcon.CreateCommand())
                 {
                     cmd.CommandText = @" SELECT SUM (WSO.QTYRESERVED / PF.NROFDIES) request
                                              FROM container so, mes_wafersusedbyso wso, productfamily pf
                                             WHERE     SO.CONTAINERID = WSO.MES_SHOPORDERCONTAINERID
                                                   AND SO.PRODUCTFAMILYID = PF.PRODUCTFAMILYID
                                                   AND so.containername = :pSO ";
                    //OracleParameter pSO = new OracleParameter(":pSO", so) { OracleType = OracleType.Char };
                    OracleParameter pSO =
      new OracleParameter(":pSO", OracleDbType.Varchar2)
      {
          Value = so
      };
                    cmd.Parameters.Add(pSO);
                     using (OracleDataReader reader = cmd.ExecuteReader())
                     {
                         if (reader.Read())
                         {
                             if (!string.IsNullOrEmpty(reader["request"].ToString()))
                             request = Convert.ToInt32(reader["request"]);
                         }
                     }
                 }
                 mesDbcon.Close();
                 return request;
             }
         }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="woid"></param>
        /// <param name="defaultqty"></param>
        /// <returns></returns>
         private static int GetRequestedQty(string woid, int defaultqty, string wstype, string package)
         {
            // int lfqty =0;
            // string strSO;
            //// strSO = woid.StartsWith("D") ? woid.Substring(1, woid.Length - 4) : woid.Substring(0, woid.Length - 3); //for APG
            // strSO = woid.StartsWith("D") ? woid.Substring(1, woid.Length - 4) : woid.Substring(0, woid.Length - 3);  //For APM
            // int all = getSOInput(strSO);
            // int reels = all / defaultqty;
            // int WOCurrent = int.Parse(woid.Substring(woid.Length - 2, 1).ToString());
            // char[] bin = woid.Substring(woid.Length-2,2).ToCharArray();
            // int high = converToInt((int)bin[0]);
            // int low = converToInt((int)bin[1]);
            // if (low > 9&& high >9)
            //     lfqty = (9 + 26 * (low - 10) + high - 10) ;
            // else
            //     lfqty = high ;
            // if (lfqty <= reels)
            //     lfqty = defaultqty;
            // if (wstype == "DIEBOND"){ lfqty = defaultqty/2; }

            // else
            //     lfqty = all % defaultqty;
            // if (wstype == "DIEBOND") { lfqty = (all % defaultqty)/2; }

            // return lfqty;    
     
             //APM Calculation

             int lfqty = 0;
             int CurrentQty = 0;
             string strSO;
             strSO = woid.StartsWith("D") ? woid.Substring(1, woid.Length - 4) : woid.Substring(0, woid.Length - 3);  //For APM
             int all = getSOInput(strSO);
             int WOCurrent = int.Parse(woid.Substring(woid.Length - 2, 1).ToString());
             lfqty = all % defaultqty;  //set final value to remainder value
             CurrentQty = defaultqty;
             if (WOCurrent > 1) { CurrentQty = (WOCurrent - 1) * defaultqty; }
             if (all - CurrentQty > lfqty) { lfqty = defaultqty; }
             if ((WOCurrent == 1) && (all > defaultqty))  //ensure first WO will get default value if its is larger
             { lfqty = defaultqty; }
             if (package != "SOT669")
             {
                 if (wstype == "DIEBOND") { lfqty = lfqty / 2; }
                 if (wstype == "MOULD") { lfqty = lfqty * 2; } //temporary. qty x 2
                 if (wstype == "TRIMFORM") { lfqty = lfqty * 2; }  //temporary. qty x 2                          
             }

             return lfqty;   

         }

        private static int converToInt(int number)
        {
           int result = 0;
            if (number  >= 65 && number <= 90)
                {
                    result = number - 55;
                }
            else
                if (number >= 48 && number <= 57)
                {
                    result = number - 48;
                }
            return result;        
        }
         public static string GetCrystalSizeStatus(string soid)
        {

            List<string> crystal = new List<string>();
            List<string> crystaldesc = new List<string>();
            List<string> crystaldescdB = new List<string>();
            List<string> RecipedB = new List<string>();
            string result ="CannotReadXtalSize";

            using (OracleConnection mesDbConn = new OracleConnection(ConfigurationManager.ConnectionStrings["MES"].ConnectionString))
            {

                mesDbConn.Open();
                using (OracleCommand cmd = mesDbConn.CreateCommand())
                {
                    cmd.CommandText = string.Format(@"select so.containername AS ShopOrder, w.containername AS MESWaferBatch, pb.PRODUCTNAME AS Wafer12NC, p.description CrystalDesc, p.CRYSTALXDIMENSION AS dimX, p.CRYSTALYDIMENSION AS dimY,
                            w.DIFFUSIONBATCHCODE AS DiffusionBatch
                            from container so, container w, mes_wafersusedbyso wso,product p, productbase pb
                            where wso.mes_shopordercontainerid(+)= so.containerid
                            and wso.mes_wafercontainerid = w.containerid(+)
                            and w.productid=p.productid
                            and p.productbaseid=pb.productbaseid
                            and p.productid=pb.REVOFRCDID
                            and w.status = '1'
                            and so.status = '1'
                            AND so.CONTAINERNAME =:psoid");

                    //OracleParameter pWO = new OracleParameter(":pSOid", soid) { OracleType = OracleType.Char };
                    OracleParameter pWO =
    new OracleParameter(":pSOid", OracleDbType.Varchar2)
    {
        Value = soid
    };
                    cmd.Parameters.Add(pWO);
                    string dimX = "";
                    string dimY = "";
                    using (OracleDataReader reader = cmd.ExecuteReader())
                     {
    
                         if (reader.HasRows)
                         {

                             while (reader.Read())
                             {
                                 //int pos = Array.IndexOf(reader["CCBOX"].ToString(), value);
                                 try
                                 {
                                     { if ((Convert.ToInt16(reader["dimX"].ToString()) >= 700) || (Convert.ToInt16(reader["dimY"].ToString()) >= 700))   
                                         result = "BIGDIE"; 
                                     else
                                     result = "SMALLDIE";}
                                 }
                                 catch
                                 {

                                 }
                             }
                         }
    
                     }
                 }
                 mesDbConn.Close();
             }
             return result;
            //}
        }
         public int CalcChecksum(String InputStr)
         {
             int Checksum;
             int i;
             Checksum = 0;
             for (i = 1; (i <= InputStr.Length); i++)
             {
                 Checksum = (((8 * Checksum) + (Strings.Asc(InputStr.Substring((i - 1), 1)) - 32)) % 59);
             }
             return Checksum;
         }
         public string GenerateCheckChar(string InputStr)
         {
             int Checksum = 0;
             int Char1;
             int Char2;
             string CheckSumVal = "";

             Checksum = CalcChecksum(InputStr + "A0");
             if (Checksum == 0)
             {
                 CheckSumVal = InputStr + "A0";
             }
             else
             {
                 Checksum = 59 - Checksum;
                 Char2 = 16 + (Checksum & 7);
                 Char1 = 33 + (Checksum & 56) / 8;
                 CheckSumVal = InputStr + (char)(32 + Char1) + (char)(32 + Char2);

             }

             return CheckSumVal;
         }
         //public static bool ContainsAll(this string source, params string[] values)
         //{
         //    return values.All(x => source.Contains(x));
         //}
         public List<string> GetFab(string woid)
         {
             List<string> WaferAttr = new List<string>();

             String result = "NA";
             string so = "";
             string xtal = "";
             string fab = "";
             string wfr = "";

             using (OracleConnection mesDbConn = new OracleConnection(ConfigurationManager.ConnectionStrings["MES"].ConnectionString))
             {

                 mesDbConn.Open();
                 using (OracleCommand cmd = mesDbConn.CreateCommand())
                 {


                     cmd.CommandText = string.Format(@"select so.containername AS ShopOrder, 
 fpb.productname FGRequest12NC, 
 fp.DESCRIPTION Device,
        w.containername AS MESWaferBatch, 
      wso.qtyreserved QTYIN,
       pb.PRODUCTNAME AS Wafer12NC, 
        p.DESCRIPTION CrystalDesc,
        o.mes_waferoriginname Fab,
       so.numberofwafer TOTALWAFER,
  NVL (RD.RESOURCENAME,'-') BIMLine, 
        w.DIFFUSIONBATCHCODE AS DiffusionBatch,
nvl((select distinct si.MES_SPECIALINSTRUCTIONNAME
from vw_mes_containersi si
where si.containername = so.containername
and si.MES_SPECIALINSTRUCTIONNAME = 'Twin Die'),'') SI
from container so, 
     container w, 
     mes_wafersusedbyso wso,
     product p, 
     productbase pb, 
     mes_waferorigin o,
     RESOURCEDEF RD, 
     PRODUCTTYPE pt,
     containermes_fginso cf, 
     mes_fginso mf, 
     product fp, 
     productbase fpb
where wso.mes_shopordercontainerid(+)= so.containerid
and wso.mes_wafercontainerid = w.containerid(+)
and w.productid=p.productid
and p.productbaseid=pb.productbaseid
and p.productid=pb.REVOFRCDID
and o.mes_waferoriginid = p.mes_waferoriginid
and pt.PRODUCTTYPEID = p.PRODUCTTYPEID
and RD.RESOURCEID(+) = SO.MES_EQUIPMENTID
and cf.mes_fginsoid = mf.mes_fginsoid  -- new
and fpb.productbaseid = fp.productbaseid
and fp.productid = mf.MES_FINISHEDGOODID
and cf.instanceid = so.containerid
and so.containername = :pSOid                  
                            ");


                    //OracleParameter pWO = new OracleParameter(":pSOid", woid) { OracleType = OracleType.Char };
                    OracleParameter pWO =
   new OracleParameter("pSOid", OracleDbType.Varchar2)
   {
       Value = woid
   };
                    cmd.Parameters.Add(pWO);
                     using (OracleDataReader reader = cmd.ExecuteReader())
                     {
                         while (reader.Read())
                         {
                             try
                             {
                                 so = reader["SHOPORDER"].ToString();
                                 wfr = reader["MESWAFERBATCH"].ToString();
                                 xtal = reader["CRYSTALDESC"].ToString();
                                 fab = reader["FAB"].ToString();
                                 WaferAttr.Add(so + "," + wfr + "," + xtal + "," + fab);



                             }
                             catch
                             { }
                         }
                     }
                 }
             }

             return WaferAttr;

         } //end sub getfab


        /// <summary>
        /// 
        /// </summary>
        /// <param name="wstype"></param>
        /// <param name="package"></param>
        /// <param name="productdecription"></param>
        /// <param name="lf12nc"></param>
        /// <param name="woid"></param>
        /// <returns>Recipe Name</returns>
        /// <author>Derrick 2018-01-12</author>
         private static string getRecipeNameFA(string wstype, string package, string product, string lf12nc, string woid)
         {
             string recipe = "";

            string[] prod = product.Split(' ');

             using (OracleConnection mesDbConn = new OracleConnection(ConfigurationManager.ConnectionStrings["OCAP"].ConnectionString))
             {
                 mesDbConn.Open();
                 using (OracleCommand cmd = mesDbConn.CreateCommand())
                 {
                     cmd.CommandText = "SELECT recipe FROM AWACSRECIPEBYWSTYPE " + 
                                       "WHERE wstype = '" + wstype + "' AND " + 
                                       "package = '" + package + "' AND " +
                                       "product = '" + prod[0] + "'";

                     if (lf12nc != string.Empty)
                     {
                         cmd.CommandText += " AND leadframe12nc = '" + lf12nc + "'";
                     }

                     using (OracleDataReader reader = cmd.ExecuteReader())
                     {
                         while (reader.Read())
                         {
                             recipe = reader["recipe"].ToString().Trim();
                         }
                     }
                 }
                 mesDbConn.Close();
             }
             return recipe;
        }

         private static string getSawingRecipeNameFA(string wstype, string product, string lf12nc)
         {
             string recipe = "";

             

             using (OracleConnection mesDbConn = new OracleConnection(ConfigurationManager.ConnectionStrings["OCAP"].ConnectionString))
             {
                 mesDbConn.Open();
                 using (OracleCommand cmd = mesDbConn.CreateCommand())
                 {
                     cmd.CommandText = "SELECT recipe FROM AWACSRECIPEBYWSTYPE " +
                                       "WHERE wstype = '" + wstype + "' AND " +
                                       "LEADFRAME12NC = '" + lf12nc + "' AND " +
                                       "product = '" + product + "'";

                    

                     using (OracleDataReader reader = cmd.ExecuteReader())
                     {
                         while (reader.Read())
                         {
                             recipe = reader["recipe"].ToString().Trim();
                         }
                     }
                 }
                 mesDbConn.Close();
             }
             return recipe;
         }

         /// <summary>
         /// True when the workorder is an engineering lot rather than an MES one.
         /// Engineering lots are keyed in on the Engineering page of the ATCB
         /// assembly recipe app and carry an ENG prefix by convention, e.g.
         /// ENGXTA54780B or ENG_V8PCN68.
         /// </summary>
         public static bool IsEngineeringWorkorder(string woid)
         {
             return !string.IsNullOrEmpty(woid)
                 && woid.Trim().StartsWith(EngineeringWoPrefix, StringComparison.OrdinalIgnoreCase);
         }

         /// <summary>
         /// Answers a DBorderUpdate for an engineering lot out of the OCAP
         /// ENGINEERING table.
         ///
         /// ENGINEERING holds one row per engineering lot and one recipe per
         /// group - RECIPESAWING, RECIPEWIREBOND, RECIPEMARKER. Which column a
         /// machine reads is not hard coded here: ENGINEERINGWSTYPE maps a WSTYPE
         /// to its column, so a new machine type is a row in that table rather
         /// than a change to this service.
         /// </summary>
         private static void dbOrderUpdateByEngineering(TraceLog tracelog, DBorderUpdate dbOrderUpdate, string woid, string wsid, string wstype)
         {
             tracelog.EnterMethod(new string[] { "woid", "wsid", "wstype" }, woid, wsid, wstype);

             if (string.IsNullOrEmpty(wstype) || wstype == "NA")
             {
                 dbOrderUpdate.Workorder.Attributes.Add(new Attribute("RESULT", string.Format("WSID:{0} not exist in recipe!", wsid)));
                 return;
             }

             string column = getEngineeringRecipeColumn(wstype);

             if (string.IsNullOrEmpty(column))
             {
                 dbOrderUpdate.Workorder.Attributes.Add(new Attribute("RESULT",
                     string.Format("WSTYPE:{0} has no ENGINEERING recipe column. Map it in ENGINEERINGWSTYPE.", wstype)));
                 return;
             }

             EngineeringLot lot = getEngineeringLot(woid, column);

             if (lot == null)
             {
                 dbOrderUpdate.Workorder.Attributes.Add(new Attribute("RESULT",
                     string.Format("Engineering lot {0} is not in the ENGINEERING table. Add it on the Engineering page first.", woid)));
                 return;
             }

             if (string.IsNullOrEmpty(lot.recipe))
             {
                 dbOrderUpdate.Workorder.Attributes.Add(new Attribute("RESULT",
                     string.Format("Engineering lot {0} has no {1} recipe for WSTYPE:{2}. Fill that cell in on the Engineering page.", woid, column, wstype)));
                 return;
             }

             setWorkOrderAttribute(dbOrderUpdate, "RECIPE", lot.recipe);

             // Package and product are optional on an engineering lot - only sent
             // when the row actually carries them, so an empty cell does not
             // overwrite what the workstation already has.
             if (!string.IsNullOrEmpty(lot.package))
             {
                 setWorkOrderAttribute(dbOrderUpdate, "PACKAGE", lot.package);
             }

             if (!string.IsNullOrEmpty(lot.product))
             {
                 setWorkOrderAttribute(dbOrderUpdate, "PRODUCT", lot.product);
                 // DEVICE is what the sawing and WAOI machines read; it is the
                 // same value, sent under both names as the MES path does.
                 setWorkOrderAttribute(dbOrderUpdate, "DEVICE", lot.product);
             }
         }

         /// <summary>
         /// The ENGINEERING column holding the recipe for a workstation type, from
         /// ENGINEERINGWSTYPE. Empty when that WSTYPE is not mapped.
         ///
         /// ENGINEERING carries one recipe per group - RECIPESAWING,
         /// RECIPEWIREBOND, RECIPEMARKER - and a line has several workstation
         /// types per group, so this is a many-to-one lookup: SAWING and WAOI
         /// both answer RECIPESAWING. Adding a machine type is a row in that
         /// table, not a change here.
         /// </summary>
         private static string getEngineeringRecipeColumn(string wstype)
         {
             string column = "";

             if (string.IsNullOrEmpty(wstype))
             {
                 return column;
             }

             using (OracleConnection ocapDbConn = new OracleConnection(ConfigurationManager.ConnectionStrings["OCAP"].ConnectionString))
             {
                 ocapDbConn.Open();
                 using (OracleCommand cmd = ocapDbConn.CreateCommand())
                 {
                     cmd.BindByName = true;
                     cmd.CommandText = "SELECT column_name FROM engineeringwstype WHERE UPPER(wstype) = UPPER(:wstype)";
                     cmd.Parameters.Add(new OracleParameter("wstype", wstype.Trim()));

                     using (OracleDataReader reader = cmd.ExecuteReader())
                     {
                         while (reader.Read())
                         {
                             column = reader["column_name"].ToString().Trim().ToUpper();
                         }
                     }
                 }
                 ocapDbConn.Close();
             }

             // The name goes into the SELECT below as an identifier, and an
             // identifier cannot be a bind variable. It comes from a table only a
             // DBA can write, but it is still checked: anything that is not a
             // plain Oracle identifier is refused rather than concatenated.
             if (column.Length > 0 && !Regex.IsMatch(column, "^[A-Z][A-Z0-9_]*$"))
             {
                 using (TraceLog traceLog = TraceLog.Create("AwacsMesService.getEngineeringRecipeColumn"))
                 {
                     traceLog.LogException(new InvalidOperationException(
                         string.Format("ENGINEERINGWSTYPE maps WSTYPE {0} to '{1}', which is not a valid column name.", wstype, column)));
                 }

                 return "";
             }

             return column;
         }

         /// <summary>
         /// One ENGINEERING row by lot number, carrying the value of the single
         /// recipe column asked for. Null when the lot is not in the table.
         /// </summary>
         private static EngineeringLot getEngineeringLot(string lotnumber, string recipeColumn)
         {
             if (string.IsNullOrEmpty(lotnumber) || string.IsNullOrEmpty(recipeColumn))
             {
                 return null;
             }

             EngineeringLot retVal = null;

             using (OracleConnection ocapDbConn = new OracleConnection(ConfigurationManager.ConnectionStrings["OCAP"].ConnectionString))
             {
                 ocapDbConn.Open();
                 using (OracleCommand cmd = ocapDbConn.CreateCommand())
                 {
                     cmd.BindByName = true;
                     // PACKAGE is a reserved word in Oracle and has to be quoted.
                     // The recipe column is quoted too: the name is validated in
                     // getEngineeringRecipeColumn, and quoting keeps a column
                     // called NO working the same way.
                     cmd.CommandText =
                         "SELECT lotnumber, requestor, \"PACKAGE\" AS pkg, product, \"" + recipeColumn + "\" AS recipe " +
                         "FROM engineering " +
                         "WHERE UPPER(lotnumber) = UPPER(:lotnumber)";
                     cmd.Parameters.Add(new OracleParameter("lotnumber", lotnumber.Trim()));

                     using (OracleDataReader reader = cmd.ExecuteReader())
                     {
                         while (reader.Read())
                         {
                             retVal = new EngineeringLot();
                             retVal.lotnumber = reader["lotnumber"] == DBNull.Value ? "" : reader["lotnumber"].ToString().Trim();
                             retVal.requestor = reader["requestor"] == DBNull.Value ? "" : reader["requestor"].ToString().Trim();
                             retVal.package = reader["pkg"] == DBNull.Value ? "" : reader["pkg"].ToString().Trim();
                             retVal.product = reader["product"] == DBNull.Value ? "" : reader["product"].ToString().Trim();
                             retVal.recipe = reader["recipe"] == DBNull.Value ? "" : reader["recipe"].ToString().Trim();
                             retVal.recipeColumn = recipeColumn;
                         }
                     }
                 }
                 ocapDbConn.Close();
             }

             return retVal;
         }

        private static void GetLotDetailsFromRms(string lotId, out string device, out string crystal12nc)
        {
            device = "";
            crystal12nc = "";

            string raw;

            using (var svc = new phcab01vww01.Ad_HocTxn())
            {
                XmlNode node = svc.GetQueryResult("LotDetails_RMS", "LOTID", lotId);
                if (node == null)
                    return;
                raw = node.OuterXml;
            }

            if (string.IsNullOrEmpty(raw))
                return;

            XDocument doc = XDocument.Parse(raw);

            // unwrap if the proxy returns the payload as an escaped string
            if (doc.Root != null && doc.Root.Name.LocalName == "string")
            {
                string inner = doc.Root.Value;
                if (string.IsNullOrEmpty(inner))
                    return;
                doc = XDocument.Parse(inner);
            }

            XElement code = doc.Descendants("Code").FirstOrDefault();
            if (code != null && code.Value.Trim() != "0")
                return;

            XElement lot = doc.Descendants("LotDetails_RMS").FirstOrDefault();
            if (lot == null)
                return;

            device = ((string)lot.Element("DEVICE") ?? "").Trim();
            crystal12nc = ((string)lot.Element("CRYSTAL12NC") ?? "").Trim();
        }

        private static FAMESInfo getFAMESInfo(string woid, string dbSrc="MES")
         {
             try
             {
                 FAMESInfo retVal = new FAMESInfo();

                 phcab01vww01.Ad_HocTxn tmpVal = new phcab01vww01.Ad_HocTxn();
                 System.Xml.XmlNode xmlDoc = tmpVal.GetQueryResult("LotDetails_RMS", "LOTID", woid);

                 if (xmlDoc.SelectSingleNode(".//LotDetails_RMS") != null)
                 {
                     retVal.nc12 = xmlDoc.SelectSingleNode(".//DEVICE12NC").InnerText;
                     retVal.package = xmlDoc.SelectSingleNode(".//PACKAGE").InnerText;
                     retVal.product = xmlDoc.SelectSingleNode(".//DEVICE").InnerText;
                     retVal.woid = xmlDoc.SelectSingleNode(".//CONTAINERNAME").InnerText;
                     retVal.Crystal12NC = xmlDoc.SelectSingleNode(".//CRYSTAL12NC").InnerText;
                 }
                 else
                 {
                    return null;
                 } 
                 return retVal;
             }
             catch (Exception ex)
             {
                 return null;
             }
         }

        private static string getFAPackageName(string strDevice, string strMESPackage){

            string retVal = string.Empty;
            using (OracleConnection ocapdbConn = new OracleConnection(ConfigurationManager.ConnectionStrings["OCAP"].ConnectionString))
            {
                 ocapdbConn.Open();
                 using (OracleCommand cmd = ocapdbConn.CreateCommand())
                 {
                     cmd.CommandText = "select mainpackage from awacspackage_master where device = '" + strDevice + "' and subpackage = '" + strMESPackage + "'";
                     using (OracleDataReader reader = cmd.ExecuteReader())
                     {
                         while (reader.Read())
                         {
                             retVal = reader["mainpackage"].ToString();
                         }
                     }
                 }
                 ocapdbConn.Close();
            }
            return retVal;
        }

        //private static List
    }
}
