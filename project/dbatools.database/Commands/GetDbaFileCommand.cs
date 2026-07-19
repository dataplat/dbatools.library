#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Enumerates files under a SQL Server instance's paths via xp_dirtree. Port of public/Get-DbaFile.ps1;
/// the workflow remains a module-scoped PowerShell compatibility hop.
///
/// A process-only port. The source begin block defines nested helper functions (Get-SQLDirTreeQuery - which closes
/// over $Depth - and Format-Path) AND computes $FileTypeComparison from $FileType. All of it is param-derived with
/// no cross-record accumulation ($sql is re-set per record; $FileTypeComparison recomputes from the constant
/// $FileType), so the WHOLE begin block is PREPENDED (verbatim) into the process hop and re-runs per pipeline record,
/// which is behaviorally identical (the prepend pattern). One Test-Bound: Test-Bound -ParameterName Path ->
/// $__boundPath (carried flag; Path is ALSO passed as a value and defaulted from Get-DbaDefaultPath when unbound).
/// Depth is an int with a source default of 1, reproduced via the compiled property initializer. No value-passed
/// switch, no ShouldProcess. Edits: -FunctionName Get-DbaFile on the process's one Stop-Function (-Continue) and
/// three Write-Message. Surface pinned by migration/baselines/Get-DbaFile.json (positions 0-4, SqlInstance Mandatory
/// VFP pos0, no ShouldProcess).
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaFile")]
public sealed class GetDbaFileCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The path(s) to enumerate (defaults to the instance's default data directory).</summary>
    [Parameter(Position = 2)]
    public string[]? Path { get; set; }

    /// <summary>Filter to files with the specified extension(s).</summary>
    [Parameter(Position = 3)]
    public string[]? FileType { get; set; }

    /// <summary>How many directory levels deep to enumerate (default 1).</summary>
    [Parameter(Position = 4)]
    public int Depth { get; set; } = 1;

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
        }, ProcessScript,
            SqlInstance, SqlCredential, Path, FileType, Depth, EnableException.ToBool(),
            TestBound(nameof(Path)), BoundCommonParameter("Verbose"), BoundCommonParameter("Debug"));
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
    // PS: the source begin block (nested helper functions Get-SQLDirTreeQuery/Format-Path + the FileTypeComparison
    // computation, VERBATIM) PREPENDED to the process block. Process edits: -FunctionName Get-DbaFile on the one
    // Stop-Function (-Continue) and three Write-Message; Test-Bound -ParameterName Path -> $__boundPath.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Path, $FileType, $Depth, $EnableException, $__boundPath, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [string[]]$Path, [string[]]$FileType, [int]$Depth, $EnableException, $__boundPath, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

        $sql = ""

        function Get-SQLDirTreeQuery {
            param
            (
                $PathList
            )

            $q1 += "DECLARE @myPath NVARCHAR(4000);
                    DECLARE @depth SMALLINT = $Depth;

                    IF OBJECT_ID('tempdb..#DirectoryTree') IS NOT NULL
                    DROP TABLE #DirectoryTree;

                    CREATE TABLE #DirectoryTree (
                       id INT IDENTITY(1,1)
                       ,subdirectory NVARCHAR(512)
                       ,depth INT
                       ,isfile BIT
                       , ParentDirectory INT
                       ,flag TINYINT DEFAULT(0));"

            $q2 = "SET @myPath = 'dirname'
                    -- top level directory
                    INSERT #DirectoryTree (subdirectory,depth,isfile)
                       VALUES (@myPath,0,0);
                    -- all the rest under top level
                    INSERT #DirectoryTree (subdirectory,depth,isfile)
                       EXEC master.sys.xp_dirtree @myPath,@depth,1;


                    UPDATE #DirectoryTree
                       SET ParentDirectory = (
                          SELECT MAX(id) FROM #DirectoryTree
                          WHERE depth = d.depth - 1 AND id < d.id   )
                    FROM #DirectoryTree d
                    WHERE ParentDirectory IS NULL;"

            $query_files_sql = "-- SEE all with full paths
                    WITH dirs AS (
                        SELECT
                           id,subdirectory,depth,isfile,ParentDirectory,flag
                           , CAST (NULL AS NVARCHAR(MAX)) AS container
                           , CAST([subdirectory] AS NVARCHAR(MAX)) AS dpath
                           FROM #DirectoryTree
                           WHERE ParentDirectory IS NULL
                        UNION ALL
                        SELECT
                           d.id,d.subdirectory,d.depth,d.isfile,d.ParentDirectory,d.flag
                           , dpath AS container
                           , dpath +'\'+d.[subdirectory]
                        FROM #DirectoryTree AS d
                        INNER JOIN dirs ON  d.ParentDirectory = dirs.id
                        WHERE dpath NOT LIKE '%RECYCLE.BIN%'
                    )
                    SELECT subdirectory AS filename, container AS filepath, isfile, dpath AS fullpath FROM dirs
                    WHERE container IS NOT NULL
                    -- Dir style ordering
                    ORDER BY container, isfile, subdirectory"

            # build the query string based on how many directories they want to enumerate
            $sql = $q1
            $sql += $($PathList | Where-Object { $_ -ne '' } | ForEach-Object { "$([System.Environment]::Newline)$($q2 -Replace 'dirname', $_)" })
            $sql += $query_files_sql
            #Write-Message -Level Debug -Message $sql
            return $sql
        }

        function Format-Path {
            param ($path)
            $path = $path.Trim()
            #Thank you windows 2000
            $path = $path -replace '[^A-Za-z0-9 _\.\-\\:]', '__'
            return $path
        }

        if ($FileType) {
            $FileTypeComparison = $FileType | ForEach-Object { $_.ToLowerInvariant() } | Where-Object { $_ } | Sort-Object | Get-Unique
        }

        foreach ($instance in $SqlInstance) {
            try {
                $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential
            } catch {
                Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -FunctionName Get-DbaFile -Continue
            }

            # Get the default data and log directories from the instance
            if (-not ($__boundPath)) {
                $Path = (Get-DbaDefaultPath -SqlInstance $server).Data
            }
            if (Test-HostOSLinux -SqlInstance $server) {
                $separator = "/"
            } else {
                $separator = "\"
            }

            Write-Message -Level Verbose -Message "Adding paths" -FunctionName Get-DbaFile -ModuleName "dbatools"
            $sql = Get-SQLDirTreeQuery $Path
            Write-Message -Level Debug -Message $sql -FunctionName Get-DbaFile -ModuleName "dbatools"

            # This should remain as not .Query() to be compat with a PSProvider Chrissy was working on
            $datatable = $server.ConnectionContext.ExecuteWithResults($sql).Tables.Rows

            Write-Message -Level Verbose -Message "$($datatable.Rows.Count) files found." -FunctionName Get-DbaFile -ModuleName "dbatools"
            if ($FileTypeComparison) {
                foreach ($row in $datatable) {
                    foreach ($type in $FileTypeComparison) {
                        if ($row.filename.ToLowerInvariant().EndsWith(".$type")) {
                            $fullpath = $row.fullpath.Replace("\", $separator)

                            # Replacing all instances of '\\' with single backslashes '\', and maintain the leading SMB share path represented by the initial '\\'.
                            $is_smb_share_path = $fullpath.SubString(0, 2) -eq "\\"
                            $fullpath = $fullpath.Replace("\\", "\")
                            if ($is_smb_share_path) {
                                $fullpath = $fullpath -replace "^\\", "\\"
                            }

                            $fullpath = $fullpath.Replace("//", "/")
                            [PSCustomObject]@{
                                ComputerName   = $server.ComputerName
                                InstanceName   = $server.ServiceName
                                SqlInstance    = $server.DomainInstanceName
                                Filename       = $fullpath
                                RemoteFilename = Join-AdminUnc -Servername $server.ComputerName -Filepath $fullpath
                            } | Select-DefaultView -ExcludeProperty ComputerName, InstanceName, RemoteFilename
                        }
                    }
                }
            } else {
                foreach ($row in $datatable) {
                    $fullpath = $row.fullpath
                    $fullpath = $fullpath.Replace("\", $separator)
                    $fullpath = $fullpath.Replace("\\", "\")
                    $fullpath = $fullpath.Replace("//", "/")
                    [PSCustomObject]@{
                        ComputerName   = $server.ComputerName
                        InstanceName   = $server.ServiceName
                        SqlInstance    = $server.DomainInstanceName
                        Filename       = $fullpath
                        RemoteFilename = Join-AdminUnc -Servername $server.ComputerName -Filepath $fullpath
                    } | Select-DefaultView -ExcludeProperty ComputerName, InstanceName, RemoteFilename
                }
            }
        }
} $SqlInstance $SqlCredential $Path $FileType $Depth $EnableException $__boundPath $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
