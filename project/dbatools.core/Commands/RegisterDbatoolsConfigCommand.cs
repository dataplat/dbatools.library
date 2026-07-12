#nullable enable

using System.Collections.Generic;
using System.Management.Automation;
using Dataplat.Dbatools.Configuration;
using Dataplat.Dbatools.Message;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Registers configuration items for persistence (registry scopes on Windows, psf_config
/// json files otherwise/for file scopes). Port of public/Register-DbatoolsConfig.ps1
/// (W1-032). The REGISTRY branch runs VERBATIM module-scoped per process call (the
/// Write-Config/Ensure-RegistryPath helpers ride along, so the $script:path_Registry*
/// module variables, Get-PSCallStack FunctionName defaulting, and Stop-Function shapes
/// match the function exactly); the FILE branch accumulates Config items natively across
/// the pipeline (the PS -notcontains dedup is case-insensitive) and the end block writes
/// through the private Write-DbatoolsConfigFile inside a module hop, with the
/// $script:path_File* variables resolved there verbatim. The begin block's
/// $script:NoRegistry guard and scope redirections run natively (the module variable is
/// read live). Only Module/Name carry positions (explicit in the source; explicit
/// positions disable implicit numbering for the rest - baseline-pinned).
/// Surface pinned by migration/baselines/Register-DbatoolsConfig.json.
/// </summary>
[Cmdlet(VerbsLifecycle.Register, "DbatoolsConfig", DefaultParameterSetName = "Default")]
public sealed class RegisterDbatoolsConfigCommand : DbaBaseCmdlet
{
    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    [Parameter(ParameterSetName = "Default", ValueFromPipeline = true)]
    public Config[]? Config { get; set; }

    [Parameter(ParameterSetName = "Default", ValueFromPipeline = true)]
    public string[]? FullName { get; set; }

    [Parameter(Mandatory = true, ParameterSetName = "Name", Position = 0)]
    public string? Module { get; set; }

    [Parameter(ParameterSetName = "Name", Position = 1)]
    public string? Name { get; set; } = "*";

    [Parameter]
    public ConfigScope Scope { get; set; } = ConfigScope.UserDefault;

    private readonly List<Config> _configurationItems = new();

    protected override void BeginProcessing()
    {
        // PS: $script:NoRegistry (module variable, live read)
        bool noRegistry = LanguagePrimitives.IsTrue(GetModuleVariable("NoRegistry"));
        if (noRegistry && (((int)Scope) & 14) != 0)
        {
            StopFunction("Cannot register configurations on non-windows machines to registry. Please specify a file-based scope", tag: new[] { "NotSupported" }, category: ErrorCategory.NotImplemented);
            return;
        }

        // Linux and MAC default to local user store file
        if (noRegistry && Scope == ConfigScope.UserDefault)
            Scope = ConfigScope.FileUserLocal;
        // Linux and MAC get redirection for SystemDefault to FileSystem
        if (noRegistry && Scope == ConfigScope.SystemDefault)
            Scope = ConfigScope.FileSystem;
    }

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        //region Registry Based
        if ((((int)Scope) & 15) != 0)
        {
            NestedCommand.InvokeScoped(this, RegistryWriteScript,
                ParameterSetName, Config, FullName, Module, Name, Scope, EnableException.ToBool());
        }
        //endregion Registry Based

        //region File Based
        else
        {
            if (ParameterSetName == "Default")
            {
                if (Config is not null)
                {
                    foreach (Config item in Config)
                    {
                        if (!ContainsFullName(item.FullName))
                            _configurationItems.Add(item);
                    }
                }

                if (FullName is not null)
                {
                    foreach (string item in FullName)
                    {
                        if (!ContainsFullName(item) && ConfigurationHost.Configurations.ContainsKey(item.ToLowerInvariant()))
                            _configurationItems.Add(ConfigurationHost.Configurations[item.ToLowerInvariant()]);
                    }
                }
            }
            else if (ParameterSetName == "Name")
            {
                // PS: ...Configurations.Values | Where-Object Module -EQ $Module | Where-Object Name -Like $Name
                WildcardPattern namePattern = new(Name ?? "*", WildcardOptions.IgnoreCase);
                foreach (Config item in ConfigurationHost.Configurations.Values)
                {
                    if (PsString.Eq(item.Module, Module) && namePattern.IsMatch(item.Name))
                    {
                        if (!ContainsFullName(item.FullName))
                            _configurationItems.Add(item);
                    }
                }
            }
        }
        //endregion File Based
    }

    protected override void EndProcessing()
    {
        if (Interrupted)
            return;

        //region Finish File Based Persistence (verbatim end block inside the module hop)
        if ((((int)Scope) & (16 | 32 | 64)) != 0)
            NestedCommand.InvokeScoped(this, FileWriteScript, _configurationItems.ToArray(), Scope);
        //endregion Finish File Based Persistence
    }

    /// <summary>PS: $configurationItems.FullName -notcontains ... (case-insensitive).</summary>
    private bool ContainsFullName(string? fullName)
    {
        foreach (Config existing in _configurationItems)
        {
            if (PsString.Eq(existing.FullName, fullName))
                return true;
        }
        return false;
    }

    /// <summary>Reads a $script:-scoped variable LIVE off the dbatools script module.</summary>
    private object? GetModuleVariable(string variableName)
    {
        System.Collections.Hashtable getModuleParams = new();
        getModuleParams["Name"] = "dbatools";
        foreach (PSObject wrapped in NestedCommand.Invoke(this, "Get-Module", getModuleParams))
        {
            if (wrapped?.BaseObject is PSModuleInfo module && module.ModuleType == ModuleType.Script)
                return module.SessionState.PSVariable.GetValue("script:" + variableName);
        }
        return null;
    }

    // The Write-Config/Ensure-RegistryPath helpers and the registry switch are VERBATIM
    // from the function (comments included); $EnableException is a named parameter so the
    // helper calls pass it exactly like the function did.
    private const string RegistryWriteScript = """
param($parSet, $Config, $FullName, $Module, $Name, $Scope, $EnableException)
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    param($parSet, $Config, $FullName, $Module, $Name, $Scope, $EnableException)

    function Write-Config {
        [CmdletBinding()]
        Param (
            [Dataplat.Dbatools.Configuration.Config]
            $Config,

            [Dataplat.Dbatools.Configuration.ConfigScope]
            $Scope,

            [bool]
            $EnableException,

            [string]
            $FunctionName = (Get-PSCallStack)[0].Command
        )

        if (-not $Config -or ($Config.RegistryData -eq "<type not supported>")) {
            Stop-Function -Message "Invalid Input, cannot export $($Config.FullName), type not supported" -EnableException $EnableException -Category InvalidArgument -Tag "config", "fail" -Target $Config -FunctionName $FunctionName
            return
        }

        try {
            Write-Message -Level Verbose -Message "Registering $($Config.FullName) for $Scope" -Tag "Config" -Target $Config -FunctionName $FunctionName
            #region User Default
            if (1 -band $Scope) {
                Ensure-RegistryPath -Path $script:path_RegistryUserDefault -ErrorAction Stop
                Set-ItemProperty -Path $script:path_RegistryUserDefault -Name $Config.FullName -Value $Config.RegistryData -ErrorAction Stop
            }
            #endregion User Default

            #region User Mandatory
            if (2 -band $Scope) {
                Ensure-RegistryPath -Path $script:path_RegistryUserEnforced -ErrorAction Stop
                Set-ItemProperty -Path $script:path_RegistryUserEnforced -Name $Config.FullName -Value $Config.RegistryData -ErrorAction Stop
            }
            #endregion User Mandatory

            #region System Default
            if (4 -band $Scope) {
                Ensure-RegistryPath -Path $script:path_RegistryMachineDefault -ErrorAction Stop
                Set-ItemProperty -Path $script:path_RegistryMachineDefault -Name $Config.FullName -Value $Config.RegistryData -ErrorAction Stop
            }
            #endregion System Default

            #region System Mandatory
            if (8 -band $Scope) {
                Ensure-RegistryPath -Path $script:path_RegistryMachineEnforced -ErrorAction Stop
                Set-ItemProperty -Path $script:path_RegistryMachineEnforced -Name $Config.FullName -Value $Config.RegistryData -ErrorAction Stop
            }
            #endregion System Mandatory
        } catch {
            Stop-Function -Message "Failed to export $($Config.FullName), to scope $Scope" -EnableException $EnableException -Tag "config", "fail" -Target $Config -ErrorRecord $_ -FunctionName $FunctionName
            return
        }
    }

    function Ensure-RegistryPath {
        [Diagnostics.CodeAnalysis.SuppressMessageAttribute("PSUseApprovedVerbs", "")]
        [CmdletBinding()]
        Param (
            [string]
            $Path
        )

        if (-not (Test-Path $Path)) {
            $null = New-Item $Path -Force
        }
    }

    switch ($parSet) {
        "Default" {
            foreach ($item in $Config) {
                Write-Config -Config $item -Scope $Scope -EnableException $EnableException
            }

            foreach ($item in $FullName) {
                if ([Dataplat.Dbatools.Configuration.ConfigurationHost]::Configurations.ContainsKey($item.ToLowerInvariant())) {
                    Write-Config -Config ([Dataplat.Dbatools.Configuration.ConfigurationHost]::Configurations[$item.ToLowerInvariant()]) -Scope $Scope -EnableException $EnableException
                }
            }
        }
        "Name" {
            foreach ($item in ([Dataplat.Dbatools.Configuration.ConfigurationHost]::Configurations.Values | Where-Object Module -EQ $Module | Where-Object Name -Like $Name)) {
                Write-Config -Config $item -Scope $Scope -EnableException $EnableException
            }
        }
    }
} $parSet $Config $FullName $Module $Name $Scope $EnableException 3>&1
""";

    private const string FileWriteScript = """
param($configurationItems, $Scope)
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    param($configurationItems, $Scope)
    if ($Scope -band 16) {
        Write-DbatoolsConfigFile -Config $configurationItems -Path (Join-Path $script:path_FileUserLocal "psf_config.json")
    }
    if ($Scope -band 32) {
        Write-DbatoolsConfigFile -Config $configurationItems -Path (Join-Path $script:path_FileUserShared "psf_config.json")
    }
    if ($Scope -band 64) {
        Write-DbatoolsConfigFile -Config $configurationItems -Path (Join-Path $script:path_FileSystem "psf_config.json")
    }
} $configurationItems $Scope 3>&1
""";
}
