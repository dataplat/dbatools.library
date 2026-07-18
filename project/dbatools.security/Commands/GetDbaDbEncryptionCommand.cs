#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves the encryption objects (database encryption keys, certificates, and asymmetric and
/// symmetric keys) from one or more SQL Server databases.
/// </summary>
/// <remarks>
/// <para>
/// The workflow remains a module-scoped PowerShell compatibility hop so that the connection, the
/// database enumeration, the encryption-object inspection, the output shape, and dbatools stream and
/// error handling stay observable-identical to the script implementation.
/// </para>
/// <para>
/// The command is process-only, so it ships as a single hop per record. It emits multiple objects per
/// database (the encryption key, then each certificate, asymmetric key, and symmetric key) and can raise
/// a terminating -EnableException failure on a later database or object after an earlier one has already
/// emitted, so output is streamed through InvokeScopedStreaming - each object reaches the pipeline as
/// produced, surviving a later throw; a buffered collection would be discarded on that throw and lose the
/// earlier output. Both switch parameters (IncludeSystemDBs, EnableException) are carried as plain
/// (untyped) bools, because a switch in the inner CmdletBinding scriptblock is excluded from positional
/// binding.
/// </para>
/// </remarks>
[Cmdlet(VerbsCommon.Get, "DbaDbEncryption")]
public sealed class GetDbaDbEncryptionCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Alternative credential for the target instances.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The database or databases to inspect for encryption objects.</summary>
    [Parameter(Position = 2)]
    public object[]? Database { get; set; }

    /// <summary>Databases to exclude from the inspection.</summary>
    [Parameter(Position = 3)]
    public object[]? ExcludeDatabase { get; set; }

    /// <summary>Includes the system databases in the inspection.</summary>
    [Parameter]
    public SwitchParameter IncludeSystemDBs { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    /// <summary>Retrieves encryption objects for one pipeline record.</summary>
    protected override void ProcessRecord()
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
            SqlInstance, SqlCredential, Database, ExcludeDatabase,
            IncludeSystemDBs.ToBool(), EnableException.ToBool(),
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

    // PS: the process body VERBATIM. Substitutions only: -FunctionName on the 3 direct Stop-Function and
    // the 1 direct Write-Message call. No ShouldProcess, no Test-Bound, no config defaults, no bound carries.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Database, $ExcludeDatabase, $IncludeSystemDBs, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [System.Management.Automation.PSCredential]$SqlCredential, [object[]]$Database, [object[]]$ExcludeDatabase, $IncludeSystemDBs, $EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

        foreach ($instance in $SqlInstance) {
            #For each SQL Server in collection, connect and get SMO object

            try {
                $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential
            } catch {
                Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Get-DbaDbEncryption
            }

            #If IncludeSystemDBs is true, include systemdbs
            #only look at online databases (Status equal normal)
            try {
                if ($Database) {
                    $dbs = $server.Databases | Where-Object Name -In $Database
                } elseif ($IncludeSystemDBs) {
                    $dbs = $server.Databases | Where-Object IsAccessible
                } else {
                    $dbs = $server.Databases | Where-Object { $_.IsAccessible -and $_.IsSystemObject -eq 0 }
                }

                if ($ExcludeDatabase) {
                    $dbs = $dbs | Where-Object Name -NotIn $ExcludeDatabase
                }
            } catch {
                Stop-Function -Message "Unable to gather dbs for $instance" -Target $instance -Continue -FunctionName Get-DbaDbEncryption
            }

            foreach ($db in $dbs) {
                Write-Message -Level Verbose -Message "Processing $db" -FunctionName Get-DbaDbEncryption

                if ($db.EncryptionEnabled) {
                    $returnCertificate = [PSCustomObject]@{
                        ComputerName             = $server.ComputerName
                        InstanceName             = $server.ServiceName
                        SqlInstance              = $server.DomainInstanceName
                        Database                 = $db.Name
                        Encryption               = "EncryptionEnabled (TDE)"
                        Name                     = $null
                        LastBackup               = $null
                        PrivateKeyEncryptionType = $null
                        EncryptionAlgorithm      = $null
                        KeyLength                = $null
                        Owner                    = $null
                        Object                   = $null
                        ExpirationDate           = $null
                    }

                    if ($db.DatabaseEncryptionKey.EncryptionType -eq 'ServerCertificate') {
                        $serverCertificate = $server.Databases['master'].Certificates | Where-Object {
                            (Compare-Object -ReferenceObject $db.DatabaseEncryptionKey.Thumbprint -DifferenceObject $_.Thumbprint -SyncWindow 0).Length -eq 0
                        }

                        if (-not $serverCertificate) {
                            Stop-Function -Message "Could not locate TDE server certificate $($db.DatabaseEncryptionKey.Name)" -Target $instance -Continue -FunctionName Get-DbaDbEncryption
                        }

                        $returnCertificate.Name = $serverCertificate.Name
                        $returnCertificate.LastBackup = $serverCertificate.LastBackupDate
                        $returnCertificate.PrivateKeyEncryptionType = $serverCertificate.PrivateKeyEncryptionType
                        $returnCertificate.Owner = $serverCertificate.Owner
                        $returnCertificate.Object = $serverCertificate
                        $returnCertificate.ExpirationDate = $serverCertificate.ExpirationDate
                        $returnCertificate.EncryptionAlgorithm = $db.DatabaseEncryptionKey.EncryptionAlgorithm
                    }

                    $returnCertificate
                }

                foreach ($cert in $db.Certificates) {
                    [PSCustomObject]@{
                        ComputerName             = $server.ComputerName
                        InstanceName             = $server.ServiceName
                        SqlInstance              = $server.DomainInstanceName
                        Database                 = $db.Name
                        Encryption               = "Certificate"
                        Name                     = $cert.Name
                        LastBackup               = $cert.LastBackupDate
                        PrivateKeyEncryptionType = $cert.PrivateKeyEncryptionType
                        EncryptionAlgorithm      = $null
                        KeyLength                = $null
                        Owner                    = $cert.Owner
                        Object                   = $cert
                        ExpirationDate           = $cert.ExpirationDate
                    }

                }

                foreach ($ak in $db.AsymmetricKeys) {
                    [PSCustomObject]@{
                        ComputerName             = $server.ComputerName
                        InstanceName             = $server.ServiceName
                        SqlInstance              = $server.DomainInstanceName
                        Database                 = $db.Name
                        Encryption               = "Asymmetric key"
                        Name                     = $ak.Name
                        LastBackup               = $null
                        PrivateKeyEncryptionType = $ak.PrivateKeyEncryptionType
                        EncryptionAlgorithm      = $ak.KeyEncryptionAlgorithm
                        KeyLength                = $ak.KeyLength
                        Owner                    = $ak.Owner
                        Object                   = $ak
                        ExpirationDate           = $null
                    }

                }
                foreach ($sk in $db.SymmetricKeys) {
                    [PSCustomObject]@{
                        Server                   = $server.name
                        Instance                 = $server.InstanceName
                        Database                 = $db.Name
                        Encryption               = "Symmetric key"
                        Name                     = $sk.Name
                        LastBackup               = $null
                        PrivateKeyEncryptionType = $sk.PrivateKeyEncryptionType
                        EncryptionAlgorithm      = $ak.EncryptionAlgorithm
                        KeyLength                = $sk.KeyLength
                        Owner                    = $sk.Owner
                        Object                   = $sk
                        ExpirationDate           = $null
                    }
                }
            }
        }
} $SqlInstance $SqlCredential $Database $ExcludeDatabase $IncludeSystemDBs $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1

""";
}
