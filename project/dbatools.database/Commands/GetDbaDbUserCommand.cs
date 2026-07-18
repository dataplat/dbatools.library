#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves database user objects and metadata from databases. Port of public/Get-DbaDbUser.ps1;
/// the workflow remains a module-scoped PowerShell compatibility hop.
///
/// A process-only port (SqlInstance is Mandatory ValueFromPipeline, so process fires per record). No accumulator,
/// no interrupt, no ShouldProcess. Two distinct switch/flag mechanisms: (1) ExcludeSystemUser is consumed as a
/// VALUE (if ($ExcludeSystemUser)), so it is passed as a marshaled bool (ExcludeSystemUser.ToBool()) into an
/// UNTYPED inner hop param - typing it [switch] would exclude it from positional binding and shift the
/// positionally-called scriptblock args (the switch-in-hop-param law; binding-probed BOUND-OK). (2) User and Login
/// are consumed via Test-Bound - Test-Bound -ParameterName User -> $__boundUser and Test-Bound -ParameterName Login
/// -> $__boundLogin (carried flags); User/Login are ALSO passed as string[] values for the Where-Object filters.
/// The one continue (source 153) is inside foreach ($db) - loop-bound. Edits: -FunctionName Get-DbaDbUser on the one
/// Stop-Function (-Continue) and one Write-Message. Surface pinned by migration/baselines/Get-DbaDbUser.json
/// (positions 0-5, ExcludeSystemUser switch non-positional, SqlInstance Mandatory VFP pos0, no ShouldProcess).
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaDbUser")]
public sealed class GetDbaDbUserCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The database(s) to process.</summary>
    [Parameter(Position = 2)]
    public object[]? Database { get; set; }

    /// <summary>The database(s) to exclude.</summary>
    [Parameter(Position = 3)]
    public object[]? ExcludeDatabase { get; set; }

    /// <summary>Exclude system database users from the results.</summary>
    [Parameter]
    public SwitchParameter ExcludeSystemUser { get; set; }

    /// <summary>Filter to the specified database user(s) by name.</summary>
    [Parameter(Position = 4)]
    public string[]? User { get; set; }

    /// <summary>Filter to database users mapped to the specified login(s).</summary>
    [Parameter(Position = 5)]
    public string[]? Login { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, ProcessScript,
            SqlInstance, SqlCredential, Database, ExcludeDatabase, ExcludeSystemUser.ToBool(), User, Login,
            EnableException.ToBool(), TestBound(nameof(User)), TestBound(nameof(Login)),
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
    // PS: the process block VERBATIM. Edits: -FunctionName Get-DbaDbUser on the one Stop-Function (-Continue) and
    // one Write-Message; Test-Bound -ParameterName User -> $__boundUser and Test-Bound -ParameterName Login ->
    // $__boundLogin (carried flags). $ExcludeSystemUser arrives as a marshaled bool (used via if); its inner param
    // is UNTYPED to keep positional binding intact. The one continue is inside foreach ($db) - loop-bound.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Database, $ExcludeDatabase, $ExcludeSystemUser, $User, $Login, $EnableException, $__boundUser, $__boundLogin, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [object[]]$Database, [object[]]$ExcludeDatabase, $ExcludeSystemUser, [string[]]$User, [string[]]$Login, $EnableException, $__boundUser, $__boundLogin, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

        foreach ($instance in $SqlInstance) {
            try {
                $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential
            } catch {
                Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -FunctionName Get-DbaDbUser -Continue
            }


            $databases = Get-DbaDatabase -SqlInstance $server -SqlCredential $SqlCredential -Database $Database -ExcludeDatabase $ExcludeDatabase -OnlyAccessible

            foreach ($db in $databases) {

                $users = $db.users

                if (!$users) {
                    Write-Message -Message "No users exist in the $db database on $instance" -Target $db -Level Verbose -FunctionName Get-DbaDbUser
                    continue
                }
                if ($ExcludeSystemUser) {
                    $users = $users | Where-Object { $_.IsSystemObject -eq $false }
                }
                if ($__boundUser) {
                    $users = $users | Where-Object { $_.Name -in $User }
                }
                if ($__boundLogin) {
                    $users = $users | Where-Object { $_.Login -in $Login }
                }

                $users | ForEach-Object {

                    Add-Member -Force -InputObject $_ -MemberType NoteProperty -Name ComputerName -value $server.ComputerName
                    Add-Member -Force -InputObject $_ -MemberType NoteProperty -Name InstanceName -value $server.ServiceName
                    Add-Member -Force -InputObject $_ -MemberType NoteProperty -Name SqlInstance -value $server.DomainInstanceName
                    Add-Member -Force -InputObject $_ -MemberType NoteProperty -Name Database -value $db.Name

                    Select-DefaultView -InputObject $_ -Property ComputerName, InstanceName, SqlInstance, Database, CreateDate, DateLastModified, Name, Login, LoginType, AuthenticationType, State, HasDbAccess, DefaultSchema
                }
            }
        }
} $SqlInstance $SqlCredential $Database $ExcludeDatabase $ExcludeSystemUser $User $Login $EnableException $__boundUser $__boundLogin $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}