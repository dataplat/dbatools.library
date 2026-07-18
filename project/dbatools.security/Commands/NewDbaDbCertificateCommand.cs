#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Creates certificates in one or more databases.
/// </summary>
/// <remarks>
/// <para>
/// The workflow remains a module-scoped PowerShell compatibility hop so that the certificate creation, the
/// password handling, the added note properties, the default view, and dbatools stream and error handling
/// stay observable-identical to the script implementation.
/// </para>
/// <para>
/// The body MUTATES $Name and $Subject, which normally forces a state sentinel so the assignments survive
/// into the next record. Here they must NOT survive: both assignments are gated on Test-Bound -Not, and
/// Test-Bound reads $PSBoundParameters, which a plain assignment never touches. The gate therefore stays
/// open on every record, and each record unconditionally re-derives both values from its own $db, discarding
/// whatever the previous record left behind. Measured directly - two piped databases, both parameters
/// unbound - the script gives the SECOND database its own name and subject rather than reusing the first's.
/// A per-record hop reproduces that exactly; a sentinel carrying the values forward would be inert machinery
/// resting on a false premise, so there is none.
/// </para>
/// <para>
/// The two [datetime] parameters carry [PsDateTimeCast] for invariant-culture bind parity, and their
/// computed defaults are resolved in BeginProcessing - during binding, before pipeline enumeration - exactly
/// as the script's parameter defaults are. ExpirationDate defaults to StartDate.AddYears(5), so the resolved
/// StartDate feeds it: resolving either per record would let a slow pipeline drift the dates apart.
/// </para>
/// <para>
/// The command streams through InvokeScopedStreaming: it emits per certificate and a later certificate can
/// raise a terminating -EnableException failure, so a buffered call would discard the certificates already
/// created and reported (DEF-001). The SecureString rides live and is flattened only by the source's own
/// ConvertFrom-SecurePass inside the ShouldProcess gate. The undefined $Credential referenced by the last
/// Add-Member is a source bug preserved verbatim - it resolves to nothing in both worlds.
/// </para>
/// </remarks>
[Cmdlet(VerbsCommon.New, "DbaDbCertificate", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Low)]
public sealed class NewDbaDbCertificateCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Alternative credential for the target instances.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The certificate names to create.</summary>
    [Parameter(Position = 2)]
    [PsStringArrayCast]
    public string[]? Name { get; set; }

    /// <summary>The databases to create the certificates in.</summary>
    [Parameter(Position = 3)]
    [PsStringArrayCast]
    public string[]? Database { get; set; } = new[] { "master" };

    /// <summary>The certificate subjects.</summary>
    [Parameter(Position = 4)]
    [PsStringArrayCast]
    public string[]? Subject { get; set; }

    /// <summary>The date the certificate becomes valid. Defaults to now.</summary>
    [Parameter(Position = 5)]
    [PsDateTimeCast]
    public DateTime StartDate { get; set; }

    /// <summary>The date the certificate expires. Defaults to five years after StartDate.</summary>
    [Parameter(Position = 6)]
    [PsDateTimeCast]
    public DateTime ExpirationDate { get; set; }

    /// <summary>Marks the certificate active for service broker dialogs.</summary>
    [Parameter]
    public SwitchParameter ActiveForServiceBrokerDialog { get; set; }

    /// <summary>Password protecting the certificate's private key.</summary>
    [Parameter(Position = 7)]
    [Alias("Password")]
    public System.Security.SecureString? SecurePassword { get; set; }

    /// <summary>Database objects from Get-DbaDatabase for pipeline operations.</summary>
    [Parameter(ValueFromPipeline = true, Position = 8)]
    public Microsoft.SqlServer.Management.Smo.Database[]? InputObject { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    /// <summary>The certificate dates resolved at bind time, exactly like the script's parameter defaults.</summary>
    private DateTime _resolvedStartDate;
    private DateTime _resolvedExpirationDate;

    /// <summary>Resolves the computed date defaults before any pipeline record is processed.</summary>
    protected override void BeginProcessing()
    {
        // The script's (Get-Date) and $StartDate.AddYears(5) defaults evaluate during binding, BEFORE
        // pipeline enumeration - and ExpirationDate is derived from the resolved StartDate, so resolving
        // either per record would let a slow pipeline drift them apart.
        _resolvedStartDate = TestBound(nameof(StartDate)) ? StartDate : DateTime.Now;
        _resolvedExpirationDate = TestBound(nameof(ExpirationDate)) ? ExpirationDate : _resolvedStartDate.AddYears(5);
    }

    /// <summary>Creates the certificates for the databases bound to the current record.</summary>
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
            else if (item is not null)
            {
                WriteObject(item);
            }
        }, ProcessScript,
            SqlInstance, SqlCredential, Name, Database, Subject, _resolvedStartDate,
            _resolvedExpirationDate, ActiveForServiceBrokerDialog.ToBool(), SecurePassword, InputObject,
            EnableException.ToBool(), this,
            TestBound(nameof(Name)), TestBound(nameof(Subject)),
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

    // PS: the process body VERBATIM. Substitutions only: $Pscmdlet -> $__realCmdlet (the ShouldProcess
    // gate); the two Test-Bound -Not reads -> the carried by-name flags; -FunctionName on the 2 DIRECT
    // Stop-Function calls; -FunctionName + -ModuleName "dbatools" on the 4 DIRECT Write-Message calls.
    // No trailing sentinel: the body's $Name/$Subject assignments are re-derived from $db on every record
    // in the script too, so nothing needs to survive the hop.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Name, $Database, $Subject, $StartDate, $ExpirationDate, $ActiveForServiceBrokerDialog, $SecurePassword, $InputObject, $EnableException, $__realCmdlet, $__nameBound, $__subjectBound, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundWhatIf) { $__commonParameters.WhatIf = [bool]$__boundWhatIf }
