#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Creates SQL Server credentials on target instances. Port of public/New-DbaCredential.ps1
/// (W3-064). The process body rides one VERBATIM module hop per record; the begin-block
/// lines are pure functions of bound parameters and ride verbatim at the hop top - INCLUDING
/// `if ($Force) { $ConfirmPreference = 'none' }`, which is why the ShouldProcess gates use
/// the INNER scriptblock's own $Pscmdlet under its own [CmdletBinding(SupportsShouldProcess,
/// ConfirmImpact = "Medium")] binding with the carried -WhatIf/-Confirm preferences (the
/// W3-005 CopyDbaRegServerCommand Copy-family convention: fn-scope ConfirmPreference
/// suppression cannot ride the REAL cmdlet's gate, and the inner binding inherits carriers
/// exactly as fn scope inheritance did). The Name parameter's cross-parameter default
/// (= $Identity) is applied once at binding (W1-087/W3-002 class). SecureString rides the
/// hop as the live object - never flattened. SOURCE QUIRK PRESERVED VERBATIM: the create-
/// catch message interpolates $cred, a variable this function never defines (renders an
/// empty token). Surface pinned by migration/baselines/New-DbaCredential.json (implicit
/// positions 0-6, Identity mandatory pos3 alias CredentialIdentity, SecurePassword pos4
/// alias Password, MappedClassType ValidateSet pos5, ConfirmImpact Medium).
/// </summary>
[Cmdlet(VerbsCommon.New, "DbaCredential", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Medium)]
public sealed class NewDbaCredentialCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Alternative credential for the target instances.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The credential name; defaults to the Identity value.</summary>
    [Parameter(Position = 2)]
    public string? Name { get; set; }

    /// <summary>The identity the credential maps to.</summary>
    [Parameter(Mandatory = true, Position = 3)]
    [Alias("CredentialIdentity")]
    public string Identity { get; set; } = null!;

    /// <summary>The password as a SecureString; omitted creates a passwordless credential.</summary>
    [Parameter(Position = 4)]
    [Alias("Password")]
    public System.Security.SecureString? SecurePassword { get; set; }

    /// <summary>Maps the credential to a cryptographic provider class.</summary>
    [Parameter(Position = 5)]
    [PsStringCast]
    [ValidateSet("CryptographicProvider", "None")]
    public string MappedClassType { get; set; } = "None";

    /// <summary>The cryptographic provider name when mapped.</summary>
    [Parameter(Position = 6)]
    public string? ProviderName { get; set; }

    /// <summary>Drops and recreates an existing credential; suppresses confirmation.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // PS: parameter defaults apply once at binding; unbound $Name reads the bound
    // $Identity (cross-parameter default, W1-087/W3-002 class). Identity is mandatory and
    // not pipeline-bound, so the value is constant across records.
    private object? _nameState;
    private bool _bindInitialized;

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        if (!_bindInitialized)
        {
            _nameState = TestBound(nameof(Name)) ? (object?)(Name ?? "") : Identity;
            _bindInitialized = true;
        }

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, ProcessScript,
            SqlInstance, SqlCredential, _nameState, Identity, SecurePassword,
            MappedClassType, ProviderName ?? "", Force.ToBool(), EnableException.ToBool(),
            BoundCommonParameter("WhatIf"), BoundCommonParameter("Confirm"),
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug"),
            BoundRaw("WarningAction")))
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

    /// <summary>The raw bound value (or null when unbound) - the -WarningAction carrier
    /// keeps the caller's preference exactly (codex W3-002 F3 class, all hops).</summary>
    private object? BoundRaw(string name)
    {
        if (MyInvocation.BoundParameters.TryGetValue(name, out object? value))
            return value;
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

    // PS: the begin-block lines (Force -> ConfirmPreference suppression + the $mappedClass
    // switch, both pure functions of bound params) ride verbatim at the hop top, then the
    // process body VERBATIM. The ShouldProcess gates use the INNER block's own $Pscmdlet
    // (W3-005 Copy-family convention - see the class doc); the only other substitutions are
    // -FunctionName New-DbaCredential on Stop-Function/Write-Message (W1-090). The
    // create-catch's $cred interpolation is the SOURCE's own undefined-variable quirk -
    // verbatim.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Name, $Identity, $SecurePassword, $MappedClassType, $ProviderName, $Force, $EnableException, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug, $__boundWarningAction)
$__commonParameters = @{}
if ($null -ne $__boundWarningAction) { $__commonParameters.WarningAction = $__boundWarningAction }
if ($null -ne $__boundWhatIf) { $__commonParameters.WhatIf = [bool]$__boundWhatIf }
if ($null -ne $__boundConfirm) { $__commonParameters.Confirm = [bool]$__boundConfirm }
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = "Medium")]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [string]$Name, [string]$Identity, [Security.SecureString]$SecurePassword, [string]$MappedClassType, [string]$ProviderName, $Force, $EnableException, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug, $__boundWarningAction)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    if ($Force) { $ConfirmPreference = 'none' }

    $mappedClass = switch ($MappedClassType) {
        "CryptographicProvider" { 1 }
        "None" { 0 }
    }

    foreach ($instance in $SqlInstance) {
        try {
            $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName New-DbaCredential
        }

        $currentCred = $server.Credentials[$Name]

        if ($currentCred) {
            if ($force) {
                Write-Message -Level Verbose -Message "Dropping credential $Name" -FunctionName New-DbaCredential
                try {
                    if ($Pscmdlet.ShouldProcess($SqlInstance, "Dropping credential '$Name' on $instance")) {
                        $currentCred.Drop()
                    }
                } catch {
                    Stop-Function -Message "Error dropping credential $Name" -Target $name -Continue -FunctionName New-DbaCredential
                }
            } else {
                Stop-Function -Message "Credential exists and Force was not specified" -Target $Name -Continue -FunctionName New-DbaCredential
            }
        }

        if ($Pscmdlet.ShouldProcess($SqlInstance, "Creating credential '$Name' on $instance")) {
            try {
                $instancecredential = New-Object Microsoft.SqlServer.Management.Smo.Credential -ArgumentList $server, $Name
                try {
                    $instancecredential.MappedClassType = $mappedClass
                } catch {
                    Add-Member -Force -InputObject $instancecredential -MemberType NoteProperty -Name MappedClassType -Value $mappedClass
                }
                $instancecredential.ProviderName = $ProviderName
                if ($SecurePassword) {
                    Write-Message -Level Verbose -Message "Creating credential with identity '$Identity' with password" -FunctionName New-DbaCredential
                    $instancecredential.Create($Identity, $SecurePassword)
                } else {
                    Write-Message -Level Verbose -Message "Password was not provided, creating credential with identity '$Identity' without password" -FunctionName New-DbaCredential
                    $instancecredential.Create($Identity)
                }

                Add-Member -Force -InputObject $instancecredential -MemberType NoteProperty -Name ComputerName -value $server.ComputerName
                Add-Member -Force -InputObject $instancecredential -MemberType NoteProperty -Name InstanceName -value $server.ServiceName
                Add-Member -Force -InputObject $instancecredential -MemberType NoteProperty -Name SqlInstance -value $server.DomainInstanceName

                Select-DefaultView -InputObject $instancecredential -Property ComputerName, InstanceName, SqlInstance, Name, Identity, CreateDate, MappedClassType, ProviderName
            } catch {
                Stop-Function -Message "Failed to create credential in $cred on $instance" -Target $instancecredential -InnerErrorRecord $_ -Continue -FunctionName New-DbaCredential
            }
        }
    }
} $SqlInstance $SqlCredential $Name $Identity $SecurePassword $MappedClassType $ProviderName $Force $EnableException $__boundWhatIf $__boundConfirm $__boundVerbose $__boundDebug $__boundWarningAction @__commonParameters 3>&1 2>&1
""";
}
