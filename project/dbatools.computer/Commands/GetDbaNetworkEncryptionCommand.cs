#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves the TLS certificate a SQL Server instance presents during the TDS pre-login
/// handshake. Port of public/Get-DbaNetworkEncryption.ps1; surface pinned by
/// migration/baselines/Get-DbaNetworkEncryption.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaNetworkEncryption")]
[OutputType(typeof(PSObject))]
public sealed class GetDbaNetworkEncryptionCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(ValueFromPipeline = true, Position = 0, Mandatory = true)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        if (Interrupted)
        {
            return;
        }

        if (SqlInstance is null)
        {
            return;
        }

        foreach (DbaInstanceParameter instance in SqlInstance)
        {
            if (instance is null)
            {
                continue;
            }

            try
            {
                string computerName = instance.ComputerName;
                WriteMessage(MessageLevel.Verbose, $"Using computerName '{computerName}'");
                string instanceName = instance.InstanceName;
                WriteMessage(MessageLevel.Verbose, $"Using instanceName '{instanceName}'");
                // PS: -eq is case-insensitive
                bool isDefaultInstance = string.Equals(instanceName, "MSSQLSERVER", StringComparison.OrdinalIgnoreCase);
                string tlsInstanceName = isDefaultInstance ? "" : instanceName;
                WriteMessage(MessageLevel.Verbose, $"Using tlsInstanceName '{tlsInstanceName}'");
                string sqlInstanceName = isDefaultInstance ? computerName : $"{computerName}\\{instanceName}";
                WriteMessage(MessageLevel.Verbose, $"Using sqlInstanceName '{sqlInstanceName}'");

                Hashtable splatTls = new Hashtable
                {
                    { "ComputerName", computerName },
                    { "InstanceName", tlsInstanceName },
                    { "ErrorAction", "Stop" }
                };

                if (instance.Port > 0)
                {
                    splatTls["ConnectionType"] = "TCP";
                    splatTls["Port"] = instance.Port;
                    WriteMessage(MessageLevel.Verbose, $"Using explicit port {instance.Port} for TLS connection");
                }

                // PS: try { $cert = Get-SqlServerTlsCertificate @splatTls } catch { Stop-Function
                //     "Failed to retrieve TLS certificate from $instance" -Continue } - the helper
                // is a PRIVATE function, so it runs in the dbatools MODULE scope (its own warnings
                // keep their [Get-SqlServerTlsCertificate] prefix and bubble to the caller).
                object? cert;
                try
                {
                    cert = ShapeOutput(InvokeTlsCertificateHelper(splatTls));
                }
                catch (PipelineStoppedException)
                {
                    throw;
                }
                catch (RuntimeException rex)
                {
                    StopFunction($"Failed to retrieve TLS certificate from {instance}", target: instance, errorRecord: rex.ErrorRecord, continueLoop: true);
                    continue;
                }

                // PS: if (-not $cert) { continue }
                if (!LanguagePrimitives.IsTrue(cert))
                {
                    continue;
                }

                PSObject output = new();
                output.Properties.Add(new PSNoteProperty("ComputerName", computerName));
                output.Properties.Add(new PSNoteProperty("InstanceName", instanceName));
                output.Properties.Add(new PSNoteProperty("SqlInstance", sqlInstanceName));
                output.Properties.Add(new PSNoteProperty("Subject", GetMemberValue(cert, "Subject")));
                output.Properties.Add(new PSNoteProperty("Issuer", GetMemberValue(cert, "Issuer")));
                output.Properties.Add(new PSNoteProperty("Thumbprint", GetMemberValue(cert, "Thumbprint")));
                output.Properties.Add(new PSNoteProperty("NotBefore", GetMemberValue(cert, "NotBefore")));
                output.Properties.Add(new PSNoteProperty("Expires", GetMemberValue(cert, "NotAfter")));
                output.Properties.Add(new PSNoteProperty("DnsNameList", GetMemberValue(cert, "DnsNameList")));
                output.Properties.Add(new PSNoteProperty("SerialNumber", GetMemberValue(cert, "SerialNumber")));
                output.Properties.Add(new PSNoteProperty("Certificate", cert));
                WriteObject(output);
            }
            catch (PipelineStoppedException)
            {
                throw;
            }
            catch (RuntimeException rex)
            {
                StopFunction($"Failed to retrieve certificate from {instance}", target: instance, errorRecord: rex.ErrorRecord, continueLoop: true);
                continue;
            }
            catch (Exception ex)
            {
                StopFunction($"Failed to retrieve certificate from {instance}", target: instance, exception: ex, continueLoop: true);
                continue;
            }
        }
    }

    // Module-scoped invocation of the PRIVATE Get-SqlServerTlsCertificate helper - the same
    // discipline as NestedCommand.Invoke (PSDPV shield, 3>&1 warning re-emit) but the call runs
    // inside the dbatools module session state where private functions resolve, exactly like the
    // retired function's own call did. The command name is a FIXED LITERAL.
    private Collection<PSObject> InvokeTlsCertificateHelper(IDictionary parameters)
    {
        object? effective = SessionState.PSVariable.GetValue("PSDefaultParameterValues");
        // Module-internal calls resolve $PSDefaultParameterValues from the MODULE session state,
        // where none is defined - neither caller-LOCAL nor GLOBAL defaults ever reached the retired
        // functions' nested calls, so the faithful shield is an EMPTY table, not the global one.
        bool swapped = effective is not null;
        if (swapped)
        {
            SessionState.PSVariable.Set("PSDefaultParameterValues", new System.Management.Automation.DefaultParameterDictionary());
        }
        try
        {
            // Under Invoke-ManualPester the shared engine dbatools.dll gets registered as a
            // SECOND module also named 'dbatools' (Binary), and `& (Get-Module dbatools)` then
            // either splats the array ("the term 'dbatools dbatools' is not recognized") or
            // refuses the binary instance ("Cannot use '&' to invoke in the context of binary
            // module"). Private functions live in the SCRIPT module - select it explicitly
            // (live-diagnosed through an instrumented IMP run in the W5-017 gate).
            ScriptBlock script = ScriptBlock.Create(
                "param($__parameters) " +
                "$__module = Get-Module dbatools | Where-Object ModuleType -eq \"Script\" | Select-Object -First 1; " +
                "& $__module { param($p) Get-SqlServerTlsCertificate @p } $__parameters 3>&1");
            Collection<PSObject> raw = InvokeCommand.InvokeScript(false, script, null, parameters);
            Collection<PSObject> output = new();
            foreach (PSObject item in raw)
            {
                if (item?.BaseObject is WarningRecord warning)
                {
                    WriteWarning(warning.Message);
                }
                else
                {
                    output.Add(item!);
                }
            }
            return output;
        }
        finally
        {
            if (swapped)
            {
                SessionState.PSVariable.Set("PSDefaultParameterValues", effective);
            }
        }
    }

    // PS pipeline-assignment shape: empty -> null, one -> the scalar, many -> object[].
    private static object? ShapeOutput(Collection<PSObject> output)
    {
        if (output.Count == 0)
        {
            return null;
        }
        if (output.Count == 1)
        {
            return output[0];
        }
        List<PSObject> many = new(output);
        return many.ToArray();
    }

    // PS property read over a pipeline-shaped value via the PSObject view.
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
}
