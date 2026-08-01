# dbatools.library

The .NET library that powers [dbatools](https://github.com/dataplat/dbatools), the community
module for SQL Server professionals.

**Stack**: C# targeting both `net472` (Windows PowerShell 5.1) and `net8.0` (PowerShell 7+),
PowerShell module loader, MSTest.

## Two language regimes — which applies depends on the directory

Normative source: `dbatools/migration/specs/architecture.md` §11.

### `project/dbatools/` (shared runtime) and `project/Dataplat.Dbatools.Csv/` — LangVersion 7.3

No C# 8+ syntax, and **no string interpolation at all**:

```csharp
// FORBIDDEN here
string? nullable = null;              // nullable reference types (C# 8)
x ??= "default";                      // null-coalescing assignment (C# 8)
var range = array[1..^1];             // ranges/indices (C# 8)
using var stream = new FileStream();  // using declarations (C# 8)
var result = obj switch { ... };      // switch expressions (C# 8)
static int Add(int a, int b) => a+b;  // static local functions (C# 8)
var msg = $"Hello {name}";            // interpolation — use String.Format("Hello {0}", name)
```

### `project/dbatools.<module>/` satellites — modern C# 12, deliberately not 7.3

Every satellite (agent, computer, core, database, hadr, maintenance, performance, replication,
security, ssis, xevents) sets `<LangVersion>12</LangVersion>` + `TreatWarningsAsErrors` per the
migration architecture spec. **Do not "fix" them back to 7.3**, and do not flag modern syntax
there as a style violation.

- **Allowed**: file-scoped namespaces, string interpolation, pattern matching, target-typed `new`,
  `using` declarations, switch expressions. `#nullable enable` is the mandatory first line of
  every `Commands/` file.
- **Still banned** (net472 + SMA 3.0.0.0 compatibility, architecture.md §11): `async`/`await`/
  `Task.Run`, `record`, `init`/`required` members, ranges/indices, `ArgumentCompleterAttribute`,
  class-level `[Alias]` on cmdlets, `Console.*`/`Host.UI.*`, static mutable state in cmdlet
  classes, LINQ in hot loops, `Assembly.LoadFile`.

## Cmdlet rules

- Cmdlets inherit `DbaBaseCmdlet` or `DbaInstanceCmdlet` — never `PSCmdlet` or `Cmdlet` directly.
- Use `WriteMessage(MessageLevel.Verbose, "Processing {0}", serverName)`, not `WriteVerbose` /
  `WriteWarning` / `WriteDebug`. Use `StopFunction(...)`, not `ThrowTerminatingError`.
- `[Cmdlet]` classes need `/// <summary>` docs.
- Never `Assembly.LoadFile()` — the module loads assemblies through its `Redirector` class and
  binding redirects.

**Legacy exemptions** from the message/error rules: `WriteMessageCommand`,
`SetDbatoolsConfigCommand`, `ImportCommand`, `ReadXEvent`, `SelectDbaObject`.

Shared cmdlet behavior belongs in the satellite's own `Commands/NestedCommand.*.cs` partial, not in
`DbaBaseCmdlet` — that class is shared runtime and every satellite pays for a change to it.

## Where things live in `project/dbatools/`

Only the folders whose names undersell or misdescribe them. The rest — `Csv/` (50 files, the
biggest), `Computer/`, `Configuration/`, `Database/`, `Exceptions/`, `Runspace/`, `Maintenance/`,
`TypeConversion/` — hold what you'd expect.

| Folder | Holds | Why it's listed |
|---|---|---|
| `Commands/` | `DbaBaseCmdlet`, `DbaInstanceCmdlet`, and the five legacy-exempt cmdlets | The base classes every cmdlet inherits |
| `Parameter/` | `DbaInstanceParameter`, `DbaCredentialParameter`, `DbaDatabaseParameter`, `DbaCmConnectionParameter` (18 files) | The input-coercion types, not `[Parameter]` attributes |
| `dbaSystem/` | `DebugHost`, `DmfLibrary`, `ReplicationLibrary`, `DbaErrorRecord` | Lowercase, and nothing about the name suggests any of it — `DmfLibrary` is the PBM/Dmf resolution path |
| `Message/` | `MessageLevel`, `DbatoolsException`, `LogEntry`, `CallStack` (16 files) | Where `WriteMessage`'s plumbing and the exception types live |
| `Utility/` | `DbaDate`/`DbaDateTime`/`DbaTimeSpan`, `ByteHex`, `CollationSensitiveFilter` (24 files) | Junk-drawer name over real types — check here before writing a helper |
| `Connection/` | `ConnectionHost`, Entra auth, CIM services (21 files) | Larger and broader than "connection" implies |
| `Discovery/` | SQL Browser replies, instance scan/availability types | Network discovery, not object enumeration |
| `TabExpansion/` | `TabExpansionHost`, `ScriptContainer` | This is TEPP |
| `General/` | `ExecutionMode` — one file | Name conveys nothing |
| `IO/` | `ProgressStream` — one file | Name conveys nothing |
| `Validation/` | `LinkedServerResult` — one file | Not a validation framework |

Satellite cmdlets live in `project/dbatools.<module>/Commands/` instead, one folder per module.

## Dev commands

```bash
dotnet build project/dbatools.sln                              # everything
dotnet build project/dbatools/dbatools.csproj                  # shared runtime only
dotnet test  project/dbatools.Tests/dbatools.Tests.csproj      # MSTest (not xUnit, not NUnit)
```

Always build `dbatools.sln` before finishing a C# change — there is no auto-build hook;
enforcement is `TreatWarningsAsErrors` in the satellite csprojs plus the migration gate's build
step.

## Gotchas

**Windows + net8.0 test failures are expected.** PowerShell SDK assembly conflicts in the test
host — only `net472` results matter on Windows. CI runs net8.0 on Linux, where it is clean.

**MSTest 3.11+ ships a `MessageLevel`** that collides with `Dataplat.Dbatools.Message.MessageLevel`
— use a `using` alias in affected test files.

**Package version ceilings** — read before upgrading anything:

| Package | Ceiling | Why |
|---------|---------|-----|
| Microsoft.Data.SqlClient | 7.x | 7.0.1 is validated with the pinned SMO and DacFx packages; revalidate their loaders before upgrading |
| Microsoft.PowerShell.SDK | 7.4.x | 7.5+ requires a net9.0 target change |
| MSTest.* | 3.x | 4.x drops `Assert.ThrowsException<T>()` on net472 |
| Microsoft.NET.Test.Sdk | 17.x | 18.x aligns with the MSTest 4.x ecosystem |

Some packages differ by framework (e.g. `System.Threading.Tasks.Dataflow`). On non-Windows,
`net472` uses `PowerShellStandard.Library` instead of the GAC `System.Management.Automation`.

**Cross-cutting state lives in singleton hosts**, not in cmdlets: `MessageHost`, `LogHost`,
`ConfigurationHost`, `ConnectionHost`, `RunspaceHost`, `TabExpansionHost`.

**The CSV code has two homes.** `project/dbatools/Csv/` is compiled into the main assembly, and
`project/Dataplat.Dbatools.Csv/` links the same source files to publish a standalone NuGet
package. A change to one is a change to both; the package carries its own `README.md`,
`CHANGELOG.md`, and `MIGRATING-FROM-LUMENWORKS.md`.

## Hooks that will block you

Stop hooks, so they fire at the end of a turn — fix the violation, don't route around it.

- `enforce-cs-rules.sh` — the cmdlet and language rules above, including *new* string interpolation
  in the 7.3 projects (pre-existing occurrences are grandfathered; satellites are C# 12 and
  interpolation is fine there).
- `enforce-psd1-rules.sh` — no wildcard exports in module manifests.
- `stop-file-length.sh` — tracked text/source/docs/scripts/config files stay at or below 400
  physical lines. Split structurally rather than growing a file.

All hooks use `set -eu` — not `pipefail`, which is unsupported on Windows `sh`.

## Tone: warm and short

Talk like a friendly colleague who is busy — kind, plain, and finished in a few lines. The warmth
is in the wording, not in extra words.

**Prose**: lead with the answer or the result, and add detail only when it changes what happens
next. No preamble, no restating the request back, no closing paragraph that summarizes the opening
one. Say "I'm not sure" once instead of hedging three times.

**Comments**: explain *why*, never *what*. If the code already says it, delete the comment. No
banners, no `// Step 1:` narration, no comment above a method that restates its signature. A
version quirk, a non-obvious workaround, a constraint that cost real debugging — those earn a line.
The mandatory `/// <summary>` docs on `[Cmdlet]` classes are not covered by this.

## Companions

This repo is one of three in the dbatools 3.0 migration; the campaign's coordination repo and
issue queue is `c:\github\dbatools\migration` (read its `CLAUDE.md` before working a row).
`c:\github\dbatools` is the PowerShell module that consumes this library. Both code repos are on
branch `libmigration`. `c:\github\dbatools.pro` is a separate fleet-management platform that uses
dbatools.
