#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves extended property objects and metadata from SQL Server objects. Port of
/// public/Get-DbaExtendedProperty.ps1; the workflow remains a module-scoped PowerShell compatibility hop.
///
/// A process-only port; InputObject (psobject[]) is the only ValueFromPipeline parameter (SqlInstance is NOT VFP).
/// The if ($SqlInstance) TRUTHINESS check (NOT Test-Bound) gates the Get-DbaDatabase gather into the LOCAL
/// $InputObject; $InputObject is not a parameter, assigned only when SqlInstance is truthy (when absent the foreach
/// is empty). The one continue (source 121, no-extended-properties skip) is inside foreach ($object) - loop-bound.
/// The Name filter is truthiness-based. No Test-Bound, no Stop-Function, no accumulator, no interrupt, no value-passed
/// switch, no ShouldProcess. The only edit is -FunctionName Get-DbaExtendedProperty on the one Write-Message. Source
/// quirks preserved verbatim: the no-props message references an undefined $instance -> empty; and $server (set only
/// inside the connection-rebuild block) may be $null in the Add-Member Server decoration when all three name
/// properties are already present. Surface pinned by migration/baselines/Get-DbaExtendedProperty.json
/// (positions 0-4, Name Property alias, InputObject VFP pos4, no ShouldProcess).
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaExtendedProperty")]
public sealed class GetDbaExtendedPropertyCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The database(s) to gather extended properties from (when -SqlInstance is used).</summary>
    [Parameter(Position = 2)]
    public string[]? Database { get; set; }

    /// <summary>Filter to the specified extended property/properties by name.</summary>
    [Parameter(Position = 3)]
    [Alias("Property")]
    public string[]? Name { get; set; }

    /// <summary>Object(s) piped in whose extended properties are returned.</summary>
    [Parameter(ValueFromPipeline = true, Position = 4)]
    public PSObject[]? InputObject { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, ProcessScript,
            SqlInstance, SqlCredential, Database, Name, InputObject, EnableException.ToBool(),
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
        {
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                RemoveHopErrorBookkeeping(nestedError);
                WriteError(nestedError);
                continue;
            }
            WriteObject(item);
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
    // PS: the process block VERBATIM. Edit: -FunctionName Get-DbaExtendedProperty on the one Write-Message. The
    // if ($SqlInstance) check is truthiness (no Test-Bound); the one continue is inside foreach ($object) - loop-bound.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Database, $Name, $InputObject, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [string[]]$Database, [string[]]$Name, [psobject[]]$InputObject, $EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

        if ($SqlInstance) {
            $InputObject = Get-DbaDatabase -SqlInstance $SqlInstance -SqlCredential $SqlCredential -Database $Database | Where-Object IsAccessible
        }

        foreach ($object in $InputObject) {
            $props = $object.ExtendedProperties

            if ($null -eq $props) {
                Write-Message -Message "No extended properties exist in the $object on $instance" -Target $object -Level Verbose -FunctionName Get-DbaExtendedProperty -ModuleName "dbatools"
                continue
            }

            if ($Name) {
                $props = $props | Where-Object Name -in $Name
            }

            # Since the inputobject is so generic, we need to re-build these properties
            $computername = $object.ComputerName
            $instancename = $object.InstanceName
            $sqlname = $object.SqlInstance

            if (-not $computername -or -not $instancename -or -not $sqlname) {
                $server = Get-ConnectionParent $object
                $servername = $server.Query("SELECT @@SERVERNAME AS servername").servername

                if (-not $computername) {
                    $computername = ([DbaInstanceParameter]$servername).ComputerName
                }

                if (-not $instancename) {
                    $instancename = ([DbaInstanceParameter]$servername).InstanceName
                }

                if (-not $sqlname) {
                    $sqlname = $servername
                }
            }

            foreach ($prop in $props) {
                Add-Member -Force -InputObject $prop -MemberType NoteProperty -Name ComputerName -Value $computername
                Add-Member -Force -InputObject $prop -MemberType NoteProperty -Name InstanceName -Value $instancename
                Add-Member -Force -InputObject $prop -MemberType NoteProperty -Name SqlInstance -Value $sqlname
                Add-Member -Force -InputObject $prop -MemberType NoteProperty -Name ParentName -Value $object.Name
                Add-Member -Force -InputObject $prop -MemberType NoteProperty -Name Type -Value $object.GetType().Name
                Add-Member -Force -InputObject $prop -MemberType NoteProperty -Name Server -Value $server


                Select-DefaultView -InputObject $prop -Property ComputerName, InstanceName, SqlInstance, ParentName, Type, Name, Value
            }
        }
} $SqlInstance $SqlCredential $Database $Name $InputObject $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
