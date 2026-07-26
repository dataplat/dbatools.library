#nullable enable

using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Copies SQL Server credentials between instances with their passwords intact. Port of
/// public/Copy-DbaCredential.ps1. The workflow stays a module-scoped PowerShell compatibility hop
/// because it leans on a dedicated admin connection, the private service-master-key decryption
/// helper, and the Get/New-DbaCredential pair, all of which keep engine semantics there. The
/// compiled cmdlet supplies the real ShouldProcess runtime.
/// </summary>
[Cmdlet(VerbsCommon.Copy, "DbaCredential",
    SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Medium)]
public sealed class CopyDbaCredentialCommand : DbaBaseCmdlet
{
    /// <summary>Source SQL Server instance. Requires sysadmin and a DAC unless passwords are excluded.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    public DbaInstanceParameter Source { get; set; } = null!;

    /// <summary>Alternative credential for the source instance.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SourceSqlCredential { get; set; }

    /// <summary>Windows credential used to reach the source OS when passwords are decrypted.</summary>
    [Parameter(Position = 2)]
    public PSCredential? Credential { get; set; }

    /// <summary>Destination SQL Server instances.</summary>
    [Parameter(Mandatory = true, Position = 3)]
    public DbaInstanceParameter[] Destination { get; set; } = null!;

    /// <summary>Alternative credential for destination instances.</summary>
    [Parameter(Position = 4)]
    public PSCredential? DestinationSqlCredential { get; set; }

    /// <summary>Only copy credentials with these names.</summary>
    [Parameter(Position = 5)]
    public string[]? Name { get; set; }

    /// <summary>Skip credentials with these names.</summary>
    [Parameter(Position = 6)]
    public string[]? ExcludeName { get; set; }

    /// <summary>Only copy credentials whose identity matches.</summary>
    [Parameter(Position = 7)]
    [Alias("CredentialIdentity")]
    public string[]? Identity { get; set; }

    /// <summary>Skip credentials whose identity matches.</summary>
    [Parameter(Position = 8)]
    [Alias("ExcludeCredentialIdentity")]
    public string[]? ExcludeIdentity { get; set; }

    /// <summary>Copy credential definitions without decrypting and carrying over the passwords.</summary>
    [Parameter]
    public SwitchParameter ExcludePassword { get; set; }

