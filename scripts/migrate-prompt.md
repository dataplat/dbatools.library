# dbatools PS1 → C# Binary Cmdlet Migration

You are an autonomous agent converting dbatools PowerShell commands into C# binary cmdlets. Each iteration, you convert ONE command, verify it builds, and stop.

## End State

dbatools becomes a **pure C# binary module**. No PS1 functions. Every command is a `[Cmdlet]` class in `c:\github\dbatools.library`.

## DENY LIST

```
git push
git --force / git push -f
git reset --hard
rm -rf
dotnet clean (use dotnet build instead)
```

## Your Process

### 0. Baseline Tests

Before making any changes, run the test suite to establish a baseline:

```bash
dotnet test project/dbatools.Tests/dbatools.Tests.csproj --verbosity quiet 2>&1 | tail -10
```

Record the pass/fail count for BOTH net472 and net8.0. ALL tests MUST pass on BOTH frameworks — zero failures, zero crashes. If the baseline has any failures on either framework, STOP and fix them before proceeding with the conversion. Do not ignore or skip failures on any framework.

#### Pester Baseline

If Pester integration tests exist for this command, establish a baseline BEFORE making any changes:

```bash
pwsh -NoProfile -Command '
    Import-Module c:\github\dbatools-ralph\dbatools.psm1 -Force
    . c:\github\dbatools-ralph\private\testing\Invoke-ManualPester.ps1
    Invoke-ManualPester -Path c:\github\dbatools-ralph\tests\{CommandName}.Tests.ps1 -TestIntegration
'
```

**Always use `pwsh -NoProfile`** to spawn a fresh process. For the baseline, the installed `dbatools.library` module is fine — a fresh process won't have the DLL locked even if another session does.

Record the pass/fail/skip counts. If no test file exists at `c:\github\dbatools-ralph\tests\{CommandName}.Tests.ps1`, note "No Pester tests" and continue — Pester testing steps later will be skipped.

If the Pester baseline itself has failures, note them — you are not responsible for pre-existing failures, but you must not add new ones.

### 1. Find Next Command

Read tracker files in `docs/plan/TRACKER-MIGRATE-*.md`. Find the FIRST command with status `PENDING` whose dependencies are all `DONE`.

If ALL commands across ALL trackers are DONE, create signal file and STOP:
```bash
touch docs/plan/.migration-complete
```

If the next PENDING command has unmet dependencies, skip to the next one that's ready.

### 2. Read the PS1 Source

Read the original PS1 file from `c:\github\dbatools-ralph\public\{CommandName}.ps1`.

**Extract EVERYTHING:**
- Every parameter: name, type, mandatory, position, pipeline binding, default, validation, aliases, parameter sets
- Every code path: if/elseif/else, switch, try/catch, foreach
- Every error: Stop-Function calls, what triggers them, -Continue or not
- Every output: properties added to objects, type names, conditional properties
- Every side effect: what gets modified, logged, cached
- Version-specific logic: SQL 2008 vs 2016 vs Azure checks
- ShouldProcess usage: target string, action string

### 3. Read Existing C# Patterns

Read these files to match existing conventions:
- `project/dbatools/Commands/DbaBaseCmdlet.cs` — base class API
- `project/dbatools/Commands/DbaInstanceCmdlet.cs` — instance cmdlet pattern
- Any existing cmdlet in `Commands/` that's similar to what you're converting

### 4. Convert to C# Binary Cmdlet

Create `project/dbatools/Commands/{Verb}{Noun}Command.cs`.

**Translation rules:**

| PS1 | C# |
|---|---|
| `function Get-DbaDatabase` | `[Cmdlet("Get", "DbaDatabase")]` |
| `[Parameter(Mandatory)]` | `[Parameter(Mandatory = true)]` |
| `[switch]$Force` | `public SwitchParameter Force { get; set; }` |
| `$SqlInstance` / `$SqlCredential` | Inherit `DbaInstanceCmdlet` (gives you these for free) |
| `[ValidateSet('A','B')]` | `[ValidateSet("A", "B")]` |
| `[Alias('OldName')]` | `[Alias("OldName")]` |
| `Write-Message -Level Verbose "msg"` | `WriteMessage(MessageLevel.Verbose, "msg")` |
| `Write-Message -Level Warning "msg"` | `WriteMessage(MessageLevel.Warning, "msg")` |
| `Stop-Function -Message "x" -Continue` | `StopFunction("x", ex, target); TestFunctionInterrupt(); continue;` |
| `Stop-Function -Message "x"` (no Continue) | `StopFunction("x", ex, target); return;` |
| `Test-Bound 'ParamName'` | `TestBound("ParamName")` |
| `$PSCmdlet.ShouldProcess(t, a)` | `ShouldProcess(t, a)` (add `SupportsShouldProcess = true` to `[Cmdlet]`) |
| `Write-Output $obj` | `WriteObject(obj)` |
| `"Value is $var"` | `String.Format("Value is {0}", var)` |
| `Test-FunctionInterrupt` | `TestFunctionInterrupt()` |
| `$PSBoundParameters.ContainsKey('X')` | `MyInvocation.BoundParameters.ContainsKey("X")` |

