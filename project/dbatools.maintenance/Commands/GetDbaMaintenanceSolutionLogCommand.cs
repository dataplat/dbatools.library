#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Parses Ola Hallengren MaintenanceSolution IndexOptimize text log files. The file enumeration,
/// block parsing, and output shaping remain a module-scoped PowerShell compatibility hop; the
/// compiled cmdlet preserves the advanced function's typed pipeline surface. Surface pinned by
/// migration/baselines/Get-DbaMaintenanceSolutionLog.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaMaintenanceSolutionLog", DefaultParameterSetName = "Default")]
public sealed class GetDbaMaintenanceSolutionLogCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    [PsDbaInstanceArrayCast]
    public DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>Which Ola Hallengren maintenance solution log type to parse from text files.</summary>
    [Parameter(Position = 2)]
    [PsStringArrayCast]
    [ValidateSet("IndexOptimize", "DatabaseBackup", "DatabaseIntegrityCheck")]
    public string[] LogType { get; set; } = new[] { "IndexOptimize" };

    /// <summary>Include only log files created on or after this date and time.</summary>
    [Parameter(Position = 3)]
    [PsDateTimeCast]
    public DateTime Since { get; set; }

    /// <summary>Custom directory path where maintenance solution log files are stored.</summary>
    [Parameter(Position = 4)]
    [PsStringCast]
    public string? Path { get; set; }

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
            SqlInstance, SqlCredential, LogType, TestBound(nameof(Since)) ? (object?)Since : null, Path,
            EnableException.ToBool(), BoundCommonParameter("Verbose"), BoundCommonParameter("Debug"));
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

    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $LogType, $Since, $Path, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, $SqlCredential, [string[]]$LogType, $Since, [string]$Path, $EnableException)

    function process-block ($block) {
        $fresh = @{
            'ObjectType'     = $null
            'IndexType'      = $null
            'ImageText'      = $null
            'NewLOB'         = $null
            'FileStream'     = $null
            'ColumnStore'    = $null
            'AllowPageLocks' = $null
            'PageCount'      = $null
            'Fragmentation'  = $null
            'Error'          = $null
        }
        foreach ($l in $block) {
            $splitted = $l -split ': ', 2
            if (($splitted.Length -ne 2) -or ($splitted[0].length -gt 20)) {
                if ($null -eq $fresh['Error']) {
                    $fresh['Error'] = New-Object System.Collections.ArrayList
                }
                $null = $fresh['Error'].Add($l)
                continue
            }
            $k = $splitted[0]
            $v = $splitted[1]
            if ($k -eq 'Date and Time') {
                # this is the end date, we already parsed the start date of the block
                if ($fresh.ContainsKey($k)) {
                    continue
                }
            }
            $fresh[$k] = $v
        }
        if ($fresh.ContainsKey('Command')) {
            if ($fresh['Command'] -match '(SET LOCK_TIMEOUT (?<timeout>\d+); )?ALTER INDEX \[(?<index>[^\]]+)\] ON \[(?<database>[^\]]+)\]\.\[(?<schema>[^]]+)\]\.\[(?<table>[^\]]+)\] (?<action>[^\ ]+)( PARTITION = (?<partition>\d+))? WITH \((?<options>[^\)]+)') {
                $fresh['Index'] = $Matches.index
                $fresh['Statistics'] = $null
                $fresh['Schema'] = $Matches.Schema
                $fresh['Table'] = $Matches.Table
                $fresh['Action'] = $Matches.action
                $fresh['Options'] = $Matches.options
                $fresh['Timeout'] = $Matches.timeout
                $fresh['Partition'] = $Matches.partition
            } elseif ($fresh['Command'] -match '(SET LOCK_TIMEOUT (?<timeout>\d+); )?UPDATE STATISTICS \[(?<database>[^\]]+)\]\.\[(?<schema>[^]]+)\]\.\[(?<table>[^\]]+)\] \[(?<stat>[^\]]+)\]') {
                $fresh['Index'] = $null
                $fresh['Statistics'] = $Matches.stat
                $fresh['Schema'] = $Matches.Schema
                $fresh['Table'] = $Matches.Table
                $fresh['Action'] = $null
                $fresh['Options'] = $null
                $fresh['Timeout'] = $Matches.timeout
                $fresh['Partition'] = $null
            }
        }
        if ($fresh.ContainsKey('Comment')) {
            $commentParts = $fresh['Comment'] -split ', '
            foreach ($part in $commentParts) {
                $indKey, $indValue = $part -split ': ', 2
                if ($fresh.ContainsKey($indKey)) {
                    $fresh[$indKey] = $indValue
                }
            }
        }
        if ($null -ne $fresh['Error']) {
            $fresh['Error'] = $fresh['Error'] -join "`n"
        }

        return $fresh
    }

    foreach ($instance in $SqlInstance) {
        $logDir = $logFiles = $null
        $computername = $instance.ComputerName

        try {
            $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Get-DbaMaintenanceSolutionLog
        }
        if ($LogType -ne 'IndexOptimize') {
            Write-Message -Level Warning -Message "Parsing $LogType is not supported at the moment" -FunctionName Get-DbaMaintenanceSolutionLog -ModuleName "dbatools"
            Continue
        }
        if (!$instance.IsLocalHost -and $server.HostPlatform -ne "Windows") {
            Write-Message -Level Warning -Message "The target instance is not Windows so logs cannot be fetched remotely" -FunctionName Get-DbaMaintenanceSolutionLog -ModuleName "dbatools"
            Continue
        }
        if ($Path) {
            $logDir = Join-AdminUnc -Servername $server.ComputerName -Filepath $Path
        } else {
            $logDir = Join-AdminUnc -Servername $server.ComputerName -Filepath $server.errorlogpath # -replace '^(.):', "\\$computername\`$1$"
        }
        if (!$logDir) {
            Write-Message -Level Warning -Message "No log directory returned from $instance" -FunctionName Get-DbaMaintenanceSolutionLog -ModuleName "dbatools"
            Continue
        }

        Write-Message -Level Verbose -Message "Log directory on $computername is $logDir" -FunctionName Get-DbaMaintenanceSolutionLog -ModuleName "dbatools"
        if (! (Test-Path $logDir)) {
            Write-Message -Level Warning -Message "Directory $logDir is not accessible" -FunctionName Get-DbaMaintenanceSolutionLog -ModuleName "dbatools"
            continue
        }
        $logFiles = [System.IO.Directory]::EnumerateFiles("$logDir", "IndexOptimize_*.txt")
        if ($Since) {
            $filteredLogs = @()
            foreach ($l in $logFiles) {
                $base = $($l.Substring($l.Length - 15, 15))
                try {
                    $dateFile = [DateTime]::ParseExact($base, 'yyyyMMdd_HHmmss', $null)
                } catch {
                    $dateFile = Get-ItemProperty -Path $l | Select-Object -ExpandProperty CreationTime
                }
                if ($dateFile -gt $since) {
                    $filteredLogs += $l
                }
            }
            $logFiles = $filteredLogs
        }
        if (! $logFiles.count -ge 1) {
            Write-Message -Level Warning -Message "No log files returned from $computername" -FunctionName Get-DbaMaintenanceSolutionLog -ModuleName "dbatools"
            Continue
        }
        $instanceInfo = @{ }
        $instanceInfo['ComputerName'] = $server.ComputerName
        $instanceInfo['InstanceName'] = $server.ServiceName
        $instanceInfo['SqlInstance'] = $server.Name

        foreach ($File in $logFiles) {
            Write-Message -Level Verbose -Message "Reading $file" -FunctionName Get-DbaMaintenanceSolutionLog -ModuleName "dbatools"
            $text = New-Object System.IO.StreamReader -ArgumentList "$File"
            $block = New-Object System.Collections.ArrayList
            $remember = @{ }
            while ($line = $text.ReadLine()) {

                $real = $line.Trim()
                if ($real.Length -eq 0) {
                    $processed = process-block $block
                    if ('Procedure' -in $processed.Keys) {
                        $block = New-Object System.Collections.ArrayList
                        continue
                    }
                    if ('Database' -in $processed.Keys) {
                        Write-Message -Level Verbose -Message "Index and Stats Optimizations on Database $($processed.Database) on $computername" -FunctionName Get-DbaMaintenanceSolutionLog -ModuleName "dbatools"
                        $processed.Remove('Is accessible')
                        $processed.Remove('User access')
                        $processed.Remove('Date and time')
                        $processed.Remove('Standby')
                        $processed.Remove('Recovery Model')
                        $processed.Remove('Updateability')
                        $processed['Database'] = $processed['Database'].Trim('[]')
                        $remember = $processed.Clone()
                    } else {
                        foreach ($k in $processed.Keys) {
                            $remember[$k] = $processed[$k]
                        }
                        $remember.Remove('Command')
                        $remember['StartTime'] = [dbadatetime]([DateTime]::ParseExact($remember['Date and time'] , "yyyy-MM-dd HH:mm:ss", $null))
                        $remember.Remove('Date and time')
                        $remember['Duration'] = ($remember['Duration'] -as [timespan])
                        [PSCustomObject]$remember
                    }
                    $block = New-Object System.Collections.ArrayList
                } else {
                    $null = $block.Add($real)
                }
            }
            $text.close()
        }
    }
} $SqlInstance $SqlCredential $LogType $Since $Path $EnableException @__commonParameters 3>&1 2>&1
""";
}
