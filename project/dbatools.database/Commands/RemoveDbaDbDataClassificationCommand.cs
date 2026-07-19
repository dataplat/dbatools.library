#nullable enable

using System;
using System.Collections;
using System.Management.Automation;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Drops database synonyms. Port of public/Remove-DbaDbDataClassification.ps1; the workflow remains a
/// module-scoped PowerShell compatibility hop.
///
/// Process-only: the source has no begin or end block, so the hop is a single per-record
/// invocation. No local needs a cross-record carry - $input, $inputType, $dbSynonyms, $dbSynonym,
/// $db and $instance are each assigned and read inside one loop iteration, and $InputObject is
/// re-bound from the pipeline on every record before the body re-points it at $SqlInstance.
///
/// No graceful-stop latch: the source has no Test-FunctionInterrupt guard, so a later record
/// re-enters the process body and re-evaluates its input guard, warning again. Carrying a latch
/// would suppress warnings the function repeats. There are no Test-Bound call sites.
///
/// The ShouldProcess gate is routed to the OUTER cmdlet. ConfirmImpact is High, so this command
/// prompts by default and -Confirm's "Yes to All" answer - which lives on the invoking runtime -
/// must survive between records rather than being forgotten by a per-record inner runtime.
///
/// Two source shapes ship unchanged because parity is the contract. The element loop variable is
/// $input, shadowing the PowerShell automatic; that was probed across the function,
/// module-scriptblock and production-hop shapes and behaves identically in all three, so it needs
/// no shim. And the default branch of the input-type switch does Stop-Function then a bare return,
/// which abandons the whole record rather than just that element.
///
/// The hop streams rather than buffers. This command DROPS synonyms and each emitted object records
/// one that was actually dropped, so a buffered invocation would discard the audit trail of
/// completed drops if a later synonym threw under -EnableException.
/// </summary>
[Cmdlet(VerbsCommon.Remove, "DbaDbDataClassification", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.High)]
[OutputType(typeof(PSObject))]
public sealed class RemoveDbaDbDataClassificationCommand : DbaBaseCmdlet
{
    /// <summary>Classification objects, from Get-DbaDbDataClassification.</summary>
    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true)]
    public PSObject[]? InputObject { get; set; }
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
        }, BodyScript,
            InputObject, EnableException.ToBool(), this,
            BoundCommonParameter("WhatIf"), BoundCommonParameter("Confirm"),
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

    // PS: the source's process body VERBATIM. Substitutions only: $PSCmdlet -> $__realCmdlet so the
    // gate is owned by the outer cmdlet, and -FunctionName on Stop-Function/Write-Message. The body
    // is embedded WITHOUT added indentation, since indenting rewrites multi-line string literals.
    private const string BodyScript = """
param($InputObject, $EnableException, $__realCmdlet, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundWhatIf) { $__commonParameters.WhatIf = [bool]$__boundWhatIf }
if ($null -ne $__boundConfirm) { $__commonParameters.Confirm = [bool]$__boundConfirm }
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = "High")]
    param([psobject[]]$InputObject, $EnableException, $__realCmdlet, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

        foreach ($classObj in $InputObject) {
            $db = $classObj.DatabaseObject
            if (-not $db) {
                Stop-Function -Message "No database object found in input. Use Get-DbaDbDataClassification to get valid input objects." -Continue -FunctionName Remove-DbaDbDataClassification
                continue
            }

            $server = $db.Parent
            $schemaName = $classObj.Schema
            $tableName = $classObj.Table
            $columnName = $classObj.Column
            $target = "[$schemaName].[$tableName].[$columnName] in $($db.Name) on $server"

            if ($__realCmdlet.ShouldProcess($target, "Removing data classification")) {
                $escapedSchema = $schemaName.Replace("'", "''")
                $escapedTable = $tableName.Replace("'", "''")
                $escapedColumn = $columnName.Replace("'", "''")

                $status = "Removed"
                $errors = @()

                foreach ($propName in "sys_information_type_id", "sys_information_type_name", "sys_sensitivity_label_id", "sys_sensitivity_label_name") {
                    $checkSql = "
SELECT COUNT(1) AS PropExists
FROM sys.extended_properties ep
INNER JOIN sys.objects o ON ep.major_id = o.object_id
INNER JOIN sys.columns c ON o.object_id = c.object_id AND ep.minor_id = c.column_id
WHERE SCHEMA_NAME(o.schema_id) = '$escapedSchema'
  AND o.name = '$escapedTable'
  AND c.name = '$escapedColumn'
  AND ep.name = '$propName'
  AND ep.class = 1"

                    try {
                        $exists = $db.Query($checkSql).PropExists
                        if ($exists -gt 0) {
                            $dropSql = "EXEC sys.sp_dropextendedproperty @name = N'$propName', @level0type = N'SCHEMA', @level0name = N'$escapedSchema', @level1type = N'TABLE', @level1name = N'$escapedTable', @level2type = N'COLUMN', @level2name = N'$escapedColumn'"
                            $db.Query($dropSql)
                        }
                    } catch {
                        $errors += $propName
                        Write-Message -Level Warning -Message "Failed to drop extended property '$propName' from $target : $_" -FunctionName Remove-DbaDbDataClassification
                    }
                }

                if ($errors.Count -gt 0) {
                    $status = "Partial - failed to remove: $($errors -join ', ')"
                }

                [PSCustomObject]@{
                    ComputerName = $classObj.ComputerName
                    InstanceName = $classObj.InstanceName
                    SqlInstance  = $classObj.SqlInstance
                    Database     = $classObj.Database
                    Schema       = $schemaName
                    Table        = $tableName
                    Column       = $columnName
                    Status       = $status
                }
            }
        }

} $InputObject $EnableException $__realCmdlet $__boundWhatIf $__boundConfirm $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
