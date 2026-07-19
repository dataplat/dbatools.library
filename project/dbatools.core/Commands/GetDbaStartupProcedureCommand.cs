#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Gets SQL Server startup stored procedures. Port of public/Get-DbaStartupProcedure.ps1 (W3-053),
/// the read-only sibling of W3-008/W3-012 (Disable/Enable-DbaStartupProcedure). Pure per-record
/// process command with no begin/end blocks. DEF-001 cond1+cond2: the process foreach EMITS a
/// decorated procedure per match (Select-DefaultView) AND has a reachable Stop-Function -Continue at
/// Connect-DbaInstance, so the hop STREAMS via InvokeScopedStreaming. The source's
/// Test-Bound -ParameterName StartupProcedure guard is carried as a bound flag - the scriptblock runs
/// in module scope and cannot see the real cmdlet's $PSBoundParameters. No ShouldProcess. Positions
/// match the retired function (SqlInstance=0, SqlCredential=1, StartupProcedure=2;
/// EnableException=switch/null) and DefaultParameterSetName "Default" is preserved. Substitutions
/// only: Test-Bound -> the carried $__boundStartupProcedure flag, explicit -FunctionName
/// Get-DbaStartupProcedure on Stop-Function (W1-090); the body is otherwise verbatim (including the
/// source's undefined $servername in the verbose message). Surface pinned by
/// migration/baselines/Get-DbaStartupProcedure.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaStartupProcedure", DefaultParameterSetName = "Default")]
public sealed class GetDbaStartupProcedureCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Alternative credential for the target instances.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>Startup procedure name(s) to filter by (schema.name).</summary>
    [Parameter(Position = 2)]
    public string[]? StartupProcedure { get; set; }

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
            SqlInstance, SqlCredential, StartupProcedure, EnableException.ToBool(), TestBound(nameof(StartupProcedure)),
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
    // Test-Bound -ParameterName StartupProcedure -> the carried $__boundStartupProcedure flag,
    // explicit -FunctionName Get-DbaStartupProcedure on Stop-Function (W1-090).
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $StartupProcedure, $EnableException, $__boundStartupProcedure, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding(DefaultParameterSetName = "Default")]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [string[]]$StartupProcedure, $EnableException, $__boundStartupProcedure, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    foreach ($instance in $SqlInstance) {
        try {
            $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Get-DbaStartupProcedure
        }
        Write-Message -Level Verbose -Message "Getting startup procedures for $servername" -FunctionName Get-DbaStartupProcedure -ModuleName "dbatools"

        $startupProcs = $server.EnumStartupProcedures()

        if ($startupProcs.Rows.Count -gt 0) {
            $db = $server.Databases['master']
            foreach ($startupProc in $startupProcs) {
                if ($__boundStartupProcedure) {
                    $returnProc = $false
                    foreach ($proc in $StartupProcedure) {
                        $procParts = Get-ObjectNameParts $proc
                        if (-not $procParts.Parsed) {
                            Write-Message -Level Verbose -Message "Requested procedure $proc could not be parsed." -FunctionName Get-DbaStartupProcedure -ModuleName "dbatools"
                            Continue
                        }
                        if (($procParts.Name -eq $startupProc.Name) -and ($procParts.Schema -eq $startupProc.Schema)) {
                            $returnProc = $true
                            Break
                        }
                    }

                } else {
                    $returnProc = $true
                }
                if (!$returnProc) {
                    Continue
                }
                $proc = $db.StoredProcedures.Item($startupProc.Name, $startupProc.Schema)

                Add-Member -Force -InputObject $proc -MemberType NoteProperty -Name ComputerName -value $server.ComputerName
                Add-Member -Force -InputObject $proc -MemberType NoteProperty -Name InstanceName -value $server.ServiceName
                Add-Member -Force -InputObject $proc -MemberType NoteProperty -Name SqlInstance -value $server.DomainInstanceName
                Add-Member -Force -InputObject $proc -MemberType NoteProperty -Name Database -value $db.Name

                $defaults = 'ComputerName', 'InstanceName', 'SqlInstance', 'Database', 'Schema', 'ID as ObjectId', 'CreateDate',
                'DateLastModified', 'Name', 'ImplementationType', 'Startup'
                Select-DefaultView -InputObject $proc -Property $defaults
            }
        }
    }
} $SqlInstance $SqlCredential $StartupProcedure $EnableException $__boundStartupProcedure $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
