#nullable enable

namespace Dataplat.Dbatools.Commands;

// Hop script constants (the verbatim retired PS begin/process bodies) - split out per the repo 400-line file limit.
public sealed partial class GetDbaCmObjectCommand
{

    // PS: the begin block VERBATIM, dot-sourced. Edits: $PSCmdlet.ParameterSetName becomes the
    // carried $__parameterSetName (inside a hop $PSCmdlet is the hop's own cmdlet), plus -FunctionName
    // stamps. The sentinel carries $disable_cache and $ParSet. The Resolve-CimError definition rides
    // verbatim here (unused in begin) and is recreated in process.
    private const string BeginScript = """
param($EnableException, $__parameterSetName, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param($EnableException, $__parameterSetName, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    . {
        #region Configuration Values
        $disable_cache = [Dataplat.Dbatools.Connection.ConnectionHost]::DisableCache

        Write-Message -Level Verbose -Message "Configuration loaded | Cache disabled: $disable_cache" -FunctionName Get-DbaCmObject
        #endregion Configuration Values

        #region Utility Functions
        function Resolve-CimError {
            <#
                .SYNOPSIS
                    Utility function to resolve CIM error states and streamline error handling in code.

                .DESCRIPTION
                    Utility function to resolve CIM error states and streamline error handling in code.
                    This determines the specific error message to provide and whether the connection type is not viable.

                    CIM Error Code Reference: https://msdn.microsoft.com/en-us/library/cc150671(v=vs.85).aspx

                .PARAMETER ErrorRecord
                    The error that just happened.

                .PARAMETER ComputerName
                    The computer against which the query was executed.

                .PARAMETER ClassName
                    The name of the class queried.

                .PARAMETER Namespace
                    The namespace executed against.

                .PARAMETER Query
                    The query executed.
            #>
            [CmdletBinding()]
            param (
                [System.Management.Automation.ErrorRecord]
                $ErrorRecord,

                [string]
                $ComputerName,

                [AllowEmptyString()]
                [string]
                $ClassName,

                [AllowEmptyString()]
                [string]
                $Namespace,

                [AllowEmptyString()]
                [string]
                $Query
            )

            if ($Query) {
                $ClassName = $Query -replace '.+from (\S+).{0,}', '$1'
            }

            $messages = @{
                1  = "[$ComputerName] An otherwise unexpected error happened."
                2  = "[$ComputerName] Access to computer granted, but access to $Namespace\$ClassName denied"
                3  = "[$ComputerName] Invalid namespace: $Namespace"
                4  = "[$ComputerName] Invalid parameters were specified"
                5  = "[$ComputerName] Invalid class name ($ClassName), not found in current namespace ($Namespace)"
                6  = "[$ComputerName] The requested object of class $ClassName could not be found"
                7  = "[$ComputerName] The operation against class $ClassName was not supported. This generally is a serverside WMI Provider issue (That is: It is specific to the application being managed via WMI)"
                8  = "[$ComputerName] The operation against class $ClassName is refused as long as it contains instances (data)"
                9  = "[$ComputerName] The operation against class $ClassName is refused as long as it contains instances (data)"
                10 = "[$ComputerName] The operation against class $ClassName cannot be carried out since the specified superclass does not exist."
                11 = "[$ComputerName] The specified object in $ClassName already exists."
                12 = "[$ComputerName] The specified property does not exist on $ClassName."
                13 = "[$ComputerName] The input type is invalid."
                14 = "[$ComputerName] Invalid query language. Please check your query string."
                15 = "[$ComputerName] Invalid query string. Please check your syntax."
                16 = "[$ComputerName] The specified method on $ClassName is not available."
                17 = "[$ComputerName] The specified method on $ClassName does not exist."
                18 = "[$ComputerName] An unexpected response has happened in this request"
                19 = "[$ComputerName] The specified destination for this request is invalid."
                20 = "[$ComputerName] The specified namespace $Namespace is not empty."
            }

            $badConnection = $false
            $badCredentials = $false
            $code = $ErrorRecord.Exception.InnerException.StatusCode -as [int]
            $message = $messages[$code]

            #region 1 = Generic runtime error
            # This routinely happens with CIM/DCOM
            if (1 -eq $code) {
                switch ($ErrorRecord.Exception.InnerException.MessageId) {
                    'HRESULT 0x8007052e' {
                        $badCredentials = $true
                        $message = "[$ComputerName] Invalid connection credentials"
                    }
                    'HRESULT 0x80070005' {
                        $badCredentials = $true
                        $message = "[$ComputerName] Invalid connection credentials"
                    }
                    'HRESULT 0x80041013' {
                        $message = "[$ComputerName] Failed to access $ClassName in namespace $Namespace"
                    }
                    'HRESULT 0x8004100e' {
                        $message = "[$ComputerName] Invalid namespace: $Namespace"
                        $code = 3
                    }
                    'HRESULT 0x80041010' {
                        $message = "[$ComputerName] Invalid class name ($ClassName), not found in current namespace ($Namespace)"
                        $code = 5
                    }
                    default {
                        $badConnection = $true
                    }
                }
            }
            #endregion 1 = Generic runtime error

            #region 0 = Non-CIM Issue not covered by the framework
            $knownCodes = 1..20
            if ($code -notin $knownCodes) {
                if ($ErrorRecord.Exception.InnerException.ErrorData.original_error -like "__ExtendedStatus") {
                    $message = "[$ComputerName] Something went wrong when looking for $ClassName, in $Namespace. This often indicates issues with the target system."
                } else {
                    $badConnection = $true
                }
            }
            #endregion 0 = Non-CIM Issue not covered by the framework

            [PSCustomObject]@{
                ErrorCode      = $code
                Message        = $message
                BadConnection  = $badConnection
                BadCredentials = $badCredentials
                Error          = $ErrorRecord
            }
        }
        #endregion Utility Functions

        $ParSet = $__parameterSetName
    }

    $__dc = Get-Variable -Name disable_cache -Scope 0 -ErrorAction Ignore
    $__ps = Get-Variable -Name ParSet -Scope 0 -ErrorAction Ignore
    $__dcv = $null; if ($__dc) { $__dcv = $__dc.Value }
    $__psv = $null; if ($__ps) { $__psv = $__ps.Value }
    @{ __getDbaCmObjectBegin = @{ DisableCache = $__dcv; ParSet = $__psv } }
} $EnableException $__parameterSetName $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
