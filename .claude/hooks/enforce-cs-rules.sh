#!/usr/bin/env bash
# PreToolUse hook: Consolidated C# rules enforcement
# Combines: base-class, lang-version, assembly-loadfile, writestream, throw-terminating, doc-comments
# Blocks (exit 2) on any violation
set -eu

input=$(cat)

# Parse JSON with node — node writes content to its own temp file and tells us the path
eval "$(node -e "
const os = require('os'), fs = require('fs'), path = require('path');
const j = JSON.parse(fs.readFileSync(0, 'utf8'));
const t = j.tool_input || {};
const fp = t.file_path || '';
const c = t.new_string || t.content || '';
if (fp && c) {
  const tmp = path.join(os.tmpdir(), 'claude_hook_' + process.pid + '.txt');
  fs.writeFileSync(tmp, c);
  console.log('file_path=' + JSON.stringify(fp));
  console.log('filename=' + JSON.stringify(path.basename(fp)));
  console.log('tmp=' + JSON.stringify(tmp));
} else {
  console.log('file_path=');
}
" <<< "$input")"

[[ -z "${file_path:-}" ]] && exit 0
[[ "$file_path" != *.cs ]] && exit 0
[[ -z "${tmp:-}" || ! -f "$tmp" ]] && exit 0

tmpnc="${tmp}.nc"
trap 'rm -f "$tmp" "$tmpnc"' EXIT

violations=()

# Classify the file
is_in_commands=false
[[ "$file_path" =~ (Commands|Internal)[/\\] ]] && is_in_commands=true

is_base_class=false
[[ "$filename" =~ ^(DbaBaseCmdlet|DbaInstanceCmdlet)\.cs$ ]] && is_base_class=true

is_legacy=false
[[ "$filename" =~ ^(WriteMessageCommand|SetDbatoolsConfigCommand|ImportCommandCommand|ReadXEventCommand|SelectDbaObjectCommand)\.cs$ ]] && is_legacy=true

is_message_cmd=false
[[ "$filename" == "WriteMessageCommand.cs" ]] && is_message_cmd=true

# Strip single-line comments for pattern checks
grep -v -E '^\s*(//|/\*|\*)' "$tmp" > "$tmpnc" 2>/dev/null || true

# ============================================================
# Rule 1: Base class enforcement (Commands/ and Internal/ only)
# ============================================================
if $is_in_commands && ! $is_base_class && ! $is_legacy; then
    if grep -qP 'class\s+\w+\s*:\s*(PS)?Cmdlet\b' "$tmpnc" 2>/dev/null; then
        violations+=("Must inherit DbaBaseCmdlet or DbaInstanceCmdlet, not PSCmdlet/Cmdlet directly")
    fi
fi

# ============================================================
# Rule 2: LangVersion 7.3 (no C# 8+ syntax)
# ============================================================
if grep -q '#nullable' "$tmp" 2>/dev/null; then
    violations+=("Nullable reference types (#nullable enable) are C# 8+")
fi

if grep -qP '\bswitch\s*\{[^}]*=>' "$tmp" 2>/dev/null; then
    violations+=("Switch expressions (switch { pattern => value }) are C# 8+")
fi

if grep -qF '??=' "$tmp" 2>/dev/null; then
    violations+=("Null-coalescing assignment (??=) is C# 8+")
fi

if grep -qP '\[\d*\.\.\d*\]' "$tmp" 2>/dev/null; then
    violations+=("Range operator (..) is C# 8+")
fi

if grep -qP '^\s*using\s+var\s+\w+\s*=' "$tmp" 2>/dev/null; then
    violations+=("Using declarations (using var x = ...) are C# 8+ - use using (var x = ...) { } instead")
fi

# Static local functions: 'static type name(' without access modifier
if grep -P '^\s*static\s+(void|int|string|bool|var|float|double|long|object)\s+\w+\s*\(' "$tmpnc" 2>/dev/null \
    | grep -qvP '^\s*(public|private|protected|internal)\s'; then
    violations+=("Static local functions are C# 8+")
fi

# Default interface methods
if grep -qPz 'interface\s+\w+[^{]*\{[^}]*\b(public|private|protected)\s+(void|int|string|bool)' "$tmp" 2>/dev/null; then
    violations+=("Default interface implementations are C# 8+")
fi

# ============================================================
# Rule 7: No string interpolation (project style: use String.Format)
# $"..." is valid C# 6 (within LangVersion 7.3) but project convention
# is String.Format() for consistency with the PS1 migration pattern.
# ============================================================
if grep -qP '\$"' "$tmpnc" 2>/dev/null; then
    violations+=("Use String.Format() instead of \$\"...\" string interpolation - project convention for consistency")
fi

# ============================================================
# Rule 3: No Assembly.LoadFile()
# ============================================================
if grep -qP 'Assembly\.LoadFile\s*\(' "$tmpnc" 2>/dev/null; then
    violations+=("Never use Assembly.LoadFile() - causes type identity issues. Use Assembly.LoadFrom() or ALC")
fi

# ============================================================
# Rule 4: No direct WriteVerbose/WriteWarning/WriteDebug in cmdlets
# ============================================================
if $is_in_commands && ! $is_base_class && ! $is_message_cmd; then
    if grep -qP '\.(WriteVerbose|WriteWarning|WriteDebug)\s*\(' "$tmpnc" 2>/dev/null; then
        violations+=("Use WriteMessage() instead of WriteVerbose/WriteWarning/WriteDebug - direct stream writes bypass the dbatools message system")
    fi
fi

# ============================================================
# Rule 5: No ThrowTerminatingError in cmdlets
# ============================================================
if $is_in_commands && ! $is_base_class; then
    if grep -qP '\.ThrowTerminatingError\s*\(' "$tmpnc" 2>/dev/null; then
        violations+=("Use StopFunction() instead of ThrowTerminatingError() - use the DbaBaseCmdlet error handling")
    fi
fi

# ============================================================
# Rule 6: Require XML doc comment on [Cmdlet] classes
# Uses awk to walk backward from [Cmdlet( and verify /// <summary>
# ============================================================
doc_violation=$(awk '
{
    lines[NR] = $0
    trimmed = $0
    gsub(/^[[:space:]]+/, "", trimmed)

    if (trimmed ~ /^\[Cmdlet[[:space:]]*\(/) {
        found = 0
        for (j = NR - 1; j >= 1 && j >= NR - 5; j--) {
            prev = lines[j]
            gsub(/^[[:space:]]+/, "", prev)
            if (prev ~ /\/\/\/[[:space:]]*<summary>/) { found = 1; break }
            if (prev != "" && prev !~ /^\/\/\// && prev !~ /^\[/ && prev !~ /^\*/) break
        }
        if (!found) { print "Missing /// <summary> XML doc comment above [Cmdlet] attribute - required for help generation"; exit 0 }
    }
}
' "$tmp")

if [[ -n "$doc_violation" ]]; then
    violations+=("$doc_violation")
fi

# ============================================================
# Report violations
# ============================================================
if [[ ${#violations[@]} -gt 0 ]]; then
    msg="BLOCKED: C# rule violation(s) in ${filename}:"
    for v in "${violations[@]}"; do
        msg+=$'\n'"$v"
    done
    echo "$msg" >&2
    exit 2
fi

exit 0
