#!/usr/bin/env bash
# PreToolUse hook: Enforce .psd1 manifest rules
# Blocks wildcard exports ('*') in FunctionsToExport, CmdletsToExport, etc.
set -eu

input=$(cat)

# Parse JSON with node — node writes content to temp file and tells us the path
eval "$(node -e "
const os = require('os'), fs = require('fs'), path = require('path');
const j = JSON.parse(fs.readFileSync(0, 'utf8'));
const t = j.tool_input || {};
const fp = t.file_path || '';
const c = t.new_string || t.content || '';
if (fp && c) {
  const tmp = path.join(os.tmpdir(), 'claude_psd1_' + process.pid + '.txt');
  fs.writeFileSync(tmp, c);
  console.log('file_path=' + JSON.stringify(fp));
  console.log('filename=' + JSON.stringify(path.basename(fp)));
  console.log('tmp=' + JSON.stringify(tmp));
} else {
  console.log('file_path=');
}
" <<< "$input")"

[[ -z "${file_path:-}" ]] && exit 0
[[ "$file_path" != *.psd1 ]] && exit 0
[[ -z "${tmp:-}" || ! -f "$tmp" ]] && exit 0

trap 'rm -f "$tmp"' EXIT

violations=()

# Strip comment lines, then check for wildcard exports
for field in Functions Cmdlets Aliases Variables; do
    if grep -v '^\s*#' "$tmp" | grep -q "${field}ToExport" && grep -v '^\s*#' "$tmp" | grep "${field}ToExport" | grep -q "'\\*'\\|\"\\*\""; then
        violations+=("${field}ToExport must use explicit list, never wildcard '*'")
    fi
done

if [[ ${#violations[@]} -gt 0 ]]; then
    msg="BLOCKED: Manifest rule violation(s) in ${filename}:"
    for v in "${violations[@]}"; do
        msg+=$'\n'"$v"
    done
    echo "$msg" >&2
    exit 2
fi

exit 0
