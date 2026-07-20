#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;
using Dataplat.Dbatools.Connection;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves WMI/CIM management information from a computer, with protocol fallback. Port of
/// public/Get-DbaCmObject.ps1 (W3-024, WAVE-3 hard remnant); the workflow remains a module-scoped
/// PowerShell compatibility hop. READ-ONLY (no SupportsShouldProcess).
///
/// BEGIN+PROCESS. -ComputerName is ValueFromPipeline, so process fires per piped target. TWO
/// parameter sets: Class (default) and Query.
///
/// THREE begin -> process CARRIES, and NO cross-record carry (confirmed by the two-step triage, not
/// assumed):
///   $disable_cache (:108, ConnectionHost::DisableCache) - read throughout process for the
///     connection-cache writes; carried from begin.
///   $ParSet (:244, "$PSCmdlet.ParameterSetName") - CANNOT ride a hop: inside a hop $PSCmdlet is the
///     HOP scriptblock's own cmdlet, so it would report the hop's set, not the caller's. The real
///     cmdlet's ParameterSetName is passed in and the body uses the carried value (read at :304/:335/
///     :366 to branch Class vs Query). Same class handled on W2-153.
///   Resolve-CimError - a ~128-line utility FUNCTION defined in begin (:114-241) and CALLED in
///     process (:314/:345/...). Begin's scope dies before process, so it is recreated verbatim at
///     the top of the process hop (the New-DbaDacProfile / W3-016 / W3-047 helper-recreation pattern).
///   $excluded (accumulator detector hit) does NOT carry: it is reset to @() at :289 INSIDE the
///     ":main foreach ($connectionObject in $ComputerName)" loop, so it is per-connectionObject, not
///     cross-record. Recorded so a reviewer does not re-add it.
///
/// NESTED LABELED LOOPS RIDE VERBATIM. process is ":main foreach ($connectionObject ...)" containing
/// ":sub while ($true)" (the protocol fallback CimRM -> CimDCOM -> Wmi -> PowerShellRemoting). The
/// body uses "continue main", "continue sub" and "Stop-Function -Continue -ContinueLabel 'main'"
/// (whose effect is "continue main"). PowerShell's labeled continue is DYNAMICALLY scoped, and the
/// loops live in the dot-sourced body, so the labels resolve correctly inside the hop.
///
/// ONE Test-Bound ("Namespace" at :374/:384) becomes a carried boundness flag. ComputerName's source
/// default is $env:COMPUTERNAME (a DEF-007 runtime default); because it is ValueFromPipeline the
/// piped/passed value wins and the env default applies only when nothing was supplied, resolved in
/// the hop by "if (-not $ComputerName) { $ComputerName = $env:COMPUTERNAME }".
///
/// -ClassName carries Alias("Class"). The three switches (Force, SilentlyContinue) and inherited
/// EnableException cross as SwitchParameter OBJECTS received untyped. In-hop Stop-Function/
/// Write-Message calls carry -FunctionName. NO interrupt (no Test-FunctionInterrupt), NO
/// $PSBoundParameters iteration, NO .IsPresent. Streaming (DEF-001): emits per target as each
/// connects. Only -ClassName is positional (0), confirmed against the exported baseline. Surface
/// pinned by migration/baselines/Get-DbaCmObject.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaCmObject", DefaultParameterSetName = "Class")]
public sealed class GetDbaCmObjectCommand : DbaBaseCmdlet
{
    /// <summary>The WMI/CIM class to query (Class parameter set).</summary>
    [Parameter(Mandatory = true, Position = 0, ParameterSetName = "Class")]
    [Alias("Class")]
    [PsStringCast]
    public string ClassName { get; set; } = null!;

    /// <summary>A WQL query to run (Query parameter set).</summary>
    [Parameter(Mandatory = true, ParameterSetName = "Query")]
    [PsStringCast]
    public string Query { get; set; } = null!;

