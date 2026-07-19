#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Enables transparent data encryption on databases, creating and backing up the required master key and
/// certificate or asymmetric key along the way.
/// </summary>
/// <remarks>
/// <para>
/// The workflow remains a module-scoped PowerShell compatibility hop so that the master-key/certificate
/// provisioning chain, the sequential-vs-parallel split (including the RunspacePool fan-out, which is ported
/// verbatim), the output shapes, and dbatools stream and error handling stay observable-identical to the
/// script implementation.
/// </para>
/// <para>
/// The script's function scope spans the whole pipeline and it MUTATES the EncryptorName parameter inside its
/// loop (falling back to the discovered master certificate or asymmetric key name), so with piped input the
/// value discovered on one record steers the next record's branch. It also appends instance-resolved databases
/// to $InputObject within an invocation while the parameter rebinds on each pipeline record. A per-record hop
/// would lose the EncryptorName carry, so the port is COLLECT-THEN-ENDPROCESSING in one scope (the same shape
/// as SetDbaLoginCommand): BeginProcessing snapshots the by-name InputObject, ProcessRecord collects one
/// batch per record, and EndProcessing runs ONE hop that replays the process body per batch, dot-sourced so
/// each record's early validation returns stay local to that batch. InputObject is the only pipeline-bound
/// parameter, so every pipeline record is a rebind (the engine reassigns the parameter before each process
/// invocation even for a repeated array instance, discarding the += mutation like the function world); only
/// a by-name InputObject persists across the run. $InputObject is a typed local ([Smo.Database[]]) so the
/// source's typed-array += re-coercion is preserved.
/// </para>
/// <para>
/// The command declares SupportsShouldProcess with ConfirmImpact High but has NO direct ShouldProcess call in
/// the body - the gates live in the nested dbatools commands (New-DbaDbCertificate, Enable-DbaDbEncryption,
/// and friends), so there is no $__realCmdlet redirect and bound -WhatIf/-Confirm are forwarded through the
/// common-parameter splat for the nested mutators to honor (the Stop-DbaDbEncryption sibling's shape). The two
/// Mandatory SecureStrings ride live end to end - never converted, logged, or persisted - including across the
/// runspace boundary in the -Parallel path, which is source behavior replicated as-is. The two [datetime]
/// parameters carry [PsDateTimeCast] for invariant-culture bind parity; when unbound, their computed defaults
/// ((Get-Date) / (Get-Date).AddYears(5)) are resolved in BeginProcessing - during binding, before pipeline
/// enumeration - exactly like the script's parameter defaults, so a slow pipeline cannot drift the dates. The command emits per database and a later record can
/// Stop-Function-terminate under -EnableException, so it streams through InvokeScopedStreaming. The only body
/// edits are message attribution: -FunctionName on the 12 direct Stop-Function calls and -FunctionName plus
/// -ModuleName "dbatools" on the 24 direct Write-Message calls - the runspace scriptblock's inner emissions
/// are untouched.
/// </para>
/// </remarks>
[Cmdlet(VerbsLifecycle.Start, "DbaDbEncryption", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.High)]
public sealed class StartDbaDbEncryptionCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Alternative credential for the target instances.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>Name of the certificate or asymmetric key that encrypts the database encryption keys.</summary>
    [Parameter(Position = 2)]
    [Alias("Certificate", "CertificateName")]
    public string? EncryptorName { get; set; }

    /// <summary>Whether the encryptor is a certificate or an asymmetric key.</summary>
    [Parameter(Position = 3)]
    [PsStringCast]
    [ValidateSet("AsymmetricKey", "Certificate")]
    public string? EncryptorType { get; set; } = "Certificate";

    /// <summary>The databases to encrypt.</summary>
    [Parameter(Position = 4)]
    public string[]? Database { get; set; }

    /// <summary>Databases to exclude from encryption.</summary>
    [Parameter(Position = 5)]
    public string[]? ExcludeDatabase { get; set; }

    /// <summary>Path where the master key and certificate backups are written.</summary>
    [Parameter(Mandatory = true, Position = 6)]
    [PsStringCast]
    public string? BackupPath { get; set; }

    /// <summary>Password protecting the service master key.</summary>
    [Parameter(Mandatory = true, Position = 7)]
    public System.Security.SecureString? MasterKeySecurePassword { get; set; }

    /// <summary>Subject for a newly created certificate.</summary>
    [Parameter(Position = 8)]
    public string? CertificateSubject { get; set; }

    /// <summary>Start date for a newly created certificate. Defaults to now.</summary>
    [Parameter(Position = 9)]
    [PsDateTimeCast]
    public DateTime CertificateStartDate { get; set; }

    /// <summary>Expiration date for a newly created certificate. Defaults to five years from now.</summary>
    [Parameter(Position = 10)]
    [PsDateTimeCast]
    public DateTime CertificateExpirationDate { get; set; }

    /// <summary>Marks a newly created certificate active for service broker dialogs.</summary>
    [Parameter]
    public SwitchParameter CertificateActiveForServiceBrokerDialog { get; set; }

    /// <summary>Password protecting the master key and certificate backups.</summary>
    [Parameter(Mandatory = true, Position = 11)]
    public System.Security.SecureString? BackupSecurePassword { get; set; }

    /// <summary>Database objects from Get-DbaDatabase for pipeline operations.</summary>
    [Parameter(ValueFromPipeline = true, Position = 12)]
    public Microsoft.SqlServer.Management.Smo.Database[]? InputObject { get; set; }

    /// <summary>Encrypts all user databases on the instance.</summary>
    [Parameter]
    public SwitchParameter AllUserDatabases { get; set; }

    /// <summary>Creates the named certificate when it does not exist.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    /// <summary>Runs the per-database encryption operations in parallel runspaces.</summary>
    [Parameter]
    public SwitchParameter Parallel { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    /// <summary>One batch per pipeline record: { inputRebound, InputObject }.</summary>
    private readonly List<object?[]?> _batches = new List<object?[]?>();

    /// <summary>Whether -InputObject was bound by name (captured before any pipeline record arrives).</summary>
    private bool _inputObjectByName;

    /// <summary>The by-name InputObject value snapshotted at begin (the hop's initial $InputObject).</summary>
    private object? _byNameInputObject;

    /// <summary>Certificate start date resolved at bind time, exactly like the script's parameter default.</summary>
    private DateTime _resolvedStartDate;

    /// <summary>Certificate expiration date resolved at bind time, exactly like the script's parameter default.</summary>
    private DateTime _resolvedExpirationDate;

    /// <summary>Captures the by-name InputObject before any pipeline record arrives.</summary>
    protected override void BeginProcessing()
    {
        // The script's (Get-Date) / (Get-Date).AddYears(5) parameter defaults evaluate during binding,
        // BEFORE pipeline enumeration - resolving them here keeps the dates from drifting by the
        // collection duration of a slow pipeline.
        _resolvedStartDate = TestBound(nameof(CertificateStartDate)) ? CertificateStartDate : DateTime.Now;
        _resolvedExpirationDate = TestBound(nameof(CertificateExpirationDate)) ? CertificateExpirationDate : DateTime.Now.AddYears(5);
        // InputObject is the command's ONLY pipeline-bound parameter, so binding is bimodal: bound by name
        // (one ProcessRecord, the by-name value seeds the hop and is never overridden) or bound per pipeline
        // record (EVERY record is a rebind - the engine reassigns the parameter before each process
        // invocation even when the same array instance arrives twice, which discards the body's += mutation
        // exactly like the function world). Reference-identity detection would miss that same-instance case
        // and leak the previous record's expanded $InputObject.
        _inputObjectByName = MyInvocation.BoundParameters.ContainsKey("InputObject");
        _byNameInputObject = InputObject;
    }

    /// <summary>Records each pipeline record's input as a batch; the work runs once in EndProcessing.</summary>
    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        _batches.Add(new object?[] { !_inputObjectByName, InputObject });
    }

    /// <summary>Replays the process body per batch in one shared scope.</summary>
    protected override void EndProcessing()
    {
        if (Interrupted)
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
            _batches.ToArray(), _byNameInputObject, SqlInstance, SqlCredential, EncryptorName, EncryptorType, Database, ExcludeDatabase,
            BackupPath, MasterKeySecurePassword, CertificateSubject,
            _resolvedStartDate, _resolvedExpirationDate,
            CertificateActiveForServiceBrokerDialog.ToBool(), BackupSecurePassword, AllUserDatabases.ToBool(), Force.ToBool(), Parallel.ToBool(), EnableException.ToBool(),
            BoundCommonParameter("WhatIf"), BoundCommonParameter("Confirm"),
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

    // PS: a per-batch dot-sourced replay of the process body, all in one scope. Substitutions only:
    // -FunctionName on the 12 DIRECT Stop-Function calls; -FunctionName + -ModuleName "dbatools" on the 24
    // DIRECT Write-Message calls. No ShouldProcess redirect (no direct gate in the body). The datetime
    // parameters always arrive resolved (bound value, or the bind-time default from BeginProcessing).
    private const string ProcessScript = """
param($__batches, $__byNameInputObject, $SqlInstance, $SqlCredential, $EncryptorName, $EncryptorType, $Database, $ExcludeDatabase, $BackupPath, $MasterKeySecurePassword, $CertificateSubject, $CertificateStartDate, $CertificateExpirationDate, $CertificateActiveForServiceBrokerDialog, $BackupSecurePassword, $AllUserDatabases, $Force, $Parallel, $EnableException, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundWhatIf) { $__commonParameters.WhatIf = [bool]$__boundWhatIf }
if ($null -ne $__boundConfirm) { $__commonParameters.Confirm = [bool]$__boundConfirm }
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding(SupportsShouldProcess)]
    param($__batches, $__byNameInputObject, [Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [System.Management.Automation.PSCredential]$SqlCredential, [string]$EncryptorName, [string]$EncryptorType, [string[]]$Database, [string[]]$ExcludeDatabase, [string]$BackupPath, [System.Security.SecureString]$MasterKeySecurePassword, [string]$CertificateSubject, $CertificateStartDate, $CertificateExpirationDate, $CertificateActiveForServiceBrokerDialog, [System.Security.SecureString]$BackupSecurePassword, $AllUserDatabases, $Force, $Parallel, $EnableException, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

        [Microsoft.SqlServer.Management.Smo.Database[]]$InputObject = $__byNameInputObject

        # Named-wrapper shim: the process body runs inside a function carrying the command's name,
        # so call-stack-deriving helpers see Start-DbaDbEncryption exactly as in the function world.
        # Write-ProgressHelper reads (Get-PSCallStack)[1].Command to build BOTH its Activity string
        # and its TotalSteps lookup; from an anonymous scriptblock frame it saw a literal scriptblock
        # marker, which produced 'Executing <ScriptBlock>' and a FLAT 0% where the script ramps.
        # The dot-sourced invocation keeps the body in the hop scope, so cross-record state is unchanged.
        function Start-DbaDbEncryption {
        if (-not $SqlInstance -and -not $InputObject) {
            Stop-Function -Message "You must specify either SqlInstance or pipe in an InputObject from Get-DbaDatabase" -FunctionName Start-DbaDbEncryption
            return
        }

        if ($Force -and -not $EncryptorName) {
            Stop-Function -Message "You must specify an EncryptorName when using Force" -FunctionName Start-DbaDbEncryption
            return
        }

        if ($SqlInstance) {
            if (-not $Database -and -not $ExcludeDatabase -and -not $AllUserDatabases) {
                Stop-Function -Message "You must specify Database, ExcludeDatabase or AllUserDatabases when using SqlInstance" -FunctionName Start-DbaDbEncryption
                return
            }
            # all does not need to be addressed in the code because it gets all the dbs if $databases is empty
            $param = @{
                SqlInstance     = $SqlInstance
                SqlCredential   = $SqlCredential
                Database        = $Database
                ExcludeDatabase = $ExcludeDatabase
            }
            $InputObject += Get-DbaDatabase @param | Where-Object Name -NotIn 'master', 'model', 'tempdb', 'msdb', 'resource'
        }

        $PSDefaultParameterValues["Connect-DbaInstance:Verbose"] = $false

        if (-not $Parallel) {
            # Sequential processing (original behavior)
            foreach ($db in $InputObject) {
                try {
                    # Just in case they use inputobject + exclude
                    if ($db.Name -in $ExcludeDatabase) { continue }
                    $server = $db.Parent
                    # refresh in case we have a stale database
                    $null = $db.Refresh()
                    $null = $server.Refresh()
                    $servername = $server.Name

                    if ($db.EncryptionEnabled) {
                        Write-Message -Level Warning -Message "Database $($db.Name) on $($server.Name) is already encrypted" -FunctionName Start-DbaDbEncryption -ModuleName "dbatools"
                        continue
                    }

                    # before doing anything, see if the master cert is in order
                    if ($EncryptorName) {
                        $mastercert = Get-DbaDbCertificate -SqlInstance $server -Database master | Where-Object Name -eq $EncryptorName
                        if (-not $mastercert -and $Force) {
                            $mastercert = New-DbaDbCertificate -SqlInstance $server -Database master -Name $EncryptorName

                            $null = $server.Refresh()
                            $null = $server.Databases["master"].Refresh()
                        }
                    } else {
                        $mastercert = Get-DbaDbCertificate -SqlInstance $server -Database master | Where-Object Name -NotMatch "##"
                    }

                    if ($EncryptorName -and -not $mastercert) {
                        Stop-Function -Message "EncryptorName specified but no matching certificate found on $($server.Name)" -Continue -FunctionName Start-DbaDbEncryption
                    }

                    if ($mastercert.Count -gt 1) {
                        Stop-Function -Message "More than one certificate found on $($server.Name), please specify an EncryptorName" -Continue -FunctionName Start-DbaDbEncryption
                    }

                    $stepCounter = 0
                    Write-ProgressHelper -StepNumber ($stepCounter++) -Message "Processing $($db.Name)"
                } catch {
                    Stop-Function -Message "Failure" -ErrorRecord $_ -Continue -FunctionName Start-DbaDbEncryption
                }

                try {
                    # Ensure a database master key exists in the master database
                    Write-Message -Level Verbose -Message "Ensure a database master key exists in the master database for $($server.Name)" -FunctionName Start-DbaDbEncryption -ModuleName "dbatools"
                    Write-ProgressHelper -StepNumber ($stepCounter++) -Message "Ensure a database master key exists in the master database for $($server.Name)"
                    $masterkey = Get-DbaDbMasterKey -SqlInstance $server -Database master

                    if (-not $masterkey) {
                        Write-Message -Level Verbose -Message "master key not found, creating one" -FunctionName Start-DbaDbEncryption -ModuleName "dbatools"
                        $params = @{
                            SqlInstance     = $server
                            SecurePassword  = $MasterKeySecurePassword
                            EnableException = $true
                        }
                        $masterkey = New-DbaServiceMasterKey @params
                    }

                    $null = $db.Refresh()
                    $null = $server.Refresh()

                    $dbmasterkeytest = Get-DbaFile -SqlInstance $server -Path $BackupPath | Where-Object FileName -match "$servername-master"
                    if (-not $dbmasterkeytest) {
                        # has to be repeated in the event databases are piped in
                        $params = @{
                            SqlInstance     = $server
                            Database        = "master"
                            Path            = $BackupPath
                            EnableException = $true
                            SecurePassword  = $BackupSecurePassword
                        }
                        $null = $server.Databases["master"].Refresh()
                        Write-Message -Level Verbose -Message "Backing up master key on $($server.Name)" -FunctionName Start-DbaDbEncryption -ModuleName "dbatools"
                        $null = Backup-DbaDbMasterKey @params
                    }
                } catch {
                    Stop-Function -Message "Failure" -ErrorRecord $_ -Continue -FunctionName Start-DbaDbEncryption
                }

                try {
                    Write-ProgressHelper -StepNumber ($stepCounter++) -Message "Processing EncryptorType for $($db.Name) on $($server.Name)"
                    if ($EncryptorType -eq "Certificate") {
                        if (-not $mastercert) {
                            Write-Message -Level Verbose -Message "master cert not found, creating one" -FunctionName Start-DbaDbEncryption -ModuleName "dbatools"
                            $params = @{
                                SqlInstance                  = $server
                                Database                     = "master"
                                StartDate                    = $CertificateStartDate
                                ExpirationDate               = $CertificateExpirationDate
                                ActiveForServiceBrokerDialog = $CertificateActiveForServiceBrokerDialog
                                EnableException              = $true
                            }
                            if ($CertificateSubject) {
                                $params.Subject = $CertificateSubject
                            }
                            $mastercert = New-DbaDbCertificate @params
                        } else {
                            Write-Message -Level Verbose -Message "master cert found on $($server.Name)" -FunctionName Start-DbaDbEncryption -ModuleName "dbatools"
                        }

                        $null = $db.Refresh()
                        $null = $server.Refresh()

                        $mastercerttest = Get-DbaFile -SqlInstance $server -Path $BackupPath | Where-Object FileName -match "$($mastercert.Name).cer"
                        if (-not $mastercerttest) {
                            # Back up certificate
                            $null = $server.Databases["master"].Refresh()
                            $params = @{
                                SqlInstance        = $server
                                Database           = "master"
                                Certificate        = $mastercert.Name
                                Path               = $BackupPath
                                EnableException    = $true
                                EncryptionPassword = $BackupSecurePassword
                            }
                            Write-Message -Level Verbose -Message "Backing up master certificate on $($server.Name)" -FunctionName Start-DbaDbEncryption -ModuleName "dbatools"
                            $null = Backup-DbaDbCertificate @params
                        }

                        if (-not $EncryptorName) {
                            Write-Message -Level Verbose -Message "Getting EncryptorName from master cert on $($server.Name)" -FunctionName Start-DbaDbEncryption -ModuleName "dbatools"
                            $EncryptorName = $mastercert.Name
                        }
                    } else {
                        $masterasym = Get-DbaDbAsymmetricKey -SqlInstance $server -Database master

                        if (-not $masterasym) {
                            Write-Message -Level Verbose -Message "Asymmetric key not found, creating one for master on $($server.Name)" -FunctionName Start-DbaDbEncryption -ModuleName "dbatools"
                            $params = @{
                                SqlInstance     = $server
                                Database        = "master"
                                EnableException = $true
                            }
                            $masterasym = New-DbaDbAsymmetricKey @params
                            $null = $server.Refresh()
                            $null = $server.Databases["master"].Refresh()
                        } else {
                            Write-Message -Level Verbose -Message "master asymmetric key found on $($server.Name)" -FunctionName Start-DbaDbEncryption -ModuleName "dbatools"
                        }

                        if (-not $EncryptorName) {
                            Write-Message -Level Verbose -Message "Getting EncryptorName from master asymmetric key" -FunctionName Start-DbaDbEncryption -ModuleName "dbatools"
                            $EncryptorName = $masterasym.Name
                        }
                    }
                } catch {
                    Stop-Function -Message "Failure" -ErrorRecord $_ -Continue -FunctionName Start-DbaDbEncryption
                }

                try {
                    # Create a database encryption key in the target database
                    # Enable database encryption on the target database
                    Write-ProgressHelper -StepNumber ($stepCounter++) -Message "Creating database encryption key in $($db.Name) on $($server.Name)"
                    if ($db.HasDatabaseEncryptionKey) {
                        Write-Message -Level Verbose -Message "$($db.Name) on $($db.Parent.Name) already has a database encryption key" -FunctionName Start-DbaDbEncryption -ModuleName "dbatools"
                    } else {
                        Write-Message -Level Verbose -Message "Creating new encryption key for $($db.Name) on $($server.Name) with EncryptorName $EncryptorName" -FunctionName Start-DbaDbEncryption -ModuleName "dbatools"
                        $null = $db | New-DbaDbEncryptionKey -EncryptorName $EncryptorName -EnableException
                    }

                    Write-ProgressHelper -StepNumber ($stepCounter++) -Message "Enabling database encryption in $($db.Name) on $($server.Name)"
                    Write-Message -Level Verbose -Message "Enabling encryption for $($db.Name) on $($server.Name) using $EncryptorType $EncryptorName" -FunctionName Start-DbaDbEncryption -ModuleName "dbatools"
                    $db | Enable-DbaDbEncryption -EncryptorName $EncryptorName
                } catch {
                    Stop-Function -Message "Failure" -ErrorRecord $_ -Continue -FunctionName Start-DbaDbEncryption
                }
            }
        } else {
            # Parallel processing - group databases by instance and pre-create shared resources
            $instanceGroups = $InputObject | Group-Object -Property { $_.Parent.Name }

            foreach ($instanceGroup in $instanceGroups) {
                $server = $instanceGroup.Group[0].Parent
                $servername = $server.Name
                $databases = $instanceGroup.Group | Where-Object { $_.Name -notin $ExcludeDatabase -and -not $_.EncryptionEnabled }

                if ($databases.Count -eq 0) {
                    Write-Message -Level Verbose -Message "No databases to encrypt on $servername" -FunctionName Start-DbaDbEncryption -ModuleName "dbatools"
                    continue
                }

                Write-Message -Level Verbose -Message "Pre-creating shared resources for $servername" -FunctionName Start-DbaDbEncryption -ModuleName "dbatools"

                try {
                    # Step 1: Ensure master key exists
                    $masterkey = Get-DbaDbMasterKey -SqlInstance $server -Database master
                    if (-not $masterkey) {
                        Write-Message -Level Verbose -Message "Creating master key on $servername" -FunctionName Start-DbaDbEncryption -ModuleName "dbatools"
                        $splatMasterKey = @{
                            SqlInstance     = $server
                            SecurePassword  = $MasterKeySecurePassword
                            EnableException = $true
                        }
                        $masterkey = New-DbaServiceMasterKey @splatMasterKey
                    }

                    # Back up master key if needed
                    $dbmasterkeytest = Get-DbaFile -SqlInstance $server -Path $BackupPath | Where-Object FileName -match "$servername-master"
                    if (-not $dbmasterkeytest) {
                        $splatBackupMasterKey = @{
                            SqlInstance     = $server
                            Database        = "master"
                            Path            = $BackupPath
                            EnableException = $true
                            SecurePassword  = $BackupSecurePassword
                        }
                        Write-Message -Level Verbose -Message "Backing up master key on $servername" -FunctionName Start-DbaDbEncryption -ModuleName "dbatools"
                        $null = Backup-DbaDbMasterKey @splatBackupMasterKey
                    }

                    # Step 2: Ensure certificate or asymmetric key exists
                    if ($EncryptorType -eq "Certificate") {
                        if ($EncryptorName) {
                            $mastercert = Get-DbaDbCertificate -SqlInstance $server -Database master | Where-Object Name -eq $EncryptorName
                            if (-not $mastercert -and $Force) {
                                $mastercert = New-DbaDbCertificate -SqlInstance $server -Database master -Name $EncryptorName
                            }
                        } else {
                            $mastercert = Get-DbaDbCertificate -SqlInstance $server -Database master | Where-Object Name -NotMatch "##"
                        }

                        if (-not $mastercert) {
                            Write-Message -Level Verbose -Message "Creating certificate on $servername" -FunctionName Start-DbaDbEncryption -ModuleName "dbatools"
                            $splatCertificate = @{
                                SqlInstance                  = $server
                                Database                     = "master"
                                StartDate                    = $CertificateStartDate
                                ExpirationDate               = $CertificateExpirationDate
                                ActiveForServiceBrokerDialog = $CertificateActiveForServiceBrokerDialog
                                EnableException              = $true
                            }
                            if ($CertificateSubject) {
                                $splatCertificate.Subject = $CertificateSubject
                            }
                            $mastercert = New-DbaDbCertificate @splatCertificate
                        }

                        # Back up certificate if needed
                        $mastercerttest = Get-DbaFile -SqlInstance $server -Path $BackupPath | Where-Object FileName -match "$($mastercert.Name).cer"
                        if (-not $mastercerttest) {
                            $splatBackupCertificate = @{
                                SqlInstance        = $server
                                Database           = "master"
                                Certificate        = $mastercert.Name
                                Path               = $BackupPath
                                EnableException    = $true
                                EncryptionPassword = $BackupSecurePassword
                            }
                            Write-Message -Level Verbose -Message "Backing up certificate on $servername" -FunctionName Start-DbaDbEncryption -ModuleName "dbatools"
                            $null = Backup-DbaDbCertificate @splatBackupCertificate
                        }

                        $encryptorNameToUse = $mastercert.Name
                    } else {
                        $masterasym = Get-DbaDbAsymmetricKey -SqlInstance $server -Database master
                        if (-not $masterasym) {
                            Write-Message -Level Verbose -Message "Creating asymmetric key on $servername" -FunctionName Start-DbaDbEncryption -ModuleName "dbatools"
                            $splatAsymmetricKey = @{
                                SqlInstance     = $server
                                Database        = "master"
                                EnableException = $true
                            }
                            $masterasym = New-DbaDbAsymmetricKey @splatAsymmetricKey
                        }
                        $encryptorNameToUse = $masterasym.Name
                    }
                } catch {
                    Stop-Function -Message "Failed to create shared resources on $servername" -ErrorRecord $_ -Continue -FunctionName Start-DbaDbEncryption
                }

                # Step 3: Create a database encryption key in the target database if needed
                # This has to be done before parallel processing as New-DbaDbEncryptionKey uses Get-DbaDatabase internally
                # which uses the custom method .Query() that is not present in runspaces due to the way dbatools is loaded there.
                foreach ($db in $databases) {
                    try {
                        if ($db.HasDatabaseEncryptionKey) {
                            Write-Message -Level Verbose -Message "$($db.Name) on $($db.Parent.Name) already has a database encryption key" -FunctionName Start-DbaDbEncryption -ModuleName "dbatools"
                        } else {
                            Write-Message -Level Verbose -Message "Creating new encryption key for $($db.Name) on $($server.Name) with EncryptorName $encryptorNameToUse" -FunctionName Start-DbaDbEncryption -ModuleName "dbatools"
                            $null = $db | New-DbaDbEncryptionKey -EncryptorName $encryptorNameToUse -EnableException
                        }
                    } catch {
                        Stop-Function -Message "Failure" -ErrorRecord $_ -Continue -FunctionName Start-DbaDbEncryption
                    }
                }

                # Step 4: Parallelize database encryption operations
                $encryptionScript = {
                    param (
                        $ServerName,
                        $DatabaseName,
                        $EncryptorName,
                        $EnableException,
                        $SqlCredential
                    )

                    $server = $null
                    try {
                        # Create new connection for this thread
                        $splatConnection = @{
                            SqlInstance   = $ServerName
                            SqlCredential = $SqlCredential
                        }
                        $server = Connect-DbaInstance @splatConnection
                        $db = $server.Databases[$DatabaseName]

                        if (-not $db) {
                            throw "Database $DatabaseName not found on $ServerName"
                        }

                        # Enable encryption
                        $result = $db | Enable-DbaDbEncryption -EncryptorName $EncryptorName -EnableException -Confirm:$false

                        [PSCustomObject]@{
                            ComputerName      = $server.ComputerName
                            InstanceName      = $server.ServiceName
                            SqlInstance       = $server.DomainInstanceName
                            DatabaseName      = $DatabaseName
                            EncryptionEnabled = $result.EncryptionEnabled
                            Status            = "Success"
                            Error             = $null
                        }
                    } catch {
                        [PSCustomObject]@{
                            ComputerName      = $null
                            InstanceName      = $null
                            SqlInstance       = $ServerName
                            DatabaseName      = $DatabaseName
                            EncryptionEnabled = $false
                            Status            = "Failed"
                            Error             = $_.Exception.Message
                        }
                    } finally {
                        $null = $server | Disconnect-DbaInstance -WhatIf:$false
                    }
                }

                # Create runspace pool with dbatools module imported
                $initialSessionState = [System.Management.Automation.Runspaces.InitialSessionState]::CreateDefault()
                $dbatools = Get-Module -Name dbatools
                if ($dbatools) {
                    $initialSessionState.ImportPSModule($dbatools.Path)
                }
                $runspacePool = [runspacefactory]::CreateRunspacePool(1, 10, $initialSessionState, $Host)
                $runspacePool.Open()

                $threads = @()

                foreach ($db in $databases) {
                    $splatRunspace = @{
                        ServerName      = $servername
                        DatabaseName    = $db.Name
                        EncryptorName   = $encryptorNameToUse
                        EnableException = $EnableException
                        SqlCredential   = $SqlCredential
                    }

                    Write-Message -Level Verbose -Message "Queuing database $($db.Name) on $servername for encryption" -FunctionName Start-DbaDbEncryption -ModuleName "dbatools"

                    $thread = [powershell]::Create()
                    $thread.RunspacePool = $runspacePool
                    $null = $thread.AddScript($encryptionScript)
                    $null = $thread.AddParameters($splatRunspace)

                    $handle = $thread.BeginInvoke()
                    $threads += [PSCustomObject]@{
                        Handle      = $handle
                        Thread      = $thread
                        Database    = $db.Name
                        Instance    = $servername
                        IsRetrieved = $false
                        Started     = Get-Date
                    }
                }

                # Retrieve results
                while ($threads | Where-Object { $_.IsRetrieved -eq $false }) {
                    $totalThreads = ($threads | Measure-Object).Count
                    $totalRetrievedThreads = ($threads | Where-Object { $_.IsRetrieved -eq $true } | Measure-Object).Count
                    Write-Progress -Id 1 -Activity "Enabling encryption on $servername" -Status "Progress" -CurrentOperation "Processing: $totalRetrievedThreads/$totalThreads" -PercentComplete ($totalRetrievedThreads / $totalThreads * 100)

                    foreach ($thread in ($threads | Where-Object { $_.IsRetrieved -eq $false })) {
                        if ($thread.Handle.IsCompleted) {
                            $result = $thread.Thread.EndInvoke($thread.Handle)
                            $thread.IsRetrieved = $true

                            if ($result) {
                                if ($result.Status -eq "Failed") {
                                    Stop-Function -Message "Failed to enable encryption for $($result.DatabaseName) on $($result.SqlInstance): $($result.Error)" -Continue -FunctionName Start-DbaDbEncryption
                                } else {
                                    $result | Select-DefaultView -Property ComputerName, InstanceName, SqlInstance, DatabaseName, EncryptionEnabled
                                }
                            }

                            $thread.Thread.Dispose()
                        }
                    }
                    Start-Sleep -Milliseconds 500
                }

                $runspacePool.Close()
                $runspacePool.Dispose()
            }
        }
        }
        foreach ($__batch in $__batches) {
            if ($__batch[0]) { $InputObject = $__batch[1] }
            . Start-DbaDbEncryption
        }
} $__batches $__byNameInputObject $SqlInstance $SqlCredential $EncryptorName $EncryptorType $Database $ExcludeDatabase $BackupPath $MasterKeySecurePassword $CertificateSubject $CertificateStartDate $CertificateExpirationDate $CertificateActiveForServiceBrokerDialog $BackupSecurePassword $AllUserDatabases $Force $Parallel $EnableException $__boundWhatIf $__boundConfirm $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1

""";
}
