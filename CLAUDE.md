# dbatools.library C# Style Guide for Claude Code

The .NET library that powers [dbatools](https://github.com/dataplat/dbatools), the community module for SQL Server professionals.

**Tech Stack**: C# (.NET Framework 4.7.2 + .NET 8.0), PowerShell module loader, MSTest.

## CRITICAL LANGUAGE VERSION RULE

### LangVersion 7.3 — NO C# 8+ FEATURES

**ABSOLUTE RULE**: This project targets `LangVersion 7.3`. NEVER use C# 8+ syntax.

```csharp
// FORBIDDEN — C# 8+ features
var x = obj?.Property ?? "default";   // null-coalescing assignment ??= is C# 8
string? nullable = null;              // nullable reference types (C# 8)
var range = array[1..^1];             // ranges/indices (C# 8)
using var stream = new FileStream(); // using declarations (C# 8)
var result = obj switch { ... };      // switch expressions (C# 8)
static int Add(int a, int b) => a+b; // static local functions (C# 8)

// FORBIDDEN — String interpolation (any version)
var msg = $"Hello {name}";            // NO — not allowed in this project

// CORRECT — Use String.Format
var msg = String.Format("Hello {0}", name);
```

## CRITICAL CMDLET RULES

### Base Class Requirement

**ABSOLUTE RULE**: All cmdlets MUST inherit from `DbaBaseCmdlet` or `DbaInstanceCmdlet`. NEVER inherit directly from `PSCmdlet` or `Cmdlet`.

```csharp
// CORRECT
[Cmdlet(VerbsCommon.Get, "DbaDatabase")]
public class GetDbaDatabase : DbaInstanceCmdlet { }

// WRONG — Will be rejected by hook
[Cmdlet(VerbsCommon.Get, "DbaDatabase")]
public class GetDbaDatabase : PSCmdlet { }
```

### Message and Error Handling

**CRITICAL**: Do NOT call `WriteVerbose`, `WriteWarning`, or `WriteDebug` directly in cmdlets — use `WriteMessage` instead. Do NOT call `ThrowTerminatingError` — use `StopFunction` instead.

```csharp
// CORRECT
WriteMessage(MessageLevel.Verbose, "Processing {0}", serverName);
StopFunction("Connection failed", exception);

// WRONG — Direct PS methods
WriteVerbose("Processing " + serverName);
ThrowTerminatingError(new ErrorRecord(...));
```

**Legacy exemptions** (DO NOT apply these rules to): `WriteMessageCommand`, `SetDbatoolsConfigCommand`, `ImportCommand`, `ReadXEvent`, `SelectDbaObject`.

### XML Documentation Required

All `[Cmdlet]` classes MUST have `/// <summary>` documentation.

## ASSEMBLY LOADING

**NEVER** use `Assembly.LoadFile()`. The module handles assembly loading via a custom `Redirector` class and binding redirects.

## Dev Commands

```bash
# Build the library
dotnet build project/dbatools/dbatools.csproj

# Run tests
dotnet test project/dbatools.Tests/dbatools.Tests.csproj

# Build the standalone CSV NuGet package
dotnet build project/Dataplat.Dbatools.Csv/Dataplat.Dbatools.Csv.csproj

# Build everything
dotnet build project/dbatools.sln
```

## Project Structure

```
dbatools.library/
├── project/
│   ├── dbatools/                  # Main C# library
│   │   ├── Csv/                   # CSV reader/writer (also published as NuGet)
│   │   ├── Computer/              # Disk/drive types
│   │   ├── Configuration/         # Config system + ConfigurationHost
│   │   ├── Connection/            # Connection management
│   │   ├── Database/              # BackupHistory, Dependency
│   │   ├── Discovery/             # SQL Server browser/discovery
│   │   ├── Exceptions/            # Custom exceptions
│   │   ├── General/               # ExecutionMode
│   │   ├── IO/                    # ProgressStream
│   │   ├── Maintenance/           # Background maintenance tasks
│   │   ├── Message/               # Messaging system (LogHost, MessageHost)
│   │   ├── Parameter/             # DbaInstanceParameter, DbaCredentialParameter, etc.
│   │   ├── Runspace/              # RunspaceHost, RunspaceContainer
│   │   ├── TabExpansion/          # Tab completion (TEPP)
│   │   ├── TypeConversion/        # Type converters
│   │   └── Utility/               # DbaDateTime, DbaTimeSpan, etc.
│   ├── Dataplat.Dbatools.Csv/     # Standalone NuGet package (links to Csv/ source)
│   └── dbatools.Tests/            # MSTest unit tests
├── dbatools.library.psd1          # PowerShell module manifest
├── dbatools.library.psm1          # Module loader (assembly loading, binding redirects)
├── benchmarks/                    # BenchmarkDotNet CSV benchmarks
└── artifacts/lib/                 # Build output
```

## Multi-Framework Targeting

The library targets **both** `net472` (Windows PowerShell 5.1) and `net8.0` (PowerShell 7+).

- `net472` uses GAC reference for `System.Management.Automation` on Windows
- `net472` uses `PowerShellStandard.Library` on non-Windows (CI)
- `net8.0` uses `Microsoft.PowerShell.SDK 7.4.x`
- Some packages differ by framework (e.g., `System.Threading.Tasks.Dataflow` versions)

## Dependency Version Constraints

**Read before upgrading any package**: Several packages have hard version ceilings due to runtime compatibility issues.

| Package | Ceiling | Why |
|---------|---------|-----|
| Microsoft.Data.SqlClient | 6.x only | DacFx/SMO compiled against 6.x; 7.x causes type-load failures |
| Microsoft.PowerShell.SDK | 7.4.x only | 7.5+ requires net9.0 target change |
| MSTest.* | 3.x only | 4.x drops `Assert.ThrowsException<T>()` on net472 |
| Microsoft.NET.Test.Sdk | 17.x only | 18.x aligns with MSTest 4.x ecosystem |

For full details and current versions, see the [dependency constraints memory](file://memory/dependency_constraints.md).

## Architecture — Static Hubs

The library uses singleton "host" classes for cross-cutting concerns:

- `MessageHost` — message configuration and event subscriptions
- `LogHost` — log entry storage and configuration
- `ConfigurationHost` — configuration values and handlers
- `ConnectionHost` — connection management
- `RunspaceHost` — runspace container registry
- `TabExpansionHost` — tab completion registration

## CSV Library (Dataplat.Dbatools.Csv)

The CSV library is both:
1. Part of the main `dbatools` assembly (under `project/dbatools/Csv/`)
2. Published as a standalone NuGet package (via `project/Dataplat.Dbatools.Csv/` which links the same source files)

When modifying CSV code, changes apply to both. The standalone package has its own:
- [README](project/Dataplat.Dbatools.Csv/README.md) — full API documentation
- [CHANGELOG](project/Dataplat.Dbatools.Csv/CHANGELOG.md) — version history
- [Migration guide](project/Dataplat.Dbatools.Csv/MIGRATING-FROM-LUMENWORKS.md) — for LumenWorks users

## Testing

- Framework: **MSTest** (not xUnit, not NUnit)
- Test project references the main `dbatools.csproj`
- Run with: `dotnet test project/dbatools.Tests/dbatools.Tests.csproj`
- MSTest 3.11+ has a `MessageLevel` type that conflicts with `Dataplat.Dbatools.Message.MessageLevel` — use a using alias in affected test files
- **Windows + net8.0 test failures**: Some tests fail under `net8.0` on Windows due to PowerShell SDK assembly conflicts in the test host. These are expected — only `net472` test results matter on Windows. CI runs the net8.0 tests on Linux where they pass cleanly.

## Hooks (Enforced Automatically)

Hooks enforce these rules — if a hook blocks you, fix the violation:

- **C# rules** (`enforce-cs-rules.sh`): Base class, LangVersion 7.3, no Assembly.LoadFile, no direct Write*, no ThrowTerminatingError, XML docs on cmdlets, no string interpolation
- **PSD1 rules** (`enforce-psd1-rules.sh`): No wildcard exports in module manifest
- **Build check** (`check-build.sh`): Auto-builds after any `.cs` file edit
- **File length check** (`stop-file-length.sh`, Stop hook): Tracked text/source/docs/scripts/config files must stay at or below 400 physical lines; split files structurally rather than growing them.

All hooks use `set -eu` (not `pipefail` — unsupported on Windows sh).

## Companion Repositories

| Repo | Purpose | Location |
|------|---------|----------|
| [dbatools](https://github.com/dataplat/dbatools) | PowerShell module (consumes this library) | `c:\github\dbatools` |
| dbatools.pro | Fleet management platform (uses dbatools) | `c:\github\dbatools.pro` |

## VERIFICATION CHECKLIST

**Before submitting any C# change:**
- [ ] No C# 8+ syntax (no `??=`, no nullable refs, no ranges, no `using` declarations, no switch expressions)
- [ ] No `$"..."` string interpolation — use `String.Format`
- [ ] Cmdlets inherit `DbaBaseCmdlet` or `DbaInstanceCmdlet`
- [ ] No direct `WriteVerbose`/`WriteWarning`/`WriteDebug` — use `WriteMessage`
- [ ] No `ThrowTerminatingError` — use `StopFunction`
- [ ] `[Cmdlet]` classes have `/// <summary>` docs
- [ ] No `Assembly.LoadFile()`
- [ ] No tracked text/source/docs/scripts/config file exceeds 400 physical lines
- [ ] Build succeeds: `dotnet build project/dbatools/dbatools.csproj`
- [ ] Tests pass: `dotnet test project/dbatools.Tests/dbatools.Tests.csproj`
