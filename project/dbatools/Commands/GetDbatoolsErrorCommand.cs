using System;
using System.Collections;
using System.Collections.Generic;
using System.Management.Automation;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Retrieves detailed error information from failed dbatools commands for troubleshooting.
    /// Filters the PowerShell global error collection to show only dbatools-related errors.
    /// </summary>
    [Cmdlet("Get", "DbatoolsError")]
    [OutputType(typeof(PSObject))]
    public class GetDbatoolsErrorCommand : DbaBaseCmdlet
    {
        /// <summary>
        /// Specifies the number of most recent dbatools errors to return. Defaults to 1 if no parameters are specified.
        /// </summary>
        [Parameter()]
        public int First { get; set; }

        /// <summary>
        /// Specifies the number of oldest dbatools errors to return from the error history.
        /// </summary>
        [Parameter()]
        public int Last { get; set; }

        /// <summary>
        /// Specifies the number of most recent dbatools errors to skip before returning results.
        /// </summary>
        [Parameter()]
        public int Skip { get; set; }

        /// <summary>
        /// Returns detailed information for all dbatools-related errors in the current PowerShell session.
        /// </summary>
        [Parameter()]
        public SwitchParameter All { get; set; }

        /// <summary>
        /// The property names to select from each ErrorRecord, matching the PS1 behavior.
        /// </summary>
        internal static readonly string[] SelectedProperties = new string[]
        {
            "CategoryInfo",
            "ErrorDetails",
            "Exception",
            "FullyQualifiedErrorId",
            "InvocationInfo",
            "PipelineIterationInfo",
            "PSMessageDetails",
            "ScriptStackTrace",
            "TargetObject"
        };

        /// <summary>
        /// Processes the error collection, filtering for dbatools errors and applying paging parameters.
        /// </summary>
        protected override void ProcessRecord()
        {
            int first = First;
            int last = Last;
            int skip = Skip;

            // If no parameters were bound, default to First = 1
            if (!TestBound("First", "Last", "Skip", "All"))
            {
                first = 1;
            }

            // Get the global $error variable
            ArrayList globalErrors = GetGlobalErrorCollection();
            if (globalErrors == null || globalErrors.Count == 0)
            {
                return;
            }

            if (All.IsPresent)
            {
                first = globalErrors.Count;
            }

            // Filter for dbatools errors and build selected PSObjects
            List<PSObject> dbatoolsErrors = FilterDbatoolsErrors(globalErrors);
            if (dbatoolsErrors.Count == 0)
            {
                return;
            }

            // Apply paging: Skip, First, Last (matching Select-Object behavior)
            List<PSObject> paged = ApplyPaging(dbatoolsErrors, first, last, skip);

            foreach (PSObject item in paged)
            {
                WriteObject(item);
            }
        }

        /// <summary>
        /// Retrieves the global $error automatic variable from the PowerShell session.
        /// </summary>
        /// <returns>The global error ArrayList, or null if not available</returns>
        internal ArrayList GetGlobalErrorCollection()
        {
            try
            {
                object errorVar = SessionState.PSVariable.GetValue("global:error");
                if (errorVar is ArrayList arrayList)
                {
                    return arrayList;
                }
            }
            catch (Exception)
            {
                // Variable access may fail in constrained scenarios
            }
            return null;
        }

        /// <summary>
        /// Filters the global error collection for dbatools-related errors and creates
        /// PSObjects with the selected properties.
        /// </summary>
        /// <param name="errors">The global error collection</param>
        /// <returns>A list of PSObjects with selected properties from matching ErrorRecords</returns>
        internal static List<PSObject> FilterDbatoolsErrors(ArrayList errors)
        {
            List<PSObject> result = new List<PSObject>();
            if (errors == null)
            {
                return result;
            }

            foreach (object item in errors)
            {
                ErrorRecord record = item as ErrorRecord;
                if (record == null)
                {
                    continue;
                }

                if (record.FullyQualifiedErrorId == null)
                {
                    continue;
                }

                // Match "dbatools" case-insensitive (PS1 uses -match which is case-insensitive)
                if (record.FullyQualifiedErrorId.IndexOf("dbatools", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                PSObject selected = new PSObject();
                selected.Properties.Add(new PSNoteProperty("CategoryInfo", record.CategoryInfo));
                selected.Properties.Add(new PSNoteProperty("ErrorDetails", record.ErrorDetails));
                selected.Properties.Add(new PSNoteProperty("Exception", record.Exception));
                selected.Properties.Add(new PSNoteProperty("FullyQualifiedErrorId", record.FullyQualifiedErrorId));
                selected.Properties.Add(new PSNoteProperty("InvocationInfo", record.InvocationInfo));
                selected.Properties.Add(new PSNoteProperty("PipelineIterationInfo", record.PipelineIterationInfo));
                object psMessageDetails = null;
                PSPropertyInfo psMessageDetailsProp = PSObject.AsPSObject(record).Properties["PSMessageDetails"];
                if (psMessageDetailsProp != null)
                {
                    try { psMessageDetails = psMessageDetailsProp.Value; }
                    catch (Exception) { }
                }
                selected.Properties.Add(new PSNoteProperty("PSMessageDetails", psMessageDetails));
                selected.Properties.Add(new PSNoteProperty("ScriptStackTrace", record.ScriptStackTrace));
                selected.Properties.Add(new PSNoteProperty("TargetObject", record.TargetObject));
                result.Add(selected);
            }

            return result;
        }

        /// <summary>
        /// Applies Select-Object-style paging (First, Last, Skip) to a list of PSObjects.
        /// Mimics PowerShell's Select-Object behavior: Skip removes from the front,
        /// First takes from the front, Last takes from the end.
        /// </summary>
        /// <param name="items">The items to page</param>
        /// <param name="first">Number of items to take from the front (0 = no limit)</param>
        /// <param name="last">Number of items to take from the end (0 = no limit)</param>
        /// <param name="skip">Number of items to skip from the front</param>
        /// <returns>The paged subset of items</returns>
        internal static List<PSObject> ApplyPaging(List<PSObject> items, int first, int last, int skip)
        {
            if (items == null || items.Count == 0)
            {
                return new List<PSObject>();
            }

            List<PSObject> result = new List<PSObject>(items);

            // Apply Skip first (remove from front)
            if (skip > 0 && skip < result.Count)
            {
                result = result.GetRange(skip, result.Count - skip);
            }
            else if (skip >= result.Count)
            {
                return new List<PSObject>();
            }

            // Apply First (take from front)
            if (first > 0 && first < result.Count)
            {
                result = result.GetRange(0, first);
            }

            // Apply Last (take from end)
            if (last > 0 && last < result.Count)
            {
                result = result.GetRange(result.Count - last, last);
            }

            return result;
        }
    }
}
