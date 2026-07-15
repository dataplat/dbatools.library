#nullable enable

using System;
using System.Collections;
using System.Globalization;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves SQL Agent error-log entries. The SMO read, stream, and output-decoration workflow
/// remains a module-scoped PowerShell compatibility hop. Surface pinned by
/// migration/baselines/Get-DbaAgentLog.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaAgentLog")]
public sealed class GetDbaAgentLogCommand : DbaBaseCmdlet
{
    /// <summary>Target SQL Server instances.</summary>
    [Parameter(ValueFromPipeline = true, Position = 0)]
    [PsAgentLogDbaInstanceArrayCast]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Alternative credential for the target instances.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>SQL Agent error-log numbers to read.</summary>
    [Parameter(Position = 2)]
    [PsAgentLogIntArrayCast]
    [ValidateRange(0, 9)]
    public int[]? LogNumber { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, BodyScript,
            SqlInstance, SqlCredential, LogNumber, EnableException.ToBool(),
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
param($SqlInstance, $SqlCredential, $LogNumber, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, $SqlCredential,
        [int[]]$LogNumber, $EnableException)
    foreach ($instance in $SqlInstance) {
        try {
            $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Get-DbaAgentLog
        }

        if ($LogNumber) {
            foreach ($number in $LogNumber) {
                try {
                    foreach ($object in $server.JobServer.ReadErrorLog($number)) {
                        Write-Message -Level Verbose -Message "Processing $object" -FunctionName Get-DbaAgentLog
                        Add-Member -Force -InputObject $object -MemberType NoteProperty ComputerName -value $server.ComputerName
                        Add-Member -Force -InputObject $object -MemberType NoteProperty InstanceName -value $server.ServiceName
                        Add-Member -Force -InputObject $object -MemberType NoteProperty SqlInstance -value $server.DomainInstanceName
                        Select-DefaultView -InputObject $object -Property ComputerName, InstanceName, SqlInstance, LogDate, ProcessInfo, Text
                    }
                } catch {
                    Stop-Function -Continue -Target $server -Message "Could not read from SQL Server Agent" -FunctionName Get-DbaAgentLog
                }
            }
        } else {
            try {
                foreach ($object in $server.JobServer.ReadErrorLog()) {
                    Write-Message -Level Verbose -Message "Processing $object" -FunctionName Get-DbaAgentLog
                    Add-Member -Force -InputObject $object -MemberType NoteProperty ComputerName -value $server.ComputerName
                    Add-Member -Force -InputObject $object -MemberType NoteProperty InstanceName -value $server.ServiceName
                    Add-Member -Force -InputObject $object -MemberType NoteProperty SqlInstance -value $server.DomainInstanceName
                    Select-DefaultView -InputObject $object -Property ComputerName, InstanceName, SqlInstance, LogDate, ProcessInfo, Text
                }
            } catch {
                Stop-Function -Continue -Target $server -Message "Could not read from SQL Server Agent" -FunctionName Get-DbaAgentLog
            }
        }
    }
} $SqlInstance $SqlCredential $LogNumber $EnableException @__commonParameters 3>&1 2>&1
""";
}

/// <summary>Reproduces the advanced function's typed DbaInstanceParameter array conversion.</summary>
internal sealed class PsAgentLogDbaInstanceArrayCastAttribute : ArgumentTransformationAttribute
{
    public override object? Transform(EngineIntrinsics engineIntrinsics, object? inputData)
    {
        if (inputData is null)
            return null;
        try
        {
            return LanguagePrimitives.ConvertTo(inputData, typeof(DbaInstanceParameter[]), CultureInfo.InvariantCulture);
        }
        catch (PSInvalidCastException ex)
        {
            throw new ArgumentTransformationMetadataException(ex.Message, ex);
        }
    }
}

/// <summary>Reproduces the advanced function's typed Int32 array conversion before validation.</summary>
internal sealed class PsAgentLogIntArrayCastAttribute : ArgumentTransformationAttribute
{
    public override object? Transform(EngineIntrinsics engineIntrinsics, object? inputData)
    {
        if (inputData is null)
            return null;
        try
        {
            return LanguagePrimitives.ConvertTo(inputData, typeof(int[]), CultureInfo.InvariantCulture);
        }
        catch (PSInvalidCastException ex)
        {
            throw new ArgumentTransformationMetadataException(ex.Message, ex);
        }
    }
}
