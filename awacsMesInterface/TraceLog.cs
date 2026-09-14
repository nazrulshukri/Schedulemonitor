//------------------------------------------------------------------------------
// <copyright file="TraceLog.cs" company="NXP Semiconductors">
// Copyright (C) NXP Semiconductors 2013. All rights reserved.
// </copyright>
// <project>Awacs MES interface.</project>
// <author>Marc Klerx</author>
// <email>marc.klerx@nxp.com</email>
// <date>2013-05-06</date>
// <summary>Utility class for tracing.</summary>
//
// $URL: $
// $Rev: $
// $Author: marc $
// $Date: 2014-07-29 18:00:25 +0700 (Tue, 29 Jul 2014) $
//
//------------------------------------------------------------------------------
using System;
using System.Configuration;

namespace EWFM.AwacsMes.Diagnostics
{
    /// <summary>
    /// Defines the trace levels.
    /// </summary>
    public enum TraceLevel
    {
        /// <summary>
        /// All traces will be logged.
        /// </summary>
        Information = 0,
       
        /// <summary>
        /// Warnings and errors will be logged.
        /// </summary>
        Warning = 1,

        /// <summary>
        /// Only errors will be logged.
        /// </summary>
        Error = 2
    }

    /// <summary>
    /// Represents a trace log.
    /// </summary>
    public class TraceLog : IDisposable
    {
        // The next variables are static and apply to all trace logs
        private static bool _enabled;
        private static string _name;
        private static string _machine;
        private static bool _writeToFile;
        private static TraceLevel _traceLevel;

        /// <summary>
        /// Sets whether writing to the trace log is enabled.
        /// </summary>
        public static bool Enabled
        {
            set { _enabled = value; }
        }

        /// <summary>
        /// Sets the name of the event log to which the trace log will be written.
        /// </summary>
        public static string Name
        {
            set { _name = value; }
        }

        /// <summary>
        /// Sets the machine of the event log to which the trace log will be written.
        /// </summary>
        public static string Machine
        {
            set { _machine = value; }
        }

        /// <summary>
        /// Sets whether trace log should be written to a file.
        /// </summary>
        public static bool WriteToFile
        {
            set { _writeToFile = value; }
        }

        /// <summary>
        /// Sets the trace level.
        /// </summary>
        public static TraceLevel Level
        {
            set { _traceLevel = value; }
        }

        /// <summary>
        /// Initializes the static TraceLog settings.
        /// </summary>
        public static void Initialize()
        {
            TraceLog.Enabled = Convert.ToBoolean(ConfigurationManager.AppSettings["traceLogEnabled"]);
            TraceLog.Name = ConfigurationManager.AppSettings["traceLogName"];
            TraceLog.Machine = ConfigurationManager.AppSettings["traceLogMachine"];
            TraceLog.WriteToFile = Convert.ToBoolean(ConfigurationManager.AppSettings["traceLogWriteToFile"]);
            try
            {
                TraceLog.Level = (TraceLevel)Enum.Parse(typeof(TraceLevel), ConfigurationManager.AppSettings["traceLogLevel"]);
            }
            catch
            {
                TraceLog.Level = TraceLevel.Information;
            }
        }

        /// <summary>
        /// Creates a new TraceLog for the specified source.
        /// </summary>
        /// <param name="source">The source for which to create the trace log.</param>
        /// <returns>The TraceLog instance.</returns>
        public static TraceLog Create(string source)
        {
            return new TraceLog(source);
        }

        /// <summary>
        /// Initializes a new instance of TraceLog for the specified source and event id.
        /// </summary>
        /// <param name="source">The source for which to create the trace log.</param>
        private TraceLog(string source)
        {
        }

        /// <summary>
        /// Initializes a new instance of TraceLog without associating any source with it.
        /// </summary>
        public TraceLog()
        {
        }

        /// <summary>
        /// Closes the TraceLog and writes all entries to the event log or file.
        /// </summary>
        public void Close()
        {
            this.Dispose();
        }

        /// <summary>
        /// Releases all resources used by the TraceLog instance.
        /// </summary>
        public void Dispose()
        {
        }

        /// <summary>;
        /// Writes an entry to the trace log.
        /// </summary>
        /// <param name="entry">The entry to write to the log.</param>
        public void WriteEntry(string entry)
        {
        }

        /// <summary>;
        /// Logs an exception to the trace log.
        /// </summary>
        /// <param name="exception">The exception to log.</param>
        public void LogException(Exception exception)
        {
        }

        /// <summary>
        /// Adds an entry to the trace log when entering a method.
        /// </summary>
        /// <param name="values">The list of parameter values.</param>
        public void EnterMethod(params object[] values)
        {
        }

        /// <summary>
        /// Adds an entry to the trace log when entering a method.
        /// </summary>
        /// <param name="parameters">The list of parameter names.</param>
        /// <param name="values">The list of parameter values.</param>
        public void EnterMethod(string[] parameters, params object[] values)
        {
        }

        /// <summary>
        /// Adds an entry to the trace log when leaving a method.
        /// </summary>
        /// <param name="result">The result of the method.</param>
        public void ExitMethod(object result)
        {
        }
    }
}
