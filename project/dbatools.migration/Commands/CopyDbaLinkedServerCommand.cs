#nullable enable

using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Copies linked servers and their remote-login passwords between instances. Port of
/// public/Copy-DbaLinkedServer.ps1. The workflow stays a module-scoped PowerShell compatibility
/// hop because it leans on a dedicated admin connection, the private service-master-key
/// decryption helper, Test-SqlSa and Test-ElevationRequirement, and on SMO LinkedServer.Script()
/// plus Server.Query - all of which keep engine semantics where they were. The compiled cmdlet
/// supplies the real ShouldProcess runtime. Surface pinned by
/// migration/baselines/Copy-DbaLinkedServer.json.
/// </summary>
[Cmdlet(VerbsCommon.Copy, "DbaLinkedServer", DefaultParameterSetName = "Default",
    SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Medium)]
public sealed class CopyDbaLinkedServerCommand : DbaBaseCmdlet
{
    /// <summary>Source SQL Server instance. Requires sysadmin on both SQL Server and Windows.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    public DbaInstanceParameter Source { get; set; } = null!;

    /// <summary>Alternative credential for the source instance.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SourceSqlCredential { get; set; }

    /// <summary>Destination SQL Server instances.</summary>
    [Parameter(Mandatory = true, Position = 2)]
    public DbaInstanceParameter[] Destination { get; set; } = null!;

    /// <summary>Alternative credential for destination instances.</summary>
    [Parameter(Position = 3)]
    public PSCredential? DestinationSqlCredential { get; set; }

    /// <summary>Windows credential used to reach the source OS when passwords are decrypted.</summary>
    [Parameter(Position = 4)]
    public PSCredential? Credential { get; set; }

    /// <summary>Only copy the linked servers with these names.</summary>
    [Parameter(Position = 5)]
    public object[]? LinkedServer { get; set; }

    /// <summary>Skip the linked servers with these names. Ignored when LinkedServer is supplied.</summary>
    [Parameter(Position = 6)]
    public object[]? ExcludeLinkedServer { get; set; }

    /// <summary>Rewrite SQLNCLI providers to the newest one present on the destination.</summary>
    [Parameter]
    public SwitchParameter UpgradeSqlClient { get; set; }

    /// <summary>Copy linked server definitions without decrypting and carrying over the passwords.</summary>
    [Parameter]
    public SwitchParameter ExcludePassword { get; set; }

