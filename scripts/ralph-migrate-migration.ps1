<#
.SYNOPSIS
    Ralph Wiggum migration orchestrator for the migration domain (3 commands).

.DESCRIPTION
    Iteratively invokes Claude Code to convert migration commands from PS1 to C# binary cmdlets.
    Each iteration converts ONE command, verifies the build, and commits.

.PARAMETER MaxIterations
    Maximum iterations before stopping (default: 13).

.PARAMETER DryRun
    Show what would be run without executing.
#>
[CmdletBinding()]
param(
    [int]$MaxIterations = 13,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$RepoRoot = 'c:\github\dbatools.library'
$TrackerPath = [IO.Path]::Combine($RepoRoot, 'docs', 'plan', 'TRACKER-MIGRATE-MIGRATION.md')
$SignalFile = [IO.Path]::Combine($RepoRoot, 'docs', 'plan', '.migrate-migration-complete')
$PromptPath = [IO.Path]::Combine($RepoRoot, 'scripts', 'migrate-prompt.md')
$Domain = 'migration'
$DomainUpper = 'MIGRATION'
$Dependencies = 'foundation, database, instance, security, agent'

function Get-TrackerStatus {
    if (-not (Test-Path $TrackerPath)) { return @{ Pending = 0; Done = 0; Total = 0 } }
    $content = Get-Content $TrackerPath -Raw
    $pending = ([regex]::Matches($content, '\| PENDING \|')).Count
    $done = ([regex]::Matches($content, '\| DONE \|')).Count
    return @{ Pending = $pending; Done = $done; Total = ($pending + $done) }
}

# Pre-flight check
Write-Host "=== Ralph Wiggum: $DomainUpper Domain ===" -ForegroundColor Cyan
Write-Host "  Commands: 3 | Max iterations: $MaxIterations" -ForegroundColor Cyan
Write-Host "  Dependencies: $Dependencies" -ForegroundColor Cyan
Write-Host "  Tracker: $TrackerPath" -ForegroundColor Cyan

if (Test-Path $SignalFile) {
    Write-Host "[DONE] $Domain already complete." -ForegroundColor Green
    exit 0
}

$agentsJson = Get-RalphAgentsJson -RepoRoot $RepoRoot

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
        $sessionId = [guid]::NewGuid().ToString()

        $claudeArgs = @(
            '--dangerously-skip-permissions'
            '--session-id', $sessionId
            '--no-session-persistence'
            '--verbose'
            '--output-format', 'stream-json'
            '-p', '-'
        )

        $promptContent | & claude @claudeArgs 2>&1 | ForEach-Object {
            $line = $PSItem
            try {
                $obj = $line | ConvertFrom-Json -ErrorAction Stop
                switch ($obj.type) {
                    'assistant' {
                        if ($obj.message.content) {
                            foreach ($c in $obj.message.content) {
                                switch ($c.type) {
                                    'tool_use' {
                                        $toolName = $c.name
                                        $detail = ""
                                        if ($c.input) {
                                            switch ($toolName) {
                                                'Read'  { $detail = Split-Path $c.input.file_path -Leaf }
                                                'Write' { $detail = Split-Path $c.input.file_path -Leaf }
                                                'Edit'  { $detail = Split-Path $c.input.file_path -Leaf }
                                                'Glob'  { $detail = $c.input.pattern -replace '.*/',''}
                                                'Grep'  { $detail = $c.input.pattern.Substring(0, [Math]::Min(30, $c.input.pattern.Length)) }
                                                'Bash'  { $detail = ($c.input.command -split '\n')[0].Substring(0, [Math]::Min(40, ($c.input.command -split '\n')[0].Length)) }
                                                'Task'  { $detail = $c.input.description }
                                            }
                                        }
                                        if ($detail) {
                                            Write-Host "  > $toolName " -ForegroundColor DarkCyan -NoNewline
                                            Write-Host $detail -ForegroundColor DarkGray
                                        } else {
                                            Write-Host "  > $toolName" -ForegroundColor DarkCyan
                                        }
                                    }
                                    'text' {
                                        if ($c.text) { Write-Host $c.text -ForegroundColor White }
                                    }
                                }
                            }
                        }
                    }
                    'result' {
                        $duration = [math]::Round($obj.duration_ms / 1000, 1)
                        $cost = [math]::Round($obj.total_cost_usd, 4)
                        Write-Host ""
                        Write-Host "  Completed in ${duration}s (`$$cost)" -ForegroundColor Green
                    }
                }
            } catch {
                if ($line -and $line -notmatch '^\s*$') { Write-Host $line -ForegroundColor DarkGray }
            }
        }
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