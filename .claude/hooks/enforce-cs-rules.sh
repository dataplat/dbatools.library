#!/usr/bin/env bash
set -eu

# C# rules per CLAUDE.md, scoped per project (normative source:
# dbatools/migration/specs/architecture.md section 11):
#   - project/dbatools/ + project/Dataplat.Dbatools.Csv/ = LangVersion 7.3 regime;
#     NEW string interpolation is blocked (existing occurrences are grandfathered).
#   - project/dbatools.<module>/ satellites = C# 12 regime; interpolation is FINE there,
#     but async/await/Task.Run and ArgumentCompleter attributes are banned (SMA 3.0.0.0).
#   - Cmdlet code everywhere: DbaBaseCmdlet/DbaInstanceCmdlet base only, no direct
#     WriteVerbose/WriteWarning/WriteDebug, no ThrowTerminatingError, XML docs required.
#   - Assembly.LoadFile banned repo-wide.

repo_root=$(git rev-parse --show-toplevel 2>/dev/null || true)
if [ -z "$repo_root" ]; then
    exit 0
fi
cd "$repo_root"

violations=""
add() { violations="${violations}  $1"$'\n'; }

# Legacy exemptions (CLAUDE.md): base classes + the five pre-existing cmdlets.
legacy_re='(DbaBaseCmdlet|DbaInstanceCmdlet|WriteMessageCommand|SetDbatoolsConfigCommand|ImportCommandCommand|ReadXEventCommand|SelectDbaObjectCommand)'

while IFS= read -r path; do
    [ -n "$path" ] || continue
    norm=${path//\\//}
    case "$norm" in
        *.cs) ;;
        *) continue ;;
    esac
    case "$norm" in
        */bin/*|*/obj/*|artifacts/*|project/dbatools.Tests/*) continue ;;
    esac
    [ -f "$norm" ] || continue

    if grep -qE 'Assembly\.LoadFile' "$norm"; then
        add "Assembly.LoadFile banned (module loader/Redirector owns loading): $norm"
    fi

    case "$norm" in
        project/dbatools/Commands/*|project/dbatools.*/Commands/*)
            base=$(basename "$norm")
            if ! [[ "$base" =~ $legacy_re ]]; then
                if grep -qE 'class[[:space:]]+[A-Za-z0-9_]+[[:space:]]*:[[:space:]]*(PSCmdlet|Cmdlet)\b' "$norm"; then
                    add "cmdlet inherits PSCmdlet/Cmdlet directly - use DbaBaseCmdlet/DbaInstanceCmdlet: $norm"
                fi
                if grep -q 'ThrowTerminatingError' "$norm"; then
                    add "ThrowTerminatingError banned - use StopFunction: $norm"
                fi
                if grep -qE '(^|[^.[:alnum:]_"])(WriteVerbose|WriteWarning|WriteDebug)[[:space:]]*\(' "$norm"; then
                    add "direct WriteVerbose/WriteWarning/WriteDebug banned - use WriteMessage: $norm"
                fi
            fi
            if grep -q '\[Cmdlet(' "$norm" && ! grep -q '<summary>' "$norm"; then
                add "[Cmdlet] class lacks /// <summary> docs: $norm"
            fi
            ;;
    esac

    case "$norm" in
        project/dbatools.*/*)
            if grep -qE '\basync[[:space:]]+(Task|ValueTask|void)\b|\bawait[[:space:]]|Task\.Run' "$norm"; then
                add "async/await/Task.Run banned in satellites (BP-003; StopProcessing is the cancellation model): $norm"
            fi
            if grep -q 'ArgumentCompleter' "$norm"; then
                add "ArgumentCompleter attributes banned (absent from SMA 3.0.0.0; use the TEPP module-loader bridge): $norm"
            fi
            ;;
    esac
done < <(git ls-files --cached --others --exclude-standard)

# NEW string interpolation in the LangVersion 7.3 projects: added lines in tracked files...
new_interp=$(git diff HEAD --unified=0 -- 'project/dbatools/*.cs' 'project/Dataplat.Dbatools.Csv/*.cs' 2>/dev/null | grep -E '^\+[^+].*\$"' || true)
if [ -n "$new_interp" ]; then
    add "NEW string interpolation added in a LangVersion 7.3 project - use String.Format (existing occurrences are grandfathered, do not add more)"
fi
# ...and anywhere in brand-new untracked files there.
while IFS= read -r path; do
    [ -n "$path" ] || continue
    norm=${path//\\//}
    case "$norm" in
        project/dbatools/*.cs|project/Dataplat.Dbatools.Csv/*.cs) ;;
        *) continue ;;
    esac
    case "$norm" in
        */bin/*|*/obj/*) continue ;;
    esac
    [ -f "$norm" ] || continue
    if grep -q '\$"' "$norm"; then
        add "string interpolation in new LangVersion 7.3 project file - use String.Format: $norm"
    fi
done < <(git ls-files --others --exclude-standard)

if [ -n "$violations" ]; then
    {
        echo "C# rule violations (CLAUDE.md / architecture.md section 11). Fix before finishing:"
        echo ""
        printf "%s" "$violations"
    } >&2
    exit 2
fi

exit 0
