#nullable enable

using System;
using System.Management.Automation;
using Dataplat.Dbatools.Configuration;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Returns the configuration value stored under a key. Port of
/// public/Get-DbatoolsConfigValue.ps1: lowered-key store indexer (a missing key
/// null-propagates), Fallback substitution, the Mandatory/Optional string-to-bool coercions
/// (two sequential PS method-call ToStrings with case-insensitive -eq), and the -NotNull
/// guard whose Stop-Function call hardcodes -EnableException $true and interpolates the
/// NONEXISTENT $Name variable as empty ("No Configuration Value available for " with the
/// trailing space - characterized as-is, TA-024). The function body had no
/// begin/process/end blocks, so EndProcessing preserves the END-block semantics. Surface
/// pinned by migration/baselines/Get-DbatoolsConfigValue.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbatoolsConfigValue")]
[OutputType(typeof(object))]
public sealed class GetDbatoolsConfigValueCommand : DbaBaseCmdlet
{
    /// <summary>The full name (module.name) of the configured value.</summary>
    [Alias("Name")]
    [Parameter(Mandatory = true)]
    public string? FullName { get; set; }

    /// <summary>A fallback value returned when the setting holds no value.</summary>
    [Parameter]
    public object? Fallback { get; set; }

    /// <summary>Throws instead of returning null when no value is available.</summary>
    [Parameter]
    public SwitchParameter NotNull { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void EndProcessing()
    {
        if (Interrupted)
        {
            return;
        }

        string fullName = FullName!.ToLowerInvariant();

        // PS: [ConfigurationHost]::Configurations[$FullName].Value - a missing key indexes
        // null and the .Value member access null-propagates.
        object? temp = null;
        if (ConfigurationHost.Configurations.TryGetValue(fullName, out Config? config) && config is not null)
        {
            temp = config.Value;
        }
        if (temp is null)
        {
            temp = Fallback;
        }
        else
        {
            // Prevent some potential [switch] parse issues
            // PS: two SEQUENTIAL $temp.ToString() -eq checks - the second reads the possibly
            // replaced value; -eq is the case-insensitive campaign PsString.Eq convention.
            if (PsStringEq(PsToStringCall(temp), "Mandatory"))
            {
                temp = true;
            }
            if (PsStringEq(PsToStringCall(temp), "Optional"))
            {
                temp = false;
            }
        }

        if (NotNull.IsPresent && temp is null)
        {
            // PS: Stop-Function ... -EnableException $true (hardcoded regardless of the
            // caller); the base reads the cmdlet's EnableException, so force it on - the
            // call throws immediately and nothing follows.
            EnableException = SwitchParameter.Present;
            StopFunction("No Configuration Value available for ", target: fullName, category: ErrorCategory.InvalidData);
            return;
        }

        WriteObject(temp);
    }

    // PS METHOD-CALL ToString: binder unwrap + virtual ToString (the W1-002 PsToStringCall
    // shape; null is impossible here - the surrounding branch guards it).
    private static string PsToStringCall(object value)
    {
        object baseValue = value is PSObject wrapped ? wrapped.BaseObject : value;
        return baseValue.ToString() ?? string.Empty;
    }

    // PS -eq string comparison (case-insensitive, the campaign PsString.Eq convention).
    private static bool PsStringEq(string left, string right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }
}
