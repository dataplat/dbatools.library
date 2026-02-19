using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Revokes permissions from SQL Server logins on database mirroring endpoints or availability groups.
    /// Supports Endpoint and AvailabilityGroup permission types with automatic login creation for Windows accounts.
    /// </summary>
    [Cmdlet("Revoke", "DbaAgPermission", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Low)]
    public class RevokeDbaAgPermissionCommand : DbaBaseCmdlet
    {
        #region Parameters

        /// <summary>
        /// The target SQL Server instance or instances.
        /// </summary>
        [Parameter()]
        public DbaInstanceParameter[] SqlInstance { get; set; }

        /// <summary>
        /// Login to the target instance using alternative credentials.
        /// </summary>
        [Parameter()]
        public PSCredential SqlCredential { get; set; }

        /// <summary>
        /// Specifies the SQL Server logins to remove permissions from.
        /// </summary>
        [Parameter()]
        public string[] Login { get; set; }

        /// <summary>
        /// Specifies which availability groups to target for permission revocation.
        /// </summary>
        [Parameter()]
        public string[] AvailabilityGroup { get; set; }

        /// <summary>
        /// Determines whether to revoke permissions on database mirroring endpoints or availability groups.
        /// </summary>
        [Parameter(Mandatory = true)]
        [ValidateSet("Endpoint", "AvailabilityGroup")]
        public string[] Type { get; set; }

        /// <summary>
        /// Specifies which permissions to revoke. Defaults to 'Connect'.
        /// </summary>
        [Parameter()]
        [ValidateSet("Alter", "Connect", "Control", "CreateAnyDatabase", "CreateSequence", "Delete", "Execute", "Impersonate", "Insert", "Receive", "References", "Select", "Send", "TakeOwnership", "Update", "ViewChangeTracking", "ViewDefinition")]
        public string[] Permission { get; set; } = new string[] { "Connect" };

        /// <summary>
        /// Accepts login objects from Get-DbaLogin pipeline input.
        /// Type is object[] because SMO types are loaded dynamically at runtime.
        /// </summary>
        [Parameter(ValueFromPipeline = true)]
        public object[] InputObject { get; set; }

        #endregion Parameters

        #region Static ScriptBlocks

        /// <summary>
        /// Script block for calling Connect-DbaInstance.
        /// </summary>
        private static readonly ScriptBlock _connectScript = ScriptBlock.Create(@"
param($si, $sc, $hasCred)
$params = @{ SqlInstance = $si }
if ($hasCred) { $params['SqlCredential'] = $sc }
Connect-DbaInstance @params
");

        /// <summary>
        /// Script block for revoking CreateAnyDatabase privilege via T-SQL.
        /// Note: PS1 had a bug using GRANT instead of REVOKE. Fixed here.
        /// </summary>
        private static readonly ScriptBlock _revokeCreateAnyDbScript = ScriptBlock.Create(@"
param($server, $agName)
$server.RevokeAvailabilityGroupCreateDatabasePrivilege($agName)
$server.Alter()
");

        /// <summary>
        /// Script block for Get-DbaLogin and optionally New-DbaLogin.
        /// </summary>
        private static readonly ScriptBlock _getOrCreateLoginsScript = ScriptBlock.Create(@"
param($server, $sc, $loginNames, $hasCred)
$params = @{ SqlInstance = $server }
if ($hasCred) { $params['SqlCredential'] = $sc }
$existingLogins = Get-DbaLogin @params -Login $loginNames
$result = @($existingLogins)
foreach ($name in $loginNames) {
    if ($name -notin $existingLogins.Name) {
        $result += New-DbaLogin -SqlInstance $server -Login $name -EnableException
    }
}
$result
");

        /// <summary>
        /// Script block for revoking endpoint permissions.
        /// </summary>
        private static readonly ScriptBlock _revokeEndpointPermScript = ScriptBlock.Create(@"
param($account, $perm)
$server = $account.Parent
$server.Endpoints.Refresh()
$endpoint = $server.Endpoints | Where-Object EndpointType -eq DatabaseMirroring
if (-not $endpoint) {
    throw ""DatabaseMirroring endpoint does not exist on $server""
}
$bigperms = New-Object Microsoft.SqlServer.Management.Smo.ObjectPermissionSet([Microsoft.SqlServer.Management.Smo.ObjectPermission]::$perm)
$endpoint.Revoke($bigperms, $account.Name)
[PSCustomObject]@{
    ComputerName = $account.ComputerName
    InstanceName = $account.InstanceName
    SqlInstance  = $account.SqlInstance
    Name         = $account.Name
    Permission   = $perm
    Type         = 'Revoke'
    Status       = 'Success'
}
");

        /// <summary>
        /// Script block for revoking availability group permissions.
        /// </summary>
        private static readonly ScriptBlock _revokeAgPermScript = ScriptBlock.Create(@"
param($account, $agNames, $perm)
$server = $account.Parent
$ags = Get-DbaAvailabilityGroup -SqlInstance $server -AvailabilityGroup $agNames
foreach ($ag in $ags) {
    $bigperms = New-Object Microsoft.SqlServer.Management.Smo.ObjectPermissionSet([Microsoft.SqlServer.Management.Smo.ObjectPermission]::$perm)
    $ag.Revoke($bigperms, $account.Name)
    [PSCustomObject]@{
        ComputerName = $account.ComputerName
        InstanceName = $account.InstanceName
        SqlInstance  = $account.SqlInstance
        Name         = $account.Name
        Permission   = $perm
        Type         = 'Revoke'
        Status       = 'Success'
    }
}
");

        #endregion Static ScriptBlocks

        /// <summary>
        /// Collected login objects across pipeline invocations.
        /// </summary>
        private List<object> _loginObjects = new List<object>();

        /// <summary>
        /// Collects pipeline InputObject items and resolves SqlInstance.
        /// </summary>
        protected override void ProcessRecord()
        {
            if (TestFunctionInterrupt())
                return;

            // Validate: either SqlInstance or InputObject must be provided
            if (TestBoundNot("SqlInstance", "InputObject") && InputObject == null)
            {
                StopFunction("You must supply either -SqlInstance or an Input Object");
                return;
            }

            // Validate: Login or AvailabilityGroup required with SqlInstance
            if (TestBound("SqlInstance") && !TestBound("Login") && !TestBound("AvailabilityGroup"))
            {
                StopFunction("You must specify one or more logins when using the SqlInstance parameter.");
                return;
            }

            // Validate: AvailabilityGroup required for AvailabilityGroup type
            if (Type != null && ArrayContains(Type, "AvailabilityGroup") && !TestBound("AvailabilityGroup"))
            {
                StopFunction("You must specify at least one availability group when using the AvailabilityGroup type.");
                return;
            }

            // Collect InputObject from pipeline
            if (InputObject != null)
            {
                foreach (object obj in InputObject)
                {
                    if (obj != null)
                        _loginObjects.Add(obj);
                }
            }

            // Process SqlInstance connections
            if (SqlInstance != null)
            {
                foreach (DbaInstanceParameter instance in SqlInstance)
                {
                    // Handle CreateAnyDatabase revocation (fixed PS1 bug: was checking undefined $perm, now checks Permission)
                    if (Permission != null && ArrayContains(Permission, "CreateAnyDatabase"))
                    {
                        object server;
                        try
                        {
                            Collection<PSObject> connResults = InvokeCommand.InvokeScript(
                                false, _connectScript, null,
                                new object[] { instance, SqlCredential, SqlCredential != null });

                            if (connResults == null || connResults.Count == 0) continue;
                            server = connResults[0];
                        }
                        catch (Exception ex)
                        {
                            StopFunction(
                                String.Format("Failed to connect to {0}", instance),
                                errorRecord: new ErrorRecord(ex, "RevokeDbaAgPermission_Connect", ErrorCategory.ConnectionError, instance),
                                target: instance, isContinue: true);
                            TestFunctionInterrupt();
                            continue;
                        }

                        if (AvailabilityGroup != null)
                        {
                            foreach (string ag in AvailabilityGroup)
                            {
                                try
                                {
                                    InvokeCommand.InvokeScript(
                                        false, _revokeCreateAnyDbScript, null,
                                        new object[] { server, ag });
                                }
                                catch (Exception ex)
                                {
                                    StopFunction(
                                        String.Format("Failure revoking CreateAnyDatabase for Availability Group {0}", ag),
                                        errorRecord: new ErrorRecord(ex, "RevokeDbaAgPermission_CreateAnyDb", ErrorCategory.InvalidOperation, instance),
                                        target: instance);
                                    return;
                                }
                            }
                        }
                    }
                    if (Login != null && Login.Length > 0)
                    {
                        // Get/create logins
                        try
                        {
                            Collection<PSObject> loginResults = InvokeCommand.InvokeScript(
                                false, _getOrCreateLoginsScript, null,
                                new object[] { instance, SqlCredential, Login, SqlCredential != null });

                            if (loginResults != null)
                            {
                                foreach (PSObject login in loginResults)
                                {
                                    if (login != null)
                                        _loginObjects.Add(login);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            StopFunction(
                                String.Format("Failure creating logins on {0}", instance),
                                errorRecord: new ErrorRecord(ex, "RevokeDbaAgPermission_CreateLogin", ErrorCategory.InvalidOperation, instance),
                                target: instance);
                            return;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Revokes permissions on endpoints and/or availability groups for each login.
        /// </summary>
        protected override void EndProcessing()
        {
            if (TestFunctionInterrupt())
                return;

            foreach (object loginObj in _loginObjects)
            {
                // Revoke endpoint permissions
                if (Type != null && ArrayContains(Type, "Endpoint"))
                {
                    foreach (string perm in Permission)
                    {
                        if (String.Equals(perm, "CreateAnyDatabase", StringComparison.OrdinalIgnoreCase))
                        {
                            StopFunction(
                                String.Format("{0} not supported by endpoints", perm),
                                isContinue: true);
                            TestFunctionInterrupt();
                            continue;
                        }

                        PSObject loginPs = PSObject.AsPSObject(loginObj);
                        string loginName = GetPropertyString(loginPs, "Name");
                        string serverName = GetServerName(loginPs);

                        if (ShouldProcess(serverName ?? "Server", String.Format("Revoking {0} from {1} on endpoint", perm, loginName ?? "login")))
                        {
                            try
                            {
                                Collection<PSObject> results = InvokeCommand.InvokeScript(
                                    false, _revokeEndpointPermScript, null,
                                    new object[] { loginObj, perm });

                                if (results != null)
                                {
                                    foreach (PSObject result in results)
                                    {
                                        if (result != null)
                                            WriteObject(result);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                StopFunction(
                                    String.Format("Failure revoking {0} on endpoint from {1}", perm, loginName ?? "login"),
                                    errorRecord: new ErrorRecord(ex, "RevokeDbaAgPermission_Endpoint", ErrorCategory.InvalidOperation, loginObj),
                                    target: loginObj, isContinue: true);
                                TestFunctionInterrupt();
                            }
                        }
                    }
                }

                // Revoke availability group permissions
                if (Type != null && ArrayContains(Type, "AvailabilityGroup") && AvailabilityGroup != null)
                {
                    foreach (string perm in Permission)
                    {
                        if (!String.Equals(perm, "Alter", StringComparison.OrdinalIgnoreCase) &&
                            !String.Equals(perm, "Control", StringComparison.OrdinalIgnoreCase) &&
                            !String.Equals(perm, "TakeOwnership", StringComparison.OrdinalIgnoreCase) &&
                            !String.Equals(perm, "ViewDefinition", StringComparison.OrdinalIgnoreCase))
                        {
                            StopFunction(
                                String.Format("{0} not supported by availability groups", perm),
                                isContinue: true);
                            TestFunctionInterrupt();
                            continue;
                        }

                        PSObject loginPs = PSObject.AsPSObject(loginObj);
                        string loginName = GetPropertyString(loginPs, "Name");
                        string serverName = GetServerName(loginPs);

                        if (ShouldProcess(serverName ?? "Server", String.Format("Revoking {0} from {1} on availability groups", perm, loginName ?? "login")))
                        {
                            try
                            {
                                Collection<PSObject> results = InvokeCommand.InvokeScript(
                                    false, _revokeAgPermScript, null,
                                    new object[] { loginObj, AvailabilityGroup, perm });

                                if (results != null)
                                {
                                    foreach (PSObject result in results)
                                    {
                                        if (result != null)
                                            WriteObject(result);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                StopFunction(
                                    String.Format("Failure revoking {0} on availability group from {1}", perm, loginName ?? "login"),
                                    errorRecord: new ErrorRecord(ex, "RevokeDbaAgPermission_AG", ErrorCategory.InvalidOperation, loginObj),
                                    target: loginObj, isContinue: true);
                                TestFunctionInterrupt();
                            }
                        }
                    }
                }
            }
        }

        #region Helpers

        /// <summary>
        /// Checks if a string array contains a value (case-insensitive).
        /// </summary>
        private static bool ArrayContains(string[] array, string value)
        {
            if (array == null) return false;
            foreach (string item in array)
            {
                if (String.Equals(item, value, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Gets a string property value from a PSObject.
        /// </summary>
        private static string GetPropertyString(PSObject obj, string propertyName)
        {
            if (obj == null) return null;
            try
            {
                PSPropertyInfo prop = obj.Properties[propertyName];
                if (prop != null && prop.Value != null)
                    return prop.Value.ToString();
            }
            catch (Exception)
            {
                // Property may not exist
            }
            return null;
        }

        /// <summary>
        /// Gets the server name from a login's parent.
        /// </summary>
        private static string GetServerName(PSObject loginObj)
        {
            try
            {
                PSPropertyInfo parentProp = loginObj.Properties["Parent"];
                if (parentProp != null && parentProp.Value != null)
                {
                    PSObject parent = PSObject.AsPSObject(parentProp.Value);
                    PSPropertyInfo nameProp = parent.Properties["Name"];
                    if (nameProp != null && nameProp.Value != null)
                        return nameProp.Value.ToString();
                }
            }
            catch (Exception)
            {
                // Best effort
            }
            return null;
        }

        #endregion Helpers
    }
}
