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

### 1. Find Next Command

Read tracker files in `docs/plan/TRACKER-MIGRATE-*.md`. Find the FIRST command with status `PENDING` whose dependencies are all `DONE`.

If ALL commands across ALL trackers are DONE, create signal file and STOP:
```bash
touch docs/plan/.migration-complete
```

If the next PENDING command has unmet dependencies, skip to the next one that's ready.

### 2. Read the PS1 Source

Read the original PS1 file from `c:\github\dbatools\public\{CommandName}.ps1`.

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

### 6. Quality Gate — Feature Parity

Compare your C# against the PS1 source. For EVERY item you extracted in Step 2, verify it exists in C#:

```
[Feature] — present at [file:line]
[Feature] — MISSING — must add
[Feature] — different behavior: [explain]
```

**Unacceptable excuses for missing features:**
- "Too complex" — implement it
- "Nobody uses that" — they do
- "We can add it later" — no
- "Edge case" — edge cases are features

**The ONLY acceptable omission:** Features that depend on PowerShell dynamic scoping with no C# equivalent AND cannot be replicated in the cmdlet.

If ANY feature is missing, go back to Step 4 and add it. Do not proceed.

### 7. Quality Gate — Security

Check EVERY T-SQL query in your code:
- User input parameterized (SqlParameter, not concatenation)
- Object names quoted with QUOTENAME() or brackets
- No String.Format() with user input into SQL text

Check credential handling:
- Credentials never in WriteMessage output
- Credentials never in exception messages
- Connection strings not logged

If ANY security issue exists, fix it immediately.

### 8. Quality Gate — Regression Check

Verify against the PS1:
- All parameter names identical (or aliased)
- All parameter types identical
- All default values identical
- Pipeline binding preserved
- Output type and properties identical
- Error conditions identical
- ShouldProcess messages match

### 9. Quality Gate — dbatools Spirit

Ask yourself:
- Can a DBA still do `Verb-DbaObject -SqlInstance localhost` and have it work?
- Are defaults still sensible?
- Do error messages explain WHY and suggest WHAT TO DO?
- Would a long-time user notice anything different?

If any answer is "no," fix it.

### 10. Update Tracker and Commit

1. Edit the tracker file: change status from `PENDING` to `DONE`
2. Fill in the C# File, Build, and Parity columns
3. Commit:
```bash
git add project/dbatools/Commands/{Verb}{Noun}Command.cs
git add docs/plan/TRACKER-MIGRATE-*.md
git commit -m "$(cat <<'EOF'
feat(migration): Convert {Command-Name} to C# binary cmdlet

- All parameters preserved
- All code paths implemented
- Build passes
- Feature parity verified

Co-Authored-By: Claude Opus 4.6 <noreply@anthropic.com>
EOF
)"
```

4. **STOP** — do not continue to the next command.

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

1. **Normal**: Converted one command, marked DONE, committed → STOP
2. **All done**: All trackers show DONE → create signal file → STOP
3. **Blocked**: Dependency not met, no other commands ready → describe blocker → STOP
4. **Build failure**: Cannot fix after 3 attempts → describe error → STOP (do NOT mark DONE)
