#!/bin/bash
# Block git commit/push if tests haven't been run in this session.
# Reads PreToolUse JSON from stdin, checks tool_input.command.

INPUT=$(cat)
COMMAND=$(echo "$INPUT" | jq -r '.tool_input.command // ""')

# Match git commit or git push (with any flags/args after)
if echo "$COMMAND" | grep -qE '(^|\&\&\s*)git (commit|push)'; then
    echo "BLOCKED: Run tests locally before committing or pushing." >&2
    echo "  dotnet test project/dbatools.Tests/dbatools.Tests.csproj" >&2
    echo "  pwsh -c \"& scripts/ralph-test-runner.ps1 -Path '<relevant test>'\"" >&2
    exit 2
fi

exit 0