if ($null -ne $__boundConfirm) { $__commonParameters.Confirm = [bool]$__boundConfirm }
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding(SupportsShouldProcess)]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [System.Management.Automation.PSCredential]$SqlCredential, [string[]]$Name, [string[]]$Database, [string[]]$Subject, [datetime]$StartDate, [datetime]$ExpirationDate, $ActiveForServiceBrokerDialog, [System.Security.SecureString]$SecurePassword, [Microsoft.SqlServer.Management.Smo.Database[]]$InputObject, $EnableException, $__realCmdlet, $__nameBound, $__subjectBound, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

        if ($SqlInstance) {
            $InputObject += Get-DbaDatabase -SqlInstance $SqlInstance -SqlCredential $SqlCredential -Database $Database
        }

        foreach ($db in $InputObject) {
            if ((-not $__nameBound)) {
                $Name = $db.Name
                Write-Message -Level Verbose -Message "Name of certificate not specified, setting it to '$name'" -FunctionName New-DbaDbCertificate -ModuleName "dbatools"
            }

            if ((-not $__subjectBound)) {
                Write-Message -Level Verbose -Message "Subject not specified, setting it to '$Name Database Certificate'" -FunctionName New-DbaDbCertificate -ModuleName "dbatools"
                $subject = "$Name Database Certificate"
            }

            foreach ($cert in $Name) {
                $null = $db.Certificates.Refresh()
                if ($null -ne $db.Certificates[$cert]) {
                    Stop-Function -Message "Certificate '$cert' already exists in $($db.Name) on $($db.Parent.Name)" -Target $db -Continue -FunctionName New-DbaDbCertificate
                }

                if ($__realCmdlet.ShouldProcess($db.Parent.Name, "Creating certificate for database '$($db.Name)'")) {

                    # something is up with .net, force a stop
                    $eap = $ErrorActionPreference
                    $ErrorActionPreference = 'Stop'
                    try {
                        $smocert = New-Object -TypeName Microsoft.SqlServer.Management.Smo.Certificate $db, $cert
                        $smocert.StartDate = $StartDate
                        $smocert.Subject = $Subject
                        $smocert.ExpirationDate = $ExpirationDate
                        $smocert.ActiveForServiceBrokerDialog = $ActiveForServiceBrokerDialog

                        if ($SecurePassword) {
                            Write-Message -Level Verbose -Message "Creating certificate with password" -FunctionName New-DbaDbCertificate -ModuleName "dbatools"
                            $smocert.Create(($SecurePassword | ConvertFrom-SecurePass))
                        } else {
                            Write-Message -Level Verbose -Message "Creating certificate without password, so it'll be protected by the master key" -FunctionName New-DbaDbCertificate -ModuleName "dbatools"
                            $smocert.Create()
                        }

                        Add-Member -Force -InputObject $smocert -MemberType NoteProperty -Name ComputerName -value $db.Parent.ComputerName
                        Add-Member -Force -InputObject $smocert -MemberType NoteProperty -Name InstanceName -value $db.Parent.ServiceName
                        Add-Member -Force -InputObject $smocert -MemberType NoteProperty -Name SqlInstance -value $db.Parent.DomainInstanceName
                        Add-Member -Force -InputObject $smocert -MemberType NoteProperty -Name Database -value $db.Name
                        Add-Member -Force -InputObject $smocert -MemberType NoteProperty -Name Credential -value $Credential
                        Select-DefaultView -InputObject $smocert -Property ComputerName, InstanceName, SqlInstance, Database, Name, Subject, StartDate, ActiveForServiceBrokerDialog, ExpirationDate, Issuer, LastBackupDate, Owner, PrivateKeyEncryptionType, Serial
                    } catch {
                        $ErrorActionPreference = $eap
                        Stop-Function -Message "Failed to create certificate in $($db.Name) on $($db.Parent.Name)" -Target $smocert -ErrorRecord $_ -Continue -FunctionName New-DbaDbCertificate
                    }
                    $ErrorActionPreference = $eap
                }
            }
        }
} $SqlInstance $SqlCredential $Name $Database $Subject $StartDate $ExpirationDate $ActiveForServiceBrokerDialog $SecurePassword $InputObject $EnableException $__realCmdlet $__nameBound $__subjectBound $__boundWhatIf $__boundConfirm $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1

""";
}
