#nullable enable

using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Management.Automation;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Generates OAuth2 access tokens for Azure SQL Database and other Azure services. Port of
/// public/New-DbaAzAccessToken.ps1 (W1-025). The three Type branches run their PS bodies
/// VERBATIM inside the dbatools script-module scope (Invoke-TlsWebRequest is a private
/// module function; ADAL and PsObjectIRenewableToken resolve dynamically), so per-edition
/// failure texts, the runtime Add-Type compilation, and the token object identity match the
/// function byte-for-byte. Lab-pinned behaviors (2026-07-12 probes az1-az4, both editions):
/// the SP/RSP begin guard warns "You must specify a Credential and Tenant ..." and stops;
/// process failures warn "Failure | (deepest)" and CONTINUE THE CALLER'S LOOP (Stop-Function
/// -Continue with no function-local loop; CallerFlow); on the Core edition the verbatim
/// Add-Type currently faults (CS1701 System.Runtime mismatch under a net9 host) and the raw
/// InvalidOperationException must unwind the calling script exactly like the function -
/// probe-az4 proved a propagating nested InvokeScript reproduces the caught type/message on
/// both editions, so the begin-block exception is deliberately NOT caught here. A user-passed
/// -Config is ALWAYS clobbered by the Subtype switch (every ValidateSet value has a case), so
/// $Config.Version is dead and the api-version pins to 2018-04-02, like the function.
/// PS function parameters are positional by default, so the port pins Positions 0-6.
/// Surface pinned by migration/baselines/New-DbaAzAccessToken.json.
/// </summary>
[Cmdlet(VerbsCommon.New, "DbaAzAccessToken")]
public sealed class NewDbaAzAccessTokenCommand : DbaBaseCmdlet
{
    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    [Parameter(Mandatory = true, Position = 0)]
    [ValidateSet("ManagedIdentity", "ServicePrincipal", "RenewableServicePrincipal")]
    public string Type { get; set; } = null!;

    // The duplicate ResourceManager entry is preserved from the PS ValidateSet.
    [Parameter(Position = 1)]
    [ValidateSet("AzureSqlDb", "ResourceManager", "DataLake", "EventHubs", "KeyVault", "ResourceManager", "ServiceBus", "Storage")]
    public string Subtype { get; set; } = "AzureSqlDb";

    [Parameter(Position = 2)]
    public object? Config { get; set; }

    [Parameter(Position = 3)]
    public PSCredential? Credential { get; set; }

    [Parameter(Position = 4)]
    public string? Tenant { get; set; }

    [Parameter(Position = 5)]
    public string? Thumbprint { get; set; }

    [Parameter(Position = 6)]
    [ValidateSet("CurrentUser", "LocalMachine")]
    public string? Store { get; set; }