**Instance loop pattern** (most commands):
```csharp
protected override void ProcessRecord()
{
    foreach (var instance in SqlInstance)
    {
        try
        {
            // Connect, do work, WriteObject
        }
        catch (Exception ex)
        {
            StopFunction(String.Format("Failed for {0}", instance), ex, instance);
            TestFunctionInterrupt();
            continue;
        }
    }
}
```

**ShouldProcess pattern** (Set/Remove/New/Disable/Enable verbs):
```csharp
if (ShouldProcess(target, String.Format("Removing {0}", name)))
{
    // perform action
}
```

**Every `[Cmdlet]` class MUST have:**
- `/// <summary>` XML doc comment
- Inherit `DbaBaseCmdlet` or `DbaInstanceCmdlet` (NEVER `PSCmdlet` or `Cmdlet`)
- Use `WriteMessage` (NEVER `WriteVerbose`/`WriteWarning`/`WriteDebug`)
- Use `StopFunction` (NEVER `ThrowTerminatingError`)
- Use `String.Format` (NEVER `$"..."`)
- C# 7.3 only (NO nullable refs, NO `??=`, NO switch expressions, NO `using var`, NO ranges, NO static local functions)

### 5. Build and Deploy

#### Compile

```bash
dotnet build project/dbatools/dbatools.csproj
```

If it fails, fix errors and rebuild. Do not proceed until the build succeeds.

#### Dev-build a loadable module

The installed `dbatools.library` at `C:\Program Files\PowerShell\Modules\` may be locked by another session. **Never fight the lock.** Instead, build a complete loadable module into `artifacts/dbatools.library/` using the dev-build script:

```bash
pwsh -NoProfile -File build/build-dev.ps1
```

This runs `dotnet publish` (which includes all ~142 dependency DLLs — SMO, SqlClient, etc.), copies them alongside the freshly built `dbatools.dll`, and adds the module manifest/loader. The output at `artifacts/dbatools.library/` is a fully self-contained, loadable module.

The `artifacts/` directory is gitignored.

#### Load the dev-built module for Pester testing

When running Pester tests, **always spawn a fresh `pwsh -NoProfile` process** and import the dev-built library FIRST:

```powershell
Import-Module c:\github\dbatools.library\artifacts\dbatools.library\dbatools.library.psd1 -Force
Import-Module c:\github\dbatools-ralph\dbatools.psm1 -Force
```

Loading the dev-built library first satisfies dbatools-ralph's `RequiredModules = 'dbatools.library'` dependency with the freshly built version, so it won't try to load the installed (potentially locked) copy.

### 5b. Run Tests

Run the test suite after the build to verify your conversion doesn't break anything:

```bash
dotnet test project/dbatools.Tests/dbatools.Tests.csproj --verbosity quiet 2>&1 | tail -10
```

ALL tests MUST pass on BOTH net472 and net8.0. Compare against the baseline from Step 0. If any test fails on either framework, fix your code and re-run. Do not proceed until all tests pass on both frameworks.

### 5c. Write C# Unit Tests

Every converted command MUST have a corresponding unit test class. Create `project/dbatools.Tests/Commands/{Verb}{Noun}CommandTests.cs`.

**Pattern:** Follow existing MSTest conventions in the test project:
```csharp
using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class {Verb}{Noun}CommandTests
    {
        #region HelperMethodName
        [TestMethod]
        public void HelperMethodName_Scenario_ExpectedResult()
        {
            // Arrange, Act, Assert
        }
        #endregion
    }
}
```

**What to test (no live SQL Server connections):**

| Testable | Examples |
|----------|---------|
| Helper methods | String conversion, array detection, path manipulation, filtering logic |
| SQL query building | Verify generated SQL strings, batching, escaping, QUOTENAME usage |
| Output PSObject shape | Verify property names and types on constructed output objects |
| Parameter validation | Null/empty inputs, boundary conditions, type coercion |
| Error message formatting | String.Format patterns produce expected messages |
| Pure business logic | Date math, size conversions, sorting, comparisons |

**Do NOT test:** SMO/SQL connection operations, `ConnectInstance`, `ExecuteWithResults`, or anything requiring a live server.

**To make helpers testable:** Change `private static` helper methods in the command class to `internal static`. The test project has `InternalsVisibleTo` access. Example:

```csharp
// In the command class — internal so tests can call it directly
internal static string[] ConvertToStringArray(object input) { ... }
internal static bool IsArrayInput(object input) { ... }
```

**Minimum requirements:**
- At least 3 test methods per command (more for complex commands)
- Cover: happy path + at least one edge case + at least one error/null case
- If the command is a pure SMO wrapper with no extractable logic, write parameter validation and output construction tests with a `// Note: limited unit test coverage — command is primarily an SMO wrapper` comment
- C# 7.3 only (match the main project constraints)
- Use `Assert.AreEqual`, `Assert.IsTrue`, `Assert.IsNull`, `Assert.ThrowsException<>`, `CollectionAssert`

