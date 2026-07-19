#nullable enable

using System;
using System.Collections;
using System.Globalization;
using System.Management.Automation;
using Dataplat.Dbatools.Connection;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Configures cached CIM/WMI management connections. Port of
/// public/Set-DbaCmConnection.ps1 (W3-087), third CmConnection sibling (W3-063/W3-071
/// shapes): begin hop emits the InternalComment/keys lines and computes $disable_cache
/// (sentinel); PER-ELEMENT process hops (25a09f3 - the source loop over $ComputerName
/// has no cross-element state); end hop emits the closing InternalComment. The
/// per-computer body rides VERBATIM inside the $__realCmdlet.ShouldProcess gate
/// (W1-085 - no ConfirmPreference override; default ConfirmImpact Medium), including
/// the Success check's Stop-Function -Continue INSIDE the gate (source ordering,
/// unlike the Remove sibling), the Reset*/Clear* branches, the bad-credential loops,
/// ELEVEN Test-Bound property gates carried as flags - including the source's
/// EnableCredentialFailover -> DisableCredentialAutoRegister assignment bug preserved
/// verbatim (the W3-063 line-199 sibling bug) - and the CimOptions ELSEIF-null defaults
/// (unlike the New sibling's plain else). $env:COMPUTERNAME bind-time default
/// (W1-087 class). NO WarningAction carrier (codex W3-005 r3). Surface pinned by
/// migration/baselines/Set-DbaCmConnection.json (sets Credential {Credential,
/// WindowsCredentialsAreBad} + Windows {UseWindowsCredentials}, default Credential,
/// NO positions, ComputerName VFP all-sets).
/// </summary>
[Cmdlet(VerbsCommon.Set, "DbaCmConnection", SupportsShouldProcess = true, DefaultParameterSetName = "Credential")]
public sealed class SetDbaCmConnectionCommand : DbaBaseCmdlet
{
    /// <summary>The computer(s) whose cached connections should be configured; defaults to this computer.</summary>
    [Parameter(ValueFromPipeline = true)]
    public DbaCmConnectionParameter[] ComputerName { get; set; } =
        (DbaCmConnectionParameter[])LanguagePrimitives.ConvertTo(
            Environment.GetEnvironmentVariable("COMPUTERNAME"),
            typeof(DbaCmConnectionParameter[]), CultureInfo.InvariantCulture);

    /// <summary>Credential to register for the connection.</summary>
    [Parameter(ParameterSetName = "Credential")]
    public PSCredential? Credential { get; set; }

    /// <summary>Marks the current Windows credentials as valid for the connection.</summary>
    [Parameter(ParameterSetName = "Windows")]
    public SwitchParameter UseWindowsCredentials { get; set; }

    /// <summary>Forces use of cached credentials over explicit ones.</summary>
    [Parameter]
    public SwitchParameter OverrideExplicitCredential { get; set; }

    /// <summary>Allows the connection to override the global connection policy.</summary>
    [Parameter]
    public SwitchParameter OverrideConnectionPolicy { get; set; }

    /// <summary>Connection protocols to disable for this connection.</summary>
    [Parameter]
    public ManagementConnectionType DisabledConnectionTypes { get; set; } = ManagementConnectionType.None;

    /// <summary>Prevents storing credentials known to fail.</summary>
    [Parameter]
    public SwitchParameter DisableBadCredentialCache { get; set; }

    /// <summary>Forces new CIM sessions instead of reusing existing ones.</summary>
    [Parameter]
    public SwitchParameter DisableCimPersistence { get; set; }

    /// <summary>Prevents auto-storing successful credentials.</summary>
    [Parameter]
    public SwitchParameter DisableCredentialAutoRegister { get; set; }

    /// <summary>Enables credential failover (source assigns DisableCredentialAutoRegister - preserved bug).</summary>
    [Parameter]
    public SwitchParameter EnableCredentialFailover { get; set; }

    /// <summary>Marks the current Windows credentials as invalid for the connection.</summary>
    [Parameter(ParameterSetName = "Credential")]
    public SwitchParameter WindowsCredentialsAreBad { get; set; }

    /// <summary>WSMan session options for CIM over WinRM.</summary>
    [Parameter]
    public Microsoft.Management.Infrastructure.Options.WSManSessionOptions? CimWinRMOptions { get; set; }

    /// <summary>DCOM session options for CIM over DCOM.</summary>
    [Parameter]
    public Microsoft.Management.Infrastructure.Options.DComSessionOptions? CimDCOMOptions { get; set; }

