#!/usr/bin/env bash
# PostToolUse hook: Run dotnet build after C# file edits
# Blocks on failure (exit 2) so Claude must fix before continuing
set -eu

input=$(cat)

# Parse JSON with node — extract file_path and cwd
eval "$(node -e "
const j = JSON.parse(require('fs').readFileSync(0, 'utf8'));
const t = j.tool_input || {};
const fp = t.file_path || '';
const cwd = j.cwd || '';
console.log('file_path=' + JSON.stringify(fp));
console.log('hook_cwd=' + JSON.stringify(cwd));
" <<< "$input")"

[[ -z "${file_path:-}" ]] && exit 0
[[ "$file_path" != *.cs ]] && exit 0

# Find the .csproj
project_path="${hook_cwd}/project/dbatools/dbatools.csproj"
if [[ ! -f "$project_path" ]]; then
    project_path="${CLAUDE_PROJECT_DIR:-$hook_cwd}/project/dbatools/dbatools.csproj"
fi

# Run the build (capture exit code without triggering set -e)
set +e
build_output=$(dotnet build "$project_path" --nologo --verbosity quiet 2>&1)
build_exit=$?
set -e

if [[ $build_exit -ne 0 ]]; then
    errors=$(echo "$build_output" | grep "error CS" | head -10)
    echo "BUILD FAILED - fix these errors before continuing:" >&2
    echo "$errors" >&2
    exit 2
fi

exit 0