**Build and run:**
```bash
dotnet build project/dbatools.Tests/dbatools.Tests.csproj
dotnet test project/dbatools.Tests/dbatools.Tests.csproj --no-build --verbosity quiet 2>&1 | tail -5
```

All new tests must pass. Do not proceed until they do.

### 6. Quality Gates

Run review agents in two tiers. **Tier 1 agents always run. Tier 2 agents run ONLY when their trigger condition is met.** Skipping irrelevant Tier 2 agents is correct behavior — not cutting corners.

#### Tier 1: Core Gates (ALWAYS — launch in one parallel batch)

Launch all three of these agents in a **single message with three parallel Task calls**:

1. **Feature Parity** — `subagent_type="feature-parity-guardian"`, `model="sonnet"`
```
Task prompt: "Review the conversion of {CommandName} from {ps1_path} to {cs_path}.
Verify every parameter, code path, error handler, conditional branch, and output
property from the PS1 is present in the C#. Report any missing features."
```

2. **Regression Check** — `subagent_type="regression-sentinel"`, `model="sonnet"`
```
Task prompt: "Compare the original PS1 at {ps1_path} against the new C# at {cs_path}.
Check all parameter names, types, defaults, pipeline binding, output types, output
properties, error conditions, and ShouldProcess messages. Report any breaking changes."
```

3. **Best Practices** — `subagent_type="best-practices-reviewer"`, `model="sonnet"`
```
Task prompt: "Review {cs_path} for code quality: SOLID principles, error handling patterns,
performance (no allocations in loops, proper StringBuilder usage), thread safety of static
state access, and adherence to C# 7.3 constraints."
```

If any agent reports issues (parity below 95%, breaking changes, or critical code quality issues), fix them before proceeding.

#### Tier 2: Conditional Gates (check triggers BEFORE launching)

**Read your C# file and check each trigger condition below. Only launch agents whose trigger is met. Launch all applicable Tier 2 agents in a single parallel Task batch.**

**Security** — `subagent_type="security-auditor"`, `model="sonnet"`
- **TRIGGER**: C# file contains string literals with SQL keywords (`SELECT`, `INSERT`, `UPDATE`, `DELETE`, `CREATE`, `ALTER`, `EXEC`, `sp_`, `sys.`), OR handles credentials/passwords, OR builds connection strings
- **SKIP if**: Command is a pure SMO property reader with no SQL strings and no credential handling
```
Task prompt: "Audit {cs_path} for security vulnerabilities. Check every T-SQL query
for SQL injection (parameterized values, QUOTENAME for identifiers). Check credential
handling — no credentials in WriteMessage output or exception messages. Check connection
string construction."
```

**T-SQL Collation** — `subagent_type="tsql-collation-reviewer"`, `model="haiku"`
- **TRIGGER**: C# file contains string literals with SQL keywords (`SELECT`, `INSERT`, `UPDATE`, `DELETE`, `CREATE`, `ALTER`, `EXEC`, `sp_`, `sys.`, `INFORMATION_SCHEMA`)
- **SKIP if**: No embedded T-SQL queries in the C# file
```
Task prompt: "Review T-SQL in {cs_path} for collation safety. Check temp table joins to
catalog views for COLLATE DATABASE_DEFAULT, verify QUOTENAME usage, check C# string
comparisons of SQL metadata use OrdinalIgnoreCase."
```

