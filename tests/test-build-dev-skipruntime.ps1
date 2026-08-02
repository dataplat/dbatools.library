<#
.SYNOPSIS
    Covers build-dev.ps1's -SkipRuntime switch, its mutual exclusion with -SkipSatellites, and the
    DLL-lock preflight added in 7b3fe811 (#849).

.DESCRIPTION
    The preflight exists because the staged base dbatools.dll cannot be replaced while any live
    process has it loaded, and long-lived pwsh 7 windows import the core drop straight out of
    artifacts. That failure used to surface as a bare Copy-Item error AFTER a multi-minute build and
    was misread as a library-edit-lease conflict, which it never was.

    The default legs are hermetic and fast: they run the real script only along paths that exit
    before any dotnet invocation, and they exercise the real lock-check functions - lifted out of
    the shipped file by AST, never re-implemented here - against genuinely locked and genuinely free
    files. A check that can only answer "locked" is as broken as one that can only answer "free", so
    every lock leg asserts BOTH directions.

    -IncludeBuild adds the end-to-end leg that actually runs -SkipRuntime and proves the staged base
    DLL is untouched. It is opt-in because it compiles every satellite and writes into artifacts:
    TAKE THE LIBRARY EDIT LEASE FIRST, or it races a gate and both results become meaningless.

.EXAMPLE
    PS C:\> pwsh -NoProfile -File tests/test-build-dev-skipruntime.ps1

    Runs the hermetic legs. Exit 0 = green.

.EXAMPLE
    PS C:\> pwsh -NoProfile -File tests/test-build-dev-skipruntime.ps1 -IncludeBuild

    Adds the end-to-end -SkipRuntime build. Hold the library edit lease when you use this.

.OUTPUTS
    None. Writes leg results to the host; exit code 0 = all legs green.

.NOTES
    Author: the dbatools team + Claude
#>
[CmdletBinding()]
param(
    [string]$ScriptPath = (Join-Path $PSScriptRoot "../build/build-dev.ps1"),
    [switch]$IncludeBuild
)

$ErrorActionPreference = "Stop"
$pass = 0
$fail = 0

function Write-Leg {
    param(
        [Parameter(Mandatory)]
        [bool]$Ok,
        [Parameter(Mandatory)]
        [string]$Message
    )
    if ($Ok) {
        Write-Host "ok   $Message"
        $script:pass++
    } else {
        Write-Host "FAIL $Message"
        $script:fail++
    }
}

$resolved = (Resolve-Path -Path $ScriptPath).Path
$errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($resolved, [ref]$null, [ref]$errors)
if ($errors) {
    Write-Leg -Ok $false -Message "parse: $($errors[0].Message)"
    exit 1
}

# 1. Mutual exclusion. Runs the REAL script; this path exits before any dotnet invocation, so the
#    leg stays hermetic. Asserting "no build happened" matters as much as the exit code - the guard
#    is worthless if it reports the conflict only after spending minutes compiling.
$conflictErrPath = Join-Path ([System.IO.Path]::GetTempPath()) "build-dev-conflict.err"
$splatConflict = @{
    FilePath               = "pwsh"
    ArgumentList           = @("-NoProfile", "-File", $resolved, "-SkipRuntime", "-SkipSatellites")
    Wait                   = $true
    PassThru               = $true
    NoNewWindow            = $true
    RedirectStandardOutput = (Join-Path ([System.IO.Path]::GetTempPath()) "build-dev-conflict.out")
    RedirectStandardError  = $conflictErrPath
}
$conflict = Start-Process @splatConflict
# BOTH streams: a binding failure lands on stderr, so a stdout-only read would let the leg below
# pass against a script that has no -SkipRuntime parameter at all.
$conflictOut = (Get-Content -Path $splatConflict.RedirectStandardOutput -Raw -ErrorAction SilentlyContinue) +
    (Get-Content -Path $conflictErrPath -Raw -ErrorAction SilentlyContinue)
# The exit code alone proves nothing here - THREE different causes all exit 1: the guard, a binding
# failure against a script with no -SkipRuntime parameter, and the base-drop precondition. The
# negative control passed this leg on the wrong one of those before the message was folded in, so
# the message IS the assertion and the exit code is only corroborating.
$namedConflict = $conflictOut -match "nothing to build"
Write-Leg -Ok ($conflict.ExitCode -eq 1 -and $namedConflict) -Message "-SkipRuntime with -SkipSatellites exits 1 naming the conflict (got $($conflict.ExitCode))"
Write-Leg -Ok ($conflictOut -notmatch "base drop missing|A parameter cannot be found|ParameterBindingException") -Message "it failed on the guard, not on a precondition or a binding error"
Write-Leg -Ok ($conflictOut -notmatch "Building runtime|Building satellite") -Message "the conflict is caught BEFORE any dotnet build runs"
Remove-Item -Path $splatConflict.RedirectStandardOutput -ErrorAction SilentlyContinue
Remove-Item -Path $conflictErrPath -ErrorAction SilentlyContinue

# 2. The lock check, both directions, against a real lock. The functions are lifted from the
#    shipped script - if someone deletes or weakens them there, this test stops finding them.
foreach ($name in @("Test-StagedDllWritable", "Get-StagedDllHolder")) {
    $found = $ast.FindAll({
            param($node)
            $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name
        }.GetNewClosure(), $true)
    if (-not $found) {
        Write-Leg -Ok $false -Message "$name is gone from build-dev.ps1 - the preflight cannot work"
        Write-Host ""
        Write-Host "$pass passed, $fail failed"
        exit 1
    }
    . ([scriptblock]::Create($found[0].Extent.Text))
}

$probe = Join-Path ([System.IO.Path]::GetTempPath()) "build-dev-lock-probe.bin"
Set-Content -Path $probe -Value "probe" -Encoding Ascii
try {
    Write-Leg -Ok (Test-StagedDllWritable -Path $probe) -Message "an unlocked file reads as writable - the check can say YES"

    $held = [System.IO.File]::Open($probe, "Open", "ReadWrite", "None")
    try {
        Write-Leg -Ok (-not (Test-StagedDllWritable -Path $probe)) -Message "a locked file reads as NOT writable - the check can say NO"

        # A plain file lock is deliberately NOT attributable: Get-StagedDllHolder enumerates loaded
        # ASSEMBLIES, so it finds the case the preflight is really about (a process that imported
        # the drop) and correctly finds nothing here. That asymmetry is why the shipped code prints
        # a distinct "no holder could be attributed" line instead of claiming the file is free.
        $holders = @(Get-StagedDllHolder -Path $probe)
        Write-Leg -Ok ($holders.Count -eq 0) -Message "a non-assembly lock yields no attributable holder, and does not throw"
    } finally {
        $held.Close()
    }

    Write-Leg -Ok (Test-StagedDllWritable -Path $probe) -Message "the same file reads writable again once released - not a latched NO"
} finally {
    Remove-Item -Path $probe -ErrorAction SilentlyContinue
}

# 3. Ordering. The preflight must run before the build, or it saves nothing - the whole point of
#    #849 was failing in seconds instead of after minutes of compilation.
$scriptText = Get-Content -Path $resolved -Raw
$preflightAt = $scriptText.IndexOf("cannot stage")
$pushAt = $scriptText.IndexOf("Push-Location")
$splatOrder = @{
    Ok      = ($preflightAt -gt 0 -and $pushAt -gt 0 -and $preflightAt -lt $pushAt)
    Message = "the lock preflight is positioned before the build block"
}
Write-Leg @splatOrder

# 4. -SkipRuntime must actually skip. Structural, because the behavioural proof is leg 5.
$runtimeSkip = $ast.FindAll({
        param($node)
        $node -is [System.Management.Automation.Language.IfStatementAst] -and
        $node.Clauses[0].Item1.Extent.Text -match "SkipRuntime" -and
        $node.Clauses[0].Item2.Extent.Text -match "continue"
    }, $true)
Write-Leg -Ok ([bool]$runtimeSkip) -Message "-SkipRuntime short-circuits the runtime staging loop"

$preflightGuard = $ast.FindAll({
        param($node)
        $node -is [System.Management.Automation.Language.IfStatementAst] -and
        $node.Clauses[0].Item1.Extent.Text -match "not \`$SkipRuntime" -and
        $node.Extent.Text -match "cannot stage"
    }, $true)
Write-Leg -Ok ([bool]$preflightGuard) -Message "-SkipRuntime also skips the preflight, so a held DLL cannot block satellite-only work"

# 5. NEGATIVE CONTROL. Everything above is unfalsifiable until a script WITHOUT the guard turns a
#    leg red. Two ways this control can be void, both seen while writing it and both guarded here:
#    the pre-change script from git exits 1 on its own base-drop precondition when run from a temp
#    directory (the exit code matched for the wrong reason), and when placed correctly it runs a
#    real multi-minute dotnet build. So the control is the CURRENT script with only the
#    mutual-exclusion block removed, living beside the original so its path assumptions hold. With
#    the guard gone, both switches are accepted and both loops skip, so nothing is compiled.
$controlPath = Join-Path -Path (Split-Path -Path $resolved) -ChildPath "build-dev.control.tmp.ps1"
try {
    $controlText = $scriptText -replace "(?ms)if \(\`$SkipRuntime -and \`$SkipSatellites\) \{.*?\r?\n\}\r?\n", ""
    if ($controlText -eq $scriptText) {
        Write-Leg -Ok $false -Message "control: could not strip the mutual-exclusion block - THE CONTROL IS VOID, do not trust the legs above"
    } else {
        Set-Content -Path $controlPath -Value $controlText -Encoding UTF8
        $controlErr = Join-Path ([System.IO.Path]::GetTempPath()) "build-dev-control.err"
        $splatControl = @{
            FilePath               = "pwsh"
            ArgumentList           = @("-NoProfile", "-File", $controlPath, "-SkipRuntime", "-SkipSatellites")
            Wait                   = $true
            PassThru               = $true
            NoNewWindow            = $true
            RedirectStandardOutput = (Join-Path ([System.IO.Path]::GetTempPath()) "build-dev-control.out")
            RedirectStandardError  = $controlErr
        }
        $control = Start-Process @splatControl
        $controlOut = (Get-Content -Path $splatControl.RedirectStandardOutput -Raw -ErrorAction SilentlyContinue) +
            (Get-Content -Path $controlErr -Raw -ErrorAction SilentlyContinue)
        Remove-Item -Path $splatControl.RedirectStandardOutput, $controlErr -ErrorAction SilentlyContinue

        if ($controlOut -match "base drop missing|A parameter cannot be found|ParameterBindingException") {
            Write-Leg -Ok $false -Message "control: died on a precondition, not on the missing guard - THE CONTROL IS VOID"
        } elseif ($controlOut -match "nothing to build") {
            Write-Leg -Ok $false -Message "control: the stripped script STILL reported the conflict - leg 1 cannot fail, do not trust it"
        } else {
            Write-Leg -Ok $true -Message "control: without the guard the conflict goes unreported (exit $($control.ExitCode)) - leg 1 detects the regression"
        }
    }
} finally {
    Remove-Item -Path $controlPath -ErrorAction SilentlyContinue
}

# 6. End-to-end, opt-in. Proves the staged base is byte-identical across a -SkipRuntime run.
if ($IncludeBuild) {
    $root = Split-Path -Path (Split-Path -Path $resolved)
    $staged = Join-Path -Path $root -ChildPath "artifacts/dbatools.library/core/lib/dbatools.dll"
    $before = (Get-FileHash -Path $staged -Algorithm SHA256).Hash
    $splatBuild = @{
        FilePath     = "pwsh"
        ArgumentList = @("-NoProfile", "-File", $resolved, "-SkipRuntime")
        Wait         = $true
        PassThru     = $true
        NoNewWindow  = $true
    }
    $build = Start-Process @splatBuild
    $after = (Get-FileHash -Path $staged -Algorithm SHA256).Hash
    Write-Leg -Ok ($build.ExitCode -eq 0) -Message "-SkipRuntime completes (exit $($build.ExitCode))"
    Write-Leg -Ok ($before -eq $after) -Message "the staged base dbatools.dll is untouched by a -SkipRuntime run"
} else {
    Write-Host "skip end-to-end -SkipRuntime build (pass -IncludeBuild, holding the library edit lease)"
}

Write-Host ""
Write-Host "$pass passed, $fail failed"
if ($fail -gt 0) { exit 1 }
exit 0
