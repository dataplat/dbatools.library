<#
.SYNOPSIS
    Master orchestrator for Ralph Wiggum stateless migration automation.

.DESCRIPTION
    Runs domain migration scripts in dependency order.
    Foundation runs first (blocking). All other domains can run in parallel.

.PARAMETER Parallel
    Run non-foundation domains in parallel (default: sequential).

.PARAMETER Domain
    Run only a specific domain (e.g., "ag", "agent", "database").

.PARAMETER DryRun
    Show what would be run without executing.

.PARAMETER MaxIterationsPerDomain
    Override the max iterations per domain (default: command count + 10).
#>
[CmdletBinding()]
param(
    [switch]$Parallel,
    [string]$Domain,
    [switch]$DryRun,
    [int]$MaxIterationsPerDomain = 0
)

$ErrorActionPreference = 'Stop'
$ScriptRoot = $PSScriptRoot
$RepoRoot = Split-Path $ScriptRoot -Parent
$PlanDir = Join-Path $RepoRoot 'docs' 'plan'

# Domain execution order - foundation MUST be first
$DomainOrder = @(
    'foundation'   # 16 commands - core connectivity, MUST be first
    'config'       # 18 commands - dbatools configuration
    'database'     # 116 commands - database operations
    'instance'     # 95 commands - instance management
    'security'     # 71 commands - authentication & authorization
    'dbobject'     # 51 commands - database child objects
    'agent'        # 51 commands - SQL Agent
    'computer'     # 50 commands - OS-level operations
    'ag'           # 35 commands - Availability Groups
    'network'      # 31 commands - endpoints, TCP, certificates
    'maintenance'  # 29 commands - community tools, diagnostics
    'backup'       # 20 commands - backup & restore
    'xevent'       # 20 commands - Extended Events
    'replication'  # 20 commands - replication
    'resourcegovernor' # 19 commands - Resource Governor + PBM
    'perfcounter'  # 15 commands - performance counters
    'logshipping'  # 14 commands - log shipping + mirroring
    'regserver'    # 12 commands - registered servers
    'dbmail'       # 12 commands - database mail
    'migration'    # 3 commands - Copy-DbaDatabase, Start-DbaMigration
)

# Command counts per domain (for max iterations calculation)
$DomainCounts = @{
    'foundation' = 16; 'config' = 18; 'database' = 116; 'instance' = 95
    'security' = 71; 'dbobject' = 51; 'agent' = 51; 'computer' = 50
    'ag' = 35; 'network' = 31; 'maintenance' = 29; 'backup' = 20
    'xevent' = 20; 'replication' = 20; 'resourcegovernor' = 19
    'perfcounter' = 15; 'logshipping' = 14; 'regserver' = 12
    'dbmail' = 12; 'migration' = 3
}

# Load agent definitions for --agents flag
. (Join-Path $ScriptRoot 'ralph-agents.ps1')
$agentsJson = Get-RalphAgentsJson -RepoRoot $RepoRoot

function Get-TrackerStatus {
    param([string]$TrackerPath)
    if (-not (Test-Path $TrackerPath)) { return @{ Pending = 0; Done = 0; Total = 0 } }
    $content = Get-Content $TrackerPath -Raw
    $pending = ([regex]::Matches($content, '\| PENDING \|')).Count
    $done = ([regex]::Matches($content, '\| DONE \|')).Count
    return @{ Pending = $pending; Done = $done; Total = ($pending + $done) }
}

function Test-DomainComplete {
    param([string]$DomainName)
    $tracker = Join-Path $PlanDir "TRACKER-MIGRATE-$($DomainName.ToUpper()).md"
    $signal = Join-Path $PlanDir ".migrate-$DomainName-complete"
    if (Test-Path $signal) { return $true }
    $status = Get-TrackerStatus $tracker
    return ($status.Pending -eq 0 -and $status.Done -gt 0)
}