**Microsoft SDK** — `subagent_type="microsoft-sdk-validator"`, `model="sonnet"`
- **TRIGGER**: C# file references SMO types beyond basic `Server` connection (e.g., `Database`, `Table`, `Index`, `Job`, `Login`, `BackupDevice`, `Endpoint`) OR uses `SqlCommand`/`SqlDataReader` directly
- **SKIP if**: Command only uses `ConnectInstance()` and reads simple server properties
```
Task prompt: "Validate SMO/SqlClient API usage in {cs_path}. Check property access
patterns, connection lifecycle, and version-specific API availability."
```

**Cross-Platform** — `subagent_type="xplat-compatibility-reviewer"`, `model="haiku"`
- **TRIGGER**: C# file uses file system paths (`Path.Combine`, `Directory.`, `File.`), process management (`Process.`), WMI (`ManagementObject`), Registry (`RegistryKey`), or P/Invoke (`DllImport`)
- **SKIP if**: Command has no file I/O, no OS-level operations, no platform-specific APIs
```
Task prompt: "Review {cs_path} for cross-platform compatibility. Check Path.Combine usage,
platform guards on Windows-only APIs, StringComparison.OrdinalIgnoreCase, and conditional
compilation for net472 vs net8.0."
```

**dbatools Spirit** — `subagent_type="dbatools-spirit-guardian"`, `model="sonnet"`
- **TRIGGER**: Command has user-facing UX decisions — default parameter values, auto-detection logic, error messages with fix suggestions, or is a frequently-used command verb (Get-Dba*, Test-Dba*, Set-Dba*)
- **SKIP if**: Command is a pure SMO property wrapper that reads one property and outputs it with no defaults or auto-detection
```
Task prompt: "Review {cs_path} for adherence to the dbatools 'it just works' philosophy.
Check that defaults are sensible, error messages explain WHY and suggest WHAT TO DO,
auto-detection is preserved, and a long-time user would not notice behavioral differences."
```

**Pester Compatibility** — `subagent_type="pester-test-guardian"`, `model="sonnet"`
- **TRIGGER**: Pester test file exists at `c:\github\dbatools-ralph\tests\{CommandName}.Tests.ps1`
- **SKIP if**: No Pester test file exists for this command
```
Task prompt: "Evaluate the Pester tests at c:\github\dbatools-ralph\tests\{CommandName}.Tests.ps1
for compatibility with the new C# binary cmdlet at {cs_path}. Identify any tests that
will break due to the conversion (changed output types, removed properties, different
error behavior). Report which tests need adaptation and suggest fixes."
```

**Unit Test Review** — `subagent_type="unit-test-guardian"`, `model="haiku"`
- **TRIGGER**: Always (reviews the unit test file from Step 5c)
```
Task prompt: "Review the unit tests at project/dbatools.Tests/Commands/{Verb}{Noun}CommandTests.cs
for the command converted at {cs_path}. Verify adequate coverage of testable pure logic,
MSTest pattern compliance, no live SQL dependencies, and minimum 3 test methods.
Report any untested extractable logic that should have tests."
```

Fix any issues reported by Tier 1 or Tier 2 agents before proceeding to Step 7.

### 7. Retire the PS1 Function

The C# cmdlet replaces the PS1 function. Both cannot coexist — PowerShell will error on duplicate command names. Perform these steps in the **dbatools repo** (`c:\github\dbatools-ralph`):

#### 7a. Archive the PS1 file

Move the PS1 to an `archive/` folder (gitignored — for reference only, the original is in git history):

```bash
mkdir -p c:/github/dbatools-ralph/archive
mv c:/github/dbatools-ralph/public/{CommandName}.ps1 c:/github/dbatools-ralph/archive/{CommandName}.ps1
```

#### 7b. Remove from dbatools FunctionsToExport

Edit `c:\github\dbatools-ralph\dbatools.psd1` — remove `'{CommandName}'` from the `FunctionsToExport` array.

#### 7c. Add to dbatools.library CmdletsToExport

Edit `c:\github\dbatools.library\dbatools.library.psd1` — add `'{CommandName}'` to the `CmdletsToExport` array. Keep the array sorted alphabetically.

#### 7d. Run Pester Integration Tests

**This step is MANDATORY if a Pester test file exists.** Skip only if Step 0 noted "No Pester tests."

Spawn a fresh PowerShell process to pick up the C# cmdlet (the PS1 is now archived). Import the **dev-built** `dbatools.library` (from Step 5) first so the freshly built DLL with your new cmdlet is loaded:

```bash
pwsh -NoProfile -Command '
    Import-Module c:\github\dbatools.library\artifacts\dbatools.library\dbatools.library.psd1 -Force
    Import-Module c:\github\dbatools-ralph\dbatools.psm1 -Force
    . c:\github\dbatools-ralph\private\testing\Invoke-ManualPester.ps1
    Invoke-ManualPester -Path c:\github\dbatools-ralph\tests\{CommandName}.Tests.ps1 -TestIntegration
'
```

