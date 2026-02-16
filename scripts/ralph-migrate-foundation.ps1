<#
.SYNOPSIS
    Ralph Wiggum migration orchestrator for the foundation domain (16 commands).

.DESCRIPTION
    Iteratively invokes Claude Code to convert foundation commands from PS1 to C# binary cmdlets.
    Each iteration converts ONE command, verifies the build, and commits.

.PARAMETER MaxIterations
    Maximum iterations before stopping (default: 26).

.PARAMETER DryRun
    Show what would be run without executing.
#>
[CmdletBinding()]
param(
    [int]$MaxIterations = 26,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$RepoRoot = 'c:\github\dbatools.library'
$TrackerPath = Join-Path $RepoRoot 'docs' 'plan' 'TRACKER-MIGRATE-FOUNDATION.md'
$SignalFile = Join-Path $RepoRoot 'docs' 'plan' '.migrate-foundation-complete'
$PromptPath = Join-Path $RepoRoot 'scripts' 'migrate-prompt.md'
$Domain = 'foundation'
$DomainUpper = 'FOUNDATION'
$Dependencies = 'None (must be converted first)'

function Get-TrackerStatus {
    if (-not (Test-Path $TrackerPath)) { return @{ Pending = 0; Done = 0; Total = 0 } }
    $content = Get-Content $TrackerPath -Raw
    $pending = ([regex]::Matches($content, '\| PENDING \|')).Count
    $done = ([regex]::Matches($content, '\| DONE \|')).Count
    return @{ Pending = $pending; Done = $done; Total = ($pending + $done) }
}

# Pre-flight check
Write-Host "=== Ralph Wiggum: $DomainUpper Domain ===" -ForegroundColor Cyan
Write-Host "  Commands: 16 | Max iterations: $MaxIterations" -ForegroundColor Cyan
Write-Host "  Dependencies: $Dependencies" -ForegroundColor Cyan
Write-Host "  Tracker: $TrackerPath" -ForegroundColor Cyan

if (Test-Path $SignalFile) {
    Write-Host "[DONE] $Domain already complete." -ForegroundColor Green
    exit 0
}

for ($i = 1; $i -le $MaxIterations; $i++) {
    $status = Get-TrackerStatus
    if ($status.Pending -eq 0 -and $status.Done -gt 0) {
        Write-Host "[DONE] All $($status.Done) commands in $Domain migrated." -ForegroundColor Green
        New-Item -Path $SignalFile -ItemType File -Force | Out-Null
        break
    }

    Write-Host ("`n--- $Domain iteration $i/$MaxIterations ({0} pending, {1} done) ---" -f $status.Pending, $status.Done) -ForegroundColor Yellow

    if ($DryRun) {
        Write-Host "[DRY RUN] Would invoke Claude Code" -ForegroundColor DarkGray
        continue
    }

    try {
        Set-Location $RepoRoot
        $promptContent = Get-Content $PromptPath -Raw
        claude --print --dangerously-skip-permissions $promptContent
    }
    catch {
        Write-Host "[ERROR] Iteration $i failed: $_" -ForegroundColor Red
    }

    Start-Sleep -Seconds 2
}

$final = Get-TrackerStatus
Write-Host ("`n=== $DomainUpper Final: {0}/{1} done ===" -f $final.Done, $final.Total) -ForegroundColor Cyan
if ($final.Pending -gt 0) {
    Write-Host "[WARN] $($final.Pending) commands still pending" -ForegroundColor Yellow
}
