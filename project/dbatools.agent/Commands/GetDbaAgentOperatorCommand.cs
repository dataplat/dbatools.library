#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves and decorates SQL Agent operators. The SMO traversal, relationship discovery,
/// process-scope state, and output shaping remain a module-scoped PowerShell compatibility hop.
/// Surface pinned by migration/baselines/Get-DbaAgentOperator.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaAgentOperator")]
public sealed class GetDbaAgentOperatorCommand : DbaBaseCmdlet
{
    /// <summary>Target SQL Server instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    [PsDbaInstanceArrayCast]
    public DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Alternative credential for the target instances.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>Operator names to include.</summary>
    [Parameter(Position = 2)]
    public object[]? Operator { get; set; }

    /// <summary>Operator names to exclude.</summary>
    [Parameter(Position = 3)]
    public object[]? ExcludeOperator { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    private object? _server;
    private object? _alertLastEmail;

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        foreach (DbaInstanceParameter instance in SqlInstance)
        {
            if (Interrupted)
                return;

            foreach (PSObject? item in NestedCommand.InvokeScoped(this, BodyScript,
                new[] { instance }, SqlCredential, Operator, ExcludeOperator,
                EnableException.ToBool(), _server, _alertLastEmail,
                BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
            {
                if (item?.BaseObject is ErrorRecord nestedError)
                {
                    RemoveHopErrorBookkeeping(nestedError);
                    WriteError(nestedError);
                }
                else if (item is not null && LanguagePrimitives.IsTrue(
                    item.Properties["__GetDbaAgentOperatorProcessComplete"]?.Value))
                {
                    _server = UnwrapHopValue(item.Properties["Server"]?.Value);
                    _alertLastEmail = UnwrapHopValue(item.Properties["AlertLastEmail"]?.Value);
                }
                else
                {
                    WriteObject(item);
                }
            }
        }
    }

    // Carried hop state arrives PSObject-wrapped. A PSCustomObject carries its content on the
    // wrapper rather than the BaseObject, so unwrapping one would discard it - keep it wrapped.
    private static object? UnwrapHopValue(object? value)
    {
        if (value is null || ReferenceEquals(value, System.Management.Automation.Internal.AutomationNull.Value))
            return null;
        if (value is not PSObject wrapper)
            return value;
        return wrapper.BaseObject is PSCustomObject ? wrapper : wrapper.BaseObject;
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

    private const string BodyScript = """
param($SqlInstance, $SqlCredential, $Operator, $ExcludeOperator, $EnableException, $Server, $AlertLastEmail, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, $SqlCredential,
        [object[]]$Operator, [object[]]$ExcludeOperator, $EnableException, $Server, $AlertLastEmail)

    $server = $Server
    $alertlastemail = $AlertLastEmail
    foreach ($instance in $SqlInstance) {
        try {
            $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Get-DbaAgentOperator
        }

        Write-Message -Level Verbose -Message "Getting Edition from $server" -FunctionName Get-DbaAgentOperator -ModuleName "dbatools"
        Write-Message -Level Verbose -Message "$server is a $($server.Edition)" -FunctionName Get-DbaAgentOperator -ModuleName "dbatools"

        if ($server.Edition -like 'Express*') {
            Stop-Function -Message "There is no SQL Agent on $server, it's a $($server.Edition)" -Continue -Target $server -FunctionName Get-DbaAgentOperator
        }

        $defaults = "ComputerName", "InstanceName", "SqlInstance", "Name", "ID", "Enabled as IsEnabled", "EmailAddress", "LastEmail"

        if ($Operator) {
            $operators = $server.JobServer.Operators | Where-Object Name -In $Operator
        } elseif ($ExcludeOperator) {
            $operators = $server.JobServer.Operators | Where-Object Name -NotIn $ExcludeOperator
        } else {
            $operators = $server.JobServer.Operators
        }

        $alerts = $server.JobServer.alerts

        foreach ($operat in $operators) {
            $jobs = $server.JobServer.jobs | Where-Object { $_.OperatorToEmail, $_.OperatorToNetSend, $_.OperatorToPage -contains $operat.Name }
            $lastemail = [dbadatetime]$operat.LastEmailDate

            $operatAlerts = @()
            foreach ($alert in $alerts) {
                $dtAlert = $alert.EnumNotifications($operat.Name)
                if ($dtAlert.Rows.Count -gt 0) {
                    $operatAlerts += $alert.Name
                    $alertlastemail = [dbadatetime]$alert.LastOccurrenceDate
                }
            }

            Add-Member -Force -InputObject $operat -MemberType NoteProperty -Name ComputerName -Value $server.ComputerName
            Add-Member -Force -InputObject $operat -MemberType NoteProperty -Name InstanceName -Value $server.ServiceName
            Add-Member -Force -InputObject $operat -MemberType NoteProperty -Name SqlInstance -Value $server.DomainInstanceName
            Add-Member -Force -InputObject $operat -MemberType NoteProperty -Name RelatedJobs -Value $jobs
            Add-Member -Force -InputObject $operat -MemberType NoteProperty -Name LastEmail -Value $lastemail
            Add-Member -Force -InputObject $operat -MemberType NoteProperty -Name RelatedAlerts -Value $operatAlerts
            Add-Member -Force -InputObject $operat -MemberType NoteProperty -Name AlertLastEmail -Value $alertlastemail
            Select-DefaultView -InputObject $operat -Property $defaults
        }
    }

    [pscustomobject]@{
        __GetDbaAgentOperatorProcessComplete = $true
        Server = $server
        AlertLastEmail = $alertlastemail
    }
} $SqlInstance $SqlCredential $Operator $ExcludeOperator $EnableException $Server $AlertLastEmail @__commonParameters 3>&1 2>&1
""";
}
