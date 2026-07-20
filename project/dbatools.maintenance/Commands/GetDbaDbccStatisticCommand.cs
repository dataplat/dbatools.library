#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using System.Text;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves statistics information via DBCC SHOW_STATISTICS. The option-driven query construction,
/// per-database statistics enumeration, and per-row output shaping remain a module-scoped PowerShell
/// compatibility hop; the compiled cmdlet preserves the advanced function's begin/process lifetime
/// and typed pipeline surface. Surface pinned by migration/baselines/Get-DbaDbccStatistic.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaDbccStatistic")]
public sealed class GetDbaDbccStatisticCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    [PsDbaInstanceArrayCast]
    public DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>Databases to analyze for statistics information.</summary>
    [Parameter(Position = 2)]
    [PsStringArrayCast]
    public string[]? Database { get; set; }

    /// <summary>The table or indexed view to analyze (two-part name Schema.ObjectName).</summary>
    [Parameter(Position = 3)]
    [PsStringCast]
    public string? Object { get; set; }

    /// <summary>The exact statistics object, index, or column name to analyze.</summary>
    [Parameter(Position = 4)]
    [PsStringCast]
    public string? Target { get; set; }

    /// <summary>Which type of statistics data to return from DBCC SHOW_STATISTICS.</summary>
    [Parameter(Position = 5)]
    [PsStringCast]
    [ValidateSet("StatHeader", "DensityVector", "Histogram", "StatsStream")]
    public string Option { get; set; } = "StatHeader";

    /// <summary>Suppresses informational messages from DBCC SHOW_STATISTICS output.</summary>
    [Parameter]
    public SwitchParameter NoInformationalMessages { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    private object? _stringBuilder;
    private object? _statList;

    protected override void BeginProcessing()
    {
        foreach (PSObject? item in NestedCommand.InvokeScoped(this, BeginScript,
            Option, BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
        {
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                RemoveHopErrorBookkeeping(nestedError);
                WriteError(nestedError);
            }
            else if (item is not null && LanguagePrimitives.IsTrue(
                item.Properties["__GetDbaDbccStatisticBeginComplete"]?.Value))
            {
                _stringBuilder = UnwrapHopValue(item.Properties["StringBuilder"]?.Value);
                _statList = UnwrapHopValue(item.Properties["StatList"]?.Value);
            }
            else if (item is not null)
            {
                WriteObject(item);
            }
        }
    }

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
            _stringBuilder, _statList, SqlInstance, SqlCredential, Database, Object, Target, Option,
            EnableException.ToBool(), TestBound(nameof(Object)), TestBound(nameof(Target)),
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug"));
    }

    // Carried hop state arrives PSObject-wrapped. A PSCustomObject carries its content on the
    // wrapper rather than the BaseObject, so unwrapping one would discard it - keep it wrapped.
    private static object? UnwrapHopValue(object? value)
    {
        if (value is PSObject wrapper && wrapper.BaseObject is not PSCustomObject)
            return wrapper.BaseObject;
        return value;
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

    private const string BeginScript = """
param($Option, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([string]$Option)

    $stringBuilder = New-Object System.Text.StringBuilder
    $null = $stringBuilder.Append("DBCC SHOW_STATISTICS(#options#) WITH NO_INFOMSGS" )
    if ($Option -eq 'StatHeader') {
        $null = $stringBuilder.Append(", STAT_HEADER")
    } elseif ($Option -eq 'DensityVector') {
        $null = $stringBuilder.Append(", DENSITY_VECTOR")
    } elseif ($Option -eq 'Histogram') {
        $null = $stringBuilder.Append(", HISTOGRAM")
    } elseif ($Option -eq 'StatsStream') {
        $null = $stringBuilder.Append(", STATS_STREAM")
    }

    $statList =
    "SELECT Object, Target, name FROM
        (
            SELECT SCHEMA_NAME(o.schema_id) + '.' + o.name AS Object, st.name AS Target, o.name
            FROM sys.stats st
            INNER JOIN sys.objects o
                ON o.object_id = st.object_id
            WHERE o.type IN ('U', 'V')
        ) a
        "

    [pscustomobject]@{ __GetDbaDbccStatisticBeginComplete = $true; StringBuilder = $stringBuilder; StatList = $statList }
} $Option @__commonParameters 3>&1 2>&1
""";

    private const string ProcessScript = """
param($StringBuilder, $statList, $SqlInstance, $SqlCredential, $Database, $Object, $Target, $Option, $EnableException, $__boundObject, $__boundTarget, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param($StringBuilder, [string]$statList, [Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, $SqlCredential, [string[]]$Database, [string]$Object, [string]$Target, [string]$Option, $EnableException, $__boundObject, $__boundTarget)

    foreach ($instance in $SqlInstance) {
        try {
            $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Get-DbaDbccStatistic
        }

        if ($server.VersionMajor -eq 8) {
            if ((-not $__boundObject) -or (-not $__boundTarget)) {
                Write-Message -Level Warning -Message "You must specify an Object and a Target for SQL Server 2000" -FunctionName Get-DbaDbccStatistic -ModuleName "dbatools"
                continue
            }
        }

        $dbs = $server.Databases

        if ($Database) {
            $dbs = $dbs | Where-Object Name -In $Database
        }

        foreach ($db in $dbs) {
            Write-Message -Level Verbose -Message "Processing $db on $instance" -FunctionName Get-DbaDbccStatistic -ModuleName "dbatools"
            $queryList = @()
            if ($db.IsAccessible -eq $false) {
                Stop-Function -Message "The database $db is not accessible. Skipping." -Continue -FunctionName Get-DbaDbccStatistic
            }

            if (($__boundObject) -and ($__boundTarget)) {
                $query = $StringBuilder.ToString()
                $query = $query.Replace('#options#', "'$Object', '$Target'")

                $queryList += New-Object -TypeName PSObject -Property @{Object = $Object;
                    Target                                                     = $Target;
                    Query                                                      = $query
                }
            } elseif ($__boundObject) {
                $whereFilter = " WHERE (Object = '$object' or name = '$object')"
                $statListFiltered = $statList + $whereFilter
                Write-Message -Level Verbose -Message "Query to execute: $statListFiltered" -FunctionName Get-DbaDbccStatistic -ModuleName "dbatools"
                $statListData = $db.Query($statListFiltered)
                foreach ($statisticObj in  $statListData) {
                    $query = $StringBuilder.ToString()
                    $query = $query.Replace('#options#', "'$($statisticObj.Object)', '$($statisticObj.Target)'")
                    $queryList += New-Object -TypeName PSObject -Property @{Object = $statisticObj.Object;
                        Target                                                     = $statisticObj.Target;
                        Query                                                      = $query
                    }
                }
            } else {
                $statListData = $db.Query($statList)
                foreach ($statisticObj in  $statListData) {
                    $query = $StringBuilder.ToString()
                    $query = $query.Replace('#options#', "'$($statisticObj.Object)', '$($statisticObj.Target)'")
                    $queryList += New-Object -TypeName PSObject -Property @{Object = $statisticObj.Object;
                        Target                                                     = $statisticObj.Target;
                        Query                                                      = $query
                    }
                }
            }

            try {
                foreach ($queryObj in $queryList ) {
                    Write-Message -Message "Running statement $($queryObj.Query)" -Level Verbose -FunctionName Get-DbaDbccStatistic -ModuleName "dbatools"
                    $results = $server | Invoke-DbaQuery  -Query $queryObj.Query -Database $db.Name -MessagesToOutput

                    if ($Option -eq 'StatHeader') {
                        foreach ($row in $results) {
                            [PSCustomObject]@{
                                ComputerName           = $server.ComputerName
                                InstanceName           = $server.ServiceName
                                SqlInstance            = $server.DomainInstanceName
                                Database               = $db.Name
                                Object                 = $queryObj.Object
                                Target                 = $queryObj.Target
                                Cmd                    = $queryObj.Query
                                Name                   = $row[0]
                                Updated                = $row[1]
                                Rows                   = $row[2]
                                RowsSampled            = $row[3]
                                Steps                  = $row[4]
                                Density                = $row[5]
                                AverageKeyLength       = $row[6]
                                StringIndex            = $row[7]
                                FilterExpression       = $row[8]
                                UnfilteredRows         = $row[9]
                                PersistedSamplePercent = $row[10]
                            }
                        }
                    }
                    if ($Option -eq 'DensityVector') {
                        foreach ($row in $results) {
                            [PSCustomObject]@{
                                ComputerName  = $server.ComputerName
                                InstanceName  = $server.ServiceName
                                SqlInstance   = $server.DomainInstanceName
                                Database      = $db.Name
                                Object        = $queryObj.Object
                                Target        = $queryObj.Target
                                Cmd           = $queryObj.Query
                                AllDensity    = $row[0].ToString()
                                AverageLength = $row[1]
                                Columns       = $row[2]
                            }
                        }
                    }
                    if ($Option -eq 'Histogram') {
                        foreach ($row in $results) {
                            [PSCustomObject]@{
                                ComputerName      = $server.ComputerName
                                InstanceName      = $server.ServiceName
                                SqlInstance       = $server.DomainInstanceName
                                Database          = $db.Name
                                Object            = $queryObj.Object
                                Target            = $queryObj.Target
                                Cmd               = $queryObj.Query
                                RangeHiKey        = $row[0]
                                RangeRows         = $row[1]
                                EqualRows         = $row[2]
                                DistinctRangeRows = $row[3]
                                AverageRangeRows  = $row[4]
                            }
                        }
                    }
                    if ($Option -eq 'StatsStream') {
                        foreach ($row in $results) {
                            [PSCustomObject]@{
                                ComputerName = $server.ComputerName
                                InstanceName = $server.ServiceName
                                SqlInstance  = $server.DomainInstanceName
                                Database     = $db.Name
                                Object       = $queryObj.Object
                                Target       = $queryObj.Target
                                Cmd          = $queryObj.Query
                                StatsStream  = $row[0]
                                Rows         = $row[1]
                                DataPages    = $row[2]
                            }
                        }
                    }
                }
            } catch {
                Stop-Function -Message "Error capturing data on $db" -Target $instance -ErrorRecord $_ -Exception $_.Exception -Continue -FunctionName Get-DbaDbccStatistic
            }
        }
    }
} $StringBuilder $statList $SqlInstance $SqlCredential $Database $Object $Target $Option $EnableException $__boundObject $__boundTarget @__commonParameters 3>&1 2>&1
""";
}
