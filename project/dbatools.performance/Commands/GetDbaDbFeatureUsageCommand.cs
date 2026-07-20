#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Management.Automation;
using Dataplat.Dbatools.Connection;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;
using Microsoft.SqlServer.Management.Smo;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Lists persisted Enterprise-edition feature usage per database. Port of
/// public/Get-DbaDbFeatureUsage.ps1 (W1-066). The InputObject pipeline shape: each
/// process block appends Get-DbaDatabase output for every -SqlInstance to the bound
/// InputObject array (the nested call passes ALL FOUR parameters verbatim - null when
/// unbound - through a module hop with the bound-Verbose carrier); each database then
/// logs the "Processing [db] on server" verbose, warns-and-continues through the
/// record-less Stop-Function when inaccessible, and EMITS THE RAW Database.Query rows
/// (an empty resultset's real $null included) with the catch mapping to a target-less
/// Stop-Function "Failure" -Continue. Surface pinned by
/// migration/baselines/Get-DbaDbFeatureUsage.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaDbFeatureUsage")]
public sealed class GetDbaDbFeatureUsageCommand : DbaInstanceCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Position = 0)]
    public override DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public override PSCredential? SqlCredential { get; set; }

    /// <summary>The databases to scan.</summary>
    [Parameter(Position = 2)]
    public string[]? Database { get; set; }

    /// <summary>The databases to skip.</summary>
    [Parameter(Position = 3)]
    public string[]? ExcludeDatabase { get; set; }

    /// <summary>Database objects piped in, typically from Get-DbaDatabase.</summary>
    [Parameter(ValueFromPipeline = true, Position = 4)]
    public Microsoft.SqlServer.Management.Smo.Database[]? InputObject { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // PS begin block: the feature query, verbatim.
    private const string Sql = @"SELECT  SERVERPROPERTY('MachineName') AS ComputerName,
            ISNULL(SERVERPROPERTY('InstanceName'), 'MSSQLSERVER') AS InstanceName,
            SERVERPROPERTY('ServerName') AS SqlInstance, feature_id AS Id,
            feature_name AS Feature,  DB_NAME() AS [Database] FROM sys.dm_db_persisted_sku_features";

    protected override void ProcessRecord()
    {
        // PS: $InputObject += Get-DbaDatabase ... per instance (typed-array append).
        List<object?> inputObjects = new List<object?>();
        if (InputObject is not null)
        {
            foreach (Microsoft.SqlServer.Management.Smo.Database db in InputObject)
                inputObjects.Add(db);
        }
        foreach (DbaInstanceParameter instance in SqlInstance ?? new DbaInstanceParameter[0])
        {
            try
            {
                foreach (PSObject? fetched in NestedCommand.InvokeScoped(this, GetDatabaseScript, instance, SqlCredential, Database, ExcludeDatabase, BoundVerbose(), BoundDebug()))
                {
                    if (fetched is not null)
                        inputObjects.Add(fetched);
                }
            }
            catch (PipelineStoppedException) { throw; }
            catch (RuntimeException ex) { StatementFault.Surface(this, ex, "Get-DbaDbFeatureUsage"); }
        }

        foreach (object? item in inputObjects)
        {
            Microsoft.SqlServer.Management.Smo.Database? db = (item is PSObject pso ? pso.BaseObject : item) as Microsoft.SqlServer.Management.Smo.Database;
            // PS: a null element logs empty interpolations, passes the accessibility
            // check ($null -eq $false is FALSE) and faults .Query() into the catch.
            WriteMessage(MessageLevel.Verbose, "Processing " + PsText(db) + " on " + PsText(db?.Parent?.Name));

            if (db is not null && !db.IsAccessible)
            {
                StopFunction("The database " + PsText(db) + " is not accessible. Skipping database.", continueLoop: true);
                continue;
            }

            try
            {
                if (db is null)
                    throw new RuntimeException("You cannot call a method on a null-valued expression.");
                // PS: $db.Query($sql) - the rows (an empty resultset's real $null
                // included) emit straight to the pipeline.
                foreach (PSObject? row in NestedCommand.InvokeScoped(this, DatabaseQueryScript, db, Sql))
                    WriteObject(row);
            }
            catch (PipelineStoppedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                StopFunction("Failure", errorRecord: StatementFault.Record(ex, "Get-DbaDbFeatureUsage"), continueLoop: true);
                continue;
            }
        }
    }

    /// <summary>PS string interpolation via LanguagePrimitives (invariant).</summary>
    private static string PsText(object? value)
    {
        if (value is null)
            return "";
        return (string)LanguagePrimitives.ConvertTo(value, typeof(string), CultureInfo.InvariantCulture);
    }

    /// <summary>A bound -Debug carrier for the hop scopes (W1-044 convention).</summary>
    private object? BoundDebug()
    {
        object? debug;
        if (MyInvocation.BoundParameters.TryGetValue("Debug", out debug))
            return LanguagePrimitives.IsTrue(debug);
        return null;
    }

    /// <summary>A bound -Verbose carrier for the hop scopes (W1-044 convention).</summary>
    private object? BoundVerbose()
    {
        object? verbose;
        if (MyInvocation.BoundParameters.TryGetValue("Verbose", out verbose))
            return LanguagePrimitives.IsTrue(verbose);
        return null;
    }

    // PS: Get-DbaDatabase called with ALL FOUR parameters verbatim (null when unbound).
    private const string GetDatabaseScript = """
param($__instance, $SqlCredential, $Database, $ExcludeDatabase, $__boundVerbose, $__boundDebug)
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    param($__instance, $SqlCredential, $Database, $ExcludeDatabase, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundVerbose) { $VerbosePreference = $(if ($__boundVerbose) { "Continue" } else { "SilentlyContinue" }) }
    if ($null -ne $__boundDebug) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }
    Get-DbaDatabase -SqlInstance $__instance -SqlCredential $SqlCredential -Database $Database -ExcludeDatabase $ExcludeDatabase
} $__instance $SqlCredential $Database $ExcludeDatabase $__boundVerbose $__boundDebug 3>&1
""";

    // PS: $db.Query($sql) - the Database-scoped ETS call (the W1-052 seam).
    private const string DatabaseQueryScript = """
param($db, $query)
$db.Query($query)
""";
}
