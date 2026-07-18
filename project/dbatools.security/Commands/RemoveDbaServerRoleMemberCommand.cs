#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Revokes server-level role membership from SQL Server logins and roles.
/// </summary>
/// <remarks>
/// <para>
/// The workflow remains a module-scoped PowerShell compatibility hop so that the role-member drops, the
/// ShouldProcess gate, the input-type dispatch, and dbatools stream and error handling stay observable-identical
/// to the script implementation.
/// </para>
/// <para>
/// The script keeps state across its begin/process blocks in one function scope, and it has TWO ValueFromPipeline
/// parameters (SqlInstance and InputObject) plus a begin state-copy ($InputObject = $SqlInstance when SqlInstance
/// is bound by name). A per-record hop would drop that, so the port is COLLECT-THEN-ENDPROCESSING in one scope
/// (like W2-208 Export-DbaUser). BeginProcessing captures the by-name flags (ContainsKey before any pipeline
/// record, so a piped SqlInstance reads false). ProcessRecord collects one batch per record, flagging a genuine
/// InputObject rebind by reference change. EndProcessing runs ONE hop: the begin block (its Test-Bound checks
/// mapped to the carried by-name flags; its early Stop-Function then return skips all work; then, in the by-name
/// case, $InputObject = $SqlInstance), then a per-batch DOT-SOURCED replay of the process body. The replay
/// overrides $InputObject only when the batch rebound it (the piped case); otherwise the begin's $InputObject
/// (= $SqlInstance) stands (the by-name case), matching the script's single scope. The dot-source keeps each
/// record's return/continue local to that batch.
/// </para>
/// <para>
/// The command mutates (DropMember/DropMembershipFromRole) and emits nothing. $Pscmdlet is redirected to
/// $__realCmdlet for the two ShouldProcess gates. EnableException is carried as a plain (untyped) value, because
/// a switch in the inner CmdletBinding scriptblock is excluded from positional binding. All five Test-Bound
/// checks (SqlInstance/ServerRole/Login in begin, ServerRole in process) map to the carried by-name flags. Every
/// DIRECT Stop-Function/Write-Message call takes -FunctionName; Get-DbaServerRole, Connect-DbaInstance, and the
/// SMO DropMember/DropMembershipFromRole methods are left unedited.
/// </para>
/// </remarks>
[Cmdlet(VerbsCommon.Remove, "DbaServerRoleMember", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.High)]
public sealed class RemoveDbaServerRoleMemberCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Alternative credential for the target instances.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The server-level roles to remove members from.</summary>
    [Parameter(Position = 2)]
    public string[]? ServerRole { get; set; }

    /// <summary>The logins to remove from the server roles.</summary>
    [Parameter(Position = 3)]
    public string[]? Login { get; set; }

    /// <summary>The nested roles to remove from the server roles.</summary>
    [Parameter(Position = 4)]
    public string[]? Role { get; set; }

    /// <summary>Server or ServerRole objects for pipeline operations.</summary>
    [Parameter(ValueFromPipeline = true, Position = 5)]
    public object[]? InputObject { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    /// <summary>One batch per pipeline record: { inputRebound, InputObject }.</summary>
    private readonly List<object?[]?> _batches = new List<object?[]?>();

    /// <summary>The InputObject seen at the previous ProcessRecord, to detect a genuine pipeline rebind.</summary>
    private object? _prevInputObject;

    /// <summary>The by-name InputObject value snapshotted at begin (the hop's initial $InputObject).</summary>
    private object? _byNameInputObject;

    private bool _sqlInstanceByName;
    private bool _serverRoleByName;
    private bool _loginByName;

    /// <summary>Captures the by-name binding of the parameters before any pipeline record arrives.</summary>
    protected override void BeginProcessing()
    {
        _sqlInstanceByName = MyInvocation.BoundParameters.ContainsKey("SqlInstance");
        _serverRoleByName = MyInvocation.BoundParameters.ContainsKey("ServerRole");
        _loginByName = MyInvocation.BoundParameters.ContainsKey("Login");
        // Snapshot the by-name InputObject (null unless -InputObject was passed by name). It is both the hop's
        // initial $InputObject and the rebind baseline, so the FIRST record's by-name value is NOT mistaken for
        // a pipeline rebind (which would defeat the begin block's $InputObject = $SqlInstance state-copy).
        _byNameInputObject = InputObject;
        _prevInputObject = InputObject;
    }

    /// <summary>Records each pipeline record's input as a batch; the work runs once in EndProcessing.</summary>
    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        bool inputRebound = !ReferenceEquals(InputObject, _prevInputObject);
        _prevInputObject = InputObject;
        _batches.Add(new object?[] { inputRebound, InputObject });
    }

    /// <summary>Runs the begin block once, then replays the process body per batch, in one shared scope.</summary>
    protected override void EndProcessing()
    {
        if (Interrupted)
            return;

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, ProcessScript,
            _batches.ToArray(), _byNameInputObject, SqlInstance, SqlCredential, ServerRole, Login, Role, EnableException.ToBool(), this,
            _sqlInstanceByName, _serverRoleByName, _loginByName,
            BoundCommonParameter("WhatIf"), BoundCommonParameter("Confirm"),
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
        {
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                RemoveHopErrorBookkeeping(nestedError);
                WriteError(nestedError);
                continue;
            }
            if (item is not null)
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

    // PS: the begin block (its Test-Bound checks mapped to the carried by-name flags) then a per-batch dot-sourced
    // replay of the process body, all in one scope. Substitutions only: $PSCmdlet -> $__realCmdlet (the two
    // ShouldProcess gates); Test-Bound SqlInstance/ServerRole/Login -> carried by-name flags; -FunctionName on the
    // 15 DIRECT Stop-Function/Write-Message calls. EnableException received untyped.
    private const string ProcessScript = """
param($__batches, $__byNameInputObject, $SqlInstance, $SqlCredential, $ServerRole, $Login, $Role, $EnableException, $__realCmdlet, $__sqlInstanceByName, $__serverRoleByName, $__loginByName, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundWhatIf) { $__commonParameters.WhatIf = [bool]$__boundWhatIf }
if ($null -ne $__boundConfirm) { $__commonParameters.Confirm = [bool]$__boundConfirm }
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding(SupportsShouldProcess)]
    param($__batches, $__byNameInputObject, [Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [System.Management.Automation.PSCredential]$SqlCredential, [string[]]$ServerRole, [string[]]$Login, [string[]]$Role, $EnableException, $__realCmdlet, $__sqlInstanceByName, $__serverRoleByName, $__loginByName, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

        $InputObject = $__byNameInputObject
        if ( (-not $__sqlInstanceByName) -and (-not $__serverRoleByName) -and (-not $__loginByName) ) {
            Stop-Function -Message "You must pipe in a ServerRole, Login, or specify a SqlInstance" -FunctionName Remove-DbaServerRoleMember
            return
        }

        if ($__sqlInstanceByName) {
            $InputObject = $SqlInstance
        }
        foreach ($__batch in $__batches) {
            if ($__batch[0]) { $InputObject = $__batch[1] }
            . {
        if (Test-FunctionInterrupt) { return }

        foreach ($input in $InputObject) {
            $inputType = $input.GetType().FullName

            if ((-not $__serverRoleByName ) -and ($inputType -ne 'Microsoft.SqlServer.Management.Smo.ServerRole')) {
                Stop-Function -Message "You must pipe in a ServerRole or specify a ServerRole." -FunctionName Remove-DbaServerRoleMember
                return
            }

            switch ($inputType) {
                'Dataplat.Dbatools.Parameter.DbaInstanceParameter' {
                    Write-Message -Level Verbose -Message "Processing DbaInstanceParameter through InputObject" -FunctionName Remove-DbaServerRoleMember
                    try {
                        $serverRoles = Get-DbaServerRole -SqlInstance $input -SqlCredential $SqlCredential -ServerRole $ServerRole -EnableException
                    } catch {
                        Stop-Function -Message "Failure access $input" -ErrorRecord $_ -Target $input -Continue -FunctionName Remove-DbaServerRoleMember
                    }
                }
                'Microsoft.SqlServer.Management.Smo.Server' {
                    Write-Message -Level Verbose -Message "Processing Server through InputObject" -FunctionName Remove-DbaServerRoleMember
                    try {
                        $serverRoles = Get-DbaServerRole -SqlInstance $input -SqlCredential $SqlCredential -ServerRole $ServerRole -EnableException
                    } catch {
                        Stop-Function -Message "Failure access $input" -ErrorRecord $_ -Target $input -Continue -FunctionName Remove-DbaServerRoleMember
                    }
                }
                'Microsoft.SqlServer.Management.Smo.ServerRole' {
                    Write-Message -Level Verbose -Message "Processing ServerRole through InputObject" -FunctionName Remove-DbaServerRoleMember
                    try {
                        $serverRoles = $inputObject
                    } catch {
                        Stop-Function -Message "Failure access $input" -ErrorRecord $_ -Target $input -Continue -FunctionName Remove-DbaServerRoleMember
                    }
                }
                default {
                    Stop-Function -Message "InputObject is not a server or role." -FunctionName Remove-DbaServerRoleMember
                    continue
                }
            }

            foreach ($sr in $serverRoles) {
                $instance = $sr.Parent
                foreach ($l in $Login) {
                    if ($__realCmdlet.ShouldProcess($instance, "Removing login $l from server-level role: $sr")) {
                        Write-Message -Level Verbose -Message "Removing login $l from server-level role: $sr on $instance" -FunctionName Remove-DbaServerRoleMember
                        try {
                            $sr.DropMember($l)
                        } catch {
                            Stop-Function -Message "Failure removing $l on $instance" -ErrorRecord $_ -Target $sr -FunctionName Remove-DbaServerRoleMember
                        }
                    }

                }
                foreach ($r in $Role) {
                    try {
                        $isServerRole = Get-DbaServerRole -SqlInstance $input -SqlCredential $SqlCredential -ServerRole $r -EnableException
                    } catch {
                        Stop-Function -Message "Failure access $input" -ErrorRecord $_ -Target $input -FunctionName Remove-DbaServerRoleMember
                        continue
                    }
                    if (-not $isServerRole) {
                        Write-Message -Level Warning -Message "$r server-level role was not found on $instance" -FunctionName Remove-DbaServerRoleMember
                        continue
                    }
                    if ($__realCmdlet.ShouldProcess($instance, "Removing role $r from server-level role: $sr")) {
                        Write-Message -Level Verbose -Message "Removing role $r from server-level role: $sr on $instance" -FunctionName Remove-DbaServerRoleMember
                        try {
                            $sr.DropMembershipFromRole($r)
                        } catch {
                            Stop-Function -Message "Failure removing $r on $instance" -ErrorRecord $_ -Target $sr -FunctionName Remove-DbaServerRoleMember
                        }
                    }
                }
            }
        }
            }
        }
} $__batches $__byNameInputObject $SqlInstance $SqlCredential $ServerRole $Login $Role $EnableException $__realCmdlet $__sqlInstanceByName $__serverRoleByName $__loginByName $__boundWhatIf $__boundConfirm $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1

""";
}
