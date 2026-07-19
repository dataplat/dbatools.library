#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves and decorates SQL Agent proxy accounts. Wildcard filtering, process-scope server
/// state, and SMO output shaping remain a module-scoped PowerShell compatibility hop. Surface
/// pinned by migration/baselines/Get-DbaAgentProxy.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaAgentProxy")]
public sealed class GetDbaAgentProxyCommand : DbaBaseCmdlet
{
    /// <summary>Target SQL Server instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    [PsDbaInstanceArrayCast]
    public DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Alternative credential for the target instances.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>Proxy-name filters, including wildcard patterns.</summary>
    [Parameter(Position = 2)]
    public string[]? Proxy { get; set; }

    /// <summary>Proxy-name exclusion patterns.</summary>
    [Parameter(Position = 3)]
    public string[]? ExcludeProxy { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    private object? _server;

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        foreach (DbaInstanceParameter instance in SqlInstance)
        {
            if (Interrupted)
                return;

            foreach (PSObject? item in NestedCommand.InvokeScoped(this, BodyScript,
                new[] { instance }, SqlCredential, Proxy, ExcludeProxy, EnableException.ToBool(),
                TestBound(nameof(Proxy)), TestBound(nameof(ExcludeProxy)), _server,
                BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
            {
                if (item?.BaseObject is ErrorRecord nestedError)
                {
                    RemoveHopErrorBookkeeping(nestedError);
                    WriteError(nestedError);
                }
                else if (item is not null && LanguagePrimitives.IsTrue(
                    item.Properties["__GetDbaAgentProxyProcessComplete"]?.Value))
                {
                    _server = UnwrapHopValue(item.Properties["Server"]?.Value);
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
param($SqlInstance, $SqlCredential, $Proxy, $ExcludeProxy, $EnableException, $__boundProxy, $__boundExcludeProxy, $Server, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, $SqlCredential,
        [string[]]$Proxy, [string[]]$ExcludeProxy, $EnableException,
        $__boundProxy, $__boundExcludeProxy, $Server)

    $server = $Server
    foreach ($instance in $SqlInstance) {
        try {
            $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Get-DbaAgentProxy
        }

        Write-Message -Level Verbose -Message "Getting Edition from $server" -FunctionName Get-DbaAgentProxy -ModuleName "dbatools"
        Write-Message -Level Verbose -Message "$server is a $($server.Edition)" -FunctionName Get-DbaAgentProxy -ModuleName "dbatools"

        if ($server.Edition -like 'Express*') {
            Stop-Function -Message "There is no SQL Agent on $server, it's a $($server.Edition)" -Continue -FunctionName Get-DbaAgentProxy
        }

        $defaults = "ComputerName", "SqlInstance", "InstanceName", "Name", "ID", "CredentialID", "CredentialIdentity", "CredentialName", "Description", "IsEnabled"
        $proxies = $server.Jobserver.ProxyAccounts

        if ($__boundProxy) {
            $tempProxies = @()
            foreach ($a in $Proxy) {
                $tempProxies += $proxies | Where-Object Name -like $a
            }
            $proxies = $tempProxies
        }

        if ($__boundExcludeProxy) {
            foreach ($e in $ExcludeProxy) {
                $proxies = $proxies | Where-Object Name -notlike $e
            }
        }

        foreach ($px in $proxies) {
            Add-Member -Force -InputObject $px -MemberType NoteProperty -Name ComputerName -value $server.ComputerName
            Add-Member -Force -InputObject $px -MemberType NoteProperty -Name InstanceName -value $server.ServiceName
            Add-Member -Force -InputObject $px -MemberType NoteProperty -Name SqlInstance -value $server.DomainInstanceName
            Select-DefaultView -InputObject $px -Property $defaults
        }
    }

    [pscustomobject]@{
        __GetDbaAgentProxyProcessComplete = $true
        Server = $server
    }
} $SqlInstance $SqlCredential $Proxy $ExcludeProxy $EnableException $__boundProxy $__boundExcludeProxy $Server @__commonParameters 3>&1 2>&1
""";
}
