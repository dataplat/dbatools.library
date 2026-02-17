using Dataplat.Dbatools.Message;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Management.Automation;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Creates a comprehensive diagnostic package for troubleshooting dbatools module issues and bugs.
    /// </summary>
    [Cmdlet("New", "DbatoolsSupportPackage", SupportsShouldProcess = true)]
    [OutputType(typeof(FileInfo))]
    public class NewDbatoolsSupportPackageCommand : DbaBaseCmdlet
    {
        /// <summary>
        /// Specifies the directory where the support package ZIP file will be created.
        /// Defaults to your desktop, or home directory if desktop doesn't exist.
        /// </summary>
        [Parameter(Position = 0)]
        public string Path { get; set; }

        /// <summary>
        /// Specifies additional PowerShell variables to include in the diagnostic package by name.
        /// </summary>
        [Parameter()]
        public string[] Variables { get; set; }

        /// <summary>
        /// Returns the FileInfo object for the created ZIP file instead of just displaying its location.
        /// </summary>
        [Parameter()]
        public SwitchParameter PassThru { get; set; }

        private string _resolvedPath;
        private int _totalSteps;
        private bool _isCore;

        /// <summary>
        /// Resolves the output path.
        /// </summary>
        protected override void BeginProcessing()
        {
            base.BeginProcessing();

            // Default path: Desktop, fall back to HOME
            if (String.IsNullOrEmpty(Path))
            {
                string userProfile = Environment.GetEnvironmentVariable("USERPROFILE");
                if (!String.IsNullOrEmpty(userProfile))
                {
                    Path = System.IO.Path.Combine(userProfile, "Desktop");
                }
            }

            _resolvedPath = Path;
            if (String.IsNullOrEmpty(_resolvedPath) || !Directory.Exists(_resolvedPath))
            {
                string home = Environment.GetEnvironmentVariable("HOME");
                if (String.IsNullOrEmpty(home))
                {
                    home = Environment.GetEnvironmentVariable("USERPROFILE");
                }
                _resolvedPath = home;
            }

            WriteMessageAtLevel("Starting", MessageLevel.InternalComment, null);
            WriteMessageAtLevel(
                String.Format("Bound parameters: {0}", String.Join(", ", MyInvocation.BoundParameters.Keys)),
                MessageLevel.Verbose, null);
        }

        /// <summary>
        /// Collects diagnostic data and creates the support package.
        /// </summary>
        protected override void ProcessRecord()
        {
            if (!ShouldProcess("Creating a Support Package for diagnosing Dbatools"))
                return;

            // Determine PS edition early for step count and snapins branch
            Collection<PSObject> psEdition = InvokePowerShellCollection("$PSVersionTable.PSEdition");
            string edition = null;
            if (psEdition != null && psEdition.Count > 0 && psEdition[0] != null)
            {
                edition = psEdition[0].ToString();
            }
            _isCore = String.Equals(edition, "Core", StringComparison.OrdinalIgnoreCase);

            // Compute exact step count: 10 fixed + conditional snapins + conditional variables
            _totalSteps = 10 + (_isCore ? 0 : 1) + (TestBound("Variables") ? 1 : 0);

            string timestamp = DateTime.Now.ToString("yyyy_MM_dd-HH_mm_ss");
            string filePathXml = System.IO.Path.Combine(_resolvedPath,
                String.Format("dbatools_support_pack_{0}.xml", timestamp));
            string filePathZip = System.IO.Path.ChangeExtension(filePathXml, ".zip");

            // Write the critical-level informational message
            WriteMessageAtLevel(String.Format(
                "Will write the final output to: {0}\n" +
                "Please submit this file to the team, to help with troubleshooting whatever issue you encountered. " +
                "Be aware that this package contains a lot of information including your input history in the console. " +
                "Please make sure no sensitive data (such as passwords) can be caught this way.\n" +
                "Ideally start a new console, perform the minimal steps required to reproduce the issue, then run this command. " +
                "This will make it easier for us to troubleshoot and you won't be sending us the keys to your castle.",
                filePathZip), MessageLevel.Critical, null);

            Hashtable hash = new Hashtable();
            int stepCounter = 0;

            // Step: Messages
            WriteProgressStep(stepCounter++, "Collecting dbatools logged messages (Get-DbatoolsLog)");
            hash["Messages"] = InvokePowerShell("Get-DbatoolsLog");

            // Step: Errors
            WriteProgressStep(stepCounter++, "Collecting dbatools logged errors (Get-DbatoolsLog -Errors)");
            hash["Errors"] = InvokePowerShell("Get-DbatoolsLog -Errors");

            // Step: Console Buffer
            WriteProgressStep(stepCounter++, "Collecting copy of console buffer (what you can see on your console)");
            hash["ConsoleBuffer"] = GetShellBuffer();

            // Step: OS info
            WriteProgressStep(stepCounter++, "Collecting Operating System information (Win32_OperatingSystem)");
            hash["OperatingSystem"] = InvokePowerShell("Get-DbaCmObject -ClassName Win32_OperatingSystem");

            // Step: CPU
            WriteProgressStep(stepCounter++, "Collecting CPU information (Win32_Processor)");
            hash["CPU"] = InvokePowerShell("Get-DbaCmObject -ClassName Win32_Processor");

            // Step: RAM
            WriteProgressStep(stepCounter++, "Collecting Ram information (Win32_PhysicalMemory)");
            hash["Ram"] = InvokePowerShell("Get-DbaCmObject -ClassName Win32_PhysicalMemory");

            // Step: PS Version
            WriteProgressStep(stepCounter++, "Collecting PowerShell & .NET Version ($PSVersionTable)");
            hash["PSVersion"] = InvokePowerShell("$PSVersionTable");

            // Step: History
            WriteProgressStep(stepCounter++, "Collecting Input history (Get-History)");
            hash["History"] = InvokePowerShell("Get-History");

            // Step: Modules
            WriteProgressStep(stepCounter++, "Collecting list of loaded modules (Get-Module)");
            hash["Modules"] = InvokePowerShell("Get-Module");

            // Step: Snapins (Windows PowerShell only)
            if (!_isCore)
            {
                WriteProgressStep(stepCounter++, "Collecting list of loaded snapins (Get-PSSnapin)");
                hash["SnapIns"] = InvokePowerShell("Get-PSSnapin");
            }

            // Step: Assemblies
            WriteProgressStep(stepCounter++, "Collecting list of loaded assemblies (Name, Version, and Location)");
            hash["Assemblies"] = InvokePowerShell(
                "[appdomain]::CurrentDomain.GetAssemblies() | Select-Object CodeBase, FullName, Location, ImageRuntimeVersion, GlobalAssemblyCache, IsDynamic");

            // Step: Variables
            if (TestBound("Variables"))
            {
                WriteProgressStep(stepCounter++,
                    String.Format("Adding variables specified for export: {0}", String.Join(", ", Variables)));
                string variableScript = BuildGetVariableScript(Variables);
                hash["Variables"] = InvokePowerShell(variableScript);
            }

            // Build the PSCustomObject from hash
            PSObject data = new PSObject();
            foreach (DictionaryEntry entry in hash)
            {
                data.Properties.Add(new PSNoteProperty(entry.Key.ToString(), entry.Value));
            }

            // Export to XML
            try
            {
                InvokeCommand.InvokeScript(
                    false,
                    ScriptBlock.Create("param($data, $path) $data | Export-Clixml -Path $path -ErrorAction Stop"),
                    null,
                    data,
                    filePathXml
                );
            }
            catch (Exception ex)
            {
                StopFunction(
                    "Failed to export dump to file.",
                    exception: ex,
                    target: filePathXml);
                return;
            }

            // Compress to ZIP
            try
            {
                InvokeCommand.InvokeScript(
                    false,
                    ScriptBlock.Create("param($src, $dst) Compress-Archive -Path $src -DestinationPath $dst -ErrorAction Stop"),
                    null,
                    filePathXml,
                    filePathZip
                );

                // Output the file info
                FileInfo zipFile = new FileInfo(filePathZip);
                WriteObject(zipFile);
            }
            catch (Exception ex)
            {
                StopFunction(
                    "Failed to pack dump-file into a zip archive. Please do so manually before submitting the results as the unpacked xml file will be rather large.",
                    exception: ex,
                    target: filePathZip);
                return;
            }

            // Clean up XML
            try
            {
                File.Delete(filePathXml);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        /// <summary>
        /// Ending message.
        /// </summary>
        protected override void EndProcessing()
        {
            WriteMessageAtLevel("Ending", MessageLevel.InternalComment, null);
        }

        #region Helper Methods

        /// <summary>
        /// Invokes a PowerShell script and returns the result as an object array
        /// suitable for storing in the support package hash.
        /// </summary>
        internal object InvokePowerShell(string script)
        {
            try
            {
                Collection<PSObject> results = InvokeCommand.InvokeScript(script);
                if (results == null || results.Count == 0)
                    return null;
                if (results.Count == 1)
                    return results[0];

                object[] array = new object[results.Count];
                for (int i = 0; i < results.Count; i++)
                {
                    array[i] = results[i];
                }
                return array;
            }
            catch (Exception ex)
            {
                WriteMessageAtLevel(
                    String.Format("Failed to execute: {0} - {1}", script, ex.Message),
                    MessageLevel.Verbose, null);
                return null;
            }
        }

        /// <summary>
        /// Invokes a PowerShell script and returns the raw PSObject collection.
        /// </summary>
        internal Collection<PSObject> InvokePowerShellCollection(string script)
        {
            try
            {
                return InvokeCommand.InvokeScript(script);
            }
            catch
            {
                return new Collection<PSObject>();
            }
        }

        /// <summary>
        /// Captures the current console buffer contents.
        /// Returns null if the host does not support buffer access.
        /// </summary>
        internal object GetShellBuffer()
        {
            try
            {
                string script = @"
try {
    $rec = New-Object System.Management.Automation.Host.Rectangle
    $rec.Left = 0
    $rec.Right = $host.ui.rawui.BufferSize.Width - 1
    $rec.Top = 0
    $rec.Bottom = $host.ui.rawui.BufferSize.Height - 1
    $buffer = $host.ui.rawui.GetBufferContents($rec)
    $int = 0
    $lines = @()
    while ($int -le $rec.Bottom) {
        $n = 0
        $line = ''
        while ($n -le $rec.Right) {
            $line += $buffer[$int, $n].Character
            $n++
        }
        $line = $line.TrimEnd()
        $lines += $line
        $int++
    }
    $int = 0
    $temp = $lines[$int]
    while ($temp -eq '') { $int++; $temp = $lines[$int] }
    $z = $rec.Bottom
    $temp = $lines[$z]
    while ($temp -eq '') { $z--; $temp = $lines[$z] }
    $z--
    $temp = $lines[$z]
    while ($temp -eq '') { $z--; $temp = $lines[$z] }
    return $lines[$int .. $z]
} catch {
    return $null
}
";
                Collection<PSObject> results = InvokeCommand.InvokeScript(script);
                if (results == null || results.Count == 0)
                    return null;

                object[] lines = new object[results.Count];
                for (int i = 0; i < results.Count; i++)
                {
                    lines[i] = results[i] != null ? results[i].BaseObject : null;
                }
                return lines;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Builds a script to retrieve specified variables by name.
        /// </summary>
        internal static string BuildGetVariableScript(string[] variableNames)
        {
            if (variableNames == null || variableNames.Length == 0)
                return "$null";

            // Build: 'name1','name2' | Get-Variable -ErrorAction Ignore
            List<string> quoted = new List<string>();
            foreach (string name in variableNames)
            {
                quoted.Add(String.Format("'{0}'", name.Replace("'", "''")));
            }
            return String.Format("{0} | Get-Variable -ErrorAction Ignore", String.Join(",", quoted));
        }

        /// <summary>
        /// Writes a progress update using Write-Progress.
        /// </summary>
        private void WriteProgressStep(int stepNumber, string message)
        {
            try
            {
                int percentComplete = _totalSteps > 0
                    ? (int)((stepNumber * 100) / _totalSteps)
                    : 0;

                string script = "param($activity, $status, $pct) Write-Progress -Activity $activity -Status $status -PercentComplete $pct";
                InvokeCommand.InvokeScript(
                    false,
                    ScriptBlock.Create(script),
                    null,
                    "Executing New-DbatoolsSupportPackage",
                    message,
                    percentComplete
                );
            }
            catch
            {
                // Progress is non-critical
            }
        }

        #endregion Helper Methods
    }
}
