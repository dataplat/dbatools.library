#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Builds an SMO Transfer object describing a database-to-database copy. Port of
/// public/New-DbaDbTransfer.ps1; the workflow remains a module-scoped PowerShell compatibility hop.
///
/// THE FIRST THREE-BLOCK ROW IN THIS SATELLITE: begin (:151-153), process (:154-171) and end
/// (:172-272), which changes the carry shape rather than just adding a hop. All three hops get fresh
/// scopes, so anything one block establishes for another must be carried explicitly.
///
/// $objectCollection IS A begin -> process -> end ACCUMULATOR. It is created ONCE in begin (:152) as
/// a System.Collections.ArrayList, mutated per record in process (:168, "$objectCollection.Add($object)"),
/// and consumed in end (:188, "foreach ($object in $objectCollection)"). It therefore has to ride
/// begin into process, survive every record, and reach end intact. Because it is an ArrayList, the
/// carried value is the same INSTANCE and .Add() accumulates naturally through the shared reference -
/// but the carry itself is still explicit, and end receives it rather than rebuilding it.
///
/// A TOOLING BLIND SPOT, RECORDED RATHER THAN LEFT IMPLIED: migration/tools/Find-AccumulatorCarry.ps1
/// reports NOTHING for this row. It detects "+=" targets, and this accumulates by METHOD CALL on an
/// ArrayList - no "+=" exists. Find-ConditionalCarry.ps1 does not cover it either, since that reasons
/// about assignment PLACEMENT, not mutation through a reference. This is the second known blind spot
/// in that pair, after the preference-variable class on W2-149. A clean tool run is not a clean row.
///
/// NO ShouldProcess GATE AT ALL. The source declares no SupportsShouldProcess - it carries a
/// SuppressMessage for PSUseShouldProcessForStateChangingFunctions instead - so there is no gate to
/// route and no $__realCmdlet parameter, and the C# cmdlet declares no SupportsShouldProcess either.
///
/// NO INTERRUPT BRIDGE: this source contains NO Test-FunctionInterrupt, so the non-Continue guards in
/// process (:156, :160, :165) and end (:176, :180, :183) simply re-evaluate. Note the end block runs
/// ONCE per invocation regardless, so its guards fire once by construction.
///
/// FIVE Test-Bound SITES BECOME CARRIED CALLER-BOUNDNESS FLAGS, SPLIT ACROSS TWO BLOCKS: SqlInstance
/// (:155) and Database (:159) in process; DestinationDatabase (:225), SchemaOnly (:268) and DataOnly
/// (:269) in end. The last two are load-bearing rather than cosmetic - they gate
/// "$transfer.CopyData = -not $SchemaOnly" and "$transfer.CopySchema = -not $DataOnly", so BOUNDNESS,
/// not truthiness, decides whether the property is touched at all. Passing -SchemaOnly:$false is
/// therefore different from omitting it, which a value test would collapse.
///
/// DEF-007 BIND-TIME DEFAULT: "$DestinationDatabase = $Database" (:133) derives one parameter's
/// default from another, which a C# property initializer cannot express. It is resolved at bind time
/// in the hop, and its boundness flag at :225 still reflects what the CALLER passed rather than the
/// derived value - otherwise the "Initial Catalog" fallback at :225 would stop firing.
///
/// POSITIONS 0-10, with the three switches unpositioned. Recorded here because I first got this
/// WRONG in the opposite direction and the surface diff caught it. My baseline dump read
/// "$_.sets.__AllParameterSets.position" and printed every position EMPTY, so I concluded the
/// command had none and shipped a port with no Position attributes; the compiled surface diff then
/// reported ELEVEN Breaking findings. The cause is that this command declares
/// DefaultParameterSetName = "Default", so its parameters live under a set named "Default" and
/// __AllParameterSets does not exist - the dump was reading a null. THE LESSON IS NOT "confirm
/// against the baseline", which I did; it is that a baseline READER must resolve the actual set name
/// rather than assume __AllParameterSets, because the wrong lookup fails SILENTLY as an empty value
/// that looks like a legitimate answer.
///
/// -SqlInstance and -DestinationSqlInstance are SINGULAR DbaInstanceParameter, not arrays. -CopyAll
/// carries a 30-value ValidateSet, preserved verbatim. The three switches (CopyAllObjects,
/// SchemaOnly, DataOnly) and the inherited EnableException cross as SwitchParameter OBJECTS received
/// untyped, per B's combined rule. In-hop Stop-Function calls carry -FunctionName. The command
/// returns an Smo.Transfer, declared via OutputType. Surface pinned by
/// migration/baselines/New-DbaDbTransfer.json.
/// </summary>
[Cmdlet(VerbsCommon.New, "DbaDbTransfer", DefaultParameterSetName = "Default")]
[OutputType(typeof(Microsoft.SqlServer.Management.Smo.Transfer))]
public sealed class NewDbaDbTransferCommand : DbaBaseCmdlet
{
    /// <summary>The source SQL Server instance.</summary>
    [Parameter(Position = 0)]
    public DbaInstanceParameter? SqlInstance { get; set; }

