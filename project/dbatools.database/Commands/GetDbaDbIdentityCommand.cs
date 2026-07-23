#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Runs DBCC CHECKIDENT(..., NORESEED) to report the current identity value and column value of each
/// specified table. Port of public/Get-DbaDbIdentity.ps1; the workflow remains a module-scoped PowerShell
/// compatibility hop.
///
/// A begin+process port (SqlInstance is ValueFromPipeline). The begin block builds a StringBuilder holding
/// the DBCC CHECKIDENT(#options#, NORESEED) template; process reads it (ToString()) read-only, so the
/// StringBuilder rides a sentinel one-way begin->process (it survives by reference, not serialized). Three
/// points. (1) SupportsShouldProcess (ConfirmImpact Low): the two $Pscmdlet.ShouldProcess gates become
/// $__realCmdlet.ShouldProcess (the compiled cmdlet is passed as $__realCmdlet; WhatIf/Confirm are handled by
/// the real cmdlet). (2) A Test-Bound guard: `if (Test-Bound -Not -ParameterName Table)` becomes
/// `if (-not $__boundTable)`; on that path Stop-Function (no -Continue) then return. The source has no
/// Test-FunctionInterrupt gate, so the guard fires per record; the interrupt is deliberately NOT carried and
/// the bare return exits the hop scriptblock cleanly. (3) Body edits also add -FunctionName Get-DbaDbIdentity
/// to the four Stop-Function and three Write-Message. Surface pinned by migration/baselines/Get-DbaDbIdentity.json
/// (positions 0-3, SupportsShouldProcess, ConfirmImpact Low).
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaDbIdentity", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Low)]
public sealed class GetDbaDbIdentityCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The database(s) to process.</summary>
    [Parameter(Position = 2)]
    [PsStringArrayCast]
    public string[]? Database { get; set; }

    /// <summary>The table(s) to check the identity value of.</summary>
    [Parameter(Position = 3)]
    [PsStringArrayCast]
    public string[]? Table { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // The DBCC-template StringBuilder built once in begin, carried one-way begin->process.
    private Hashtable? _state;

    protected override void BeginProcessing()
    {
        if (Interrupted)
            return;

        NestedCommand.InvokeScopedStreaming(this, item =>
        {
            if (item?.BaseObject is Hashtable sentinel && sentinel.ContainsKey("__getDbaDbIdentityBegin"))
            {
                _state = sentinel["__getDbaDbIdentityBegin"] as Hashtable;
                return;
            }
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                NestedCommand.RemoveDuplicateError(this, nestedError);
                WriteError(nestedError);
                return;
            }
            WriteObject(item);
        }, BeginScript,
            EnableException.ToBool(), NestedCommand.BoundCommonParameter(this, "Verbose"), NestedCommand.BoundCommonParameter(this, "Debug"));
    }

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        NestedCommand.InvokeScopedStreaming(this, item =>
        {
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                NestedCommand.RemoveDuplicateError(this, nestedError);
                WriteError(nestedError);
                return;
            }
            WriteObject(item);
        }, ProcessScript,
            SqlInstance, SqlCredential, Database, Table, EnableException.ToBool(), _state, this,
            TestBound(nameof(Table)), NestedCommand.BoundCommonParameter(this, "Verbose"), NestedCommand.BoundCommonParameter(this, "Debug"));
    }
    // PS: the begin block VERBATIM (builds the DBCC-template StringBuilder) plus a sentinel carrying it to
    // the process hop. begin has no Stop-Function/Write-Message.
    private const string BeginScript = """
param($EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param($EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

        $stringBuilder = New-Object System.Text.StringBuilder
        $null = $stringBuilder.Append("DBCC CHECKIDENT(#options#, NORESEED)")

    @{ __getDbaDbIdentityBegin = @{ StringBuilder = $stringBuilder } }
} $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
    // PS: the process block VERBATIM. Edits: Test-Bound -Not -ParameterName Table -> -not $__boundTable;
    // $Pscmdlet.ShouldProcess -> $__realCmdlet.ShouldProcess (both gates); -FunctionName Get-DbaDbIdentity on
    // the four Stop-Function and three Write-Message. The StringBuilder is restored read-only from the state.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Database, $Table, $EnableException, $__state, $__realCmdlet, $__boundTable, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [string[]]$Database, [string[]]$Table, $EnableException, $__state, $__realCmdlet, $__boundTable, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    $stringBuilder = $__state.StringBuilder

        if (-not $__boundTable) {
            Stop-Function -Message "You must specify a table to execute against using -Table" -FunctionName Get-DbaDbIdentity
            return
        }
        foreach ($instance in $SqlInstance) {
            Write-Message -Message "Attempting Connection to $instance" -Level Verbose -FunctionName Get-DbaDbIdentity -ModuleName "dbatools"
            try {
                $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential
            } catch {
                Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Get-DbaDbIdentity
            }

            $dbs = $server.Databases

            if ($Database) {
                $dbs = $dbs | Where-Object Name -In $Database
            }

            foreach ($db in $dbs) {
                Write-Message -Level Verbose -Message "Processing $db on $instance" -FunctionName Get-DbaDbIdentity -ModuleName "dbatools"

                if ($db.IsAccessible -eq $false) {
                    Stop-Function -Message "The database $db is not accessible. Skipping." -Continue -FunctionName Get-DbaDbIdentity
                }

                foreach ($tbl in $Table) {
                    try {
                        $query = $StringBuilder.ToString()
                        $nameParts = Get-ObjectNameParts -ObjectName $tbl
                        if ($nameParts.Name) {
                            $escapedTableName = $nameParts.Name.Replace("]", "]]")
                            if ($nameParts.Schema) {
                                $escapedTableSchema = $nameParts.Schema.Replace("]", "]]")
                                $tblIdentifier = "[$escapedTableSchema].[$escapedTableName]"
                            } else {
                                $tblIdentifier = "[$escapedTableName]"
                            }
                        } else {
                            $tblIdentifier = $tbl
                        }
                        $query = $query.Replace('#options#', "'$($tblIdentifier)'")

                        if ($__realCmdlet.ShouldProcess($server.Name, "Execute the command $query against $instance")) {
                            Write-Message -Message "Query to run: $query" -Level Verbose -FunctionName Get-DbaDbIdentity -ModuleName "dbatools"
                            $results = $server | Invoke-DbaQuery  -Query $query -Database $db.Name -MessagesToOutput
                            if ($null -ne $results) {
                                $words = $results.Split(" ")
                                $identityValue = $words[6].Replace("'", "").Replace(",", "")
                                $columnValue = $words[10].Replace("'", "").Replace(".", "")
                            } else {
                                $identityValue = $null
                                $columnValue = $null
                            }
                        }
                    } catch {
                        Stop-Function -Message "Error running  $query against $db" -Target $instance -ErrorRecord $_ -Exception $_.Exception -Continue -FunctionName Get-DbaDbIdentity
                    }
                    if ($__realCmdlet.ShouldProcess("console", "Outputting object")) {
                        [PSCustomObject]@{
                            ComputerName  = $server.ComputerName
                            InstanceName  = $server.ServiceName
                            SqlInstance   = $server.DomainInstanceName
                            Database      = $db.Name
                            Table         = $tbl
                            Cmd           = $query.ToString()
                            IdentityValue = $identityValue
                            ColumnValue   = $columnValue
                            Output        = $results
                        }
                    }
                }
            }
        }
} $SqlInstance $SqlCredential $Database $Table $EnableException $__state $__realCmdlet $__boundTable $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
