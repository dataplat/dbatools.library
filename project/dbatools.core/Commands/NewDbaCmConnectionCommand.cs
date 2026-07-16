#nullable enable

using System;
using System.Collections;
using System.Globalization;
using System.Management.Automation;
using Dataplat.Dbatools.Connection;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Creates and caches CIM/WMI management connection objects. Port of
/// public/New-DbaCmConnection.ps1 (W3-063). The begin/process/end bodies ride verbatim
/// module hops (the W3-001 lifecycle shape): begin computes $disable_cache via the nested
/// Get-DbatoolsConfigValue and emits the two InternalComment/Verbose messages (the
/// "Bound parameters:" text carries this cmdlet's OWN bound-key list, which matches the
/// function's $PSBoundParameters keys for the same invocation); process wraps each record
/// in the $__realCmdlet.ShouldProcess gate and applies the eleven Test-Bound property
/// gates as carried bound flags - INCLUDING the source's line-199 bug, preserved verbatim:
/// -EnableCredentialFailover assigns DisableCredentialAutoRegister, never a failover
/// property. Nested private New-DbaCimSessionOptionWithTimeout and the
/// ConnectionHost.Connections cache write ride the hop. The surface mirrors the baseline's
/// parameter sets (Credential = {Credential, WindowsCredentialsAreBad}, Windows =
/// {UseWindowsCredentials}, default Credential), NO positional parameters, and the
/// $env:COMPUTERNAME default applied at construction exactly like PS bind-time defaults
/// (W1-087 class; engine conversion via LanguagePrimitives). Surface pinned by
/// migration/baselines/New-DbaCmConnection.json.
/// </summary>
[Cmdlet(VerbsCommon.New, "DbaCmConnection", SupportsShouldProcess = true, DefaultParameterSetName = "Credential")]
public sealed class NewDbaCmConnectionCommand : DbaBaseCmdlet
{
    /// <summary>The computer(s) to build connection objects for; defaults to this computer.</summary>
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

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // begin-scope $disable_cache, computed once and read by every process record
    // (the W3-001 lifecycle-state shape, single value instead of a bag).
    private object? _disableCache;

