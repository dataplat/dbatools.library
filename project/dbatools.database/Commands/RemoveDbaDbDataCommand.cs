#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Truncates all data in one or more databases, scripting out and recreating the foreign keys and
/// views that block truncation. Port of public/Remove-DbaDbData.ps1 (W2-158); the workflow remains a
/// module-scoped PowerShell compatibility hop.
///
/// THIS COMMAND DESTROYS DATA, and that fact drives the whole design of this port. It carries the
/// DEF-011 interrupt latch in BOTH halves, per the 2026-07-18 20:25 standing order ("every NEW port
/// with a hop Test-FunctionInterrupt + hard Stop-Function + VFP carries E's latch pattern"). This row
/// meets all three conditions: Test-FunctionInterrupt opens the process block, three Stop-Function
/// calls are hard (no -Continue), and -InputObject is ValueFromPipeline.
///
/// THE BEGIN HALF IS THE DANGEROUS ONE. The source's begin block is a single line -
/// `$null = Test-ExportDirectory -Path $Path` - and that helper can itself Stop-Function when the
/// export directory is unusable. That is precisely WHY the process block opens with
/// `if (Test-FunctionInterrupt) { return }`: in the function world a begin failure stops every
/// record. begin and process are separate scoped invocations in a hop, so an uncarried begin
/// interrupt is simply LOST - and the very next thing this command does is truncate every table in
/// the database. A port without this carry destroys data the source refuses to touch when -Path is
/// bad. The begin hop therefore reports Test-FunctionInterrupt through its sentinel into
/// _beginInterrupted, and ProcessRecord refuses to run when it is set.
///
/// THE PROCESS HALF is the ordinary cross-record latch: an in-hop Stop-Function cannot set
/// DbaBaseCmdlet.Interrupted (private setter), so without a carry each record would start fresh and
/// keep truncating after a stop the source treated as fatal. The process hop reports its own
/// Test-FunctionInterrupt into _interruptLatched. Both fields plus the base flag are read by one
/// guard, mirroring AddDbaServerRoleMemberCommand (dbatools.security), the pattern's canonical
/// carrier:
///     if (_beginInterrupted || _interruptLatched || Interrupted) return;
///
/// Everything else is a plain process-only hop. THREE hard `return`s (the interrupt guard, the
/// no-input guard, and the switch's default arm) all reproduce as scriptblock returns, so NO
/// continue-guard wrapper is involved - there is no `continue` anywhere in this body. ZERO Test-Bound
/// calls: the no-input guard tests parameter VALUES.
///
/// $input SHADOWING is left verbatim, as on W2-164: `foreach ($input in $InputObject)` rebinds
/// PowerShell's automatic $input, and the foreach binds the name locally in both worlds.
///
/// -SqlInstance is declared `[DbaInstance[]]` in the source, which is the dbatools TYPE ACCELERATOR
/// for Dataplat.Dbatools.Parameter.DbaInstanceParameter - the same lesson W2-164 taught when the
/// compiler rejected the accelerator name. The baseline pins the resolved type and the C# declares it;
/// the body's type switch dispatches on that full name and still matches values bound through the
/// accelerator.
///
/// -Path defaults to `Get-DbatoolsConfigValue -FullName 'Path.DbatoolsExport'`. That default is
/// evaluated by CALLING THROUGH to the config system inside the hop when the caller did not supply
/// one, never by inlining whatever the value happened to be at build time - a literal would freeze
/// one machine's export path into the assembly.
///
/// PRESERVED SOURCE BEHAVIOUR worth flagging for review, none of it tidied:
///   * the catch around truncation runs the _Create.Sql recovery script and does NOT rethrow, so a
///     failed truncation leaves the objects restored and execution continues;
///   * the recovery Invoke-DbaQuery in that catch is itself unguarded - if it throws, the error
///     escapes;
///   * `$objects` is tested for truthiness to decide whether drop/create scripts run, so a database
///     with no foreign keys or views truncates without scripting anything;
///   * the cleanup Remove-Item failure only warns.
///
/// Surface pinned by migration/baselines/Remove-DbaDbData.json
/// (sourceSha256 7e8f3c73c89168ae49ddd6c41a248088cf95ba09ce5dcbe6593a54ce4ede0dbf): supportsShouldProcess
/// true, confirmImpact High, no named parameter sets, outputType empty.
/// </summary>
[Cmdlet(VerbsCommon.Remove, "DbaDbData", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.High)]
public sealed class RemoveDbaDbDataCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The database(s) to process.</summary>
    [Parameter(Position = 2)]
    public string[]? Database { get; set; }

    /// <summary>The database(s) to exclude.</summary>
    [Parameter(Position = 3)]
    public string[]? ExcludeDatabase { get; set; }

    /// <summary>Server or database object(s) piped in.</summary>
    [Parameter(ValueFromPipeline = true, Position = 4)]
    public object[]? InputObject { get; set; }

    /// <summary>Directory for the scripted drop/create files. Defaults to the Path.DbatoolsExport
    /// config value, resolved at run time inside the hop rather than baked in at build time.</summary>
    [Parameter(Position = 5)]
    public string? Path { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // DEF-011 latch, both halves. _beginInterrupted carries a stop raised by the begin block's
    // Test-ExportDirectory; _interruptLatched carries a stop raised while handling an earlier record.
    // Without them a hop forgets the stop and keeps truncating - see the class remarks.
    private bool _beginInterrupted;
    private bool _interruptLatched;

    protected override void BeginProcessing()
    {
        if (Interrupted)
            return;

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, BeginScript,
            Path, EnableException.ToBool(),
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
        {
            if (item is not null && LanguagePrimitives.IsTrue(item.Properties["__removeDbaDbDataBeginComplete"]?.Value))
            {
                _beginInterrupted = LanguagePrimitives.IsTrue(item.Properties["Interrupted"]?.Value);
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
        // The whole point of the latch: a stop raised in begin, or while handling an earlier record,
        // must prevent this record from truncating anything.
        if (_beginInterrupted || _interruptLatched || Interrupted)
            return;

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, ProcessScript,
            SqlInstance, SqlCredential, Database, ExcludeDatabase, InputObject, Path,
            EnableException.ToBool(), this,
            BoundCommonParameter("WhatIf"), BoundCommonParameter("Confirm"),
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
        {
            if (item is not null && LanguagePrimitives.IsTrue(item.Properties["__removeDbaDbDataProcessComplete"]?.Value))
            {
                _interruptLatched = LanguagePrimitives.IsTrue(item.Properties["Interrupted"]?.Value);
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

    // PS: the begin block verbatim. The -Path default is resolved HERE, by calling the config system,
    // when the caller supplied none. Edit: the trailing completion sentinel, which reports whether
    // Test-ExportDirectory stopped the command so ProcessRecord can refuse to truncate.
    private const string BeginScript = """
param($Path, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param($Path, $EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    try {
        if (-not $PSBoundParameters.ContainsKey('Path') -or $null -eq $Path -or '' -eq $Path) {
            $Path = (Get-DbatoolsConfigValue -FullName 'Path.DbatoolsExport')
        }

        $null = Test-ExportDirectory -Path $Path
    } finally {
        # Hop mechanism, not source: report whether Test-ExportDirectory stopped the command, so the
        # process hop can honour the source's begin-wide stop instead of truncating anyway. Emitted
        # from FINALLY so it is reported even if the body throws or returns.
        [pscustomobject]@{ __removeDbaDbDataBeginComplete = $true; Interrupted = [bool](Test-FunctionInterrupt) }
    }
} $Path $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";

    // PS: the process block verbatim. Edits: $Pscmdlet -> $__realCmdlet, -FunctionName
    // Remove-DbaDbData on the direct Stop-Function and Write-Message sites, the same -Path default
    // resolution as the begin hop, and the trailing completion sentinel carrying the interrupt latch.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Database, $ExcludeDatabase, $InputObject, $Path, $EnableException, $__realCmdlet, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundWhatIf) { $__commonParameters.WhatIf = [bool]$__boundWhatIf }
if ($null -ne $__boundConfirm) { $__commonParameters.Confirm = [bool]$__boundConfirm }
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = "High")]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [string[]]$Database, [string[]]$ExcludeDatabase, [object[]]$InputObject, $Path, $EnableException, $__realCmdlet, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    if ($null -eq $Path -or '' -eq $Path) {
        $Path = (Get-DbatoolsConfigValue -FullName 'Path.DbatoolsExport')
    }

    # try/finally is HOP MECHANISM, not source. The body below has FOUR hard returns, three of them
    # immediately after a Stop-Function - which are exactly the paths that set the interrupt. A
    # sentinel emitted at the end of the body would be SKIPPED on precisely those returns, so the
    # latch would fail to carry on the only paths that need it and the next record would truncate.
    try {
    if (Test-FunctionInterrupt) { return }

    if (-not $InputObject -and -not $SqlInstance) {
        Stop-Function -Message "You must pipe in a database or a server, or specify a SqlInstance" -FunctionName Remove-DbaDbData
        return
    }

    if ($SqlInstance) {
        $InputObject = $SqlInstance
    }

    foreach ($input in $InputObject) {
        $inputType = $input.GetType().FullName
        switch ($inputType) {
            'Dataplat.Dbatools.Parameter.DbaInstanceParameter' {
                Write-Message -Level Verbose -Message "Processing DbaInstanceParameter through InputObject" -FunctionName Remove-DbaDbData
                $dbDatabases = Get-DbaDatabase -SqlInstance $input -SqlCredential $SqlCredential -Database $Database -ExcludeDatabase $ExcludeDatabase -ExcludeSystem
            }
            'Microsoft.SqlServer.Management.Smo.Server' {
                Write-Message -Level Verbose -Message "Processing Server through InputObject" -FunctionName Remove-DbaDbData
                $dbDatabases = Get-DbaDatabase -SqlInstance $input -SqlCredential $SqlCredential -Database $Database -ExcludeDatabase $ExcludeDatabase -ExcludeSystem
            }
            'Microsoft.SqlServer.Management.Smo.Database' {
                Write-Message -Level Verbose -Message "Processing Database through InputObject" -FunctionName Remove-DbaDbData
                $dbDatabases = $input | Where-Object { -not $_.IsSystemObject }
            }
            default {
                Stop-Function -Message "InputObject is not a server or database." -FunctionName Remove-DbaDbData
                return
            }
        }

        foreach ($db in $dbDatabases) {
            if ($__realCmdlet.ShouldProcess($db.Name, "Removing all data on $($db.Parent.Name)")) {
                $server = $db.Parent
                Write-Message -Level Verbose -Message "Truncating tables in $db on instance $server" -FunctionName Remove-DbaDbData
                try {

                    # Collect up the objects we need to drop and recreate
                    $objects = @()
                    $objects += Get-DbaDbForeignKey -SqlInstance $server -Database $db.Name
                    $objects += Get-DbaDbView -SqlInstance $server -Database $db.Name -ExcludeSystemView

                    # Script out the create statements for objects
                    $createOptions = New-DbaScriptingOption
                    $createOptions.Permissions = $true
                    $createOptions.ScriptBatchTerminator = $true
                    $createOptions.AnsiFile = $true
                    $null = $objects | Export-DbaScript -FilePath "$Path\$($db.Name)_Create.Sql" -ScriptingOptionsObject $createOptions

                    # Script out the drop statements for objects
                    $dropOptions = New-DbaScriptingOption
                    $dropOptions.ScriptDrops = $true
                    $null = $objects | Export-DbaScript -FilePath "$Path\$($db.Name)_Drop.Sql" -ScriptingOptionsObject $dropOptions
                } catch {
                    Stop-Function -Message "Issue scripting out the drop\create scripts for objects in $db on instance $server" -ErrorRecord $_ -FunctionName Remove-DbaDbData
                    return
                }

                try {
                    if ($objects) {
                        Invoke-DbaQuery -SqlInstance $server -Database $db.Name -File "$Path\$($db.Name)_Drop.Sql"
                    }

                    $db.Tables | ForEach-Object { $_.TruncateData() }

                    if ($objects) {
                        Invoke-DbaQuery -SqlInstance $server -Database $db.Name -File "$Path\$($db.Name)_Create.Sql"
                    }
                } catch {
                    Write-Message -Level warning -Message "Issue truncating tables in $db on instance $server" -FunctionName Remove-DbaDbData
                    Invoke-DbaQuery -SqlInstance $server -Database $db.Name -File "$Path\$($db.Name)_Create.Sql"
                }
                if ($objects) {
                    try {
                        Remove-Item "$Path\$($db.Name)_Drop.Sql", "$Path\$($db.Name)_Create.Sql" -ErrorAction Stop
                    } catch {
                        Write-Message -Level warning -Message "Unable to clear up output files for $db on $server" -FunctionName Remove-DbaDbData
                    }
                }
            }
        }
    }

    } finally {
        # Carry the interrupt latch to the next record, so a hard stop here prevents later records
        # from truncating - what the function's shared scope did for free.
        [pscustomobject]@{ __removeDbaDbDataProcessComplete = $true; Interrupted = [bool](Test-FunctionInterrupt) }
    }
} $SqlInstance $SqlCredential $Database $ExcludeDatabase $InputObject $Path $EnableException $__realCmdlet $__boundWhatIf $__boundConfirm $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}


