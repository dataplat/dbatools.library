#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Disables Transparent Data Encryption on one or more databases, optionally dropping the encryption key.
/// </summary>
/// <remarks>
/// <para>
/// The workflow remains a module-scoped PowerShell compatibility hop so that the decryption wait loop,
/// the encryption-key drop, the progress reporting, the output shape, and dbatools stream and error
/// handling stay observable-identical to the script implementation.
/// </para>
/// <para>
/// The command is process-only, so it ships as a single hop per record. Database objects piped through
/// InputObject bind per record; when SqlInstance is supplied instead, the body fills InputObject from
/// Get-DbaDatabase within that record - so no cross-record state is carried.
/// </para>
/// </remarks>
[Cmdlet(VerbsLifecycle.Disable, "DbaDbEncryption", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.High, DefaultParameterSetName = "Default")]
public sealed class DisableDbaDbEncryptionCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Alternative credential for the target instances.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The database or databases to disable encryption on.</summary>
    [Parameter(Position = 2)]
    public string[]? Database { get; set; }

    /// <summary>Database objects, typically piped from Get-DbaDatabase.</summary>
    [Parameter(ValueFromPipeline = true, Position = 3)]
    public Microsoft.SqlServer.Management.Smo.Database[]? InputObject { get; set; }

    /// <summary>Keeps the database encryption key instead of dropping it after disabling encryption.</summary>
    [Parameter]
    public SwitchParameter NoEncryptionKeyDrop { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    /// <summary>Disables encryption for one pipeline record.</summary>
    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, ProcessScript,
            SqlInstance, SqlCredential, Database, InputObject, NoEncryptionKeyDrop.ToBool(),
            EnableException.ToBool(), this,
            BoundCommonParameter("WhatIf"), BoundCommonParameter("Confirm"),
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
        {
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                RemoveHopErrorBookkeeping(nestedError);
                WriteError(nestedError);
                continue;
            }
            WriteObject(item);
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

    // PS: the process body VERBATIM. Substitutions only: $Pscmdlet -> $__realCmdlet and -FunctionName
    // on the two direct Stop-Function calls (no nested named helper). The Write-ProgressHelper call
    // derives its progress Activity label from Get-PSCallStack, which resolves to this hop rather than
    // the public command name - a cosmetic, untested progress-bar difference of the same class as
    // DEF-006 in-hop attribution; the body is shipped as-is.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Database, $InputObject, $NoEncryptionKeyDrop, $EnableException, $__realCmdlet, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundWhatIf) { $__commonParameters.WhatIf = [bool]$__boundWhatIf }
if ($null -ne $__boundConfirm) { $__commonParameters.Confirm = [bool]$__boundConfirm }
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = "High")]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [System.Management.Automation.PSCredential]$SqlCredential, [string[]]$Database, [Microsoft.SqlServer.Management.Smo.Database[]]$InputObject, $NoEncryptionKeyDrop, $EnableException, $__realCmdlet, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    if ($SqlInstance) {
        if (-not $Database) {
            Stop-Function -Message "You must specify Database or ExcludeDatabase when using SqlInstance" -FunctionName Disable-DbaDbEncryption
            return
        }
        # all does not need to be addressed in the code because it gets all the dbs if $databases is empty
        $InputObject = Get-DbaDatabase -SqlInstance $SqlInstance -SqlCredential $SqlCredential -Database $Database
    }

    foreach ($db in $InputObject) {
        $server = $db.Parent
        if (-not $NoEncryptionKeyDrop) {
            $msg = "Disabling encryption on $($db.Name)"
        } else {
            $msg = "Disabling encryption on $($db.Name) will also drop the database encryption key. Continue?"
        }
        if ($__realCmdlet.ShouldProcess($server.Name, $msg)) {
            try {
                $db.EncryptionEnabled = $false
                $db.Alter()
                $stepCounter = 0
                do {
                    Start-Sleep 1
                    $db.Refresh()
                    Write-ProgressHelper -StepNumber ($stepCounter++) -Message "Disabling encryption for $($db.Name) on $($server.Name)" -TotalSteps 100
                    if ($stepCounter -eq 100) {
                        $stepCounter = 0
                    }
                    Write-Message -Level Verbose -Message "Database state for $($db.Name) on $($server.Name): $($db.DatabaseEncryptionKey.EncryptionState)" -FunctionName Disable-DbaDbEncryption
                }
                while ($db.DatabaseEncryptionKey.EncryptionState -notin "Unencrypted", "None")

                if (-not $NoEncryptionKeyDrop) {
                    # https://www.sqlservercentral.com/steps/stairway-to-tde-removing-tde-from-a-database
                    $null = $db.DatabaseEncryptionKey | Remove-DbaDbEncryptionKey
                }
                $db | Select-DefaultView -Property ComputerName, InstanceName, SqlInstance, 'Name as DatabaseName', EncryptionEnabled
            } catch {
                Stop-Function -Message "Failure" -ErrorRecord $_ -Continue -FunctionName Disable-DbaDbEncryption
            }
        }
    }
} $SqlInstance $SqlCredential $Database $InputObject $NoEncryptionKeyDrop $EnableException $__realCmdlet $__boundWhatIf $__boundConfirm $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
