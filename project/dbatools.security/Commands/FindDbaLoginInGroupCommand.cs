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
/// The script's begin block loads the AccountManagement assembly (and, on failure, stops the command)
/// and defines the recursive Get-AllLogins helper; the process block emits per instance. The port keeps
/// the assembly load in a BeginProcessing hop so it runs EXACTLY ONCE, including for a zero-record
/// pipeline, matching the script's begin semantics; that hop emits a sentinel as its last statement, so
/// its absence tells the cmdlet the begin stopped (assembly-load failure) and ProcessRecord then does no
/// work. The Get-AllLogins helper definition stays folded into the ProcessRecord hop because the helper
/// reads $server (assigned in the process body) by dynamic scoping and so must be defined in the scope
/// that calls it; defining it per record is deterministic and silent, so it is observably identical.
/// </para>
/// <para>
/// ProcessRecord output is streamed through InvokeScopedStreaming: SqlInstance binds an array within a
/// record and the body emits one result per instance, so a later instance's terminating -EnableException
/// failure (the connection Stop-Function) or a downstream early stop must not discard or overrun the
/// earlier instances' output. EnableException is carried as a plain (untyped) value, because a switch in
/// the inner CmdletBinding scriptblock is excluded from positional binding. Only the three DIRECT
/// begin/process Stop-Function/Write-Message calls take -FunctionName; the nested Get-AllLogins calls
/// already attribute to "Get-AllLogins" in both worlds (the helper name is preserved verbatim) and are
/// left unedited.
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

    /// <summary>Set when the begin block stopped the command (the assembly failed to load).</summary>
    private bool _beginInterrupted;

    /// <summary>Loads the AccountManagement assembly once, before any records are processed.</summary>
    protected override void BeginProcessing()
    {
        bool completed = false;
        foreach (PSObject? item in NestedCommand.InvokeScoped(this, BeginScript,
            EnableException.ToBool(), BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
        {
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                RemoveHopErrorBookkeeping(nestedError);
                WriteError(nestedError);
            }
            else if (item is not null && LanguagePrimitives.IsTrue(
                item.Properties["__FindDbaLoginInGroupBeginComplete"]?.Value))
            {
                completed = true;
            }
            else if (item is not null)
            {
                WriteObject(item);
            }
        }

        // The sentinel is the last statement of the begin body, so it is absent exactly when that body
        // returned early - which it does only after the assembly-load Stop-Function.
        _beginInterrupted = !completed;
    }

    /// <summary>Expands group logins for one pipeline record.</summary>
    protected override void ProcessRecord()
    {
        if (_beginInterrupted || Interrupted)
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

    // PS: the begin body's assembly load VERBATIM (its Stop-Function takes -FunctionName), then a sentinel.
    private const string BeginScript = """
param($EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param($EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

        try {
            Add-Type -AssemblyName System.DirectoryServices.AccountManagement
        } catch {
            Stop-Function -Message "Failed to load Assembly needed" -ErrorRecord $_ -FunctionName Find-DbaLoginInGroup
            return
        }
    [PSCustomObject]@{ __FindDbaLoginInGroupBeginComplete = $true }
} $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1

""";

    // PS: the Get-AllLogins helper (folded here for dynamic $server) then the process body VERBATIM.
    // Substitutions only: -FunctionName on the 2 DIRECT process Stop-Function/Write-Message calls; the
    // nested Get-AllLogins calls attribute to "Get-AllLogins" in both worlds and are unedited.
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
                Write-Message -Level Verbose -Message "Looking at Group: $AdGroup" -FunctionName Find-DbaLoginInGroup -ModuleName "dbatools"
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
