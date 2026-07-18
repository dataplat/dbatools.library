#nullable enable

using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Sets up database mirroring: validates the pair, seeds the mirror from backups,
/// creates and starts the mirroring endpoints, grants CONNECT to the service accounts
/// and wires the partners (and witness).
/// Port of public/Invoke-DbaDbMirroring.ps1; surface pinned by
/// migration/baselines/Invoke-DbaDbMirroring.json.
/// </summary>
[Cmdlet(VerbsLifecycle.Invoke, "DbaDbMirroring", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.High)]
public sealed class InvokeDbaDbMirroringCommand : DbaBaseCmdlet
{
    /// <summary>The primary SQL Server instance.</summary>
    [Parameter(Position = 0)]
    public DbaInstanceParameter? Primary { get; set; }

    /// <summary>Login to the primary instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? PrimarySqlCredential { get; set; }

    /// <summary>The mirror SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, Position = 2)]
    public DbaInstanceParameter[]? Mirror { get; set; }

    /// <summary>Login to the mirror instances using alternative credentials.</summary>
    [Parameter(Position = 3)]
    public PSCredential? MirrorSqlCredential { get; set; }

    /// <summary>The witness SQL Server instance.</summary>
    [Parameter(Position = 4)]
    public DbaInstanceParameter? Witness { get; set; }

    /// <summary>Login to the witness instance using alternative credentials.</summary>
    [Parameter(Position = 5)]
    public PSCredential? WitnessSqlCredential { get; set; }

    /// <summary>The databases to mirror.</summary>
    [Parameter(Position = 6)]
    public string[]? Database { get; set; }

    /// <summary>Endpoint encryption requirement.</summary>
    [Parameter(Position = 7)]
    [ValidateSet("Disabled", "Required", "Supported")]
    public string EndpointEncryption { get; set; } = "Required";

    /// <summary>Endpoint encryption algorithm.</summary>
    [Parameter(Position = 8)]
    [ValidateSet("Aes", "AesRC4", "None", "RC4", "RC4Aes")]
    public string EncryptionAlgorithm { get; set; } = "Aes";

    /// <summary>Network share both instances can read, used to stage the seeding backups.</summary>
    [Parameter(Position = 9)]
    public string? SharedPath { get; set; }

    /// <summary>Database objects piped from Get-DbaDatabase.</summary>
    [Parameter(ValueFromPipeline = true, Position = 10)]
    public Microsoft.SqlServer.Management.Smo.Database[]? InputObject { get; set; }

    /// <summary>Seeds the mirror from the existing backup chain instead of taking new backups.</summary>
    [Parameter]
    public SwitchParameter UseLastBackup { get; set; }

    /// <summary>Drops and recreates an existing mirroring configuration, suppressing prompts.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    private Hashtable? _state;

    protected override void BeginProcessing()
    {
        base.BeginProcessing();

        // C1 transplant condition: loud fail before any record if the engine field is gone.
        PromptStateTransplant.AssertResolvable("Invoke-DbaDbMirroring");
    }

