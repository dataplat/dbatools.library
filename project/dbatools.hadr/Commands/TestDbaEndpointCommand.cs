#nullable enable

using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Tests connectivity to SQL Server endpoints over their TCP listener ports.
/// Port of public/Test-DbaEndpoint.ps1; surface pinned by
/// migration/baselines/Test-DbaEndpoint.json.
/// </summary>
[Cmdlet(VerbsDiagnostic.Test, "DbaEndpoint")]
public sealed class TestDbaEndpointCommand : DbaBaseCmdlet
{
    /// <summary>The SQL Server instance or instances hosting the endpoint.</summary>
    [Parameter(Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Login to the target instances using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The endpoint or endpoints to test.</summary>
    [Parameter(Position = 2)]
    [PsStringArrayCast]
    public string[]? Endpoint { get; set; }

    /// <summary>Endpoint objects piped from Get-DbaEndpoint.</summary>
    [Parameter(ValueFromPipeline = true, Position = 3)]
    public Microsoft.SqlServer.Management.Smo.Endpoint[]? InputObject { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        if (Interrupted)
        {
            return;
        }

        // WHOLE-RECORD hop, the simplest kind: read-only ([CmdletBinding()], NO ShouldProcess),
        // NO Test-Bound (no bound flags), NO cross-record state. The only process-block parameter
        // mutation is `$InputObject +=` at :102, targeting the ValueFromPipeline parameter, which
        // the binder RE-BINDS every record. Both detectors clean; and per the W4-067 DEF-012
        // lesson I checked the conditionally-assigned locals: $connect/$sslconnect are assigned on
        // EVERY try/catch path before their read at the output object, and $tcp/$ssl are safe not
        // by lexical scope (they are process-scoped like the rest) but because every reachable read
        // is assignment-dominated - a New-Object/assignment precedes it or the failure transfers to
        // the catch (codex precision) - so no cross-record leak to carry. $EnableException is passed
        // for consistency though the body has no Stop-Function that reads it.
        //
        // T8/DEF-002 on Endpoint [string[]] CLOSED via [PsStringArrayCast]. DEF-001 (weak here -
        // read-only, TcpClient failures caught locally) closed via InvokeScopedStreaming (ab7492c);
        // read-only so no WhatIf interaction.
        NestedCommand.InvokeScopedStreaming(this, item =>
        {
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                RemoveHopErrorBookkeeping(nestedError);
                WriteError(nestedError);
                return;
            }
            WriteObject(item);
        }, ProcessScript,
            SqlInstance, SqlCredential, Endpoint, InputObject,
            EnableException.ToBool(),
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
                return;
            if (errorList[0] is not ErrorRecord first)
                return;
            if (ReferenceEquals(first, record) || ReferenceEquals(first.Exception, record.Exception) ||
                string.Equals(first.Exception?.Message, record.Exception?.Message, System.StringComparison.Ordinal))
            {
                errorList.RemoveAt(0);
            }
        }
        catch
        {
            // Best-effort bookkeeping only.
        }
    }

    // PS: the source process block VERBATIM, byte-proven against source lines 101-146 (extracted
    // programmatically) after reversing the 2 direct-Write-Message DEF-006 rewrites (each carries a
    // # SOURCE: marker; -FunctionName + -ModuleName "dbatools"). No Test-Bound, no Stop-Function
    // (asserted at generation). No gate, no sentinel.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Endpoint, $InputObject, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [string[]]$Endpoint, [Microsoft.SqlServer.Management.Smo.Endpoint[]]$InputObject, $EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    . {
        foreach ($instance in $SqlInstance) {
            $InputObject += Get-DbaEndpoint -SqlInstance $instance -SqlCredential $SqlCredential -Endpoint $Endpoint
        }

        foreach ($end in $InputObject) {
            if (-not $end.Protocol.Tcp.ListenerPort) {
                Write-Message -Level Verbose -Message "$end on $($end.Parent) does not have a tcp listener port" -FunctionName Test-DbaEndpoint -ModuleName "dbatools" # SOURCE: Write-Message -Level Verbose -Message "$end on $($end.Parent) does not have a tcp listener port"
            } else {
                Write-Message "Connecting to port $($end.Protocol.Tcp.ListenerPort) on $($end.ComputerName) for endpoint $($end.Name)" -FunctionName Test-DbaEndpoint -ModuleName "dbatools" # SOURCE: Write-Message "Connecting to port $($end.Protocol.Tcp.ListenerPort) on $($end.ComputerName) for endpoint $($end.Name)"

                try {
                    $tcp = New-Object System.Net.Sockets.TcpClient
                    $tcp.Connect($end.ComputerName, $end.Protocol.Tcp.ListenerPort)
                    $tcp.Close()
                    $tcp.Dispose()
                    $connect = "Success"
                } catch {
                    $connect = $_
                }

                try {
                    $ssl = $end.Protocol.Tcp.SslPort
                    if ($ssl) {
                        $tcp = New-Object System.Net.Sockets.TcpClient
                        $tcp.Connect($end.ComputerName, $ssl)
                        $tcp.Close()
                        $tcp.Dispose()
                        $sslconnect = "Success"
                    } else {
                        $sslconnect = "None"
                    }
                } catch {
                    $sslconnect = $_
                }

                [PSCustomObject]@{
                    ComputerName  = $end.ComputerName
                    InstanceName  = $end.InstanceName
                    SqlInstance   = $end.SqlInstance
                    Endpoint      = $end.Name
                    Port          = $end.Protocol.Tcp.ListenerPort
                    Connection    = $connect
                    SslConnection = $sslconnect
                }
            }
        }
    }
} $SqlInstance $SqlCredential $Endpoint $InputObject $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}