    /// <summary>Drop and recreate credentials that already exist on the destination.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        NestedCommand.InvokeScopedStreaming(this, item =>
        {
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                NestedCommand.RemoveDuplicateError(this, nestedError);
                WriteError(nestedError);
            }
            else
            {
                WriteObject(item);
            }
        }, BodyScript,
            Source, SourceSqlCredential, Credential, Destination, DestinationSqlCredential,
            Name, ExcludeName, Identity, ExcludeIdentity,
            ExcludePassword.ToBool(), Force.ToBool(), EnableException.ToBool(), this,
            NestedCommand.BoundCommonParameter(this, "Verbose"), NestedCommand.BoundCommonParameter(this, "Debug"));
    }

    private const string BodyScript = """
param($Source, $SourceSqlCredential, $Credential, $Destination, $DestinationSqlCredential, $Name, $ExcludeName, $Identity, $ExcludeIdentity, $ExcludePassword, $Force, $EnableException, $__realCmdlet, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter]$Source, $SourceSqlCredential, $Credential, [Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$Destination, $DestinationSqlCredential, [string[]]$Name, [string[]]$ExcludeName, [string[]]$Identity, [string[]]$ExcludeIdentity, $ExcludePassword, $Force, $EnableException, $__realCmdlet, $__boundVerbose, $__boundDebug)

    if (-not $script:isWindows) {
        Stop-Function -Message "Copy-DbaCredential is only supported on Windows" -FunctionName Copy-DbaCredential
        return
    }

    if ($Force) { $ConfirmPreference = 'none' }

    try {
        # Do we need a dedicated admin connection to the source for password retrieval?
        # If passwords are excluded, we don't need a DAC
        if ($ExcludePassword) { $dacNeeded = $false } else { $dacNeeded = $true }

        # Do we have a dedicated admin connection already?
        $dacConnected = $Source.Type -eq 'Server' -and $Source.InputObject.Name -match '^ADMIN:'

        $dacOpened = $false
        if ($dacNeeded) {
            if ($dacConnected) {
                Write-Message -Level Verbose -Message "Reusing dedicated admin connection for password retrieval." -FunctionName Copy-DbaCredential -ModuleName "dbatools"
                $sourceServer = $Source.InputObject
            } else {
                Write-Message -Level Verbose -Message "Opening dedicated admin connection for password retrieval." -FunctionName Copy-DbaCredential -ModuleName "dbatools"
                $sourceServer = Connect-DbaInstance -SqlInstance $Source -SqlCredential $SourceSqlCredential -MinimumVersion 9 -DedicatedAdminConnection -WarningAction SilentlyContinue
                $dacOpened = $true
            }
        } else {
            Write-Message -Level Verbose -Message "Opening or reusing normal connection because passwords are excluded." -FunctionName Copy-DbaCredential -ModuleName "dbatools"
            $sourceServer = Connect-DbaInstance -SqlInstance $Source -SqlCredential $SourceSqlCredential -MinimumVersion 9
        }
    } catch {
        Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $Source -FunctionName Copy-DbaCredential
        return
    }

    try {
    if (-not $ExcludePassword) {
        Write-Message -Level Verbose -Message "Decrypting all Credential logins and passwords on $($sourceServer.Name)" -FunctionName Copy-DbaCredential -ModuleName "dbatools"
        try {
            $decryptedCredentials = Get-DecryptedObject -SqlInstance $sourceServer -Credential $Credential -Type Credential -EnableException
        } catch {
            Stop-Function -Message "Failed to decrypt credentials on $($sourceServer.Name)" -ErrorRecord $_ -FunctionName Copy-DbaCredential
            return
        }
    }

    Write-Message -Level Verbose -Message "Getting all Credentials that should be processed on $($sourceServer.Name)" -FunctionName Copy-DbaCredential -ModuleName "dbatools"
    $credentialList = Get-DbaCredential -SqlInstance $sourceServer -Name $Name -ExcludeName $ExcludeName -Identity $Identity -ExcludeIdentity $ExcludeIdentity

    if (Test-FunctionInterrupt) { return }

    foreach ($destinstance in $Destination) {
        try {
            $destServer = Connect-DbaInstance -SqlInstance $destinstance -SqlCredential $DestinationSqlCredential -MinimumVersion 9
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $destinstance -Continue -FunctionName Copy-DbaCredential
        }

        Write-Message -Level Verbose -Message "Starting migration" -FunctionName Copy-DbaCredential -ModuleName "dbatools"
        $destServer.Credentials.Refresh()
        foreach ($cred in $credentialList) {
            $credentialName = $cred.Name

            $copyCredentialStatus = [PSCustomObject]@{
                SourceServer      = $sourceServer.DomainInstanceName
                DestinationServer = $destServer.DomainInstanceName
                Type              = "Credential"
                Name              = $credentialName
                Status            = $null
                Notes             = $null
                DateTime          = [Dataplat.Dbatools.Utility.DbaDateTime](Get-Date)
            }

            if ($null -ne $destServer.Credentials[$credentialName]) {
                if (!$force) {
                    $copyCredentialStatus.Status = "Skipping"
                    $copyCredentialStatus.Notes = "Already exists on destination"
                    if ($__realCmdlet.ShouldProcess($destServer.Name, "Skipping $credentialName, already exists")) {
                        $copyCredentialStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                    }
                    continue
                } else {
                    if ($__realCmdlet.ShouldProcess($destinstance, "Dropping $credentialName")) {
                        try {
                            $destServer.Credentials[$credentialName].Drop()
                        } catch {
                            $copyCredentialStatus.Status = "Failed"
                            $copyCredentialStatus.Notes = "$PSItem"
                            Write-Message -Level Verbose -Message "Issue dropping $credentialName on $destinstance | $PSItem" -FunctionName Copy-DbaCredential -ModuleName "dbatools"
                            continue
                        }
                    }
                }
            }

            Write-Message -Level Verbose -Message "Attempting to migrate $credentialName" -FunctionName Copy-DbaCredential -ModuleName "dbatools"
            try {
                $splatNewCredential = @{
                    SqlInstance     = $destServer
                    Name            = $cred.Name
                    Identity        = $cred.Identity
                    MappedClassType = $cred.MappedClassType
                    EnableException = $true
                }
                if ($cred.mappedClassType -eq "CryptographicProvider") {
                    $cryptoConfiguredOnDestination = $destServer.Query("SELECT is_enabled FROM sys.cryptographic_providers WHERE name = '$($cred.ProviderName)'")
                    if (-not $cryptoConfiguredOnDestination.is_enabled) {
                        throw "The cryptographic provider $($cred.ProviderName) needs to be configured and enabled on $destServer"
                    }
                    $splatNewCredential.ProviderName = $cred.ProviderName
                }
                if (-not $ExcludePassword) {
                    $decryptedCred = $decryptedCredentials | Where-Object { $_.Name -eq $credentialName }
                    $splatNewCredential.SecurePassword = ConvertTo-SecureString -String $decryptedCred.Password -AsPlainText -Force
                }

                if ($__realCmdlet.ShouldProcess($destinstance, "Copying $identity ($credentialName)")) {
                    $null = New-DbaCredential @splatNewCredential
                    Write-Message -Level Verbose -Message "$credentialName successfully copied" -FunctionName Copy-DbaCredential -ModuleName "dbatools"
                    $copyCredentialStatus.Status = "Successful"
                    $copyCredentialStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                }
            } catch {
                $copyCredentialStatus.Status = "Failed"
                $copyCredentialStatus.Notes = "$PSItem"
                $copyCredentialStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                Write-Message -Level Verbose -Message "Issue creating $credentialName on $destinstance | $PSItem" -FunctionName Copy-DbaCredential -ModuleName "dbatools"
                continue
            }
        }
    }

    } finally {
        if ($dacOpened) {
            $null = $sourceServer | Disconnect-DbaInstance -WhatIf:$false
        }
    }
} $Source $SourceSqlCredential $Credential $Destination $DestinationSqlCredential $Name $ExcludeName $Identity $ExcludeIdentity $ExcludePassword $Force $EnableException $__realCmdlet $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
