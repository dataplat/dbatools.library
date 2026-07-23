#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Gets SQL Server credentials. Port of public/Get-DbaCredential.ps1 (W3-025). Pure per-record
/// process command with no begin/end blocks. DEF-001 cond1+cond2: the process foreach EMITS a
/// decorated credential per match (Select-DefaultView) AND has a reachable Stop-Function -Continue
/// at Connect-DbaInstance, so the hop STREAMS via InvokeScopedStreaming - a buffered hop would lose
/// an earlier instance's emit when a later instance throws under -EnableException. No ShouldProcess,
/// no cross-record state, no carriers beyond the parameters. Positions match the retired function's
/// implicit positional binding (SqlInstance=0 .. ExcludeIdentity=5; EnableException=switch/null) and
/// the four aliases (Name / ExcludeName / CredentialIdentity / ExcludeCredentialIdentity) are
/// preserved. Substitution only: explicit -FunctionName Get-DbaCredential on Stop-Function (W1-090);
/// the body is otherwise verbatim. Surface pinned by migration/baselines/Get-DbaCredential.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaCredential")]
public sealed class GetDbaCredentialCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Alternative credential for the target instances.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>Credential name(s) to include.</summary>
    [Parameter(Position = 2)]
    [Alias("Name")]
    public string[]? Credential { get; set; }

    /// <summary>Credential name(s) to exclude.</summary>
    [Parameter(Position = 3)]
    [Alias("ExcludeName")]
    public string[]? ExcludeCredential { get; set; }

    /// <summary>Credential identity/identities to include.</summary>
    [Parameter(Position = 4)]
    [Alias("CredentialIdentity")]
    public string[]? Identity { get; set; }

    /// <summary>Credential identity/identities to exclude.</summary>
    [Parameter(Position = 5)]
    [Alias("ExcludeCredentialIdentity")]
    public string[]? ExcludeIdentity { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        NestedCommand.InvokeScopedStreaming(this, item =>
        {
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                NestedCommand.RemoveDuplicateError(this, nestedError);
                WriteError(nestedError);
                return;
            }
            WriteObject(item);
        }, ProcessScript,
            SqlInstance, SqlCredential, Credential, ExcludeCredential, Identity, ExcludeIdentity, EnableException.ToBool(),
            NestedCommand.BoundCommonParameter(this, "Verbose"), NestedCommand.BoundCommonParameter(this, "Debug"));
    }

    // PS: the process body VERBATIM per record (no begin/end blocks). Substitution only: explicit
    // -FunctionName Get-DbaCredential on Stop-Function (W1-090).
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Credential, $ExcludeCredential, $Identity, $ExcludeIdentity, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [string[]]$Credential, [string[]]$ExcludeCredential, [string[]]$Identity, [string[]]$ExcludeIdentity, $EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    foreach ($instance in $SqlInstance) {
        try {
            $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Get-DbaCredential
        }

        $creds = $server.Credentials

        if ($Credential) {
            $creds = $creds | Where-Object { $Credential -contains $_.Name }
        }

        if ($ExcludeCredential) {
            $creds = $creds | Where-Object { $ExcludeCredential -notcontains $_.Name }
        }

        if ($Identity) {
            $creds = $creds | Where-Object { $Identity -contains $_.Identity }
        }

        if ($ExcludeIdentity) {
            $creds = $creds | Where-Object { $ExcludeIdentity -notcontains $_.Identity }
        }

        foreach ($currentcredential in $creds) {
            Add-Member -Force -InputObject $currentcredential -MemberType NoteProperty -Name ComputerName -value $currentcredential.Parent.ComputerName
            Add-Member -Force -InputObject $currentcredential -MemberType NoteProperty -Name InstanceName -value $currentcredential.Parent.ServiceName
            Add-Member -Force -InputObject $currentcredential -MemberType NoteProperty -Name SqlInstance -value $currentcredential.Parent.DomainInstanceName

            Select-DefaultView -InputObject $currentcredential -Property ComputerName, InstanceName, SqlInstance, ID, Name, Identity, MappedClassType, ProviderName
        }
    }
} $SqlInstance $SqlCredential $Credential $ExcludeCredential $Identity $ExcludeIdentity $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
