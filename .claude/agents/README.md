# dbatools Rewrite Agent Team

## Overview

This is a set of 12 specialized Claude Code subagents designed for converting dbatools PowerShell ps1 functions into C# binary cmdlets in dbatools.library with maximum quality, security, and fidelity. Each agent runs on the model best suited to its cognitive task — **Opus** for judgment and thoroughness, **Sonnet** for precise structured execution.

**Architecture:** Pure C# binary module. Every PS1 function becomes a `[Cmdlet]` class. No PS1 wrapper layer.

## The Agent Team

```
                         ┌──────────────┐
                         │  YOU (Boss)  │
                         └──────┬───────┘
                                │
                                ▼
                    ┌───────────────────────┐
                    │ migration-coordinator │
                    │    Plans & Tracks     │
                    └──┬──────────┬────────┘
          ┌────────────┘          └─────────────┐
          ▼                                     ▼
┌──────────────────┐                  ┌────────────┐
│ csharp-library-  │                  │ sqlserver- │
│ architect        │                  │ domain-    │
│ Writes cmdlets   │                  │ expert     │
└────────┬─────────┘                  │ Reference  │
         │                            └────────────┘
         ▼
┌──────────────────┐
│ feature-parity-  │
│ guardian         │
│ 100% coverage    │
└────────┬─────────┘
         ▼
┌──────────────────┐
│ microsoft-sdk-   │
│ validator        │
│ SMO/SqlClient    │
└────────┬─────────┘
         ▼
┌──────────────────┐
│ best-practices-  │
│ reviewer         │
│ Code quality     │
└────────┬─────────┘
         ▼
┌──────────────────┐
│ xplat-compat-    │
│ reviewer         │
│ Linux/macOS      │
└────────┬─────────┘
         ▼
┌──────────────────┐
│ tsql-collation-  │
│ reviewer         │
│ CS collations    │
└────────┬─────────┘
         ▼
┌──────────────────┐
│ security-        │
│ auditor          │
│ No CVEs          │
└────────┬─────────┘
         ▼
┌──────────────────┐
│ pester-test-     │
│ guardian         │
│ Tests & Speed    │
└────────┬─────────┘
         ▼
┌──────────────────┐
│ regression-      │
│ sentinel         │
│ Diffs old vs new │
│ Read-only        │
└────────┬─────────┘
         ▼
┌──────────────────┐
│ dbatools-spirit- │
│ guardian         │
│ Final UX Gate    │
│ Read-only        │
└──────────────────┘
```

## All 12 Agents

| Agent | Model | Why | Access | Purpose |
|-------|-------|-----|--------|---------|
| **csharp-library-architect** | Opus | Complex architectural reasoning | Read/Write | Converts ps1 functions into C# binary cmdlets |
| **best-practices-reviewer** | Opus | Senior dev judgment | Read-only | Code quality, SOLID, performance, maintainability |
| **xplat-compatibility-reviewer** | Opus | Platform nuance requires judgment | Read-only | Cross-platform compat: path handling, Windows API guards, culture-safe strings |
| **tsql-collation-reviewer** | Opus | Collation subtlety requires deep SQL knowledge | Read-only | T-SQL case-sensitivity: COLLATE clauses, CS collation compliance |
| **security-auditor** | Opus | Can't miss a vulnerability | Read-only | SQL injection, credential handling, OWASP, prevents CVEs |
| **dbatools-spirit-guardian** | Opus | UX philosophy requires nuance | Read-only | Final gate — rejects enterprise-itis, protects "it just works" |
| **microsoft-sdk-validator** | Opus | Deep source comprehension | Read-only | Validates against actual SMO/SqlClient source code |
| **feature-parity-guardian** | Opus | Must not miss a feature | Read-only | Exhaustively verifies every PS1 feature is present in C# |
| **regression-sentinel** | Opus | Thoroughness is the point | Read-only | Diffs old vs new: parameters, output, behavior, edge cases |
| **migration-coordinator** | Sonnet | Structured tracking, not philosophy | Read/Write | Plans conversion order, tracks progress, manages dependencies |
| **pester-test-guardian** | Sonnet | Systematic execution | Read/Write | Validates tests pass, finds speed optimizations |
| **sqlserver-domain-expert** | Sonnet | Knowledge retrieval, not judgment | Read-only | SQL Server versions, SMO quirks, Azure SQL differences |

## Workflow Per Command