    protected override void BeginProcessing()
    {
        // PS param defaults: Get-DbatoolsConfigValue runs at bind time when the parameter is
        // not bound; the [string] cast turns a null config into "". Defaults bypass
        // ValidateSet, so an out-of-set azure.certificate.store value assigns unvalidated.
        if (!TestBound("Tenant"))
            Tenant = LanguagePrimitives.ConvertTo<string>(GetConfigValue("azure.tenantid"));
        if (!TestBound("Thumbprint"))
            Thumbprint = LanguagePrimitives.ConvertTo<string>(GetConfigValue("azure.certificate.thumbprint"));
        if (!TestBound("Store"))
            Store = LanguagePrimitives.ConvertTo<string>(GetConfigValue("azure.certificate.store"));

        if (PsString.In(Type, "ServicePrincipal", "RenewableServicePrincipal"))
        {
            object? appid = GetConfigValue("azure.appid");
            object? clientsecret = GetConfigValue("azure.clientsecret");

            if (LanguagePrimitives.IsTrue(appid) && LanguagePrimitives.IsTrue(clientsecret) && !LanguagePrimitives.IsTrue(Credential))
                Credential = BuildConfigCredential(appid, clientsecret);

            if (!LanguagePrimitives.IsTrue(Credential) && !LanguagePrimitives.IsTrue(Tenant))
            {
                StopFunction("You must specify a Credential and Tenant when using ServicePrincipal or RenewableServicePrincipal");
                // PS: return exits begin - the Add-Type block and the Subtype switch are
                // both SKIPPED (lab probe az1: no Add-Type errors on a guard stop).
                return;
            }
        }

        if (PsString.Eq(Type, "RenewableServicePrincipal"))
        {
            // The function's begin compiles PsObjectIRenewableToken via Add-Type with the
            // EXACT source text below - byte-identical so the engine's source-keyed compile
            // cache treats function- and cmdlet-added types as the same. Failures propagate
            // raw and uncaught (probe-az4: the caller's catch sees the naked
            // InvalidOperationException on both editions; uncaught it unwinds the script,
            // exactly like the function under a net9 host where CS1701 faults the compile).
            using (NestedCommand.ShieldDefaultParameterValues(this))
                InvokeCommand.InvokeScript(false, ScriptBlock.Create(RenewableTokenTypeScript), null);
        }

        ResolveSubtypeConfig();
    }

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        try
        {
            if (PsString.Eq(Type, "ManagedIdentity"))
            {
                EmitAll(InvokeModuleScoped(ManagedIdentityScript, Config));
            }
            else if (PsString.Eq(Type, "ServicePrincipal"))
            {
#if NETFRAMEWORK
                // thanks to Jose M Jurado - MSFT for this code
                // https://blogs.msdn.microsoft.com/azuresqldbsupport/2018/05/10/lesson-learned-49-does-azure-sql-database-support-azure-active-directory-connections-using-service-principals/
                EmitAll(InvokeModuleScoped(ServicePrincipalScript, Credential, Tenant, Config));
#else
                // PS: if ($script:core) { Stop-Function "ServicePrincipal currently
                // unsupported in Core"; return } - the site sits INSIDE the try, so under
                // EnableException the throw lands in the function's own catch and rewraps as
                // "Failure | ..."; InnerCommand.Stop reproduces both modes. ($error keeps
                // one record here where PS keeps the intermediate too - accepted micro-skew
                // on this EnableException-only corner.)
                if (InnerCommand.Stop(this, "New-DbaAzAccessToken", EnableException.ToBool(), "ServicePrincipal currently unsupported in Core"))
                    return;
#endif
            }
            else if (PsString.Eq(Type, "RenewableServicePrincipal"))
            {
                EmitAll(InvokeModuleScoped(RenewableTokenNewScript, Credential, Tenant));
            }
        }
        catch (PipelineStoppedException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // PS: catch { Stop-Function -Message "Failure" -ErrorRecord $_ -Continue } -
            // with no loop inside process{}, the continue crosses into the CALLER's nearest
            // loop (lab probe az2 P5). Under EnableException StopFunction throws first.
            StopFunction("Failure", errorRecord: ToCaughtRecord(ex), continueLoop: true);
            CallerFlow.Continue(this);
        }
    }

    /// <summary>
    /// PS: switch ($Subtype) - every ValidateSet value has a case, so a user-passed -Config
    /// is always replaced with the one-key Resource table (@{} literal rides
    /// PsHashtable.Literal for the edition-split comparer).
    /// </summary>
    private void ResolveSubtypeConfig()
    {
        string resource;
        if (PsString.Eq(Subtype, "AzureSqlDb"))
            resource = "https://database.windows.net/";
        else if (PsString.Eq(Subtype, "ResourceManager"))
            resource = "https://management.azure.com/";
        else if (PsString.Eq(Subtype, "KeyVault"))
            resource = "https://vault.azure.net/";
        else if (PsString.Eq(Subtype, "DataLake"))
            resource = "https://datalake.azure.net/";
        else if (PsString.Eq(Subtype, "EventHubs"))
            resource = "https://eventhubs.azure.net/";
        else if (PsString.Eq(Subtype, "ServiceBus"))
            resource = "https://servicebus.azure.net/";
        else if (PsString.Eq(Subtype, "Storage"))
            resource = "https://storage.azure.com/";
        else
            return; // unreachable: ValidateSet covers every case

        Hashtable configTable = PsHashtable.Literal(1);
        configTable["Resource"] = resource;
        Config = configTable;
    }

    private void EmitAll(Collection<PSObject> output)
    {
        foreach (PSObject item in output)
            WriteObject(item);
    }

    /// <summary>
    /// Runs a branch body in the dbatools SCRIPT module scope (private functions, module
    /// $PSDefaultParameterValues resolution) via NestedCommand.InvokeScoped: the branch
    /// scripts merge warnings back (3>&1) and the helper re-emits them through THIS
    /// cmdlet's warning stream, so the caller's -WarningAction/-WarningVariable behave
    /// exactly like they did around the function (codex r1 F1).
    /// </summary>
    private Collection<PSObject> InvokeModuleScoped(string scriptText, params object?[] scriptArgs)
    {
        return NestedCommand.InvokeScoped(this, scriptText, scriptArgs);
    }

    /// <summary>PS: $x = (Get-DbatoolsConfigValue -FullName ...) - scalar for one output.</summary>
    private object? GetConfigValue(string fullName)
    {
        Hashtable splatValue = new();
        splatValue["FullName"] = fullName;
        Collection<PSObject> output = NestedCommand.Invoke(this, "Get-DbatoolsConfigValue", splatValue);
        if (output.Count == 0)
            return null;
        if (output.Count == 1)
            return output[0]?.BaseObject;
        object?[] many = new object?[output.Count];
        for (int i = 0; i < output.Count; i++)
            many[i] = output[i]?.BaseObject;
        return many;
    }

    /// <summary>
    /// PS: New-Object System.Management.Automation.PSCredential ($appid, $clientsecret) -
    /// verbatim, so a mistyped config pair (e.g. a plain-string secret) faults with the PS
    /// New-Object text at the same uncaught begin-block point.
    /// </summary>
    private PSCredential? BuildConfigCredential(object? appid, object? clientsecret)
    {
        Collection<PSObject> output = InvokeModuleScoped(ConfigCredentialScript, appid, clientsecret);
        if (output.Count > 0 && output[0] != null)
            return output[0].BaseObject as PSCredential;
        return null;
    }

    /// <summary>PS: catch { $_ } - a nested terminating error carries the original failing
    /// record; anything else wraps like the landed W1 ports.</summary>
    private static ErrorRecord ToCaughtRecord(Exception ex)
    {
        if (ex is InnerCommandException inner)
            return inner.FirstRecord;
        if (ex is RuntimeException runtime && runtime.ErrorRecord is not null)
            return runtime.ErrorRecord;
        return new ErrorRecord(ex, "New-DbaAzAccessToken", ErrorCategory.NotSpecified, null);
    }

    private const string ConfigCredentialScript = """
param($appid, $clientsecret)
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    param($appid, $clientsecret)
    New-Object System.Management.Automation.PSCredential ($appid, $clientsecret)
} $appid $clientsecret 3>&1
""";

    private const string ManagedIdentityScript = """
param($Config)
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    param($Config)
    $version = $Config.Version
    if (-not $version) {
        $version = "2018-04-02"
    }
    $resource = $Config.Resource
    $params = @{
        Uri     = "http://169.254.169.254/metadata/identity/oauth2/token?api-version=$version&resource=$resource"
        Method  = "GET"
        Headers = @{ Metadata = "true" }
    }
    $response = Invoke-TlsWebRequest @params -UseBasicParsing -ErrorAction Stop
    return ($response.Content | ConvertFrom-Json).access_token
} $Config 3>&1
""";