    /// <summary>Alternative credential for the source instance.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The destination SQL Server instance.</summary>
    [Parameter(Position = 2)]
    public DbaInstanceParameter? DestinationSqlInstance { get; set; }

    /// <summary>Alternative credential for the destination instance.</summary>
    [Parameter(Position = 3)]
    public PSCredential? DestinationSqlCredential { get; set; }

    /// <summary>The source database.</summary>
    [Parameter(Position = 4)]
    [PsStringCast]
    public string? Database { get; set; }

    /// <summary>The destination database; defaults to the source database at bind time.</summary>
    [Parameter(Position = 5)]
    [PsStringCast]
    public string? DestinationDatabase { get; set; }

    /// <summary>Bulk copy batch size.</summary>
    [Parameter(Position = 6)]
    public int BatchSize { get; set; } = 50000;

    /// <summary>Bulk copy timeout in seconds.</summary>
    [Parameter(Position = 7)]
    public int BulkCopyTimeOut { get; set; } = 5000;

    /// <summary>Scripting options applied to the transfer.</summary>
    [Parameter(Position = 8)]
    public Microsoft.SqlServer.Management.Smo.ScriptingOptions? ScriptingOption { get; set; }

    /// <summary>SMO objects to transfer, piped in.</summary>
    [Parameter(ValueFromPipeline = true, Position = 9)]
    public Microsoft.SqlServer.Management.Smo.NamedSmoObject[]? InputObject { get; set; }

    /// <summary>Transfer every object in the database.</summary>
    [Parameter]
    public SwitchParameter CopyAllObjects { get; set; }

    /// <summary>Object categories to transfer wholesale.</summary>
    [Parameter(Position = 10)]
    [ValidateSet("FullTextCatalogs", "FullTextStopLists", "SearchPropertyLists", "Tables",
        "Views", "StoredProcedures", "UserDefinedFunctions", "UserDefinedDataTypes", "UserDefinedTableTypes",
        "PlanGuides", "Rules", "Defaults", "Users", "Roles", "PartitionSchemes", "PartitionFunctions",
        "XmlSchemaCollections", "SqlAssemblies", "UserDefinedAggregates", "UserDefinedTypes", "Schemas",
        "Synonyms", "Sequences", "DatabaseTriggers", "DatabaseScopedCredentials", "ExternalFileFormats",
        "ExternalDataSources", "Logins", "ExternalLibraries")]
    [PsStringArrayCast]
    public string[]? CopyAll { get; set; }

    /// <summary>Transfer schema only; suppresses data copy.</summary>
    [Parameter]
    public SwitchParameter SchemaOnly { get; set; }

