#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves SQL Agent alert categories and their associated alert counts.
/// Port of public/Get-DbaAgentAlertCategory.ps1; surface pinned by
/// migration/baselines/Get-DbaAgentAlertCategory.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaAgentAlertCategory")]
public sealed class GetDbaAgentAlertCategoryCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>One or more exact alert category names to return.</summary>
    [Parameter(Position = 2)]
    public string[]? Category { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        if (Interrupted)
        {
            return;
        }

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, BodyScript,
            SqlInstance, SqlCredential, Category, EnableException.ToBool(),
            TestBound(nameof(Category)), BoundCommonParameter("Verbose"),
            BoundCommonParameter("Debug")))
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
        {
            return LanguagePrimitives.IsTrue(value);
        }
        return null;
    }

    private void RemoveHopErrorBookkeeping(ErrorRecord record)
    {
        try
        {
            if (SessionState.PSVariable.GetValue("Error") is not ArrayList errorList || errorList.Count == 0)
            {
                return;
            }
            if (errorList[0] is not ErrorRecord first)
            {
                return;
            }
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
param($SqlInstance, $SqlCredential, $Category, $EnableException, $__boundCategory, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, $SqlCredential, [string[]]$Category, $EnableException, $__boundCategory)

    foreach ($instance in $SqlInstance) {
        try {
            $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Get-DbaAgentAlertCategory
        }

        $alertCategories = $server.JobServer.AlertCategories
        if ($__boundCategory) {
            $alertCategories = $alertCategories | Where-Object { $_.Name -in $Category }
        }

        $defaults = 'ComputerName', 'InstanceName', 'SqlInstance', 'Name', 'ID', 'AlertCount'

        try {
            foreach ($cat in $alertCategories) {
                $alertCount = ($server.JobServer.Alerts | Where-Object { $_.CategoryName -eq $cat.Name }).Count

                Add-Member -Force -InputObject $cat -MemberType NoteProperty -Name ComputerName -value $server.ComputerName
                Add-Member -Force -InputObject $cat -MemberType NoteProperty -Name InstanceName -value $server.ServiceName
                Add-Member -Force -InputObject $cat -MemberType NoteProperty -Name SqlInstance -value $server.DomainInstanceName
                Add-Member -Force -InputObject $cat -MemberType NoteProperty -Name AlertCount -Value $alertCount

                Select-DefaultView -InputObject $cat -Property $defaults
            }
        } catch {
            Stop-Function -Message "Something went wrong getting the alert category $cat on $instance" -Target $cat -Continue -ErrorRecord $_ -FunctionName Get-DbaAgentAlertCategory
        }
    }
} $SqlInstance $SqlCredential $Category $EnableException $__boundCategory @__commonParameters 3>&1 2>&1
""";
}
