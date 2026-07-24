#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Text.RegularExpressions;
using Dataplat.Dbatools.Connection;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;
using Dataplat.Dbatools.TabExpansion;

namespace Dataplat.Dbatools.Commands;

public sealed partial class ConnectDbaInstanceCommand
{
    private SmoConnectionRequest? _request;
    // Only one of these is reachable per framework - the service decides which - so both carry
    // explicit initializers to keep CS0649 quiet under warnings-as-errors.
    private bool _tryConnString = false;
    private bool _syntheticAccessTokenBound = false;

    protected override void ProcessRecord()
    {
        if (Interrupted)
        {
            return;
        }

        // PS: -in is case-insensitive and ValidateSet passes the user's casing through
        // (cross-model review 2026-07-07 finding 1).
        if (ConnectionService.RequiresSqlCredential(AuthenticationType) && SqlCredential is null)
        {
            StopFunction($"AuthenticationType {AuthenticationType} requires SqlCredential.");
            return;
        }

        // if tenant is specified with a GUID username such as 21f5633f-6776-4bab-b878-bbd5e3e5ed72 (for clientid)
        // PS: -not $AccessToken is truthiness, so an empty-string token counts as absent
        // (cross-model review 2026-07-07 finding 3).
        // Which strategy this framework supports is the service's call, not the command's.
        ServicePrincipalPlan? servicePrincipalPlan = ConnectionService.PlanServicePrincipalConnection(Tenant, AccessToken, SqlCredential);
        if (servicePrincipalPlan is not null)
        {
            try
            {
                WriteMessage(MessageLevel.Verbose, servicePrincipalPlan.VerboseMessage);
                if (servicePrincipalPlan.Strategy == ServicePrincipalStrategy.ConnectionStringRewrite)
                {
                    _tryConnString = true;
                }
                else
                {
                    // New-DbaAzAccessToken is still a PS function during the hybrid; the nested
                    // invocation runs in this cmdlet's session state, exactly like the PS call did.
                    Collection<PSObject> tokenResults = InvokeCommand.InvokeScript(
                        servicePrincipalPlan.TokenScript,
                        Tenant, SqlCredential);
                    AccessToken = tokenResults.Count > 0 ? tokenResults[0] : null;
                    // PS: $PSBoundParameters.Tenant = $Tenant = $null; $PSBoundParameters.SqlCredential = $SqlCredential = $null;
                    //     $PSBoundParameters.AccessToken = $AccessToken
                    Tenant = null;
                    SqlCredential = null;
                    _syntheticAccessTokenBound = true;
                }
            }
            catch (Exception ex)
            {
                string? errormessage = ConnectionService.GetDeepErrorMessage(ex);
                StopFunction($"Failed to get access token for Azure SQL DB ({errormessage})");
                return;
            }
        }

        WriteMessage(MessageLevel.Debug, "Starting process block");

        if (_request is null)
        {
            _request = BuildRequest();
        }

        foreach (DbaInstanceParameter rawInstance in SqlInstance)
        {
            DbaInstanceParameter instance = rawInstance;

            if (_tryConnString)
            {
                // The instance becomes an Active Directory Service Principal connection string
                // (PR #7610); the plain-text unwrap at this boundary matches the PS source.
                string azureserver = instance.InputObject.ToString()!;
                string clientSecret = SqlCredential!.GetNetworkCredential().Password;
                instance = new DbaInstanceParameter(ConnectionService.BuildServicePrincipalConnectionString(azureserver, _request.Database, SqlCredential.UserName, clientSecret));
            }

            _request.Instance = instance;

            ConnectionResolution resolution;
            try
            {
                resolution = ConnectionService.ResolveInstance(_request);
            }
            catch (ConnectionResolutionException ex)
            {
                switch (ex.Kind)
                {
                    case ConnectionResolutionFailure.ConnectFailure:
                        // PS: Stop-Function -Target $instance -Message "Failure" -Category ConnectionError -ErrorRecord $connectionError -Continue
                        StopFunction("Failure", target: instance, errorRecord: BuildConnectionErrorRecord(ex, instance), category: ErrorCategory.ConnectionError, continueLoop: true);
                        continue;
                    case ConnectionResolutionFailure.AzureUnsupported:
                        // PS: Stop-Function -Target $instance -Message "Azure SQL Database not supported" -Continue
                        StopFunction(ex.Message, target: instance, continueLoop: true);
                        continue;
                    case ConnectionResolutionFailure.MinimumVersion:
                        // PS: Stop-Function -Target $instance -Message "SQL Server version N required - X not supported." -Continue
                        StopFunction(ex.Message, target: instance, continueLoop: true);
                        continue;
                    case ConnectionResolutionFailure.AccessTokenConversion:
                        // PS: Stop-Function -Target $instance -Message "Failed to convert SecureString AccessToken to plain text: ..." -Continue
                        StopFunction(ex.Message, target: instance, continueLoop: true);
                        continue;
                    case ConnectionResolutionFailure.WindowsCredentialOnUnix:
                    default:
                        // PS: Stop-Function -Target $instance -Message "Cannot use Windows credentials ..." followed by
                        // return - the remaining instances are NOT processed (no -Continue in the source).
                        StopFunction(ex.Message, target: instance);
                        return;
                }
            }

            if (resolution.SqlConnection is not null)
            {
                // PS: $server.ConnectionContext.SqlConnectionObject; continue
                WriteObject(resolution.SqlConnection);
                continue;
            }

            WriteObject(resolution.Server);

            if (resolution.IsNewConnection && !DedicatedAdminConnection)
            {
                ConnectionService.RegisterInstanceForTepp(resolution.Instance, resolution.Server);

                // Update lots of registered stuff
                // Default for [Dataplat.Dbatools.TabExpansion.TabExpansionHost]::TeppSyncDisabled is $true, so will not run by default
                // Must be explicitly activated with [Dataplat.Dbatools.TabExpansion.TabExpansionHost]::TeppSyncDisabled = $false to run
                if (!TabExpansionHost.TeppSyncDisabled)
                {
                    // Variable $FullSmoName is used inside the script blocks, so we have to set
                    string fullSmoName = resolution.Instance.FullSmoName.ToLowerInvariant();
                    WriteMessage(MessageLevel.Debug, $"Will run Invoke-TEPPCacheUpdate for FullSmoName = {fullSmoName}");
                    SessionState.PSVariable.Set("FullSmoName", fullSmoName);
                    foreach (ScriptBlock scriptBlock in TabExpansionHost.TeppGatherScriptsFast)
                    {
                        if (!InvokeTeppCacheUpdate(scriptBlock))
                        {
                            break;
                        }
                    }
                }

                ConnectionService.ApplyDefaultInitFields(resolution.Server, resolution.IsAzure, MessageSink);
            }

            ConnectionService.RegisterConnection(resolution.Server.ConnectionContext.ConnectionString, resolution.Server, MessageSink);
            WriteMessage(MessageLevel.Debug, "We are finished with this instance");
        }
    }