    /// <summary>Credentials to add to the known-bad list.</summary>
    [Parameter]
    public PSCredential[]? AddBadCredential { get; set; }

    /// <summary>Credentials to remove from the known-bad list.</summary>
    [Parameter]
    public PSCredential[]? RemoveBadCredential { get; set; }

    /// <summary>Clears the known-bad credential list.</summary>
    [Parameter]
    public SwitchParameter ClearBadCredential { get; set; }

    /// <summary>Clears the stored credential.</summary>
    [Parameter]
    public SwitchParameter ClearCredential { get; set; }

    /// <summary>Resets all credential state.</summary>
    [Parameter]
    public SwitchParameter ResetCredential { get; set; }

    /// <summary>Resets the cached connection-protocol status.</summary>
    [Parameter]
    public SwitchParameter ResetConnectionStatus { get; set; }

    /// <summary>Restores the default configuration.</summary>
    [Parameter]
    public SwitchParameter ResetConfiguration { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // begin-scope $disable_cache, computed once and read by every process record.
    private object? _disableCache;

    protected override void BeginProcessing()
    {
        foreach (PSObject? item in NestedCommand.InvokeScoped(this, BeginScript,
            string.Join(", ", MyInvocation.BoundParameters.Keys), EnableException.ToBool(),
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
        {
            Hashtable? sentinel = item?.BaseObject as Hashtable;
            if (sentinel is not null && sentinel.ContainsKey("__w3087DisableCache"))
            {
                _disableCache = sentinel["__w3087DisableCache"];
                continue;
            }
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                RemoveHopErrorBookkeeping(nestedError);
                WriteError(nestedError);
                continue;
            }
            WriteObject(item);
        }
    }

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        // Stream one hop PER COMPUTER: a whole-array hop batches every element's live
        // Debug/Verbose ahead of all buffered output, where the source's foreach
        // interleaves them per element (W2-010 P2A; coordinator 25a09f3 ruling). The
        // source loop body has no cross-element state.
        foreach (DbaCmConnectionParameter computer in ComputerName ?? Array.Empty<DbaCmConnectionParameter>())
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
            new[] { computer }, Credential, UseWindowsCredentials.ToBool(),
                OverrideExplicitCredential.ToBool(), OverrideConnectionPolicy.ToBool(),
                DisabledConnectionTypes, DisableBadCredentialCache.ToBool(),
                DisableCimPersistence.ToBool(), DisableCredentialAutoRegister.ToBool(),
                EnableCredentialFailover.ToBool(), WindowsCredentialsAreBad.ToBool(),
                CimWinRMOptions, CimDCOMOptions, AddBadCredential, RemoveBadCredential,
                ClearBadCredential.ToBool(), ClearCredential.ToBool(), ResetCredential.ToBool(),
                ResetConnectionStatus.ToBool(), ResetConfiguration.ToBool(),
                EnableException.ToBool(), _disableCache,
                TestBound(nameof(Credential)), TestBound(nameof(OverrideExplicitCredential)),
                TestBound(nameof(DisabledConnectionTypes)), TestBound(nameof(DisableBadCredentialCache)),
                TestBound(nameof(DisableCimPersistence)), TestBound(nameof(DisableCredentialAutoRegister)),
                TestBound(nameof(EnableCredentialFailover)), TestBound(nameof(WindowsCredentialsAreBad)),
                TestBound(nameof(CimWinRMOptions)), TestBound(nameof(CimDCOMOptions)),
                TestBound(nameof(OverrideConnectionPolicy)), this,
                BoundCommonParameter("WhatIf"), BoundCommonParameter("Confirm"),
                BoundCommonParameter("Verbose"), BoundCommonParameter("Debug"));
        }
    }

    protected override void EndProcessing()
    {
        if (Interrupted)
            return;

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, EndScript,
            EnableException.ToBool(),
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

    // PS: the begin block verbatim (the W3-063 sibling shape).
    private const string BeginScript = """
param($__boundKeys, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param($__boundKeys, $EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    Write-Message -Level InternalComment -Message "Starting execution" -FunctionName Set-DbaCmConnection
    Write-Message -Level Verbose -Message "Bound parameters: $__boundKeys" -FunctionName Set-DbaCmConnection

    $disable_cache = Get-DbatoolsConfigValue -Name 'ComputerManagement.Cache.Disable.All' -Fallback $false
    @{ __w3087DisableCache = $disable_cache }
} $__boundKeys $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";

    // PS: the process body VERBATIM per element. Substitutions only: Test-Bound "X" ->
    // carried $__boundX flags, $Pscmdlet -> $__realCmdlet, $disable_cache -> the
    // begin-computed carried value, and explicit -FunctionName Set-DbaCmConnection on
    // Write-Message/Stop-Function (W1-090). The EnableCredentialFailover ->
    // DisableCredentialAutoRegister assignment is the SOURCE's own bug - verbatim.
    private const string ProcessScript = """
param($ComputerName, $Credential, $UseWindowsCredentials, $OverrideExplicitCredential, $OverrideConnectionPolicy, $DisabledConnectionTypes, $DisableBadCredentialCache, $DisableCimPersistence, $DisableCredentialAutoRegister, $EnableCredentialFailover, $WindowsCredentialsAreBad, $CimWinRMOptions, $CimDCOMOptions, $AddBadCredential, $RemoveBadCredential, $ClearBadCredential, $ClearCredential, $ResetCredential, $ResetConnectionStatus, $ResetConfiguration, $EnableException, $__disableCache, $__boundCredential, $__boundOverrideExplicitCredential, $__boundDisabledConnectionTypes, $__boundDisableBadCredentialCache, $__boundDisableCimPersistence, $__boundDisableCredentialAutoRegister, $__boundEnableCredentialFailover, $__boundWindowsCredentialsAreBad, $__boundCimWinRMOptions, $__boundCimDCOMOptions, $__boundOverrideConnectionPolicy, $__realCmdlet, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundWhatIf) { $__commonParameters.WhatIf = [bool]$__boundWhatIf }
if ($null -ne $__boundConfirm) { $__commonParameters.Confirm = [bool]$__boundConfirm }
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding(SupportsShouldProcess)]
    param([Dataplat.Dbatools.Parameter.DbaCmConnectionParameter[]]$ComputerName, [PSCredential]$Credential, $UseWindowsCredentials, $OverrideExplicitCredential, $OverrideConnectionPolicy, [Dataplat.Dbatools.Connection.ManagementConnectionType]$DisabledConnectionTypes, $DisableBadCredentialCache, $DisableCimPersistence, $DisableCredentialAutoRegister, $EnableCredentialFailover, $WindowsCredentialsAreBad, [Microsoft.Management.Infrastructure.Options.WSManSessionOptions]$CimWinRMOptions, [Microsoft.Management.Infrastructure.Options.DComSessionOptions]$CimDCOMOptions, [System.Management.Automation.PSCredential[]]$AddBadCredential, [System.Management.Automation.PSCredential[]]$RemoveBadCredential, $ClearBadCredential, $ClearCredential, $ResetCredential, $ResetConnectionStatus, $ResetConfiguration, $EnableException, $__disableCache, $__boundCredential, $__boundOverrideExplicitCredential, $__boundDisabledConnectionTypes, $__boundDisableBadCredentialCache, $__boundDisableCimPersistence, $__boundDisableCredentialAutoRegister, $__boundEnableCredentialFailover, $__boundWindowsCredentialsAreBad, $__boundCimWinRMOptions, $__boundCimDCOMOptions, $__boundOverrideConnectionPolicy, $__realCmdlet, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    $disable_cache = $__disableCache

    foreach ($connectionObject in $ComputerName) {
        if ($__realCmdlet.ShouldProcess($($connectionObject.Connection.ComputerName), "Setting Connection")) {
            if (-not $connectionObject.Success) { Stop-Function -Message "Failed to interpret computername input: $($connectionObject.InputObject)" -Category InvalidArgument -Target $connectionObject.InputObject -Continue -FunctionName Set-DbaCmConnection }
            Write-Message -Level VeryVerbose -Message "Processing computer: $($connectionObject.Connection.ComputerName)" -FunctionName Set-DbaCmConnection

            $connection = $connectionObject.Connection

            if ($ResetConfiguration) {
                Write-Message -Level Verbose -Message "Resetting the configuration to system default" -FunctionName Set-DbaCmConnection

                $connection.RestoreDefaultConfiguration()
            }

            if ($ResetConnectionStatus) {
                Write-Message -Level Verbose -Message "Resetting the connection status" -FunctionName Set-DbaCmConnection

                $connection.CimRM = 'Unknown'
                $connection.CimDCOM = 'Unknown'
                $connection.Wmi = 'Unknown'
                $connection.PowerShellRemoting = 'Unknown'

                $connection.LastCimRM = New-Object System.DateTime(0)
                $connection.LastCimDCOM = New-Object System.DateTime(0)
                $connection.LastWmi = New-Object System.DateTime(0)
                $connection.LastPowerShellRemoting = New-Object System.DateTime(0)
            }

            if ($ResetCredential) {
                Write-Message -Level Verbose -Message "Resetting credentials" -FunctionName Set-DbaCmConnection

                $connection.KnownBadCredentials.Clear()
                $connection.Credentials = $null
                $connection.UseWindowsCredentials = $false
                $connection.WindowsCredentialsAreBad = $false
            } else {
                if ($ClearBadCredential) {
                    Write-Message -Level Verbose -Message "Clearing bad credentials" -FunctionName Set-DbaCmConnection

                    $connection.KnownBadCredentials.Clear()
                    $connection.WindowsCredentialsAreBad = $false
                }

                if ($ClearCredential) {
                    Write-Message -Level Verbose -Message "Clearing credentials" -FunctionName Set-DbaCmConnection

                    $connection.Credentials = $null
                    $connection.UseWindowsCredentials = $false
                }
            }

            foreach ($badCred in $RemoveBadCredential) {
                $connection.RemoveBadCredential($badCred)
            }

            foreach ($badCred in $AddBadCredential) {
                $connection.AddBadCredential($badCred)
            }

            if ($__boundCredential) { $connection.Credentials = $Credential }
            if ($UseWindowsCredentials) {
                $connection.Credentials = $null
                $connection.UseWindowsCredentials = $UseWindowsCredentials
            }
            if ($__boundOverrideExplicitCredential) { $connection.OverrideExplicitCredential = $OverrideExplicitCredential }
            if ($__boundDisabledConnectionTypes) { $connection.DisabledConnectionTypes = $DisabledConnectionTypes }
            if ($__boundDisableBadCredentialCache) { $connection.DisableBadCredentialCache = $DisableBadCredentialCache }
            if ($__boundDisableCimPersistence) { $connection.DisableCimPersistence = $DisableCimPersistence }
            if ($__boundDisableCredentialAutoRegister) { $connection.DisableCredentialAutoRegister = $DisableCredentialAutoRegister }
            if ($__boundEnableCredentialFailover) { $connection.DisableCredentialAutoRegister = $EnableCredentialFailover }
            if ($__boundWindowsCredentialsAreBad) { $connection.WindowsCredentialsAreBad = $WindowsCredentialsAreBad }
            if ($__boundCimWinRMOptions) {
                $connection.CimWinRMOptions = $CimWinRMOptions
            } elseif ($null -eq $connection.CimWinRMOptions) {
                $connection.CimWinRMOptions = New-DbaCimSessionOptionWithTimeout -Protocol Default
            }
            if ($__boundCimDCOMOptions) {
                $connection.CimDCOMOptions = $CimDCOMOptions
            } elseif ($null -eq $connection.CimDCOMOptions) {
                $connection.CimDCOMOptions = New-DbaCimSessionOptionWithTimeout -Protocol Dcom
            }
            if ($__boundOverrideConnectionPolicy) { $connection.OverrideConnectionPolicy = $OverrideConnectionPolicy }

            if (-not $disable_cache) {
                Write-Message -Level Verbose -Message "Writing connection to cache" -FunctionName Set-DbaCmConnection
                [Dataplat.Dbatools.Connection.ConnectionHost]::Connections[$connectionObject.Connection.ComputerName] = $connection
            } else { Write-Message -Level Verbose -Message "Skipping writing to cache, since the cache has been disabled." -FunctionName Set-DbaCmConnection }
            $connection
        }
    }
} $ComputerName $Credential $UseWindowsCredentials $OverrideExplicitCredential $OverrideConnectionPolicy $DisabledConnectionTypes $DisableBadCredentialCache $DisableCimPersistence $DisableCredentialAutoRegister $EnableCredentialFailover $WindowsCredentialsAreBad $CimWinRMOptions $CimDCOMOptions $AddBadCredential $RemoveBadCredential $ClearBadCredential $ClearCredential $ResetCredential $ResetConnectionStatus $ResetConfiguration $EnableException $__disableCache $__boundCredential $__boundOverrideExplicitCredential $__boundDisabledConnectionTypes $__boundDisableBadCredentialCache $__boundDisableCimPersistence $__boundDisableCredentialAutoRegister $__boundEnableCredentialFailover $__boundWindowsCredentialsAreBad $__boundCimWinRMOptions $__boundCimDCOMOptions $__boundOverrideConnectionPolicy $__realCmdlet $__boundWhatIf $__boundConfirm $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";

    // PS: the end block verbatim.
    private const string EndScript = """
param($EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param($EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    Write-Message -Level InternalComment -Message "Stopping execution" -FunctionName Set-DbaCmConnection
} $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