function Invoke-DomainMigration {
    param([string]$DomainName)

    $domainUpper = $DomainName.ToUpper()
    $tracker = Join-Path $PlanDir "TRACKER-MIGRATE-$domainUpper.md"
    $signal = Join-Path $PlanDir ".migrate-$DomainName-complete"
    $prompt = Join-Path $ScriptRoot 'migrate-prompt.md'

    if (Test-Path $signal) {
        Write-Host "[SKIP] $DomainName - already complete" -ForegroundColor Green
        return
    }

    $status = Get-TrackerStatus $tracker
    if ($status.Pending -eq 0 -and $status.Done -gt 0) {
        Write-Host "[SKIP] $DomainName - all $($status.Done) commands done" -ForegroundColor Green
        New-Item -Path $signal -ItemType File -Force | Out-Null
        return
    }

    $maxIter = if ($MaxIterationsPerDomain -gt 0) { $MaxIterationsPerDomain } else { $DomainCounts[$DomainName] + 10 }

    Write-Host "`n========================================" -ForegroundColor Cyan
    Write-Host "  DOMAIN: $domainUpper ($($status.Pending) pending, $($status.Done) done)" -ForegroundColor Cyan
    Write-Host "  Max iterations: $maxIter" -ForegroundColor Cyan
    Write-Host "========================================`n" -ForegroundColor Cyan

    for ($i = 1; $i -le $maxIter; $i++) {
        # Check if done
        $currentStatus = Get-TrackerStatus $tracker
        if ($currentStatus.Pending -eq 0) {
            Write-Host "[DONE] $DomainName - all $($currentStatus.Done) commands migrated" -ForegroundColor Green
            New-Item -Path $signal -ItemType File -Force | Out-Null
            break
        }

        Write-Host "`n--- Iteration $i/$maxIter for $DomainName ($($currentStatus.Pending) remaining) ---" -ForegroundColor Yellow

        if ($DryRun) {
            Write-Host "[DRY RUN] Would invoke Claude Code for $DomainName iteration $i" -ForegroundColor DarkGray
            continue
        }

        # Invoke Claude Code with the migration prompt (streaming tool use output)
        try {
            Set-Location $RepoRoot
            $promptContent = Get-Content $prompt -Raw
            $sessionId = [guid]::NewGuid().ToString()

            $claudeArgs = @(
                '--dangerously-skip-permissions'
                '--session-id', $sessionId
                '--no-session-persistence'
                '--verbose'
                '--output-format', 'stream-json'
                '--agents', $agentsJson
                '-p', $promptContent
            )

            & claude @claudeArgs 2>&1 | ForEach-Object {
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
            # Don't break - let the next iteration try
        }

        # Small delay between iterations
        Start-Sleep -Seconds 2
    }

    # Final status
    $finalStatus = Get-TrackerStatus $tracker
    if ($finalStatus.Pending -gt 0) {
        Write-Host "[WARN] $DomainName still has $($finalStatus.Pending) pending commands after $maxIter iterations" -ForegroundColor Yellow
    }
}

# === MAIN ===

Write-Host "`n=== Ralph Wiggum Migration Automation ===" -ForegroundColor Magenta
Write-Host "=== dbatools PS1 -> C# Binary Module  ===" -ForegroundColor Magenta
Write-Host "=== 698 commands across 20 domains     ===`n" -ForegroundColor Magenta

# Single domain mode
if ($Domain) {
    if ($Domain -notin $DomainOrder) {
        Write-Error "Unknown domain: $Domain. Valid domains: $($DomainOrder -join ', ')"
        return
    }

    # Check foundation dependency
    if ($Domain -ne 'foundation' -and -not (Test-DomainComplete 'foundation')) {
        Write-Host "[BLOCK] Cannot run $Domain - foundation not complete. Run foundation first." -ForegroundColor Red
        return
    }

    Invoke-DomainMigration -DomainName $Domain
    return
}

# Full run mode

# Phase 1: Foundation (blocking)
Write-Host "`n=== PHASE 1: Foundation (blocking) ===" -ForegroundColor Magenta
Invoke-DomainMigration -DomainName 'foundation'

if (-not (Test-DomainComplete 'foundation')) {
    Write-Host "[BLOCK] Foundation not complete. Cannot proceed." -ForegroundColor Red
    return
}

# Phase 2: All other domains
$remainingDomains = $DomainOrder | Where-Object { $_ -ne 'foundation' }

if ($Parallel) {
    Write-Host "`n=== PHASE 2: Remaining domains (PARALLEL) ===" -ForegroundColor Magenta

    # Migration domain has extra dependencies - run it last
    $parallelDomains = $remainingDomains | Where-Object { $_ -ne 'migration' }

    $jobs = @()
    foreach ($d in $parallelDomains) {
        $scriptPath = Join-Path $ScriptRoot "ralph-migrate-$d.ps1"
        if (Test-Path $scriptPath) {
            Write-Host "  Launching $d in background..." -ForegroundColor DarkCyan
            $jobs += Start-Job -FilePath $scriptPath
        }
        else {
            Write-Host "  Running $d inline (no domain script)..." -ForegroundColor DarkCyan
            Invoke-DomainMigration -DomainName $d
        }
    }

    if ($jobs.Count -gt 0) {
        Write-Host "  Waiting for $($jobs.Count) parallel domain jobs..." -ForegroundColor DarkCyan
        $jobs | Wait-Job | Receive-Job
        $jobs | Remove-Job
    }

    # Phase 3: Migration domain (depends on many others)
    Write-Host "`n=== PHASE 3: Migration domain ===" -ForegroundColor Magenta
    Invoke-DomainMigration -DomainName 'migration'
}
else {
    Write-Host "`n=== PHASE 2: Remaining domains (sequential) ===" -ForegroundColor Magenta
    foreach ($d in $remainingDomains) {
        Invoke-DomainMigration -DomainName $d
    }
}

# Final report
Write-Host "`n`n=== MIGRATION STATUS REPORT ===" -ForegroundColor Magenta
$totalPending = 0
$totalDone = 0
foreach ($d in $DomainOrder) {
    $tracker = Join-Path $PlanDir "TRACKER-MIGRATE-$($d.ToUpper()).md"
    $status = Get-TrackerStatus $tracker
    $complete = if (Test-DomainComplete $d) { "[DONE]" } else { "[    ]" }
    $bar = if ($status.Total -gt 0) { [math]::Round(($status.Done / $status.Total) * 100) } else { 0 }
    Write-Host ("  {0} {1,-20} {2,3}/{3,3} ({4,3}%)" -f $complete, $d, $status.Done, $status.Total, $bar)
    $totalPending += $status.Pending
    $totalDone += $status.Done
}
Write-Host ("`n  TOTAL: {0}/{1} commands migrated ({2}% complete)" -f $totalDone, ($totalPending + $totalDone), [math]::Round(($totalDone / [math]::Max(1, $totalPending + $totalDone)) * 100))

if ($totalPending -eq 0) {
    Write-Host "`n  ALL COMMANDS MIGRATED! dbatools is now a pure C# binary module." -ForegroundColor Green
}