    private SmoConnectionRequest BuildRequest()
    {
        SmoConnectionRequest request = new()
        {
            SqlCredential            = SqlCredential,
            Database                 = Database,
            ApplicationIntent        = ApplicationIntent,
            AzureUnsupported         = AzureUnsupported,
            BatchSeparator           = BatchSeparator,
            ClientName               = ClientName,
            ConnectTimeout           = ConnectTimeout,
            EncryptConnection        = EncryptConnection,
            FailoverPartner          = FailoverPartner,
            LockTimeout              = LockTimeout,
            MaxPoolSize              = MaxPoolSize,
            MinPoolSize              = MinPoolSize,
            MinimumVersion           = MinimumVersion,
            MultipleActiveResultSets = MultipleActiveResultSets,
            MultiSubnetFailover      = MultiSubnetFailover,
            NetworkProtocol          = NetworkProtocol,
            NonPooledConnection      = NonPooledConnection,
            PacketSize               = PacketSize,
            PooledConnectionLifetime = PooledConnectionLifetime,
            SqlExecutionModes        = SqlExecutionModes,
            StatementTimeout         = StatementTimeout,
            TrustServerCertificate   = TrustServerCertificate,
            AllowTrustServerCertificate = AllowTrustServerCertificate,
            WorkstationId            = WorkstationId,
            AlwaysEncrypted          = AlwaysEncrypted,
            AppendConnectionString   = AppendConnectionString,
            SqlConnectionOnly        = SqlConnectionOnly,
            AzureDomain              = AzureDomain,
            AccessToken              = AccessToken,
            AuthenticationType       = AuthenticationType,
            DedicatedAdminConnection = DedicatedAdminConnection,
            MessageCallback          = MessageSink
        };

        HashSet<string> bound = new(StringComparer.OrdinalIgnoreCase);
        foreach (string key in MyInvocation.BoundParameters.Keys)
        {
            bound.Add(key);
        }
        if (_syntheticAccessTokenBound)
        {
            // PS: $PSBoundParameters.AccessToken = $AccessToken after the token acquisition -
            // the key becomes "bound" for the Test-Bound checks of the ignored-parameter warnings.
            bound.Add("AccessToken");
        }
        request.BoundParameters = bound;

        // The Get-DbatoolsConfigValue parameter defaults of the PS source, resolved for
        // everything the caller did not bind (architecture.md section 4.6).
        request.ApplyConfigurationDefaults();
        return request;
    }

