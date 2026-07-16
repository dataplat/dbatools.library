#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Renames databases, filegroups, logical files and physical filenames. Port of
/// public/Rename-DbaDatabase.ps1 (W3-081) - the largest lane-D hop port (620-line process
/// body, verbatim). Begin block splits three ways per the established laws: the
/// SetOffline-without-FileName validation and $CurrentDate (Get-Date, midnight-stable only
/// within a day) ride a BEGIN hop whose sentinel carries the Stop-Function LATCH state
/// (the W1-108/W3-066 class: the source gates every process record on
/// Test-FunctionInterrupt, whose function-scope latch cannot cross hop scopes - the C#
/// _hopInterrupted flag replays it, fed by both the begin and per-record process
/// sentinels); the Copy-family `if ($Force) { $ConfirmPreference = 'none' }` line rides at
/// PROCESS-hop top with the INNER $PSCmdlet serving every ShouldProcess gate (W3-005/
/// W3-064 convention - NO $__realCmdlet in this port); the two begin-scope helper
/// functions (Get-DbaNameStructure, Get-DbaKeyByValue) are re-declared at process-hop top
/// (function definitions cannot cross hop scopes). The process body rides VERBATIM inside
/// a dot-sourced block (two `Stop-Function; return` early exits re-fire per record); all
/// per-record state ($InstanceDbs/$InstanceFiles/$Pending_Renames/$Entities_Before) is
/// record-local in the source. Mechanical W1-090 pass: -FunctionName Rename-DbaDatabase
/// appended to all 26 Stop-Function/Write-Message call sites. Private/dependency calls
/// ride the hop (Get-DbaFile, Test-PSRemoting, Resolve-DbaNetworkName, Join-AdminUnc,
/// Invoke-Command2, Set-DbaDbState, Select-DefaultView, [DbaValidate]). NO
/// DefaultParameterSetName (the source declares none - zero-determining-arg invocations
/// fail set resolution identically). NO WarningAction carrier (codex W3-005 r3). Surface
/// pinned by migration/baselines/Rename-DbaDatabase.json (sets Server {SqlInstance
/// Mandatory, Database} + Pipe {InputObject Database[] Mandatory VFP}, no positions,
/// ConfirmImpact Medium).
/// </summary>
[Cmdlet(VerbsCommon.Rename, "DbaDatabase", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Medium)]
public sealed class RenameDbaDatabaseCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ParameterSetName = "Server")]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Alternative credential for the target instances.</summary>
    [Parameter]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The database(s) to process.</summary>
    [Parameter(ParameterSetName = "Server")]
    public object[]? Database { get; set; }

    /// <summary>Database(s) to exclude.</summary>
    [Parameter]
    public object[]? ExcludeDatabase { get; set; }

    /// <summary>Process all databases.</summary>
    [Parameter]
    public SwitchParameter AllDatabases { get; set; }

    /// <summary>New database name template (&lt;DBN&gt;/&lt;DATE&gt; placeholders).</summary>
    [Parameter]
    public string? DatabaseName { get; set; }

    /// <summary>New filegroup name template.</summary>
    [Parameter]
    public string? FileGroupName { get; set; }

    /// <summary>New logical file name template.</summary>
    [Parameter]
    public string? LogicalName { get; set; }

    /// <summary>New physical file name template.</summary>
    [Parameter]
    public string? FileName { get; set; }

    /// <summary>Strip pre-existing entity names from placeholders before composing.</summary>
    [Parameter]
    public SwitchParameter ReplaceBefore { get; set; }

    /// <summary>Kills connections and suppresses prompts (ConfirmPreference override).</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    /// <summary>Performs the physical file moves after renaming.</summary>
    [Parameter]
    public SwitchParameter Move { get; set; }

    /// <summary>Sets the database offline after filename renames.</summary>
    [Parameter]
    public SwitchParameter SetOffline { get; set; }

    /// <summary>Shows what would be renamed without doing it.</summary>
    [Parameter]
    public SwitchParameter Preview { get; set; }

    /// <summary>SMO Database object(s), typically from Get-DbaDatabase.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, ParameterSetName = "Pipe")]
    public Microsoft.SqlServer.Management.Smo.Database[]? InputObject { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // begin-computed $CurrentDate rides the state bag; the begin/process Stop-Function
    // latch replays through _hopInterrupted (Test-FunctionInterrupt cannot cross hops).
    private Hashtable? _state;
    private bool _hopInterrupted;

    protected override void BeginProcessing()
    {
        foreach (PSObject? item in NestedCommand.InvokeScoped(this, BeginScript,
            EnableException.ToBool(),
            TestBound(nameof(SetOffline)), TestBound(nameof(FileName)),
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
        {
            Hashtable? sentinel = item?.BaseObject as Hashtable;
            if (sentinel is not null && sentinel.ContainsKey("__w3081State"))
            {
                _state = sentinel["__w3081State"] as Hashtable;
                if (_state is not null && _state["interrupted"] is bool interrupted && interrupted)
                    _hopInterrupted = true;
                continue;
            }
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                RemoveHopErrorBookkeeping(nestedError);
                WriteError(nestedError);
                continue;
            }
            WriteObject(item);
        }
    }

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, ProcessScript,
            SqlInstance, SqlCredential, Database, ExcludeDatabase, AllDatabases.ToBool(),
            DatabaseName, FileGroupName, LogicalName, FileName, ReplaceBefore.ToBool(),
            Force.ToBool(), Move.ToBool(), SetOffline.ToBool(), Preview.ToBool(),
            InputObject, EnableException.ToBool(), _state, _hopInterrupted,
            BoundCommonParameter("WhatIf"), BoundCommonParameter("Confirm"),
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
        {
            Hashtable? sentinel = item?.BaseObject as Hashtable;
            if (sentinel is not null && sentinel.ContainsKey("__w3081State"))
            {
                Hashtable? latch = sentinel["__w3081State"] as Hashtable;
                if (latch is not null && latch["interrupted"] is bool interrupted && interrupted)
                    _hopInterrupted = true;
                continue;
            }
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

    // PS: the begin-block validation + $CurrentDate VERBATIM (helper functions and the
    // ConfirmPreference line live at process-hop top instead - see the class doc). The
    // sentinel carries $CurrentDate plus the Stop-Function latch state.
    private const string BeginScript = """
param($EnableException, $__boundSetOffline, $__boundFileName, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param($EnableException, $__boundSetOffline, $__boundFileName, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    $CurrentDate = Get-Date -Format 'yyyyMMdd'

    if (($__boundSetOffline) -and (-not($__boundFileName))) {
        Stop-Function -Category InvalidArgument -Message "-SetOffline is only useful when -FileName is passed. Quitting." -FunctionName Rename-DbaDatabase
    }

    @{ __w3081State = @{ CurrentDate = $CurrentDate; interrupted = (Test-FunctionInterrupt) } }
} $EnableException $__boundSetOffline $__boundFileName $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";

    // PS: the ENTIRE process body VERBATIM per record inside a dot-sourced block (two
    // validation early returns re-fire per record). Substitutions only: the
    // Test-FunctionInterrupt gate -> the carried $__hopInterrupted flag, and the
    // mechanical -FunctionName Rename-DbaDatabase appends (W1-090) on every
    // Stop-Function/Write-Message. $PSCmdlet stays UNSUBSTITUTED: the inner block's own
    // cmdlet serves the gates so the verbatim Force/ConfirmPreference override works
    // (W3-005/W3-064 Copy-family convention). The trailing sentinel re-reads the latch so
    // a mid-record no-Continue Stop-Function suppresses LATER records like the source.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Database, $ExcludeDatabase, $AllDatabases, $DatabaseName, $FileGroupName, $LogicalName, $FileName, $ReplaceBefore, $Force, $Move, $SetOffline, $Preview, $InputObject, $EnableException, $__state, $__hopInterrupted, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundWhatIf) { $__commonParameters.WhatIf = [bool]$__boundWhatIf }
if ($null -ne $__boundConfirm) { $__commonParameters.Confirm = [bool]$__boundConfirm }
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = "Medium")]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [object[]]$Database, [object[]]$ExcludeDatabase, $AllDatabases, [string]$DatabaseName, [string]$FileGroupName, [string]$LogicalName, [string]$FileName, $ReplaceBefore, $Force, $Move, $SetOffline, $Preview, [Microsoft.SqlServer.Management.Smo.Database[]]$InputObject, $EnableException, $__state, $__hopInterrupted, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    # begin-block lines that must live in the hop scope: the Copy-family ConfirmPreference
    # override (the INNER $PSCmdlet serves every gate - W3-005/W3-064 convention) and the
    # two helper functions (function definitions cannot cross hop scopes)
    if ($Force) { $ConfirmPreference = 'none' }

    $CurrentDate = $__state.CurrentDate

        function Get-DbaNameStructure($database) {
            $obj = @()
            # db name
            $obj += "- Database : $database"
            # FileGroups
            foreach ($fg in $database.FileGroups) {
                $obj += "  - FileGroup: $($fg.Name)"
                # LogicalNames
                foreach ($ln in $fg.Files) {
                    $obj += "    - Logical: $($ln.Name)"
                    $obj += "      - FileName: $($ln.FileName)"
                }
            }
            $obj += "  - Logfiles"
            foreach ($log in $database.LogFiles) {
                $obj += "    - Logical: $($log.Name)"
                $obj += "      - FileName: $($log.FileName)"
            }
            return $obj -Join "`n"
        }


        function Get-DbaKeyByValue($hashtable, $Value) {
            ($hashtable.GetEnumerator() | Where-Object Value -eq $Value).Name
        }

    . {
        if ($__hopInterrupted) { return }
        if (!$Database -and !$AllDatabases -and !$InputObject -and !$ExcludeDatabase) {
            Stop-Function -Message "You must specify a -AllDatabases or -Database/ExcludeDatabase to continue" -FunctionName Rename-DbaDatabase
            return
        }
        if (!$DatabaseName -and !$FileGroupName -and !$LogicalName -and !$FileName) {
            Stop-Function -Message "You must specify at least one of -DatabaseName,-FileGroupName,-LogicalName or -Filename to continue" -FunctionName Rename-DbaDatabase
            return
        }
        $dbs = @()
        if ($InputObject) {
            if ($InputObject.Name) {
                # comes from Get-DbaDatabase
                $dbs += $InputObject
            }
        } else {
            foreach ($instance in $SqlInstance) {
                try {
                    $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $sqlCredential
                } catch {
                    Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Rename-DbaDatabase
                }
                $all_dbs = $server.Databases | Where-Object IsAccessible
                $dbs += $all_dbs | Where-Object { @('master', 'model', 'msdb', 'tempdb', 'distribution') -notcontains $_.Name }
                if ($Database) {
                    $dbs = $dbs | Where-Object { $Database -contains $_.Name }
                }
                if ($ExcludeDatabase) {
                    $dbs = $dbs | Where-Object { $ExcludeDatabase -notcontains $_.Name }
                }
            }
        }

        # holds all dbs per instance to avoid naming clashes
        $InstanceDbs = @{ }

        # holds all db file enumerations (used for -Move only)
        $InstanceFiles = @{ }

        #region db loop
        foreach ($db in $dbs) {
            # used to stop futher operations on database
            $failed = $false

            # pending renames initialized at db level
            $Pending_Renames = @()

            $Entities_Before = @{ }

            $server = $db.Parent
            if ($db.Name -in @('master', 'model', 'msdb', 'tempdb', 'distribution')) {
                Write-Message -Level Warning -Message "Database $($db.Name) is a system one, skipping..." -FunctionName Rename-DbaDatabase
                continue
            }
            if (!$db.IsAccessible) {
                Write-Message -Level Warning -Message "Database $($db.Name) is not accessible, skipping..." -FunctionName Rename-DbaDatabase
                continue
            }
            if ($db.IsMirroringEnabled -eq $true -or $db.AvailabilityGroupName.Length -gt 0) {
                Write-Message -Level Warning -Message "Database $($db.Name) is either mirrored or in an AG, skipping..." -FunctionName Rename-DbaDatabase
                continue
            }
            $Server_Id = $server.DomainInstanceName
            if ( !$InstanceDbs.ContainsKey($Server_Id) ) {
                $InstanceDbs[$Server_Id] = @{ }
                foreach ($dn in $server.Databases.Name) {
                    $InstanceDbs[$Server_Id][$dn] = 1
                }
            }

            $Entities_Before['DBN'] = @{ }
            $Entities_Before['FGN'] = @{ }
            $Entities_Before['LGN'] = @{ }
            $Entities_Before['FNN'] = @{ }
            $Entities_Before['DBN'][$db.Name] = $db.Name
            #region databasename
            if ($DatabaseName) {
                $Orig_DBName = $db.Name
                # fixed replacements
                $NewDBName = $DatabaseName.Replace('<DBN>', $Orig_DBName).Replace('<DATE>', $CurrentDate)
                if ($Orig_DBName -eq $NewDBName) {
                    Write-Message -Level VeryVerbose -Message "Database name unchanged, skipping" -FunctionName Rename-DbaDatabase
                } else {
                    if ($InstanceDbs[$Server_Id].ContainsKey($NewDBName)) {
                        Write-Message -Level Warning -Message "Database $NewDBName exists already, skipping this rename" -FunctionName Rename-DbaDatabase
                        $failed = $true
                    } else {
                        if ($PSCmdlet.ShouldProcess($db, "Renaming Database $db to $NewDBName")) {
                            if ($Force) {
                                $server.KillAllProcesses($Orig_DBName)
                            }
                            try {
                                if (!$Preview) {
                                    $db.Rename($NewDBName)
                                }
                                $InstanceDbs[$Server_Id].Remove($Orig_DBName)
                                $InstanceDbs[$Server_Id][$NewDBName] = 1
                                $Entities_Before['DBN'][$Orig_DBName] = $NewDBName
                                #$db.Refresh()
                            } catch {
                                Stop-Function -Message "Failed to rename Database : $($_.Exception.InnerException.InnerException.InnerException)" -ErrorRecord $_ -Target $server.DomainInstanceName -OverrideExceptionMessage -FunctionName Rename-DbaDatabase
                                # stop any further renames
                                $failed = $true
                            }
                        }
                    }
                }
            }
            #endregion databasename
            #region filegroupname
            if ($ReplaceBefore) {
                #backfill PRIMARY
                $Entities_Before['FGN']['PRIMARY'] = 'PRIMARY'
                foreach ($fg in $db.FileGroups.Name) {
                    $Entities_Before['FGN'][$fg] = $fg
                }
            }

            if (!$failed -and $FileGroupName) {
                $Editable_FGs = $db.FileGroups | Where-Object Name -ne 'PRIMARY'
                $New_FGNames = @{ }
                foreach ($fg in $db.FileGroups.Name) {
                    $New_FGNames[$fg] = 1
                }
                $FGCounter = 0
                foreach ($fg in $Editable_FGs) {
                    $Orig_FGName = $fg.Name
                    $Orig_Placeholder = $Orig_FGName
                    if ($ReplaceBefore) {
                        # at Filegroup level, we need to worry about database name
                        $Orig_Placeholder = $Orig_Placeholder.Replace($Entities_Before['DBN'][$Orig_DBName], '')
                    }
                    $NewFGName = $FileGroupName.Replace('<DBN>', $Entities_Before['DBN'][$db.Name]).Replace('<DATE>', $CurrentDate).Replace('<FGN>', $Orig_Placeholder)
                    $FinalFGName = $NewFGName
                    while ($fg.Name -ne $FinalFGName) {
                        if ($FinalFGName -in $New_FGNames.Keys) {
                            $FGCounter += 1
                            $FinalFGName = "$NewFGName$($FGCounter.ToString('000'))"
                        } else {
                            break
                        }
                    }
                    if ($fg.Name -eq $FinalFGName) {
                        Write-Message -Level VeryVerbose -Message "No rename necessary for FileGroup $($fg.Name) (on $db)" -FunctionName Rename-DbaDatabase
                        continue
                    }
                    if ($PSCmdlet.ShouldProcess($db, "Renaming FileGroup $($fg.Name) to $FinalFGName")) {
                        try {
                            if (!$Preview) {
                                $fg.Rename($FinalFGName)
                            }
                            $New_FGNames.Remove($Orig_FGName)
                            $New_FGNames[$FinalFGName] = 1
                            $Entities_Before['FGN'][$Orig_FGName] = $FinalFGName
                        } catch {
                            Stop-Function -Message "Failed to rename FileGroup : $($_.Exception.InnerException.InnerException.InnerException)" -ErrorRecord $_ -Target $server.DomainInstanceName -OverrideExceptionMessage -FunctionName Rename-DbaDatabase
                            # stop any further renames
                            $failed = $true
                            break
                        }
                    }
                }
                #$db.FileGroups.Refresh()
            }

            #endregion filegroupname
            #region logicalname
            if ($ReplaceBefore) {
                foreach ($fn in $db.FileGroups.Files.Name) {
                    $Entities_Before['LGN'][$fn] = $fn
                }
                foreach ($fn in $db.Logfiles.Name) {
                    $Entities_Before['LGN'][$fn] = $fn
                }
            }
            if (!$failed -and $LogicalName) {
                $New_LogicalNames = @{ }
                foreach ($fn in $db.FileGroups.Files.Name) {
                    $New_LogicalNames[$fn] = 1
                }
                foreach ($fn in $db.Logfiles.Name) {
                    $New_LogicalNames[$fn] = 1
                }
                $LNCounter = 0
                foreach ($fg in $db.FileGroups) {
                    $logicalfiles = @($fg.Files)
                    for ($i = 0; $i -lt $logicalfiles.Count; $i++) {
                        $logical = $logicalfiles[$i]
                        $FileType = switch ($fg.FileGroupType) {
                            'RowsFileGroup' { 'ROWS' }
                            'MemoryOptimizedDataFileGroup' { 'MMO' }
                            'FileStreamDataFileGroup' { 'FS' }
                            default { 'STD' }
                        }
                        $Orig_LGName = $logical.Name
                        $Orig_Placeholder = $Orig_LGName
                        if ($ReplaceBefore) {
                            # at Logical Name level, we need to worry about database name and filegroup name
                            $dbKey = Get-DbaKeyByValue -HashTable $Entities_Before['DBN'] -Value $db.Name
                            $fgKey = Get-DbaKeyByValue -HashTable $Entities_Before['FGN'] -Value $fg.Name
                            if ($dbKey) {
                                $Orig_Placeholder = $Orig_Placeholder.Replace($dbKey, '')
                            }
                            if ($fgKey) {
                                $Orig_Placeholder = $Orig_Placeholder.Replace($fgKey, '')
                            }
                        }
                        $NewLGName = $LogicalName.Replace('<DBN>', $db.Name).Replace('<DATE>', $CurrentDate).Replace('<FGN>', $fg.Name).Replace(
                            '<FT>', $FileType).Replace('<LGN>', $Orig_Placeholder)
                        $FinalLGName = $NewLGName
                        while ($logical.Name -ne $FinalLGName) {
                            if ($FinalLGName -in $New_LogicalNames.Keys) {
                                $LNCounter += 1
                                $FinalLGName = "$NewLGName$($LNCounter.ToString('000'))"
                            } else {
                                break
                            }
                        }
                        if ($logical.Name -eq $FinalLGName) {
                            Write-Message -Level VeryVerbose -Message "No rename necessary for LogicalFile $($logical.Name) (on FileGroup $($fg.Name) (on $db))" -FunctionName Rename-DbaDatabase
                            continue
                        }
                        if ($PSCmdlet.ShouldProcess($db, "Renaming LogicalFile $($logical.Name) to $FinalLGName (on FileGroup $($fg.Name))")) {
                            try {
                                if (!$Preview) {
                                    $logical.Rename($FinalLGName)
                                }
                                $New_LogicalNames.Remove($Orig_LGName)
                                $New_LogicalNames[$FinalLGName] = 1
                                $Entities_Before['LGN'][$Orig_LGName] = $FinalLGName
                            } catch {
                                Stop-Function -Message "Failed to Rename Logical File : $($_.Exception.InnerException.InnerException.InnerException)" -ErrorRecord $_ -Target $server.DomainInstanceName -OverrideExceptionMessage -FunctionName Rename-DbaDatabase
                                # stop any further renames
                                $failed = $true
                                break
                            }
                        }
                    }
                }
                #$fg.Files.Refresh()
                if (!$failed) {
                    $logfiles = @($db.LogFiles)
                    for ($i = 0; $i -lt $logfiles.Count; $i++) {
                        $logicallog = $logfiles[$i]
                        $Orig_LGName = $logicallog.Name
                        $Orig_Placeholder = $Orig_LGName
                        if ($ReplaceBefore) {
                            # at Logical Name level, we need to worry about database name and filegroup name, but for logfiles filegroup is not there
                            $dbKey = Get-DbaKeyByValue -HashTable $Entities_Before['DBN'] -Value $db.Name
                            $fgKey = Get-DbaKeyByValue -HashTable $Entities_Before['FGN'] -Value $fg.Name
                            if ($dbKey) {
                                $Orig_Placeholder = $Orig_Placeholder.Replace($dbKey, '')
                            }
                            if ($fgKey) {
                                $Orig_Placeholder = $Orig_Placeholder.Replace($fgKey, '')
                            }
                        }
                        $NewLGName = $LogicalName.Replace('<DBN>', $db.Name).Replace('<DATE>', $CurrentDate).Replace('<FGN>', '').Replace(
                            '<FT>', 'LOG').Replace('<LGN>', $Orig_Placeholder)
                        $FinalLGName = $NewLGName
                        if ($FinalLGName.Length -eq 0) {
                            #someone passed in -LogicalName '<FGN>'.... but we don't have FGN here
                            $FinalLGName = $Orig_LGName
                        }
                        while ($logicallog.Name -ne $FinalLGName) {
                            if ($FinalLGName -in $New_LogicalNames.Keys) {
                                $LNCounter += 1
                                $FinalLGName = "$NewLGName$($LNCounter.ToString('000'))"
                            } else {
                                break
                            }
                        }
                        if ($logicallog.Name -eq $FinalLGName) {
                            Write-Message -Level VeryVerbose -Message "No Rename necessary for LogicalFile log $($logicallog.Name) (LOG on (on $db))" -FunctionName Rename-DbaDatabase
                            continue
                        }
                        if ($PSCmdlet.ShouldProcess($db, "Renaming LogicalFile log $($logicallog.Name) to $FinalLGName (LOG)")) {
                            try {
                                if (!$Preview) {
                                    $logicallog.Rename($FinalLGName)
                                }
                                $New_LogicalNames.Remove($Orig_LGName)
                                $New_LogicalNames[$FinalLGName] = 1
                                $Entities_Before['LGN'][$Orig_LGName] = $FinalLGName
                            } catch {
                                Stop-Function -Message "Failed to Rename Logical File : $($_.Exception.InnerException.InnerException.InnerException)" -ErrorRecord $_ -Target $server.DomainInstanceName -OverrideExceptionMessage -FunctionName Rename-DbaDatabase
                                # stop any further renames
                                $failed = $true
                                break
                            }
                        }
                    }
                    #$db.Logfiles.Refresh()
                }
            }
            #endregion logicalname
            #region filename
            if ($ReplaceBefore) {
                foreach ($fn in $db.FileGroups.Files.FileName) {
                    $Entities_Before['FNN'][$fn] = $fn
                }
                foreach ($fn in $db.Logfiles.FileName) {
                    $Entities_Before['FNN'][$fn] = $fn
                }
            }
            if (!$failed -and $FileName) {

                $New_FileNames = @{ }
                foreach ($fn in $db.FileGroups.Files.FileName) {
                    $New_FileNames[$fn] = 1
                }
                foreach ($fn in $db.Logfiles.FileName) {
                    $New_FileNames[$fn] = 1
                }
                # we need to inspect what files are in the same directory
                # to avoid failing the process because the move won't work
                # here we have a dict keyed by instance and then keyed by path
                if ( !$InstanceFiles.ContainsKey($Server_Id) ) {
                    $InstanceFiles[$Server_Id] = @{ }
                }
                foreach ($fn in $New_FileNames.Keys) {
                    $dirname = [IO.Path]::GetDirectoryName($fn)
                    if ( !$InstanceFiles[$Server_Id].ContainsKey($dirname) ) {
                        $InstanceFiles[$Server_Id][$dirname] = @{ }
                        try {
                            $dirfiles = Get-DbaFile -SqlInstance $server -Path $dirname -EnableException
                        } catch {
                            Write-Message -Level Warning -Message "Failed to enumerate existing files at $dirname, move could go wrong" -FunctionName Rename-DbaDatabase
                        }
                        foreach ($f in $dirfiles) {
                            $InstanceFiles[$Server_Id][$dirname][$f.Filename] = 1
                        }
                    }
                }
                $FNCounter = 0
                foreach ($fg in $db.FileGroups) {
                    $FG_Files = @($fg.Files)
                    foreach ($logical in $FG_Files) {
                        $FileType = switch ($fg.FileGroupType) {
                            'RowsFileGroup' { 'ROWS' }
                            'MemoryOptimizedDataFileGroup' { 'MMO' }
                            'FileStreamDataFileGroup' { 'FS' }
                            default { 'STD' }
                        }
                        $FNName = $logical.FileName
                        $FNNameDir = [IO.Path]::GetDirectoryName($FNName)
                        $Orig_FNNameLeaf = [IO.Path]::GetFileNameWithoutExtension($logical.FileName)
                        $Orig_Placeholder = $Orig_FNNameLeaf
                        if ($ReplaceBefore) {
                            # at Filename level, we need to worry about database name, filegroup name and logical file name
                            $dbKey = Get-DbaKeyByValue -HashTable $Entities_Before['DBN'] -Value $db.Name
                            $fgKey = Get-DbaKeyByValue -HashTable $Entities_Before['FGN'] -Value $fg.Name
                            $lgKey = Get-DbaKeyByValue -HashTable $Entities_Before['LGN'] -Value $logical.Name
                            if ($dbKey) {
                                $Orig_Placeholder = $Orig_Placeholder.Replace($dbKey, '')
                            }
                            if ($fgKey) {
                                $Orig_Placeholder = $Orig_Placeholder.Replace($fgKey, '')
                            }
                            if ($lgKey) {
                                $Orig_Placeholder = $Orig_Placeholder.Replace($lgKey, '')
                            }
                        }
                        $NewFNName = $FileName.Replace('<DBN>', $db.Name).Replace('<DATE>', $CurrentDate).Replace('<FGN>', $fg.Name).Replace(
                            '<FT>', $FileType).Replace('<LGN>', $logical.Name).Replace('<FNN>', $Orig_Placeholder)
                        $FinalFNName = [IO.Path]::Combine($FNNameDir, "$NewFNName$([IO.Path]::GetExtension($FNName))")

                        while ($logical.FileName -ne $FinalFNName) {
                            if ($InstanceFiles[$Server_Id][$FNNameDir].ContainsKey($FinalFNName)) {
                                $FNCounter += 1
                                $FinalFNName = [IO.Path]::Combine($FNNameDir, "$NewFNName$($FNCounter.ToString('000'))$([IO.Path]::GetExtension($FNName))"
                                )
                            } else {
                                break
                            }
                        }
                        if ($logical.FileName -eq $FinalFNName) {
                            Write-Message -Level VeryVerbose -Message "No rename necessary (on FileGroup $($fg.Name) (on $db))" -FunctionName Rename-DbaDatabase
                            continue
                        }
                        if ($PSCmdlet.ShouldProcess($db, "Renaming FileName $($logical.FileName) to $FinalFNName (on FileGroup $($fg.Name))")) {
                            try {
                                if (!$Preview) {
                                    $logical.FileName = $FinalFNName
                                    $db.Alter()
                                }
                                $InstanceFiles[$Server_Id][$FNNameDir].Remove($FNName)
                                $InstanceFiles[$Server_Id][$FNNameDir][$FinalFNName] = 1
                                $Entities_Before['FNN'][$FNName] = $FinalFNName
                                $Pending_Renames += [PSCustomObject]@{
                                    Source      = $FNName
                                    Destination = $FinalFNName
                                }
                            } catch {
                                Stop-Function -Message "Failed to Rename FileName : $($_.Exception.InnerException.InnerException.InnerException)" -ErrorRecord $_ -Target $server.DomainInstanceName -OverrideExceptionMessage -FunctionName Rename-DbaDatabase
                                # stop any further renames
                                $failed = $true
                                break
                            }
                        }
                    }
                    if (!$failed) {
                        $FG_Files = @($db.Logfiles)
                        foreach ($logical in $FG_Files) {
                            $FNName = $logical.FileName
                            $FNNameDir = [IO.Path]::GetDirectoryName($FNName)
                            $Orig_FNNameLeaf = [IO.Path]::GetFileNameWithoutExtension($logical.FileName)
                            $Orig_Placeholder = $Orig_FNNameLeaf
                            if ($ReplaceBefore) {
                                # at Filename level, we need to worry about database name, filegroup name and logical file name
                                $dbKey = Get-DbaKeyByValue -HashTable $Entities_Before['DBN'] -Value $db.Name
                                $fgKey = Get-DbaKeyByValue -HashTable $Entities_Before['FGN'] -Value $fg.Name
                                $lgKey = Get-DbaKeyByValue -HashTable $Entities_Before['LGN'] -Value $logical.Name
                                if ($dbKey) {
                                    $Orig_Placeholder = $Orig_Placeholder.Replace($dbKey, '')
                                }
                                if ($fgKey) {
                                    $Orig_Placeholder = $Orig_Placeholder.Replace($fgKey, '')
                                }
                                if ($lgKey) {
                                    $Orig_Placeholder = $Orig_Placeholder.Replace($lgKey, '')
                                }
                            }
                            $NewFNName = $FileName.Replace('<DBN>', $db.Name).Replace('<DATE>', $CurrentDate).Replace('<FGN>', '').Replace(
                                '<FT>', 'LOG').Replace('<LGN>', $logical.Name).Replace('<FNN>', $Orig_Placeholder)
                            $FinalFNName = [IO.Path]::Combine($FNNameDir, "$NewFNName$([IO.Path]::GetExtension($FNName))")
                            while ($logical.FileName -ne $FinalFNName) {
                                if ($InstanceFiles[$Server_Id][$FNNameDir].ContainsKey($FinalFNName)) {
                                    $FNCounter += 1
                                    $FinalFNName = [IO.Path]::Combine($FNNameDir, "$NewFNName$($FNCounter.ToString('000'))$([IO.Path]::GetExtension($FNName))")
                                } else {
                                    break
                                }
                            }
                            if ($logical.FileName -eq $FinalFNName) {
                                Write-Message -Level VeryVerbose -Message "No rename necessary for $($logical.FileName) (LOG on (on $db))" -FunctionName Rename-DbaDatabase
                                continue
                            }

                            if ($PSCmdlet.ShouldProcess($db, "Renaming FileName $($logical.FileName) to $FinalFNName (LOG)")) {
                                try {
                                    if (!$Preview) {
                                        $logical.FileName = $FinalFNName
                                        $db.Alter()
                                    }
                                    $InstanceFiles[$Server_Id][$FNNameDir].Remove($FNName)
                                    $InstanceFiles[$Server_Id][$FNNameDir][$FinalFNName] = 1
                                    $Entities_Before['FNN'][$FNName] = $FinalFNName
                                    $Pending_Renames += [PSCustomObject]@{
                                        Source      = $FNName
                                        Destination = $FinalFNName
                                    }
                                } catch {
                                    Stop-Function -Message "Failed to Rename FileName : $($_.Exception.InnerException.InnerException.InnerException)" -ErrorRecord $_ -Target $server.DomainInstanceName -OverrideExceptionMessage -FunctionName Rename-DbaDatabase
                                    # stop any further renames
                                    $failed = $true
                                    break
                                }
                            }
                        }
                    }

                }
                #endregion filename
                #region move
                $ComputerName = $null
                $Final_Renames = New-Object System.Collections.ArrayList
                if ([DbaValidate]::IsLocalhost($server.ComputerName)) {
                    # locally ran so we can just use rename-item
                    $ComputerName = $server.ComputerName
                } else {
                    # let's start checking if we can access .ComputerName
                    $testPS = $false
                    if ($SqlCredential) {
                        # why does Test-PSRemoting require a Credential param ? this is ugly...
                        $testPS = Test-PSRemoting -ComputerName $server.ComputerName -Credential $SqlCredential -ErrorAction Stop
                    } else {
                        $testPS = Test-PSRemoting -ComputerName $server.ComputerName -ErrorAction Stop
                    }
                    if (!($testPS)) {
                        # let's try to resolve it to a more qualified name, without "cutting" knowledge about the domain (only $server.Name possibly holds the complete info)
                        $Resolved = (Resolve-DbaNetworkName -ComputerName $server.Name).FullComputerName
                        if ($SqlCredential) {
                            $testPS = Test-PSRemoting -ComputerName $Resolved -Credential $SqlCredential -ErrorAction Stop
                        } else {
                            $testPS = Test-PSRemoting -ComputerName $Resolved -ErrorAction Stop
                        }
                        if ($testPS) {
                            $ComputerName = $Resolved
                        }
                    } else {
                        $ComputerName = $server.ComputerName
                    }
                }
                foreach ($op in $pending_renames) {
                    if ([DbaValidate]::IsLocalhost($server.ComputerName)) {
                        $null = $Final_Renames.Add([PSCustomObject]@{
                                Source       = $op.Source
                                Destination  = $op.Destination
                                ComputerName = $ComputerName
                            })
                    } else {
                        if ($null -eq $ComputerName) {
                            # if we don't have remote access ($ComputerName is null) we can fallback to admin shares if they're available
                            if (Test-Path (Join-AdminUnc -ServerName $server.ComputerName -filepath $op.Source)) {
                                $null = $Final_Renames.Add([PSCustomObject]@{
                                        Source       = Join-AdminUnc -ServerName $server.ComputerName -filepath $op.Source
                                        Destination  = Join-AdminUnc -ServerName $server.ComputerName -filepath $op.Destination
                                        ComputerName = $server.ComputerName
                                    })
                            } else {
                                # flag the impossible rename ($ComputerName is $null)
                                $null = $Final_Renames.Add([PSCustomObject]@{
                                        Source       = $op.Source
                                        Destination  = $op.Destination
                                        ComputerName = $ComputerName
                                    })
                            }
                        } else {
                            # we can do renames in a remote pssession
                            $null = $Final_Renames.Add([PSCustomObject]@{
                                    Source       = $op.Source
                                    Destination  = $op.Destination
                                    ComputerName = $ComputerName
                                })
                        }
                    }
                }
                $Status = 'FULL'
                if (!$failed -and ($SetOffline -or $Move) -and $Final_Renames) {
                    if (!$Move) {
                        Write-Message -Level VeryVerbose -Message "Setting the database offline. You are in charge of moving the files to the new location" -FunctionName Rename-DbaDatabase
                        # because renames still need to be dealt with
                        $Status = 'PARTIAL'
                    } else {
                        if ($PSCmdlet.ShouldProcess($db, "File Rename required, setting db offline")) {
                            $SetState = Set-DbaDbState -SqlInstance $server -Database $db.Name -Offline -Force
                            if ($SetState.Status -ne 'OFFLINE') {
                                Write-Message -Level Warning -Message "Setting db offline failed, You are in charge of moving the files to the new location" -FunctionName Rename-DbaDatabase
                                # because it was impossible to set the database offline
                                $Status = 'PARTIAL'
                            } else {
                                try {
                                    while ($Final_Renames.Count -gt 0) {
                                        $op = $Final_Renames.Item(0)
                                        if ($null -eq $op.ComputerName) {
                                            Stop-Function -Message "No access to physical files for renames" -FunctionName Rename-DbaDatabase
                                        } else {
                                            Write-Message -Level VeryVerbose -Message "Moving file $($op.Source) to $($op.Destination)" -FunctionName Rename-DbaDatabase
                                            if (!$Preview) {
                                                $scriptBlock = {
                                                    $op = $args[0]
                                                    Rename-Item -Path $op.Source -NewName $op.Destination
                                                }
                                                Invoke-Command2 -ComputerName $op.ComputerName -Credential $sqlCredential -ScriptBlock $scriptBlock -ArgumentList $op
                                            }
                                        }
                                        $null = $Final_Renames.RemoveAt(0)
                                    }
                                } catch {
                                    $failed = $true
                                    # because a rename operation failed
                                    $Status = 'PARTIAL'
                                    Stop-Function -Message "Failed to rename $($op.Source) to $($op.Destination), you are in charge of moving the files to the new location" -ErrorRecord $_ -Target $instance -Exception $_.Exception -Continue -FunctionName Rename-DbaDatabase
                                }
                                if (!$failed) {
                                    if ($PSCmdlet.ShouldProcess($db, "Setting database online")) {
                                        $SetState = Set-DbaDbState -SqlInstance $server -Database $db.Name -Online -Force
                                        if ($SetState.Status -ne 'ONLINE') {
                                            Write-Message -Level Warning -Message "Setting db online failed" -FunctionName Rename-DbaDatabase
                                            # because renames were done, but the database didn't wake up
                                            $Status = 'PARTIAL'
                                        } else {
                                            $Status = 'FULL'
                                        }
                                    }
                                }
                            }
                        }
                    }
                } else {
                    # because of a previous error with renames to do
                    $Status = 'PARTIAL'
                }
            } else {
                if (!$failed) {
                    # because no previous error and not filename
                    $Status = 'FULL'
                } else {
                    # because previous errors and not filename
                    $Status = 'PARTIAL'
                }
            }
            #endregion move
            # remove entities that match for the output
            foreach ($k in $Entities_Before.Keys) {
                $ToRemove = $Entities_Before[$k].GetEnumerator() | Where-Object { $_.Name -eq $_.Value } | Select-Object -ExpandProperty Name
                foreach ($el in $ToRemove) {
                    $Entities_Before[$k].Remove($el)
                }
            }
            [PSCustomObject]@{
                ComputerName       = $server.ComputerName
                InstanceName       = $server.ServiceName
                SqlInstance        = $server.DomainInstanceName
                Database           = $db
                DBN                = $Entities_Before['DBN']
                DatabaseRenames    = ($Entities_Before['DBN'].GetEnumerator() | ForEach-Object { "$($_.Name) --> $($_.Value)" }) -Join "`n"
                FGN                = $Entities_Before['FGN']
                FileGroupsRenames  = ($Entities_Before['FGN'].GetEnumerator() | ForEach-Object { "$($_.Name) --> $($_.Value)" }) -Join "`n"
                LGN                = $Entities_Before['LGN']
                LogicalNameRenames = ($Entities_Before['LGN'].GetEnumerator() | ForEach-Object { "$($_.Name) --> $($_.Value)" }) -Join "`n"
                FNN                = $Entities_Before['FNN']
                FileNameRenames    = ($Entities_Before['FNN'].GetEnumerator() | ForEach-Object { "$($_.Name) --> $($_.Value)" }) -Join "`n"
                PendingRenames     = $Final_Renames
                Status             = $Status
            } | Select-DefaultView -ExcludeProperty DatabaseRenames, FileGroupsRenames, LogicalNameRenames, FileNameRenames
        }
        #endregion db loop
    }

    @{ __w3081State = @{ interrupted = (Test-FunctionInterrupt) } }
} $SqlInstance $SqlCredential $Database $ExcludeDatabase $AllDatabases $DatabaseName $FileGroupName $LogicalName $FileName $ReplaceBefore $Force $Move $SetOffline $Preview $InputObject $EnableException $__state $__hopInterrupted $__boundWhatIf $__boundConfirm $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1

""";
}
