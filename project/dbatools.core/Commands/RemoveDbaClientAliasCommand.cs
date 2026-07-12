#nullable enable

using System;
using System.Collections;
using System.Globalization;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Removes SQL Server client aliases from the Windows registry (HKLM ConnectTo keys, both
/// 32-bit and 64-bit hives). Port of public/Remove-DbaClientAlias.ps1 (W1-033). Sibling of
/// New-DbaClientAlias (W1-026): the registry scriptblock rides Invoke-Command2 VERBATIM
/// (local in-process or remoting, exactly like the function) and Test-ElevationRequirement
/// runs module-scoped with -Continue, so the private helper's dynamically scoped continue
/// propagates out of the nested script as the engine flow-control exception and continues
/// THIS cmdlet's computer loop. Warnings from both helpers merge back 3&gt;&amp;1 for caller
/// -WarningVariable parity. PS function parameters are positional by default, so Positions
/// 0-2 are pinned. One object per removed alias is emitted immediately to the pipeline.
/// Surface pinned by migration/baselines/Remove-DbaClientAlias.json.
/// </summary>
[Cmdlet(VerbsCommon.Remove, "DbaClientAlias", SupportsShouldProcess = true)]
public sealed class RemoveDbaClientAliasCommand : DbaBaseCmdlet
{
    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    [Parameter(ValueFromPipelineByPropertyName = true, Position = 0)]
    public DbaInstanceParameter[]? ComputerName { get; set; }

    [Parameter(Position = 1)]
    public PSCredential? Credential { get; set; }

    [Parameter(Mandatory = true, ValueFromPipelineByPropertyName = true, Position = 2)]
    [Alias("AliasName")]
    public string[] Alias { get; set; } = null!;

    protected override void BeginProcessing()
    {
        // PS: [DbaInstanceParameter[]]$ComputerName = $env:COMPUTERNAME (bind-time cast;
        // a null environment value casts to null and the process loop just never runs).
        if (!TestBound("ComputerName"))
        {
            string? localName = Environment.GetEnvironmentVariable("COMPUTERNAME");
            if (localName is not null)
                ComputerName = (DbaInstanceParameter[])LanguagePrimitives.ConvertTo(localName, typeof(DbaInstanceParameter[]), CultureInfo.InvariantCulture);
        }
    }

    protected override void ProcessRecord()
    {
        if (ComputerName is null)
            return;

        // PS: foreach ($computer in $ComputerName) - member enumeration of the parameter array.
        foreach (DbaInstanceParameter computer in ComputerName)
        {
            string computerText = computer.ComputerName;

            // PS: $null = Test-ElevationRequirement -ComputerName $computer -Continue -
            // output discarded, warnings bubble, the failure continue targets this loop.
            try
            {
                NestedCommand.InvokeScoped(this, TestElevationScript, computerText, EnableException.ToBool());
            }
            catch (FlowControlException)
            {
                continue;
            }

            // PS: if ($PSCmdlet.ShouldProcess("$($Alias -join ', ') on $computer", "Remove aliases"))
            if (ShouldProcess(string.Join(", ", Alias) + " on " + computerText, "Remove aliases"))
            {
                try
                {
                    foreach (PSObject item in NestedCommand.InvokeScoped(this, InvokeCommand2Script, computerText, Credential, Alias))
                        WriteObject(item);
                }
                catch (PipelineStoppedException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // PS: catch { Stop-Function -Message "Failure" -ErrorRecord $_ -Target $computer -Continue }
                    StopFunction("Failure", target: computerText, errorRecord: ToCaughtRecord(ex), continueLoop: true);
                    continue;
                }
            }
        }
    }

    /// <summary>PS: catch { $_ } - a nested terminating error carries the original failing
    /// record; a hand-built RuntimeException's lazy record drops the inner chain
    /// (ParentContainsErrorRecordException), so that shape rebuilds from the exception.</summary>
    private static ErrorRecord ToCaughtRecord(Exception ex)
    {
        if (ex is RuntimeException runtime && runtime.ErrorRecord is not null &&
            runtime.ErrorRecord.Exception is not ParentContainsErrorRecordException)
        {
            return runtime.ErrorRecord;
        }
        return new ErrorRecord(ex, "Remove-DbaClientAlias", ErrorCategory.NotSpecified, null);
    }

    private const string TestElevationScript = """
param($computer, $enable)
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    param($computer, $enable)
    Test-ElevationRequirement -ComputerName $computer -Continue -EnableException $enable 3>&1
} $computer $enable
""";

    // The inner registry scriptblock is VERBATIM from the function's begin block (comments
    // included) - Invoke-Command2 runs it in-process locally or serializes it for remoting.
    private const string InvokeCommand2Script = """
param($computer, $credential, $Alias)
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    param($computer, $credential, $Alias)
    $scriptBlock = {
        $Alias = $args

        $basekeys = "HKLM:\SOFTWARE\WOW6432Node\Microsoft\MSSQLServer", "HKLM:\SOFTWARE\Microsoft\MSSQLServer"

        foreach ($basekey in $basekeys) {
            $fullKey = "$basekey\Client\ConnectTo"
            if ((Test-Path $fullKey) -eq $false) {
                <# DO NOT use Write-Message as this is inside of a script block #>
                Write-Warning "Registry key ($fullKey) does not exist on $env:COMPUTERNAME"
                continue
            }

            if ($basekey -like "*WOW64*") {
                $architecture = "32-bit"
            } else {
                $architecture = "64-bit"
            }

            $all = Get-Item -Path $fullKey
            foreach ($entry in $all) {
                $e = $entry.ToString().Replace('HKEY_LOCAL_MACHINE', 'HKLM:\')
                foreach ($a in $Alias) {
                    if ($entry.Property -contains $a) {
                        $null = Remove-ItemProperty -Path $e -Name $a
                        [PSCustomObject]@{
                            ComputerName = $env:COMPUTERNAME
                            Architecture = $architecture
                            Alias        = $a
                            Status       = "Removed"
                        }
                    }
                }
            }
        }
    }
    Invoke-Command2 -ComputerName $computer -Credential $credential -ScriptBlock $scriptBlock -ErrorAction Stop -Verbose:$false -ArgumentList $Alias 3>&1
} $computer $credential $Alias
""";
}