#if NETFRAMEWORK
    private const string ServicePrincipalScript = """
param($Credential, $Tenant, $Config)
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    param($Credential, $Tenant, $Config)
    $cred = New-Object Microsoft.IdentityModel.Clients.ActiveDirectory.ClientCredential -ArgumentList $Credential.UserName, $Credential.GetNetworkCredential().Password
    $context = New-Object Microsoft.IdentityModel.Clients.ActiveDirectory.AuthenticationContext -ArgumentList "https://login.windows.net/$Tenant"
    $result = $context.AcquireTokenAsync($Config.Resource, $cred)

    if ($result.Result.AccessToken) {
        return $result.Result.AccessToken
    } else {
        throw ($result.Exception | ConvertTo-Json | ConvertFrom-Json).InnerException.Message
    }
} $Credential $Tenant $Config 3>&1
""";
#endif

    private const string RenewableTokenNewScript = """
param($Credential, $Tenant)
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    param($Credential, $Tenant)
    New-Object PSObjectIRenewableToken -Property @{
        ClientSecret = $Credential.GetNetworkCredential().Password
        Resource     = "https://database.windows.net/"
        Tenant       = $Tenant
        UserID       = $Credential.UserName
    }
} $Credential $Tenant 3>&1
""";

    // The $source here-string content below is BYTE-IDENTICAL to
    // public/New-DbaAzAccessToken.ps1 lines 118-168 (indentation included): Add-Type keys
    // its compile cache on the exact source text, so the function and the cmdlet must
    // register the same type from the same string.
    private const string RenewableTokenTypeScript = """
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
            $source = @"
            using System;
            using Microsoft.SqlServer.Management.Common;
            using System.Management.Automation;
            using System.Collections.ObjectModel;
            using System.Management.Automation.Runspaces;

            public class PsObjectIRenewableToken : IRenewableToken {
                public String GetAccessToken() {
                    PowerShell psCmd = PowerShell.Create().AddScript(@"param(`$this)$({
                    $authority = "https://login.microsoftonline.com/$($this.Tenant)/oauth2/token"
                    $parameter = @{
                        grant_type='client_credentials'
                        client_id=$this.UserID
                        client_secret=$this.ClientSecret
                        resource=$this.Resource
                    }

                    $body = (@(foreach ($param in $parameter.GetEnumerator()) {
                        "$($param.key)=$([Uri]::EscapeDataString($param.Value.ToString()))"
                    }) -join '&')

                    $bearerInfo = Invoke-RestMethod -Uri $authority -Method Post -Body $body
                    $this.TokenExpiry = [DateTimeOffset]::FromUnixTimeSeconds($BearerInfo.expires_on)
                    return $bearerInfo.access_token
                    }.ToString().Replace('"','""'))").AddArgument(this);

                    Collection<string> results = psCmd.Invoke<string>();
                    if (psCmd.Streams.Error.Count > 0) {
                        throw psCmd.Streams.Error[0].Exception;
                    }

                    psCmd.Dispose();

                    if (results.Count == 1) {
                        return results[0];
                    } else {
                        return String.Empty;
                    }
                }

                public System.DateTimeOffset TokenExpiry { get; set;  }
                public String Resource { get; set; }
                public System.String Tenant { get; set; }
                public System.String UserId { get; set; }
                public string ClientSecret { get; set; }
            }
"@
            Add-Type -TypeDefinition $source -ReferencedAssemblies ([Microsoft.SqlServer.Management.Common.IRenewableToken].Assembly,
                [PowerShell].Assembly,
                [Microsoft.SqlServer.Management.Common.IRenewableToken].Assembly.GetReferencedAssemblies()[0])
}
""";
}
