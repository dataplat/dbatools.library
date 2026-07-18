#nullable enable

using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Grants endpoint or availability-group permissions to logins, creating missing
/// logins on demand. Port of public/Grant-DbaAgPermission.ps1; surface pinned by
/// migration/baselines/Grant-DbaAgPermission.json.
/// </summary>
[Cmdlet(VerbsSecurity.Grant, "DbaAgPermission", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Low)]
public sealed class GrantDbaAgPermissionCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Login to the target instances using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The logins to grant permissions to; created when missing.</summary>
    [Parameter(Position = 2)]
    public string[]? Login { get; set; }

    /// <summary>The availability groups to grant permissions on.</summary>
    [Parameter(Position = 3)]
    public string[]? AvailabilityGroup { get; set; }

    /// <summary>Whether the grant targets the mirroring endpoint or availability groups.</summary>
    [Parameter(Mandatory = true, Position = 4)]
    [ValidateSet("Endpoint", "AvailabilityGroup")]
    public string[]? Type { get; set; }

    /// <summary>The permissions to grant.</summary>
    [Parameter(Position = 5)]
    [ValidateSet("Alter", "Connect", "Control", "CreateAnyDatabase", "CreateSequence", "Delete", "Execute", "Impersonate", "Insert", "Receive", "References", "Select", "Send", "TakeOwnership", "Update", "ViewChangeTracking", "ViewDefinition")]
    public string[] Permission { get; set; } = new[] { "Connect" };

    /// <summary>Login objects piped from Get-DbaLogin.</summary>
    [Parameter(ValueFromPipeline = true, Position = 6)]
    public Microsoft.SqlServer.Management.Smo.Login[]? InputObject { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        if (Interrupted)
        {
            return;
        }

        // Whole-record hop per the W4-011 mutating convention: both ShouldProcess gates
        // run on the INNER hop scriptblock's own $Pscmdlet (the inner block re-declares
        // SupportsShouldProcess + ConfirmImpact Low - never prompting by default,
        // exactly like the function; bound WhatIf/Confirm forward raw). The source's
        // three validation returns and the two mid-loop catch returns exit the whole
        // record via the dot-block frame; the $InputObject += accumulation and the
        // account loop share record scope. The plain Stop-Function sites' interrupt
        // flags are write-only inert (no Test-FunctionInterrupt in source).
        foreach (PSObject? item in NestedCommand.InvokeScoped(this, ProcessScript,
            SqlInstance, SqlCredential, Login, AvailabilityGroup, Type, Permission,
            InputObject, EnableException.ToBool(),
            TestBound(nameof(SqlInstance)), TestBound(nameof(InputObject)),
            BoundCommonParameter("WhatIf"), BoundCommonParameter("Confirm"),
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

    // PS: the source process block VERBATIM, CRLF-preserved and cmp-proven byte-exact
    // after stripping eleven -FunctionName appends and the one multi-name Test-Bound
    // rewrite (SOURCE comment). ShouldProcess gates use the inner block's own
    // $Pscmdlet; the dot-block preserves the source's early returns.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Login, $AvailabilityGroup, $Type, $Permission, $InputObject, $EnableException, $__boundSqlInstance, $__boundInputObject, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundWhatIf) { $__commonParameters.WhatIf = [bool]$__boundWhatIf }
if ($null -ne $__boundConfirm) { $__commonParameters.Confirm = [bool]$__boundConfirm }
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'Low')]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [string[]]$Login, [string[]]$AvailabilityGroup, [string[]]$Type, [string[]]$Permission, [Microsoft.SqlServer.Management.Smo.Login[]]$InputObject, $EnableException, $__boundSqlInstance, $__boundInputObject, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    . {
        if (-not ($__boundSqlInstance -or $__boundInputObject)) { # SOURCE: if (Test-Bound -Not SqlInstance, InputObject) {
            Stop-Function -Message "You must supply either -SqlInstance or an Input Object" -FunctionName Grant-DbaAgPermission
            return
        }

        if ($Type -contains "Endpoint" -and $SqlInstance -and -not $Login) {
            Stop-Function -Message "You must specify one or more logins when using the Endpoint type together with the SqlInstance parameter." -FunctionName Grant-DbaAgPermission
            return
        }

        if ($Type -contains "AvailabilityGroup" -and -not $AvailabilityGroup) {
            Stop-Function -Message "You must specify at least one availability group when using the AvailabilityGroup type." -FunctionName Grant-DbaAgPermission
            return
        }

        foreach ($instance in $SqlInstance) {
            try {
                $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential
            } catch {
                Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Grant-DbaAgPermission
            }
            if ($Permission -contains "CreateAnyDatabase") {
                foreach ($ag in $AvailabilityGroup) {
                    try {
                        $server.GrantAvailabilityGroupCreateDatabasePrivilege($ag)
                        $server.Alter()
                    } catch {
                        Stop-Function -Message "Failure executing GrantAvailabilityGroupCreateDatabasePrivilege for Availability Group $ag" -ErrorRecord $_ -Target $instance -FunctionName Grant-DbaAgPermission
                        return
                    }
                }
            }
            if ($Login) {
                $InputObject += Get-DbaLogin -SqlInstance $server -SqlCredential $SqlCredential -Login $Login
                foreach ($account in $Login) {
                    if ($account -notin $InputObject.Name) {
                        try {
                            $InputObject += New-DbaLogin -SqlInstance $server -Login $account -EnableException
                        } catch {
                            Stop-Function -Message "Failure creating login $account" -ErrorRecord $_ -Target $instance -FunctionName Grant-DbaAgPermission
                            return
                        }
                    }
                }
            }
        }

        foreach ($account in $InputObject) {
            $server = $account.Parent
            if ($Type -contains "Endpoint") {
                $server.Endpoints.Refresh()
                $endpoint = $server.Endpoints | Where-Object EndpointType -eq DatabaseMirroring

                if (-not $endpoint) {
                    Stop-Function -Message "DatabaseMirroring endpoint does not exist on $server" -Target $server -Continue -FunctionName Grant-DbaAgPermission
                }

                foreach ($perm in $Permission) {
                    if ($Pscmdlet.ShouldProcess($server.Name, "Granting $perm on $endpoint")) {
                        if ($perm -in 'CreateAnyDatabase') {
                            Stop-Function -Message "$perm not supported by endpoints" -Continue -FunctionName Grant-DbaAgPermission
                        }
                        try {
                            $bigperms = New-Object Microsoft.SqlServer.Management.Smo.ObjectPermissionSet([Microsoft.SqlServer.Management.Smo.ObjectPermission]::$perm)
                            $endpoint.Grant($bigperms, $account.Name)
                            [PSCustomObject]@{
                                ComputerName = $account.ComputerName
                                InstanceName = $account.InstanceName
                                SqlInstance  = $account.SqlInstance
                                Name         = $account.Name
                                Permission   = $perm
                                Type         = "Grant"
                                Status       = "Success"
                            }
                        } catch {
                            Stop-Function -Message "Failure granting $perm on endpoint to $($account.Name)" -ErrorRecord $_ -Target $account -Continue -FunctionName Grant-DbaAgPermission
                        }
                    }
                }
            }

            if ($Type -contains "AvailabilityGroup") {
                $ags = Get-DbaAvailabilityGroup -SqlInstance $server -AvailabilityGroup $AvailabilityGroup
                foreach ($ag in $ags) {
                    foreach ($perm in $Permission) {
                        if ($perm -notin 'Alter', 'Control', 'TakeOwnership', 'ViewDefinition') {
                            Stop-Function -Message "$perm not supported by availability groups" -Continue -FunctionName Grant-DbaAgPermission
                        }
                        if ($Pscmdlet.ShouldProcess($server.Name, "Granting $perm on $ags")) {
                            try {
                                $bigperms = New-Object Microsoft.SqlServer.Management.Smo.ObjectPermissionSet([Microsoft.SqlServer.Management.Smo.ObjectPermission]::$perm)
                                $ag.Grant($bigperms, $account.Name)
                                [PSCustomObject]@{
                                    ComputerName = $account.ComputerName
                                    InstanceName = $account.InstanceName
                                    SqlInstance  = $account.SqlInstance
                                    Name         = $account.Name
                                    Permission   = $perm
                                    Type         = "Grant"
                                    Status       = "Success"
                                }
                            } catch {
                                Stop-Function -Message "Failure granting $perm on availability group $($ag.Name) to $($account.Name)" -ErrorRecord $_ -Target $ag -Continue -FunctionName Grant-DbaAgPermission
                            }
                        }
                    }
                }
            }
        }
    }
} $SqlInstance $SqlCredential $Login $AvailabilityGroup $Type $Permission $InputObject $EnableException $__boundSqlInstance $__boundInputObject $__boundWhatIf $__boundConfirm $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