    /// <summary>The target computer(s); defaults to the local machine.</summary>
    [Parameter(ValueFromPipeline = true, ParameterSetName = "Class")]
    [Parameter(ValueFromPipeline = true, ParameterSetName = "Query")]
    public DbaCmConnectionParameter[]? ComputerName { get; set; }

    /// <summary>Alternative credential for the target computer(s).</summary>
    [Parameter(ParameterSetName = "Class")]
    [Parameter(ParameterSetName = "Query")]
    public PSCredential? Credential { get; set; }

    /// <summary>The WMI namespace to query.</summary>
    [Parameter(ParameterSetName = "Class")]
    [Parameter(ParameterSetName = "Query")]
    [PsStringCast]
    public string Namespace { get; set; } = "root\\cimv2";

    /// <summary>Connection protocols to skip.</summary>
    [Parameter(ParameterSetName = "Class")]
    [Parameter(ParameterSetName = "Query")]
    public ManagementConnectionType[] DoNotUse { get; set; } = new[] { ManagementConnectionType.None };

    /// <summary>Force a fresh connection, bypassing the cache.</summary>
    [Parameter(ParameterSetName = "Class")]
    [Parameter(ParameterSetName = "Query")]
    public SwitchParameter Force { get; set; }

    /// <summary>Suppress non-terminating connection errors.</summary>
    [Parameter(ParameterSetName = "Class")]
    [Parameter(ParameterSetName = "Query")]
    public SwitchParameter SilentlyContinue { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // begin's $disable_cache and $ParSet; opaque to C#.
    private Hashtable? _beginState;

    protected override void BeginProcessing()
    {
        foreach (PSObject? item in NestedCommand.InvokeScoped(this, BeginScript,
            EnableException, ParameterSetName,
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
        {
            if (item?.BaseObject is Hashtable sentinel && sentinel.ContainsKey("__getDbaCmObjectBegin"))
            {
                _beginState = sentinel["__getDbaCmObjectBegin"] as Hashtable;
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

        // Streaming, not buffered (DEF-001): each target's objects are emitted as it connects, so a
        // buffered hop would discard results already produced when a later target failed.
        NestedCommand.InvokeScopedStreaming(this, item =>
        {
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                RemoveHopErrorBookkeeping(nestedError);
                WriteError(nestedError);
                return;
            }
            WriteObject(item);
        }, ProcessScript,
            ClassName, Query, ComputerName, Credential, Namespace, DoNotUse, Force, SilentlyContinue,
            EnableException, _beginState,
            MyInvocation.BoundParameters.ContainsKey("Namespace"),
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

    // PS: the begin block VERBATIM, dot-sourced. Edits: $PSCmdlet.ParameterSetName becomes the
    // carried $__parameterSetName (inside a hop $PSCmdlet is the hop's own cmdlet), plus -FunctionName
    // stamps. The sentinel carries $disable_cache and $ParSet. The Resolve-CimError definition rides
    // verbatim here (unused in begin) and is recreated in process.
    private const string BeginScript = """
param($EnableException, $__parameterSetName, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param($EnableException, $__parameterSetName, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    . {
        #region Configuration Values
        $disable_cache = [Dataplat.Dbatools.Connection.ConnectionHost]::DisableCache

        Write-Message -Level Verbose -Message "Configuration loaded | Cache disabled: $disable_cache" -FunctionName Get-DbaCmObject
        #endregion Configuration Values

        #region Utility Functions
        function Resolve-CimError {
            <#
                .SYNOPSIS
                    Utility function to resolve CIM error states and streamline error handling in code.

                .DESCRIPTION
                    Utility function to resolve CIM error states and streamline error handling in code.
                    This determines the specific error message to provide and whether the connection type is not viable.

                    CIM Error Code Reference: https://msdn.microsoft.com/en-us/library/cc150671(v=vs.85).aspx

                .PARAMETER ErrorRecord
                    The error that just happened.

                .PARAMETER ComputerName
                    The computer against which the query was executed.

                .PARAMETER ClassName
                    The name of the class queried.

                .PARAMETER Namespace
                    The namespace executed against.

                .PARAMETER Query
                    The query executed.
            #>
            [CmdletBinding()]
            param (
                [System.Management.Automation.ErrorRecord]
                $ErrorRecord,

                [string]
                $ComputerName,

                [AllowEmptyString()]
                [string]
                $ClassName,

                [AllowEmptyString()]
                [string]
                $Namespace,

                [AllowEmptyString()]
                [string]
                $Query
            )

            if ($Query) {
                $ClassName = $Query -replace '.+from (\S+).{0,}', '$1'
            }

            $messages = @{
                1  = "[$ComputerName] An otherwise unexpected error happened."
                2  = "[$ComputerName] Access to computer granted, but access to $Namespace\$ClassName denied"
                3  = "[$ComputerName] Invalid namespace: $Namespace"
                4  = "[$ComputerName] Invalid parameters were specified"
                5  = "[$ComputerName] Invalid class name ($ClassName), not found in current namespace ($Namespace)"
                6  = "[$ComputerName] The requested object of class $ClassName could not be found"
                7  = "[$ComputerName] The operation against class $ClassName was not supported. This generally is a serverside WMI Provider issue (That is: It is specific to the application being managed via WMI)"
                8  = "[$ComputerName] The operation against class $ClassName is refused as long as it contains instances (data)"
                9  = "[$ComputerName] The operation against class $ClassName is refused as long as it contains instances (data)"
                10 = "[$ComputerName] The operation against class $ClassName cannot be carried out since the specified superclass does not exist."
                11 = "[$ComputerName] The specified object in $ClassName already exists."
                12 = "[$ComputerName] The specified property does not exist on $ClassName."
                13 = "[$ComputerName] The input type is invalid."
                14 = "[$ComputerName] Invalid query language. Please check your query string."
                15 = "[$ComputerName] Invalid query string. Please check your syntax."
                16 = "[$ComputerName] The specified method on $ClassName is not available."
                17 = "[$ComputerName] The specified method on $ClassName does not exist."
                18 = "[$ComputerName] An unexpected response has happened in this request"
                19 = "[$ComputerName] The specified destination for this request is invalid."
                20 = "[$ComputerName] The specified namespace $Namespace is not empty."
            }

            $badConnection = $false
            $badCredentials = $false
            $code = $ErrorRecord.Exception.InnerException.StatusCode -as [int]
            $message = $messages[$code]

            #region 1 = Generic runtime error
            # This routinely happens with CIM/DCOM
            if (1 -eq $code) {
                switch ($ErrorRecord.Exception.InnerException.MessageId) {
                    'HRESULT 0x8007052e' {
                        $badCredentials = $true
                        $message = "[$ComputerName] Invalid connection credentials"
                    }
                    'HRESULT 0x80070005' {
                        $badCredentials = $true
                        $message = "[$ComputerName] Invalid connection credentials"
                    }
                    'HRESULT 0x80041013' {
                        $message = "[$ComputerName] Failed to access $ClassName in namespace $Namespace"
                    }
                    'HRESULT 0x8004100e' {
                        $message = "[$ComputerName] Invalid namespace: $Namespace"
                        $code = 3
                    }
                    'HRESULT 0x80041010' {
                        $message = "[$ComputerName] Invalid class name ($ClassName), not found in current namespace ($Namespace)"
                        $code = 5
                    }
                    default {
                        $badConnection = $true
                    }
                }
            }
            #endregion 1 = Generic runtime error

            #region 0 = Non-CIM Issue not covered by the framework
            $knownCodes = 1..20
            if ($code -notin $knownCodes) {
                if ($ErrorRecord.Exception.InnerException.ErrorData.original_error -like "__ExtendedStatus") {
                    $message = "[$ComputerName] Something went wrong when looking for $ClassName, in $Namespace. This often indicates issues with the target system."
                } else {
                    $badConnection = $true
                }
            }
            #endregion 0 = Non-CIM Issue not covered by the framework

            [PSCustomObject]@{
                ErrorCode      = $code
                Message        = $message
                BadConnection  = $badConnection
                BadCredentials = $badCredentials
                Error          = $ErrorRecord
            }
        }
        #endregion Utility Functions

        $ParSet = $__parameterSetName
    }

    $__dc = Get-Variable -Name disable_cache -Scope 0 -ErrorAction Ignore
    $__ps = Get-Variable -Name ParSet -Scope 0 -ErrorAction Ignore
    $__dcv = $null; if ($__dc) { $__dcv = $__dc.Value }
    $__psv = $null; if ($__ps) { $__psv = $__ps.Value }
    @{ __getDbaCmObjectBegin = @{ DisableCache = $__dcv; ParSet = $__psv } }
} $EnableException $__parameterSetName $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
    // PS: the process block VERBATIM, dot-sourced. Edits: Test-Bound "Namespace" becomes the carried
    // $__boundNamespace flag, plus -FunctionName stamps. Resolve-CimError is recreated first (begin's
    // scope is gone); $disable_cache and $ParSet restore from the begin sentinel; ComputerName's
    // $env:COMPUTERNAME default resolves when nothing was supplied. The :main/:sub labeled loops and
    // their continue main / continue sub / -ContinueLabel "main" ride verbatim (labels are
    // dynamically scoped, so they resolve inside the dot-sourced body).
    private const string ProcessScript = """
param($ClassName, $Query, $ComputerName, $Credential, $Namespace, $DoNotUse, $Force, $SilentlyContinue, $EnableException, $__beginState, $__boundNamespace, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([string]$ClassName, [string]$Query, [Dataplat.Dbatools.Parameter.DbaCmConnectionParameter[]]$ComputerName, [System.Management.Automation.PSCredential]$Credential, [string]$Namespace, [Dataplat.Dbatools.Connection.ManagementConnectionType[]]$DoNotUse, $Force, $SilentlyContinue, $EnableException, $__beginState, $__boundNamespace, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    # the begin helper, recreated (begin's scope does not reach this hop)
    function Resolve-CimError {
        <#
            .SYNOPSIS
                Utility function to resolve CIM error states and streamline error handling in code.
    
            .DESCRIPTION
                Utility function to resolve CIM error states and streamline error handling in code.
                This determines the specific error message to provide and whether the connection type is not viable.
    
                CIM Error Code Reference: https://msdn.microsoft.com/en-us/library/cc150671(v=vs.85).aspx
    
            .PARAMETER ErrorRecord
                The error that just happened.
    
            .PARAMETER ComputerName
                The computer against which the query was executed.
    
            .PARAMETER ClassName
                The name of the class queried.
    
            .PARAMETER Namespace
                The namespace executed against.
    
            .PARAMETER Query
                The query executed.
        #>
        [CmdletBinding()]
        param (
            [System.Management.Automation.ErrorRecord]
            $ErrorRecord,
    
            [string]
            $ComputerName,
    
            [AllowEmptyString()]
            [string]
            $ClassName,
    
            [AllowEmptyString()]
            [string]
            $Namespace,
    
            [AllowEmptyString()]
            [string]
            $Query
        )
    
        if ($Query) {
            $ClassName = $Query -replace '.+from (\S+).{0,}', '$1'
        }
    
        $messages = @{
            1  = "[$ComputerName] An otherwise unexpected error happened."
            2  = "[$ComputerName] Access to computer granted, but access to $Namespace\$ClassName denied"
            3  = "[$ComputerName] Invalid namespace: $Namespace"
            4  = "[$ComputerName] Invalid parameters were specified"
            5  = "[$ComputerName] Invalid class name ($ClassName), not found in current namespace ($Namespace)"
            6  = "[$ComputerName] The requested object of class $ClassName could not be found"
            7  = "[$ComputerName] The operation against class $ClassName was not supported. This generally is a serverside WMI Provider issue (That is: It is specific to the application being managed via WMI)"
            8  = "[$ComputerName] The operation against class $ClassName is refused as long as it contains instances (data)"
            9  = "[$ComputerName] The operation against class $ClassName is refused as long as it contains instances (data)"
            10 = "[$ComputerName] The operation against class $ClassName cannot be carried out since the specified superclass does not exist."
            11 = "[$ComputerName] The specified object in $ClassName already exists."
            12 = "[$ComputerName] The specified property does not exist on $ClassName."
            13 = "[$ComputerName] The input type is invalid."
            14 = "[$ComputerName] Invalid query language. Please check your query string."
            15 = "[$ComputerName] Invalid query string. Please check your syntax."
            16 = "[$ComputerName] The specified method on $ClassName is not available."
            17 = "[$ComputerName] The specified method on $ClassName does not exist."
            18 = "[$ComputerName] An unexpected response has happened in this request"
            19 = "[$ComputerName] The specified destination for this request is invalid."
            20 = "[$ComputerName] The specified namespace $Namespace is not empty."
        }
    
        $badConnection = $false
        $badCredentials = $false
        $code = $ErrorRecord.Exception.InnerException.StatusCode -as [int]
        $message = $messages[$code]
    
        #region 1 = Generic runtime error
        # This routinely happens with CIM/DCOM
        if (1 -eq $code) {
            switch ($ErrorRecord.Exception.InnerException.MessageId) {
                'HRESULT 0x8007052e' {
                    $badCredentials = $true
                    $message = "[$ComputerName] Invalid connection credentials"
                }
                'HRESULT 0x80070005' {
                    $badCredentials = $true
                    $message = "[$ComputerName] Invalid connection credentials"
                }
                'HRESULT 0x80041013' {
                    $message = "[$ComputerName] Failed to access $ClassName in namespace $Namespace"
                }
                'HRESULT 0x8004100e' {
                    $message = "[$ComputerName] Invalid namespace: $Namespace"
                    $code = 3
                }
                'HRESULT 0x80041010' {
                    $message = "[$ComputerName] Invalid class name ($ClassName), not found in current namespace ($Namespace)"
                    $code = 5
                }
                default {
                    $badConnection = $true
                }
            }
        }
        #endregion 1 = Generic runtime error
    
        #region 0 = Non-CIM Issue not covered by the framework
        $knownCodes = 1..20
        if ($code -notin $knownCodes) {
            if ($ErrorRecord.Exception.InnerException.ErrorData.original_error -like "__ExtendedStatus") {
                $message = "[$ComputerName] Something went wrong when looking for $ClassName, in $Namespace. This often indicates issues with the target system."
            } else {
                $badConnection = $true
            }
        }
        #endregion 0 = Non-CIM Issue not covered by the framework
    
        [PSCustomObject]@{
            ErrorCode      = $code
            Message        = $message
            BadConnection  = $badConnection
            BadCredentials = $badCredentials
            Error          = $ErrorRecord
        }
    }
    # begin's carried values
    $disable_cache = $__beginState.DisableCache
    $ParSet = $__beginState.ParSet
    # ComputerName DEF-007: source default $env:COMPUTERNAME, applied only when nothing was supplied
    if (-not $ComputerName) { $ComputerName = $env:COMPUTERNAME }

    . {
        # uses cim commands
        :main foreach ($connectionObject in $ComputerName) {
            if (-not $connectionObject.Success) { Stop-Function -Message "Failed to interpret input: $($connectionObject.Input)" -Category InvalidArgument -Target $connectionObject.Input -Continue -SilentlyContinue:$SilentlyContinue -FunctionName Get-DbaCmObject }

            # Since all connection caching runs using lower-case strings, making it lowercase here simplifies things.
            $computer = $connectionObject.Connection.ComputerName.ToLowerInvariant()

            Write-Message -Message "[$computer] Retrieving Management Information" -Level VeryVerbose -Target $computer -FunctionName Get-DbaCmObject

            $connection = $connectionObject.Connection

            # Ensure CIM session options are initialized with the configured operation timeout
            if ($null -eq $connection.CimWinRMOptions) {
                $connection.CimWinRMOptions = New-DbaCimSessionOptionWithTimeout -Protocol Default
            }
            if ($null -eq $connection.CimDCOMOptions) {
                $connection.CimDCOMOptions = New-DbaCimSessionOptionWithTimeout -Protocol Dcom
            }

            # Ensure using the right credentials
            try { $cred = $connection.GetCredential($Credential) }
            catch {
                $message = "Bad credentials. "
                if ($Credential) { $message += "The credentials for $($Credential.UserName) are known to not work. " }
                else { $message += "The windows credentials are known to not work. " }
                if ($connection.EnableCredentialFailover -or $connection.OverrideExplicitCredential) { $message += "The connection is configured to use credentials that are known to be good, but none have been registered yet. " }
                elseif ($connection.Credentials) { $message += "Working credentials are known for $($connection.Credentials.UserName), however the connection is not configured to automatically use them. This can be done using 'Set-DbaCmConnection -ComputerName $connection -OverrideExplicitCredential' " }
                elseif ($connection.UseWindowsCredentials) { $message += "The windows credentials are known to work, however the connection is not configured to automatically use them. This can be done using 'Set-DbaCmConnection -ComputerName $connection -OverrideExplicitCredential' " }
                $message += $_.Exception.Message
                Stop-Function -Message $message -ErrorRecord $_ -Target $connection -Continue -OverrideExceptionMessage -FunctionName Get-DbaCmObject
            }

            # Flags-Enumerations cannot be added in PowerShell 4 or older.
            # Thus we create a string and convert it afterwards.
            $enabledProtocols = "None"
            if ($connection.CimRM -notlike "Disabled") { $enabledProtocols += ", CimRM" }
            if ($connection.CimDCOM -notlike "Disabled") { $enabledProtocols += ", CimDCOM" }
            if ($connection.Wmi -notlike "Disabled") { $enabledProtocols += ", Wmi" }
            if ($connection.PowerShellRemoting -notlike "Disabled") { $enabledProtocols += ", PowerShellRemoting" }
            [Dataplat.Dbatools.Connection.ManagementConnectionType]$enabledProtocols = $enabledProtocols

            # Create list of excluded connection types (Duplicates don't matter)
            $excluded = @()
            foreach ($item in $DoNotUse) { $excluded += $item }

            :sub while ($true) {
                try { $conType = $connection.GetConnectionType(($excluded -join ","), $Force) }
                catch {
                    if (-not $disable_cache) { [Dataplat.Dbatools.Connection.ConnectionHost]::Connections[$computer] = $connection }
                    Stop-Function -Message "[$computer] Unable to find a connection to the target system. Ensure the name is typed correctly, and the server allows any of the following protocols: $enabledProtocols" -Target $computer -Category OpenError -Continue -ContinueLabel "main" -SilentlyContinue:$SilentlyContinue -ErrorRecord $_ -FunctionName Get-DbaCmObject
                }

                switch ($conType.ToString()) {
                    #region CimRM
                    "CimRM" {
                        Write-Message -Level Verbose -Message "[$computer] Accessing computer using Cim over WinRM" -FunctionName Get-DbaCmObject
                        try {
                            if ($ParSet -eq "Class") { $connection.GetCimRMInstance($cred, $ClassName, $Namespace) }
                            else { $connection.QueryCimRMInstance($cred, $Query, "WQL", $Namespace) }

                            Write-Message -Level Verbose -Message "[$computer] Accessing computer using Cim over WinRM - Success" -FunctionName Get-DbaCmObject
                            $connection.ReportSuccess('CimRM')
                            $connection.AddGoodCredential($cred)
                            if (-not $disable_cache) { [Dataplat.Dbatools.Connection.ConnectionHost]::Connections[$computer] = $connection }
                            continue main
                        } catch {
                            Write-Message -Level Verbose -Message "[$computer] Accessing computer using Cim over WinRM - Failed" -FunctionName Get-DbaCmObject
                            $errorDetails = Resolve-CimError -ErrorRecord $_ -ComputerName $computer -ClassName $ClassName -Namespace $Namespace -Query $Query

                            if ($errorDetails.BadCredentials) {
                                $connection.AddBadCredential($cred)
                                if (-not $disable_cache) { [Dataplat.Dbatools.Connection.ConnectionHost]::Connections[$computer] = $connection }
                                Stop-Function -Message "[$computer] Invalid connection credentials" -Target $computer -Continue -ContinueLabel "main" -ErrorRecord $_ -SilentlyContinue:$SilentlyContinue -OverrideExceptionMessage -FunctionName Get-DbaCmObject
                            }
                            if ($errorDetails.BadConnection) {
                                $connection.ReportFailure('CimRM')
                                $excluded += "CimRM"
                                continue sub
                            }
                            Stop-Function -Message $errorDetails.Message -Target $computer -Continue -ContinueLabel "main" -ErrorRecord $_ -SilentlyContinue:$SilentlyContinue -OverrideExceptionMessage -FunctionName Get-DbaCmObject
                        }
                    }
                    #endregion CimRM

                    #region CimDCOM
                    "CimDCOM" {
                        Write-Message -Level Verbose -Message "[$computer] Accessing computer using Cim over DCOM" -FunctionName Get-DbaCmObject
                        try {
                            if ($ParSet -eq "Class") { $connection.GetCimDCOMInstance($cred, $ClassName, $Namespace) }
                            else { $connection.QueryCimDCOMInstance($cred, $Query, "WQL", $Namespace) }

                            Write-Message -Level Verbose -Message "[$computer] Accessing computer using Cim over DCOM - Success" -FunctionName Get-DbaCmObject
                            $connection.ReportSuccess('CimDCOM')
                            $connection.AddGoodCredential($cred)
                            if (-not $disable_cache) { [Dataplat.Dbatools.Connection.ConnectionHost]::Connections[$computer] = $connection }
                            continue main
                        } catch {
                            Write-Message -Level Verbose -Message "[$computer] Accessing computer using Cim over DCOM - Failed" -FunctionName Get-DbaCmObject
                            $errorDetails = Resolve-CimError -ErrorRecord $_ -ComputerName $computer -ClassName $ClassName -Namespace $Namespace -Query $Query

                            if ($errorDetails.BadCredentials) {
                                $connection.AddBadCredential($cred)
                                if (-not $disable_cache) { [Dataplat.Dbatools.Connection.ConnectionHost]::Connections[$computer] = $connection }
                                Stop-Function -Message "[$computer] Invalid connection credentials" -Target $computer -Continue -ContinueLabel "main" -ErrorRecord $_ -SilentlyContinue:$SilentlyContinue -OverrideExceptionMessage -FunctionName Get-DbaCmObject
                            }
                            if ($errorDetails.BadConnection) {
                                $connection.ReportFailure('CimDCOM')
                                $excluded += "CimDCOM"
                                continue sub
                            }
                            Stop-Function -Message $errorDetails.Message -Target $computer -Continue -ContinueLabel "main" -ErrorRecord $_ -SilentlyContinue:$SilentlyContinue -OverrideExceptionMessage -FunctionName Get-DbaCmObject
                        }
                    }
                    #endregion CimDCOM

                    #region Wmi
                    "Wmi" {
                        Write-Message -Level Verbose -Message "[$computer] Accessing computer using WMI" -FunctionName Get-DbaCmObject
                        try {
                            switch ($ParSet) {
                                "Class" {
                                    $parameters = @{
                                        ComputerName = $computer
                                        ClassName    = $ClassName
                                        ErrorAction  = 'Stop'
                                    }
                                    if ($cred) { $parameters["Credential"] = $cred }
                                    if ($__boundNamespace) { $parameters["Namespace"] = $Namespace }

                                }
                                "Query" {
                                    $parameters = @{
                                        ComputerName = $computer
                                        Query        = $Query
                                        ErrorAction  = 'Stop'
                                    }
                                    if ($cred) { $parameters["Credential"] = $cred }
                                    if ($__boundNamespace) { $parameters["Namespace"] = $Namespace }
                                }
                            }

                            Get-WmiObject @parameters

                            Write-Message -Level Verbose -Message "[$computer] Accessing computer using WMI - Success" -FunctionName Get-DbaCmObject
                            $connection.ReportSuccess('Wmi')
                            $connection.AddGoodCredential($cred)
                            if (-not $disable_cache) { [Dataplat.Dbatools.Connection.ConnectionHost]::Connections[$computer] = $connection }
                            continue main
                        } catch {
                            Write-Message -Level Verbose -Message "[$computer] Accessing computer using WMI - Failed" -ErrorRecord $_ -FunctionName Get-DbaCmObject

                            if ($_.CategoryInfo.Reason -eq "UnauthorizedAccessException") {
                                # Ignore the global setting for bad credential cache disabling, since the connection object is aware of that state and will ignore input if it should.
                                # This is due to the ability to locally override the global setting, thus it must be done on the object and can then be done in code
                                $connection.AddBadCredential($cred)
                                if (-not $disable_cache) { [Dataplat.Dbatools.Connection.ConnectionHost]::Connections[$computer] = $connection }
                                Stop-Function -Message "[$computer] Invalid connection credentials" -Target $computer -Continue -ContinueLabel "main" -ErrorRecord $_ -SilentlyContinue:$SilentlyContinue -FunctionName Get-DbaCmObject
                            } elseif ($_.CategoryInfo.Category -eq "InvalidType") {
                                Stop-Function -Message "[$computer] Invalid class name ($ClassName), not found in current namespace ($Namespace)" -Target $computer -Continue -ContinueLabel "main" -ErrorRecord $_ -SilentlyContinue:$SilentlyContinue -FunctionName Get-DbaCmObject
                            } elseif ($_.Exception.ErrorCode -eq "ProviderLoadFailure") {
                                Stop-Function -Message "[$computer] Failed to access: $ClassName, in namespace: $Namespace - There was a provider error. This indicates a potential issue with WMI on the server side." -Target $computer -Continue -ContinueLabel "main" -ErrorRecord $_ -SilentlyContinue:$SilentlyContinue -FunctionName Get-DbaCmObject
                            } else {
                                $connection.ReportFailure('Wmi')
                                $excluded += "Wmi"
                                continue sub
                            }
                        }
                    }
                    #endregion Wmi

                    #region PowerShell Remoting
                    "PowerShellRemoting" {
                        try {
                            Write-Message -Level Verbose -Message "[$computer] Accessing computer using PowerShell Remoting" -FunctionName Get-DbaCmObject
                            $scp_string = "Get-WmiObject -Class $ClassName -ErrorAction Stop"
                            if ($__boundNamespace) { $scp_string += " -Namespace $Namespace" }

                            $parameters = @{
                                ScriptBlock  = ([System.Management.Automation.ScriptBlock]::Create($scp_string))
                                ComputerName = $computer
                                Raw          = $true
                            }
                            if ($Credential) { $parameters["Credential"] = $Credential }
                            Invoke-Command2 @parameters

                            Write-Message -Level Verbose -Message "[$computer] Accessing computer using PowerShell Remoting - Success" -FunctionName Get-DbaCmObject
                            $connection.ReportSuccess('PowerShellRemoting')
                            $connection.AddGoodCredential($cred)
                            if (-not $disable_cache) { [Dataplat.Dbatools.Connection.ConnectionHost]::Connections[$computer] = $connection }
                            continue main
                        } catch {
                            # Will always consider authenticated, since any call with credentials to a server that doesn't exist will also carry invalid credentials error.
                            # There simply is no way to differentiate between actual authentication errors and server not reached
                            $connection.ReportFailure('PowerShellRemoting')
                            $excluded += "PowerShellRemoting"
                            continue sub
                        }
                    }
                    #endregion PowerShell Remoting
                }
            }
        }
    }
} $ClassName $Query $ComputerName $Credential $Namespace $DoNotUse $Force $SilentlyContinue $EnableException $__beginState $__boundNamespace $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}