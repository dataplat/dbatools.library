using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Connection;
using Dataplat.Dbatools.Internal;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Returns SQL Server instances currently cached in the dbatools connection pool.
    /// </summary>
    [Cmdlet("Get", "DbaConnectedInstance")]
    public class GetDbaConnectedInstanceCommand : DbaBaseCmdlet
    {
        private static readonly string[] _defaultProperties = new string[]
        {
            "SqlInstance",
            "ConnectionType",
            "ConnectionObject",
            "Pooled"
        };

        /// <summary>
        /// Iterates through the connection hash and outputs connection details.
        /// </summary>
        protected override void ProcessRecord()
        {
            // Snapshot the keys to avoid collection-modified-during-enumeration
            object[] keys;
            lock (ConnectionHost.ConnectionHash.SyncRoot)
            {
                keys = new object[ConnectionHost.ConnectionHash.Keys.Count];
                ConnectionHost.ConnectionHash.Keys.CopyTo(keys, 0);
            }

            foreach (object keyObj in keys)
            {
                string key = keyObj as string;
                if (key == null)
                    continue;

                object rawValue = ConnectionHost.ConnectionHash[key];
                if (rawValue == null)
                    continue;

                // The value is stored as an array (object[]) from PS1
                object firstValue = null;
                object connectionObject = rawValue;

                if (rawValue is object[] arr && arr.Length > 0)
                {
                    firstValue = arr[0];
                }
                else if (rawValue is IList list && list.Count > 0)
                {
                    firstValue = list[0];
                }
                else
                {
                    // Single object, not wrapped in array
                    firstValue = rawValue;
                }

                if (firstValue == null)
                    continue;

                // Determine instance name: prefer DataSource, fall back to Name
                string instance = GetPropertyValue(firstValue, "DataSource") as string;
                if (String.IsNullOrEmpty(instance))
                {
                    instance = GetPropertyValue(firstValue, "Name") as string;
                }

                // Determine pooling status
                bool pooled = true;
                object connectionContext = GetPropertyValue(firstValue, "ConnectionContext");
                if (connectionContext != null)
                {
                    object nonPooled = GetPropertyValue(connectionContext, "NonPooledConnection");
                    if (nonPooled is bool npBool && npBool)
                        pooled = false;
                }
                if (pooled)
                {
                    object nonPooledDirect = GetPropertyValue(firstValue, "NonPooledConnection");
                    if (nonPooledDirect is bool npDirectBool && npDirectBool)
                        pooled = false;
                }

                // Unwrap PSObject to get the base .NET type (mirrors PS1 .GetType() behavior)
                object baseValue = firstValue;
                if (firstValue is PSObject psWrapped && psWrapped.BaseObject != null)
                    baseValue = psWrapped.BaseObject;

                // Build the output object
                PSObject result = new PSObject();
                result.Properties.Add(new PSNoteProperty("SqlInstance", instance ?? key));
                result.Properties.Add(new PSNoteProperty("ConnectionObject", connectionObject));
                result.Properties.Add(new PSNoteProperty("ConnectionType", baseValue.GetType().FullName));
                result.Properties.Add(new PSNoteProperty("Pooled", pooled));
                result.Properties.Add(new PSNoteProperty("ConnectionString", ConversionHelpers.HideConnectionString(key)));

                SystemHelpers.SelectDefaultView(result, _defaultProperties);
                WriteObject(result);
            }
        }

        /// <summary>
        /// Gets a property value from an object using reflection, handling both
        /// CLR properties and PSObject note properties. Unwraps PSObject wrappers
        /// to match PowerShell member access behavior.
        /// </summary>
        private static object GetPropertyValue(object obj, string propertyName)
        {
            if (obj == null)
                return null;

            // Unwrap PSObject to get the base object (mirrors PS1 member access)
            object baseObj = obj;
            if (obj is PSObject psWrapper && psWrapper.BaseObject != null)
                baseObj = psWrapper.BaseObject;

            // Try CLR reflection on the base object first
            try
            {
                var prop = baseObj.GetType().GetProperty(propertyName);
                if (prop != null)
                    return prop.GetValue(baseObj);
            }
            catch
            {
                // Reflection may fail for some types
            }

            // Try PSObject properties (note properties, script properties, etc.)
            try
            {
                PSObject psObj = PSObject.AsPSObject(obj);
                PSPropertyInfo psProp = psObj.Properties[propertyName];
                if (psProp != null)
                    return psProp.Value;
            }
            catch
            {
                // Property access may fail
            }

            return null;
        }
    }
}
