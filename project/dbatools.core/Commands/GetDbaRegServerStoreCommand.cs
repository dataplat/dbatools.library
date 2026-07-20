#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Gets the Registered Servers Store (CMS or local) for SQL Server instances. Port of
/// public/Get-DbaRegServerStore.ps1 (W3-049). Pure per-record process command with no begin/end
/// blocks. DEF-001 cond1+cond2: the process foreach EMITS a decorated store per instance
/// (Select-DefaultView) AND has reachable Stop-Function -Continue at Connect-DbaInstance and the
/// RegisteredServersStore construction, so the hop STREAMS via InvokeScopedStreaming. After the
/// foreach, a once-per-call block (guarded by "-not $PSBoundParameters.SqlInstance") initializes the
/// LOCAL file store via NonPublic reflection (InitChildObjects) - that reflection is ordinary
/// PowerShell running in module scope, so it is carried verbatim with no special C# handling; it
/// emits nothing. The source's $PSBoundParameters.SqlInstance check is carried as its VALUE-TRUTHINESS proxy - $PSBoundParameters.SqlInstance returns the VALUE (falsy for -SqlInstance @()), NOT mere boundness (the
/// scriptblock cannot see the real cmdlet's $PSBoundParameters). Cross-record-state: $server and
/// $store are FUNCTION-scoped (not per-iteration locals), and the local-store branch also runs for
/// a bound-but-falsy SqlInstance (@()) under the value-truthiness proxy - the no-carry conclusion
/// holds for the TRUE reason that every successful path ASSIGNS both variables before any read in
/// the same iteration, while every failure path Stop-Function -Continues past the reads, so a
/// prior record's value is never observable (assigned-vs-unset semantics never diverge; codex). No ShouldProcess. Positions match the retired
/// function (SqlInstance=0, SqlCredential=1; EnableException=switch/null). Substitutions only:
/// $PSBoundParameters.SqlInstance -> the carried $__pboundSqlInstance flag, explicit -FunctionName
/// Get-DbaRegServerStore on Stop-Function (W1-090); the body is otherwise verbatim. Surface pinned by
/// migration/baselines/Get-DbaRegServerStore.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaRegServerStore")]
public sealed class GetDbaRegServerStoreCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances (CMS host). Omit for the local file store.</summary>
    [Parameter(Position = 0, ValueFromPipeline = true)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Alternative credential for the target instances.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

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
            SqlInstance, SqlCredential, EnableException.ToBool(), BoundCommonParameter(nameof(SqlInstance)),
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

    // PS: the process body VERBATIM per record (no begin/end blocks). Substitutions only:
    // -not $PSBoundParameters.SqlInstance -> -not $__pboundSqlInstance (value-truthiness via BoundCommonParameter: IsTrue-or-null), explicit -FunctionName
    // Get-DbaRegServerStore on Stop-Function (W1-090).
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $EnableException, $__pboundSqlInstance, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, $EnableException, $__pboundSqlInstance, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    foreach ($instance in $SqlInstance) {
        try {
            $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Get-DbaRegServerStore
        }

        try {
            $store = New-Object Microsoft.SqlServer.Management.RegisteredServers.RegisteredServersStore($server.ConnectionContext)
        } catch {
            Stop-Function -Message "Cannot access Central Management Server on $instance" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Get-DbaRegServerStore
        }

        Add-Member -Force -InputObject $store -MemberType NoteProperty -Name ComputerName -value $server.ComputerName
        Add-Member -Force -InputObject $store -MemberType NoteProperty -Name InstanceName -value $server.ServiceName
        Add-Member -Force -InputObject $store -MemberType NoteProperty -Name SqlInstance -value $server.DomainInstanceName
        Add-Member -Force -InputObject $store -MemberType NoteProperty -Name ParentServer -value $server
        Select-DefaultView -InputObject $store -ExcludeProperty ServerConnection, DomainInstanceName, DomainName, Urn, Properties, Metadata, Parent, ConnectionContext, PropertyMetadataChanged, PropertyChanged, ParentServer
    }

    # Magic courtesy of Mathias Jessen and David Shifflet
    if (-not $__pboundSqlInstance) {
        $file = [Microsoft.SqlServer.Management.RegisteredServers.RegisteredServersStore]::LocalFileStore.DomainInstanceName
        if ($file) {
            if (-not (Test-Path -Path $file)) {
                $regfile = Join-DbaPath -Path $script:PSModuleRoot -ChildPath bin, RegSrvr.xml
                Copy-Item -Path $regfile -Destination $file -Force
            }
            $class = [Microsoft.SqlServer.Management.RegisteredServers.RegisteredServersStore]
            $initMethod = $class.GetMethod('InitChildObjects', [Reflection.BindingFlags]'Static,NonPublic')
            $initMethod.Invoke($null, @($file))
        }
    }
} $SqlInstance $SqlCredential $EnableException $__pboundSqlInstance $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
