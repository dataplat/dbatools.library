#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Gets the Policy Based Management policy store from a SQL Server instance.
/// </summary>
/// <remarks>
/// <para>
/// The workflow remains a module-scoped PowerShell compatibility hop so that the DMF library load, the
/// policy-store construction, the added note properties, the default view, and dbatools stream and error
/// handling stay observable-identical to the script implementation.
/// </para>
/// <para>
/// The script guards PowerShell Core with a Stop-Function that has NO -Continue, so it latches the
/// interrupt and returns. That latch lives in the function scope and spans the whole pipeline: the first
/// record warns, and every later record returns immediately at the process block's Test-FunctionInterrupt
/// without warning again. A per-record hop scope would lose it and warn once per record, so the latch is
/// carried - the hop's body runs dot-sourced (its early returns stay local) and the trailing sentinel
/// reports Test-FunctionInterrupt, which the demux latches into a field that short-circuits later records.
/// The connection failure path uses Stop-Function -Continue, which continues the instance loop and does not
/// latch, so it needs no carry (probe-confirmed against the real Stop-Function).
/// </para>
/// <para>
/// Add-PbmLibrary rides at the top of each record's hop rather than in a begin hop: it only loads the DMF
/// assemblies through Add-Type, which is idempotent, and it resolves its module-scoped library root inside
/// the dbatools module scope exactly as the script's begin block does. Streaming is unnecessary here - the
/// only terminating path is the Core guard, which fires before any output - so buffered InvokeScoped is
/// correct. EnableException is carried as a plain (untyped) value, because a switch in the inner
/// CmdletBinding scriptblock is excluded from positional binding.
/// </para>
/// </remarks>
// No [OutputType] is declared: the emitted DMF PolicyStore type ships in assemblies that
// Add-PbmLibrary loads at RUNTIME, so it cannot be referenced at compile time.
[Cmdlet(VerbsCommon.Get, "DbaPbmStore")]
public sealed class GetDbaPbmStoreCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Alternative credential for the target instances.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    /// <summary>Set once the body has latched the dbatools interrupt, mirroring the script's function scope.</summary>
    private bool _bodyInterrupted;

    /// <summary>Returns the policy store for the instances bound to the current record.</summary>
    protected override void ProcessRecord()
    {
        if (_bodyInterrupted || Interrupted)
            return;

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, ProcessScript,
            SqlInstance, SqlCredential, EnableException.ToBool(),
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
        {
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                RemoveHopErrorBookkeeping(nestedError);
                WriteError(nestedError);
            }
            else if (item is not null && LanguagePrimitives.IsTrue(
                item.Properties["__GetDbaPbmStoreProcessComplete"]?.Value))
            {
                _bodyInterrupted = LanguagePrimitives.IsTrue(item.Properties["Interrupted"]?.Value);
            }
            else if (item is not null)
            {
                WriteObject(item);
            }
        }
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

    // PS: the begin block's Add-PbmLibrary, then the process body VERBATIM inside a dot-sourced block so
    // its early returns stay local and the trailing sentinel still runs. The sentinel reports the dbatools
    // interrupt latch so the next record can skip exactly as the script's function-scoped latch makes it.
    // No other edits: the source has no Test-Bound, no $PSBoundParameters read, and no ShouldProcess, and
    // its two Stop-Function calls are left unstamped-by-name only where the source itself is direct - both
    // are DIRECT, so both take -FunctionName. EnableException is received untyped.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [System.Management.Automation.PSCredential]$SqlCredential, $EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    Add-PbmLibrary

    . {
        if (Test-FunctionInterrupt) { return }
        if ($PSVersionTable.PSEdition -eq "Core") {
            Stop-Function -Message "This command is not supported on Linux or macOS" -FunctionName Get-DbaPbmStore
            return
        }
        foreach ($instance in $SqlInstance) {
            try {
                $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential -MinimumVersion 10
                $sqlStoreConnection = New-Object Microsoft.SqlServer.Management.Sdk.Sfc.SqlStoreConnection $server.ConnectionContext.SqlConnectionObject
                # DMF is the Declarative Management Framework, Policy Based Management's old name
                $store = New-Object Microsoft.SqlServer.Management.DMF.PolicyStore $sqlStoreConnection
            } catch {
                Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Get-DbaPbmStore
            }

            Add-Member -Force -InputObject $store -MemberType NoteProperty ComputerName -value $server.ComputerName
            Add-Member -Force -InputObject $store -MemberType NoteProperty InstanceName -value $server.ServiceName
            Add-Member -Force -InputObject $store -MemberType NoteProperty SqlInstance -value $server.DomainInstanceName

            Select-DefaultView -InputObject $store -ExcludeProperty SqlStoreConnection, ConnectionContext, Properties, Urn, Parent, DomainInstanceName, Metadata, IdentityKey, Name
        }
    }

    [pscustomobject]@{ __GetDbaPbmStoreProcessComplete = $true; Interrupted = (Test-FunctionInterrupt) }
} $SqlInstance $SqlCredential $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1

""";
}