```
 1. PLAN          → migration-coordinator picks the next command & checks deps
 2. CONVERT       → csharp-library-architect writes the C# binary cmdlet
 3. CONSULT       → sqlserver-domain-expert answers version/SMO questions (as needed)
 4. FEATURE CHECK → feature-parity-guardian verifies 100% feature coverage
 5. SDK CHECK     → microsoft-sdk-validator validates against real Microsoft source
 6. CODE REVIEW   → best-practices-reviewer does senior dev review
 7. XPLAT CHECK   → xplat-compatibility-reviewer checks cross-platform compatibility
 8. COLLATION     → tsql-collation-reviewer checks T-SQL case-sensitivity compliance
 9. SECURITY      → security-auditor audits for vulnerabilities
10. TEST          → pester-test-guardian validates tests pass + identifies speedups
11. DETECT        → regression-sentinel compares old vs new, flags all differences
12. REVIEW        → dbatools-spirit-guardian gives final approval or rejects
13. DONE          → migration-coordinator updates tracker, picks next command
```

## Conversion Order Strategy

| Phase | What | Why First |
|-------|------|-----------|
| **1. Foundation** | Connect-DbaInstance, connection management, message system, shared types | Everything depends on these |
| **2. Leaf reads** | `Get-Dba*`, `Test-Dba*`, `Find-Dba*` | No deps, lowest risk, read-only |
| **3. Leaf writes** | `Set-Dba*`, `New-Dba*` | Still no deps, but modify state |
| **4. Destructive** | `Remove-Dba*` | Need extra WhatIf/Confirm validation |
| **5. Orchestration** | `Copy-Dba*`, migration commands | Call other commands internally |
| **6. Edge cases** | Rarely used, complex externals | Lowest priority |

## Review Gates — NONE Can Be Skipped

| Gate | Agent | What It Catches |
|------|-------|-----------------|
| **Feature parity** | feature-parity-guardian | Silently dropped features, missing code paths |
| **SDK compatibility** | microsoft-sdk-validator | API misuse, version incompatibilities, SMO gotchas |
| **Code quality** | best-practices-reviewer | Code smells, performance issues, maintainability problems |
| **Xplat compat** | xplat-compatibility-reviewer | Windows-only APIs without guards, hardcoded paths, culture bugs |
| **Collation safety** | tsql-collation-reviewer | Missing COLLATE clauses, CS collation failures, case-sensitive comparisons |
| **Security audit** | security-auditor | SQL injection, credential leaks, input validation gaps |
| **Regression check** | regression-sentinel | Changed parameters, output types, defaults, edge cases |
| **Spirit review** | dbatools-spirit-guardian | Enterprise-itis, lost discoverability, worse errors, UX regressions |
| **Test validation** | pester-test-guardian | Broken tests, coverage gaps |

## Red Flags That Block Approval

- Parameter removed or renamed without alias
- Output property removed or type changed
- Default behavior changed silently
- New required parameters that didn't exist before
- Raw .NET stack traces instead of friendly error messages
- Something that was auto-detected now requires explicit specification
- Lost pipeline support
- SQL injection vulnerability (any severity)
- Credentials appearing in logs or error messages
- Missing features from the original PS1
- Windows-only code without platform guard (crashes on Linux)
- T-SQL without COLLATE on cross-collation joins (fails on CS instances)

## Quick Reference: When to Invoke Each Agent

| You want to... | Use this agent |
|----------------|---------------|
| Plan what to convert next | `migration-coordinator` |
| Convert a ps1 to C# binary cmdlet | `csharp-library-architect` |
| Check "does this work on SQL 2012?" | `sqlserver-domain-expert` |
| Verify all features are preserved | `feature-parity-guardian` |
| Validate against Microsoft source | `microsoft-sdk-validator` |
| Senior code review | `best-practices-reviewer` |
| Check "does this work on Linux?" | `xplat-compatibility-reviewer` |
| Check "does this work on CS collation?" | `tsql-collation-reviewer` |
| Security audit | `security-auditor` |
| Run tests / find speed wins | `pester-test-guardian` |
| Compare old vs new behavior | `regression-sentinel` |
| Final sign-off on a conversion | `dbatools-spirit-guardian` |
| Check migration status | `migration-coordinator` |

## Tips for Using the Team

1. **Start each session** by asking the migration-coordinator for current status
2. **Opus agents think deep** — let them run on judgment-heavy work, don't rush
3. **Sonnet agents execute fast** — they follow patterns without overthinking, which is the point
4. **Run the review pipeline in order** — feature parity → SDK → code review → xplat → collation → security → tests → regression → spirit
5. **Batch related commands** — e.g., all backup commands together share C# classes
6. **Don't skip ANY review gate** — they each catch different categories of issues
7. **Use sqlserver-domain-expert proactively** — don't wait until tests fail on Azure SQL
8. **The microsoft-sdk-validator has source code access** — use it to resolve API behavior questions definitively
