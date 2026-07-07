#!/usr/bin/env bash
set -eu

# PSD1 rules per CLAUDE.md: no wildcard exports in module manifests. Wildcards defeat
# command auto-loading discovery and make the export surface unauditable.

repo_root=$(git rev-parse --show-toplevel 2>/dev/null || true)
if [ -z "$repo_root" ]; then
    exit 0
fi
cd "$repo_root"

violations=""

while IFS= read -r path; do
    [ -n "$path" ] || continue
    norm=${path//\\//}
    case "$norm" in
        */bin/*|*/obj/*|artifacts/*) continue ;;
    esac
    [ -f "$norm" ] || continue

    match=$(grep -nE "^[[:space:]]*(FunctionsToExport|CmdletsToExport|AliasesToExport|VariablesToExport)[[:space:]]*=[[:space:]]*(@\()?[[:space:]]*['\"]\*['\"]" "$norm" || true)
    if [ -n "$match" ]; then
        violations="${violations}  wildcard export in ${norm}: ${match}"$'\n'
    fi
done < <(git ls-files --cached --others --exclude-standard -- '*.psd1')

if [ -n "$violations" ]; then
    {
        echo "Module manifest rule violations (CLAUDE.md: no wildcard exports). Fix before finishing:"
        echo ""
        printf "%s" "$violations"
    } >&2
    exit 2
fi

exit 0