    /// <summary>Transfer data only; suppresses schema copy.</summary>
    [Parameter]
    public SwitchParameter DataOnly { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // The $objectCollection ArrayList, carried begin -> process -> end; opaque to C#.
    private Hashtable? _state;

    protected override void BeginProcessing()
    {
        if (Interrupted)
            return;

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, BeginScript,
            EnableException, BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
        {
            if (item?.BaseObject is Hashtable sentinel && sentinel.ContainsKey("__newDbaDbTransferBegin"))
            {
                _state = sentinel["__newDbaDbTransferBegin"] as Hashtable;
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

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, ProcessScript,
            SqlInstance, Database, InputObject, EnableException, _state,
            MyInvocation.BoundParameters.ContainsKey("SqlInstance"),
            MyInvocation.BoundParameters.ContainsKey("Database"),
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
        {
            if (item?.BaseObject is Hashtable sentinel && sentinel.ContainsKey("__newDbaDbTransferProcess"))
            {
                _state = sentinel["__newDbaDbTransferProcess"] as Hashtable;
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

    protected override void EndProcessing()
    {
        if (Interrupted)
            return;

        // Streaming, not buffered (DEF-001): the end block emits the Transfer object it built, and a
        // buffered hop would discard output already produced if a later statement terminated the hop
        // under -EnableException.
        NestedCommand.InvokeScopedStreaming(this, item =>
        {
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                RemoveHopErrorBookkeeping(nestedError);
                WriteError(nestedError);
                return;
            }
            WriteObject(item);
        }, EndScript,
            SqlInstance, SqlCredential, DestinationSqlInstance, DestinationSqlCredential,
            Database, DestinationDatabase, BatchSize, BulkCopyTimeOut, ScriptingOption,
            CopyAllObjects, CopyAll, SchemaOnly, DataOnly, EnableException, _state,
            MyInvocation.BoundParameters.ContainsKey("DestinationDatabase"),
            MyInvocation.BoundParameters.ContainsKey("SchemaOnly"),
            MyInvocation.BoundParameters.ContainsKey("DataOnly"),
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

    // PS: the begin block VERBATIM. Its single statement creates the ArrayList that process fills
    // and end consumes; the sentinel carries that INSTANCE out so .Add() accumulates on the shared
    // reference across every later hop.
    private const string BeginScript = """
param($EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param($EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    . {
        $objectCollection = New-Object System.Collections.ArrayList
    }

    $__oc = Get-Variable -Name objectCollection -Scope 0 -ErrorAction Ignore
    @{ __newDbaDbTransferBegin = @{ ObjectCollection = $(if ($__oc) { $__oc.Value } else { $null }) } }
} $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
    // PS: the process block VERBATIM, dot-sourced so its three early returns exit only the body.
    // Edits: two Test-Bound probes become carried boundness flags, plus -FunctionName stamps.
    // $objectCollection is restored from the carry BEFORE the body so .Add() lands on the same
    // ArrayList begin created, then handed back for the next record and ultimately for end.
    private const string ProcessScript = """
param($SqlInstance, $Database, $InputObject, $EnableException, $__state, $__boundSqlInstance, $__boundDatabase, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter]$SqlInstance, [string]$Database, [Microsoft.SqlServer.Management.Smo.NamedSmoObject[]]$InputObject, $EnableException, $__state, $__boundSqlInstance, $__boundDatabase, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    # begin's ArrayList - the same instance, so .Add() accumulates across records
    $objectCollection = $__state.ObjectCollection

    . {
        if (-not $__boundSqlInstance) {
            Stop-Function -Message "Source instance was not specified" -FunctionName New-DbaDbTransfer
            return
        }
        if (-not $__boundDatabase) {
            Stop-Function -Message "Source database was not specified" -FunctionName New-DbaDbTransfer
            return
        }
        foreach ($object in $InputObject) {
            if (-not $object) {
                Stop-Function -Message "Object is empty" -FunctionName New-DbaDbTransfer
                return
            }
            $objectCollection.Add($object) | Out-Null
        }

    }

    $__oc = Get-Variable -Name objectCollection -Scope 0 -ErrorAction Ignore
    @{ __newDbaDbTransferProcess = @{ ObjectCollection = $(if ($__oc) { $__oc.Value } else { $null }) } }
} $SqlInstance $Database $InputObject $EnableException $__state $__boundSqlInstance $__boundDatabase $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
    // PS: the end block VERBATIM, dot-sourced so its early returns exit only the body. Edits: three
    // Test-Bound probes become carried boundness flags, plus -FunctionName stamps.
    //
    // $DestinationDatabase is the DEF-007 bind-time default "= $Database" (:133). It is resolved here
    // rather than by a C# initializer, and $__boundDestinationDatabase still reports what the CALLER
    // passed so the "Initial Catalog" fallback at :225 keeps firing.
    private const string EndScript = """
param($SqlInstance, $SqlCredential, $DestinationSqlInstance, $DestinationSqlCredential, $Database, $DestinationDatabase, $BatchSize, $BulkCopyTimeOut, $ScriptingOption, $CopyAllObjects, $CopyAll, $SchemaOnly, $DataOnly, $EnableException, $__state, $__boundDestinationDatabase, $__boundSchemaOnly, $__boundDataOnly, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter]$SqlInstance, [PSCredential]$SqlCredential, [Dataplat.Dbatools.Parameter.DbaInstanceParameter]$DestinationSqlInstance, [PSCredential]$DestinationSqlCredential, [string]$Database, [string]$DestinationDatabase, [int]$BatchSize, [int]$BulkCopyTimeOut, [Microsoft.SqlServer.Management.Smo.ScriptingOptions]$ScriptingOption, $CopyAllObjects, [string[]]$CopyAll, $SchemaOnly, $DataOnly, $EnableException, $__state, $__boundDestinationDatabase, $__boundSchemaOnly, $__boundDataOnly, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    # DEF-007: the source's "[string]$DestinationDatabase = $Database" bind-time default
    if (-not $__boundDestinationDatabase) { $DestinationDatabase = $Database }
    # the accumulator begin created and every process record filled
    $objectCollection = $__state.ObjectCollection

    . {
        try {
            $sourceDb = Get-DbaDatabase -SqlInstance $SqlInstance -SqlCredential $SqlCredential -Database $Database -EnableException
        } catch {
            Stop-Function -Message "Failed to retrieve database from the source instance $SqlInstance" -ErrorRecord $_ -FunctionName New-DbaDbTransfer
            return
        }
        if (-not $sourceDb) {
            Stop-Function -Message "Database $Database not found on $SqlInstance" -FunctionName New-DbaDbTransfer
            return
        } elseif ($sourceDb.Count -gt 1) {
            Stop-Function -Message "More than one database found on $SqlInstanced with the parameters provided" -FunctionName New-DbaDbTransfer
            return
        }
        # Create transfer object and define properties based on parameters
        $transfer = New-Object Microsoft.SqlServer.Management.Smo.Transfer($sourceDb)
        foreach ($object in $objectCollection) {
            $transfer.ObjectList.Add($object) | Out-Null
        }
        $transfer.BatchSize = $BatchSize
        $transfer.BulkCopyTimeOut = $BulkCopyTimeOut
        $transfer.CopyAllObjects = $CopyAllObjects
        foreach ($copyType in $CopyAll) {
            $transfer."CopyAll$copyType" = $true
        }
        if ($ScriptingOption) { $transfer.Options = $ScriptingOption }

        # Add destination connection parameters
        # Infer SSL/TLS settings from source connection
        $sourceTrustCert = $sourceDb.Parent.ConnectionContext.TrustServerCertificate
        $sourceEncrypt = $sourceDb.Parent.ConnectionContext.EncryptConnection

        if ($DestinationSqlInstance.IsConnectionString) {
            $connString = $DestinationSqlInstance.InputObject
        } elseif ($DestinationSqlInstance.Type -eq 'RegisteredServer' -and $DestinationSqlInstance.InputObject.ConnectionString) {
            $connString = $DestinationSqlInstance.InputObject.ConnectionString
        } elseif ($DestinationSqlInstance.Type -eq 'Server' -and $DestinationSqlInstance.InputObject.ConnectionContext.ConnectionString) {
            $connString = $DestinationSqlInstance.InputObject.ConnectionContext.ConnectionString
        } else {
            $transfer.DestinationServer = $DestinationSqlInstance.InputObject
            $transfer.DestinationLoginSecure = $true
        }

        # Build connection string for destination with SSL settings from source
        $destServer = $null
        $destDatabase = $DestinationDatabase
        $destIntegratedSecurity = $true
        $destUserName = $null
        $destPassword = $null

        if ($connString) {
            $connStringBuilder = New-Object Microsoft.Data.SqlClient.SqlConnectionStringBuilder $connString
            $destServer = if ($srv = $connStringBuilder["Data Source"]) { $srv } else { "localhost" }
            if (($db = $connStringBuilder["Initial Catalog"]) -and (-not $__boundDestinationDatabase)) {
                $destDatabase = $db
            }
            $destIntegratedSecurity = $connStringBuilder["Integrated Security"]
            $destUserName = $connStringBuilder["User ID"]
            $destPassword = $connStringBuilder["Password"]
        } else {
            $destServer = $DestinationSqlInstance.InputObject
        }

        # Override with DestinationSqlCredential if provided
        if ($DestinationSqlCredential) {
            $destIntegratedSecurity = $false
            $destUserName = $DestinationSqlCredential.UserName
            $destPassword = $DestinationSqlCredential.GetNetworkCredential().Password
        }

        # Build connection string with SSL settings from source
        $destConnStringBuilder = New-Object Microsoft.Data.SqlClient.SqlConnectionStringBuilder
        $destConnStringBuilder["Data Source"] = $destServer
        $destConnStringBuilder["Initial Catalog"] = $destDatabase
        $destConnStringBuilder["Integrated Security"] = $destIntegratedSecurity
        $destConnStringBuilder["TrustServerCertificate"] = $sourceTrustCert
        $destConnStringBuilder["Encrypt"] = $sourceEncrypt

        if (-not $destIntegratedSecurity) {
            $destConnStringBuilder["User ID"] = $destUserName
            $destConnStringBuilder["Password"] = $destPassword
        }

        # Create ServerConnection with SSL settings
        $destSqlConnection = New-Object Microsoft.Data.SqlClient.SqlConnection $destConnStringBuilder.ConnectionString
        $destServerConnection = New-Object Microsoft.SqlServer.Management.Common.ServerConnection $destSqlConnection
        $transfer.DestinationServerConnection = $destServerConnection

        # Also set individual properties for backward compatibility
        $transfer.DestinationServer = $destServer
        $transfer.DestinationDatabase = $destDatabase
        $transfer.DestinationLoginSecure = $destIntegratedSecurity
        if (-not $destIntegratedSecurity) {
            $transfer.DestinationLogin = $destUserName
            $transfer.DestinationPassword = $destPassword
        }
        if ($__boundSchemaOnly) { $transfer.CopyData = -not $SchemaOnly }
        if ($__boundDataOnly) { $transfer.CopySchema = -not $DataOnly }

        return $transfer
    }
} $SqlInstance $SqlCredential $DestinationSqlInstance $DestinationSqlCredential $Database $DestinationDatabase $BatchSize $BulkCopyTimeOut $ScriptingOption $CopyAllObjects $CopyAll $SchemaOnly $DataOnly $EnableException $__state $__boundDestinationDatabase $__boundSchemaOnly $__boundDataOnly $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}