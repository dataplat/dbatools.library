#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves SQL Agent job categories with usage counts and filtering options.
/// Port of public/Get-DbaAgentJobCategory.ps1; surface pinned by
/// migration/baselines/Get-DbaAgentJobCategory.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaAgentJobCategory", DefaultParameterSetName = "Default")]
public sealed class GetDbaAgentJobCategoryCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>One or more exact job category names to return.</summary>
    [Parameter(Position = 2)]
    [ValidateNotNullOrEmpty]
    public string[]? Category { get; set; }

    /// <summary>Filters job categories by deployment type.</summary>
    [Parameter(Position = 3)]
    [ValidateSet("LocalJob", "MultiServerJob", "None")]
    public string? CategoryType { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        if (Interrupted)
        {
            return;
        }

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
        }, BodyScript,
            SqlInstance, SqlCredential, Category, CategoryType, EnableException.ToBool(),
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug"));
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
param($SqlInstance, $SqlCredential, $Category, $CategoryType, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding(DefaultParameterSetName = "Default")]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, $SqlCredential, [string[]]$Category, [string]$CategoryType, $EnableException)

    foreach ($instance in $SqlInstance) {
        try {
            $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Get-DbaAgentJobCategory
        }

        $jobCategories = $server.JobServer.JobCategories |
            Where-Object {
                ($_.Name -in $Category -or !$Category) -and
                ($_.CategoryType -in $CategoryType -or !$CategoryType)
            }

        $defaults = 'ComputerName', 'InstanceName', 'SqlInstance', 'Name', 'ID', 'CategoryType', 'JobCount'

        try {
            foreach ($cat in $jobCategories) {
                $jobCount = ($server.JobServer.Jobs | Where-Object { $_.CategoryID -eq $cat.ID }).Count

                Add-Member -Force -InputObject $cat -MemberType NoteProperty -Name ComputerName -value $server.ComputerName
                Add-Member -Force -InputObject $cat -MemberType NoteProperty -Name InstanceName -value $server.ServiceName
                Add-Member -Force -InputObject $cat -MemberType NoteProperty -Name SqlInstance -value $server.DomainInstanceName
                Add-Member -Force -InputObject $cat -MemberType NoteProperty -Name JobCount -Value $jobCount

                Select-DefaultView -InputObject $cat -Property $defaults
            }
        } catch {
            Stop-Function -Message "Something went wrong getting the job category $cat on $instance" -Target $cat -Continue -ErrorRecord $_ -FunctionName Get-DbaAgentJobCategory
        }
    }
} $SqlInstance $SqlCredential $Category $CategoryType $EnableException @__commonParameters 3>&1 2>&1
""";
}
