#nullable enable

using System;
using System.Collections.ObjectModel;
using System.Data.Common;
using System.Management.Automation;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Returns a SqlConnectionStringBuilder built from a connection string plus parameter
/// overrides. Port of public/New-DbaConnectionStringBuilder.ps1 (W1-028). The modern
/// Microsoft.Data.SqlClient builder is constructed natively; -Legacy rides nested PS
/// New-Object for engine type resolution per edition (W1-027 pattern), and both are driven
/// through the DbConnectionStringBuilder base (ShouldSerialize + virtual indexer). The
/// guard warns PER PIPELINE ITEM (the function's Stop-Function + return only exits that
/// process block), so ProcessRecord deliberately does NOT gate on Interrupted. Positions
/// 0-8 pin the PS implicit positional binding (non-switch parameters numbered
/// consecutively; switches never positional).
/// Surface pinned by migration/baselines/New-DbaConnectionStringBuilder.json.
/// </summary>
[Cmdlet(VerbsCommon.New, "DbaConnectionStringBuilder")]
public sealed class NewDbaConnectionStringBuilderCommand : DbaBaseCmdlet
{
    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    [Parameter(ValueFromPipeline = true, Position = 0)]
    public string[] ConnectionString { get; set; } = new[] { "" };

    [Parameter(Position = 1)]
    public string? ApplicationName { get; set; } = "dbatools Powershell Module";

    [Parameter(Position = 2)]
    [Alias("SqlInstance")]
    public string? DataSource { get; set; }

    [Parameter(Position = 3)]
    public PSCredential? SqlCredential { get; set; }

    [Parameter(Position = 4)]
    [Alias("Database")]
    public string? InitialCatalog { get; set; }

    [Parameter]
    public SwitchParameter IntegratedSecurity { get; set; }

    [Parameter(Position = 5)]
    public string? UserName { get; set; }

    [Parameter(Position = 6)]
    public string? Password { get; set; }

    [Parameter]
    [Alias("MARS")]
    public SwitchParameter MultipleActiveResultSets { get; set; }

    [Parameter(Position = 7)]
    [Alias("AlwaysEncrypted")]
    [ValidateSet("Enabled")]
    public string? ColumnEncryptionSetting { get; set; }

    [Parameter]
    public SwitchParameter Legacy { get; set; }

    [Parameter]
    public SwitchParameter NonPooledConnection { get; set; }

    [Parameter(Position = 8)]
    public string? WorkstationID { get; set; }

    protected override void BeginProcessing()
    {
        // PS param default: [string]$WorkstationID = $env:COMPUTERNAME at bind time.
        if (!TestBound("WorkstationID"))
            WorkstationID = Environment.GetEnvironmentVariable("COMPUTERNAME");
    }

    protected override void ProcessRecord()
    {
        bool pooling = !NonPooledConnection.ToBool();
        if (LanguagePrimitives.IsTrue(SqlCredential) && (LanguagePrimitives.IsTrue(UserName) || LanguagePrimitives.IsTrue(Password)))
        {
            // PS: Stop-Function ... -EnableException $EnableException; return - exits THIS
            // process block only, so the warning repeats for every pipeline item.
            StopFunction("You can only specify SQL Credential or Username/Password, not both.");
            return;
        }
        if (LanguagePrimitives.IsTrue(SqlCredential))
        {
            UserName = SqlCredential!.UserName;
            Password = SqlCredential.GetNetworkCredential().Password;
        }

        foreach (string cs in ConnectionString)
        {
            DbConnectionStringBuilder builder;
            if (Legacy.ToBool())
                builder = BuildLegacyBuilder(cs);
            else
                builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(cs);

            if (!builder.ShouldSerialize("Application Name"))
                builder["Application Name"] = ApplicationName;
            if (TestBound("DataSource"))
                builder["Data Source"] = DataSource;
            if (TestBound("InitialCatalog"))
                builder["Initial Catalog"] = InitialCatalog;
            if (TestBound("IntegratedSecurity"))
            {
                if (IntegratedSecurity.ToBool())
                    builder["Integrated Security"] = true;
                else
                    builder["Integrated Security"] = false;
            }
            if (LanguagePrimitives.IsTrue(UserName))
            {
                builder["User ID"] = UserName;
            }
            else if (!IntegratedSecurity.ToBool())
            {
                builder["Integrated Security"] = false;
            }
            if (LanguagePrimitives.IsTrue(Password))
                builder["Password"] = Password;
            if (!builder.ShouldSerialize("Workstation ID"))
                builder["Workstation ID"] = WorkstationID;
            if (TestBound("WorkstationID"))
                builder["Workstation ID"] = WorkstationID;
            if (TestBound("MultipleActiveResultSets"))
            {
                if (MultipleActiveResultSets.ToBool())
                    builder["MultipleActiveResultSets"] = true;
                else
                    builder["MultipleActiveResultSets"] = false;
            }
            if (PsString.Eq(ColumnEncryptionSetting, "Enabled"))
                builder["Column Encryption Setting"] = "Enabled";
            if (!builder.ShouldSerialize("Pooling"))
                builder["Pooling"] = pooling;
            if (TestBound("NonPooledConnection"))
                builder["Pooling"] = pooling;
            WriteObject(builder);
        }
    }

    /// <summary>
    /// Creates the -Legacy System.Data.SqlClient.SqlConnectionStringBuilder through nested
    /// PS (engine type resolution per edition), exactly like the function's New-Object.
    /// </summary>
    private DbConnectionStringBuilder BuildLegacyBuilder(string cs)
    {
        Collection<PSObject> output = NestedCommand.InvokeScoped(this, LegacyBuilderScript, cs);
        if (output.Count > 0 && output[0]?.BaseObject is DbConnectionStringBuilder builder)
            return builder;
        throw new InvalidOperationException("System.Data.SqlClient.SqlConnectionStringBuilder could not be created.");
    }

    private const string LegacyBuilderScript = """
param($cs)
New-Object System.Data.SqlClient.SqlConnectionStringBuilder $cs
""";
}
