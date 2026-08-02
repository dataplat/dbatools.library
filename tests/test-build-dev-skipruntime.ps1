<#
.SYNOPSIS
    Covers build-dev.ps1's -SkipRuntime switch, its mutual exclusion with -SkipSatellites, and the
    DLL-lock preflight added in 7b3fe811 (#849).

.DESCRIPTION
    The preflight exists because the staged base dbatools.dll cannot be replaced while any live
    process has it loaded, and long-lived pwsh 7 windows import the core drop straight out of
    artifacts. That failure used to surface as a bare Copy-Item error AFTER a multi-minute build and
    was misread as a library-edit-lease conflict, which it never was.

    Everything runs against a disposable SANDBOX: a minimal tree holding a copy of the script and a
    dummy staged dbatools.dll. That is what makes the important leg possible - the real script is
    invoked with the staged DLL genuinely locked, and asserted to abort before any dotnet
    invocation - without locking the shared artifacts drop or compiling anything. The sandbox is a
    uniquely named directory removed in a finally, so concurrent runs on this shared box cannot
    collide or delete each other's files.

    Every lock assertion checks BOTH directions. A check that can only answer "locked" is as broken
    as one that can only answer "free".

    -IncludeBuild adds the end-to-end leg that runs -SkipRuntime against the REAL tree and proves
    the staged base DLL is untouched. It is opt-in because it compiles every satellite and writes
    into artifacts: TAKE THE LIBRARY EDIT LEASE FIRST, or it races a gate and both results become
    meaningless.

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

# Run the sandboxed script and return exit code plus BOTH streams merged. Both matter: the guard
# messages go to stdout, while binding failures and terminating errors go to stderr, and a
# stdout-only read would let legs pass against a script that never had the parameter at all.
function Invoke-BuildDev {
    param(
        [Parameter(Mandatory)]
        [string]$Script,
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [string[]]$Switches,
        [Parameter(Mandatory)]
        [string]$WorkDir
    )
    $outPath = Join-Path -Path $WorkDir -ChildPath "run.out"
    $errPath = Join-Path -Path $WorkDir -ChildPath "run.err"
    $splatRun = @{
        FilePath               = "pwsh"
        ArgumentList           = @("-NoProfile", "-File", $Script) + $Switches
        Wait                   = $true
        PassThru               = $true
        NoNewWindow            = $true
        RedirectStandardOutput = $outPath
        RedirectStandardError  = $errPath
    }
    $proc = Start-Process @splatRun
    $text = (Get-Content -Path $outPath -Raw -ErrorAction SilentlyContinue) +
        (Get-Content -Path $errPath -Raw -ErrorAction SilentlyContinue)
    Remove-Item -Path $outPath, $errPath -ErrorAction SilentlyContinue
    [PSCustomObject]@{
        ExitCode = $proc.ExitCode
        Output   = [string]$text
    }
}

$resolved = (Resolve-Path -Path $ScriptPath).Path
$errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($resolved, [ref]$null, [ref]$errors)
if ($errors) {
    Write-Leg -Ok $false -Message "parse: $($errors[0].Message)"
    exit 1
}
$scriptText = Get-Content -Path $resolved -Raw

# Unique per run - this box is shared by many windows and fixed names let concurrent runs overwrite
# and delete each other's files.
$runDir = Join-Path ([System.IO.Path]::GetTempPath()) ("build-dev-test-" + [System.IO.Path]::GetRandomFileName())
$null = New-Item -ItemType Directory -Path $runDir -Force
$originalPath = $env:PATH

try {
    # The sandbox only has to satisfy what the script inspects before it would build: a script
    # directory whose parent holds artifacts/dbatools.library/<edition>/lib/dbatools.dll. The DLL is
    # a dummy - the preflight opens the file, it never loads it as an assembly.
    $sandboxBuild = Join-Path -Path $runDir -ChildPath "build"
    $null = New-Item -ItemType Directory -Path $sandboxBuild -Force
    $stagedCore = Join-Path -Path $runDir -ChildPath "artifacts/dbatools.library/core/lib/dbatools.dll"
    $stagedDesktop = Join-Path -Path $runDir -ChildPath "artifacts/dbatools.library/desktop/lib/dbatools.dll"
    foreach ($staged in @($stagedCore, $stagedDesktop)) {
        $null = New-Item -ItemType Directory -Path (Split-Path -Path $staged) -Force
        Set-Content -Path $staged -Value "not a real assembly" -Encoding Ascii
    }
    $sandboxScript = Join-Path -Path $sandboxBuild -ChildPath "build-dev.ps1"
    Copy-Item -Path $resolved -Destination $sandboxScript -Force

    # A fake dotnet on PATH plus a minimal project tree. Without them the script cannot get past
    # Push-Location, so a -SkipRuntime leg could only assert "no lock error" - which passes just as
    # readily when the run dies one line later for an unrelated reason. With them the run completes
    # and the leg asserts what actually matters: satellites built, runtime not.
    $fakeBin = Join-Path -Path $runDir -ChildPath "fakebin"
    $null = New-Item -ItemType Directory -Path $fakeBin -Force
    Set-Content -Path (Join-Path -Path $fakeBin -ChildPath "dotnet.cmd") -Value "@echo off`r`necho   fake dotnet %*`r`nexit /b 0" -Encoding Ascii
    if (-not $IsWindows) {
        $shim = Join-Path -Path $fakeBin -ChildPath "dotnet"
        Set-Content -Path $shim -Value "#!/bin/sh`necho `"  fake dotnet `$@`"`nexit 0" -Encoding Ascii
        chmod +x $shim
    }
    $env:PATH = $fakeBin + [System.IO.Path]::PathSeparator + $env:PATH

    $sandboxProject = Join-Path -Path $runDir -ChildPath "project"
    foreach ($proj in @("dbatools", "dbatools.fake")) {
        $projDir = Join-Path -Path $sandboxProject -ChildPath $proj
        $null = New-Item -ItemType Directory -Path $projDir -Force
        Set-Content -Path (Join-Path -Path $projDir -ChildPath "$proj.csproj") -Value "<Project />" -Encoding Ascii
    }
    # The script verifies each build's output exists before staging it, so a fake compiler that
    # writes nothing needs these seeded in the exact locations the real projects emit to.
    $builtRuntime = Join-Path -Path $runDir -ChildPath "artifacts/lib/Release/net8.0/dbatools.dll"
    $builtSatellite = Join-Path -Path $sandboxProject -ChildPath "dbatools.fake/bin/Release/net8.0/dbatools.fake.dll"
    foreach ($built in @($builtRuntime, $builtSatellite)) {
        $null = New-Item -ItemType Directory -Path (Split-Path -Path $built) -Force
        Set-Content -Path $built -Value "built by the fake dotnet" -Encoding Ascii
    }
    $stagedSatellite = Join-Path -Path $runDir -ChildPath "artifacts/modules/dbatools.fake/core/dbatools.fake.dll"

    # 1. Mutual exclusion. This path exits before any dotnet invocation, so it stays hermetic.
    #    The exit code alone proves nothing - the guard, a binding failure against a script with no
    #    -SkipRuntime parameter, and the base-drop precondition ALL exit 1, and an earlier control
    #    passed this leg on the wrong one of those. The message is the assertion.
    $splatConflict = @{
        Script   = $sandboxScript
        Switches = @("-SkipRuntime", "-SkipSatellites")
        WorkDir  = $runDir
    }
    $conflict = Invoke-BuildDev @splatConflict
    Write-Leg -Ok ($conflict.ExitCode -eq 1 -and $conflict.Output -match "nothing to build") -Message "-SkipRuntime with -SkipSatellites exits 1 naming the conflict (got $($conflict.ExitCode))"
    Write-Leg -Ok ($conflict.Output -notmatch "base drop missing|A parameter cannot be found|ParameterBindingException") -Message "it failed on the guard, not on a precondition or a binding error"
    Write-Leg -Ok ($conflict.Output -notmatch "Building runtime|Building satellite") -Message "the conflict is caught BEFORE any dotnet build runs"

    # 2. THE BEHAVIOURAL LEG. The real script, a genuinely locked staged DLL, and the assertion that
    #    matters: it aborts before dotnet. Structural checks cannot establish this.
    $lock = [System.IO.File]::Open($stagedCore, "Open", "ReadWrite", "None")
    try {
        $splatLocked = @{
            Script   = $sandboxScript
            Switches = @()
            WorkDir  = $runDir
        }
        $locked = Invoke-BuildDev @splatLocked
        Write-Leg -Ok ($locked.ExitCode -eq 1) -Message "a locked staged DLL aborts the run (exit $($locked.ExitCode))"
        Write-Leg -Ok ($locked.Output -match "cannot stage") -Message "the abort names the lock rather than failing obscurely"
        Write-Leg -Ok ($locked.Output -notmatch "Building runtime|Building satellite") -Message "it aborts BEFORE dotnet build - the whole point of the preflight"
        Write-Leg -Ok ($locked.Output -match "NOT the library edit lease") -Message "the message rules out the lease, which is what #849 was misdiagnosed as"
    } finally {
        $lock.Close()
    }

    # 3. The other direction, at the same level: unlocked, the preflight must NOT block - and the run
    #    must reach BOTH build paths. That second half is what gives the -SkipRuntime leg below any
    #    meaning: in a sandbox that could never build a runtime, "no runtime build ran" proves
    #    nothing at all.
    $splatFree = @{
        Script   = $sandboxScript
        Switches = @()
        WorkDir  = $runDir
    }
    $free = Invoke-BuildDev @splatFree
    Write-Leg -Ok ($free.Output -notmatch "cannot stage") -Message "with the DLL released the preflight lets the run proceed - not a blanket refusal"
    Write-Leg -Ok ($free.ExitCode -eq 0) -Message "a full default run completes in the sandbox (exit $($free.ExitCode))"
    Write-Leg -Ok ($free.Output -match "Building runtime dbatools.dll") -Message "the default run DOES build the runtime - the positive control for -SkipRuntime"
    Write-Leg -Ok ($free.Output -match "Staged satellite: dbatools.fake") -Message "the default run also builds and stages satellites"

    # 4. The lock helpers themselves, lifted from the shipped script - never re-implemented here, so
    #    deleting or weakening them there makes this stop finding them.
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

    $probe = Join-Path -Path $runDir -ChildPath "lock-probe.bin"
    Set-Content -Path $probe -Value "probe" -Encoding Ascii
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

    # Holder enumeration must still name a process whose StartTime is unreadable - one that exited
    # mid-scan, or that this session cannot open. That is the case the diagnostic exists for, so
    # losing the row (or printing a blank time) defeats it.
    #
    # Injected, never observed: sweeping the real process table only exercises this if a holder
    # happens to be dying at that instant, so it would pass whether or not the bug was present -
    # which is the same as not testing it. A local function outranks a cmdlet in PowerShell's
    # command precedence, so this is what the dot-sourced Get-StagedDllHolder resolves.
    # A compiled type whose StartTime getter throws. PowerShell swallows a failing property getter
    # and hands back $null - verified on pwsh 7 and 5.1, even under $ErrorActionPreference = "Stop"
    # - so this reproduces an inaccessible real process exactly: the sweep sees $null, not an
    # exception. That $null is what the fallback has to convert into a usable "unknown".
    if (-not ("FakeHolderProcess" -as [type])) {
        Add-Type -TypeDefinition @"
public class FakeHolderProcess {
    public int Id { get; set; }
    public string ProcessName { get; set; }
    public object[] Modules { get; set; }
    public System.DateTime StartTime {
        get { throw new System.ComponentModel.Win32Exception(5, "Access is denied"); }
    }
}
"@
    }
    function Get-Process {
        param(
            [Parameter(ValueFromRemainingArguments)]
            $Rest
        )
        $fake = New-Object -TypeName FakeHolderProcess
        $fake.Id = -1
        $fake.ProcessName = "fake-holder"
        $fake.Modules = @([PSCustomObject]@{ FileName = $stagedCore })
        , @($fake)
    }

    $injected = @(Get-StagedDllHolder -Path $stagedCore)
    Write-Leg -Ok ($injected.Count -eq 1) -Message "a holder with an unreadable StartTime is still reported, not dropped (got $($injected.Count))"
    Write-Leg -Ok ($injected.Count -eq 1 -and $injected[0].Started -eq "unknown") -Message "its start time reads `"unknown`" rather than printing blank"
    Write-Leg -Ok ($injected.Count -eq 1 -and $injected[0].Id -eq -1 -and $injected[0].Name -eq "fake-holder") -Message "the pid and name - the whole point of the diagnostic - survive"

    # And the control: the same injection against the pre-fix shape, which assigned StartTime
    # unconditionally. It must yield a BLANK start time - if it still says "unknown", the fallback is
    # not what produces that and the leg above cannot fail.
    $holderText = ($ast.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq "Get-StagedDllHolder"
            }, $true))[0].Extent.Text
    $unguarded = $holderText -replace "(?ms)if \(\`$proc\.StartTime\) \{\r?\n\s*(\`$started = \`$proc\.StartTime)\r?\n\s*\}", '$1'
    $unguarded = $unguarded -replace "function Get-StagedDllHolder", "function Get-StagedDllHolderUnguarded"
    if ($unguarded -eq $holderText -or $unguarded -notmatch "Get-StagedDllHolderUnguarded") {
        Write-Leg -Ok $false -Message "control: could not reconstruct the pre-fix holder sweep - THE CONTROL IS VOID, do not trust the legs above"
    } else {
        . ([scriptblock]::Create($unguarded))
        $controlHolders = @(Get-StagedDllHolderUnguarded -Path $stagedCore)
        if ($controlHolders.Count -ne 1) {
            Write-Leg -Ok $false -Message "control: reported $($controlHolders.Count) holders, so it failed before reaching StartTime - THE CONTROL IS VOID"
        } else {
            Write-Leg -Ok ([string]::IsNullOrEmpty([string]$controlHolders[0].Started)) -Message "control: without the fallback the start time comes back blank - the leg above detects the regression"
        }
    }
    Remove-Item -Path Function:\Get-Process -ErrorAction SilentlyContinue

    # 5. -SkipRuntime must skip both the staging loop and the preflight.
    $runtimeSkip = $ast.FindAll({
            param($node)
            $node -is [System.Management.Automation.Language.IfStatementAst] -and
            $node.Clauses[0].Item1.Extent.Text -match "SkipRuntime" -and
            $node.Clauses[0].Item2.Extent.Text -match "continue"
        }, $true)
    Write-Leg -Ok ([bool]$runtimeSkip) -Message "-SkipRuntime short-circuits the runtime staging loop"

    # Behavioural, not structural: with the DLL locked, -SkipRuntime must get past the preflight AND
    # go on to do the work it exists for. "No lock error" on its own is not that assertion - it also
    # holds when the run dies immediately afterwards, which is how this leg first passed.
    Remove-Item -Path $stagedSatellite -Force -ErrorAction SilentlyContinue
    $baseBefore = (Get-FileHash -Path $stagedCore -Algorithm SHA256).Hash
    $lock2 = [System.IO.File]::Open($stagedCore, "Open", "ReadWrite", "None")
    try {
        $splatSkip = @{
            Script   = $sandboxScript
            Switches = @("-SkipRuntime")
            WorkDir  = $runDir
        }
        $skipLocked = Invoke-BuildDev @splatSkip
    } finally {
        $lock2.Close()
    }
    $baseAfter = (Get-FileHash -Path $stagedCore -Algorithm SHA256).Hash
    Write-Leg -Ok ($skipLocked.Output -notmatch "cannot stage") -Message "-SkipRuntime proceeds even with the staged DLL locked - the #849 unblock"
    Write-Leg -Ok ($skipLocked.ExitCode -eq 0) -Message "-SkipRuntime runs to completion with the base DLL locked (exit $($skipLocked.ExitCode))"
    Write-Leg -Ok ($skipLocked.Output -match "Skipping runtime dbatools.dll") -Message "it says it skipped the runtime rather than skipping silently"
    Write-Leg -Ok ($skipLocked.Output -notmatch "Building runtime dbatools.dll") -Message "no runtime build ran - and leg 3 proved this sandbox CAN run one"
    Write-Leg -Ok ($skipLocked.Output -match "Staged satellite: dbatools.fake") -Message "satellites still built and staged - -SkipRuntime is not a no-op"
    Write-Leg -Ok (Test-Path -LiteralPath $stagedSatellite) -Message "the satellite assembly really landed in the module stage"
    Write-Leg -Ok ($baseBefore -eq $baseAfter) -Message "the locked staged base is byte-identical after the run"

    # 6. NEGATIVE CONTROL. Everything above is unfalsifiable until a script WITHOUT the guard turns a
    #    leg red. Two earlier control shapes were void, both guarded against here: the pre-change
    #    script from git exits 1 on its own base-drop precondition when run from a bare temp
    #    directory (matching the exit code for entirely the wrong reason), and placed where its
    #    paths resolve it runs a real multi-minute dotnet build. So the control is the CURRENT
    #    script with only the mutual-exclusion block removed, inside the sandbox. With the guard
    #    gone both switches are accepted and both loops skip, so nothing is compiled.
    $controlPath = Join-Path -Path $sandboxBuild -ChildPath "build-dev-control.ps1"
    $controlText = $scriptText -replace "(?ms)if \(\`$SkipRuntime -and \`$SkipSatellites\) \{.*?\r?\n\}\r?\n", ""
    if ($controlText -eq $scriptText) {
        Write-Leg -Ok $false -Message "control: could not strip the mutual-exclusion block - THE CONTROL IS VOID, do not trust the legs above"
    } else {
        Set-Content -Path $controlPath -Value $controlText -Encoding UTF8
        $splatCtl = @{
            Script   = $controlPath
            Switches = @("-SkipRuntime", "-SkipSatellites")
            WorkDir  = $runDir
        }
        $control = Invoke-BuildDev @splatCtl
        if ($control.Output -match "base drop missing|A parameter cannot be found|ParameterBindingException") {
            Write-Leg -Ok $false -Message "control: died on a precondition, not on the missing guard - THE CONTROL IS VOID"
        } elseif ($control.Output -match "nothing to build") {
            Write-Leg -Ok $false -Message "control: the stripped script STILL reported the conflict - leg 1 cannot fail, do not trust it"
        } else {
            Write-Leg -Ok $true -Message "control: without the guard the conflict goes unreported (exit $($control.ExitCode)) - leg 1 detects the regression"
        }
    }

    # 7. End-to-end against the REAL tree, opt-in. Proves a -SkipRuntime run leaves the staged base
    #    byte-identical.
    if ($IncludeBuild) {
        $root = Split-Path -Path (Split-Path -Path $resolved)
        $realStaged = Join-Path -Path $root -ChildPath "artifacts/dbatools.library/core/lib/dbatools.dll"
        $before = (Get-FileHash -Path $realStaged -Algorithm SHA256).Hash
        $splatBuild = @{
            FilePath     = "pwsh"
            ArgumentList = @("-NoProfile", "-File", $resolved, "-SkipRuntime")
            Wait         = $true
            PassThru     = $true
            NoNewWindow  = $true
        }
        $build = Start-Process @splatBuild
        $after = (Get-FileHash -Path $realStaged -Algorithm SHA256).Hash
        Write-Leg -Ok ($build.ExitCode -eq 0) -Message "-SkipRuntime completes against the real tree (exit $($build.ExitCode))"
        Write-Leg -Ok ($before -eq $after) -Message "the staged base dbatools.dll is untouched by a -SkipRuntime run"
    } else {
        Write-Host "skip end-to-end -SkipRuntime build (pass -IncludeBuild, holding the library edit lease)"
    }
} finally {
    $env:PATH = $originalPath
    Remove-Item -Path $runDir -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "$pass passed, $fail failed"
if ($fail -gt 0) { exit 1 }
exit 0
