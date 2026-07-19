#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Adds an extended property to a SQL Server object. Port of public/Add-DbaExtendedProperty.ps1;
/// the workflow remains a module-scoped PowerShell compatibility hop.
///
/// The WHOLE process body rides ONE verbatim hop per pipeline record. The body never connects - it
/// reaches databases through Get-DbaDatabase and the server through the private Get-ConnectionParent
/// - so there is no NestedConnect.
///
/// THIS COMMAND CARRIES CROSS-RECORD STATE, unlike its Add-DbaDb* siblings. $server is assigned
/// only inside the "if (-not $computername -or -not $instancename -or -not $sqlname)" branch, and
/// then read unconditionally by the Add-Member that stamps the Server note property. So a record
/// whose input object already carries ComputerName/InstanceName/SqlInstance skips the branch and
/// stamps the PREVIOUS record's server. That stale carry is the retired function's behavior,
/// because the local lives in the function scope which spans the whole pipeline; a hop-local would
/// die with the hop and stamp the wrong value on every skipped-branch record instead.
///
/// The carry preserves ASSIGNMENT, not just value. A function-scope local that was never assigned
/// is UNSET - a read walks up to a module- or global-scope $server if one exists - whereas an
/// assignment to $null is a set local that shadows the walk. Restoring the value unconditionally
/// would convert "never assigned" into "assigned null" and stamp null where the function would
/// stamp an up-scope $server (reachable via a caller's $global:server, a common variable name).
/// So the sentinel carries a ServerAssigned flag: the hop restores $server only when an earlier
/// record actually assigned it, and reports whether $server exists at the hop's own scope after
/// the body (set either by that restore or by the branch running this record), detected with
/// Get-Variable -Scope 0 so an up-scope $server is not mistaken for a local one.
///
/// The body has no early return, so the inner block needs no dot-source wrapper: the sentinel
/// emission simply follows the body in the same scope, which is also what makes the body's $server
/// assignment visible to it.
///
/// No Test-Bound sites - the SqlInstance gate reads the VALUE, which rides the hop unchanged.
/// $Pscmdlet.ShouldProcess routes to the real cmdlet via $__realCmdlet so -WhatIf and any
/// yes-to-all answer persist across records. The single in-hop Stop-Function carries -FunctionName
/// because Stop-Function defaults it from Get-PSCallStack and the hop's frame is generated script;
/// -ModuleName/-File/-Line still misattribute [DEF-006]. $EnableException rides the hop param scope
/// because Stop-Function self-defaults it with a scope-walking $EnableException = $EnableException
/// default.
///
/// Surface pinned by migration/baselines/Add-DbaExtendedProperty.json.
/// </summary>
[Cmdlet(VerbsCommon.Add, "DbaExtendedProperty", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Medium)]
public sealed class AddDbaExtendedPropertyCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The database(s) to process.</summary>
    [Parameter(Position = 2)]
    [PsStringArrayCast]
    public string[]? Database { get; set; }

    /// <summary>The name of the extended property.</summary>
    [Parameter(Mandatory = true, Position = 3)]
    [Alias("Property")]
    [PsStringCast]
    public string Name { get; set; } = null!;

    /// <summary>The value of the extended property.</summary>
    [Parameter(Mandatory = true, Position = 4)]
    [PsStringCast]
    public string Value { get; set; } = null!;

    /// <summary>The object(s) to add the extended property to.</summary>
    [Parameter(ValueFromPipeline = true, Position = 5)]
    public PSObject[]? InputObject { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // The source's $server: function-scoped, so it survives between records. Starts as a real null,
    // never a typed default, because the first record must see exactly what the function saw.
    private Hashtable? _state;

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        NestedCommand.InvokeScopedStreaming(this, item =>
        {
            if (item?.BaseObject is Hashtable sentinel && sentinel.ContainsKey("__addDbaExtendedPropertyState"))
            {
                _state = sentinel["__addDbaExtendedPropertyState"] as Hashtable;
                return;
            }
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                RemoveHopErrorBookkeeping(nestedError);
                WriteError(nestedError);
                return;
            }
            WriteObject(item);
        }, ProcessScript,
            SqlInstance, SqlCredential, Database, Name, Value, InputObject,
            EnableException.ToBool(), _state,
            this, BoundCommonParameter("Verbose"), BoundCommonParameter("Debug"));
    }

    /// <summary>Carries a bound common parameter into the hop scopes, which cannot see the
    /// caller's $PSBoundParameters. Null means the caller never bound it.</summary>
    private object? BoundCommonParameter(string name)
    {
        if (MyInvocation.BoundParameters.TryGetValue(name, out object? value))
            return LanguagePrimitives.IsTrue(value);
        return null;
    }

    /// <summary>Removes the silent $error copy the nested pipeline bagged for a merged-back
    /// non-terminating record, so the caller sees one entry per error as the function did.</summary>
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

    // PS: the ENTIRE process body VERBATIM per record. Substitutions only: $Pscmdlet ->
    // $__realCmdlet and -FunctionName Add-DbaExtendedProperty on the single Stop-Function. The
    // $server restore before the body and the state emission after it are the hop's own carry of a
    // function-scoped local; everything between rides as-is, including the re-build of the
    // ComputerName/InstanceName/SqlInstance properties, the six Add-Member -Force stamps, and
    // Select-DefaultView.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Database, $Name, $Value, $InputObject, $EnableException, $__state, $__realCmdlet, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [string[]]$Database, [string]$Name, [string]$Value, [psobject[]]$InputObject, $EnableException, $__state, $__realCmdlet, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    # cross-record carry: $server lives in the function scope, which spans the pipeline, and a
    # record that skips the rebuild branch reads whatever an earlier record left there. Restore
    # ONLY when an earlier record actually assigned it - restoring null unconditionally would
    # convert the function's "unset" (which walks up to a module/global $server) into a local null.
    if ($null -ne $__state -and $__state.ServerAssigned) {
        $server = $__state.Server
    }

        if ($SqlInstance) {
            $InputObject = Get-DbaDatabase -SqlInstance $SqlInstance -SqlCredential $SqlCredential -Database $Database | Where-Object IsAccessible
        }

        foreach ($object in $InputObject) {
            try {
                # Since the inputobject is so generic, we need to re-build these properties
                $computername = $object.ComputerName
                $instancename = $object.InstanceName
                $sqlname = $object.SqlInstance

                if (-not $computername -or -not $instancename -or -not $sqlname) {
                    $server = Get-ConnectionParent $object
                    $servername = $server.Query("SELECT @@SERVERNAME AS servername").servername

                    if (-not $computername) {
                        $computername = ([DbaInstanceParameter]$servername).ComputerName
                    }

                    if (-not $instancename) {
                        $instancename = ([DbaInstanceParameter]$servername).InstanceName
                    }

                    if (-not $sqlname) {
                        $sqlname = $servername
                    }
                }

                if ($__realCmdlet.ShouldProcess($object.Name, "Adding an extended property named $Name with a value of '$Value'")) {
                    $prop = New-Object Microsoft.SqlServer.Management.Smo.ExtendedProperty ($object, $Name, $Value)
                    $prop.Create()
                    Add-Member -Force -InputObject $prop -MemberType NoteProperty -Name ComputerName -Value $computername
                    Add-Member -Force -InputObject $prop -MemberType NoteProperty -Name InstanceName -Value $instancename
                    Add-Member -Force -InputObject $prop -MemberType NoteProperty -Name SqlInstance -Value $sqlname
                    Add-Member -Force -InputObject $prop -MemberType NoteProperty -Name ParentName -Value $object.Name
                    Add-Member -Force -InputObject $prop -MemberType NoteProperty -Name Type -Value $object.GetType().Name
                    Add-Member -Force -InputObject $prop -MemberType NoteProperty -Name Server -Value $server

                    Select-DefaultView -InputObject $prop -Property ComputerName, InstanceName, SqlInstance, ParentName, Type, Name, Value
                }
            } catch {
                Stop-Function -Message "Failed to add extended property $Name with a value of '$Value' to $($object.Name)" -ErrorRecord $_ -FunctionName Add-DbaExtendedProperty
            }
        }

    # Carry $server forward only if it exists at THIS scope (assigned by the restore above or by
    # the branch this record) - Get-Variable -Scope 0 misses an up-scope $server, so an unset local
    # stays unset next record and keeps walking up, exactly as the function-scope variable does.
    $__serverLocal = Get-Variable -Name server -Scope 0 -ErrorAction Ignore
    if ($__serverLocal) {
        @{ __addDbaExtendedPropertyState = @{ Server = $__serverLocal.Value; ServerAssigned = $true } }
    } else {
        @{ __addDbaExtendedPropertyState = @{ ServerAssigned = $false } }
    }
} $SqlInstance $SqlCredential $Database $Name $Value $InputObject $EnableException $__state $__realCmdlet $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
