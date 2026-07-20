#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using System.Text;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves DBCC command syntax help via DBCC HELP. The Statement validation, trace-flag
/// setup, and per-instance query execution remain a module-scoped PowerShell compatibility hop;
/// the compiled cmdlet preserves the advanced function's begin/process lifetime and typed
/// pipeline surface. Surface pinned by migration/baselines/Get-DbaDbccHelp.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaDbccHelp")]
public sealed class GetDbaDbccHelpCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    [PsDbaInstanceArrayCast]
    public DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The DBCC command name to get syntax help for (the portion after "DBCC").</summary>
    [Parameter(Position = 2)]
    [PsStringCast]
    public string? Statement { get; set; }

    /// <summary>Enable access to help for undocumented DBCC commands via trace flag 2588.</summary>
    [Parameter]
    public SwitchParameter IncludeUndocumented { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    private object? _stringBuilder;
    private bool _beginInterrupted;

    protected override void BeginProcessing()
    {
        bool completed = false;
        foreach (PSObject? item in NestedCommand.InvokeScoped(this, BeginScript,
            Statement, EnableException.ToBool(), TestBound(nameof(Statement)),
            TestBound(nameof(IncludeUndocumented)),
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
        {
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                RemoveHopErrorBookkeeping(nestedError);
                WriteError(nestedError);
            }
            else if (item?.BaseObject is StringBuilder stringBuilder)
            {
                _stringBuilder = stringBuilder;
            }
            else if (item is not null && LanguagePrimitives.IsTrue(
                item.Properties["__GetDbaDbccHelpBeginComplete"]?.Value))
            {
                completed = true;
            }
            else if (item is not null)
            {
                WriteObject(item);
            }
        }
        _beginInterrupted = !completed || _stringBuilder is null;
    }

    protected override void ProcessRecord()
    {
        if (_beginInterrupted || Interrupted)
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
            _stringBuilder, SqlInstance, SqlCredential, Statement, EnableException.ToBool(),
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

    private const string BeginScript = """
param($Statement, $EnableException, $__boundStatement, $__boundIncludeUndocumented, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([string]$Statement, $EnableException, $__boundStatement, $__boundIncludeUndocumented)

    if (-not $__boundStatement) {
        Stop-Function -Message "You must specify a value for Statement" -FunctionName Get-DbaDbccHelp
        return
    }
    $stringBuilder = New-Object System.Text.StringBuilder

    if ($__boundIncludeUndocumented) {
        $null = $stringBuilder.Append("DBCC TRACEON (2588) WITH NO_INFOMSGS;")
    }

    Write-Message -Message "Get Help Information for $Statement" -Level Verbose -FunctionName Get-DbaDbccHelp -ModuleName "dbatools"
    $null = $stringBuilder.Append("DBCC HELP($Statement) WITH NO_INFOMSGS;")

    $stringBuilder
    [pscustomobject]@{ __GetDbaDbccHelpBeginComplete = $true }
} $Statement $EnableException $__boundStatement $__boundIncludeUndocumented @__commonParameters 3>&1 2>&1
""";

    private const string ProcessScript = """
param($StringBuilder, $SqlInstance, $SqlCredential, $Statement, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param($StringBuilder, [Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, $SqlCredential, [string]$Statement, $EnableException)

    if (Test-FunctionInterrupt) { return }

    foreach ($instance in $SqlInstance) {
        try {
            $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential -MinimumVersion 9
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Get-DbaDbccHelp
        }

        try {
            $query = $StringBuilder.ToString()
            Write-Message -Message "Query to run: $query" -Level Verbose -FunctionName Get-DbaDbccHelp -ModuleName "dbatools"
            $results = $server | Invoke-DbaQuery  -Query $query -MessagesToOutput

        } catch {
            Stop-Function -Message "Failure" -ErrorRecord $_ -Target $server -Continue -FunctionName Get-DbaDbccHelp
        }

        [PSCustomObject]@{
            Operation = $Statement
            Cmd       = "DBCC HELP($Statement)"
            Output    = $results
        }

    }
} $StringBuilder $SqlInstance $SqlCredential $Statement $EnableException @__commonParameters 3>&1 2>&1
""";
}
