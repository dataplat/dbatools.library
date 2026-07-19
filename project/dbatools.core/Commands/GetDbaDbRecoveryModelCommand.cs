#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Gets the recovery model for SQL Server databases. Port of
/// public/Get-DbaDbRecoveryModel.ps1 (W3-029). The begin block only sets the $defaults display-column
/// array (a read-only constant consumed inside the same process body), so there is NO cross-record
/// accumulator and NO end hop - it inlines into the process script. The command has no Stop-Function
/// and no $PSCmdlet; it delegates to Get-DbaDatabase and streams the result through a filter into
/// Select-DefaultView. DEF-001 cond1+cond2: the pipeline EMITS per database AND a reachable throw can
/// come from Get-DbaDatabase under -EnableException, so the hop STREAMS via InvokeScopedStreaming - a
/// buffered hop would lose earlier databases' emits when a later instance throws. Positions match the
/// retired function (SqlInstance=0, SqlCredential=1, RecoveryModel=2, Database=3, ExcludeDatabase=4;
/// EnableException=switch/null) and RecoveryModel's ValidateSet (Simple/Full/BulkLogged) is preserved.
/// The body is fully verbatim (no substitutions). Surface pinned by
/// migration/baselines/Get-DbaDbRecoveryModel.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaDbRecoveryModel")]
public sealed class GetDbaDbRecoveryModelCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Alternative credential for the target instances.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>Recovery model(s) to filter by.</summary>
    [Parameter(Position = 2)]
    [ValidateSet("Simple", "Full", "BulkLogged")]
    public string[]? RecoveryModel { get; set; }

    /// <summary>Database(s) to include.</summary>
    [Parameter(Position = 3)]
    public object[]? Database { get; set; }

    /// <summary>Database(s) to exclude.</summary>
    [Parameter(Position = 4)]
    public object[]? ExcludeDatabase { get; set; }

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
            SqlInstance, SqlCredential, RecoveryModel, Database, ExcludeDatabase, EnableException.ToBool(),
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

    // PS: the begin block's $defaults display-column constant inlines ahead of the process body,
    // which is VERBATIM (no Stop-Function, no $PSCmdlet - no substitutions).
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $RecoveryModel, $Database, $ExcludeDatabase, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [string[]]$RecoveryModel, [object[]]$Database, [object[]]$ExcludeDatabase, $EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    $defaults = 'ComputerName', 'InstanceName', 'SqlInstance', 'Name', 'Status', 'IsAccessible', 'RecoveryModel',
    'LastBackupDate as LastFullBackup', 'LastDifferentialBackupDate as LastDiffBackup',
    'LastLogBackupDate as LastLogBackup'

    $params = @{
        SqlInstance     = $SqlInstance
        SqlCredential   = $SqlCredential
        Database        = $Database
        ExcludeDatabase = $ExcludeDatabase
        EnableException = $EnableException
    }

    if ($RecoveryModel) {
        Get-DbaDatabase @params | Where-Object RecoveryModel -in $RecoveryModel | Where-Object IsAccessible | Select-DefaultView -Property $defaults
    } else {
        Get-DbaDatabase @params | Select-DefaultView -Property $defaults
    }
} $SqlInstance $SqlCredential $RecoveryModel $Database $ExcludeDatabase $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
