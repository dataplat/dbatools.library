#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves SQL Server Agent alert categories and the number of alerts assigned to each.
/// </summary>
/// <remarks>
/// The retrieval workflow runs the original dbatools PowerShell body inside the dbatools module
/// scope instead of being reimplemented in C#, so the PowerShell engine keeps deciding the
/// observable details: the case-insensitive -in match behind -Category, SMO object identity and
/// enumeration order, the four forced NoteProperties, and the default display set.
///
/// The category collection is retrieved and filtered BEFORE the try block that decorates and
/// emits it. That placement is deliberate and load-bearing: a failure raised while populating
/// the collection stays uncaught and surfaces as a raw SMO error, whereas a failure raised while
/// counting or decorating an individual category is caught and reported per category. Moving the
/// retrieval inside the try would silently reclassify the first kind of failure as the second.
///
/// Output streams as it is produced, so warnings, verbose and debug records interleave with the
/// emitted categories in the order the script implementation produced them.
///
/// The script implementation's scope spans a whole pipeline, so the category loop variable it
/// names in a retrieval failure survives from one input record to the next. A module-scoped hop
/// gets a fresh scope per record, so that variable is carried across records explicitly.
/// </remarks>
[Cmdlet(VerbsCommon.Get, "DbaAgentAlertCategory")]
public sealed class GetDbaAgentAlertCategoryCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>One or more exact alert category names to return. Wildcards are not supported.</summary>
    [Parameter(Position = 2)]
    public string[]? Category { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // The category the retrieval loop last reached, carried between pipeline records because the
    // hop's scope does not outlive one record. Starts null, matching an untouched local.
    private object? _cat;

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
            else if (item is not null && LanguagePrimitives.IsTrue(
                item.Properties["__GetDbaAgentAlertCategoryProcessComplete"]?.Value))
            {
                _cat = UnwrapHopValue(item.Properties["Cat"]?.Value);
            }
            else
            {
                WriteObject(item);
            }
        }, BodyScript,
            SqlInstance, SqlCredential, Category, EnableException.ToBool(),
            TestBound(nameof(Category)), _cat, BoundCommonParameter("Verbose"), BoundCommonParameter("Debug"));
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
param($SqlInstance, $SqlCredential, $Category, $EnableException, $__boundCategory, $Cat, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, $SqlCredential, [string[]]$Category, $EnableException, $__boundCategory, $Cat)

    $cat = $Cat
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

    [pscustomobject]@{
        __GetDbaAgentAlertCategoryProcessComplete = $true
        Cat = $cat
    }
} $SqlInstance $SqlCredential $Category $EnableException $__boundCategory $Cat @__commonParameters 3>&1 2>&1
""";
}
