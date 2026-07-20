#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Gets registered servers from Central Management Servers and local stores. Port of
/// public/Get-DbaRegServer.ps1 (W3-047, WAVE-3 remnant); the workflow remains a module-scoped
/// PowerShell compatibility hop. READ-ONLY (no mutating verbs, no SupportsShouldProcess).
///
/// BEGIN+PROCESS. -SqlInstance is ValueFromPipeline, so process fires per piped instance.
///
/// $defaults BEGIN -> PROCESS CARRY, bug-for-bug. begin :175-180 sets $defaults, and a SOURCE BUG
/// rides verbatim: :175 sets an 8-column set under "if ($ResolveNetworkName)", but :180
/// UNCONDITIONALLY overwrites it with the 5-column set - so -ResolveNetworkName never actually
/// widens the default view. Process reads $defaults at :383/:403 for Select-DefaultView. It is
/// carried from begin, not recomputed.
///
/// TWO begin HELPERS RECREATED IN THE PROCESS HOP. begin defines Unprotect-String (a function,
/// :182) and $matchesPattern (a scriptblock, :186) but CALLS neither; process calls them at :335 and
/// :294. Begin's scope dies before process, so both are recreated verbatim at the top of the process
/// hop - the same treatment New-DbaDacProfile's helpers and W3-016's Get-ServerName needed. Neither
/// closes over carried state (both are pure over their arguments), so recreation is faithful.
///
/// TWO DEF-007 CONFIG DEFAULTS resolved in the process hop:
///   -SqlInstance defaults to Get-DbatoolsConfigValue 'commands.get-dbaregserver.defaultcms'. Because
///     it is ValueFromPipeline, the piped/passed value wins per record and the CMS default applies
///     only when nothing was supplied - reproduced by "if (-not $SqlInstance) { $SqlInstance = ... }".
///   -IncludeLocal (a switch) defaults to Get-DbatoolsConfigValue
///     'commands.get-dbaregserver.includelocal'; resolved when the caller did not pass it.
///
/// $PSBoundParameters PROJECTION (VALUE reads). The body reads $PSBoundParameters.SqlInstance
/// (:197/:232), .IncludeLocal (:232), .Group (:378) and .ExcludeGroup (:379) as VALUES - these
/// distinguish an EXPLICIT caller pass from a default and drive the local-store and group-filter
/// logic. Inside a hop $PSBoundParameters is the hop's own bindings, so the caller's real
/// BoundParameters dictionary is passed in and substituted for $PSBoundParameters before the body
/// (the W2-151 approach), which keeps every value read faithful. It is only ever indexed by key,
/// never iterated.
///
/// NO INTERRUPT BRIDGE: no Test-FunctionInterrupt in the source; its Stop-Function calls carry
/// -Continue (:210) or terminate a single object.
///
/// ONE CROSS-RECORD STATE CARRY - $azureids (codex r1, DO NOT REMOVE). It is initialized to @() ONLY
/// inside the local-store path (:244, gated by :232 "-not $PSBoundParameters.SqlInstance -or
/// $PSBoundParameters.IncludeLocal"), but READ at :308 in the UNCONDITIONAL "foreach ($server in
/// $servers)" loop. On a record that does not take the local-store path, the source's persistent
/// process scope reads a PREVIOUS record's $azureids, so a later server can be matched to a stale
/// Azure id/group; a fresh hop scope would see $null and diverge. It rides a process sentinel with an
/// AzureidsAssigned flag, restored before the body so the :244 re-init still overwrites it on a
/// record that does take the path, and a first record leaves it undefined exactly as the source does.
/// The remaining process locals genuinely do NOT carry: $servers, $serverstores and
/// $serverToServerStore reset to empty at :201-203 on every record, and every other local is
/// assigned before use within its own loop.
///
/// -ExcludeServerName carries Alias("ExcludeServer"). The three switches (IncludeSelf,
/// ResolveNetworkName, IncludeLocal) and inherited EnableException cross as SwitchParameter OBJECTS
/// received untyped. In-hop Stop-Function/Write-Message calls carry -FunctionName. Implicit positions
/// 0-8 are made explicit per the W2-071 law and confirmed against the exported baseline; SqlInstance
/// is position 0 and ValueFromPipeline. Streaming (DEF-001): emits per registered server via
/// Select-DefaultView. Surface pinned by migration/baselines/Get-DbaRegServer.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaRegServer")]
public sealed class GetDbaRegServerCommand : DbaBaseCmdlet
{
    /// <summary>The CMS instance(s); defaults to the configured default CMS.</summary>
    [Parameter(ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Alternative credential for the target instances.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>Filter to these registered-server names.</summary>
    [Parameter(Position = 2)]
    [PsStringArrayCast]
    public string[]? Name { get; set; }

    /// <summary>Filter to these server names.</summary>
    [Parameter(Position = 3)]
    [PsStringArrayCast]
    public string[]? ServerName { get; set; }

    /// <summary>Regex patterns to match names or server names against.</summary>
    [Parameter(Position = 4)]
    [PsStringArrayCast]
    public string[]? Pattern { get; set; }

    /// <summary>Exclude these server names.</summary>
    [Parameter(Position = 5)]
    [Alias("ExcludeServer")]
    [PsStringArrayCast]
    public string[]? ExcludeServerName { get; set; }

    /// <summary>Limit to these groups.</summary>
    [Parameter(Position = 6)]
    [PsStringArrayCast]
    public string[]? Group { get; set; }

    /// <summary>Exclude these groups.</summary>
    [Parameter(Position = 7)]
    [PsStringArrayCast]
    public string[]? ExcludeGroup { get; set; }

    /// <summary>Limit to these registered-server ids.</summary>
    [Parameter(Position = 8)]
    public int[]? Id { get; set; }

    /// <summary>Include the CMS server itself in the results.</summary>
    [Parameter]
    public SwitchParameter IncludeSelf { get; set; }

    /// <summary>Resolve network names (widens the default view - though a source bug suppresses that).</summary>
    [Parameter]
    public SwitchParameter ResolveNetworkName { get; set; }

    /// <summary>Include local server stores; defaults to the configured value.</summary>
    [Parameter]
    public SwitchParameter IncludeLocal { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // begin's $defaults column set; opaque to C#.
    private Hashtable? _beginState;
    // $azureids carried record-to-record (codex r1); opaque to C#.
    private Hashtable? _state;

    protected override void BeginProcessing()
    {
        foreach (PSObject? item in NestedCommand.InvokeScoped(this, BeginScript,
            ResolveNetworkName, EnableException,
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
        {
            if (item?.BaseObject is Hashtable sentinel && sentinel.ContainsKey("__getDbaRegServerBegin"))
            {
                _beginState = sentinel["__getDbaRegServerBegin"] as Hashtable;
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

        // Streaming, not buffered (DEF-001): each registered server is emitted as it is found, so a
        // buffered hop would discard results already produced when a later instance's failure
        // terminated the hop under -EnableException.
        NestedCommand.InvokeScopedStreaming(this, item =>
        {
            if (item?.BaseObject is Hashtable sentinel && sentinel.ContainsKey("__getDbaRegServerProcess"))
            {
                _state = sentinel["__getDbaRegServerProcess"] as Hashtable;
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
            SqlInstance, SqlCredential, Name, ServerName, Pattern, ExcludeServerName, Group,
            ExcludeGroup, Id, IncludeSelf, ResolveNetworkName, IncludeLocal, EnableException,
            _beginState, _state, GetBoundParametersCopy(),
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug"));
    }

    private Hashtable GetBoundParametersCopy()
    {
        Hashtable copy = new Hashtable(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.Generic.KeyValuePair<string, object> kv in MyInvocation.BoundParameters)
            copy[kv.Key] = kv.Value;
        return copy;
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

    // PS: the begin block VERBATIM, dot-sourced. Only edit is -FunctionName on the message calls.
    // The sentinel carries $defaults (the source-bug 5-column set - :180 overwrites the :175 set).
    // The two helper definitions ride verbatim here (unused in begin) and are recreated in process.
    private const string BeginScript = """
param($ResolveNetworkName, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param($ResolveNetworkName, $EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    . {
        if ($ResolveNetworkName) {
            $defaults = 'ComputerName', 'FQDN', 'IPAddress', 'Name', 'ServerName', 'Group', 'Description', 'Source'
        }
        $defaults = 'Name', 'ServerName', 'Group', 'Description', 'Source'
        # thank you forever https://social.msdn.microsoft.com/Forums/sqlserver/en-US/57811d43-a2b9-4179-a97b-a9936ddb188e/how-to-retrieve-a-password-saved-by-sql-server?forum=sqltools
        function Unprotect-String([string] $base64String) {
            return [System.Text.Encoding]::Unicode.GetString([System.Security.Cryptography.ProtectedData]::Unprotect([System.Convert]::FromBase64String($base64String), $null, [System.Security.Cryptography.DataProtectionScope]::CurrentUser))
        }

        # Helper function to test if a name matches any of the provided regex patterns
        $matchesPattern = {
            param($name, $serverName, $patterns)
            if (!$patterns) { return $true }
            foreach ($pattern in $patterns) {
                if ($name -match $pattern -or $serverName -match $pattern) {
                    return $true
                }
            }
            return $false
        }
    }

    $__df = Get-Variable -Name defaults -Scope 0 -ErrorAction Ignore
    $__dfv = $null; if ($__df) { $__dfv = $__df.Value }
    @{ __getDbaRegServerBegin = @{ Defaults = $__dfv } }
} $ResolveNetworkName $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
    // PS: the process block VERBATIM, dot-sourced. Only edit is -FunctionName on message calls. The
    // two begin helpers are recreated first (begin's scope is gone); $defaults restores from the
    // begin sentinel; the DEF-007 SqlInstance/IncludeLocal defaults resolve here; and the caller's
    // real bound-parameter dictionary is substituted for $PSBoundParameters so its value reads at
    // :197/:232/:378/:379 stay faithful.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Name, $ServerName, $Pattern, $ExcludeServerName, $Group, $ExcludeGroup, $Id, $IncludeSelf, $ResolveNetworkName, $IncludeLocal, $EnableException, $__beginState, $__state, $__boundParameters, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [string[]]$Name, [string[]]$ServerName, [string[]]$Pattern, [string[]]$ExcludeServerName, [string[]]$Group, [string[]]$ExcludeGroup, [int[]]$Id, $IncludeSelf, $ResolveNetworkName, $IncludeLocal, $EnableException, $__beginState, $__state, $__boundParameters, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    # the caller's real bound parameters - the body reads $PSBoundParameters.X by VALUE at :197/:232/:378/:379
    $PSBoundParameters = $__boundParameters
    # the begin helpers, recreated (begin's scope does not reach this hop)
    function Unprotect-String([string] $base64String) {
        return [System.Text.Encoding]::Unicode.GetString([System.Security.Cryptography.ProtectedData]::Unprotect([System.Convert]::FromBase64String($base64String), $null, [System.Security.Cryptography.DataProtectionScope]::CurrentUser))
    }
    $matchesPattern = {
        param($name, $serverName, $patterns)
        if (!$patterns) { return $true }
        foreach ($pattern in $patterns) {
            if ($name -match $pattern -or $serverName -match $pattern) {
                return $true
            }
        }
        return $false
    }
    # begin's once-computed column set
    $defaults = $__beginState.Defaults
    # DEF-007 config defaults, matching the source parameter defaults
    if (-not $PSBoundParameters.ContainsKey('SqlInstance')) { $SqlInstance = Get-DbatoolsConfigValue -FullName 'commands.get-dbaregserver.defaultcms' }
    if (-not $PSBoundParameters.ContainsKey('IncludeLocal')) { $IncludeLocal = [bool](Get-DbatoolsConfigValue -FullName 'commands.get-dbaregserver.includelocal') }
    # $azureids (codex r1): initialized only inside the local-store path (:244), but read at :308 in
    # the unconditional "foreach ($server in $servers)" loop. On a record that does NOT take the
    # local-store path, the SOURCE (persistent process scope) reads a PREVIOUS record's $azureids;
    # a fresh hop scope would see $null. Carried with an Assigned flag so a first record - or a record
    # whose predecessors never took the path - leaves it undefined, exactly as the source does.
    if ($null -ne $__state -and $__state.AzureidsAssigned) { $azureids = $__state.Azureids }

    . {
        if (-not $PSBoundParameters.SqlInstance -and -not ($IsLinux -or $IsMacOs)) {
            $null = Get-ChildItem -Recurse "$(Get-DbatoolsPath -Name appdata)\Microsoft\*sql*" -Filter RegSrvr*.xml | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        }

        $servers = @()
        $serverstores = @()
        $serverToServerStore = @{ }
        foreach ($instance in $SqlInstance) {
            $serverstore = $null

            try {
                $serverstore = Get-DbaRegServerStore -SqlInstance $instance -SqlCredential $SqlCredential -EnableException
            } catch {
                Stop-Function -Message "Cannot access Central Management Server '$instance'." -ErrorRecord $_ -Continue -FunctionName Get-DbaRegServer -ModuleName "dbatools"
                continue
            }
            $serverstores += $serverstore

            if ($Group) {
                $groupservers = Get-DbaRegServerGroup -SqlInstance $instance -SqlCredential $SqlCredential -Group $Group -ExcludeGroup $ExcludeGroup
                if ($groupservers) {
                    $servers += $groupservers.GetDescendantRegisteredServers()
                }
            } else {
                $servers += ($serverstore.DatabaseEngineServerGroup.GetDescendantRegisteredServers())
                $serverstore.ServerConnection.Disconnect()
            }

            # save the $serverstore for later usage
            foreach ($server in $servers) {
                $serverToServerStore[$server] = $serverstore
            }
        }

        # Magic courtesy of Mathias Jessen and David Shifflet
        if (-not $PSBoundParameters.SqlInstance -or $PSBoundParameters.IncludeLocal) {
            $file = [Microsoft.SqlServer.Management.RegisteredServers.RegisteredServersStore]::LocalFileStore.DomainInstanceName
            if ($file) {
                if ((Test-Path -Path $file)) {
                    $class = [Microsoft.SqlServer.Management.RegisteredServers.RegisteredServersStore]
                    $initMethod = $class.GetMethod('InitChildObjects', [Reflection.BindingFlags]'Static,NonPublic')
                    $store = ($initMethod.Invoke($null, @($file)))
                    # Local Reg Servers
                    foreach ($tempserver in $store.DatabaseEngineServerGroup.GetDescendantRegisteredServers()) {
                        $servers += $tempserver | Add-Member -Force -Name Source -Value "Local Server Groups" -MemberType NoteProperty -PassThru
                    }
                    # Azure Reg Servers
                    $azureids = @()
                    if ($store.AzureDataStudioConnectionStore.Groups) {
                        $adsconnection = Get-ADSConnection
                    }
                    foreach ($azuregroup in $store.AzureDataStudioConnectionStore.Groups) {
                        $groupname = $azuregroup.Name
                        if ($groupname -eq 'ROOT' -or $groupname -eq '') {
                            $groupname = $null
                        }
                        $tempgroup = New-Object Microsoft.SqlServer.Management.RegisteredServers.ServerGroup $groupname
                        $tempgroup.Description = $azuregroup.Description

                        foreach ($server in ($store.AzureDataStudioConnectionStore.Connections | Where-Object GroupId -eq $azuregroup.Id)) {
                            $azureids += [PSCustomObject]@{ id = $server.Id; group = $groupname }
                            $connname = $server.Options['connectionName']
                            if (-not $connname) {
                                $connname = $server.Options['server']
                            }
                            $adsconn = $adsconnection | Where-Object { $_.server -eq $server.Options['server'] -and -not $_.database }

                            $tempserver = New-Object Microsoft.SqlServer.Management.RegisteredServers.RegisteredServer $tempgroup, $connname
                            $tempserver.Description = $server.Options['Description']
                            if ($adsconn.ConnectionString) {
                                $tempserver.ConnectionString = $adsconn.ConnectionString
                            }
                            # update read-only or problematic properties
                            $tempserver | Add-Member -Force -Name Source -Value "Azure Data Studio" -MemberType NoteProperty
                            $tempserver | Add-Member -Force -Name ServerName -Value $server.Options['server'] -MemberType NoteProperty
                            $tempserver | Add-Member -Force -Name Id -Value $server.Id -MemberType NoteProperty
                            $tempserver | Add-Member -Force -Name CredentialPersistenceType -Value 1 -MemberType NoteProperty
                            $tempserver | Add-Member -Force -Name ServerType -Value DatabaseEngine -MemberType NoteProperty
                            $servers += $tempserver
                        }
                    }
                }
            }
        }

        if ($Name) {
            Write-Message -Level Verbose -Message "Filtering by name for $name" -FunctionName Get-DbaRegServer -ModuleName "dbatools"
            $servers = $servers | Where-Object Name -in $Name
        }

        if ($ServerName) {
            Write-Message -Level Verbose -Message "Filtering by servername for $servername" -FunctionName Get-DbaRegServer -ModuleName "dbatools"
            $servers = $servers | Where-Object ServerName -in $ServerName
        }

        if ($Pattern) {
            Write-Message -Level Verbose -Message "Filtering by pattern for $Pattern" -FunctionName Get-DbaRegServer -ModuleName "dbatools"
            $servers = $servers | Where-Object { & $matchesPattern $_.Name $_.ServerName $Pattern }
        }

        if ($ExcludeServerName) {
            Write-Message -Level Verbose -Message "Excluding servers: $ExcludeServerName" -FunctionName Get-DbaRegServer -ModuleName "dbatools"
            $servers = $servers | Where-Object ServerName -notin $ExcludeServerName
        }

        if ($Id) {
            Write-Message -Level Verbose -Message "Filtering by id for $Id (1 = default/root)" -FunctionName Get-DbaRegServer -ModuleName "dbatools"
            $servers = $servers | Where-Object Id -in $Id
        }

        foreach ($server in $servers) {
            $az = $azureids | Where-Object Id -in $server.Id
            if ($az) {
                $groupname = $az.Group
            } else {
                $groupname = Get-RegServerGroupReverseParse $server
                if ($groupname -eq $server.Name) {
                    $groupname = $null
                } else {
                    $groupname = ($groupname).Split("\")
                    $groupname = $groupname[0 .. ($groupname.Count - 2)]
                    $groupname = ($groupname -join "\")
                }
            }
            # ugly way around it but it works
            $badform = "$($server.Name.Split("\")[0])\$($server.Name.Split("\")[0])"
            if ($groupname -eq $badform) {
                $groupname = $null
            }

            if ($ExcludeGroup -and ($groupname -in $ExcludeGroup)) {
                continue
            }

            if ($server.ConnectionStringWithEncryptedPassword) {
                $encodedconnstring = $connstring = $server.ConnectionStringWithEncryptedPassword
                if ($encodedconnstring -imatch 'password="?([^";]+)"?') {
                    $password = $Matches[1]
                    $password = Unprotect-String $password
                    $connstring = $encodedconnstring -ireplace 'password="?([^";]+)"?', "password=`"$password`""
                    Add-Member -Force -InputObject $server -MemberType NoteProperty -Name ConnectionString -Value $connstring
                    Add-Member -Force -InputObject $server -MemberType NoteProperty -Name SecureConnectionString -Value (ConvertTo-SecureString -String $connstring -AsPlainText -Force)
                }
            }

            if (-not $server.Source) {
                Add-Member -Force -InputObject $server -MemberType NoteProperty -Name Source -value "Central Management Servers"
            }

            if ( $null -ne $serverToServerStore[$server] ) {
                Add-Member -Force -InputObject $server -MemberType NoteProperty -Name ComputerName -value $serverToServerStore[$server].ComputerName
                Add-Member -Force -InputObject $server -MemberType NoteProperty -Name InstanceName -value $serverToServerStore[$server].InstanceName
                Add-Member -Force -InputObject $server -MemberType NoteProperty -Name SqlInstance -value $serverToServerStore[$server].SqlInstance
                Add-Member -Force -InputObject $server -MemberType NoteProperty -Name ParentServer -Value $serverToServerStore[$server].ParentServer
            }

            Add-Member -Force -InputObject $server -MemberType NoteProperty -Name Group -value $groupname
            Add-Member -Force -InputObject $server -MemberType NoteProperty -Name FQDN -Value $null
            Add-Member -Force -InputObject $server -MemberType NoteProperty -Name IPAddress -Value $null

            if ($ResolveNetworkName) {
                try {
                    $lookup = Resolve-DbaNetworkName $server.ServerName -Turbo
                    $server.ComputerName = $lookup.ComputerName
                    $server.FQDN = $lookup.FQDN
                    $server.IPAddress = $lookup.IPAddress
                } catch {
                    try {
                        $lookup = Resolve-DbaNetworkName $server.ServerName
                        $server.ComputerName = $lookup.ComputerName
                        $server.FQDN = $lookup.FQDN
                        $server.IPAddress = $lookup.IPAddress
                    } catch {
                        # here to avoid an empty catch
                        $null = 1
                    }
                }
            }

            # this is a bit dirty and should be addressed by someone who better knows recursion and regex
            if ($server.Source -ne "Central Management Servers") {
                if ($PSBoundParameters.Group -and $groupname -notin $PSBoundParameters.Group) { continue }
                if ($PSBoundParameters.ExcludeGroup -and $groupname -in $PSBoundParameters.ExcludeGroup) { continue }
            }

            Add-Member -Force -InputObject $server -MemberType ScriptMethod -Name ToString -Value { $this.ServerName }
            Select-DefaultView -InputObject $server -Property $defaults
        }

        if ($IncludeSelf -and $serverstores) {
            foreach ($currentServerStore in $serverstores) {
                Write-Message -Level Verbose -Message "Adding CMS instance" -FunctionName Get-DbaRegServer -ModuleName "dbatools"
                $self = [PSCustomObject]@{
                    Name         = "CMS Instance"
                    ServerName   = $currentServerStore.SqlInstance
                    Group        = $null
                    Description  = $null
                    Source       = "Central Management Servers"
                    ComputerName = $currentServerStore.ComputerName
                    InstanceName = $currentServerStore.InstanceName
                    SqlInstance  = $currentServerStore.SqlInstance
                    ParentServer = $currentServerStore.ParentServer
                    FQDN         = $null
                    IPAddress    = $null
                }
                $self | Add-Member -MemberType ScriptMethod -Name ToString -Value { $this.ServerName } -Force
                Select-DefaultView -InputObject $self -Property $defaults
            }
        }
    }

    $__az = Get-Variable -Name azureids -Scope 0 -ErrorAction Ignore
    $__azv = $null; if ($__az) { $__azv = $__az.Value }
    @{ __getDbaRegServerProcess = @{ AzureidsAssigned = [bool]$__az; Azureids = $__azv } }
} $SqlInstance $SqlCredential $Name $ServerName $Pattern $ExcludeServerName $Group $ExcludeGroup $Id $IncludeSelf $ResolveNetworkName $IncludeLocal $EnableException $__beginState $__state $__boundParameters $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}