#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;
using Microsoft.SqlServer.Management.Smo.Mail;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves and decorates Database Mail configuration values. The SqlMail aggregation, SMO
/// traversal, filtering, and output shaping remain a module-scoped PowerShell compatibility hop.
/// Surface pinned by migration/baselines/Get-DbaDbMailConfig.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaDbMailConfig")]
public sealed class GetDbaDbMailConfigCommand : DbaBaseCmdlet
{
    /// <summary>Target SQL Server instances.</summary>
    [Parameter(Position = 0)]
    [PsDbaInstanceArrayCast]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Alternative credential for the target instances.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>Database Mail configuration names to include.</summary>
    [Parameter(Position = 2)]
    [Alias("Config", "ConfigName")]
    public string[]? Name { get; set; }

    /// <summary>SqlMail objects supplied directly or through the pipeline.</summary>
    [Parameter(ValueFromPipeline = true, Position = 3)]
    [PsDbMailArrayCast]
    public SqlMail[]? InputObject { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, BodyScript,
            SqlInstance, SqlCredential, Name, InputObject, EnableException.ToBool(),
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
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

    private const string BodyScript = """
param($SqlInstance, $SqlCredential, $Name, $InputObject, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, $SqlCredential,
        [string[]]$Name, [Microsoft.SqlServer.Management.Smo.Mail.SqlMail[]]$InputObject,
        $EnableException)

    if ($SqlInstance) {
        $InputObject += Get-DbaDbMail -SqlInstance $SqlInstance -SqlCredential $SqlCredential
    }

    if (-not $InputObject) {
        Stop-Function -Message "No servers to process" -FunctionName Get-DbaDbMailConfig
        return
    }

    foreach ($mailserver in $InputObject) {
        try {
            $configs = $mailserver.ConfigurationValues

            if ($Name) {
                $configs = $configs | Where-Object Name -in $Name
            }

            $configs | Add-Member -Force -MemberType NoteProperty -Name ComputerName -value $mailserver.ComputerName
            $configs | Add-Member -Force -MemberType NoteProperty -Name InstanceName -value $mailserver.InstanceName
            $configs | Add-Member -Force -MemberType NoteProperty -Name SqlInstance -value $mailserver.SqlInstance
            $configs | Select-DefaultView -Property ComputerName, InstanceName, SqlInstance, Name, Value, Description
        } catch {
            Stop-Function -Message "Failure" -ErrorRecord $_ -Continue -FunctionName Get-DbaDbMailConfig
        }
    }
} $SqlInstance $SqlCredential $Name $InputObject $EnableException @__commonParameters 3>&1 2>&1
""";
}
