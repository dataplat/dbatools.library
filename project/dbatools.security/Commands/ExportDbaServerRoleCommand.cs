#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Exports server-level role definitions from a SQL Server instance as T-SQL scripts.
/// </summary>
/// <remarks>
/// <para>
/// The workflow remains a module-scoped PowerShell compatibility hop so that role enumeration, the
/// generated T-SQL, permission scripting, file output, and dbatools stream and error handling stay
/// observable-identical to the script implementation.
/// </para>
/// <para>
/// The script has begin, process, and end blocks and produces all of its output from the end block,
/// which walks a collection the process block fills. The port collects the piped InputObject records
/// across ProcessRecord and then, in EndProcessing, runs ONE hop that executes the begin body, the
/// process body over the collected records, and the end body in a single scope - so the accumulators
/// persist naturally. The process body is only run when a record was actually processed, matching the
/// script (an empty pipeline runs begin and end but not process); it is dot-sourced so its early
/// returns leave only that block and the end body still emits.
/// </para>
/// <para>
/// The two config-value parameter defaults (Path, BatchSeparator) are reproduced inside the hop,
/// applied only when the caller did not bind them, and the begin block's default ScriptingOptions is
/// created only when ScriptingOptionsObject was not bound. The end block's Get-ExportFilePath call
/// reads the caller's bound Path and FilePath, which a hop cannot see, so those bound values are
/// carried in explicitly. Every switch parameter is carried as a plain bool and received untyped,
/// because a switch in the inner CmdletBinding scriptblock is excluded from positional binding.
/// </para>
/// </remarks>
[Cmdlet(VerbsData.Export, "DbaServerRole")]
public sealed class ExportDbaServerRoleCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Alternative credential for the target instances.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>Server or server-role objects to export, typically piped in.</summary>
    [Parameter(ValueFromPipeline = true, Position = 2)]
    public object[]? InputObject { get; set; }

    /// <summary>Scripting options applied when generating the role definitions.</summary>
    [Parameter(Position = 3)]
    public Microsoft.SqlServer.Management.Smo.ScriptingOptions? ScriptingOptionsObject { get; set; }

    /// <summary>The server role or roles to export.</summary>
    [Parameter(Position = 4)]
    public string[]? ServerRole { get; set; }

    /// <summary>Server roles to exclude.</summary>
    [Parameter(Position = 5)]
    public string[]? ExcludeServerRole { get; set; }

    /// <summary>The directory the exported script is written to.</summary>
    [Parameter(Position = 6)]
    public string? Path { get; set; }

    /// <summary>An explicit output file path.</summary>
    [Parameter(Position = 7)]
    [Alias("OutFile", "FileName")]
    public string? FilePath { get; set; }

    /// <summary>The batch separator placed between statements.</summary>
    [Parameter(Position = 8)]
    public string? BatchSeparator { get; set; }

    /// <summary>The file encoding of the exported script.</summary>
    [Parameter(Position = 9)]
    [ValidateSet("ASCII", "BigEndianUnicode", "Byte", "String", "Unicode", "UTF7", "UTF8", "Unknown")]
    public string Encoding { get; set; } = "UTF8";

    /// <summary>Excludes fixed (built-in) server roles from the export.</summary>
    [Parameter]
    public SwitchParameter ExcludeFixedRole { get; set; }

    /// <summary>Includes role membership (ALTER SERVER ROLE ADD MEMBER) statements.</summary>
    [Parameter]
    public SwitchParameter IncludeRoleMember { get; set; }

    /// <summary>Returns the generated SQL to the pipeline instead of writing a file.</summary>
    [Parameter]
    public SwitchParameter Passthru { get; set; }

    /// <summary>Prevents overwriting an existing output file.</summary>
    [Parameter]
    public SwitchParameter NoClobber { get; set; }

    /// <summary>Appends to the output file instead of replacing it.</summary>
    [Parameter]
    public SwitchParameter Append { get; set; }

    /// <summary>Omits the descriptive comment prefix from the script.</summary>
    [Parameter]
    public SwitchParameter NoPrefix { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    private readonly List<object?[]?> _batches = new List<object?[]?>();

    /// <summary>Records each pipeline record's input as a batch; the work runs once in EndProcessing.</summary>
    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        // One batch per ProcessRecord call, preserving the boundaries the script's process block saw
        // (an empty pipeline never calls ProcessRecord, so there are no batches and process never runs).
        _batches.Add(InputObject);
    }

    /// <summary>Runs the begin, process, and end logic in one hop against the collected input.</summary>
    protected override void EndProcessing()
    {
        if (Interrupted)
            return;

        // [DEF-001] streamed via InvokeScopedStreaming: the hop body loops emitting per-item and
        // carries reachable terminating throws (-Continue Stop-Function under -EnableException), so a
        // buffered InvokeScoped would lose an earlier item's emit when a later item throws. Streaming
        // yields each record as produced; no state carry on this row.
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
            SqlInstance, SqlCredential, _batches.ToArray(), ScriptingOptionsObject, ServerRole, ExcludeServerRole,
            Path, FilePath, BatchSeparator, Encoding,
            ExcludeFixedRole.ToBool(), IncludeRoleMember.ToBool(), Passthru.ToBool(), NoClobber.ToBool(),
            Append.ToBool(), NoPrefix.ToBool(), EnableException.ToBool(),
            TestBound(nameof(Path)), TestBound(nameof(BatchSeparator)), TestBound(nameof(ScriptingOptionsObject)),
            BoundValue("Path"), BoundValue("FilePath"),
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug"));
    }

    private object? BoundValue(string name)
    {
        return MyInvocation.BoundParameters.TryGetValue(name, out object? value) ? value : null;
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

    // PS: the begin body, the process body (dot-sourced, run only when a record was processed), and
    // the end body VERBATIM. Substitutions only: -FunctionName on every direct Stop-Function/Write-Message;
    // Test-Bound on ScriptingOptionsObject -> the carried flag; $PSBoundParameters.Path/.FilePath -> the
    // carried bound values; and $MyInvocation.MyCommand.Name -> the literal command name (a hop scriptblock
    // cannot resolve the public command identity). The two config-value defaults and the process/end split
    // are hop adaptations described in the class remarks. There is no ShouldProcess in this command.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $__batches, $ScriptingOptionsObject, $ServerRole, $ExcludeServerRole, $Path, $FilePath, $BatchSeparator, $Encoding, $ExcludeFixedRole, $IncludeRoleMember, $Passthru, $NoClobber, $Append, $NoPrefix, $EnableException, $__pathBound, $__batchSepBound, $__scriptingBound, $__boundPathValue, $__boundFilePathValue, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [System.Management.Automation.PSCredential]$SqlCredential, $__batches, [Microsoft.SqlServer.Management.Smo.ScriptingOptions]$ScriptingOptionsObject, [string[]]$ServerRole, [string[]]$ExcludeServerRole, [string]$Path, [string]$FilePath, [string]$BatchSeparator, [string]$Encoding, $ExcludeFixedRole, $IncludeRoleMember, $Passthru, $NoClobber, $Append, $NoPrefix, $EnableException, $__pathBound, $__batchSepBound, $__scriptingBound, $__boundPathValue, $__boundFilePathValue, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    if (-not $__pathBound) { $Path = Get-DbatoolsConfigValue -FullName 'Path.DbatoolsExport' }
    if (-not $__batchSepBound) { $BatchSeparator = Get-DbatoolsConfigValue -FullName 'Formatting.BatchSeparator' }

        $null = Test-ExportDirectory -Path $Path
        $outsql = @()
        $outputFileArray = @()
        $roleCollection = New-Object System.Collections.ArrayList
        if ($IsLinux -or $IsMacOs) {
            $executingUser = $env:USER
        } else {
            $executingUser = [Security.Principal.WindowsIdentity]::GetCurrent().Name
        }
        $commandName = "Export-DbaServerRole"

        $roleSQL = "SELECT
                    CASE sperm.state
                        WHEN 'D' THEN 'DENY'
                        WHEN 'G' THEN 'GRANT'
                        WHEN 'R' THEN 'REVOKE'
                        WHEN 'W' THEN 'GRANT'
                    END AS GrantState,
                    sperm.permission_name AS Permission,
                    CASE
                        WHEN sperm.class = 100 THEN ''
                        WHEN sperm.class = 101 AND sp2.type = 'S' THEN 'ON LOGIN::' + QUOTENAME(sp2.name)
                        WHEN sperm.class = 101 AND sp2.type = 'R' THEN 'ON SERVER ROLE::' + QUOTENAME(sp2.name)
                        WHEN sperm.class = 101 AND sp2.type = 'U' THEN 'ON LOGIN::' + QUOTENAME(sp2.name)
                        WHEN sperm.class = 105 THEN 'ON ENDPOINT::' + QUOTENAME(ep.name)
                        WHEN sperm.class = 108 THEN 'ON AVAILABILITY GROUP::' + QUOTENAME(ag.name)
                        ELSE ''
                    END AS OnClause,
                    QUOTENAME(sp.name) AS RoleName,
                    CASE
                        WHEN sperm.state = 'W' THEN 'WITH GRANT OPTION AS ' + QUOTENAME(gsp.name)
                        ELSE ''
                    END AS GrantOption
                FROM sys.server_permissions sperm
                INNER JOIN sys.server_principals sp
                    ON sp.principal_id = sperm.grantee_principal_id
                INNER JOIN sys.server_principals gsp
                    ON gsp.principal_id = sperm.grantor_principal_id
                LEFT JOIN sys.endpoints ep
                    ON ep.endpoint_id = sperm.major_id
                    AND sperm.class = 105
                LEFT JOIN sys.server_principals sp2
                    ON sp2.principal_id = sperm.major_id
                    AND sperm.class = 101
                LEFT JOIN
                (
                    SELECT
                        ar.replica_metadata_id,
                        ag.name
                    FROM sys.availability_groups ag
                    INNER JOIN sys.availability_replicas ar
                        ON ag.group_id = ar.group_id
                ) ag
                    ON ag.replica_metadata_id = sperm.major_id
                    AND sperm.class = 108
                WHERE sp.type='R'
                AND sp.name=N'/*RoleName*/'"

        if (-not $__scriptingBound) {
            $ScriptingOptionsObject = New-DbaScriptingOption
            $ScriptingOptionsObject.AllowSystemObjects = $false
            $ScriptingOptionsObject.ContinueScriptingOnError = $false
            $ScriptingOptionsObject.IncludeDatabaseContext = $true
            $ScriptingOptionsObject.IncludeIfNotExists = $true
            $ScriptingOptionsObject.ScriptOwner = $true
        }

        if ($ScriptingOptionsObject.NoCommandTerminator) {
            $commandTerminator = ''
        } else {
            $commandTerminator = ';'
        }
        $outsql = @()
    foreach ($__batch in $__batches) {
        $InputObject = $__batch
        . {
        if (Test-FunctionInterrupt) {
            return
        }

        if (-not $InputObject -and -not $SqlInstance) {
            Stop-Function -Message "You must pipe in a ServerRole or server or specify a SqlInstance" -FunctionName Export-DbaServerRole
            return
        }

        if ($SqlInstance) {
            $InputObject = $SqlInstance
        }

        foreach ($input in $InputObject) {
            $inputType = $input.GetType().FullName
            switch ($inputType) {
                'Dataplat.Dbatools.Parameter.DbaInstanceParameter' {
                    Write-Message -Level Verbose -Message "Processing DbaInstanceParameter through InputObject" -FunctionName Export-DbaServerRole
                    $serverRoles = Get-DbaServerRole -SqlInstance $input -SqlCredential $SqlCredential  -ServerRole $ServerRole -ExcludeServerRole $ExcludeServerRole -ExcludeFixedRole:$ExcludeFixedRole
                }
                'Microsoft.SqlServer.Management.Smo.Server' {
                    Write-Message -Level Verbose -Message "Processing Server through InputObject" -FunctionName Export-DbaServerRole
                    $serverRoles = Get-DbaServerRole -SqlInstance $input -SqlCredential $SqlCredential -ServerRole $ServerRole -ExcludeServerRole $ExcludeServerRole -ExcludeFixedRole:$ExcludeFixedRole
                }
                'Microsoft.SqlServer.Management.Smo.ServerRole' {
                    Write-Message -Level Verbose -Message "Processing ServerRole through InputObject" -FunctionName Export-DbaServerRole
                    $serverRoles = $input
                }
                default {
                    Stop-Function -Message "InputObject is not a server or serverrole." -FunctionName Export-DbaServerRole
                    return
                }
            }

            foreach ($role in $serverRoles) {
                $server = $role.Parent

                if ($server.ServerType -eq 'SqlAzureDatabase') {
                    Stop-Function -Message "The SqlAzureDatabase - $server is not supported." -Continue -FunctionName Export-DbaServerRole
                }

                try {
                    # Get user defined Server roles
                    if ($server.VersionMajor -ge 11) {
                        $outsql += $role.Script($ScriptingOptionsObject)

                        $query = $roleSQL.Replace('/*RoleName*/', "$($role.Name)")
                        $rolePermissions = $server.Query($query)

                        foreach ($rolePermission in $rolePermissions) {
                            $script = $rolePermission.GrantState + " " + $rolePermission.Permission
                            if ($rolePermission.OnClause) {
                                $script += " " + $rolePermission.OnClause
                            }
                            if ($rolePermission.RoleName) {
                                $script += " TO " + $rolePermission.RoleName
                            }
                            if ($rolePermission.GrantOption) {
                                $script += " " + $rolePermission.GrantOption + $commandTerminator
                            } else {
                                $script += $commandTerminator
                            }
                            $outsql += "$script"
                        }
                    }

                    if ($IncludeRoleMember) {
                        foreach ($roleUser in $role.Login) {
                            $script = 'ALTER SERVER ROLE [' + $role.Role + "] ADD MEMBER [" + $roleUser + "]" + $commandTerminator
                            $outsql += "$script"
                        }
                    }
                    if ($outsql) {
                        $roleObject = [PSCustomObject]@{
                            Name     = $role.Name
                            Instance = $role.SqlInstance
                            Sql      = $outsql
                        }
                    }
                    $roleCollection.Add($roleObject) | Out-Null
                    $outsql = @()
                } catch {
                    $outsql = @()
                    Stop-Function -Message "Error occurred processing role $Role" -Category ConnectionError -ErrorRecord $_ -Target $role.SqlInstance -Continue -FunctionName Export-DbaServerRole
                }
            }
        }
        }
    }
        if (Test-FunctionInterrupt) { return }

        $eol = [System.Environment]::NewLine

        $timeNow = $(Get-Date -Format (Get-DbatoolsConfigValue -FullName 'Formatting.DateTime'))
        foreach ($role in $roleCollection) {
            $instanceName = $role.Instance

            if ($NoPrefix) {
                $prefix = $null
            } else {
                $prefix = "/*$eol`tCreated by $executingUser using dbatools $commandName for objects on $instanceName.$databaseName at $timeNow$eol`tSee https://dbatools.io/$commandName for more information$eol*/"
            }

            if ($BatchSeparator) {
                $sql = $role.SQL -join "$eol$BatchSeparator$eol"
                #add the final GO
                $sql += "$eol$BatchSeparator"
            } else {
                $sql = $role.SQL
            }

            if ($Passthru) {
                if ($null -ne $prefix) {
                    $sql = "$prefix$eol$sql"
                }
                $sql
            } elseif ($Path -Or $FilePath) {
                $outputFileName = $instanceName.Replace('\', '$')
                if ($outputFileArray -notcontains $outputFileName) {
                    Write-Message -Level Verbose -Message "New File $outputFileName " -FunctionName Export-DbaServerRole
                    if ($null -ne $prefix) {
                        $sql = "$prefix$eol$sql"
                    }
                    $scriptPath = Get-ExportFilePath -Path $__boundPathValue -FilePath $__boundFilePathValue -Type sql -ServerName $outputFileName
                    $sql | Out-File -Encoding $Encoding -LiteralPath $scriptPath -Append:$Append -NoClobber:$NoClobber
                    $outputFileArray += $outputFileName
                    Get-ChildItem $scriptPath
                } else {
                    Write-Message -Level Verbose -Message "Adding to $outputFileName " -FunctionName Export-DbaServerRole
                    $sql | Out-File -Encoding $Encoding -LiteralPath $scriptPath -Append
                }
            } else {
                $sql
            }
        }
} $SqlInstance $SqlCredential $__batches $ScriptingOptionsObject $ServerRole $ExcludeServerRole $Path $FilePath $BatchSeparator $Encoding $ExcludeFixedRole $IncludeRoleMember $Passthru $NoClobber $Append $NoPrefix $EnableException $__pathBound $__batchSepBound $__scriptingBound $__boundPathValue $__boundFilePathValue $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1

""";
}