    protected override void BeginProcessing()
    {
        foreach (PSObject? item in NestedCommand.InvokeScoped(this, BeginScript,
            string.Join(", ", MyInvocation.BoundParameters.Keys), EnableException.ToBool(),
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
        {
            Hashtable? sentinel = item?.BaseObject as Hashtable;
            if (sentinel is not null && sentinel.ContainsKey("__w3063DisableCache"))
            {
                _disableCache = sentinel["__w3063DisableCache"];
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

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, ProcessScript,
            ComputerName, Credential, UseWindowsCredentials.ToBool(),
            OverrideExplicitCredential.ToBool(), DisabledConnectionTypes,
            DisableBadCredentialCache.ToBool(), DisableCimPersistence.ToBool(),
            DisableCredentialAutoRegister.ToBool(), EnableCredentialFailover.ToBool(),
            WindowsCredentialsAreBad.ToBool(), CimWinRMOptions, CimDCOMOptions,
            EnableException.ToBool(), _disableCache,
            TestBound(nameof(Credential)), TestBound(nameof(UseWindowsCredentials)),
            TestBound(nameof(OverrideExplicitCredential)), TestBound(nameof(DisabledConnectionTypes)),
            TestBound(nameof(DisableBadCredentialCache)), TestBound(nameof(DisableCimPersistence)),
            TestBound(nameof(DisableCredentialAutoRegister)), TestBound(nameof(EnableCredentialFailover)),
            TestBound(nameof(WindowsCredentialsAreBad)), TestBound(nameof(CimWinRMOptions)),
            TestBound(nameof(CimDCOMOptions)), this,
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

    // PS: the begin block verbatim. $PSBoundParameters.Keys -join ", " is carried as the
    // pre-joined key list from the cmdlet's own binding (identical keys for identical
    // invocations); $disable_cache returns through the sentinel for the process hops.
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

    Write-Message -Level InternalComment -Message "Starting execution" -FunctionName New-DbaCmConnection
    Write-Message -Level Verbose -Message "Bound parameters: $__boundKeys" -FunctionName New-DbaCmConnection

    $disable_cache = Get-DbatoolsConfigValue -Name 'ComputerManagement.Cache.Disable.All' -Fallback $false
    @{ __w3063DisableCache = $disable_cache }
} $__boundKeys $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";

    // PS: the process body VERBATIM per record. Substitutions only: Test-Bound "X" ->
    // carried $__boundX flags, $Pscmdlet -> $__realCmdlet, $disable_cache -> the
    // begin-computed carried value, and explicit -FunctionName New-DbaCmConnection on
    // Write-Message/Stop-Function (W1-090). Line 199's EnableCredentialFailover ->
    // DisableCredentialAutoRegister assignment is the SOURCE's own bug - verbatim.
    private const string ProcessScript = """
param($ComputerName, $Credential, $UseWindowsCredentials, $OverrideExplicitCredential, $DisabledConnectionTypes, $DisableBadCredentialCache, $DisableCimPersistence, $DisableCredentialAutoRegister, $EnableCredentialFailover, $WindowsCredentialsAreBad, $CimWinRMOptions, $CimDCOMOptions, $EnableException, $__disableCache, $__boundCredential, $__boundUseWindowsCredentials, $__boundOverrideExplicitCredential, $__boundDisabledConnectionTypes, $__boundDisableBadCredentialCache, $__boundDisableCimPersistence, $__boundDisableCredentialAutoRegister, $__boundEnableCredentialFailover, $__boundWindowsCredentialsAreBad, $__boundCimWinRMOptions, $__boundCimDCOMOptions, $__realCmdlet, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundWhatIf) { $__commonParameters.WhatIf = [bool]$__boundWhatIf }
if ($null -ne $__boundConfirm) { $__commonParameters.Confirm = [bool]$__boundConfirm }
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = "Medium")]
    param([Dataplat.Dbatools.Parameter.DbaCmConnectionParameter[]]$ComputerName, [PSCredential]$Credential, $UseWindowsCredentials, $OverrideExplicitCredential, [Dataplat.Dbatools.Connection.ManagementConnectionType]$DisabledConnectionTypes, $DisableBadCredentialCache, $DisableCimPersistence, $DisableCredentialAutoRegister, $EnableCredentialFailover, $WindowsCredentialsAreBad, [Microsoft.Management.Infrastructure.Options.WSManSessionOptions]$CimWinRMOptions, [Microsoft.Management.Infrastructure.Options.DComSessionOptions]$CimDCOMOptions, $EnableException, $__disableCache, $__boundCredential, $__boundUseWindowsCredentials, $__boundOverrideExplicitCredential, $__boundDisabledConnectionTypes, $__boundDisableBadCredentialCache, $__boundDisableCimPersistence, $__boundDisableCredentialAutoRegister, $__boundEnableCredentialFailover, $__boundWindowsCredentialsAreBad, $__boundCimWinRMOptions, $__boundCimDCOMOptions, $__realCmdlet, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    $disable_cache = $__disableCache

    foreach ($connectionObject in $ComputerName) {
        if ($__realCmdlet.ShouldProcess($($connectionObject.connection.computername), "Creating connection object")) {
            if (-not $connectionObject.Success) { Stop-Function -Message "Failed to interpret computername input: $($connectionObject.InputObject)" -Category InvalidArgument -Target $connectionObject.InputObject -Continue -FunctionName New-DbaCmConnection }
            Write-Message -Level VeryVerbose -Message "Processing computer: $($connectionObject.Connection.ComputerName)" -Target $connectionObject.Connection -FunctionName New-DbaCmConnection

            $connection = New-Object -TypeName Dataplat.Dbatools.Connection.ManagementConnection -ArgumentList $connectionObject.Connection.ComputerName
            if ($__boundCredential) { $connection.Credentials = $Credential }
            if ($__boundUseWindowsCredentials) {
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
            } else {
                $connection.CimWinRMOptions = New-DbaCimSessionOptionWithTimeout -Protocol Default
            }
            if ($__boundCimDCOMOptions) {
                $connection.CimDCOMOptions = $CimDCOMOptions
            } else {
                $connection.CimDCOMOptions = New-DbaCimSessionOptionWithTimeout -Protocol Dcom
            }

            if (-not $disable_cache) {
                Write-Message -Level Verbose -Message "Writing connection to cache" -FunctionName New-DbaCmConnection
                [Dataplat.Dbatools.Connection.ConnectionHost]::Connections[$connectionObject.Connection.ComputerName] = $connection
            } else { Write-Message -Level Verbose -Message "Skipping writing to cache, since the cache has been disabled." -FunctionName New-DbaCmConnection }
            $connection
        }
    }
} $ComputerName $Credential $UseWindowsCredentials $OverrideExplicitCredential $DisabledConnectionTypes $DisableBadCredentialCache $DisableCimPersistence $DisableCredentialAutoRegister $EnableCredentialFailover $WindowsCredentialsAreBad $CimWinRMOptions $CimDCOMOptions $EnableException $__disableCache $__boundCredential $__boundUseWindowsCredentials $__boundOverrideExplicitCredential $__boundDisabledConnectionTypes $__boundDisableBadCredentialCache $__boundDisableCimPersistence $__boundDisableCredentialAutoRegister $__boundEnableCredentialFailover $__boundWindowsCredentialsAreBad $__boundCimWinRMOptions $__boundCimDCOMOptions $__realCmdlet $__boundWhatIf $__boundConfirm $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
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

    Write-Message -Level InternalComment -Message "Stopping execution" -FunctionName New-DbaCmConnection
} $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
