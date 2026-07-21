#!/bin/bash
# stop-verify.sh - Quality gate when Claude finishes a dbatools.library session.
# Fires ONCE per session, the first time C# / manifest files have changed, and
# forces a single self-verification round. Disable per-session with
# CLAUDE_STOP_VERIFY=off.
#
# Adapted from the dbatools (PowerShell) hook of the same name. The file filter
# and the checklist are C#/migration specific — the PS version keys on
# .ps1/.psm1/.psd1 and emits a Pester-shaped checklist that would never fire here.
set -uo pipefail

source "$(dirname "$0")/lib-stop-guard.sh"
source "$(dirname "$0")/lib-git-changes.sh"

if [[ "${CLAUDE_STOP_VERIFY:-}" == "off" ]]; then
    exit 0
fi

# Needs transcript context to mark "already verified" — without it, stay quiet
# rather than risk a block loop.
[[ -z "${_MARKER_DIR:-}" || -z "${_TRANSCRIPT_HASH:-}" ]] && exit 0

# SESSION_CHANGED_FILES (not CHANGED_FILES): only files THIS session wrote, so
# another lane's uncommitted work in a shared checkout never trips this gate.
CODE_FILES=$(printf '%s\n' "$SESSION_CHANGED_FILES" | grep -E '\.(cs|psd1)$' | grep -v '^\.claude/' | grep -v '^\.codex/')
[[ -z "$CODE_FILES" ]] && exit 0

DONE_MARKER="${_MARKER_DIR}/${_TRANSCRIPT_HASH}_stop-verify.done"
[[ -f "$DONE_MARKER" ]] && exit 0
touch "$DONE_MARKER" 2>/dev/null

emit_stop_block "QUALITY GATE — C# / manifest files changed this session. Perform ALL checks below, then finish (this gate fires once per session).

## 1. BUILD AND TEST
- Build clean: dotnet build project/dbatools.sln -c Release
- MSTest green on BOTH TFMs (net472 AND net8.0). One TFM is not a pass.

## 2. LANGUAGE REGIME (scoped per project)
- project/dbatools/ and project/Dataplat.Dbatools.Csv/ are LangVersion 7.3 — NO C# 8+ syntax
- Satellite command projects follow their own regime; check before using newer syntax
- Normative source: dbatools/migration/specs/architecture.md section 11

## 3. PORT PARITY — the traps that have actually shipped bugs here
- Cross-record state: does the source accumulate or carry a variable ACROSS pipeline records?
  A single-record test leg CANNOT observe that class. Prove it with a MULTI-RECORD piped leg.
- Buffered vs streaming hops: if the source emits then can throw mid-loop, a buffered
  InvokeScoped DISCARDS already-emitted rows. Use InvokeScopedStreaming.
- Positional bindings: [Parameter(Position=N)] is easy to drop silently. surfaceDiff catches it.
- ShouldProcess / -WhatIf: assert the side effect does NOT happen, not just that it returned.
- Message seams: route through WriteMessage, never direct WriteVerbose/WriteWarning.

## 4. FILE AND REGISTRATION RULES
- 400-line limit per file; split into .Helpers.cs / .Scripts.cs partials
- After splitting, scan with Get-MissingSplitPartial — detectors keyed on *Command.cs under-scan partials
- New cmdlets registered in modules/<module>/<module>.psd1 CmdletsToExport (explicit, never wildcards)

## 5. VERIFY AGAINST SOURCE, NOT AGAINST EVIDENCE
Tracker Evidence cells in migration/trackers/ have been caught describing code that does not
exist. If you acted on a tracker row, confirm the claim against the actual source and .cs
before finishing. When evidence and code disagree, the code wins.

## 6. GATE (if you expect this to be DONE)
  pwsh.exe -NoProfile -File C:\\\\github\\\\dbatools\\\\migration\\\\tools\\\\Invoke-GateWithWorkstationSteps.ps1 -Command <Cmd> -Module <satellite>
- PASS = all 7 core steps green. SKIPPED NEVER counts as PASS.
- A green gate that did not exercise the distinguishing leg proves nothing.

If anything fails, fix it before finishing. If everything passes, state what you verified and finish."
exit 0