    protected override void ProcessRecord()
    {
        if (Interrupted)
        {
            return;
        }

        // WHOLE-RECORD hop per the W3-005/W4-011 convention: the begin block's
        // Force -> ConfirmPreference suppression and its $params construction ride at
        // the hop top, and all four ShouldProcess gates run on the INNER scriptblock's
        // own $Pscmdlet. Because InputObject is a per-record VFP axis, the
        // ShouldProcess Yes/No-to-All answer must survive BETWEEN piped records the way
        // the source's single function-scope $Pscmdlet does: the W3-082 prompt-state
        // transplant carries lastShouldProcessContinueStatus through the __w4041State
        // sentinel. The source's begin captures $PSBoundParameters into $params and
        // strips four keys before splatting it at Invoke-DbMirrorValidation - the hop's
        // own $PSBoundParameters would see hop plumbing rather than user input, so a
        // clone of the REAL bound parameters is carried as $__allParams (W3-090
        // Set-DbaDbState precedent). The two loop-less validation Stop-Function+return
        // sites exit the record via the dot-block frame; the in-loop sites are
        // -Continue (loop-local). Test-Bound scope-walks the caller, so its three
        // call sites become carried bound flags.
        foreach (PSObject? item in NestedCommand.InvokeScoped(this, ProcessScript,
            Primary, PrimarySqlCredential, Mirror, MirrorSqlCredential, Witness,
            WitnessSqlCredential, Database, EndpointEncryption, EncryptionAlgorithm,
            SharedPath, InputObject, UseLastBackup.ToBool(), Force.ToBool(),
            EnableException.ToBool(), new Hashtable(MyInvocation.BoundParameters),
            TestBound(nameof(Primary)), TestBound(nameof(Database)),
            TestBound(nameof(SharedPath)), TestBound(nameof(UseLastBackup)), _state,
            BoundCommonParameter("WhatIf"), BoundCommonParameter("Confirm"),
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
        {
            Hashtable? sentinel = item?.BaseObject as Hashtable;
            if (sentinel is not null && sentinel.ContainsKey("__w4041State"))
            {
                _state = sentinel["__w4041State"] as Hashtable;
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

    private object? BoundCommonParameter(string name)
    {
        if (MyInvocation.BoundParameters.TryGetValue(name, out object? value))
        {
            return LanguagePrimitives.IsTrue(value);
        }
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
                string.Equals(first.Exception?.Message, record.Exception?.Message, System.StringComparison.Ordinal))
            {
                errorList.RemoveAt(0);
            }
        }
        catch
        {
            // Best-effort bookkeeping only.
        }
    }

    // PS: the begin block's Force -> ConfirmPreference suppression and $params
    // construction ride verbatim at the hop top ($params reads the CARRIED clone of the
    // real bound parameters), then the source process block VERBATIM, CRLF-preserved and
    // cmp-proven byte-exact after stripping 17 -FunctionName appends (16 Stop-Function +
    // 1 Write-Message) and reversing the three Test-Bound flag rewrites (SOURCE
    // comments). ShouldProcess gates use the inner block's own $Pscmdlet (hop-scope-local,
    // so the Force suppression above still applies); the dot-block preserves the source's
    // two early returns. The W3-082 prompt-state transplant brackets the body so
    // Yes/No-to-All spans piped records.
    private const string ProcessScript = """
param($Primary, $PrimarySqlCredential, $Mirror, $MirrorSqlCredential, $Witness, $WitnessSqlCredential, $Database, $EndpointEncryption, $EncryptionAlgorithm, $SharedPath, $InputObject, $UseLastBackup, $Force, $EnableException, $__allParams, $__boundPrimary, $__boundDatabase, $__boundSharedPath, $__boundUseLastBackup, $__state, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundWhatIf) { $__commonParameters.WhatIf = [bool]$__boundWhatIf }
if ($null -ne $__boundConfirm) { $__commonParameters.Confirm = [bool]$__boundConfirm }
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter]$Primary, [PSCredential]$PrimarySqlCredential, [Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$Mirror, [PSCredential]$MirrorSqlCredential, [Dataplat.Dbatools.Parameter.DbaInstanceParameter]$Witness, [PSCredential]$WitnessSqlCredential, [string[]]$Database, [string]$EndpointEncryption, [string]$EncryptionAlgorithm, [string]$SharedPath, [Microsoft.SqlServer.Management.Smo.Database[]]$InputObject, $UseLastBackup, $Force, $EnableException, $__allParams, $__boundPrimary, $__boundDatabase, $__boundSharedPath, $__boundUseLastBackup, $__state, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    if ($Force) { $ConfirmPreference = 'none' }

    $params = $__allParams
    $null = $params.Remove('UseLastBackup')
    $null = $params.Remove('Force')
    $null = $params.Remove('Confirm')
    $null = $params.Remove('Whatif')

    # cross-record engine-state restore: the ShouldProcess Yes/No-to-All answer spans the
    # pipeline in the source (one CommandRuntime); the transplant field name is identical
    # on PS 5.1 and PS 7 (W3-082 mechanism, empirically verified)
    $__spField = $Pscmdlet.CommandRuntime.GetType().GetField("lastShouldProcessContinueStatus", [System.Reflection.BindingFlags]"NonPublic,Instance")
    if ($null -eq $__spField) {
        throw "Invoke-DbaDbMirroring: prompt-state transplant field lastShouldProcessContinueStatus not resolvable on this engine (C1 assert)."
    }
    if ($null -ne $__state -and $null -ne $__state.shouldProcessContinueStatus) {
        $__spField.SetValue($Pscmdlet.CommandRuntime, [Enum]::Parse($__spField.FieldType, $__state.shouldProcessContinueStatus))
    }

    . {
        if ($__boundPrimary -and -not $__boundDatabase) { # SOURCE: if ((Test-Bound -ParameterName Primary) -and (Test-Bound -Not -ParameterName Database)) {
            Stop-Function -Message "Database is required when Primary is specified" -FunctionName Invoke-DbaDbMirroring
            return
        }

        if ($Force -and (-not $SharedPath -and -not $UseLastBackup)) {
            Stop-Function -Message "SharedPath or UseLastBackup is required when Force is used" -FunctionName Invoke-DbaDbMirroring
            return
        }

        if ($Primary) {
            $InputObject += Get-DbaDatabase -SqlInstance $Primary -SqlCredential $PrimarySqlCredential -Database $Database
        }

        foreach ($primarydb in $InputObject) {
            $stepCounter = 0
            $Primary = $source = $primarydb.Parent
            foreach ($currentmirror in $Mirror) {
                $stepCounter = 0
                try {
                    $dest = Connect-DbaInstance -SqlInstance $currentmirror -SqlCredential $MirrorSqlCredential
                } catch {
                    Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $currentmirror -Continue -FunctionName Invoke-DbaDbMirroring
                }

                if ($Witness) {
                    try {
                        $witserver = Connect-DbaInstance -SqlInstance $Witness -SqlCredential $WitnessSqlCredential
                    } catch {
                        Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $Witness -Continue -FunctionName Invoke-DbaDbMirroring
                    }
                }

                $dbName = $primarydb.Name

                Write-ProgressHelper -StepNumber ($stepCounter++) -Message "Validating mirror setup"
                # Thanks to https://github.com/mmessano/PowerShell/blob/master/SQL-ConfigureDatabaseMirroring.ps1 for the tips

                $params.Database = $dbName
                $validation = Invoke-DbMirrorValidation @params

                if ($__boundSharedPath -and -not $validation.AccessibleShare) { # SOURCE: if ((Test-Bound -ParameterName SharedPath) -and -not $validation.AccessibleShare) {
                    Stop-Function -Continue -Message "Cannot access $SharedPath from $($dest.Name)" -FunctionName Invoke-DbaDbMirroring
                }

                if (-not $validation.EditionMatch) {
                    Stop-Function -Continue -Message "This mirroring configuration is not supported. Because the principal server instance, $source, is $($source.EngineEdition) Edition, the mirror server instance must also be $($source.EngineEdition) Edition." -FunctionName Invoke-DbaDbMirroring
                }

                $badstate = $validation | Where-Object MirroringStatus -ne "none"
                if ($badstate) {
                    Stop-Function -Message "Cannot setup mirroring on database ($dbName) due to its current mirroring state on primary: $($badstate.MirroringStatus)" -Continue -FunctionName Invoke-DbaDbMirroring
                }

                if ($primarydb.Status -ne "Normal") {
                    Stop-Function -Message "Cannot setup mirroring on database ($dbName) due to its current state: $($primarydb.Status)" -Continue -FunctionName Invoke-DbaDbMirroring
                }

                Write-ProgressHelper -StepNumber ($stepCounter++) -Message "Setting recovery model for $dbName on $($source.Name) to Full"

                if ($primarydb.RecoveryModel -ne "Full") {
                    if ($__boundUseLastBackup) { # SOURCE: if ((Test-Bound -ParameterName UseLastBackup)) {
                        Stop-Function -Message "$dbName not set to full recovery. UseLastBackup cannot be used." -FunctionName Invoke-DbaDbMirroring
                    } else {
                        $null = Set-DbaDbRecoveryModel -SqlInstance $source -Database $primarydb.Name -RecoveryModel Full
                    }
                }

                Write-ProgressHelper -StepNumber ($stepCounter++) -Message "Copying $dbName from primary to mirror"

                if (-not $validation.DatabaseExistsOnMirror -or $Force) {
                    if ($UseLastBackup) {
                        $allbackups = Get-DbaDbBackupHistory -SqlInstance $primarydb.Parent -Database $primarydb.Name -IncludeCopyOnly -Last
                    } else {
                        if ($Force -or $Pscmdlet.ShouldProcess("$Primary", "Creating full and log backups of $primarydb on $SharedPath")) {
                            try {
                                $fullbackup = $primarydb | Backup-DbaDatabase -BackupDirectory $SharedPath -Type Full -EnableException
                                $logbackup = $primarydb | Backup-DbaDatabase -BackupDirectory $SharedPath -Type Log -EnableException
                                $allbackups = $fullbackup, $logbackup
                                $UseLastBackup = $true
                            } catch {
                                Stop-Function -Message "Failure" -ErrorRecord $_ -Target $primarydb -Continue -FunctionName Invoke-DbaDbMirroring
                            }
                        }
                    }

                    if ($Pscmdlet.ShouldProcess("$currentmirror", "Restoring full and log backups of $primarydb from $Primary")) {
                        foreach ($currentmirrorinstance in $currentmirror) {
                            try {
                                $null = $allbackups | Restore-DbaDatabase -SqlInstance $currentmirrorinstance -SqlCredential $MirrorSqlCredential -WithReplace -NoRecovery -TrustDbBackupHistory -EnableException
                            } catch {
                                Stop-Function -Message "Failure" -ErrorRecord $_ -Target $dest -Continue -FunctionName Invoke-DbaDbMirroring
                            }
                        }
                    }

                    if ($SharedPath) {
                        Write-Message -Level Verbose -Message "Backups still exist on $SharedPath" -FunctionName Invoke-DbaDbMirroring
                    }
                }

                $currentmirrordb = Get-DbaDatabase -SqlInstance $dest -Database $dbName
                $primaryendpoint = Get-DbaEndpoint -SqlInstance $source | Where-Object EndpointType -eq DatabaseMirroring
                $currentmirrorendpoint = Get-DbaEndpoint -SqlInstance $dest | Where-Object EndpointType -eq DatabaseMirroring

                if (-not $primaryendpoint) {
                    Write-ProgressHelper -StepNumber ($stepCounter++) -Message "Setting up endpoint for primary"
                    $primaryendpoint = New-DbaEndpoint -SqlInstance $source -Type DatabaseMirroring -Role Partner -Name Mirroring -EncryptionAlgorithm $EncryptionAlgorithm -EndpointEncryption $EndpointEncryption
                    $null = $primaryendpoint | Stop-DbaEndpoint
                    $null = $primaryendpoint | Start-DbaEndpoint
                }

                if (-not $currentmirrorendpoint) {
                    Write-ProgressHelper -StepNumber ($stepCounter++) -Message "Setting up endpoint for mirror"
                    $currentmirrorendpoint = New-DbaEndpoint -SqlInstance $dest -Type DatabaseMirroring -Role Partner -Name Mirroring -EncryptionAlgorithm $EncryptionAlgorithm -EndpointEncryption $EndpointEncryption
                    $null = $currentmirrorendpoint | Stop-DbaEndpoint
                    $null = $currentmirrorendpoint | Start-DbaEndpoint
                }

                if ($witserver) {
                    Write-ProgressHelper -StepNumber ($stepCounter++) -Message "Setting up endpoint for witness"
                    $witnessendpoint = Get-DbaEndpoint -SqlInstance $witserver | Where-Object EndpointType -eq DatabaseMirroring
                    if (-not $witnessendpoint) {
                        $witnessendpoint = New-DbaEndpoint -SqlInstance $witserver -Type DatabaseMirroring -Role Witness -Name Mirroring -EncryptionAlgorithm $EncryptionAlgorithm -EndpointEncryption $EndpointEncryption
                        $null = $witnessendpoint | Stop-DbaEndpoint
                        $null = $witnessendpoint | Start-DbaEndpoint
                    }
                }

                Write-ProgressHelper -StepNumber ($stepCounter++) -Message "Granting permissions to service account"

                $serviceAccounts = $source.ServiceAccount, $dest.ServiceAccount, $witserver.ServiceAccount | Select-Object -Unique

                foreach ($account in $serviceAccounts) {
                    if ($account) {
                        if ($account -eq "LocalSystem" -and $source.HostPlatform -eq "Linux") {
                            $account = "NT AUTHORITY\SYSTEM"
                        }
                        if ($Pscmdlet.ShouldProcess("primary, mirror and witness (if specified)", "Creating login $account and granting CONNECT ON ENDPOINT")) {
                            if (-not (Get-DbaLogin -SqlInstance $source -Login $account)) {
                                $null = New-DbaLogin -SqlInstance $source -Login $account
                            }
                            if (-not (Get-DbaLogin -SqlInstance $dest -Login $account)) {
                                $null = New-DbaLogin -SqlInstance $dest -Login $account
                            }
                            try {
                                $null = $source.Query("GRANT CONNECT ON ENDPOINT::$primaryendpoint TO [$account]")
                                $null = $dest.Query("GRANT CONNECT ON ENDPOINT::$currentmirrorendpoint TO [$account]")
                                if ($witserver) {
                                    if (-not (Get-DbaLogin -SqlInstance $source -Login $account)) {
                                        $null = New-DbaLogin -SqlInstance $witserver -Login $account
                                    }
                                    $witserver.Query("GRANT CONNECT ON ENDPOINT::$witnessendpoint TO [$account]")
                                }
                            } catch {
                                Stop-Function -Continue -Message "Failure" -ErrorRecord $_ -FunctionName Invoke-DbaDbMirroring
                            }
                        }
                    }
                }

                Write-ProgressHelper -StepNumber ($stepCounter++) -Message "Starting endpoints if necessary"
                try {
                    $null = $primaryendpoint, $currentmirrorendpoint, $witnessendpoint | Start-DbaEndpoint -EnableException
                } catch {
                    Stop-Function -Continue -Message "Failure" -ErrorRecord $_ -FunctionName Invoke-DbaDbMirroring
                }

                try {
                    Write-ProgressHelper -StepNumber ($stepCounter++) -Message "Setting up partner for mirror"
                    $null = $currentmirrordb | Set-DbaDbMirror -Partner $primaryendpoint.Fqdn -EnableException
                } catch {
                    Stop-Function -Message "Failure on mirror" -ErrorRecord $_ -Continue -FunctionName Invoke-DbaDbMirroring
                }

                try {
                    Write-ProgressHelper -StepNumber ($stepCounter++) -Message "Setting up partner for primary"
                    $null = $primarydb | Set-DbaDbMirror -Partner $currentmirrorendpoint.Fqdn -EnableException
                } catch {
                    Stop-Function -Continue -Message "Failure on primary" -ErrorRecord $_ -FunctionName Invoke-DbaDbMirroring
                }

                try {
                    if ($witnessendpoint) {
                        $null = $primarydb | Set-DbaDbMirror -Witness $witnessendpoint.Fqdn -EnableException
                    }
                } catch {
                    Stop-Function -Continue -Message "Failure with the new last part" -ErrorRecord $_ -FunctionName Invoke-DbaDbMirroring
                }


                if ($Pscmdlet.ShouldProcess("console", "Showing results")) {
                    $results = [PSCustomObject]@{
                        Primary        = $Primary
                        Mirror         = $currentmirror
                        Witness        = $Witness
                        Database       = $primarydb.Name
                        ServiceAccount = $serviceAccounts
                        Status         = "Success"
                    }
                    if ($Witness) {
                        $results | Select-DefaultView -Property Primary, Mirror, Witness, Database, Status
                    } else {
                        $results | Select-DefaultView -Property Primary, Mirror, Database, Status
                    }
                }
            }
        }
    }

    @{ __w4041State = @{ shouldProcessContinueStatus = $(if ($null -ne $__spField) { "$($__spField.GetValue($Pscmdlet.CommandRuntime))" } else { $null }) } }
} $Primary $PrimarySqlCredential $Mirror $MirrorSqlCredential $Witness $WitnessSqlCredential $Database $EndpointEncryption $EncryptionAlgorithm $SharedPath $InputObject $UseLastBackup $Force $EnableException $__allParams $__boundPrimary $__boundDatabase $__boundSharedPath $__boundUseLastBackup $__state $__boundWhatIf $__boundConfirm $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}