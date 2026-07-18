#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Creates a new (empty) Extended Events session on one or more SQL Server instances.
/// </summary>
/// <remarks>
/// The connection, the XEStore construction, and the CreateSession call all run the original dbatools
/// PowerShell body VERBATIM inside the dbatools module scope rather than being reimplemented in C#, so the
/// engine decides the observable details.
///
/// The function is process-only. The sole Stop-Function (connect failure) is -Continue, so there is no
/// begin/end, no interrupt, and no Interrupted guard. There is one ShouldProcess site and NO -Force, so it
/// routes through $__realCmdlet (this cmdlet supplies the real ShouldProcess runtime; ConfirmImpact Medium,
/// the CmdletBinding default). WhatIf/Confirm are forwarded to the nested scope so the inner
/// [CmdletBinding(SupportsShouldProcess)] accepts them. Each instance's CreateSession result is emitted
/// before a later instance may fail under -EnableException (DEF-001), so the process hop uses
/// InvokeScopedStreaming. Surface pinned by migration/baselines/New-DbaXESession.json.
/// </remarks>
[Cmdlet(VerbsCommon.New, "DbaXESession", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Medium)]
public sealed class NewDbaXESessionCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    [PsDbaInstanceArrayCast]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The name for the new Extended Events session.</summary>
    [Parameter(Mandatory = true, Position = 2)]
    public string? Name { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - the source declares it bare (every set), which the
    // inherited [Parameter] (no ParameterSetName) already matches; no override needed.

    protected override void ProcessRecord()
    {
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
            SqlInstance, SqlCredential, Name, EnableException.ToBool(), this,
            BoundCommonParameter("WhatIf"), BoundCommonParameter("Confirm"),
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

    // PS: the process block VERBATIM apart from $Pscmdlet.ShouldProcess -> $__realCmdlet.ShouldProcess and
    // -FunctionName New-DbaXESession on the direct Stop-Function.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Name, $EnableException, $__realCmdlet, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundWhatIf) { $__commonParameters.WhatIf = [bool]$__boundWhatIf }
if ($null -ne $__boundConfirm) { $__commonParameters.Confirm = [bool]$__boundConfirm }
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding(SupportsShouldProcess)]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, $SqlCredential, [string]$Name, $EnableException, $__realCmdlet)
    foreach ($instance in $SqlInstance) {
        try {
            $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential -MinimumVersion 11
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName New-DbaXESession
        }

        if ($__realCmdlet.ShouldProcess($instance, "Creating new XESession")) {
            $SqlConn = $server.ConnectionContext.SqlConnectionObject
            $SqlStoreConnection = New-Object Microsoft.SqlServer.Management.Sdk.Sfc.SqlStoreConnection $SqlConn
            $store = New-Object  Microsoft.SqlServer.Management.XEvent.XEStore $SqlStoreConnection

            $store.CreateSession($Name)
        }
    }
} $SqlInstance $SqlCredential $Name $EnableException $__realCmdlet @__commonParameters 3>&1 2>&1
""";
}
