#nullable enable

using System;
using System.Collections;
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
/// The begin block reads the bundled template metadata from "$script:PSModuleRoot\bin\xetemplates-metadata.xml"
/// (a module-scope path, I/O that can throw terminating), a once-only computation whose $metadata result
/// rides a sentinel to the process records.
///
/// Every Test-Bound in the body is replaced by a carried immutable flag (Test-Bound never rides the hop):
/// $__pathBound, $__templateBound, $__nameBound, $__targetFilePathBound, $__targetFileMetadataPathBound.
/// The in-body $Path, $Name, $TargetFilePath, and $TargetFileMetadataPath mutations do not need cross-record
/// carry: $Path is ValueFromPipelineByPropertyName (rebound per record; its within-record accumulation
/// across instances is preserved because the whole process body runs in one hop per record); $Name is
/// recomputed per file only when unbound; and the $TargetFilePath/$TargetFileMetadataPath "TrimEnd then
/// append separator" mutation is idempotent, so a per-record reset from the bound value yields the same
/// value the source's persisted function-scope variable would. No process block reads Test-FunctionInterrupt,
/// so no interrupt is carried. Each created session (Get-DbaXESession) emits before a later file or instance
/// may fail under -EnableException, so the process hop uses InvokeScopedStreaming. Surface pinned by
/// migration/baselines/Import-DbaXESessionTemplate.json.
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

    // The bundled template metadata, read once in the begin hop and carried to every record.
    private object? _metadata;

    protected override void BeginProcessing()
    {
        foreach (PSObject? item in NestedCommand.InvokeScoped(this, BeginScript,
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
        {
            if (item?.BaseObject is Hashtable sentinel && sentinel.ContainsKey("__importDbaXESessionTemplateBegin"))
            {
                if (sentinel["__importDbaXESessionTemplateBegin"] is Hashtable state)
                {
                    _metadata = state["Metadata"];
                }
                continue;
            }
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                RemoveHopErrorBookkeeping(nestedError);
                WriteError(nestedError);
                continue;
            }
            WriteObject(item);
        }
    }

    protected override void ProcessRecord()
    {
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
            SqlInstance, SqlCredential, Name, Path, Template, TargetFilePath, TargetFileMetadataPath,
            StartUpState, _metadata, EnableException.ToBool(),
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

    // PS: the begin block VERBATIM ($script:PSModuleRoot resolves in the module scope), returning $metadata
    // via a sentinel. Runs once in BeginProcessing.
    private const string BeginScript = """
param($__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    $xmlpath = Join-DbaPath $script:PSModuleRoot "bin" "xetemplates-metadata.xml"
    $metadata = Import-Clixml $xmlpath
    @{ __importDbaXESessionTemplateBegin = @{ Metadata = $metadata } }
} @__commonParameters 3>&1 2>&1
""";

    // PS: the process block VERBATIM apart from -FunctionName Import-DbaXESessionTemplate on the direct
    // Stop-Function/Write-Message sites, every Test-Bound -> the carried immutable flag, and $metadata
    // supplied from the begin carry. EnableException is bound so Stop-Function's scope-walking default
    // inherits the caller's value.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Name, $Path, $Template, $TargetFilePath, $TargetFileMetadataPath, $StartUpState, $metadata, $EnableException, $__pathBound, $__templateBound, $__nameBound, $__targetFilePathBound, $__targetFileMetadataPathBound, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, $SqlCredential, [string]$Name, [string[]]$Path, [string[]]$Template, [string]$TargetFilePath, [string]$TargetFileMetadataPath, [string]$StartUpState, $metadata, $EnableException, $__pathBound, $__templateBound, $__nameBound, $__targetFilePathBound, $__targetFileMetadataPathBound)
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
} $SqlInstance $SqlCredential $Name $Path $Template $TargetFilePath $TargetFileMetadataPath $StartUpState $metadata $EnableException $__pathBound $__templateBound $__nameBound $__targetFilePathBound $__targetFileMetadataPathBound @__commonParameters 3>&1 2>&1
""";
}
