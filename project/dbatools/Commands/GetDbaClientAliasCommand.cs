using System;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Retrieves SQL Server client aliases from the Windows registry on local or remote computers.
    /// Client aliases allow DBAs to create friendly names that map to actual SQL Server instances.
    /// </summary>
    [Cmdlet("Get", "DbaClientAlias")]
    public class GetDbaClientAliasCommand : DbaBaseCmdlet
    {
        /// <summary>
        /// The computer(s) to retrieve SQL Server client aliases from. Accepts pipeline input.
        /// Defaults to the local computer when not specified.
        /// </summary>
        [Parameter(ValueFromPipeline = true)]
        public DbaInstanceParameter[] ComputerName { get; set; }

        /// <summary>
        /// Allows you to login to remote computers using alternative credentials.
        /// </summary>
        [Parameter()]
        public PSCredential Credential { get; set; }

        private ScriptBlock _sbInvokeCommand;
        private ScriptBlock _sbInvokeNoCred;
        private ScriptBlock _sbInvokeWithCred;

        /// <summary>
        /// Initializes the script blocks for remote registry access and resolves default ComputerName.
        /// </summary>
        protected override void BeginProcessing()
        {
            base.BeginProcessing();

            _sbInvokeCommand = ScriptBlock.Create(GetRegistryScriptBlock());
            _sbInvokeNoCred = ScriptBlock.Create(
                "param($cn, $sb) Invoke-Command2 -ComputerName $cn -ScriptBlock $sb -ErrorAction Stop");
            _sbInvokeWithCred = ScriptBlock.Create(
                "param($cn, $cred, $sb) Invoke-Command2 -ComputerName $cn -Credential $cred -ScriptBlock $sb -ErrorAction Stop");

            if (!TestBound("ComputerName"))
            {
                string envComputer = Environment.GetEnvironmentVariable("COMPUTERNAME");
                if (String.IsNullOrEmpty(envComputer))
                    envComputer = Environment.MachineName;
                ComputerName = new DbaInstanceParameter[] { new DbaInstanceParameter(envComputer) };
            }
        }

        /// <summary>
        /// Processes each computer name, invoking the registry scriptblock via Invoke-Command2.
        /// </summary>
        protected override void ProcessRecord()
        {
            if (ComputerName == null)
                return;

            foreach (DbaInstanceParameter computer in ComputerName)
            {
                try
                {
                    Collection<PSObject> results = InvokeRemoteCommand(computer);
                    if (results != null)
                    {
                        foreach (PSObject result in results)
                        {
                            if (result != null)
                            {
                                WriteObject(result);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    StopFunction(
                        String.Format("Failed to retrieve client aliases from {0}", computer),
                        exception: ex,
                        target: computer,
                        isContinue: true);
                    TestFunctionInterrupt();
                    continue;
                }
            }
        }

        #region Helper Methods

        /// <summary>
        /// Invokes Invoke-Command2 to run the registry scriptblock on the target computer.
        /// </summary>
        private Collection<PSObject> InvokeRemoteCommand(DbaInstanceParameter computer)
        {
            if (Credential != null)
            {
                return InvokeCommand.InvokeScript(
                    false, _sbInvokeWithCred, null,
                    computer, Credential, _sbInvokeCommand);
            }
            else
            {
                return InvokeCommand.InvokeScript(
                    false, _sbInvokeNoCred, null,
                    computer, _sbInvokeCommand);
            }
        }

        /// <summary>
        /// Returns the PowerShell script block that reads SQL Server client aliases from the registry.
        /// This runs on the target computer via Invoke-Command2. The inner Get-ItemPropertyValue
        /// function is defined because the target may run PowerShell 3.0 which lacks the built-in.
        /// </summary>
        internal static string GetRegistryScriptBlock()
        {
            return @"
                function Get-ItemPropertyValue {
                    param (
                        [parameter()]
                        [String]$Path,
                        [parameter()]
                        [String]$Name
                    )
                    (Get-ItemProperty -LiteralPath $Path -Name $Name).$Name
                }

                $basekeys = 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\MSSQLServer', 'HKLM:\SOFTWARE\Microsoft\MSSQLServer'

                foreach ($basekey in $basekeys) {
                    if ((Test-Path $basekey) -eq $false) {
                        continue
                    }

                    $client = ""$basekey\Client""

                    if ((Test-Path $client) -eq $false) {
                        continue
                    }

                    $connect = ""$client\ConnectTo""

                    if ((Test-Path $connect) -eq $false) {
                        continue
                    }

                    if ($basekey -like '*WOW64*') {
                        $architecture = '32-bit'
                    } else {
                        $architecture = '64-bit'
                    }

                    $all = Get-Item -Path $connect
                    foreach ($entry in $all.Property) {
                        $value = Get-ItemPropertyValue -Path $connect -Name $entry
                        $clean = $value.Replace('DBNMPNTW,', '').Replace('DBMSSOCN,', '')
                        if ($value.StartsWith('DBMSSOCN')) { $protocol = 'TCP/IP' } else { $protocol = 'Named Pipes' }
                        [PSCustomObject]@{
                            ComputerName   = $env:COMPUTERNAME
                            NetworkLibrary = $protocol
                            ServerName     = $clean
                            AliasName      = $entry
                            AliasString    = $value
                            Architecture   = $architecture
                        }
                    }
                }
            ";
        }

        #endregion Helper Methods
    }
}
