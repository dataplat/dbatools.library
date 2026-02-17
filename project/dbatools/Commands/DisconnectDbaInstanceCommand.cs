using System;
using System.Collections;
using System.Collections.Generic;
using System.Management.Automation;
using Dataplat.Dbatools.Connection;
using Dataplat.Dbatools.Internal;
using Dataplat.Dbatools.Message;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Closes active SQL Server connections and removes them from the dbatools connection cache.
    /// Properly closes SQL Server connections created by dbatools commands like Connect-DbaInstance,
    /// preventing connection leaks and freeing up server connection limits.
    /// </summary>
    [Cmdlet("Disconnect", "DbaInstance", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Low)]
    [OutputType(typeof(PSObject))]
    public class DisconnectDbaInstanceCommand : DbaBaseCmdlet
    {
        private static readonly string[] _defaultProperties = new string[]
        {
            "SqlInstance",
            "ConnectionType",
            "State"
        };

        /// <summary>
        /// Specifies the SQL Server connection object(s) to disconnect, such as SMO Server objects
        /// or SqlConnection objects from Connect-DbaInstance. Accepts pipeline input from
        /// Get-DbaConnectedInstance to disconnect multiple connections at once.
        /// </summary>
        [Parameter(ValueFromPipeline = true)]
        public PSObject[] InputObject { get; set; }

        /// <summary>
        /// Accumulates pipeline input across ProcessRecord calls to avoid enumeration problems.
        /// Uses List instead of PS1's $objects += $InputObject pattern to avoid O(n^2) array reallocation.
        /// </summary>
        private List<PSObject> _collectedInput;

        /// <summary>
        /// Initializes the input collection for this pipeline invocation.
        /// </summary>
        protected override void BeginProcessing()
        {
            base.BeginProcessing();
            _collectedInput = new List<PSObject>();
        }

        /// <summary>
        /// Accumulates pipeline input to handle piped collections correctly.
        /// </summary>
        protected override void ProcessRecord()
        {
            if (InputObject != null)
            {
                foreach (PSObject item in InputObject)
                {
                    _collectedInput.Add(item);
                }
            }
        }

        /// <summary>
        /// Processes all collected input objects and disconnects them.
        /// </summary>
        protected override void EndProcessing()
        {
            foreach (PSObject inputObj in _collectedInput)
            {
                if (inputObj == null)
                    continue;

                // Unwrap ConnectionObject property if present (from Get-DbaConnectedInstance output)
                object[] servers = UnwrapConnectionObjects(inputObj);

                foreach (object server in servers)
                {
                    if (server == null)
                        continue;

                    try
                    {
                        // Unwrap PSObject wrapper if present
                        object baseServer = server;
                        if (server is PSObject psWrapped && psWrapped.BaseObject != null)
                            baseServer = psWrapped.BaseObject;

                        bool handled = false;

                        // Handle SMO Server objects (have ConnectionContext property)
                        object connectionContext = GetPropertyValue(baseServer, "ConnectionContext");
                        if (connectionContext != null)
                        {
                            handled = true;
                            string serverName = GetPropertyValue(baseServer, "Name") as string ?? baseServer.ToString();

                            if (ShouldProcess(serverName, "Disconnecting SQL Connection"))
                            {
                                // Call ConnectionContext.Disconnect()
                                InvokeMethod(connectionContext, "Disconnect");

                                // Remove from connection hash (only if present, matching PS1 behavior)
                                string connString = GetPropertyValue(connectionContext, "ConnectionString") as string;
                                if (!String.IsNullOrEmpty(connString) && ConnectionHost.ConnectionHash.ContainsKey(connString))
                                {
                                    WriteMessageVerbose("removing from connection hash");
                                    RemoveFromConnectionHash(connString);
                                }

                                // Build output object
                                PSObject result = new PSObject();
                                result.Properties.Add(new PSNoteProperty("SqlInstance", serverName));
                                result.Properties.Add(new PSNoteProperty("ConnectionString", ConversionHelpers.HideConnectionString(connString)));
                                result.Properties.Add(new PSNoteProperty("ConnectionType", baseServer.GetType().FullName));
                                result.Properties.Add(new PSNoteProperty("State", "Disconnected"));

                                SystemHelpers.SelectDefaultView(result, _defaultProperties);
                                WriteObject(result);
                            }
                        }

                        // Handle SqlConnection objects
                        if (!handled && baseServer.GetType().Name == "SqlConnection")
                        {
                            handled = true;
                            string dataSource = GetPropertyValue(baseServer, "DataSource") as string ?? baseServer.ToString();

                            if (ShouldProcess(dataSource, "Closing SQL Connection"))
                            {
                                // Close if open
                                object stateObj = GetPropertyValue(baseServer, "State");
                                if (stateObj != null && stateObj.ToString() == "Open")
                                {
                                    InvokeMethod(baseServer, "Close");
                                }

                                // Remove from connection hash (only if present, matching PS1 behavior)
                                string connString = GetPropertyValue(baseServer, "ConnectionString") as string;
                                if (!String.IsNullOrEmpty(connString) && ConnectionHost.ConnectionHash.ContainsKey(connString))
                                {
                                    WriteMessageVerbose("removing from connection hash");
                                    RemoveFromConnectionHash(connString);
                                }

                                // Get state after closing
                                object newState = GetPropertyValue(baseServer, "State");

                                // Build output object
                                PSObject result = new PSObject();
                                result.Properties.Add(new PSNoteProperty("SqlInstance", dataSource));
                                result.Properties.Add(new PSNoteProperty("ConnectionString", ConversionHelpers.HideConnectionString(connString)));
                                result.Properties.Add(new PSNoteProperty("ConnectionType", baseServer.GetType().FullName));
                                result.Properties.Add(new PSNoteProperty("State", newState != null ? newState.ToString() : "Closed"));

                                SystemHelpers.SelectDefaultView(result, _defaultProperties);
                                WriteObject(result);
                            }
                        }

                        if (!handled)
                        {
                            WriteMessageWarning(String.Format("Cannot disconnect object of type '{0}'. Pass an SMO Server object from Connect-DbaInstance or a SqlConnection. Use Get-DbaConnectedInstance to see available connections.", baseServer.GetType().FullName));
                        }
                    }
                    catch (Exception ex)
                    {
                        StopFunction(String.Format("Failed to disconnect {0}", inputObj), ex, target: inputObj, isContinue: true);
                        TestFunctionInterrupt();
                        continue;
                    }
                }
            }
        }

        /// <summary>
        /// Unwraps ConnectionObject property from Get-DbaConnectedInstance output,
        /// or returns the input object as-is. Handles arrays stored in ConnectionObject,
        /// including PSObject-wrapped arrays.
        /// </summary>
        internal static object[] UnwrapConnectionObjects(PSObject inputObj)
        {
            if (inputObj == null)
                return new object[0];

            // Check for ConnectionObject property (from Get-DbaConnectedInstance output)
            PSPropertyInfo connObjProp = inputObj.Properties["ConnectionObject"];
            if (connObjProp != null && connObjProp.Value != null)
            {
                object connObj = connObjProp.Value;

                // Unwrap PSObject wrapper around the value (can happen after pipeline serialization)
                if (connObj is PSObject psWrappedConn && psWrappedConn.BaseObject != null)
                    connObj = psWrappedConn.BaseObject;

                // ConnectionObject may be an array
                if (connObj is object[] arr)
                    return arr;
                if (connObj is IList list)
                {
                    object[] result = new object[list.Count];
                    list.CopyTo(result, 0);
                    return result;
                }
                return new object[] { connObj };
            }

            // No ConnectionObject property — treat the object itself as the server
            object baseObj = inputObj.BaseObject != null ? inputObj.BaseObject : inputObj;
            return new object[] { baseObj };
        }

        /// <summary>
        /// Removes a connection string key from the ConnectionHash.
        /// ConnectionHash is Hashtable.Synchronized; Remove is a no-op if the key is absent.
        /// </summary>
        private static void RemoveFromConnectionHash(string connectionString)
        {
            if (String.IsNullOrEmpty(connectionString))
                return;

            ConnectionHost.ConnectionHash.Remove(connectionString);
        }

        /// <summary>
        /// Gets a property value from an object using reflection, handling both
        /// CLR properties and PSObject note properties.
        /// </summary>
        private static object GetPropertyValue(object obj, string propertyName)
        {
            if (obj == null)
                return null;

            // Unwrap PSObject
            object baseObj = obj;
            if (obj is PSObject psWrapper && psWrapper.BaseObject != null)
                baseObj = psWrapper.BaseObject;

            // Try CLR reflection first
            try
            {
                var prop = baseObj.GetType().GetProperty(propertyName);
                if (prop != null)
                    return prop.GetValue(baseObj);
            }
            catch (System.Reflection.TargetInvocationException) { }
            catch (System.Reflection.AmbiguousMatchException) { }
            catch (MemberAccessException) { }
            catch (InvalidOperationException) { }

            // Try PSObject properties
            try
            {
                PSObject psObj = PSObject.AsPSObject(obj);
                PSPropertyInfo psProp = psObj.Properties[propertyName];
                if (psProp != null)
                    return psProp.Value;
            }
            catch (ExtendedTypeSystemException) { }
            catch (InvalidOperationException) { }

            return null;
        }

        /// <summary>
        /// Invokes a parameterless method on an object using reflection.
        /// </summary>
        private static void InvokeMethod(object obj, string methodName)
        {
            if (obj == null)
                return;

            // Unwrap PSObject
            object baseObj = obj;
            if (obj is PSObject psWrapper && psWrapper.BaseObject != null)
                baseObj = psWrapper.BaseObject;

            var method = baseObj.GetType().GetMethod(methodName, Type.EmptyTypes);
            if (method != null)
                method.Invoke(baseObj, null);
        }
    }
}
