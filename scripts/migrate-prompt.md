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
dotnet test project/dbatools.Tests/dbatools.Tests.csproj --no-build --verbosity quiet 2>&1 | tail -5
```

Record the pass/fail count. Any test that passes now MUST still pass after your conversion. If the baseline itself has failures, note them — you are not responsible for pre-existing failures, but you must not add new ones.

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

### 5. Build

```bash
dotnet build project/dbatools/dbatools.csproj
```

If it fails, fix errors and rebuild. Do not proceed until the build succeeds.

### 5b. Run Tests

Run the test suite after the build to verify your conversion doesn't break anything:

```bash
dotnet test project/dbatools.Tests/dbatools.Tests.csproj --no-build --verbosity quiet 2>&1 | tail -5
```

Compare against the baseline from Step 0. If any test that previously passed now fails, fix your code and re-run. Do not proceed until the test count is equal to or better than baseline.

### 6. Quality Gate — Feature Parity

Use the **Task tool** with `subagent_type="feature-parity-guardian"` to perform an exhaustive feature parity check:

```
Task prompt: "Review the conversion of {CommandName} from {ps1_path} to {cs_path}.
Verify every parameter, code path, error handler, conditional branch, and output
property from the PS1 is present in the C#. Report any missing features."
```

If the agent reports ANY missing features (parity score below 95%), go back to Step 4 and add them. Do not proceed until parity is achieved.

### 7. Quality Gate — Security

Use the **Task tool** with `subagent_type="security-auditor"` to audit your code:

```
Task prompt: "Audit {cs_path} for security vulnerabilities. Check every T-SQL query
for SQL injection (parameterized values, QUOTENAME for identifiers). Check credential
handling — no credentials in WriteMessage output or exception messages. Check connection
string construction."
```

If the agent reports any CRITICAL or HIGH severity issues, fix them immediately before proceeding.

### 8. Quality Gate — Regression Check

Use the **Task tool** with `subagent_type="regression-sentinel"` to detect breaking changes:

```
Task prompt: "Compare the original PS1 at {ps1_path} against the new C# at {cs_path}.
Check all parameter names, types, defaults, pipeline binding, output types, output
properties, error conditions, and ShouldProcess messages. Report any breaking changes."
```

If the agent reports any breaking changes, fix them before proceeding.

### 9. Quality Gate — dbatools Spirit

Use the **Task tool** with `subagent_type="dbatools-spirit-guardian"` to verify UX quality:

```
Task prompt: "Review {cs_path} for adherence to the dbatools 'it just works' philosophy.
Check that defaults are sensible, error messages explain WHY and suggest WHAT TO DO,
auto-detection is preserved, and a long-time user would not notice behavioral differences."
```

If the agent rejects the conversion, address its concerns before proceeding.

### 9b. Specialized Reviews (when applicable)

Run these additional agent reviews based on what the command does:

**If the command contains embedded T-SQL queries**, use `subagent_type="tsql-collation-reviewer"`:
```
Task prompt: "Review T-SQL in {cs_path} for collation safety. Check temp table joins to
catalog views for COLLATE DATABASE_DEFAULT, verify QUOTENAME usage, check C# string
comparisons of SQL metadata use OrdinalIgnoreCase."
```

**If the command uses SMO or SqlClient APIs**, use `subagent_type="microsoft-sdk-validator"`:
```
Task prompt: "Validate SMO/SqlClient API usage in {cs_path}. Check property access
patterns, connection lifecycle, and version-specific API availability."
```

**If the command has cross-platform concerns** (file paths, Windows APIs, WMI), use `subagent_type="xplat-compatibility-reviewer"`:
```
Task prompt: "Review {cs_path} for cross-platform compatibility. Check Path.Combine usage,
platform guards on Windows-only APIs, StringComparison.OrdinalIgnoreCase, and conditional
compilation for net472 vs net8.0."
```

**Always** run `subagent_type="best-practices-reviewer"` for a senior code review:
```
Task prompt: "Review {cs_path} for code quality: SOLID principles, error handling patterns,
performance (no allocations in loops, proper StringBuilder usage), thread safety of static
state access, and adherence to C# 7.3 constraints."
```

**If existing Pester tests exist** for this command (check `c:\github\dbatools-ralph\tests\{CommandName}.Tests.ps1`), use `subagent_type="pester-test-guardian"`:
```
Task prompt: "Evaluate the Pester tests at c:\github\dbatools-ralph\tests\{CommandName}.Tests.ps1
for compatibility with the new C# binary cmdlet at {cs_path}. Identify any tests that
will break due to the conversion (changed output types, removed properties, different
error behavior). Report which tests need adaptation and suggest fixes."
```

Fix any issues reported by these specialized reviewers before proceeding to Step 10.

### 10. Retire the PS1 Function

The C# cmdlet replaces the PS1 function. Both cannot coexist — PowerShell will error on duplicate command names. Perform these steps in the **dbatools repo** (`c:\github\dbatools-ralph`):

#### 10a. Archive the PS1 file

Move the PS1 to an `archive/` folder (gitignored — for reference only, the original is in git history):

```bash
mkdir -p c:/github/dbatools-ralph/archive
mv c:/github/dbatools-ralph/public/{CommandName}.ps1 c:/github/dbatools-ralph/archive/{CommandName}.ps1
```

#### 10b. Remove from dbatools FunctionsToExport

Edit `c:\github\dbatools-ralph\dbatools.psd1` — remove `'{CommandName}'` from the `FunctionsToExport` array.

#### 10c. Add to dbatools.library CmdletsToExport

Edit `c:\github\dbatools.library\dbatools.library.psd1` — add `'{CommandName}'` to the `CmdletsToExport` array. Keep the array sorted alphabetically.

### 11. Final Test Run

Re-run the test suite one last time after all quality gate fixes:

```bash
dotnet test project/dbatools.Tests/dbatools.Tests.csproj --no-build --verbosity quiet 2>&1 | tail -5
```

Compare against the baseline from Step 0. All previously passing tests must still pass. If not, fix and re-run before committing.

### 12. Update Tracker and Commit

1. Edit the tracker file: change status from `PENDING` to `DONE`
2. Fill in the C# File, Build, and Parity columns
3. Commit in **dbatools.library** repo:
```bash
cd c:/github/dbatools.library
git add project/dbatools/Commands/{Verb}{Noun}Command.cs
git add dbatools.library.psd1
git add docs/plan/TRACKER-MIGRATE-*.md
git commit -m "$(cat <<'EOF'
feat(migration): Convert {Command-Name} to C# binary cmdlet

- All parameters preserved
- All code paths implemented
- Build passes
- Tests pass (baseline maintained)
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
5. **Test regression**: Tests that passed at baseline now fail and cannot be fixed → describe failures → STOP (do NOT mark DONE)
