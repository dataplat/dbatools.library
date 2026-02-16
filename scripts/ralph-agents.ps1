<#
.SYNOPSIS
    Builds the --agents JSON for Ralph Wiggum migration scripts.

.DESCRIPTION
    Dynamically reads .claude/agents/*.md files, extracts YAML front-matter
    (name, description), and produces a compact JSON string suitable for
    the claude CLI --agents flag.

    Full agent prompts are NOT embedded (they would blow the 32K command-line
    limit). Instead, each agent's prompt tells it to read its instructions
    from the .claude/agents/{name}.md file.

.PARAMETER RepoRoot
    Root of the dbatools.library repository. Defaults to c:\github\dbatools.library.
#>

function Get-RalphAgentsJson {
    param(
        [string]$RepoRoot = 'c:\github\dbatools.library'
    )

    $agentsDir = Join-Path $RepoRoot '.claude' 'agents'
    $agents = @{}

    foreach ($file in Get-ChildItem -Path $agentsDir -Filter '*.md' | Where-Object { $_.Name -ne 'README.md' }) {
        $content = Get-Content $file.FullName -Raw

        # Parse YAML front-matter between --- delimiters
        if ($content -match '(?s)^---\r?\n(.+?)\r?\n---') {
            $yaml = $Matches[1]
            $name = $null
            $description = $null

            foreach ($line in ($yaml -split '\r?\n')) {
                if ($line -match '^name:\s*(.+)$') { $name = $Matches[1].Trim() }
                if ($line -match '^description:\s*(.+)$') { $description = $Matches[1].Trim() }
            }

            if ($name -and $description) {
                # Truncate description to 200 chars to save command-line space
                if ($description.Length -gt 200) {
                    $description = $description.Substring(0, 197) + '...'
                }
                $agents[$name] = @{
                    description = $description
                    prompt      = "Read your full instructions from .claude/agents/$($file.Name) and follow them exactly. You are operating in the dbatools.library repo at c:\github\dbatools.library."
                }
            }
        }
    }

    return ($agents | ConvertTo-Json -Depth 3 -Compress)
}