    /// <summary>Drop and recreate linked servers that already exist on the destination.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // Begin, process and end merge into one hop, and there are no carried locals: no parameter
    // takes pipeline input, so ProcessRecord runs exactly once and nothing can survive between
    // records. The merge is what keeps $dacOpened, $sourceServer and the Copy-DbaLinkedServers
    // helper reachable from the destination loop, the way $ConfirmPreference is. Test-FunctionInterrupt
    // still earns its line inside the script: the begin half's elevation test and source connect have
    // to stop the destination loop that follows them in the same invocation.
    protected override void ProcessRecord()
    {
        NestedCommand.InvokeScopedStreaming(this, item =>
        {
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                NestedCommand.RemoveDuplicateError(this, nestedError);
                WriteError(NestedCommand.PreserveErrorIdentity(nestedError));
            }
            else
            {
                WriteObject(item);
            }
        }, BodyScript,
            Source, SourceSqlCredential, Destination, DestinationSqlCredential, Credential,
            LinkedServer, ExcludeLinkedServer, UpgradeSqlClient.ToBool(), ExcludePassword.ToBool(),
            Force.ToBool(), EnableException.ToBool(), this,
            NestedCommand.BoundCommonParameter(this, "Verbose"),
            NestedCommand.BoundCommonParameter(this, "Debug"));
    }

    private const string BodyScript = """
param($Source, $SourceSqlCredential, $Destination, $DestinationSqlCredential, $Credential, $LinkedServer, $ExcludeLinkedServer, $UpgradeSqlClient, $ExcludePassword, $Force, $EnableException, $__realCmdlet, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    # The three flags are deliberately untyped: PowerShell excludes [switch] parameters from
    # positional binding, so one typed flag would shift every argument after it. They arrive as
    # real booleans, which the truthiness tests below read the way a switch reads.
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter]$Source, $SourceSqlCredential, [Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$Destination, $DestinationSqlCredential, $Credential, [object[]]$LinkedServer, [object[]]$ExcludeLinkedServer, $UpgradeSqlClient, $ExcludePassword, $Force, $EnableException, $__realCmdlet)

    if (-not $script:isWindows) {
        Stop-Function -Message "Copy-DbaLinkedServer is only supported on Windows" -FunctionName Copy-DbaLinkedServer
        return
    }
    $null = Test-ElevationRequirement -ComputerName $Source.ComputerName

    if ($Force) { $ConfirmPreference = 'none' }

    function Copy-DbaLinkedServers {
        param (
            [string[]]$LinkedServer,
            [bool]$force
        )

        Write-Message -Level Verbose -Message "Collecting Linked Server logins and passwords on $($sourceServer.Name)."
        if ($ExcludePassword) {
            $sourcelogins = @()
            foreach ($svr in $sourceServer.LinkedServers) {
                $sourcelogins += [PSCustomObject]@{
                    Name     = $sourcelogin.Name
                    Identity = $sourcelogin.LinkedServerLogins.RemoteUser
                    Password = $null
                }
            }
        } else {
            $sourcelogins = Get-DecryptedObject -SqlInstance $sourceServer -Credential $Credential -Type LinkedServer -EnableException:$EnableException
        }

        $serverlist = $sourceServer.LinkedServers

        if ($LinkedServer) {
            $serverlist = $serverlist | Where-Object Name -In $LinkedServer
        }
        if ($ExcludeLinkedServer) {
            $serverList = $serverlist | Where-Object Name -NotIn $ExcludeLinkedServer
        }

        foreach ($currentLinkedServer in $serverlist) {
            $provider = $currentLinkedServer.ProviderName
            try {
                $destServer.LinkedServers.Refresh()
                $destServer.LinkedServers.LinkedServerLogins.Refresh()
            } catch {
                #here to avoid an empty catch
                $null = 1
            }

            $linkedServerName = $currentLinkedServer.Name
            $linkedServerProductName = $currentLinkedServer.ProductName
            $linkedServerDataSource = $currentLinkedServer.DataSource

            $copyLinkedServer = [PSCustomObject]@{
                SourceServer      = $sourceServer.DomainInstanceName
                DestinationServer = $destServer.DomainInstanceName
                Name              = $linkedServerName
                ProductName       = $linkedServerProductName
                DataSource        = $linkedServerDataSource
                Type              = "Linked Server"
                Status            = $null
                Notes             = $provider
                DateTime          = [Dataplat.Dbatools.Utility.DbaDateTime](Get-Date)
            }

            # This does a check to warn of missing OleDbProviderSettings but should only be checked on SQL on Windows
            if ($destServer.Settings.OleDbProviderSettings.Name.Length -ne 0) {
                if ($destServer.VersionMajor -ge 17 -and $provider -eq "MSOLEDBSQL") {
                    # Starting with SQL Server 2025 (17.x), MSOLEDBSQL uses Microsoft OLE DB Driver version 19, which adds support for TDS 8.0. However, this driver introduces a breaking change. You must now specify the encrypt parameter.
                    Write-Message -Level Verbose -Message "Upgrading provider from MSOLEDBSQL to MSOLEDBSQL19 to ensure compatibility with SQL Server 2025+."
                    $provider = "MSOLEDBSQL19"
                }
                if (-not ($destServer.Settings.OleDbProviderSettings.Name -contains $provider) -and -not ($provider.StartsWith("SQLN"))) {
                    if ($__realCmdlet.ShouldProcess($destinstance, "$($destServer.Name) does not support the $provider provider. Skipping $linkedServerName.")) {
                        $copyLinkedServer.Status = "Skipped"
                        $copyLinkedServer.Notes = "Missing provider"
                        $copyLinkedServer | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                        Write-Message -Level Verbose -Message "$($destServer.Name) does not support the $provider provider. Skipping $linkedServerName."
                    }
                    continue
                }
            }

            if ($null -ne $destServer.LinkedServers[$linkedServerName]) {
                if (!$force) {
                    if ($__realCmdlet.ShouldProcess($destinstance, "Linked server $linkedServerName exists on $($destServer.Name)")) {
                        $copyLinkedServer.Status = "Skipped"
                        $copyLinkedServer.Notes = "Already exists on destination"
                        $copyLinkedServer | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                        Write-Message -Level Verbose -Message "Linked server $linkedServerName exists on $($destServer.Name)."
                    }
                    continue
                } else {
                    if ($__realCmdlet.ShouldProcess($destinstance, "Dropping $linkedServerName")) {
                        try {
                            if ($currentLinkedServer.Name -eq 'repl_distributor') {
                                Write-Message -Level Verbose -Message "repl_distributor cannot be dropped. Not going to try."
                                continue
                            }
                            $destServer.LinkedServers[$linkedServerName].Drop($true)
                            $destServer.LinkedServers.refresh()
                        } catch {
                            $copyLinkedServer.Status = "Failed"
                            $copyLinkedServer.Notes = "Issue dropping linked server $linkedServerName on $destinstance | $PSItem"
                            $copyLinkedServer | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                            Write-Message -Level Verbose -Message "Issue dropping linked server $linkedServerName on $destinstance | $PSItem"
                            continue
                        }
                    }
                }
            }

            Write-Message -Level Verbose -Message "Attempting to migrate: $linkedServerName."
            If ($__realCmdlet.ShouldProcess($destinstance, "Migrating $linkedServerName")) {
                try {
                    $sql = $currentLinkedServer.Script() | Out-String
                    Write-Message -Level Debug -Message $sql

                    if ($UpgradeSqlClient -and $sql -match "sqlncli") {
                        $destProviders = $destServer.Settings.OleDbProviderSettings | Where-Object { $_.Name -like 'SQLNCLI*' }
                        $newProvider = $destProviders | Sort-Object Name -Descending | Select-Object -First 1 -ExpandProperty Name

                        Write-Message -Level Verbose -Message "Changing sqlncli to $newProvider"
                        $sql = $sql -replace ("sqlncli[0-9]+", $newProvider)
                    }

                    if ($provider -eq "MSOLEDBSQL19") {
                        # Starting with SQL Server 2025 (17.x), MSOLEDBSQL uses Microsoft OLE DB Driver version 19, which adds support for TDS 8.0. However, this driver introduces a breaking change. You must now specify the encrypt parameter.
                        $providerString = $currentLinkedServer.ProviderString
                        if ($providerString) {
                            if ($providerString -notmatch "Encrypt\s*=\s*Optional" -and $providerString -notmatch "TrustServerCertificate\s*=\s*Yes") {
                                Write-Message -Level Warning -Message "Provider string currently set to '$providerString', so will not change it. Please verify that it includes 'Encrypt=Optional;TrustServerCertificate=Yes' to ensure connectivity."
                            } else {
                                Write-Message -Level Verbose -Message "Provider string already includes encrypt and trustservercertificate settings, so not modifying it."
                            }
                        } else {
                            Write-Message -Level Verbose -Message "Provider string is empty. Adding 'Encrypt=Optional;TrustServerCertificate=Yes' to provider string for MSOLEDBSQL19."
                            $sql = $sql -replace "@provider=N'MSOLEDBSQL'", "@provider=N'MSOLEDBSQL19', @provstr=N'Encrypt=Optional;TrustServerCertificate=Yes'"
                        }
                    }

                    $null = $destServer.Query($sql)

                    if ($copyLinkedServer.ProductName -eq 'SQL Server' -and $copyLinkedServer.Name -ne $copyLinkedServer.DataSource) {
                        $sql2 = "EXEC sp_setnetname '$($copyLinkedServer.Name)', '$($copyLinkedServer.DataSource)'; "
                        $destServer.Query($sql2)
                    }

                    $destServer.LinkedServers.Refresh()
                    Write-Message -Level Verbose -Message "$linkedServerName successfully copied."
                    $copyLinkedServer.Status = "Successful"
                    $copyLinkedServer | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                } catch {
                    $copyLinkedServer.Notes = (Get-ErrorMessage -Record $_)
                    $copyLinkedServer.Status = "Failed"
                    $copyLinkedServer | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                    Write-Message -Level Verbose -Message "Issue creating linked server $linkedServerName on $destinstance | $PSItem"
                    continue
                }
            }

            $destlogins = $destServer.LinkedServers[$linkedServerName].LinkedServerLogins
            $lslogins = $sourcelogins | Where-Object { $_.Name -eq $linkedServerName }

            foreach ($login in $lslogins) {
                $currentlogin = $destlogins | Where-Object { $_.RemoteUser -eq $login.Identity }

                $copyLinkedServer.Type = $login.Identity

                if ($currentlogin.RemoteUser.length -ne 0) {
                    if ($__realCmdlet.ShouldProcess($destinstance, "Migrating linked server identity $($login.Identity)")) {
                        try {
                            if ($login.Password) {
                                $currentlogin.SetRemotePassword($login.Password)
                                $currentlogin.Alter()
                            }

                            $copyLinkedServer.Status = "Successful"
                            $copyLinkedServer | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                        } catch {
                            $copyLinkedServer.Status = "Failed"
                            $copyLinkedServer.Notes = (Get-ErrorMessage -Record $_)
                            $copyLinkedServer | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                            Write-Message -Level Verbose -Message "Issue creating linked server identity for $($login.Identity) on $destinstance | $PSItem"
                            continue
                        }
                    }
                }
            }
        }
    }

    if ($null -ne $SourceSqlCredential.Username) {
        Write-Message -Level Verbose -Message "You are using a SQL Credential. Note that this script requires Windows Administrator access on the source server. Attempting with $($SourceSqlCredential.Username)." -FunctionName Copy-DbaLinkedServer -ModuleName "dbatools"
    }

    try {
        # Do we need a dedicated admin connection to the source for password retrieval?
        # If passwords are excluded, we don't need a DAC
        if ($ExcludePassword) { $dacNeeded = $false } else { $dacNeeded = $true }

        # Do we have a dedicated admin connection already?
        $dacConnected = $Source.Type -eq 'Server' -and $Source.InputObject.Name -match '^ADMIN:'

        $dacOpened = $false
        if ($dacNeeded) {
            if ($dacConnected) {
                Write-Message -Level Verbose -Message "Reusing dedicated admin connection for password retrieval." -FunctionName Copy-DbaLinkedServer -ModuleName "dbatools"
                $sourceServer = $Source.InputObject
            } else {
                Write-Message -Level Verbose -Message "Opening dedicated admin connection for password retrieval." -FunctionName Copy-DbaLinkedServer -ModuleName "dbatools"
                $sourceServer = Connect-DbaInstance -SqlInstance $Source -SqlCredential $SourceSqlCredential -DedicatedAdminConnection -WarningAction SilentlyContinue
                $dacOpened = $true
            }
        } else {
            Write-Message -Level Verbose -Message "Opening or reusing normal connection because passwords are excluded." -FunctionName Copy-DbaLinkedServer -ModuleName "dbatools"
            $sourceServer = Connect-DbaInstance -SqlInstance $Source -SqlCredential $SourceSqlCredential
        }
    } catch {
        Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $Source -FunctionName Copy-DbaLinkedServer
        return
    }

    try {
    if (Test-FunctionInterrupt) { return }

    foreach ($destinstance in $Destination) {
        try {
            $destServer = Connect-DbaInstance -SqlInstance $destinstance -SqlCredential $DestinationSqlCredential
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $destinstance -Continue -FunctionName Copy-DbaLinkedServer
        }
        if (!(Test-SqlSa -SqlInstance $destServer -SqlCredential $DestinationSqlCredential)) {
            Stop-Function -Message "Not a sysadmin on $destinstance" -Target $destServer -Continue -FunctionName Copy-DbaLinkedServer
        }

        # Magic happens here
        Copy-DbaLinkedServers $LinkedServer -Force:$force
    }

    } finally {
        if ($dacOpened) {
            $null = $sourceServer | Disconnect-DbaInstance -WhatIf:$false
        }
    }
} $Source $SourceSqlCredential $Destination $DestinationSqlCredential $Credential $LinkedServer $ExcludeLinkedServer $UpgradeSqlClient $ExcludePassword $Force $EnableException $__realCmdlet @__commonParameters 3>&1 2>&1
""";
}
