#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Compares the logins on a source SQL Server instance against one or more destination instances.
/// </summary>
/// <remarks>
/// The workflow remains a module-scoped PowerShell compatibility hop so that login discovery, the
/// comparison, output shape, and dbatools stream and error handling stay observable-identical to the
/// script implementation. The command takes no pipeline input, so its begin and process blocks each
/// run exactly once; they are shipped as a single hop, which preserves the source's reliance on the
/// source-server lookup from the begin block being visible to the process block through one shared
/// scope. The begin connection failure sets the interrupt flag and returns before the comparison
/// runs, exactly as in the source.
/// </remarks>
[Cmdlet(VerbsData.Compare, "DbaLogin")]
public sealed class CompareDbaLoginCommand : DbaBaseCmdlet
{
    /// <summary>The source SQL Server instance.</summary>
    [Parameter(Mandatory = true)]
    public DbaInstanceParameter Source { get; set; } = null!;

    /// <summary>Alternative credential for the source instance.</summary>
    [Parameter]
    public PSCredential? SourceSqlCredential { get; set; }

    /// <summary>The destination SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true)]
    public DbaInstanceParameter[] Destination { get; set; } = null!;

    /// <summary>Alternative credential for the destination instances.</summary>
    [Parameter]
    public PSCredential? DestinationSqlCredential { get; set; }

    /// <summary>The login or logins to compare; all logins are compared when omitted.</summary>
    [Parameter]
    public string[]? Login { get; set; }

    /// <summary>Logins to exclude from the comparison.</summary>
    [Parameter]
    public string[]? ExcludeLogin { get; set; }

    /// <summary>Excludes system logins from the comparison.</summary>
    [Parameter]
    public SwitchParameter ExcludeSystemLogin { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    /// <summary>Runs the comparison. Fires once because the command takes no pipeline input.</summary>
    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, BodyScript,
            Source, SourceSqlCredential, Destination, DestinationSqlCredential, Login, ExcludeLogin,
            ExcludeSystemLogin.ToBool(), EnableException.ToBool(),
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

    // PS: the begin body followed by the process body, VERBATIM. The two blocks are concatenated
    // because the command has no pipeline input, so each ran once and shared one scope - the process
    // block reads $sourceServer and $sourceLogins that the begin block set. Substitutions only:
    // -FunctionName on the two direct Stop-Function calls. The begin catch keeps its explicit return,
    // which now exits the hop before the process body, matching the source (begin failure sets the
    // interrupt flag and the process block's Test-FunctionInterrupt guard would have returned anyway).
    private const string BodyScript = """
param($Source, $SourceSqlCredential, $Destination, $DestinationSqlCredential, $Login, $ExcludeLogin, $ExcludeSystemLogin, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter]$Source, [PSCredential]$SourceSqlCredential, [Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$Destination, [PSCredential]$DestinationSqlCredential, [string[]]$Login, [string[]]$ExcludeLogin, $ExcludeSystemLogin, $EnableException, $__boundVerbose, $__boundDebug)

    try {
        $sourceServer = Connect-DbaInstance -SqlInstance $Source -SqlCredential $SourceSqlCredential
    } catch {
        Stop-Function -Message "Failure connecting to $Source" -Category ConnectionError -ErrorRecord $_ -Target $Source -FunctionName Compare-DbaLogin
        return
    }

    $splatGetSource = @{
        SqlInstance        = $sourceServer
        ExcludeSystemLogin = $ExcludeSystemLogin
    }
    if ($Login) {
        $splatGetSource["Login"] = $Login
    }
    if ($ExcludeLogin) {
        $splatGetSource["ExcludeLogin"] = $ExcludeLogin
    }
    $sourceLogins = Get-DbaLogin @splatGetSource

    if (Test-FunctionInterrupt) { return }

    foreach ($destInstance in $Destination) {
        $destServer = $null
        try {
            $destServer = Connect-DbaInstance -SqlInstance $destInstance -SqlCredential $DestinationSqlCredential
        } catch {
            Stop-Function -Message "Failure connecting to $destInstance" -Category ConnectionError -ErrorRecord $_ -Target $destInstance -Continue -FunctionName Compare-DbaLogin
        }
        if ($null -eq $destServer) { continue }

        $splatGetDest = @{
            SqlInstance        = $destServer
            ExcludeSystemLogin = $ExcludeSystemLogin
        }
        if ($Login) {
            $splatGetDest["Login"] = $Login
        }
        if ($ExcludeLogin) {
            $splatGetDest["ExcludeLogin"] = $ExcludeLogin
        }
        $destLogins = Get-DbaLogin @splatGetDest

        $allLoginNames = New-Object System.Collections.ArrayList
        foreach ($srcLogin in $sourceLogins) {
            if ($srcLogin.Name -notin $allLoginNames) {
                $null = $allLoginNames.Add($srcLogin.Name)
            }
        }
        foreach ($dstLogin in $destLogins) {
            if ($dstLogin.Name -notin $allLoginNames) {
                $null = $allLoginNames.Add($dstLogin.Name)
            }
        }

        foreach ($loginName in $allLoginNames) {
            $srcLogin = $sourceLogins | Where-Object Name -eq $loginName
            $dstLogin = $destLogins | Where-Object Name -eq $loginName

            if ($srcLogin -and $dstLogin) {
                $status = "Both"
            } elseif ($srcLogin) {
                $status = "SourceOnly"
            } else {
                $status = "DestinationOnly"
            }

            [PSCustomObject]@{
                SourceServer      = $sourceServer.Name
                DestinationServer = $destServer.Name
                LoginName         = $loginName
                LoginType         = if ($srcLogin) { $srcLogin.LoginType } else { $dstLogin.LoginType }
                Status            = $status
            }
        }
    }
} $Source $SourceSqlCredential $Destination $DestinationSqlCredential $Login $ExcludeLogin $ExcludeSystemLogin $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
