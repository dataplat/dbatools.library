#nullable enable

using System;
using System.Collections.Generic;
using System.Management.Automation;
using System.Xml;
using Dataplat.Dbatools.Connection;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Runs the SQL Server setup discovery report on target computers and returns the installed
/// feature inventory. Port of public/Get-DbaFeature.ps1; surface pinned by
/// migration/baselines/Get-DbaFeature.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaFeature")]
[OutputType(typeof(PSObject))]
public sealed class GetDbaFeatureCommand : DbaBaseCmdlet
{
    /// <summary>The target computer(s) to run the discovery report on; defaults to the local computer.</summary>
    [Parameter(ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[]? ComputerName { get; set; } = DefaultComputerName();

    /// <summary>Alternate credential for connecting to the remote computer.</summary>
    [Parameter(Position = 1)]
    public PSCredential? Credential { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // PS begin block, verbatim: locates the newest setup.exe under Setup Bootstrap, runs
    // /Action=RunDiscovery /q and returns the SqlDiscoveryReport.xml content line by line.
    private const string DiscoveryScript = @"
            $setup = Get-ChildItem -Recurse -Include setup.exe -Path ""$([System.Environment]::GetFolderPath(""ProgramFiles""))\Microsoft SQL Server"" -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -match 'Setup Bootstrap\\SQL' -or $_.FullName -match 'Bootstrap\\Release\\Setup.exe' -or $_.FullName -match 'Bootstrap\\Setup.exe' } |
                Sort-Object FullName -Descending | Select-Object -First 1
            if ($setup) {
                $null = Start-Process -FilePath $setup.FullName -ArgumentList ""/Action=RunDiscovery /q"" -Wait
                $parent = Split-Path (Split-Path $setup.Fullname)
                $xmlfile = Get-ChildItem -Recurse -Include SqlDiscoveryReport.xml -Path $parent | Sort-Object LastWriteTime -Descending | Select-Object -First 1

                if ($xmlfile) {
                    Get-Content -Path $xmlfile
                }
            }";

    protected override void ProcessRecord()
    {
        if (Interrupted)
        {
            return;
        }

        if (ComputerName is null)
        {
            return;
        }

        // PS: foreach ($computer in $ComputerName) - the loop variable stays a
        // DbaInstanceParameter and lands verbatim in the output's ComputerName (live-proven).
        foreach (DbaInstanceParameter computer in ComputerName)
        {
            if (computer is null)
            {
                continue;
            }

            try
            {
                // PS: $text = Invoke-Command2 -ComputerName $Computer -ScriptBlock $scriptBlock
                //     -Credential $Credential -Raw
                RemoteExecutionService.RemoteCommandRequest request = new()
                {
                    ComputerName = computer,
                    Credential = Credential,
                    ScriptText = DiscoveryScript,
                    Raw = true
                };
                RemoteExecutionService.RemoteCommandResult result = RemoteExecutionService.InvokeCommand(request);
                foreach (ErrorRecord error in result.Errors)
                {
                    WriteError(error);
                }
                object? text = ShapeOutput(result.Output);

                // PS: if (-not $text) { Write-Message -Level Verbose "No features found on $computer" }
                if (!LanguagePrimitives.IsTrue(text))
                {
                    WriteMessage(MessageLevel.Verbose, $"No features found on {computer}");
                }

                // PS: $xml = [xml]($text) - the engine cast: a line array joins via $OFS before
                // parsing, $null casts to null, and an empty string THROWS into the catch below.
                XmlDocument? xml = LanguagePrimitives.ConvertTo<XmlDocument>(text);

                // PS: foreach ($result in $xml.ArrayOfDiscoveryInformation.DiscoveryInformation)
                object? infoArray = GetMemberValue(xml, "ArrayOfDiscoveryInformation");
                object? infoItems = GetMemberValue(infoArray, "DiscoveryInformation");
                if (infoItems is not null)
                {
                    foreach (object item in EnumeratePipeline(infoItems))
                    {
                        PSObject output = new();
                        output.Properties.Add(new PSNoteProperty("ComputerName", computer));
                        output.Properties.Add(new PSNoteProperty("Product", GetMemberValue(item, "Product")));
                        output.Properties.Add(new PSNoteProperty("Instance", GetMemberValue(item, "Instance")));
                        output.Properties.Add(new PSNoteProperty("InstanceID", GetMemberValue(item, "InstanceID")));
                        output.Properties.Add(new PSNoteProperty("Feature", GetMemberValue(item, "Feature")));
                        output.Properties.Add(new PSNoteProperty("Language", GetMemberValue(item, "Language")));
                        output.Properties.Add(new PSNoteProperty("Edition", GetMemberValue(item, "Edition")));
                        output.Properties.Add(new PSNoteProperty("Version", GetMemberValue(item, "Version")));
                        output.Properties.Add(new PSNoteProperty("Clustered", GetMemberValue(item, "Clustered")));
                        output.Properties.Add(new PSNoteProperty("Configured", GetMemberValue(item, "Configured")));
                        WriteObject(output);
                    }
                }
            }
            catch (PipelineStoppedException)
            {
                throw;
            }
            catch (RuntimeException rex)
            {
                StopFunction("Failure", errorRecord: rex.ErrorRecord, continueLoop: true);
                continue;
            }
            catch (Exception ex)
            {
                StopFunction("Failure", exception: ex, continueLoop: true);
                continue;
            }
        }
    }

    // PS pipeline-assignment shape: empty -> null, one -> the scalar, many -> object[].
    private static object? ShapeOutput(List<PSObject> output)
    {
        if (output.Count == 0)
        {
            return null;
        }
        if (output.Count == 1)
        {
            return output[0];
        }
        return output.ToArray();
    }

    // PS: foreach over a property value - a scalar iterates once, an array element-wise.
    private static IEnumerable<object> EnumeratePipeline(object value)
    {
        if (value is object[] many)
        {
            foreach (object item in many)
            {
                if (item is not null)
                {
                    yield return item;
                }
            }
        }
        else
        {
            yield return value;
        }
    }

    // PS property read over a pipeline-shaped value via the PSObject view - the XML adapter
    // resolves element children (simple text elements read back as strings, like $xml.a.b).
    private static object? GetMemberValue(object? source, string name)
    {
        if (source is null)
        {
            return null;
        }
        if (source is object[] many)
        {
            List<object?> values = new();
            foreach (object item in many)
            {
                if (item is null)
                {
                    continue;
                }
                PSPropertyInfo? property = PSObject.AsPSObject(item).Properties[name];
                if (property is not null)
                {
                    values.Add(property.Value);
                }
            }
            if (values.Count == 0)
            {
                return null;
            }
            if (values.Count == 1)
            {
                return values[0];
            }
            return values.ToArray();
        }
        return PSObject.AsPSObject(source).Properties[name]?.Value;
    }

    // PS: [DbaInstanceParameter[]]$ComputerName = $env:COMPUTERNAME
    private static DbaInstanceParameter[]? DefaultComputerName()
    {
        string? machine = Environment.GetEnvironmentVariable("COMPUTERNAME");
        if (string.IsNullOrEmpty(machine))
        {
            return null;
        }
        return new[] { new DbaInstanceParameter(machine) };
    }
}
