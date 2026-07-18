#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Returns the next value from a sequence. Port of public/Select-DbaDbSequenceNextValue.ps1; the
/// workflow remains a module-scoped PowerShell compatibility hop.
///
/// A small process-only port carrying TWO bound flags from three Test-Bound reads:
///     Test-Bound -ParameterName SqlInstance      -> $__boundSqlInstance
///     Test-Bound -Not -ParameterName Database    -> -not $__boundDatabase
///     Test-Bound SqlInstance                     -> $__boundSqlInstance (the same flag, read twice)
/// Both are was-it-SUPPLIED tests and cannot become truthiness tests: an explicitly-passed empty
/// -Database must still satisfy the guard rather than trip "Database is required".
///
/// NO $__realCmdlet is passed, and that is not an omission. The source declares
/// SupportsShouldProcess with ConfirmImpact Low but NEVER CALLS $PSCmdlet.ShouldProcess anywhere in
/// the body, so there is no gate to re-route. The declaration is still reproduced on the [Cmdlet]
/// attribute because the baseline pins it (supportsShouldProcess true, confirmImpact Low) and it is
/// part of the observable surface - it is the CALL that is absent, not the capability.
///
/// TWO SOURCE BUGS ARE PRESERVED HERE, both logged upstream rather than repaired:
///   1. Because no ShouldProcess gate exists, -WhatIf does NOT prevent anything. `SELECT NEXT VALUE
///      FOR` is a MUTATING operation - it advances the sequence - so a user running -WhatIf against
///      this command still burns a sequence value while being told nothing happened. The declared
///      SupportsShouldProcess actively creates that false assurance.
///   2. -Sequence is [string[]] but is interpolated into the query as a single token,
///      "[$($Schema)].[$($Sequence)]". Passing more than one sequence name renders them
///      space-joined inside one bracket pair and produces invalid T-SQL rather than iterating.
///      The parameter's plurality is a lie the type system tells; the body only ever handles one.
/// Both reproduce for free because the body is unmodified.
///
/// NO continue-guard wrapper is needed: the body's single early exit is a plain `return`, which a
/// return inside the hop scriptblock reproduces, and there is no `continue` anywhere.
///
/// Pre-port DEF-012 cross-record scope check returns no hits in either shape - no local here is read
/// before assignment, and none is assigned only inside a branch, so the hop's per-record scope reset
/// cannot change behaviour and no sentinel carry is required.
///
/// Note the SINGULAR types: -SqlInstance is DbaInstanceParameter (not an array) and -InputObject is
/// one Smo.Database (not an array), matching the source and the baseline. -Sequence keeps its
/// "Name" alias, which is part of the pinned surface.
///
/// Only body edit is -FunctionName Select-DbaDbSequenceNextValue on the direct Stop-Function site.
///
/// Surface pinned by migration/baselines/Select-DbaDbSequenceNextValue.json
/// (sourceSha256 a5ada3ddf913eaef473c1bad045d436e3191a40198bf90989c485ebaab4565b5):
/// SqlInstance 0, SqlCredential 1, Database 2, Sequence 3 MANDATORY alias Name, Schema 4
/// (default "dbo"), InputObject 5 ValueFromPipeline; no parameter sets; outputType empty. Positions
/// are declared explicitly per the positional-binding-loss class.
/// </summary>
[Cmdlet(VerbsCommon.Select, "DbaDbSequenceNextValue", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Low)]
public sealed class SelectDbaDbSequenceNextValueCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance.</summary>
    [Parameter(Position = 0)]
    public DbaInstanceParameter? SqlInstance { get; set; }

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The database holding the sequence.</summary>
    [Parameter(Position = 2)]
    public string? Database { get; set; }

    /// <summary>The sequence to read the next value from.</summary>
    [Parameter(Mandatory = true, Position = 3)]
    [Alias("Name")]
    public string[]? Sequence { get; set; }

    /// <summary>The schema owning the sequence.</summary>
    [Parameter(Position = 4)]
    public string Schema { get; set; } = "dbo";

    /// <summary>Database object piped in.</summary>
    [Parameter(ValueFromPipeline = true, Position = 5)]
    public Microsoft.SqlServer.Management.Smo.Database? InputObject { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, ProcessScript,
            SqlInstance, SqlCredential, Database, Sequence, Schema, InputObject,
            EnableException.ToBool(),
            TestBound(nameof(SqlInstance)), TestBound(nameof(Database)),
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
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

    // PS: the process block verbatim. Edits: the three Test-Bound reads -> the two carried bound
    // flags, and -FunctionName Select-DbaDbSequenceNextValue on the direct Stop-Function site.
    // No $__realCmdlet: the source never calls ShouldProcess (see the class remarks).
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Database, $Sequence, $Schema, $InputObject, $EnableException, $__boundSqlInstance, $__boundDatabase, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter]$SqlInstance, [PSCredential]$SqlCredential, [string]$Database, [string[]]$Sequence, [string]$Schema, [Microsoft.SqlServer.Management.Smo.Database]$InputObject, $EnableException, $__boundSqlInstance, $__boundDatabase, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    if ($__boundSqlInstance -and (-not $__boundDatabase)) {
        Stop-Function -Message "Database is required when SqlInstance is specified" -FunctionName Select-DbaDbSequenceNextValue
        return
    }

    if ($__boundSqlInstance) {
        $InputObject = Get-DbaDatabase -SqlInstance $SqlInstance -SqlCredential $SqlCredential -Database $Database
    }

    $InputObject.Query("SELECT NEXT VALUE FOR [$($Schema)].[$($Sequence)] AS NextValue").NextValue
} $SqlInstance $SqlCredential $Database $Sequence $Schema $InputObject $EnableException $__boundSqlInstance $__boundDatabase $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
