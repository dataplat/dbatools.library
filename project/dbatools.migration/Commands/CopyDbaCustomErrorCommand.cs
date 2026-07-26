#nullable enable

using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Copies user-defined error messages and their language translations between instances. Port of
/// public/Copy-DbaCustomError.ps1. The whole workflow rides one module-scoped PowerShell hop
/// because it leans on SMO UserDefinedMessages scripting, the us_english-first ordering, and
/// Select-DefaultView decoration, all of which keep the retired function's engine semantics
/// there. The compiled cmdlet supplies the real ShouldProcess runtime. Surface pinned by
/// migration/baselines/Copy-DbaCustomError.json.
/// </summary>
[Cmdlet(VerbsCommon.Copy, "DbaCustomError", DefaultParameterSetName = "Default",
    SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Medium)]
public sealed class CopyDbaCustomErrorCommand : DbaBaseCmdlet
{
    /// <summary>The source SQL Server instance. Requires sysadmin and SQL Server 2005 or higher.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    public DbaInstanceParameter Source { get; set; } = null!;

    /// <summary>Alternative credential for the source instance.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SourceSqlCredential { get; set; }

    /// <summary>The destination SQL Server instances.</summary>
    [Parameter(Mandatory = true, Position = 2)]
    public DbaInstanceParameter[] Destination { get; set; } = null!;

    /// <summary>Alternative credential for destination instances.</summary>
    [Parameter(Position = 3)]
    public PSCredential? DestinationSqlCredential { get; set; }

    /// <summary>Only copy the custom errors with these message IDs.</summary>
    [Parameter(Position = 4)]
    public object[]? CustomError { get; set; }

    /// <summary>Skip the custom errors with these message IDs.</summary>
    [Parameter(Position = 5)]
    public object[]? ExcludeCustomError { get; set; }

    /// <summary>Drop and recreate custom errors that already exist on the destination.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        NestedCommand.InvokeScopedStreaming(this, item =>
        {
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                NestedCommand.RemoveDuplicateError(this, nestedError);
                WriteError(nestedError);
            }
            else
            {
                WriteObject(item);
            }
        }, BodyScript,
            Source, SourceSqlCredential, Destination, DestinationSqlCredential,
            CustomError, ExcludeCustomError, Force.ToBool(),
            EnableException.ToBool(), this, NestedCommand.BoundCommonParameter(this, "Verbose"),
            NestedCommand.BoundCommonParameter(this, "Debug"));
    }

    private const string BodyScript = """
param($Source, $SourceSqlCredential, $Destination, $DestinationSqlCredential, $CustomError, $ExcludeCustomError, $Force, $EnableException, $__realCmdlet, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter]$Source, $SourceSqlCredential, [Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$Destination, $DestinationSqlCredential, [object[]]$CustomError, [object[]]$ExcludeCustomError, $Force, $EnableException, $__realCmdlet, $__boundVerbose, $__boundDebug)

    try {
        $sourceServer = Connect-DbaInstance -SqlInstance $Source -SqlCredential $SourceSqlCredential -MinimumVersion 9
    } catch {
        Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $Source -FunctionName Copy-DbaCustomError
        return
    }
    $orderedCustomErrors = @($sourceServer.UserDefinedMessages | Where-Object Language -eq "us_english")
    $orderedCustomErrors += $sourceServer.UserDefinedMessages | Where-Object Language -ne "us_english"

    if ($Force) { $ConfirmPreference = 'none' }

    if (Test-FunctionInterrupt) { return }
    foreach ($destinstance in $Destination) {
        try {
            $destServer = Connect-DbaInstance -SqlInstance $destinstance -SqlCredential $DestinationSqlCredential -MinimumVersion 9
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $destinstance -Continue -FunctionName Copy-DbaCustomError
        }
        # US has to go first
        $destCustomErrors = $destServer.UserDefinedMessages

        foreach ($currentCustomError in $orderedCustomErrors) {
            $customErrorId = $currentCustomError.ID
            $language = $currentCustomError.Language.ToString()

            $copyCustomErrorStatus = [PSCustomObject]@{
                SourceServer      = $sourceServer.Name
                DestinationServer = $destServer.Name
                Type              = "Custom error"
                Name              = $currentCustomError
                Status            = $null
                Notes             = $null
                DateTime          = [Dataplat.Dbatools.Utility.DbaDateTime](Get-Date)
            }

            if ($CustomError -and ($customErrorId -notin $CustomError -or $customErrorId -in $ExcludeCustomError)) {
                continue
            }

            if ($destCustomErrors.ID -contains $customErrorId) {
                if ($force -eq $false) {
                    If ($__realCmdlet.ShouldProcess($destinstance, "Custom error $customErrorId $language exists at destination. Use -Force to drop and migrate.")) {
                        $copyCustomErrorStatus.Status = "Skipped"
                        $copyCustomErrorStatus.Notes = "Already exists on destination"
                        $copyCustomErrorStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject

                        Write-Message -Level Verbose -Message "Custom error $customErrorId $language exists at destination. Use -Force to drop and migrate." -FunctionName Copy-DbaCustomError -ModuleName "dbatools"
                    }
                    continue
                } else {
                    If ($__realCmdlet.ShouldProcess($destinstance, "Dropping custom error $customErrorId $language and recreating")) {
                        try {
                            Write-Message -Level Verbose -Message "Dropping custom error $customErrorId (drops all languages for custom error $customErrorId)" -FunctionName Copy-DbaCustomError -ModuleName "dbatools"
                            $destServer.UserDefinedMessages[$customErrorId, $language].Drop()
                        } catch {
                            $copyCustomErrorStatus.Status = "Failed"
                            $copyCustomErrorStatus.Notes = "$PSItem"
                            $copyCustomErrorStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                            Write-Message -Level Verbose -Message "Issue dropping custom error $customErrorId $language on $destinstance | $PSItem" -FunctionName Copy-DbaCustomError -ModuleName "dbatools"
                            continue
                        }
                    }
                }
            }

            if ($__realCmdlet.ShouldProcess($destinstance, "Creating custom error $customErrorId $language")) {
                try {
                    Write-Message -Level Verbose -Message "Copying custom error $customErrorId $language" -FunctionName Copy-DbaCustomError -ModuleName "dbatools"
                    $sql = $currentCustomError.Script() | Out-String
                    Write-Message -Level Debug -Message $sql -FunctionName Copy-DbaCustomError -ModuleName "dbatools"
                    $destServer.Query($sql)
                    $copyCustomErrorStatus.Status = "Successful"
                    $copyCustomErrorStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                } catch {
                    $copyCustomErrorStatus.Status = "Failed"
                    $copyCustomErrorStatus.Notes = "$PSItem"
                    $copyCustomErrorStatus | Select-DefaultView -Property DateTime, SourceServer, DestinationServer, Name, Type, Status, Notes -TypeName MigrationObject
                    Write-Message -Level Verbose -Message "Issue creating custom error $customErrorId $language on $destinstance | $PSItem" -FunctionName Copy-DbaCustomError -ModuleName "dbatools"
                    continue
                }
            }
        }
    }
} $Source $SourceSqlCredential $Destination $DestinationSqlCredential $CustomError $ExcludeCustomError $Force $EnableException $__realCmdlet $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
