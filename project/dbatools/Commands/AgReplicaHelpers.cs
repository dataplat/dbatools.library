using System;
using System.Collections.Generic;
using System.Management.Automation;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Shared utility methods and script blocks used by the Compare-DbaAgReplica* command family.
    /// </summary>
    internal static class AgReplicaHelpers
    {
        /// <summary>
        /// Connects to an instance, validates HADR is enabled, and returns availability group
        /// information including replica names. Throws sentinel strings for known error conditions.
        /// </summary>
        internal static readonly ScriptBlock GetAgReplicaInfoScript = ScriptBlock.Create(@"
param($si, $sc, $hasCred, $agFilter, $hasAgFilter)
$params = @{ SqlInstance = $si; MinimumVersion = 11 }
if ($hasCred) { $params['SqlCredential'] = $sc }
$server = Connect-DbaInstance @params
if (-not $server.IsHadrEnabled) { throw 'HADR_NOT_ENABLED' }
$ags = $server.AvailabilityGroups
if ($hasAgFilter) { $ags = $ags | Where-Object Name -in $agFilter }
if (-not $ags) { throw 'NO_AG_FOUND' }
foreach ($ag in $ags) {
    $replicaNames = @($ag.AvailabilityReplicas | ForEach-Object { $_.Name })
    [PSCustomObject]@{ AgName = $ag.Name; ReplicaNames = $replicaNames }
}
");

        /// <summary>
        /// Gets a property value (any type) from a PSObject.
        /// </summary>
        internal static object GetPropertyValue(PSObject obj, string propertyName)
        {
            if (obj == null) return null;
            try
            {
                PSPropertyInfo prop = obj.Properties[propertyName];
                if (prop != null) return prop.Value;
            }
            catch (Exception)
            {
                // PSObject property access can throw for dynamic or computed properties
            }
            return null;
        }

        /// <summary>
        /// Gets a string array property from a PSObject, handling PowerShell array coercion.
        /// </summary>
        internal static string[] GetStringArray(PSObject obj, string propertyName)
        {
            if (obj == null) return null;
            try
            {
                PSPropertyInfo prop = obj.Properties[propertyName];
                if (prop == null || prop.Value == null) return null;

                if (prop.Value is string[] strArray)
                    return strArray;

                if (prop.Value is object[] objArray)
                {
                    List<string> result = new List<string>();
                    foreach (object item in objArray)
                    {
                        if (item != null)
                        {
                            if (item is PSObject pso)
                                result.Add(pso.BaseObject != null ? pso.BaseObject.ToString() : pso.ToString());
                            else
                                result.Add(item.ToString());
                        }
                    }
                    return result.ToArray();
                }

                return new string[] { prop.Value.ToString() };
            }
            catch (Exception)
            {
                // PSObject property access can throw for dynamic or computed properties
            }
            return null;
        }

        /// <summary>
        /// Gets the full exception message chain including inner exceptions.
        /// Used for sentinel string detection in errors thrown from embedded PowerShell scripts.
        /// </summary>
        internal static string GetFullExceptionMessage(Exception ex)
        {
            if (ex == null)
                return "";
            string msg = ex.Message ?? "";
            Exception inner = ex.InnerException;
            while (inner != null)
            {
                if (inner.Message != null)
                    msg = msg + " " + inner.Message;
                inner = inner.InnerException;
            }
            return msg;
        }

        /// <summary>
        /// Finds a PSObject in a list by its Name property (case-insensitive).
        /// </summary>
        internal static PSObject FindByName(List<PSObject> items, string name)
        {
            foreach (PSObject item in items)
            {
                string itemName = DbaBaseCmdlet.GetPropertyString(item, "Name");
                if (String.Equals(itemName, name, StringComparison.OrdinalIgnoreCase))
                    return item;
            }
            return null;
        }
    }
}
