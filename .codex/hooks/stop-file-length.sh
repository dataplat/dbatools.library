#!/usr/bin/env bash
set -eu

MAX_LINES=400

repo_root=$(git rev-parse --show-toplevel 2>/dev/null || true)
if [ -z "$repo_root" ]; then
    exit 0
fi
cd "$repo_root"

skip_prefixes='^(artifacts/|benchmarks/CsvBenchmarks/(bin|obj)/|project/(Dataplat\.Dbatools\.Csv|dbatools|dbatools\.Tests)/(bin|obj)/|var/(misc|third-party-licenses)/|\.claude/)'
binary_re='\.(7z|dll|exe|gif|gz|ico|jpe?g|nupkg|pdb|pdf|png|snupkg|ttf|vsix|woff2?|zip)$'

violations=""

while IFS= read -r path; do
    [ -n "$path" ] || continue
    norm=${path//\\//}

    if [[ "$norm" =~ $skip_prefixes ]]; then continue; fi
    if [[ "$norm" =~ $binary_re ]]; then continue; fi
    [ -f "$norm" ] || continue

    if grep -qIl '' "$norm" 2>/dev/null; then
        lines=$(wc -l < "$norm" | tr -d ' ')
        if [ "$lines" -gt "$MAX_LINES" ]; then
            violations="${violations}  ${lines} lines: ${norm}"$'\n'
        fi
    fi
done < <(git ls-files --cached --others --exclude-standard)

if [ -n "$violations" ]; then
    {
        echo "Files exceed ${MAX_LINES}-line limit. Split structurally before finishing:"
        echo ""
        printf "%s" "$violations"
    } >&2
    exit 2
fi

exit 0
