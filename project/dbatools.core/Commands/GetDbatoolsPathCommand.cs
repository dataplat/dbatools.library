#nullable enable

using System.Collections;
using System.Collections.ObjectModel;
using System.Management.Automation;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Returns the path of a managed dbatools path key. Port of public/Get-DbatoolsPath.ps1:
/// the single Get-DbatoolsConfigValue -FullName "Path.Managed.$Name" call rides the (now
/// compiled) command through NestedCommand so binder, store and null-shape semantics are
/// identical, with the process-block body preserved via ProcessRecord. Surface pinned by
/// migration/baselines/Get-DbatoolsPath.json (Mandatory Name pos0).
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbatoolsPath")]
[OutputType(typeof(object))]
public sealed class GetDbatoolsPathCommand : DbaBaseCmdlet
{
    /// <summary>The name of the managed path to retrieve.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    public string? Name { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        if (Interrupted)
        {
            return;
        }

        // PS: Get-DbatoolsConfigValue -FullName "Path.Managed.$Name"
        Hashtable splatValue = new();
        splatValue["FullName"] = "Path.Managed." + Name;
        Collection<PSObject> output = NestedCommand.Invoke(this, "Get-DbatoolsConfigValue", splatValue);
        foreach (PSObject item in output)
        {
            WriteObject(item);
        }
    }
}
