#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Adds users or roles to database roles. Port of public/Add-DbaDbRoleMember.ps1; the workflow
/// remains a module-scoped PowerShell compatibility hop.
///
/// The WHOLE process body rides ONE verbatim hop per pipeline record. The body never connects -
/// it reaches roles through Get-DbaDbRole - so there is no NestedConnect.
///
/// SqlInstance and InputObject are BOTH ValueFromPipeline, and the body's first act reassigns the
/// InputObject parameter from SqlInstance. That reassignment cannot leak across records: the
/// engine rebinds both pipeline parameters every record, and a record whose value does not convert
/// to DbaInstanceParameter leaves SqlInstance NULL rather than carrying the previous record's
/// value - measured on a mixed pipeline against both a script function and a compiled cmdlet using
/// this same parameter type, which agree. So no process-complete sentinel and no C# state field.
/// Note the asymmetry that measurement also showed: the SqlInstance KEY stays in
/// $PSBoundParameters on a record that did not bind it, while the VALUE is null. The body reads
/// the value (if ($SqlInstance)), never the boundness, so it is unaffected - but a Test-Bound on a
/// pipeline parameter would read the stale key and must never be flag-substituted.
///
/// Test-Bound cannot ride the hop (it scope-walks the caller's $PSBoundParameters, which inside
/// the hop is the inner scriptblock's own positional binding, where every parameter always appears
/// bound). The single Role call site is flag-substituted; Role is not a pipeline parameter, so its
/// boundness is stable for the whole invocation.
///
/// ConfirmImpact is High and both $PSCmdlet.ShouldProcess gates route to the real cmdlet via
/// $__realCmdlet, so prompting, -WhatIf, and a yes-to-all answer all behave as the function did and
/// persist across records. In-hop Stop-Function and Write-Message carry -FunctionName because
/// Stop-Function defaults it from Get-PSCallStack and the hop's frame is generated script;
/// -ModuleName/-File/-Line still misattribute [DEF-006]. $EnableException rides the hop param
/// scope because Stop-Function self-defaults it with a scope-walking $EnableException =
/// $EnableException default.
///
/// Surface pinned by migration/baselines/Add-DbaDbRoleMember.json.
/// </summary>
[Cmdlet(VerbsCommon.Add, "DbaDbRoleMember", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.High)]
public sealed class AddDbaDbRoleMemberCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The database(s) to process.</summary>
    [Parameter(Position = 2)]
    [PsStringArrayCast]
    public string[]? Database { get; set; }

    /// <summary>The role(s) to add members to.</summary>
    [Parameter(Position = 3)]
    [PsStringArrayCast]
    public string[]? Role { get; set; }

    /// <summary>The user(s) or role(s) to add to the role.</summary>
    [Parameter(Mandatory = true, Position = 4)]
    [Alias("User")]
    [PsStringArrayCast]
    public string[] Member { get; set; } = null!;

    /// <summary>A server, database, or database role object to process.</summary>
    [Parameter(ValueFromPipeline = true, Position = 5)]
    public object[]? InputObject { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

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
            SqlInstance, SqlCredential, Database, Role, Member, InputObject,
            EnableException.ToBool(), TestBound(nameof(Role)),
            this, BoundCommonParameter("WhatIf"), BoundCommonParameter("Confirm"),
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug"));
    }

    /// <summary>Carries a bound common parameter into the hop scopes, which cannot see the
    /// caller's $PSBoundParameters. Null means the caller never bound it.</summary>
    private object? BoundCommonParameter(string name)
    {
        if (MyInvocation.BoundParameters.TryGetValue(name, out object? value))
            return LanguagePrimitives.IsTrue(value);
        return null;
    }

    /// <summary>Removes the silent $error copy the nested pipeline bagged for a merged-back
    /// non-terminating record, so the caller sees one entry per error as the function did.</summary>
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

    // PS: the ENTIRE process body VERBATIM per record. Substitutions only: $PSCmdlet ->
    // $__realCmdlet, the single Test-Bound read -> a carried bound flag, and explicit
    // -FunctionName Add-DbaDbRoleMember on every Stop-Function/Write-Message. The switch over the
    // input's type name, the reassignment of $InputObject from $SqlInstance, the loop variable
    // named $input that shadows the automatic, the EnumMembers duplicate check, and the
    // user-vs-role branch all ride as-is.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Database, $Role, $Member, $InputObject, $EnableException, $__boundRole, $__realCmdlet, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundWhatIf) { $__commonParameters.WhatIf = [bool]$__boundWhatIf }
