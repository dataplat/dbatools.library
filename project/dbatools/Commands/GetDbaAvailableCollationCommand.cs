using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Management.Automation;
using System.Text;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Retrieves all available collations from SQL Server instances with
    /// detailed locale and code page information.
    /// </summary>
    [Cmdlet("Get", "DbaAvailableCollation")]
    public class GetDbaAvailableCollationCommand : DbaInstanceCmdlet
    {
        /// <summary>
        /// Default display properties for the output objects.
        /// </summary>
        private static readonly string[] DefaultDisplayProperties = new string[]
        {
            "ComputerName", "InstanceName", "SqlInstance", "Name", "CodePage",
            "CodePageName", "LocaleID", "LocaleName", "Description"
        };

        /// <summary>
        /// Cache for locale descriptions to avoid repeated lookups.
        /// </summary>
        private Dictionary<int, string> _localeCache;

        /// <summary>
        /// Cache for code page descriptions to avoid repeated lookups.
        /// </summary>
        private Dictionary<int, string> _codePageCache;

        /// <summary>
        /// Initializes caches for locale and code page descriptions.
        /// </summary>
        protected override void BeginProcessing()
        {
            base.BeginProcessing();

            _localeCache = new Dictionary<int, string>();
            // No longer supported by Windows, but still shows up in SQL Server
            _localeCache[66577] = "Japanese_Unicode";

            _codePageCache = new Dictionary<int, string>();

#if !NETFRAMEWORK
            // On .NET Core/8+, non-Unicode code pages (1252, 932, etc.) require
            // explicit registration of the CodePagesEncodingProvider.
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
#endif
        }

        /// <summary>
        /// Connects to each SQL Server instance, enumerates collations,
        /// and outputs them with added locale and code page descriptions.
        /// </summary>
        protected override void ProcessRecord()
        {
            foreach (DbaInstanceParameter instance in SqlInstance)
            {
                object server;
                try
                {
                    server = ConnectInstance(instance);
                    if (server == null)
                    {
                        StopFunction(
                            "Failure",
                            target: instance,
                            isContinue: true,
                            category: ErrorCategory.ConnectionError);
                        TestFunctionInterrupt();
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    StopFunction(
                        "Failure",
                        errorRecord: new ErrorRecord(ex, "GetDbaAvailableCollation_ConnectionError", ErrorCategory.ConnectionError, instance),
                        target: instance,
                        isContinue: true,
                        category: ErrorCategory.ConnectionError);
                    TestFunctionInterrupt();
                    continue;
                }

                // Get server connection info
                string computerName = GetServerPropertySafe(server, "ComputerName");
                string serviceName = GetServerPropertySafe(server, "ServiceName");
                string domainInstanceName = GetServerPropertySafe(server, "DomainInstanceName");

                // Enumerate collations
                Collection<PSObject> collations;
                try
                {
                    collations = EnumCollations(server);
                }
                catch (Exception ex)
                {
                    StopFunction(
                        String.Format("Failed to enumerate collations from {0}", instance),
                        exception: ex,
                        target: instance,
                        isContinue: true);
                    TestFunctionInterrupt();
                    continue;
                }

                if (collations == null || collations.Count == 0)
                    continue;

                foreach (PSObject collation in collations)
                {
                    if (collation == null)
                        continue;

                    int codePage = GetIntProperty(collation, "CodePage");
                    int localeId = GetIntProperty(collation, "LocaleID");

                    AddOrSetProperty(collation, "ComputerName", computerName);
                    AddOrSetProperty(collation, "InstanceName", serviceName);
                    AddOrSetProperty(collation, "SqlInstance", domainInstanceName);
                    AddOrSetProperty(collation, "CodePageName", GetCodePageDescription(codePage));
                    AddOrSetProperty(collation, "LocaleName", GetLocaleDescription(localeId));

                    SetDefaultDisplayPropertySet(collation, DefaultDisplayProperties);

                    WriteObject(collation);
                }
            }
        }

        #region Helpers

        /// <summary>
        /// Connects to a SQL Server instance via Connect-DbaInstance.
        /// </summary>
        private object ConnectInstance(DbaInstanceParameter instance)
        {
            string script;
            object[] args;
            if (SqlCredential != null)
            {
                script = "param($i, $c) Connect-DbaInstance -SqlInstance $i -SqlCredential $c";
                args = new object[] { instance, SqlCredential };
            }
            else
            {
                script = "param($i) Connect-DbaInstance -SqlInstance $i";
                args = new object[] { instance };
            }

            Collection<PSObject> results = InvokeCommand.InvokeScript(true, ScriptBlock.Create(script), null, args);
            if (results != null && results.Count > 0)
                return results[0].BaseObject;
            return null;
        }

        /// <summary>
        /// Calls EnumCollations() on the server object.
        /// </summary>
        private Collection<PSObject> EnumCollations(object server)
        {
            string script = "param($s) $s.EnumCollations()";
            return InvokeCommand.InvokeScript(true, ScriptBlock.Create(script), null, new object[] { server });
        }

        /// <summary>
        /// Gets a string property from a server object safely.
        /// </summary>
        internal static string GetServerPropertySafe(object server, string propertyName)
        {
            if (server == null)
                return null;
            try
            {
                PSObject pso = PSObject.AsPSObject(server);
                PSPropertyInfo prop = pso.Properties[propertyName];
                if (prop != null && prop.Value != null)
                    return prop.Value.ToString();
            }
            catch (Exception)
            {
                // Property may not exist
            }
            return null;
        }

        /// <summary>
        /// Gets an integer property from a PSObject.
        /// </summary>
        internal static int GetIntProperty(PSObject obj, string propertyName)
        {
            if (obj == null)
                return 0;
            try
            {
                PSPropertyInfo prop = obj.Properties[propertyName];
                if (prop != null && prop.Value != null)
                {
                    if (prop.Value is int intVal)
                        return intVal;
                    int parsed;
                    if (Int32.TryParse(prop.Value.ToString(), out parsed))
                        return parsed;
                }
            }
            catch (Exception)
            {
                // Ignore
            }
            return 0;
        }

        /// <summary>
        /// Gets the locale description for a given locale ID.
        /// </summary>
        internal string GetLocaleDescription(int localeId)
        {
            string result;
            if (_localeCache.TryGetValue(localeId, out result))
                return result;

            try
            {
                CultureInfo culture = CultureInfo.GetCultureInfo(localeId);
                result = culture.DisplayName;
            }
            catch (Exception)
            {
                result = null;
            }

            _localeCache[localeId] = result;
            return result;
        }

        /// <summary>
        /// Gets the code page encoding name for a given code page ID.
        /// </summary>
        internal string GetCodePageDescription(int codePageId)
        {
            string result;
            if (_codePageCache.TryGetValue(codePageId, out result))
                return result;

            try
            {
                Encoding encoding = Encoding.GetEncoding(codePageId);
                result = encoding.EncodingName;
            }
            catch (Exception)
            {
                result = null;
            }

            _codePageCache[codePageId] = result;
            return result;
        }

        /// <summary>
        /// Adds or updates a NoteProperty on a PSObject.
        /// </summary>
        internal static void AddOrSetProperty(PSObject obj, string name, object value)
        {
            if (obj == null)
                return;
            try
            {
                PSPropertyInfo existing = obj.Properties[name];
                if (existing != null)
                {
                    existing.Value = value;
                }
                else
                {
                    obj.Properties.Add(new PSNoteProperty(name, value));
                }
            }
            catch (Exception)
            {
                try
                {
                    obj.Properties.Remove(name);
                    obj.Properties.Add(new PSNoteProperty(name, value));
                }
                catch (Exception)
                {
                    // Best-effort
                }
            }
        }

        /// <summary>
        /// Sets the DefaultDisplayPropertySet on a PSObject.
        /// </summary>
        internal static void SetDefaultDisplayPropertySet(PSObject obj, string[] properties)
        {
            if (obj == null || properties == null)
                return;

            try { obj.Members.Remove("PSStandardMembers"); }
            catch (Exception) { /* May not exist yet */ }

            try
            {
                obj.Members.Add(new PSMemberSet("PSStandardMembers", new PSMemberInfo[]
                {
                    new PSPropertySet("DefaultDisplayPropertySet", properties)
                }));
            }
            catch (Exception)
            {
                // Best-effort
            }
        }

        #endregion Helpers
    }
}
