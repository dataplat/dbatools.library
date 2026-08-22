<#
.SYNOPSIS
    Reproduces #1447: byte-skew alone cannot distinguish stale staging from unchanged source.
#>
[CmdletBinding()]
param(
    [string]$ScriptPath = (Join-Path $PSScriptRoot "../build/build-dev.ps1")
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "BuildDevTestSupport.ps1")

$resolved = (Resolve-Path $ScriptPath).Path
$originalPath = $env:PATH
$sandbox = New-BuildDevSandbox -ScriptSource $resolved
try {
    # The source tree in this sandbox is deliberately untouched.  Only the output differs, which
    # models an interrupted full build whose copy to the staged base was blocked by a loaded DLL.
    Set-Content -LiteralPath $sandbox.BuiltRuntime -Value "same-source newer compiler output" -Encoding Ascii -NoNewline
    $result = Invoke-BuildDev -Script $sandbox.Script -Switches @("-SkipRuntime") -WorkDir $sandbox.RunDir
    $refused = $result.ExitCode -eq 1 -and $result.Output -match "DIFFERS from the staged base"
    Write-Host "SOURCE_TREE_CHANGED=False"
    Write-Host "BUILT_AND_STAGED_BYTES_DIFFER=True"
    Write-Host "SKIPRUNTIME_REFUSED=$refused"
    if (-not $refused) { throw "Expected the current byte-only guard to refuse unchanged source." }
    Write-Host "HEAD_REPRODUCTION=CONFIRMED"
} finally {
    $env:PATH = $originalPath
    Remove-Item -LiteralPath $sandbox.RunDir -Recurse -Force -ErrorAction SilentlyContinue
}