if ($null -ne $__boundConfirm) { $__commonParameters.Confirm = [bool]$__boundConfirm }
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = "High")]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [string[]]$Database, [string[]]$Role, [string[]]$Member, [object[]]$InputObject, $EnableException, $__boundRole, $__realCmdlet, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

        if (-not $InputObject -and -not $SqlInstance) {
            Stop-Function -Message "You must pipe in a role, database, or server or specify a SqlInstance" -FunctionName Add-DbaDbRoleMember
            return
        }

        if ($SqlInstance) {
            $InputObject = $SqlInstance
        }

        foreach ($input in $InputObject) {
            $inputType = $input.GetType().FullName
            switch ($inputType) {
                'Dataplat.Dbatools.Parameter.DbaInstanceParameter' {
                    Write-Message -Level Verbose -Message "Processing DbaInstanceParameter through InputObject" -FunctionName Add-DbaDbRoleMember -ModuleName "dbatools"
                    $dbRoles = Get-DbaDbRole -SqlInstance $input -SqlCredential $SqlCredential -Database $Database -Role $Role
                }
                'Microsoft.SqlServer.Management.Smo.Server' {
                    Write-Message -Level Verbose -Message "Processing Server through InputObject" -FunctionName Add-DbaDbRoleMember -ModuleName "dbatools"
                    $dbRoles = Get-DbaDbRole -SqlInstance $input -SqlCredential $SqlCredential -Database $Database -Role $Role
                }
                'Microsoft.SqlServer.Management.Smo.Database' {
                    Write-Message -Level Verbose -Message "Processing Database through InputObject" -FunctionName Add-DbaDbRoleMember -ModuleName "dbatools"
                    $dbRoles = $input | Get-DbaDbRole -Role $Role
                }
                'Microsoft.SqlServer.Management.Smo.DatabaseRole' {
                    Write-Message -Level Verbose -Message "Processing DatabaseRole through InputObject" -FunctionName Add-DbaDbRoleMember -ModuleName "dbatools"
                    $dbRoles = $input
                }
                default {
                    Stop-Function -Message "InputObject is not a server, database, or database role." -FunctionName Add-DbaDbRoleMember
                    return
                }
            }

            if ((-not $__boundRole) -and ($inputType -ne 'Microsoft.SqlServer.Management.Smo.DatabaseRole')) {
                Stop-Function -Message "You must pipe in a DatabaseRole or specify a Role." -FunctionName Add-DbaDbRoleMember
                return
            }

            foreach ($dbRole in $dbRoles) {
                $db = $dbRole.Parent
                $instance = $db.Parent
                Write-Message -Level 'Verbose' -Message "Getting Database Role Members for $dbRole in $db on $instance" -FunctionName Add-DbaDbRoleMember -ModuleName "dbatools"

                $members = $dbRole.EnumMembers()

                foreach ($newMember in $Member) {
                    if ($db.Users.Name -contains $newMember) {
                        if ($members -notcontains $newMember) {
                            if ($__realCmdlet.ShouldProcess($instance, "Adding user $newMember to role: $dbRole in database $db")) {
                                Write-Message -Level 'Verbose' -Message "Adding user $newMember to role: $dbRole in database $db on $instance" -FunctionName Add-DbaDbRoleMember -ModuleName "dbatools"
                                $dbRole.AddMember($newMember)
                            }
                        }
                    } elseif ($db.Roles.Name -contains $newMember) {
                        if ($members -notcontains $newMember) {
                            if ($__realCmdlet.ShouldProcess($instance, "Adding role $newMember to role: $dbRole in database $db")) {
                                Write-Message -Level 'Verbose' -Message "Adding role $newMember to role: $dbRole in database $db on $instance" -FunctionName Add-DbaDbRoleMember -ModuleName "dbatools"
                                $dbRole.AddMember($newMember)
                            }
                        }
                    } else {
                        Write-Message -Level 'Warning' -Message "User or role $newMember does not exist in $db on $instance" -FunctionName Add-DbaDbRoleMember -ModuleName "dbatools"
                    }
                }
            }
        }
} $SqlInstance $SqlCredential $Database $Role $Member $InputObject $EnableException $__boundRole $__realCmdlet $__boundWhatIf $__boundConfirm $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
