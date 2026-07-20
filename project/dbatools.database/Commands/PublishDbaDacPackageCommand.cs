#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Publishes a DAC package (dacpac/bacpac) to one or more databases. Port of
/// public/Publish-DbaDacPackage.ps1 (418 lines); the workflow remains a module-scoped PowerShell
/// compatibility hop.
///
/// BEGIN+PROCESS, two hops. -Database and -Path bind ValueFromPipelineByPropertyName (NOT plain
/// ValueFromPipeline), so process fires once per PIPED OBJECT whose properties supply them - a real
/// cross-record axis. Two named sets with an explicit default: Obj (default) and Xml. ZERO positional
/// parameters. Surface confirmed against migration/baselines/Publish-DbaDacPackage.json with the
/// per-set reader.
///
/// INTERRUPT CARRY IS LIVE. Begin's non-Continue guards (:172 instance/connstring required, :187
/// bacpac+scriptonly conflict, :207 DacFx load failure) set the module latch, and process opens with
/// "if (Test-FunctionInterrupt) { return }" at :216. The begin hop reads the latch at
/// Get-Variable -Scope 0 and carries it; C# skips process. Mechanism measured in
/// migration/logs/probe-20260718-latch-sentinel.
///
/// FOUR CARRIES, found with the corrected detectors BEFORE coding:
///
/// 1. $ConnectionString - begin MUTATES it (:176, "$ConnectionString = $ConnectionString |
///    Convert-ConnectionString") and process ACCUMULATES it (:256, "+=" per instance) then iterates
///    it (:297). It is NOT one of the pipeline-bound parameters, so the binder never rewrites it: the
///    Convert'd value rides begin -> process, and the accumulation rides record -> record. Same class
///    as New-DbaDacProfile's $ConnectionString (W2-142), here compounded by a begin mutation.
///
/// 2. $Type - process MUTATES it (:227, bacpac auto-detect from the .bacpac extension of $Path). $Path
///    is per-record (VFPBPN), so record 1 may set $Type='Bacpac' and, because $Type is NOT the
///    pipeline parameter, the SOURCE keeps that value for a later record whose $Path is a .dacpac -
///    processing it as a Bacpac. Bug-for-bug: $Type carries record -> record so the port reproduces
///    it. Begin's own read of $Type (to pick $defaultColumns) uses the ORIGINAL value, which is why
///    $defaultColumns is carried from begin as-is and never recomputed - see 3.
///
/// 3. $defaultColumns - computed ONCE in begin (:180/:182/:189) from $Type and the ScriptOnly/
///    GenerateDeploymentReport boundness, read in process at :413 for Select-DefaultView. Because it
///    is fixed in begin, the source shows a quirk that rides verbatim: an auto-detected Bacpac
///    (:227) still emits the Dacpac column set begin chose. Carried from begin, never recomputed.
///
/// 4. Get-ServerName - a helper DEFINED in begin (:192) but CALLED in process (:299). Begin's scope
///    dies before process runs, so it is recreated verbatim at the top of the process hop, the same
///    treatment New-DbaDacProfile's helpers needed. It closes over nothing carried, so recreation is
///    faithful.
///
/// SIX CARRIES TOTAL. Beyond the four above, codex r1 found two more cross-record variables I had
/// missed - both non-obvious because their reads are on failure/declined paths:
///   $result (:348/:352) - assigned only inside the publish/script ShouldProcess gates, but read in
///     the finally at :367/:373. Under -WhatIf, or a declined gate, it is unassigned this record and
///     the SOURCE reads a PREVIOUS record's $result. Carried with an Assigned flag.
///   $server (:252/:384) - assigned in the SqlInstance loop, but read as -Target at :333 when
///     DacServices construction fails. A ConnectionString-only record never enters that loop, so the
///     source reads the prior record's $server. Carried with an Assigned flag; affects an error's
///     target object, so low-severity but a real divergence.
///
/// TWO CANDIDATES REFUTED, recorded so a reviewer does not re-add them:
///   $dacPackage (:262) / $bacPackage (:269) - assigned in a try whose catch is a NON-Continue
///     Stop-Function followed by return (:265/:272), so a load failure exits the record before the
///     reads at :348/:352/:357; and on the success path each is reassigned before its read within the
///     same record. No stale cross-record read is reachable.
///   $dacServices (:331) - REFUTED (codex r2 raised it; the finding is wrong). It is assigned in a
///     try (:330-332) whose catch (:333) is "Stop-Function ... -Continue", and its reads (:337
///     Register-ObjectEvent, then :348/:352/:357) are all in the SECOND try that FOLLOWS the catch.
///     Because PowerShell's continue is dynamically scoped, the catch's continue unwinds the
///     enclosing "foreach ($dbName)" loop, so on a construction failure :337 is never reached - every
///     read of $dacServices is gated behind a SUCCESSFUL construction that just assigned it. A stale
///     cross-record value is therefore unobservable. This is the continue-in-catch class: a read
///     INSIDE the catch (like $server's -Target at :333) is reachable and does carry; a read AFTER
///     the catch does not. Measured with a fall-through control in
///     migration/logs/probe-20260718-continue-propagation.
///
/// $deploymentReport (:366) needs NO carry, but the earlier rationale was imprecise (codex r1): its
/// read at :397 is UNCONDITIONAL in the output object, not inside the :365 report guard. The safety
/// is instead that GenerateDeploymentReport is a CONSTANT switch across records - so it is either
/// assigned-before-read every time (GDR true) or never assigned and read as $null every time (GDR
/// false). Neither differs from the source's persistent scope.
///
/// SEVEN Test-Bound PARAMETERS become carried caller-boundness flags, split across the blocks:
/// SqlInstance, ConnectionString, ScriptOnly, GenerateDeploymentReport in begin; Type, PublishXml,
/// DacOption, ScriptOnly, GenerateDeploymentReport in process (the last two tested in both). Type's
/// flag is load-bearing: the :226 auto-detect only fires when -Type was NOT explicitly passed, so a
/// value test would misfire when a caller passes -Type Dacpac for a .bacpac path.
///
/// FOUR $Pscmdlet.ShouldProcess gates (:347 generating script, :351 dacpac publish, :356 bacpac
/// import, :364 report/output) route to the real cmdlet via $__realCmdlet - the three destructive
/// operations plus the output stage, all reachable at the default $ConfirmPreference under
/// ConfirmImpact Medium. In-hop Stop-Function/Write-Message calls carry -FunctionName. -DacOption
/// carries Alias("Option"). The three switches (GenerateDeploymentReport, ScriptOnly,
/// IncludeSqlCmdVars) and inherited EnableException cross as SwitchParameter OBJECTS received untyped.
/// -OutputPath's default derives from Get-DbatoolsConfigValue - a DEF-007 bind-time default resolved
/// in the hop. Surface pinned by migration/baselines/Publish-DbaDacPackage.json.
/// </summary>
[Cmdlet(VerbsData.Publish, "DbaDacPackage", DefaultParameterSetName = "Obj", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Medium)]
public sealed class PublishDbaDacPackageCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance(s). Source declares [DbaInstance[]]; the accelerator
    /// resolves to DbaInstanceParameter[] on the surface, which the baseline confirms.</summary>
    [Parameter]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Alternative credential for the target instances.</summary>
    [Parameter]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>Path to the dacpac or bacpac file.</summary>
    [Parameter(Mandatory = true, ValueFromPipelineByPropertyName = true)]
    [PsStringCast]
    public string Path { get; set; } = null!;

    /// <summary>Path to a publish profile XML (Xml parameter set).</summary>
    [Parameter(ParameterSetName = "Xml")]
    [PsStringCast]
    public string? PublishXml { get; set; }

    /// <summary>The database(s) to publish to.</summary>
    [Parameter(Mandatory = true, ValueFromPipelineByPropertyName = true)]
    [PsStringArrayCast]
    public string[] Database { get; set; } = null!;

    /// <summary>Connection strings to publish against instead of connecting.</summary>
    [Parameter]
    [PsStringArrayCast]
    public string[]? ConnectionString { get; set; }

    /// <summary>Generate a deployment report.</summary>
    [Parameter]
    public SwitchParameter GenerateDeploymentReport { get; set; }

    /// <summary>Generate the script only, without executing.</summary>
    [Parameter]
    public SwitchParameter ScriptOnly { get; set; }

    /// <summary>The package type; auto-detected from a .bacpac extension when not passed.</summary>
    [Parameter]
    [ValidateSet("Dacpac", "Bacpac")]
    [PsStringCast]
    public string Type { get; set; } = "Dacpac";

    /// <summary>Directory for generated script/report output.</summary>
    [Parameter]
    [PsStringCast]
    public string? OutputPath { get; set; }

    /// <summary>Prompt for SqlCmd variable values.</summary>
    [Parameter]
    public SwitchParameter IncludeSqlCmdVars { get; set; }

    /// <summary>A pre-built publish options object (Obj parameter set).</summary>
    [Parameter(ParameterSetName = "Obj")]
    [Alias("Option")]
    public object? DacOption { get; set; }

    /// <summary>Path to the DacFx assembly to load.</summary>
    [Parameter]
    [PsStringCast]
    public string? DacFxPath { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // begin's $ConnectionString/$defaultColumns; opaque to C#.
    private Hashtable? _beginState;
    // the $ConnectionString accumulator and mutated $Type carried record -> record; opaque to C#.
    private Hashtable? _state;
    // a failed begin guard silences every record.
    private bool _interrupted;

    protected override void BeginProcessing()
    {
        if (Interrupted)
            return;

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, BeginScript,
            ConnectionString, ScriptOnly, GenerateDeploymentReport, Type, DacFxPath, EnableException,
            MyInvocation.BoundParameters.ContainsKey("SqlInstance"),
            MyInvocation.BoundParameters.ContainsKey("ConnectionString"),
            MyInvocation.BoundParameters.ContainsKey("ScriptOnly"),
            MyInvocation.BoundParameters.ContainsKey("GenerateDeploymentReport"),
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
        {
            if (item?.BaseObject is Hashtable sentinel && sentinel.ContainsKey("__publishDbaDacPackageBegin"))
            {
                if (sentinel["__publishDbaDacPackageBegin"] is Hashtable state)
                {
                    _beginState = state;
                    _interrupted = LanguagePrimitives.IsTrue(state["Interrupted"]);
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
        if (Interrupted || _interrupted)
            return;

        // Streaming, not buffered (DEF-001): the command publishes database by database and emits a
        // result object per database, so a buffered hop would discard the record of publishes already
        // performed when a later database's failure terminated the hop under -EnableException.
        NestedCommand.InvokeScopedStreaming(this, item =>
        {
            if (item?.BaseObject is Hashtable sentinel && sentinel.ContainsKey("__publishDbaDacPackageProcess"))
            {
                _state = sentinel["__publishDbaDacPackageProcess"] as Hashtable;
                return;
            }
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                RemoveHopErrorBookkeeping(nestedError);
                WriteError(nestedError);
                return;
            }
            WriteObject(item);
        }, ProcessScript,
            SqlInstance, SqlCredential, Path, PublishXml, Database, ConnectionString,
            GenerateDeploymentReport, Type, OutputPath, IncludeSqlCmdVars, DacOption, EnableException,
            _beginState, _state, this,
            MyInvocation.BoundParameters.ContainsKey("Type"),
            MyInvocation.BoundParameters.ContainsKey("PublishXml"),
            MyInvocation.BoundParameters.ContainsKey("DacOption"),
            MyInvocation.BoundParameters.ContainsKey("ScriptOnly"),
            MyInvocation.BoundParameters.ContainsKey("GenerateDeploymentReport"),
            MyInvocation.BoundParameters.ContainsKey("OutputPath"),
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

    // PS: the begin block VERBATIM, dot-sourced. Edits: four Test-Bound probes become carried
    // boundness flags, plus -FunctionName stamps. The sentinel carries the Convert-mutated
    // $ConnectionString, the once-computed $defaultColumns, and the interrupt latch.
    private const string BeginScript = """
param($ConnectionString, $ScriptOnly, $GenerateDeploymentReport, $Type, $DacFxPath, $EnableException, $__boundSqlInstance, $__boundConnectionString, $__boundScriptOnly, $__boundGenerateDeploymentReport, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([string[]]$ConnectionString, $ScriptOnly, $GenerateDeploymentReport, [string]$Type, [String]$DacFxPath, $EnableException, $__boundSqlInstance, $__boundConnectionString, $__boundScriptOnly, $__boundGenerateDeploymentReport, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    . {
        if ((-not $__boundSqlInstance) -and (-not $__boundConnectionString)) {
            Stop-Function -Message "You must specify either SqlInstance or ConnectionString." -FunctionName Publish-DbaDacPackage
            return
        }
        if ($ConnectionString) {
            $ConnectionString = $ConnectionString | Convert-ConnectionString
        }
        if ($Type -eq 'Dacpac') {
            if (($__boundScriptOnly) -or ($__boundGenerateDeploymentReport)) {
                $defaultColumns = 'ComputerName', 'InstanceName', 'SqlInstance', 'Database', 'Dacpac', 'PublishXml', 'Result', 'DatabaseScriptPath', 'MasterDbScriptPath', 'DeploymentReport', 'DeployOptions', 'SqlCmdVariableValues'
            } else {
                $defaultColumns = 'ComputerName', 'InstanceName', 'SqlInstance', 'Database', 'Dacpac', 'PublishXml', 'Result', 'DeployOptions', 'SqlCmdVariableValues'
            }
        } elseif ($Type -eq 'Bacpac') {
            if ($ScriptOnly -or $GenerateDeploymentReport) {
                Stop-Function -Message "ScriptOnly and GenerateDeploymentReport cannot be used in a Bacpac scenario." -FunctionName Publish-DbaDacPackage
                return
            }
            $defaultColumns = 'ComputerName', 'InstanceName', 'SqlInstance', 'Database', 'Bacpac', 'Result', 'DeployOptions'
        }

        function Get-ServerName ($connString) {
            $builder = New-Object System.Data.Common.DbConnectionStringBuilder
            $builder.set_ConnectionString($connString)
            $instance = $builder['data source']

            if (-not $instance) {
                $instance = $builder['server']
            }

            return $instance.ToString().Replace('\', '-').Replace('(', '').Replace(')', '')
        }

        if ($DacFxPath) {
            try {
                Add-Type -Path $DacFxPath
                Write-Message -Level Verbose -Message "Dac Fx loaded from [$DacFxPath]." -FunctionName Publish-DbaDacPackage
            } catch {
                Stop-Function -Message "Dac Fx could not be loaded from [$DacFxPath]." -ErrorRecord $_ -FunctionName Publish-DbaDacPackage
                return
            }
        }
    }

    $__iv = Get-Variable -Name __dbatools_interrupt_function_78Q9VPrM6999g6zo24Qn83m09XF56InEn4hFrA8Fwhu5xJrs6r -Scope 0 -ErrorAction Ignore
    $__cs = Get-Variable -Name ConnectionString -Scope 0 -ErrorAction Ignore
    $__dc = Get-Variable -Name defaultColumns -Scope 0 -ErrorAction Ignore
    $__csv = $null; if ($__cs) { $__csv = $__cs.Value }
    $__dcv = $null; if ($__dc) { $__dcv = $__dc.Value }
    @{ __publishDbaDacPackageBegin = @{ Interrupted = [bool]($__iv -and $__iv.Value); ConnectionString = $__csv; DefaultColumns = $__dcv } }
} $ConnectionString $ScriptOnly $GenerateDeploymentReport $Type $DacFxPath $EnableException $__boundSqlInstance $__boundConnectionString $__boundScriptOnly $__boundGenerateDeploymentReport $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
    // PS: the process block VERBATIM, dot-sourced. Edits: five Test-Bound probes become carried
    // boundness flags, the four $Pscmdlet gates route to $__realCmdlet, plus -FunctionName stamps.
    //
    // The begin helper Get-ServerName is recreated here because begin's scope is gone (:299 calls it).
    // $ConnectionString restores from the PROCESS carry across records, else from begin's Convert'd
    // value on the first record. $Type restores from the process carry (its :227 auto-detect
    // persists across records in the source). $defaultColumns is carried from begin as-is, never
    // recomputed. -OutputPath's DEF-007 config default is resolved when the caller omitted it.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Path, $PublishXml, $Database, $ConnectionString, $GenerateDeploymentReport, $Type, $OutputPath, $IncludeSqlCmdVars, $DacOption, $EnableException, $__beginState, $__state, $__realCmdlet, $__boundType, $__boundPublishXml, $__boundDacOption, $__boundScriptOnly, $__boundGenerateDeploymentReport, $__boundOutputPath, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundWhatIf) { $__commonParameters.WhatIf = [bool]$__boundWhatIf }
if ($null -ne $__boundConfirm) { $__commonParameters.Confirm = [bool]$__boundConfirm }
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = "Medium")]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [string]$Path, [string]$PublishXml, [string[]]$Database, [string[]]$ConnectionString, $GenerateDeploymentReport, [string]$Type, [string]$OutputPath, $IncludeSqlCmdVars, [object]$DacOption, $EnableException, $__beginState, $__state, $__realCmdlet, $__boundType, $__boundPublishXml, $__boundDacOption, $__boundScriptOnly, $__boundGenerateDeploymentReport, $__boundOutputPath, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    # the begin helper, recreated (begin's scope does not reach this hop)
    function Get-ServerName ($connString) {
        $builder = New-Object System.Data.Common.DbConnectionStringBuilder
        $builder.set_ConnectionString($connString)
        $instance = $builder['data source']
    
        if (-not $instance) {
            $instance = $builder['server']
        }
    
        return $instance.ToString().Replace('\', '-').Replace('(', '').Replace(')', '')
    }

    # DEF-007: -OutputPath's source default "(Get-DbatoolsConfigValue -FullName 'Path.DbatoolsExport')"
    # is a bind-time default a C# initializer cannot express; resolve it here when the caller omitted
    # it, or Join-Path at :455/:459/:503 receives $null. (codex r1 - I documented this and had not
    # implemented it.)
    if (-not $__boundOutputPath) { $OutputPath = Get-DbatoolsConfigValue -FullName 'Path.DbatoolsExport' }

    # begin's once-computed column set (carried as-is, so an auto-detected Bacpac keeps begin's choice)
    $defaultColumns = $__beginState.DefaultColumns
    # $ConnectionString: the process carry across records, else begin's Convert'd value on record 1
    if ($null -ne $__state) { $ConnectionString = $__state.ConnectionString } else { $ConnectionString = $__beginState.ConnectionString }
    # $Type: its :227 auto-detect persists across records in the source
    if ($null -ne $__state) { $Type = $__state.Type }
    # $result (:348/:352, conditional under the publish/script gates) is read in the finally at
    # :367/:373; under -WhatIf or a declined gate it is unassigned this record and the SOURCE reads a
    # PREVIOUS record's value. $server (:252/:384) is read as -Target at :333; a ConnectionString-only
    # record never assigns it and the source reads the prior record's. Both carry with Assigned flags
    # so a first record leaves them undefined, as the source does. (codex r1.)
    if ($null -ne $__state -and $__state.ResultAssigned) { $result = $__state.Result }
    if ($null -ne $__state -and $__state.ServerAssigned) { $server = $__state.Server }

    . {
        if (Test-FunctionInterrupt) {
            return
        }

        if (-not (Test-Path -Path $Path)) {
            Stop-Function -Message "$Path not found." -FunctionName Publish-DbaDacPackage
            return
        }

        # auto detect if a .bacpac was passed in, just in case the -Type param was not specified
        if (-not ($__boundType) -and [IO.Path]::GetExtension($Path) -eq '.bacpac') {
            $Type = 'Bacpac'
        }

        #Check Option object types - should have a specific type
        if ($Type -eq 'Dacpac') {
            if ($DacOption -and $DacOption -isnot [Microsoft.SqlServer.Dac.PublishOptions]) {
                Stop-Function -Message "Microsoft.SqlServer.Dac.PublishOptions object type is expected for `"-Type Dacpac`" but $($DacOption.GetType()) was passed in." -FunctionName Publish-DbaDacPackage
                return
            }
        } elseif ($Type -eq 'Bacpac') {
            if ($DacOption -and $DacOption -isnot [Microsoft.SqlServer.Dac.DacImportOptions]) {
                Stop-Function -Message "Microsoft.SqlServer.Dac.DacImportOptions object type is expected for `"-Type Bacpac`" but $($DacOption.GetType()) was passed in." -FunctionName Publish-DbaDacPackage
                return
            }
        }

        if ($__boundPublishXml) {
            if (-not (Test-Path -Path $PublishXml)) {
                Stop-Function -Message "$PublishXml not found." -FunctionName Publish-DbaDacPackage
                return
            }
        }

        foreach ($instance in $SqlInstance) {
            try {
                $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential
            } catch {
                Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Publish-DbaDacPackage
            }
            $ConnectionString += $server.ConnectionContext.ConnectionString.Replace('"', "'") | Convert-ConnectionString
        }

        #Use proper class to load the object
        if ($Type -eq 'Dacpac') {
            try {
                $dacPackage = [Microsoft.SqlServer.Dac.DacPackage]::Load($Path)
            } catch {
                Stop-Function -Message "Could not load Dacpac." -ErrorRecord $_ -FunctionName Publish-DbaDacPackage
                return
            }
        } elseif ($Type -eq 'Bacpac') {
            try {
                $bacPackage = [Microsoft.SqlServer.Dac.BacPackage]::Load($Path)
            } catch {
                Stop-Function -Message "Could not load Bacpac." -ErrorRecord $_ -FunctionName Publish-DbaDacPackage
                return
            }
        }
        #Load XML profile when used
        if ($__boundPublishXml) {
            try {
                $options = New-DbaDacOption -Type $Type -Action Publish -PublishXml $PublishXml -EnableException
            } catch {
                Stop-Function -Message "Could not load profile." -ErrorRecord $_ -FunctionName Publish-DbaDacPackage
                return
            }
        }
        #Create/re-use deployment options object
        else {
            if (-not ($__boundDacOption)) {
                $options = New-DbaDacOption -Type $Type -Action Publish
            } else {
                $options = $DacOption
            }
        }
        #Replace variables if defined
        if ($IncludeSqlCmdVars) {
            Get-SqlCmdVars -SqlCommandVariableValues $options.DeployOptions.SqlCommandVariableValues
        }

        foreach ($connString in $ConnectionString) {
            $connString = $connString | Convert-ConnectionString
            $cleaninstance = Get-ServerName $connString
            $instance = $cleaninstance.ToString().Replace('--', '\')

            # Fix for #7704 to take care that $cleaninstance can be used as a filename:
            $cleaninstance = $cleaninstance.Replace(':', '_')

            foreach ($dbName in $Database) {
                #Set deployment properties when specified
                if ($__boundScriptOnly) {
                    $options.GenerateDeploymentScript = $true
                }
                if ($__boundGenerateDeploymentReport) {
                    $options.GenerateDeploymentReport = $GenerateDeploymentReport
                }
                #Set output file paths when needed
                $timeStamp = (Get-Date).ToString("yyMMdd_HHmmss_f")
                if ($options.GenerateDeploymentScript) {
                    if (-not $options.DatabaseScriptPath) {
                        Write-Message -Level Verbose -Message "DatabaseScriptPath not set, using default path." -FunctionName Publish-DbaDacPackage
                        $options.DatabaseScriptPath = Join-Path $OutputPath "$cleaninstance-$dbName`_DeployScript_$timeStamp.sql"
                    }
                    if (-not $options.MasterDbScriptPath) {
                        Write-Message -Level Verbose -Message "MasterDbScriptPath not set, using default path." -FunctionName Publish-DbaDacPackage
                        $options.MasterDbScriptPath = Join-Path $OutputPath "$cleaninstance-$dbName`_Master.DeployScript_$timeStamp.sql"
                    }
                }
                if ($connString -notmatch 'Database=') {
                    $connString = "$connString;Database=$dbName"
                }

                #Create services object
                try {
                    $dacServices = New-Object Microsoft.SqlServer.Dac.DacServices $connString
                } catch {
                    Stop-Function -Message "Error occurred while establishing connection to $instance" -Category ConnectionError -ErrorRecord $_ -Target $server -Continue -FunctionName Publish-DbaDacPackage
                }

                try {
                    $null = $output = Register-ObjectEvent -InputObject $dacServices -EventName "Message" -SourceIdentifier "msg" -ErrorAction SilentlyContinue -Action {
                        $EventArgs.Message.Message
                    }
                    #Perform proper action depending on the Type
                    if ($Type -eq 'Dacpac') {
                        if ($options.GenerateDeploymentScript) {
                            Write-Message -Level Verbose -Message "Generating the deployment script as requested by the caller." -FunctionName Publish-DbaDacPackage
                            if (!$options.DatabaseScriptPath) {
                                Stop-Function -Message "DatabaseScriptPath option should be specified when running with -ScriptOnly" -EnableException $true -FunctionName Publish-DbaDacPackage
                            }
                            if ($__realCmdlet.ShouldProcess($instance, "Generating script")) {
                                $result = $dacServices.Script($dacPackage, $dbName, $options)
                            }
                        } else {
                            if ($__realCmdlet.ShouldProcess($instance, "Executing Dacpac publish")) {
                                $result = $dacServices.Publish($dacPackage, $dbName, $options)
                            }
                        }
                    } elseif ($Type -eq 'Bacpac') {
                        if ($__realCmdlet.ShouldProcess($instance, "Executing Bacpac import")) {
                            $dacServices.ImportBacpac($bacPackage, $dbName, $options, $null)
                        }
                    }
                } catch [Microsoft.SqlServer.Dac.DacServicesException] {
                    Stop-Function -Message "Deployment failed" -ErrorRecord $_ -Continue -FunctionName Publish-DbaDacPackage
                } finally {
                    Unregister-Event -SourceIdentifier "msg"
                    if ($__realCmdlet.ShouldProcess($instance, "Generating deployment report and output")) {
                        if ($options.GenerateDeploymentReport) {
                            $deploymentReport = Join-Path $OutputPath "$cleaninstance-$dbName`_Result.DeploymentReport_$timeStamp.xml"
                            $result.DeploymentReport | Out-File $deploymentReport
                            Write-Message -Level Verbose -Message "Deployment Report - $deploymentReport." -FunctionName Publish-DbaDacPackage
                        }
                        if ($options.GenerateDeploymentScript) {
                            Write-Message -Level Verbose -Message "Database change script - $($options.DatabaseScriptPath)." -FunctionName Publish-DbaDacPackage
                            if ((Test-Path $options.MasterDbScriptPath)) {
                                Write-Message -Level Verbose -Message "Master database change script - $($result.MasterDbScript)." -FunctionName Publish-DbaDacPackage
                            }
                        }
                        $resultOutput = ($output.output -join [System.Environment]::NewLine | Out-String).Trim()
                        if ($resultOutput -match "Failed" -and ($options.GenerateDeploymentReport -or $options.GenerateDeploymentScript)) {
                            Write-Message -Level Warning -Message "Seems like the attempt to publish/script may have failed. If scripts have not generated load dacpac into Visual Studio to check SQL is valid." -FunctionName Publish-DbaDacPackage
                        }

                        # Fix for #7704 to take care that named pipe connections to the local host work:
                        $instance = $instance.Replace('NP:.', '.')

                        $server = [dbainstance]$instance
                        if ($Type -eq 'Dacpac') {
                            $output = [PSCustomObject]@{
                                ComputerName         = $server.ComputerName
                                InstanceName         = $server.InstanceName
                                SqlInstance          = $server.FullName
                                Database             = $dbName
                                Result               = $resultOutput
                                Dacpac               = $Path
                                PublishXml           = $PublishXml
                                ConnectionString     = $connString
                                DatabaseScriptPath   = $options.DatabaseScriptPath
                                MasterDbScriptPath   = $options.MasterDbScriptPath
                                DeploymentReport     = $DeploymentReport
                                DeployOptions        = $options.DeployOptions | Select-Object -Property * -ExcludeProperty "SqlCommandVariableValues"
                                SqlCmdVariableValues = $options.DeployOptions.SqlCommandVariableValues.Keys
                            }
                        } elseif ($Type -eq 'Bacpac') {
                            $output = [PSCustomObject]@{
                                ComputerName     = $server.ComputerName
                                InstanceName     = $server.InstanceName
                                SqlInstance      = $server.FullName
                                Database         = $dbName
                                Result           = $resultOutput
                                Bacpac           = $Path
                                ConnectionString = $connString
                                DeployOptions    = $options
                            }
                        }
                        $output | Select-DefaultView -Property $defaultColumns
                    }
                }
            }
        }
    }

    $__cs = Get-Variable -Name ConnectionString -Scope 0 -ErrorAction Ignore
    $__ty = Get-Variable -Name Type -Scope 0 -ErrorAction Ignore
    $__rs = Get-Variable -Name result -Scope 0 -ErrorAction Ignore
    $__sv = Get-Variable -Name server -Scope 0 -ErrorAction Ignore
    $__csv = $null; if ($__cs) { $__csv = $__cs.Value }
    $__tyv = $null; if ($__ty) { $__tyv = $__ty.Value }
    $__rsv = $null; if ($__rs) { $__rsv = $__rs.Value }
    $__svv = $null; if ($__sv) { $__svv = $__sv.Value }
    @{ __publishDbaDacPackageProcess = @{ ConnectionString = $__csv; Type = $__tyv; ResultAssigned = [bool]$__rs; Result = $__rsv; ServerAssigned = [bool]$__sv; Server = $__svv } }
} $SqlInstance $SqlCredential $Path $PublishXml $Database $ConnectionString $GenerateDeploymentReport $Type $OutputPath $IncludeSqlCmdVars $DacOption $EnableException $__beginState $__state $__realCmdlet $__boundType $__boundPublishXml $__boundDacOption $__boundScriptOnly $__boundGenerateDeploymentReport $__boundOutputPath $__boundWhatIf $__boundConfirm $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}