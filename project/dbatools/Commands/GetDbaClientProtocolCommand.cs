using System;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Retrieves SQL Server client network protocol configuration and status from local or remote computers.
    /// Shows which protocols are enabled, their order of precedence, and associated DLL files.
    /// </summary>
    [Cmdlet("Get", "DbaClientProtocol")]
    public class GetDbaClientProtocolCommand : DbaBaseCmdlet
    {
        /// <summary>
        /// The target computer(s) to retrieve SQL Server client protocol configuration from.
        /// Accepts computer names, IP addresses, or SQL Server instance names.
        /// Defaults to the local computer when not specified.
        /// </summary>
        [Parameter(ValueFromPipeline = true)]
        [Alias("cn", "host", "Server")]
        public DbaInstanceParameter[] ComputerName { get; set; }

        /// <summary>
        /// Credential object used to connect to the computer as a different user.
        /// </summary>
        [Parameter()]
        public PSCredential Credential { get; set; }

        private ScriptBlock _sbProcessComputer;

        /// <summary>
        /// Initializes the script block for CIM-based protocol retrieval and resolves default ComputerName.
        /// </summary>
        protected override void BeginProcessing()
        {
            base.BeginProcessing();

            _sbProcessComputer = ScriptBlock.Create(GetProcessComputerScript());

            if (!TestBound("ComputerName"))
            {
                string envComputer = Environment.GetEnvironmentVariable("COMPUTERNAME");
                if (String.IsNullOrEmpty(envComputer))
                    envComputer = Environment.MachineName;
                ComputerName = new DbaInstanceParameter[] { new DbaInstanceParameter(envComputer) };
            }
        }

        /// <summary>
        /// Processes each computer name, resolving the name and querying CIM for client network protocols.
        /// </summary>
        protected override void ProcessRecord()
        {
            if (ComputerName == null)
                return;

            foreach (DbaInstanceParameter computer in ComputerName)
            {
                try
                {
                    Collection<PSObject> results = InvokeForComputer(computer);

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
                        String.Format("Failed to retrieve client protocols from {0}", computer),
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
        /// Invokes the process-computer script block on the target computer.
        /// </summary>
        private Collection<PSObject> InvokeForComputer(DbaInstanceParameter computer)
        {
            return InvokeCommand.InvokeScript(
                false, _sbProcessComputer, null,
                computer.ComputerName, Credential);
        }

        /// <summary>
        /// Returns the PowerShell script that resolves the computer name and queries CIM
        /// for SQL Server client network protocols. Mirrors the original PS1 logic:
        /// resolve name, find ComputerManagement namespace, query ClientNetworkProtocol,
        /// add IsEnabled/Enable/Disable members, and apply default view.
        /// </summary>
        internal static string GetProcessComputerScript()
        {
            return @"
param($computerName, $credential)

$server = Resolve-DbaNetworkName -ComputerName $computerName -Credential $credential
if ($server.FullComputerName) {
    $computer = $server.FullComputerName
    Write-Message -Level Verbose -Message ""Getting SQL Server namespace on $computer""
    $namespace = Get-DbaCmObject -ComputerName $computer -Namespace root\Microsoft\SQLServer -Query ""Select * FROM __NAMESPACE WHERE Name LIke 'ComputerManagement%'"" -ErrorAction SilentlyContinue | Sort-Object Name -Descending | Select-Object -First 1

    if ($namespace.Name) {
        Write-Message -Level Verbose -Message ""Getting Cim class ClientNetworkProtocol in Namespace $($namespace.Name) on $computer""
        try {
            $prot = Get-DbaCmObject -ComputerName $computer -Namespace $(""root\Microsoft\SQLServer\"" + $namespace.Name) -ClassName ClientNetworkProtocol -ErrorAction SilentlyContinue

            $prot | Add-Member -Force -MemberType ScriptProperty -Name IsEnabled -Value { switch ( $this.ProtocolOrder ) { 0 { $false } default { $true } } }
            $prot | Add-Member -Force -MemberType ScriptMethod -Name Enable -Value { Invoke-CimMethod -MethodName SetEnable -InputObject $this }
            $prot | Add-Member -Force -MemberType ScriptMethod -Name Disable -Value { Invoke-CimMethod -MethodName SetDisable -InputObject $this }

            foreach ($protocol in $prot) {
                Select-DefaultView -InputObject $protocol -Property 'PSComputerName as ComputerName', 'ProtocolDisplayName as DisplayName', 'ProtocolDll as DLL', 'ProtocolOrder as Order', 'IsEnabled'
            }
        } catch {
            Write-Message -Level Warning -Message ""No Sql ClientNetworkProtocol found on $computer""
        }
    } else {
        Write-Message -Level Warning -Message ""No ComputerManagement Namespace on $computer. Please note that this function is available from SQL 2005 up.""
    }
} else {
    Write-Message -Level Warning -Message ""Failed to connect to $computerName""
}
";
        }

        #endregion Helper Methods
    }
}
