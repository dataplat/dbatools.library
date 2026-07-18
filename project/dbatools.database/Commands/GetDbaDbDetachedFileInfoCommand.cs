#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Reads the header of a detached MDF file (via the online instance's DetachedDatabaseInfo methods) to
/// report the database name, version, collation and file structure. Port of
/// public/Get-DbaDbDetachedFileInfo.ps1; the workflow remains a module-scoped PowerShell compatibility hop.
///
/// A begin+process port. SqlInstance is a single (non-pipeline) value so begin connects ONCE; Path is
/// ValueFromPipeline so process fires per record. The begin block connects and, on failure, runs
/// Stop-Function (no -Continue) then `return`, which sets the function-scope interrupt latch; on success it
/// builds $server (the live SMO connection), $servername and $serviceAccount. process opens with
/// `if (Test-FunctionInterrupt) { return }` and iterates the paths.
///
/// The latch and the SMO state cannot cross hop scopes, so: the begin body is DOT-SOURCED (its return exits
/// only the body while the sentinel still emits), after which Get-Variable -Scope 0 detects the latch and the
/// begin sentinel carries { Server; ServerName; ServiceAccount; Interrupted }; C# stores _state and
/// _beginInterrupted and gates ProcessRecord on _beginInterrupted so a failed begin connection silences all
/// process records (reproducing the source Test-FunctionInterrupt guard). process restores $server/
/// $servername/$serviceAccount from _state (the live SMO object survives the sentinel by reference); its
/// in-body Test-FunctionInterrupt is redundant (C# gates begin interrupts, each hop is a fresh scope) but kept
/// verbatim, and the process body is NOT dot-sourced (no re-emit; its three Stop-Function are -Continue and
/// set no latch). Body edits: -FunctionName Get-DbaDbDetachedFileInfo on the begin Stop-Function and the three
/// process Stop-Function. Surface pinned by migration/baselines/Get-DbaDbDetachedFileInfo.json (positions 0-2,
/// Path Mdf/FilePath/FullName aliases, no ShouldProcess).
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaDbDetachedFileInfo")]
public sealed class GetDbaDbDetachedFileInfoCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    public DbaInstanceParameter SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The path(s) to the detached MDF file(s).</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 2)]
    [Alias("Mdf", "FilePath", "FullName")]
    [PsStringArrayCast]
    public string[] Path { get; set; } = null!;

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // The begin-built SMO server + names, and whether the begin connection failed (Stop-Function set the
    // latch), carried begin->process.
    private Hashtable? _state;
    private bool _beginInterrupted;

    protected override void BeginProcessing()
    {
        if (Interrupted)
            return;

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, BeginScript,
            SqlInstance, SqlCredential, EnableException.ToBool(),
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
        {
            if (item?.BaseObject is Hashtable sentinel && sentinel.ContainsKey("__getDbaDbDetachedFileInfoBegin"))
            {
                if (sentinel["__getDbaDbDetachedFileInfoBegin"] is Hashtable state)
                {
                    _state = state;
                    _beginInterrupted = LanguagePrimitives.IsTrue(state["Interrupted"]);
                }
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
        if (Interrupted || _beginInterrupted)
            return;

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, ProcessScript,
            SqlInstance, Path, EnableException.ToBool(), _state,
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
    // PS: the begin block, DOT-SOURCED so its Stop-Function+return exits only the body while the sentinel
    // still emits. Edit: -FunctionName on the Stop-Function. After the body, Get-Variable -Scope 0 detects the
    // latch and the sentinel carries the SMO server + names + the interrupt flag.
    private const string BeginScript = """
param($SqlInstance, $SqlCredential, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter]$SqlInstance, [PSCredential]$SqlCredential, $EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    . {
        try {
            $server = Connect-DbaInstance -SqlInstance $SqlInstance -SqlCredential $SqlCredential
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $SqlInstance -FunctionName Get-DbaDbDetachedFileInfo
            return
        }
        $servername = $server.name
        $serviceAccount = $server.ServiceAccount
    }

    $__iv = Get-Variable -Name __dbatools_interrupt_function_78Q9VPrM6999g6zo24Qn83m09XF56InEn4hFrA8Fwhu5xJrs6r -Scope 0 -ErrorAction Ignore
    @{ __getDbaDbDetachedFileInfoBegin = @{ Server = $server; ServerName = $servername; ServiceAccount = $serviceAccount; Interrupted = [bool]($__iv -and $__iv.Value) } }
} $SqlInstance $SqlCredential $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
    // PS: the process block VERBATIM. Edit: -FunctionName on the three Stop-Function. The SMO server + names
    // are restored from the carried state; the in-body Test-FunctionInterrupt is redundant (C# gates begin
    // interrupts) but kept, and the body is not dot-sourced (its 91 return exits cleanly, no re-emit).
    private const string ProcessScript = """
param($SqlInstance, $Path, $EnableException, $__state, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter]$SqlInstance, [string[]]$Path, $EnableException, $__state, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    $server = $__state.Server
    $servername = $__state.ServerName
    $serviceAccount = $__state.ServiceAccount

        if (Test-FunctionInterrupt) { return }
        foreach ($filepath in $Path) {
            $datafiles = New-Object System.Collections.Specialized.StringCollection
            $logfiles = New-Object System.Collections.Specialized.StringCollection

            if (-not (Test-DbaPath -SqlInstance $server -Path $filepath)) {
                Stop-Function -Message "$servername cannot access the file $filepath. Does the file exist and does the service account ($serviceAccount) have access to the path?" -Continue -FunctionName Get-DbaDbDetachedFileInfo
            }

            try {
                $detachedDatabaseInfo = $server.DetachedDatabaseInfo($filepath)
                $dbName = ($detachedDatabaseInfo | Where-Object { $_.Property -eq "Database name" }).Value
                $exactdbversion = ($detachedDatabaseInfo | Where-Object { $_.Property -eq "Database version" }).Value
                $collationid = ($detachedDatabaseInfo | Where-Object { $_.Property -eq "Collation" }).Value
            } catch {
                Stop-Function -Message "$servername cannot read the file $filepath. Is the database detached?" -Continue -FunctionName Get-DbaDbDetachedFileInfo
            }

            # Source: https://sqlserverbuilds.blogspot.com/2014/01/sql-server-internal-database-versions.html
            switch ($exactdbversion) {
                998 { $dbversion = "SQL Server 2025" }
                957 { $dbversion = "SQL Server 2022" }
                904 { $dbversion = "SQL Server 2019" }
                869 { $dbversion = "SQL Server 2017" }
                868 { $dbversion = "SQL Server 2017" }
                852 { $dbversion = "SQL Server 2016" }
                782 { $dbversion = "SQL Server 2014" }
                706 { $dbversion = "SQL Server 2012" }
                661 { $dbversion = "SQL Server 2008 R2" }
                660 { $dbversion = "SQL Server 2008 R2" }
                655 { $dbversion = "SQL Server 2008 SP2+" }
                612 { $dbversion = "SQL Server 2005" }
                611 { $dbversion = "SQL Server 2005" }
                539 { $dbversion = "SQL Server 2000" }
                515 { $dbversion = "SQL Server 7.0" }
                408 { $dbversion = "SQL Server 6.5" }
                default { $dbversion = "Unknown" }
            }

            $collationsql = "SELECT name FROM fn_helpcollations() WHERE COLLATIONPROPERTY(name, N'COLLATIONID')  = $collationid"

            try {
                $dataset = $server.databases['master'].ExecuteWithResults($collationsql)
                $collation = "$($dataset.Tables[0].Rows[0].Item(0))"
            } catch {
                $collation = $collationid
            }

            if (-not $collation) { $collation = $collationid }

            try {
                foreach ($file in $server.EnumDetachedDatabaseFiles($filepath)) {
                    $datafiles += $file
                }

                foreach ($file in $server.EnumDetachedLogFiles($filepath)) {
                    $logfiles += $file
                }
            } catch {
                Stop-Function -Message "$servername unable to enumerate database or log structure information for $filepath" -Continue -FunctionName Get-DbaDbDetachedFileInfo
            }
            [PSCustomObject]@{
                ComputerName = $SqlInstance.ComputerName
                InstanceName = $SqlInstance.InstanceName
                SqlInstance  = $SqlInstance.InputObject
                Name         = $dbName
                Version      = $dbversion
                ExactVersion = $exactdbversion
                Collation    = $collation
                DataFiles    = $datafiles
                LogFiles     = $logfiles
            }
        }
} $SqlInstance $Path $EnableException $__state $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}