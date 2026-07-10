#nullable enable

using System;
using System.Collections.Generic;
using System.Management.Automation;
using System.Text.RegularExpressions;
using Dataplat.Dbatools.Connection;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;
using Dataplat.Dbatools.Utility;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Returns the Windows firewall rules for SQL Server on the target computer. Port of
/// public/Get-DbaFirewallRule.ps1; surface pinned by migration/baselines/Get-DbaFirewallRule.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaFirewallRule")]
[OutputType(typeof(PSObject))]
public sealed class GetDbaFirewallRuleCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance(s).</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[] SqlInstance { get; set; } = Array.Empty<DbaInstanceParameter>();

    /// <summary>Alternate Windows credential for the target computer.</summary>
    [Parameter(Position = 1)]
    public PSCredential? Credential { get; set; }

    /// <summary>Which rule types to return.</summary>
    [Parameter(Position = 2)]
    [ValidateSet("Engine", "Browser", "DAC", "DatabaseMirroring", "AllInstance")]
    public string[]? Type { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // Verbatim Invoke-Command2 scriptblock: reads Get-NetFirewallRule for the 'SQL Server' group and
    // returns an envelope {Successful, Rules, Verbose, Warning, Error, Exception}.
    private const string RuleScript = @"
            # This scriptblock will be processed by Invoke-Command2.
            try {
                if (-not (Get-Command -Name Get-NetFirewallRule -ErrorAction SilentlyContinue)) {
                    throw 'The module NetSecurity with the command Get-NetFirewallRule is missing on the target computer, so Get-DbaFirewallRule is not supported.'
                }
                $successful = $true
                $verbose = @( )
                $rules = Get-NetFirewallRule -Group 'SQL Server' -WarningVariable warn -ErrorVariable err -WarningAction SilentlyContinue -ErrorAction SilentlyContinue
                if ($warn.Count -gt 0) {
                    $successful = $false
                } else {
                    $warn = $null
                }
                if ($err.Count -gt 0) {
                    if ($err.Count -eq 1 -and $err[0] -match 'No MSFT_NetFirewallRule objects found') {
                        $verbose += ""No objects found. Detailed error message: $($err[0])""
                        $err = $null
                    } else {
                        $successful = $false
                    }
                } else {
                    $err = $null
                }
                if ($successful) {
                    $verbose += ""Get-NetFirewallRule was successful, we have $($rules.Count) rules.""
                    $rulesWithDetails = @( )
                    foreach ($rule in $rules) {
                        $rulesWithDetails += [PSCustomObject]@{
                            DisplayName = $rule.DisplayName
                            Name        = $rule.Name
                            Protocol    = ($rule | Get-NetFirewallPortFilter).Protocol
                            LocalPort   = ($rule | Get-NetFirewallPortFilter).LocalPort
                            Program     = ($rule | Get-NetFirewallApplicationFilter).Program
                            Rule        = $rule
                        }
                    }
                }
                [PSCustomObject]@{
                    Successful = $successful
                    Rules      = $rulesWithDetails
                    Verbose    = $verbose
                    Warning    = $warn
                    Error      = $err
                    Exception  = $null
                }
            } catch {
                [PSCustomObject]@{
                    Successful = $false
                    Rules      = $null
                    Verbose    = $null
                    Warning    = $null
                    Error      = $null
                    Exception  = $_
                }
            }";

    private static readonly string[] ErrorDisplaySet = { "ComputerName", "Warning", "Error", "Exception" };
    private static readonly string[] RuleDisplaySet = { "ComputerName", "InstanceName", "SqlInstance", "DisplayName", "Type", "Protocol", "LocalPort", "Program" };

    protected override void ProcessRecord()
    {
        if (Interrupted)
        {
            return;
        }

        foreach (DbaInstanceParameter instance in SqlInstance)
        {
            if (instance is null)
            {
                continue;
            }

            PSObject? commandResult = null;
            try
            {
                WriteMessage(MessageLevel.Debug, $"Executing Invoke-Command2 with ComputerName = {instance.ComputerName}.");
                RemoteExecutionService.RemoteCommandRequest request = new()
                {
                    ComputerName = new DbaInstanceParameter(instance.ComputerName),
                    Credential = Credential,
                    ScriptText = RuleScript,
                    Raw = true
                };
                RemoteExecutionService.RemoteCommandResult res = RemoteExecutionService.InvokeCommand(request);
                foreach (PSObject o in res.Output)
                {
                    if (o is not null) { commandResult = o; break; }
                }
                object? verbose = commandResult?.Properties["Verbose"]?.Value;
                foreach (object message in EnumerateAny(verbose))
                {
                    WriteMessage(MessageLevel.Verbose, message?.ToString() ?? string.Empty);
                }
            }
            catch (PipelineStoppedException)
            {
                throw;
            }
            catch (RuntimeException rex)
            {
                StopFunction($"Failed to execute command on {instance.ComputerName} for instance {instance.InstanceName}.", target: instance, errorRecord: rex.ErrorRecord, continueLoop: true);
                continue;
            }
            catch (Exception ex)
            {
                StopFunction($"Failed to execute command on {instance.ComputerName} for instance {instance.InstanceName}.", target: instance, exception: ex, continueLoop: true);
                continue;
            }

            // PS: if (-not $commandResult.Successful) { emit error object; continue }
            if (commandResult is null || !LanguagePrimitives.IsTrue(commandResult.Properties["Successful"]?.Value))
            {
                PSObject errObj = new();
                errObj.Properties.Add(new PSNoteProperty("ComputerName", instance.ComputerName));
                errObj.Properties.Add(new PSNoteProperty("Warning", commandResult?.Properties["Warning"]?.Value));
                errObj.Properties.Add(new PSNoteProperty("Error", commandResult?.Properties["Error"]?.Value));
                errObj.Properties.Add(new PSNoteProperty("Exception", commandResult?.Properties["Exception"]?.Value));
                errObj.Properties.Add(new PSNoteProperty("Details", commandResult));
                OutputHelper.SetDefaultDisplayPropertySet(errObj, ErrorDisplaySet);
                WriteObject(errObj);
                continue;
            }

            // PS: classify each rule by Name, build the detail object.
            List<PSObject> rules = new();
            foreach (object ruleObj in EnumerateAny(commandResult.Properties["Rules"]?.Value))
            {
                PSObject rule = ruleObj as PSObject ?? new PSObject(ruleObj);
                string ruleName = rule.Properties["Name"]?.Value?.ToString() ?? string.Empty;
                string? typeName = null, instanceName = null, sqlInstanceName = null;

                if (ruleName == "SQL Server Browser")
                {
                    typeName = "Browser";
                }
                else if (ruleName == "SQL Server default instance (DAC)")
                {
                    typeName = "DAC"; instanceName = "MSSQLSERVER"; sqlInstanceName = instance.ComputerName;
                }
                else if (ruleName == "SQL Server default instance (DatabaseMirroring)")
                {
                    typeName = "DatabaseMirroring"; instanceName = "MSSQLSERVER"; sqlInstanceName = instance.ComputerName;
                }
                else if (ruleName == "SQL Server default instance")
                {
                    typeName = "Engine"; instanceName = "MSSQLSERVER"; sqlInstanceName = instance.ComputerName;
                }
                else if (Regex.IsMatch(ruleName, @"SQL Server instance .+ \(DAC\)"))
                {
                    typeName = "DAC";
                    instanceName = Regex.Replace(ruleName, @"^SQL Server instance (.+) \(DAC\)$", "$1");
                    sqlInstanceName = instance.ComputerName + "\\" + instanceName;
                }
                else if (Regex.IsMatch(ruleName, @"SQL Server instance .+ \(DatabaseMirroring\)"))
                {
                    typeName = "DatabaseMirroring";
                    instanceName = Regex.Replace(ruleName, @"^SQL Server instance (.+) \(DatabaseMirroring\)$", "$1");
                    sqlInstanceName = instance.ComputerName + "\\" + instanceName;
                }
                else if (Regex.IsMatch(ruleName, @"SQL Server instance .+"))
                {
                    typeName = "Engine";
                    instanceName = Regex.Replace(ruleName, @"^SQL Server instance (.+)$", "$1");
                    sqlInstanceName = instance.ComputerName + "\\" + instanceName;
                }

                PSObject detail = new();
                detail.Properties.Add(new PSNoteProperty("ComputerName", instance.ComputerName));
                detail.Properties.Add(new PSNoteProperty("InstanceName", instanceName));
                detail.Properties.Add(new PSNoteProperty("SqlInstance", sqlInstanceName));
                detail.Properties.Add(new PSNoteProperty("DisplayName", rule.Properties["DisplayName"]?.Value));
                detail.Properties.Add(new PSNoteProperty("Name", ruleName));
                detail.Properties.Add(new PSNoteProperty("Type", typeName));
                detail.Properties.Add(new PSNoteProperty("Protocol", rule.Properties["Protocol"]?.Value));
                detail.Properties.Add(new PSNoteProperty("LocalPort", rule.Properties["LocalPort"]?.Value));
                detail.Properties.Add(new PSNoteProperty("Program", rule.Properties["Program"]?.Value));
                detail.Properties.Add(new PSNoteProperty("Rule", rule.Properties["Rule"]?.Value));
                detail.Properties.Add(new PSNoteProperty("Credential", Credential));
                rules.Add(detail);
            }

            // PS: which rules to output, by Type.
            List<PSObject> output = new();
            bool typeBound = Type is not null && Type.Length > 0;
            if (ContainsType("AllInstance"))
            {
                WriteMessage(MessageLevel.Verbose, "Returning all rules for target computer");
                output.AddRange(rules);
            }
            else if (!typeBound)
            {
                WriteMessage(MessageLevel.Verbose, "Returning rule for instance, DAC and maybe for Browser");
                output.AddRange(FilterRules(rules, r => (RuleType(r) == "Engine" || RuleType(r) == "DAC") && InstanceEq(r, instance.InstanceName)));
                if (output.Count == 0)
                {
                    WriteMessage(MessageLevel.Verbose, "No rule found for instance");
                }
                else if (AnyLocalPort1433(output))
                {
                    WriteMessage(MessageLevel.Verbose, "No rule for Browser needed");
                }
                else
                {
                    output.AddRange(FilterRules(rules, r => RuleType(r) == "Browser"));
                }
            }
            else
            {
                WriteMessage(MessageLevel.Verbose, "Returning specific rules");
                if (ContainsType("Engine"))
                {
                    output.AddRange(FilterRules(rules, r => RuleType(r) == "Engine" && InstanceEq(r, instance.InstanceName)));
                }
                if (ContainsType("Browser"))
                {
                    output.AddRange(FilterRules(rules, r => RuleType(r) == "Browser"));
                }
                if (ContainsType("DAC"))
                {
                    output.AddRange(FilterRules(rules, r => RuleType(r) == "DAC" && InstanceEq(r, instance.InstanceName)));
                }
                if (ContainsType("DatabaseMirroring"))
                {
                    output.AddRange(FilterRules(rules, r => RuleType(r) == "DatabaseMirroring" && InstanceEq(r, instance.InstanceName)));
                }
            }

            foreach (PSObject row in output)
            {
                OutputHelper.SetDefaultDisplayPropertySet(row, RuleDisplaySet);
                WriteObject(row);
            }
        }
    }

    private bool ContainsType(string candidate)
    {
        if (Type is null) { return false; }
        foreach (string t in Type)
        {
            if (string.Equals(t, candidate, StringComparison.OrdinalIgnoreCase)) { return true; }
        }
        return false;
    }

    private static string? RuleType(PSObject rule) => rule.Properties["Type"]?.Value?.ToString();

    private static bool InstanceEq(PSObject rule, string? instanceName)
    {
        return LanguagePrimitives.Equals(rule.Properties["InstanceName"]?.Value, instanceName, ignoreCase: true);
    }

    // PS: $outputRules.LocalPort -eq '1433' - true when ANY collected rule has LocalPort 1433.
    private static bool AnyLocalPort1433(IEnumerable<PSObject> rules)
    {
        foreach (PSObject r in rules)
        {
            if (LanguagePrimitives.Equals(r.Properties["LocalPort"]?.Value, "1433", ignoreCase: true)) { return true; }
        }
        return false;
    }

    private static List<PSObject> FilterRules(List<PSObject> rules, Func<PSObject, bool> predicate)
    {
        List<PSObject> result = new();
        foreach (PSObject r in rules) { if (predicate(r)) { result.Add(r); } }
        return result;
    }

    private static IEnumerable<object> EnumerateAny(object? value)
    {
        if (value is null) { yield break; }
        object baseObject = value is PSObject wrapped ? wrapped.BaseObject : value;
        if (baseObject is string) { yield return value; yield break; }
        if (baseObject is System.Collections.IEnumerable enumerable)
        {
            foreach (object? item in enumerable) { if (item is not null) { yield return item; } }
            yield break;
        }
        yield return value;
    }
}
