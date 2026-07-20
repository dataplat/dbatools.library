#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Analyzes backup files to determine encryption status (backup encryption or TDE). Port of
/// public/Test-DbaBackupEncrypted.ps1 (W3-105). The function is process-only with no begin/end
/// and no cross-record state, so each pipeline record rides ONE VERBATIM module hop in
/// ProcessRecord (GetDbaExtendedProtection precedent): connect, then the per-$FilePath loop
/// running RESTORE HEADERONLY / FILELISTONLY and emitting one PSCustomObject per file, all
/// decided by the engine exactly as the function decided them (Convert-ByteToHexString,
/// [dbnull] comparisons, the thumbprint-exception branch). No $PSBoundParameters reads, so no
/// carriers. Surface pinned by migration/baselines/Test-DbaBackupEncrypted.json (SqlInstance
/// pos0 VFPBPN; SqlCredential pos1; FilePath pos2 mandatory VFPBPN alias FullName/Path; no SSP;
/// no OutputType).
///
/// DEF-001: the per-file Stop-Function -Continue (RESTORE HEADERONLY failure) throws under
/// -EnableException mid-loop, so objects already emitted for earlier files would be lost by a
/// buffered foreach - delivered via InvokeScopedStreaming, the streaming graft.
///
/// DEF-006: every hop-level Write-Message carries -FunctionName Test-DbaBackupEncrypted
/// -ModuleName "dbatools" (explicit -FunctionName suppresses frame auto-resolution, so
/// -ModuleName must be restored or it logs &lt;Unknown&gt;); the two Stop-Function calls carry
/// -FunctionName Test-DbaBackupEncrypted. No helpers, so every site is hop-level.
/// </summary>
[Cmdlet(VerbsDiagnostic.Test, "DbaBackupEncrypted")]
public sealed class TestDbaBackupEncryptedCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Position = 0, ValueFromPipelineByPropertyName = true)]
    public DbaInstanceParameter? SqlInstance { get; set; }

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The backup file path(s) to analyze for encryption status.</summary>
    [Parameter(Position = 2, Mandatory = true, ValueFromPipelineByPropertyName = true)]
    [Alias("FullName", "Path")]
    public string[]? FilePath { get; set; }

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
            }
            else
            {
                WriteObject(item);
            }
        }, ProcessScript,
            SqlInstance, SqlCredential, FilePath, EnableException.ToBool(),
            BoundCommonParameter("WhatIf"), BoundCommonParameter("Confirm"),
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug"));
    }

    private object? BoundCommonParameter(string name)
    {
        if (MyInvocation.BoundParameters.TryGetValue(name, out object? value))
            return LanguagePrimitives.IsTrue(value);
        return null;
    }

    /// <summary>Removes the silent $error copy the nested pipeline bagged for a merged-back
    /// non-terminating record (the W1-045 compensation).</summary>
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

    // PS: process body VERBATIM (single hop per record; no begin/end, no cross-record state).
    // Substitutions only: hop-level Write-Message gain -FunctionName Test-DbaBackupEncrypted
    // -ModuleName "dbatools"; hop-level Stop-Function gain -FunctionName Test-DbaBackupEncrypted.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $FilePath, $EnableException, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundWhatIf) { $__commonParameters.WhatIf = [bool]$__boundWhatIf }
if ($null -ne $__boundConfirm) { $__commonParameters.Confirm = [bool]$__boundConfirm }
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter]$SqlInstance, [PSCredential]$SqlCredential, [string[]]$FilePath, $EnableException, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    try {
        $server = Connect-DbaInstance -SqlInstance $SqlInstance -SqlCredential $SqlCredential
    } catch {
        Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $SqlInstance -FunctionName Test-DbaBackupEncrypted
        return
    }

    #for each database, create custom object for return set.
    foreach ($file in $FilePath) {
        $encrypted = $false
        $thumbprint = $null
        try {
            $file = $file.Replace("'", "''")
            $sql = "RESTORE HEADERONLY FROM DISK = N'$file'"
            Write-Message -Level Verbose -Message "SQL Query: $sql" -FunctionName Test-DbaBackupEncrypted -ModuleName "dbatools"
            $results = $server.Query($sql)
        } catch {
            Stop-Function -Message "Failure on $SqlInstance" -ErrorRecord $PSItem -Target $SqlInstance -Continue -FunctionName Test-DbaBackupEncrypted
        }

        if ($results.KeyAlgorithm -isnot [dbnull] -or
            $results.EncryptorThumbprint -isnot [dbnull] -or
            $results.EncryptorType -isnot [dbnull]) {

            Write-Message -Level Verbose -Message "KeyAlgorithm or EncryptorThumbprint or EncryptorType is not null" -FunctionName Test-DbaBackupEncrypted -ModuleName "dbatools"
            $encrypted = $true
        }

        try {
            $sql = "RESTORE FILELISTONLY FROM DISK = N'$file'"
            $filelistonly = $server.Query($sql)
            $thumb = ($filelistonly | Where-Object TDEThumbprint | Select-Object -First 1).TDEThumbprint

            if ($thumb.Length -gt 1) {
                Write-Message -Level Verbose -Message "Thumbprint found: $($filelistonly.TDEThumbprint)" -FunctionName Test-DbaBackupEncrypted -ModuleName "dbatools"
                $encrypted = $true
                $thumbprint = Convert-ByteToHexString $thumb
            }
        } catch {
            if ($PSItem -match "thumbprint") {
                Write-Message -Level Verbose -Message "Thumbprint referenced in exception" -FunctionName Test-DbaBackupEncrypted -ModuleName "dbatools"
                $encrypted = $true
            } else {
                Write-Message -Level Verbose -Message "Caught exception: $PSItem" -FunctionName Test-DbaBackupEncrypted -ModuleName "dbatools"
            }
        }

        Write-Message -Level Verbose -Message "Checking $file" -FunctionName Test-DbaBackupEncrypted -ModuleName "dbatools"
        [PSCustomObject]@{
            ComputerName        = $server.ComputerName
            InstanceName        = $server.ServiceName
            SqlInstance         = $server.DomainInstanceName
            FilePath            = $file
            BackupName          = $results.BackupName | Select-Object -First 1
            Encrypted           = $encrypted
            KeyAlgorithm        = $results.KeyAlgorithm | Select-Object -First 1
            EncryptorThumbprint = $results.EncryptorThumbprint | Select-Object -First 1
            EncryptorType       = $results.EncryptorType | Select-Object -First 1
            TDEThumbprint       = $thumbprint
            Compressed          = ($results | Select-Object -First 1).Compressed -eq $true
        }
    }
} $SqlInstance $SqlCredential $FilePath $EnableException $__boundWhatIf $__boundConfirm $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
