#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Dataplat.Dbatools.Message;
using Microsoft.SqlServer.Management.Smo;

namespace Dataplat.Dbatools.Commands;

public sealed partial class RestoreDbaDatabaseCommand
{
    protected override void EndProcessing()
    {
        if (Interrupted || _skipEnd)
        {
            return;
        }
        // ($BackupHistory.Database | Sort-Object -Unique).count -gt 1 -and ('' -ne $DatabaseName)
        HashSet<string> uniqueDatabases = new(StringComparer.OrdinalIgnoreCase);
        foreach (object? history in _backupHistory)
            uniqueDatabases.Add(RestoreUtility.PsStringify(PsProperty.Get(history, "Database")));
        bool databaseNameTruthy = false;
        foreach (object? name in _databaseName ?? Array.Empty<object>())
        {
            if (!PsOps.Eq(name, ""))
            {
                databaseNameTruthy = true;
                break;
            }
        }
        if (uniqueDatabases.Count > 1 && databaseNameTruthy)
        {
            StopFunction("Multiple Databases' backups passed in, but only 1 name to restore them under. Stopping as cannot work out how to proceed", category: ErrorCategory.InvalidArgument);
            return;
        }
        if (!ParameterSetName.StartsWith("Restore", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (_backupHistory.Count == 0 && _restoreInstance!.VersionMajor != 8)
        {
            WriteMessage(MessageLevel.Warning, "No backups passed through. \n This could mean the SQL instance cannot see the referenced files, the file's headers could not be read or some other issue");
            return;
        }
        WriteMessage(MessageLevel.Verbose, $"Processing DatabaseName - {RestoreUtility.PsStringify(_databaseName)}");
        List<object?> filteredBackupHistory = new();
        if (TestBound("GetBackupInformation"))
        {
            WriteMessage(MessageLevel.Verbose, $"Setting {GetBackupInformation} to BackupHistory");
            SessionState.InvokeCommand.InvokeScript(false, ScriptBlock.Create("param($__name, $__value) Set-Variable -Name $__name -Value $__value -Scope Global"), null, GetBackupInformation, _backupHistory.ToArray());
        }
        if (StopAfterGetBackupInformation.ToBool())
        {
            return;
        }
        string pathSep = RestoreUtility.GetPathSep(_restoreInstance);
        Hashtable formatParms = new()
        {
            ["DataFileDirectory"] = DestinationDataDirectory,
            ["LogFileDirectory"] = DestinationLogDirectory,
            ["DestinationFileStreamDirectory"] = DestinationFileStreamDirectory,
            ["DatabaseFileSuffix"] = DestinationFileSuffix,
            ["DatabaseFilePrefix"] = DestinationFilePrefix,
            ["DatabaseNamePrefix"] = RestoredDatabaseNamePrefix,
            ["ReplaceDatabaseName"] = _databaseName,
            ["Continue"] = Continue.ToBool(),
            ["ReplaceDbNameInFile"] = ReplaceDbNameInFile.ToBool(),
            ["FileMapping"] = FileMapping,
            ["PathSep"] = pathSep
        };
        List<object?> formatted = new();
        foreach (PSObject item in NestedCommand.Invoke(this, "Format-DbaBackupInformation", formatParms, _backupHistory.ToArray()))
            formatted.Add(item);
        _backupHistory.Clear();
        _backupHistory.AddRange(formatted);

        if (TestBound("FormatBackupInformation"))
        {
            SessionState.InvokeCommand.InvokeScript(false, ScriptBlock.Create("param($__name, $__value) Set-Variable -Name $__name -Value $__value -Scope Global"), null, FormatBackupInformation, _backupHistory.ToArray());
        }
        if (StopAfterFormatBackupInformation.ToBool())
        {
            return;
        }
        if (VerifyOnly.ToBool())
        {
            filteredBackupHistory = new List<object?>(_backupHistory);
        }
        else
        {
            Hashtable selectParms = new()
            {
                ["RestoreTime"] = RestoreTime,
                // PS passes the never-assigned $IgnoreLogBackups here (note the trailing s) —
                // the switch binds $null and reads as false. Preserved.
                ["IgnoreLogs"] = null,
                ["IgnoreDiffs"] = IgnoreDiffBackup.ToBool(),
                ["ContinuePoints"] = _continuePoints,
                ["LastRestoreType"] = _lastRestoreType,
                ["DatabaseName"] = _databaseName
            };
            foreach (PSObject item in NestedCommand.Invoke(this, "Select-DbaBackupInformation", selectParms, _backupHistory.ToArray()))
                filteredBackupHistory.Add(item);
        }
        if (TestBound("SelectBackupInformation"))
        {
            WriteMessage(MessageLevel.Verbose, $"Setting {SelectBackupInformation} to FilteredBackupHistory");
            SessionState.InvokeCommand.InvokeScript(false, ScriptBlock.Create("param($__name, $__value) Set-Variable -Name $__name -Value $__value -Scope Global"), null, SelectBackupInformation, filteredBackupHistory.ToArray());
        }
        if (StopAfterSelectBackupInformation.ToBool())
        {
            return;
        }
        try
        {
            WriteMessage(MessageLevel.Verbose, $"VerifyOnly = {PsBool.Text(VerifyOnly.ToBool())}");
            Hashtable testParms = new()
            {
                ["SqlInstance"] = _restoreInstance,
                ["WithReplace"] = _withReplace,
                ["Continue"] = Continue.ToBool(),
                ["VerifyOnly"] = VerifyOnly.ToBool(),
                ["EnableException"] = true,
                ["OutputScriptOnly"] = OutputScriptOnly.ToBool()
            };
            NestedCommand.Invoke(this, "Test-DbaBackupInformation", testParms, filteredBackupHistory.ToArray());
        }
        catch (Exception ex)
        {
            ErrorRecord record = new(ex, "dbatools_Restore-DbaDatabase", ErrorCategory.NotSpecified, null);
            StopFunction("Failure", errorRecord: record, continueLoop: true);
            return;
        }
        if (TestBound("TestBackupInformation"))
        {
            SessionState.InvokeCommand.InvokeScript(false, ScriptBlock.Create("param($__name, $__value) Set-Variable -Name $__name -Value $__value -Scope Global"), null, TestBackupInformation, filteredBackupHistory.ToArray());
        }
        if (StopAfterTestBackupInformation.ToBool())
        {
            return;
        }
        List<object?> verified = new();
        List<object?> unverified = new();
        foreach (object? history in filteredBackupHistory)
        {
            // PS: Where-Object { $_.IsVerified -eq $True } / { $_.IsVerified -eq $False } —
            // entries with neither value fall into neither list, exactly like PS.
            if (PsOps.Eq(PsProperty.Get(history, "IsVerified"), true))
                verified.Add(history);
            else if (PsOps.Eq(PsProperty.Get(history, "IsVerified"), false))
                unverified.Add(history);
        }
        string dbVerified = JoinUniqueDatabases(verified);
        WriteMessage(MessageLevel.Verbose, $"{dbVerified} passed testing");
        if (verified.Count < filteredBackupHistory.Count)
        {
            string dbUnverified = JoinUniqueDatabases(unverified);
            // PS has no return after this Stop-Function: under -EnableException it throws,
            // otherwise the verified subset still restores below.
            StopFunction($"Database {dbUnverified} unable to be restored, see warnings for details");
        }
        if (ParameterSetName == "RestorePage")
        {
            HashSet<string> pageDatabases = new(StringComparer.OrdinalIgnoreCase);
            foreach (object? history in filteredBackupHistory)
                pageDatabases.Add(RestoreUtility.PsStringify(PsProperty.Get(history, "Database")));
            if (pageDatabases.Count != 1)
            {
                StopFunction("Must only 1 database passed in for Page Restore. Sorry");
                return;
            }
            else
            {
                _withReplace = false;
            }
        }
        WriteMessage(MessageLevel.Verbose, "Passing in to restore");

        if (ParameterSetName == "RestorePage" && _restoreInstance!.Edition.IndexOf("Enterprise", StringComparison.OrdinalIgnoreCase) < 0)
        {
            WriteMessage(MessageLevel.Verbose, "Taking Tail log backup for page restore for non-Enterprise");
            TakeTailBackup();
        }
        try
        {
            Hashtable restoreParms = new()
            {
                ["SqlInstance"] = _restoreInstance,
                ["WithReplace"] = _withReplace,
                ["RestoreTime"] = RestoreTime,
                ["StandbyDirectory"] = StandbyDirectory,
                ["NoRecovery"] = NoRecovery.ToBool(),
                ["Continue"] = Continue.ToBool(),
                ["OutputScriptOnly"] = OutputScriptOnly.ToBool(),
                ["BlockSize"] = BlockSize,
                ["MaxTransferSize"] = MaxTransferSize,
                ["BufferCount"] = BufferCount,
                ["KeepCDC"] = KeepCDC.ToBool(),
                ["ErrorBrokerConversations"] = ErrorBrokerConversations.ToBool(),
                ["VerifyOnly"] = VerifyOnly.ToBool(),
                ["PageRestore"] = PageRestore,
                ["StorageCredential"] = StorageCredential,
                ["KeepReplication"] = KeepReplication.ToBool(),
                ["StopMark"] = StopMark,
                ["StopAfterDate"] = StopAfterDate,
                ["StopBefore"] = StopBefore.ToBool(),
                ["StopAtLsn"] = StopAtLsn,
                ["ExecuteAs"] = ExecuteAs,
                ["Checksum"] = Checksum.ToBool(),
                ["Restart"] = Restart.ToBool(),
                ["EnableException"] = true
            };
            if (TestBound("WhatIf"))
            {
                restoreParms["WhatIf"] = true;
            }
            if (TestBound("Confirm"))
            {
                restoreParms["Confirm"] = MyInvocation.BoundParameters["Confirm"];
            }
            NestedCommand.InvokeStreamed(this, "Invoke-DbaAdvancedRestore", restoreParms, verified);
        }
        catch (Exception ex)
        {
            ErrorRecord record = new(ex, "dbatools_Restore-DbaDatabase", ErrorCategory.NotSpecified, _restoreInstance);
            StopFunction("Failure", target: _restoreInstance, errorRecord: record, continueLoop: true);
            return;
        }
        if (ParameterSetName == "RestorePage")
        {
            if (_restoreInstance!.Edition.IndexOf("Enterprise", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                WriteMessage(MessageLevel.Verbose, "Taking Tail log backup for page restore for Enterprise");
                TakeTailBackup();
            }
            WriteMessage(MessageLevel.Verbose, "Restoring Tail log backup for page restore");
            Hashtable tailRestoreParms = new()
            {
                ["SqlInstance"] = _restoreInstance,
                ["TrustDbBackupHistory"] = true,
                ["NoRecovery"] = true,
                ["OutputScriptOnly"] = OutputScriptOnly.ToBool(),
                ["BlockSize"] = BlockSize,
                ["MaxTransferSize"] = MaxTransferSize,
                ["BufferCount"] = BufferCount,
                ["Continue"] = true
            };
            if (TestBound("WhatIf"))
            {
                tailRestoreParms["WhatIf"] = true;
            }
            NestedCommand.InvokeStreamed(this, "Restore-DbaDatabase", tailRestoreParms, _tailBackup ?? new List<object?>());

            Hashtable recoverParms = new()
            {
                ["SqlInstance"] = _restoreInstance,
                ["Recover"] = true,
                ["DatabaseName"] = _databaseName,
                ["OutputScriptOnly"] = OutputScriptOnly.ToBool()
            };
            if (TestBound("WhatIf"))
            {
                recoverParms["WhatIf"] = true;
            }
            foreach (PSObject recovered in NestedCommand.Invoke(this, "Restore-DbaDatabase", recoverParms))
                WriteObject(recovered);
        }
        // refresh the SMO as we probably used T-SQL, but only if we already got a SMO
        if (SqlInstance.InputObject is Server inputServer)
        {
            inputServer.Databases.Refresh();
        }
    }

    private List<object?>? _tailBackup;

    private void TakeTailBackup()
    {
        Hashtable backupParms = new()
        {
            ["SqlInstance"] = _restoreInstance,
            ["Database"] = _databaseName,
            ["Type"] = "Log",
            ["BackupDirectory"] = PageRestoreTailFolder,
            ["NoRecovery"] = true,
            ["CopyOnly"] = true
        };
        if (TestBound("WhatIf"))
        {
            backupParms["WhatIf"] = true;
        }
        _tailBackup = new List<object?>();
        foreach (PSObject item in NestedCommand.Invoke(this, "Backup-DbaDatabase", backupParms))
            _tailBackup.Add(item);
    }

    private static string JoinUniqueDatabases(List<object?> histories)
    {
        // (X | Sort-Object -Property Database -Unique).Database -join ','
        List<string> names = new();
        foreach (object? history in histories)
            names.Add(RestoreUtility.PsStringify(PsProperty.Get(history, "Database")));
        names.Sort(StringComparer.CurrentCultureIgnoreCase);
        List<string> unique = new();
        foreach (string name in names)
        {
            if (unique.Count == 0 || !string.Equals(unique[unique.Count - 1], name, StringComparison.CurrentCultureIgnoreCase))
                unique.Add(name);
        }
        return string.Join(",", unique);
    }
}
