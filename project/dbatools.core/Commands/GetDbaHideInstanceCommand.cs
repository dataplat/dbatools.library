#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Gets the HideInstance flag for SQL Server instances. Port of
/// public/Get-DbaHideInstance.ps1 (W3-034), the read-only sibling of W3-007/W3-011
/// (Disable/Enable-DbaHideInstance): same resolve / WMI / registry path, but it READS HideInstance
/// (no ShouldProcess, no write) and returns a hashtable that the client rehydrates to a
/// [PSCustomObject]. Pure per-record process command with no begin/end blocks. DEF-001 cond1+cond2:
/// the process foreach EMITS a [PSCustomObject] per instance (from the Invoke-Command2 remote read)
/// AND has reachable Stop-Function -Continue at Resolve-DbaNetworkName / Invoke-ManagedComputerCommand
/// / the REGROOT lookup / Invoke-Command2, so the hop STREAMS via InvokeScopedStreaming. The source's
/// SqlInstance default ($env:COMPUTERNAME) is applied in ProcessRecord ONLY when the parameter was
/// not explicitly bound (an explicit $null/@() must not fall back to localhost - the W3-007 gate
/// lesson). Substitution only: explicit -FunctionName Get-DbaHideInstance on Stop-Function (W1-090);
/// the body is otherwise verbatim. Surface pinned by migration/baselines/Get-DbaHideInstance.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaHideInstance")]
public sealed class GetDbaHideInstanceCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances. Defaults to the local computer.</summary>
    [Parameter(Position = 0, ValueFromPipeline = true)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Alternative Windows credential for the target server.</summary>
    [Parameter(Position = 1)]
    public PSCredential? Credential { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        // PS: [DbaInstanceParameter[]]$SqlInstance = $env:COMPUTERNAME - apply the default ONLY when
        // the parameter was not explicitly bound (an explicit $null/@() must NOT fall back to
        // localhost; the compiled parameter cannot default to a runtime expression).
        DbaInstanceParameter[]? instances = SqlInstance;
        if (!MyInvocation.BoundParameters.ContainsKey("SqlInstance") && (instances is null || instances.Length == 0))
        {
            string? machine = Environment.GetEnvironmentVariable("COMPUTERNAME");
            if (!string.IsNullOrEmpty(machine))
                instances = new[] { new DbaInstanceParameter(machine) };
        }

        NestedCommand.InvokeScopedStreaming(this, item =>
        {
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                RemoveHopErrorBookkeeping(nestedError);
                WriteError(nestedError);
                return;
            }
            WriteObject(item);
        }, ProcessScript,
            instances, Credential, EnableException.ToBool(),
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug"));
    }

    private object? BoundCommonParameter(string name)
    {
        if (MyInvocation.BoundParameters.TryGetValue(name, out object? value))
            return LanguagePrimitives.IsTrue(value);
        return null;
    }

    private void RemoveHopErrorBookkeeping(ErrorRecord record)
    {
        try
        {
            if (SessionState.PSVariable.GetValue("Error") is not ArrayList errorList || errorList.Count == 0)
                return;
            if (errorList[0] is not ErrorRecord first)
                return;
            if (ReferenceEquals(first, record) || ReferenceEquals(first.Exception, record.Exception) ||
                string.Equals(first.Exception?.Message, record.Exception?.Message, StringComparison.Ordinal))
            {
                errorList.RemoveAt(0);
            }
        }
        catch
        {
            // Best-effort bookkeeping only.
        }
    }

    // PS: the process body VERBATIM per record (no begin/end blocks). Substitution only: explicit
    // -FunctionName Get-DbaHideInstance on Stop-Function (W1-090). SqlInstance arrives already
    // defaulted to the computer name by ProcessRecord.
    private const string ProcessScript = """
param($SqlInstance, $Credential, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$Credential, $EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    foreach ($instance in $SqlInstance) {
        Write-Message -Level VeryVerbose -Message "Processing $instance" -Target $instance -FunctionName Get-DbaHideInstance -ModuleName "dbatools"
        $null = Test-ElevationRequirement -ComputerName $instance -Continue

        try {
            Write-Message -Level Verbose -Message "Resolving hostname." -FunctionName Get-DbaHideInstance -ModuleName "dbatools"
            $resolved = $null
            $resolved = Resolve-DbaNetworkName -ComputerName $instance -Credential $Credential -EnableException
        } catch {
            $resolved = Resolve-DbaNetworkName -ComputerName $instance -Credential $Credential -Turbo
        }

        if ($null -eq $resolved) {
            Stop-Function -Message "Can't resolve $instance" -Target $instance -Continue -Category InvalidArgument -FunctionName Get-DbaHideInstance
        }

        try {
            $sqlwmi = Invoke-ManagedComputerCommand -ComputerName $resolved.FullComputerName -ScriptBlock {
                $wmi.Services
            } -Credential $Credential -ErrorAction Stop | Where-Object DisplayName -eq "SQL Server ($($instance.InstanceName))"
        } catch {
            Stop-Function -Message "Failed to access $instance" -Target $instance -Continue -ErrorRecord $_ -FunctionName Get-DbaHideInstance
        }

        $regRoot = ($sqlwmi.AdvancedProperties | Where-Object Name -eq REGROOT).Value
        $vsname = ($sqlwmi.AdvancedProperties | Where-Object Name -eq VSNAME).Value
        try {
            $instanceName = $sqlwmi.DisplayName.Replace('SQL Server (', '').Replace(')', '') # Don't clown, I don't know regex :(
        } catch {
            # Probably because the instance name has been aliased or does not exist or something
            # here to avoid an empty catch
            $null = 1
        }
        $serviceAccount = $sqlwmi.ServiceAccount

        if ([System.String]::IsNullOrEmpty($regRoot)) {
            $regRoot = $sqlwmi.AdvancedProperties | Where-Object {
                $_ -match 'REGROOT'
            }
            $vsname = $sqlwmi.AdvancedProperties | Where-Object {
                $_ -match 'VSNAME'
            }

            if (-not [System.String]::IsNullOrEmpty($regRoot)) {
                $regRoot = ($regRoot -Split 'Value\=')[1]
                $vsname = ($vsname -Split 'Value\=')[1]
            } else {
                Stop-Function -Message "Can't find instance $vsname on $instance" -Continue -Category ObjectNotFound -Target $instance -FunctionName Get-DbaHideInstance
            }
        }

        if ([System.String]::IsNullOrEmpty($vsname)) {
            $vsname = $instance
        }

        Write-Message -Level Verbose -Message "Regroot: $regRoot" -Target $instance -FunctionName Get-DbaHideInstance -ModuleName "dbatools"
        Write-Message -Level Verbose -Message "ServiceAcct: $serviceAccount" -Target $instance -FunctionName Get-DbaHideInstance -ModuleName "dbatools"
        Write-Message -Level Verbose -Message "InstanceName: $instanceName" -Target $instance -FunctionName Get-DbaHideInstance -ModuleName "dbatools"
        Write-Message -Level Verbose -Message "VSNAME: $vsname" -Target $instance -FunctionName Get-DbaHideInstance -ModuleName "dbatools"

        $scriptBlock = {
            $regPath = "Registry::HKEY_LOCAL_MACHINE\$($args[0])\MSSQLServer\SuperSocketNetLib"
            $HideInstance = (Get-ItemProperty -Path $regPath -Name HideInstance).HideInstance

            # [PSCustomObject] doesn't always work, unsure why. so return hashtable then turn it into  PSCustomObject on client
            @{
                ComputerName = $env:COMPUTERNAME
                InstanceName = $args[2]
                SqlInstance  = $args[1]
                HideInstance = ($hideinstance -eq $true)
            }
        }

        try {
            $results = Invoke-Command2 -ComputerName $resolved.FullComputerName -Credential $Credential -ArgumentList $regRoot, $vsname, $instanceName -ScriptBlock $scriptBlock -ErrorAction Stop -Raw
            foreach ($result in $results) {
                [PSCustomObject]$result
            }
        } catch {
            Stop-Function -Message "Failed to connect to $($resolved.FullComputerName) using PowerShell remoting" -ErrorRecord $_ -Target $instance -Continue -FunctionName Get-DbaHideInstance
        }
    }
} $SqlInstance $Credential $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