**Always use `pwsh -NoProfile`** — never test in the current session where the installed DLL may be locked or a stale module is loaded. The dev-built module at `artifacts/dbatools.library/` contains your freshly compiled `dbatools.dll` plus all dependency DLLs.

Compare results against the Pester baseline from Step 0:
- **ALL tests MUST pass.** There are no pre-existing failures — if something fails, it's a real problem that must be fixed.
- A previously-passing test that now fails is a regression in your C# implementation — fix it.
- If any test fails, determine the root cause and fix it before proceeding.

If any baseline-passing test now fails:
1. Determine if the failure is a C# implementation issue or a test adaptation issue
2. If implementation issue: go back to Step 4 and fix the C# code, rebuild, re-run all quality gates
3. If test adaptation issue: use the **Task tool** with `subagent_type="pester-test-guardian"` to adapt the test minimally
4. Re-run Pester tests until all baseline-passing tests pass again

**Do NOT proceed to Step 8 until Pester tests match or exceed the baseline.**

If tests fail and cannot be fixed after 3 attempts, log failures to `c:/github/dbatools-ralph/tests/migration-failures/{CommandName}.md` with:
- Test name
- Failure message
- Whether it's an implementation or test issue
- What was attempted

Then STOP — do not mark DONE, do not commit.

### 8. Final C# Unit Test Run

Re-run the test suite one last time after all quality gate fixes:

```bash
dotnet test project/dbatools.Tests/dbatools.Tests.csproj --no-build --verbosity quiet 2>&1 | tail -5
```

Compare against the baseline from Step 0. All previously passing tests must still pass, plus your new command tests from Step 5c must pass. If not, fix and re-run before committing.

### 9. Update Tracker and Commit

1. Edit the tracker file: change status from `PENDING` to `DONE`
2. Fill in the C# File, Build, Parity, and Pester columns
3. Commit in **dbatools.library** repo:
```bash
cd c:/github/dbatools.library
git add project/dbatools/Commands/{Verb}{Noun}Command.cs
git add project/dbatools.Tests/Commands/{Verb}{Noun}CommandTests.cs
git add dbatools.library.psd1
git add docs/plan/TRACKER-MIGRATE-*.md
git commit -m "$(cat <<'EOF'
feat(migration): Convert {Command-Name} to C# binary cmdlet

- All parameters preserved
- All code paths implemented
- Build passes
- C# unit tests written and passing
- Pester integration tests pass (baseline maintained)
- Feature parity verified
- PS1 retired, cmdlet exported from dbatools.library

Co-Authored-By: Claude Opus 4.6 <noreply@anthropic.com>
EOF
)"
```

4. Commit in **dbatools** repo:
```bash
cd c:/github/dbatools-ralph
git add -u public/{CommandName}.ps1
git add dbatools.psd1
git commit -m "$(cat <<'EOF'
feat(migration): Retire {Command-Name} PS1 — now C# binary cmdlet

- Function removed from FunctionsToExport
- PS1 archived (C# implementation in dbatools.library)

Co-Authored-By: Claude Opus 4.6 <noreply@anthropic.com>
EOF
)"
cd c:/github/dbatools.library
```

5. **STOP** — do not continue to the next command.

## C# Reference: What's Available in DbaBaseCmdlet

```csharp
// From DbaBaseCmdlet:
protected void WriteMessage(MessageLevel level, string message)
protected void StopFunction(string message, Exception exception = null, object target = null)
protected bool TestBound(string parameterName)
protected void TestFunctionInterrupt()
public bool EnableException { get; set; }  // auto-provided

// From DbaInstanceCmdlet (extends DbaBaseCmdlet):
public DbaInstanceParameter[] SqlInstance { get; set; }
public PSCredential SqlCredential { get; set; }
```

## Exit Conditions

1. **Normal**: Converted one command, tests pass, marked DONE, committed → STOP
2. **All done**: All trackers show DONE → create signal file → STOP
3. **Blocked**: Dependency not met, no other commands ready → describe blocker → STOP
4. **Build failure**: Cannot fix after 3 attempts → describe error → STOP (do NOT mark DONE)
5. **Test regression**: C# unit tests that passed at baseline now fail and cannot be fixed → describe failures → STOP (do NOT mark DONE)
6. **Pester regression**: Pester integration tests that passed at baseline now fail and cannot be fixed → log to `tests/migration-failures/` → STOP (do NOT mark DONE)