    private void MessageSink(MessageLevel level, string message)
    {
        WriteMessage(level, message);
    }

    private ErrorRecord BuildConnectionErrorRecord(ConnectionResolutionException failure, DbaInstanceParameter instance)
    {
        // The PS source passed the caught ErrorRecord ($connectionError); its category was
        // NotSpecified so Stop-Function's -Category ConnectionError won the category vote
        // (the DbaBaseCmdlet truth table replicates that).
        Exception inner = failure.InnerException ?? failure;
        return new ErrorRecord(inner, "dbatools_Connect-DbaInstance", ErrorCategory.NotSpecified, instance);
    }

    // The begin-block utility function of the PS source, translated: invokes one TEPP gather
    // script with the same triage. Returns false when the loop must stop (the PS AppVeyor /
    // DeveloperMode branch raised a binding error that aborted the foreach).
    private bool InvokeTeppCacheUpdate(ScriptBlock scriptBlock)
    {
        try
        {
            // PS: [ScriptBlock]::Create($scriptBlock).Invoke() - recreated so it binds to the
            // scope where $FullSmoName was just set; InvokeScript gives the same binding.
            InvokeCommand.InvokeScript(scriptBlock.ToString());
            return true;
        }
        catch (PipelineStoppedException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // If the SQL Server version doesn't support the feature, we ignore it and silently continue
            // (the PS source matched $_.Exception.InnerException.InnerException; the compiled
            // invocation wraps differently, so the whole chain is checked for the same type).
            Exception? current = ex;
            while (current is not null)
            {
                if (current.GetType().FullName == "Microsoft.SqlServer.Management.Sdk.Sfc.InvalidVersionEnumeratorException")
                {
                    return true;
                }
                current = current.InnerException;
            }

            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPVEYOR_BUILD_FOLDER")) || MessageHost.DeveloperMode)
            {
                // PS source: `Stop-Function -Message` (missing argument - and note the
                // [MEssageHost] typo) - a latent parameter-binding error that surfaced as an
                // error record and aborted the TEPP foreach. Replicated as a logged failure
                // that stops the loop without terminating the pipeline.
                WriteMessage(MessageLevel.Warning, $"Failed TEPP Caching: {ExtractTeppScriptName(scriptBlock)}");
                return false;
            }

            // PS: Write-Message -Level Warning -Message "Failed TEPP Caching: ..." -ErrorRecord $_ 3>$null
            // The 3>$null suppressed the warning display while it still reached the logs.
            WriteSuppressedWarning($"Failed TEPP Caching: {ExtractTeppScriptName(scriptBlock)}", ex);
            return true;
        }
    }

    private static string ExtractTeppScriptName(ScriptBlock scriptBlock)
    {
        // PS: $scriptBlock.ToString() | Select-String '"(.*?)"' | ForEach-Object { $_.Matches[0].Groups[1].Value }
        Match match = Regex.Match(scriptBlock.ToString(), "\"(.*?)\"");
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private void WriteSuppressedWarning(string message, Exception exception)
    {
        // Write-Message ... 3>$null equivalent: the text reaches LogHost and the error log but
        // not the warning stream (the same mechanism StopFunction uses under EnableException).
        MessageService.MessageRequest request = new()
        {
            Level                  = MessageLevel.Warning,
            Message                = message,
            FunctionName           = "Connect-DbaInstance",
            ModuleName             = "dbatools",
            Exception              = exception,
            EnableException        = EnableException.ToBool(),
            SuppressWarningDisplay = true,
            File                   = MyInvocation.ScriptName,
            Line                   = MyInvocation.ScriptLineNumber
        };
        MessageService.Write(this, request);
    }
}
