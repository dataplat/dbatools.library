#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves asymmetric keys from one or more SQL Server databases.
/// </summary>
/// <remarks>
/// <para>
/// The workflow remains a module-scoped PowerShell compatibility hop so that database enumeration, the
/// asymmetric-key retrieval, the added note properties, the default view, and dbatools stream and error
/// handling stay observable-identical to the script implementation.
/// </para>
/// <para>
/// The command is process-only, so it ships as a single hop per record. Database objects piped through
/// InputObject bind per record; when SqlInstance is supplied instead, the body appends the resolved
/// databases to InputObject within that record - so no cross-record state is carried. The body has no
/// Stop-Function, try/catch, or other terminating path (only Write-Message warnings and continue), so
/// there is no earlier-output-before-later-throw exposure and buffered InvokeScoped is correct.
/// EnableException is carried into the hop as a plain (untyped) value, because a switch in the inner
/// CmdletBinding scriptblock is excluded from positional binding.
/// </para>
/// </remarks>
[Cmdlet(VerbsCommon.Get, "DbaDbAsymmetricKey")]
[OutputType(typeof(Microsoft.SqlServer.Management.Smo.AsymmetricKey))]
public sealed class GetDbaDbAsymmetricKeyCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Alternative credential for the target instances.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The database or databases to scan for asymmetric keys.</summary>
    [Parameter(Position = 2)]
    public string[]? Database { get; set; }

    /// <summary>Databases to exclude from the scan.</summary>
    [Parameter(Position = 3)]
    public string[]? ExcludeDatabase { get; set; }

    /// <summary>Filters results to asymmetric keys with these names.</summary>
    [Parameter(Position = 4)]
    public string[]? Name { get; set; }

    /// <summary>Database objects, typically piped from Get-DbaDatabase.</summary>
    [Parameter(ValueFromPipeline = true, Position = 5)]
    public Microsoft.SqlServer.Management.Smo.Database[]? InputObject { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    /// <summary>Retrieves asymmetric keys for one pipeline record.</summary>
    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, ProcessScript,
            SqlInstance, SqlCredential, Database, ExcludeDatabase, Name, InputObject,
            EnableException.ToBool(),
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

    // PS: the process body VERBATIM. Substitutions only: -FunctionName on the two direct Write-Message
    // calls (no nested named helper; no Stop-Function; no ShouldProcess). EnableException is received as
    // a plain (untyped) param - never re-typed [switch] - because a [switch] in the inner [CmdletBinding()]
    // scriptblock is excluded from positional binding. The undefined $instance in the verbose message is
    // a source artifact (it stays empty) and is preserved verbatim.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Database, $ExcludeDatabase, $Name, $InputObject, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [System.Management.Automation.PSCredential]$SqlCredential, [string[]]$Database, [string[]]$ExcludeDatabase, [string[]]$Name, [Microsoft.SqlServer.Management.Smo.Database[]]$InputObject, $EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

        if ($SqlInstance) {
            $InputObject += Get-DbaDatabase -SqlInstance $SqlInstance -SqlCredential $SqlCredential -Database $Database -ExcludeDatabase $ExcludeDatabase
        }

        foreach ($db in $InputObject) {
            if (!$db.IsAccessible) {
                Write-Message -Level Warning -Message "$db is not accessible, skipping" -FunctionName Get-DbaDbAsymmetricKey
                continue
            }

            $akeys = $db.AsymmetricKeys

            if ($null -eq $akeys) {
                Write-Message -Message "No Asymmetic Keys exists in the $db database on $instance" -Target $db -Level Verbose -FunctionName Get-DbaDbAsymmetricKey
                continue
            }

            if ($Name) {
                $akeys = $akeys | Where-Object Name -in $Name
            }

            foreach ($akey in $akeys) {
                Add-Member -Force -InputObject $akey -MemberType NoteProperty -Name ComputerName -value $db.ComputerName
                Add-Member -Force -InputObject $akey -MemberType NoteProperty -Name InstanceName -value $db.InstanceName
                Add-Member -Force -InputObject $akey -MemberType NoteProperty -Name SqlInstance -value $db.SqlInstance
                Add-Member -Force -InputObject $akey -MemberType NoteProperty -Name Database -value $db.Name
                Add-Member -Force -InputObject $akey -MemberType NoteProperty -Name DatabaseId -value $db.Id

                Select-DefaultView -InputObject $akey -Property ComputerName, InstanceName, SqlInstance, Database, Name, Owner, KeyEncryptionAlgorithm, KeyLength, PrivateKeyEncryptionType, Thumbprint
            }
        }
} $SqlInstance $SqlCredential $Database $ExcludeDatabase $Name $InputObject $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1

""";
}
