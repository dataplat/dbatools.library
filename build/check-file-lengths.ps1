param(
    [int]$MaximumLines = 400
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (& git rev-parse --show-toplevel).Trim()
if (-not $repoRoot) {
    throw "Unable to determine repository root."
}

$binaryExtensions = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
@(
    ".7z",
    ".dll",
    ".exe",
    ".gif",
    ".gz",
    ".ico",
    ".jpeg",
    ".jpg",
    ".nupkg",
    ".pdb",
    ".pdf",
    ".png",
    ".snupkg",
    ".ttf",
    ".vsix",
    ".woff",
    ".woff2",
    ".zip"
) | ForEach-Object { $null = $binaryExtensions.Add($PSItem) }

$skippedPrefixes = @(
    "artifacts/",
    "benchmarks/CsvBenchmarks/bin/",
    "benchmarks/CsvBenchmarks/obj/",
    "project/Dataplat.Dbatools.Csv/bin/",
    "project/Dataplat.Dbatools.Csv/obj/",
    "project/dbatools.Tests/bin/",
    "project/dbatools.Tests/obj/",
    "project/dbatools/bin/",
    "project/dbatools/obj/",
    "var/misc/",
    "var/third-party-licenses/"
)

$allowList = @{}
$violations = New-Object System.Collections.Generic.List[object]

Push-Location $repoRoot
try {
    foreach ($path in (& git ls-files)) {
        $normalizedPath = $path.Replace("\", "/")

        $skip = $false
        foreach ($prefix in $skippedPrefixes) {
            if ($normalizedPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
                $skip = $true
                break
            }
        }
        if ($skip) {
            continue
        }

        if ($allowList.ContainsKey($normalizedPath)) {
            continue
        }

        $extension = [IO.Path]::GetExtension($normalizedPath)
        if ($binaryExtensions.Contains($extension)) {
            continue
        }

        $fullPath = Join-Path $repoRoot $normalizedPath
        if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
            continue
        }

        $bytes = [IO.File]::ReadAllBytes($fullPath)
        if ($bytes.Length -gt 0 -and [Array]::IndexOf($bytes, [byte]0) -ge 0) {
            continue
        }

        $text = [Text.Encoding]::UTF8.GetString($bytes)
        if ($bytes.Length -eq 0) {
            $lineCount = 0
        } else {
            $lineCount = ([regex]::Matches($text, "`n")).Count
            if (-not $text.EndsWith("`n")) {
                $lineCount++
            }
        }

        if ($lineCount -gt $MaximumLines) {
            $violations.Add([pscustomobject]@{
                Path = $normalizedPath
                Lines = $lineCount
            })
        }
    }
} finally {
    Pop-Location
}

if ($violations.Count -gt 0) {
    Write-Host "Tracked text files exceed $MaximumLines physical lines:" -ForegroundColor Red
    $violations |
        Sort-Object -Property Lines, Path -Descending |
        ForEach-Object {
            Write-Host ("{0,5}  {1}" -f $PSItem.Lines, $PSItem.Path) -ForegroundColor Red
        }
    exit 1
}

Write-Host "All tracked text files are at or below $MaximumLines physical lines." -ForegroundColor Green
