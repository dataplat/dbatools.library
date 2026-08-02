#nullable enable

namespace Dataplat.Dbatools.Commands;

/// <summary>The embedded hop scripts for Set-DbaCmConnection (W3-087) - split from
/// the main class file per the repository 400-line limit (codex r4; the DEF-007 fix
/// and its measured-semantics comments pushed the file over).</summary>
public sealed partial class SetDbaCmConnectionCommand
{
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

    Write-Message -Level InternalComment -Message "Starting execution" -FunctionName Set-DbaCmConnection -ModuleName "dbatools"
    Write-Message -Level Verbose -Message "Bound parameters: $__boundKeys" -FunctionName Set-DbaCmConnection -ModuleName "dbatools"

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
            Write-Message -Level VeryVerbose -Message "Processing computer: $($connectionObject.Connection.ComputerName)" -FunctionName Set-DbaCmConnection -ModuleName "dbatools"

            $connection = $connectionObject.Connection

            if ($ResetConfiguration) {
                Write-Message -Level Verbose -Message "Resetting the configuration to system default" -FunctionName Set-DbaCmConnection -ModuleName "dbatools"

                $connection.RestoreDefaultConfiguration()
            }

            if ($ResetConnectionStatus) {
                Write-Message -Level Verbose -Message "Resetting the connection status" -FunctionName Set-DbaCmConnection -ModuleName "dbatools"

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
                Write-Message -Level Verbose -Message "Resetting credentials" -FunctionName Set-DbaCmConnection -ModuleName "dbatools"

                $connection.KnownBadCredentials.Clear()
                $connection.Credentials = $null
                $connection.UseWindowsCredentials = $false
                $connection.WindowsCredentialsAreBad = $false
            } else {
                if ($ClearBadCredential) {
                    Write-Message -Level Verbose -Message "Clearing bad credentials" -FunctionName Set-DbaCmConnection -ModuleName "dbatools"

                    $connection.KnownBadCredentials.Clear()
                    $connection.WindowsCredentialsAreBad = $false
                }

                if ($ClearCredential) {
                    Write-Message -Level Verbose -Message "Clearing credentials" -FunctionName Set-DbaCmConnection -ModuleName "dbatools"

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
                Write-Message -Level Verbose -Message "Writing connection to cache" -FunctionName Set-DbaCmConnection -ModuleName "dbatools"
                [Dataplat.Dbatools.Connection.ConnectionHost]::Connections[$connectionObject.Connection.ComputerName] = $connection
            } else { Write-Message -Level Verbose -Message "Skipping writing to cache, since the cache has been disabled." -FunctionName Set-DbaCmConnection -ModuleName "dbatools" }
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

    Write-Message -Level InternalComment -Message "Stopping execution" -FunctionName Set-DbaCmConnection -ModuleName "dbatools"
} $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
