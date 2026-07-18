#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Expands the Windows Active Directory group logins on one or more SQL Server instances into the
/// individual user accounts that inherit access through group membership.
/// </summary>
/// <remarks>
/// <para>
/// The workflow remains a module-scoped PowerShell compatibility hop so that the assembly load, the
/// recursive group expansion, the connection, the output shape, and dbatools stream and error handling
/// stay observable-identical to the script implementation.
/// </para>
/// <para>
/// The script's begin block does two deterministic things - it loads the AccountManagement assembly and
/// defines the recursive Get-AllLogins helper - and its process block emits per instance. The begin work
/// is folded into the per-record hop rather than a separate BeginProcessing hop: Add-Type is idempotent
/// (loading an already-loaded assembly is a no-op) and the helper definition is deterministic, so
/// per-record == run-once, and the helper MUST be defined in the same scope that calls it (Get-AllLogins
/// reads $server from the process scope by dynamic scoping). The only non-parity edge - the begin's
/// "Failed to load Assembly needed" stop would warn once per record instead of once - is unreachable in
/// practice because that assembly is always present on a Windows host running SQL Server tooling.
/// </para>
/// <para>
/// Output is streamed through InvokeScopedStreaming: SqlInstance binds an array within a record and the
/// body emits one result per instance, so a later instance's terminating -EnableException failure (the
/// connection Stop-Function) or a downstream early stop must not discard or overrun the earlier instances'
/// output - streaming reaches the pipeline as produced and honors the stop. EnableException is carried as
/// a plain (untyped) value, because a switch in the inner CmdletBinding scriptblock is excluded from
/// positional binding. Only the three DIRECT begin/process Stop-Function/Write-Message calls take
/// -FunctionName; the nested Get-AllLogins calls already attribute to "Get-AllLogins" in both worlds
/// (the helper name is preserved verbatim) and are left unedited.
/// </para>
/// </remarks>
[Cmdlet(VerbsCommon.Find, "DbaLoginInGroup")]
public sealed class FindDbaLoginInGroupCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Alternative credential for the target instances.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>Filters the expanded results to these login names.</summary>
    [Parameter(Position = 2)]
    public string[]? Login { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    /// <summary>Expands group logins for one pipeline record.</summary>
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
            }
            else
            {
                WriteObject(item);
            }
        }, ProcessScript,
            SqlInstance, SqlCredential, Login, EnableException.ToBool(),
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug"));
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

    // PS: the begin body (Add-Type + the Get-AllLogins helper) and the process body VERBATIM, folded into
    // one per-record hop. Substitutions only: -FunctionName on the 3 DIRECT Stop-Function/Write-Message
    // calls (the begin assembly-load stop and the process connection stop + group message). The nested
    // Get-AllLogins calls are unedited - they attribute to the helper in both worlds. No ShouldProcess,
    // Test-Bound, config defaults, or bound carries.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Login, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [System.Management.Automation.PSCredential]$SqlCredential, [string[]]$Login, $EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

        try {
            Add-Type -AssemblyName System.DirectoryServices.AccountManagement
        } catch {
            Stop-Function -Message "Failed to load Assembly needed" -ErrorRecord $_ -FunctionName Find-DbaLoginInGroup
            return
        }

        function Get-AllLogins {
            param
            (
                [string]$ADGroup,
                [string[]]$discard,
                [string]$ParentADGroup
            )
            begin {
                $output = @()
            }
            process {
                try {
                    $domain = $AdGroup.Split("\")[0]
                    $ads = New-Object System.DirectoryServices.AccountManagement.PrincipalContext('Domain', $domain)
                    [string]$groupName = $AdGroup
                    $group = [System.DirectoryServices.AccountManagement.GroupPrincipal]::FindByIdentity($ads, $groupName);
                    $subgroups = @()
                    foreach ($member in $group.Members) {
                        $memberDomain = $member.Context.Name
                        if ($member.StructuralObjectClass -eq 'group') {
                            $fullName = $memberDomain + "\" + $member.SamAccountName
                            if ($fullName -in $discard) {
                                Write-Message -Level Verbose -Message "skipping $fullName, already enumerated"
                                continue
                            } else {
                                $subgroups += $fullName
                            }
                        } else {
                            $output += [PSCustomObject]@{
                                SqlInstance        = $server.Name
                                InstanceName       = $server.ServiceName
                                ComputerName       = $server.ComputerName
                                Login              = $memberDomain + "\" + $member.SamAccountName
                                DisplayName        = $member.DisplayName
                                MemberOf           = $AdGroup
                                ParentADGroupLogin = $ParentADGroup
                            }
                        }
                    }
                } catch {
                    Stop-Function -Message "Failed to connect to Group: $member." -Target $member -ErrorRecord $_
                }
                $discard += $ADGroup
                foreach ($gr in $subgroups) {
                    if ($gr -notin $discard) {
                        $discard += $gr
                        Write-Message -Level Verbose -Message "Looking at $gr, recursively."
                        Get-AllLogins -ADGroup $gr -discard $discard -ParentADGroup $ParentADGroup
                    }
                }
            }
            end {
                $output
            }
        }
        if (Test-FunctionInterrupt) { return }

        foreach ($instance in $SqlInstance) {
            try {
                $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential
            } catch {
                Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Find-DbaLoginInGroup
            }

            $AdGroups = $server.Logins | Where-Object { $_.LoginType -eq "WindowsGroup" -and $_.Name -ne "BUILTIN\Administrators" -and $_.Name -notlike "*NT SERVICE*" }
            # remove local groups, see #7820
            $AdGroups = $AdGroups | Where-Object Name -notlike "$($server.ComputerName)\*"
            $ADGroupOut = @()
            foreach ($AdGroup in $AdGroups) {
                Write-Message -Level Verbose -Message "Looking at Group: $AdGroup" -FunctionName Find-DbaLoginInGroup
                $ADGroupOut += Get-AllLogins $AdGroup.Name -ParentADGroup $AdGroup.Name
            }

            if (-not $Login) {
                $res = $ADGroupOut
            } else {
                $res = $ADGroupOut | Where-Object { $Login -contains $_.Login }
                if ($res.Length -eq 0) {
                    continue
                }
            }
            Select-DefaultView -InputObject $res -Property SqlInstance, Login, DisplayName, MemberOf, ParentADGroupLogin
        }
} $SqlInstance $SqlCredential $Login $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1

""";
}
