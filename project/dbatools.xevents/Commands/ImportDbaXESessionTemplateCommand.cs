#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Imports Extended Events session templates (bundled or from files) onto SQL Server instances, optionally
/// rewriting the event_file target path.
/// </summary>
/// <remarks>
/// The instance connection, the template resolution, the XML target-path rewriting, the
/// CreateSessionFromTemplate call, and the resulting Get-DbaXESession all run the original dbatools
/// PowerShell body VERBATIM inside the dbatools module scope rather than being reimplemented in C#, so the
/// engine decides the observable details.
///
/// The script has begin and process blocks, and its per-record process locals persist on the function
/// scope across pipeline records (the source retains stale $server / $xml / $basename / $tempfile /
/// $TargetFilePath / $TargetFileMetadataPath / $Name when an assignment is skipped by a Stop-Function
/// -Continue). To reproduce that faithfully the port collects each record's (SqlInstance, Path) pair into
/// _batches across ProcessRecord and, in EndProcessing, runs ONE hop that executes the begin body (the
/// $metadata Import-Clixml) and then the process body once per collected batch in a SINGLE persistent scope
/// - so every one of those locals carries record to record exactly as the function scope does. An empty
/// pipeline collects no batches, so the process body never runs (begin still does), matching the script.
///
/// Substitutions only: -FunctionName on every direct Stop-Function/Write-Message, and every Test-Bound ->
/// the carried immutable flag ($__pathBound, $__templateBound, $__nameBound, $__targetFilePathBound,
/// $__targetFileMetadataPathBound; Test-Bound never rides the hop). $script:PSModuleRoot resolves in the
/// module scope. No process block reads Test-FunctionInterrupt, so no interrupt guard is needed. Each
/// created session emits before a later file or instance may fail under -EnableException, so the hop uses
/// InvokeScopedStreaming. Surface pinned by migration/baselines/Import-DbaXESessionTemplate.json.
/// </remarks>
[Cmdlet(VerbsData.Import, "DbaXESessionTemplate")]
public sealed class ImportDbaXESessionTemplateCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    [PsDbaInstanceArrayCast]
    public DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The name for the imported session.</summary>
    [Parameter(Position = 2)]
    public string? Name { get; set; }

    /// <summary>The path(s) to template file(s) to import.</summary>
    [Parameter(ValueFromPipelineByPropertyName = true, Position = 3)]
    [Alias("FullName")]
    public string[]? Path { get; set; }

    /// <summary>The bundled template name(s) to import.</summary>
    [Parameter(Position = 4)]
    public string[]? Template { get; set; }

    /// <summary>Rewrite the event_file target to this path.</summary>
    [Parameter(Position = 5)]
    public string? TargetFilePath { get; set; }

    /// <summary>Rewrite the event_file metadata target to this path.</summary>
    [Parameter(Position = 6)]
    public string? TargetFileMetadataPath { get; set; }

    /// <summary>Whether the imported session should start automatically.</summary>
    [Parameter(Position = 7)]
    [ValidateSet("On", "Off")]
    public string StartUpState { get; set; } = "Off";

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // One batch per ProcessRecord: the (SqlInstance, Path) pipeline bindings that record saw. The begin body
    // and every process record then run in a single EndProcessing hop so the source's function-scope process
    // locals persist across records.
    private readonly List<object?[]> _batches = new List<object?[]>();

    protected override void ProcessRecord()
    {
        if (Interrupted)
        {
            return;
        }

        _batches.Add(new object?[] { SqlInstance, Path });
    }

    protected override void EndProcessing()
    {
        if (Interrupted)
        {
            return;
        }

        NestedCommand.InvokeScopedStreaming(this, item =>
        {
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                RemoveHopErrorBookkeeping(nestedError);
                WriteError(nestedError);
            }
            else
            {
                WriteObject(item);
            }
        }, ProcessScript,
            _batches.ToArray(), SqlCredential, Name, Template, TargetFilePath, TargetFileMetadataPath,
            StartUpState, EnableException.ToBool(),
            TestBound(nameof(Path)), TestBound(nameof(Template)), TestBound(nameof(Name)),
            TestBound(nameof(TargetFilePath)), TestBound(nameof(TargetFileMetadataPath)),
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug"));
    }

    private object? BoundCommonParameter(string name)
    {
        if (MyInvocation.BoundParameters.TryGetValue(name, out object? value))
        {
            return LanguagePrimitives.IsTrue(value);
        }
        return null;
    }

    private void RemoveHopErrorBookkeeping(ErrorRecord record)
    {
        try
        {
            if (SessionState.PSVariable.GetValue("Error") is not ArrayList errorList || errorList.Count == 0)
            {
                return;
            }
            if (errorList[0] is not ErrorRecord first)
            {
                return;
            }
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

    // PS: the begin body then the process body (dot-sourced, once per collected batch) VERBATIM in ONE scope,
    // so the process locals persist record to record like the function scope. Substitutions: -FunctionName on
    // every direct Stop-Function/Write-Message, and every Test-Bound -> the carried immutable flag.
    private const string ProcessScript = """
param($__batches, $SqlCredential, $Name, $Template, $TargetFilePath, $TargetFileMetadataPath, $StartUpState, $EnableException, $__pathBound, $__templateBound, $__nameBound, $__targetFilePathBound, $__targetFileMetadataPathBound, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param($__batches, $SqlCredential, [string]$Name, [string[]]$Template, [string]$TargetFilePath, [string]$TargetFileMetadataPath, [string]$StartUpState, $EnableException, $__pathBound, $__templateBound, $__nameBound, $__targetFilePathBound, $__targetFileMetadataPathBound)
    $xmlpath = Join-DbaPath $script:PSModuleRoot "bin" "xetemplates-metadata.xml"
    $metadata = Import-Clixml $xmlpath

    foreach ($__batch in $__batches) {
        $SqlInstance = $__batch[0]
        $Path = $__batch[1]
        . {
        if ((-not $__pathBound) -and (-not $__templateBound)) {
            Stop-Function -Message "You must specify Path or Template." -FunctionName Import-DbaXESessionTemplate
        }

        if (($Path.Count -gt 1 -or $Template.Count -gt 1) -and ($__nameBound)) {
            Stop-Function -Message "Name cannot be specified with multiple files or templates because the Session will already exist." -FunctionName Import-DbaXESessionTemplate
            return
        }

        foreach ($instance in $SqlInstance) {
            try {
                $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential -MinimumVersion 11
            } catch {
                Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Import-DbaXESessionTemplate
            }

            $SqlConn = $server.ConnectionContext.SqlConnectionObject
            $SqlStoreConnection = New-Object Microsoft.SqlServer.Management.Sdk.Sfc.SqlStoreConnection $SqlConn
            $store = New-Object Microsoft.SqlServer.Management.XEvent.XEStore $SqlStoreConnection

            foreach ($file in $template) {
                $templatepath = Join-DbaPath $script:PSModuleRoot "bin" "XEtemplates" "$file.xml"
                if ((Test-Path $templatepath)) {
                    $Path += $templatepath
                } else {
                    Stop-Function -Message "Invalid template ($templatepath does not exist)." -Continue -FunctionName Import-DbaXESessionTemplate
                }
            }

            foreach ($file in $Path) {

                if (-not $__targetFilePathBound) {
                    Write-Message -Level Verbose -Message "Importing $file to $instance" -FunctionName Import-DbaXESessionTemplate
                    try {
                        $xml = [xml](Get-Content $file -ErrorAction Stop)
                    } catch {
                        Stop-Function -Message "Failure" -ErrorRecord $_ -Target $file -Continue -FunctionName Import-DbaXESessionTemplate
                    }
                } else {
                    Write-Message -Level Verbose -Message "TargetFilePath specified, changing all file locations in $file for $instance." -FunctionName Import-DbaXESessionTemplate

                    # Handle whatever people specify
                    $TargetFilePath = $TargetFilePath.TrimEnd("\").TrimEnd("/")
                    if ($__targetFileMetadataPathBound) {
                        Write-Message -Level Verbose -Message "TargetFileMetadataPath specified, changing all metadata file locations in $file for $instance." -FunctionName Import-DbaXESessionTemplate
                        $TargetFileMetadataPath = $TargetFileMetadataPath.TrimEnd("\").TrimEnd("/")
                    }
                    if ((Test-HostOSLinux -SqlInstance $server)) {
                        $TargetFilePath = "$TargetFilePath/"
                        if ($__targetFileMetadataPathBound) {
                            $TargetFileMetadataPath = "$TargetFileMetadataPath/"
                        }
                    } else {
                        $TargetFilePath = "$TargetFilePath\"
                        if ($__targetFileMetadataPathBound) {
                            $TargetFileMetadataPath = "$TargetFileMetadataPath\"
                        }
                    }

                    try {
                        $basename = (Get-ChildItem $file).Basename
                        $templateXml = [xml](Get-Content $file -Raw -ErrorAction Stop)
                        $namespaceUri = $templateXml.DocumentElement.NamespaceURI
                        $eventSessionNode = $templateXml.SelectSingleNode("/*[local-name()='event_sessions']/*[local-name()='event_session']")
                        if (-not $eventSessionNode) {
                            throw "No event_session element found in template $file."
                        }

                        $eventFileTargetNode = $eventSessionNode.SelectSingleNode("*[local-name()='target' and @name='event_file']")
                        $eventFileTargetExists = $null -ne $eventFileTargetNode

                        if (-not $eventFileTargetExists) {
                            # No event_file target found in template - add one so TargetFilePath is honored
                            Write-Message -Level Verbose -Message "No event_file target found in template, adding one with TargetFilePath." -FunctionName Import-DbaXESessionTemplate
                            $eventFileTargetNode = $templateXml.CreateElement("target", $namespaceUri)
                            $null = $eventFileTargetNode.SetAttribute("package", "package0")
                            $null = $eventFileTargetNode.SetAttribute("name", "event_file")
                        }

                        $filenameParameterNode = $eventFileTargetNode.SelectSingleNode("*[local-name()='parameter' and @name='filename']")
                        if (-not $filenameParameterNode) {
                            $filenameParameterNode = $templateXml.CreateElement("parameter", $namespaceUri)
                            $null = $filenameParameterNode.SetAttribute("name", "filename")
                            $null = $eventFileTargetNode.AppendChild($filenameParameterNode)
                        }

                        $filenameValue = $filenameParameterNode.GetAttribute("value")
                        if ([string]::IsNullOrWhiteSpace($filenameValue)) {
                            $filenameValue = $basename
                        }
                        $null = $filenameParameterNode.SetAttribute("value", "$TargetFilePath$filenameValue")

                        if ($__targetFileMetadataPathBound) {
                            $metadataParameterNode = $eventFileTargetNode.SelectSingleNode("*[local-name()='parameter' and @name='metadatafile']")
                            if ($metadataParameterNode) {
                                $metadataValue = $metadataParameterNode.GetAttribute("value")
                                if ([string]::IsNullOrWhiteSpace($metadataValue)) {
                                    $metadataValue = $basename
                                }
                                $null = $metadataParameterNode.SetAttribute("value", "$TargetFileMetadataPath$metadataValue")
                            } elseif (-not $eventFileTargetExists) {
                                $metadataParameterNode = $templateXml.CreateElement("parameter", $namespaceUri)
                                $null = $metadataParameterNode.SetAttribute("name", "metadatafile")
                                $null = $metadataParameterNode.SetAttribute("value", "$TargetFileMetadataPath$basename")
                                $null = $eventFileTargetNode.AppendChild($metadataParameterNode)
                            }
                        }

                        if (-not $eventFileTargetExists) {
                            $null = $eventSessionNode.AppendChild($eventFileTargetNode)
                        }

                        $temp = ([System.IO.Path]::GetTempPath()).TrimEnd("").TrimEnd("\").TrimEnd("/")
                        $tempfile = Join-DbaPath $temp $basename
                        $null = Set-Content -Path $tempfile -Value $templateXml.OuterXml -Encoding UTF8
                        $xml = $templateXml
                        $file = $tempfile
                    } catch {
                        Stop-Function -Message "Failure" -ErrorRecord $_ -Target $file -Continue -FunctionName Import-DbaXESessionTemplate
                    }

                    Write-Message -Level Verbose -Message "$TargetFilePath does not exist on $server, creating now." -FunctionName Import-DbaXESessionTemplate
                    try {
                        if (-not (Test-DbaPath -SqlInstance $server -Path $TargetFilePath)) {
                            $null = New-DbaDirectory -SqlInstance $server -Path $TargetFilePath
                        }
                    } catch {
                        Stop-Function -Message "Failure" -ErrorRecord $_ -Target $file -Continue -FunctionName Import-DbaXESessionTemplate
                    }
                }

                if (-not $xml.event_sessions) {
                    Stop-Function -Message "$file is not a valid XESession template document." -Continue -FunctionName Import-DbaXESessionTemplate
                }

                if ((-not $__nameBound)) {
                    $Name = (Get-ChildItem $file).BaseName
                }

                # This could be done better but not today
                $no2012 = ($metadata | Where-Object Compatibility -gt 2012).Name
                $no2014 = ($metadata | Where-Object Compatibility -gt 2014).Name

                if ($Name -in $no2012 -and $server.VersionMajor -eq 11) {
                    Stop-Function -Message "$Name is not supported in SQL Server 2012 ($server)" -Continue -FunctionName Import-DbaXESessionTemplate
                }

                if ($Name -in $no2014 -and $server.VersionMajor -eq 12) {
                    Stop-Function -Message "$Name is not supported in SQL Server 2014 ($server)" -Continue -FunctionName Import-DbaXESessionTemplate
                }

                if ((Get-DbaXESession -SqlInstance $server -Session $Name)) {
                    Stop-Function -Message "$Name already exists on $instance" -Continue -FunctionName Import-DbaXESessionTemplate
                }

                try {
                    Write-Message -Level Verbose -Message "Importing $file as $Name" -FunctionName Import-DbaXESessionTemplate
                    $session = $store.CreateSessionFromTemplate($Name, $file)
                    $session.Create()
                    if ($file -eq $tempfile) {
                        Remove-Item $tempfile -ErrorAction SilentlyContinue
                    }
                    if ($StartUpState -eq "On") {
                        $newsession = Get-DbaXESession -SqlInstance $server -Session $session.Name
                        if (-not $newsession.AutoStart) {
                            $newsession.AutoStart = $true
                            $newsession.Alter()
                        }
                        $newsession | Start-DbaXESession
                    } else {
                        Get-DbaXESession -SqlInstance $server -Session $session.Name
                    }
                } catch {
                    Stop-Function -Message "Failure" -ErrorRecord $_ -Target $store -Continue -FunctionName Import-DbaXESessionTemplate
                }
            }
        }
        }
    }
} $__batches $SqlCredential $Name $Template $TargetFilePath $TargetFileMetadataPath $StartUpState $EnableException $__pathBound $__templateBound $__nameBound $__targetFilePathBound $__targetFileMetadataPathBound @__commonParameters 3>&1 2>&1
""";
}
