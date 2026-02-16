# dbatools.library

C# binary module backing the [dbatools](https://dbatools.io) PowerShell module.

## Build

- **Namespace:** `Dataplat.Dbatools`
- **Targets:** `net472` and `net8.0`
- **LangVersion:** 7.3 (NO C# 8+ features)
- **PowerShell compatibility:** v3+ (no syntax or features from v4+/v5+/v7+)
- **Build:** `dotnet build project/dbatools/dbatools.csproj`
- **Tests:** `dotnet test project/dbatools.Tests/dbatools.Tests.csproj`

## PS1 to C# Migration Map

When migrating PowerShell functions to C# cmdlets, use these translations:

| PowerShell | Correct C# | WRONG C# |
|---|---|---|
| `Write-Verbose $msg` | `WriteMessage(MessageLevel.Verbose, msg)` | `WriteVerbose(msg)` |
| `Write-Warning $msg` | `WriteMessage(MessageLevel.Warning, msg)` | `WriteWarning(msg)` |
| `Write-Debug $msg` | `WriteMessage(MessageLevel.Debug, msg)` | `WriteDebug(msg)` |
| `Stop-Function -Message $msg` | `StopFunction(message, ...)` | `ThrowTerminatingError(...)` |
| `Write-Output $obj` | `WriteObject(obj)` | |
| `$PSBoundParameters.ContainsKey('X')` | `MyInvocation.BoundParameters.ContainsKey("X")` | |
| `$PSCmdlet.ShouldProcess(...)` | `ShouldProcess(...)` | |
| `[Parameter(Mandatory)]` | `[Parameter(Mandatory = true)]` | |
| `"Value is $var"` | `String.Format("Value is {0}", var)` | `$"Value is {var}"` (C# 6+) |
| `throw "msg"` | `StopFunction(message)` | `throw new Exception(msg)` |

## Migration Patterns

### ProcessRecord vs PS1 Process Block

In PS1, `process { }` runs once per pipeline item. In C#, `ProcessRecord()` receives the whole array when the parameter is `Type[]`. You MUST loop internally:

```csharp
protected override void ProcessRecord()
{
    foreach (var instance in SqlInstance)
    {
        try
        {
            // process one instance
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

### Error Handling in Instance Loops

The PS1 pattern `Stop-Function -Message "..." -Continue` translates to:

```csharp
StopFunction(message, errorRecord, target);
TestFunctionInterrupt();
continue;   // skip to next item in the loop
```

Always call `TestFunctionInterrupt()` after `StopFunction()` -- it checks if `EnableException` caused a halt. The `continue` skips to the next loop iteration (equivalent of `-Continue`).

Without `-Continue` (terminal error within the function):

```csharp
StopFunction(message, errorRecord, target);
return;
```

### ShouldProcess (Set/Remove/New/Clear verbs)

Destructive cmdlets must declare `SupportsShouldProcess`:

```csharp
[Cmdlet("Remove", "DbaDatabase", SupportsShouldProcess = true)]
```

And wrap the action:

```csharp
if (ShouldProcess(target, String.Format("Removing database {0}", dbName)))
{
    // perform action
}
```

### Error Handling: throw vs StopFunction

- In cmdlet `ProcessRecord`: use `StopFunction()` for operational errors (connection failures, query errors)
- Use `throw new ArgumentException()` ONLY for parameter validation before `ProcessRecord`
- In non-cmdlet classes (types, utilities): `throw` is fine -- these aren't cmdlets

## Cmdlet Structure

New cmdlets go in `Commands/`, one class per file, inheriting DbaBaseCmdlet or DbaInstanceCmdlet:

```csharp
namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// Gets database information from a SQL Server instance.
    /// </summary>
    [Cmdlet("Get", "DbaDatabase")]
    public class GetDbaDatabaseCommand : DbaInstanceCmdlet
    {
        [Parameter(Position = 1)]
        public string[] Database { get; set; }

        protected override void ProcessRecord()
        {
            // Use WriteMessage for verbose/warning output
            // Use StopFunction for errors
            // Use WriteObject for pipeline output
        }
    }
}
```

## Base Classes

- **DbaBaseCmdlet** : PSCmdlet -- base for ALL cmdlets
  - `WriteMessage()` -- routed through MessageHost (replaces Write-Verbose/Warning/Debug)
  - `StopFunction()` -- error handling (replaces Stop-Function / ThrowTerminatingError)
  - `TestBound()` -- check if parameter was bound (replaces $PSBoundParameters.ContainsKey)
  - `TestFunctionInterrupt()` -- check if Stop-Function requested a halt
  - `EnableException` -- auto-provided parameter
- **DbaInstanceCmdlet** : DbaBaseCmdlet -- base for SQL instance cmdlets
  - `SqlInstance` (DbaInstanceParameter[])
  - `SqlCredential` (PSCredential)

## Parameter Types

| Type | Use for | Accepts |
|---|---|---|
| `DbaInstanceParameter` | SQL Server instance | strings, SMO Server objects |
| `DbaCredentialParameter` | Credentials | PSCredential, NetworkCredential |
| `DbaDatabaseParameter` | Database name | strings |
| `DbaDatabaseSmoParameter` | Database object | strings, SMO Database objects |

## Static Hubs

All cross-runspace state lives in static hub classes:

| Hub | Namespace | Purpose |
|---|---|---|
| `MessageHost` | `Message` | Message routing, verbosity levels, transforms |
| `LogHost` | `Message` | Error/log queues, max counts |
| `ConfigurationHost` | `Configuration` | Config registry, validation, persistence |
| `ConnectionHost` | `Connection` | Connection cache, protocol settings |
| `RunspaceHost` | `Runspace` | Managed runspace registry |
| `TabExpansionHost` | `TabExpansion` | Tab completion scripts and cache |

## Project Structure

```
project/dbatools/
  Commands/        Cmdlet implementations (one per file)
  Computer/        DiskSpace, DriveType, PageFileSetting
  Configuration/   Config, ConfigurationHost, ConfigScope
  Connection/      ConnectionHost, ManagementConnection
  Csv/             CsvReader, CsvWriter, TypeConverters
  Database/        BackupHistory, Dependency
  Discovery/       Instance discovery types
  Exceptions/      Custom exceptions
  IO/              ProgressStream
  Maintenance/     MaintenanceHost, MaintenanceTask
  Message/         MessageHost, LogHost, LogEntry, MessageLevel
  Parameter/       DbaInstanceParameter, DbaCredentialParameter, etc.
  Runspace/        RunspaceHost, RunspaceContainer
  TabExpansion/    TabExpansionHost
  TypeConversion/  DbaCredentialParameterConverter
  Utility/         Size, DbaDateTime, RegexHelper, UtilityHost
  Validation/      Validation types
```

## Assembly Loading

- Framework detection in `dbatools.library.psm1`
- net472: `AppDomain.CurrentDomain.AssemblyResolve` (Redirector class)
- net8.0: `AssemblyLoadContext.Default.Resolving` (CoreRedirector class)
- Use `#if NETFRAMEWORK` / `#else` for framework-specific code
- Auto-defined symbols: `NETFRAMEWORK`, `NET472` for net472; `NETCOREAPP`, `NET8_0` for net8.0
- NEVER use `Assembly.LoadFile()` -- causes type identity issues

## Module Manifest Rules

- `RootModule` points to `.psm1`, not `.dll`
- `FunctionsToExport`, `CmdletsToExport`, `AliasesToExport` must use explicit arrays, never `'*'`
- `RequiredAssemblies` is empty -- assemblies loaded in `.psm1`

## Rules Enforced by Hooks

These are automatically enforced on every edit -- do not fight them:

1. Cmdlets must inherit DbaBaseCmdlet or DbaInstanceCmdlet, never PSCmdlet/Cmdlet directly
2. No C# 8+ syntax (LangVersion 7.3): no `#nullable`, `??=`, switch expressions, ranges, `using var`, static local functions
3. No `Assembly.LoadFile()` -- use `Assembly.LoadFrom()` or ALC
4. No direct `WriteVerbose()`/`WriteWarning()`/`WriteDebug()` in cmdlets -- use `WriteMessage()`
5. No `ThrowTerminatingError()` in cmdlets -- use `StopFunction()`
6. All `[Cmdlet]` classes must have `/// <summary>` XML doc comment
7. No `$"..."` string interpolation -- use `String.Format()` (project convention)
8. No wildcard exports in `.psd1` manifests
9. Build must succeed after every C# edit
10. Any PowerShell code (`.psm1`, `.psd1`, scripts) must be PowerShell v3 compatible -- no classes, no `using` statements, no ternary operators, no `??`, no `&&`/`||` pipeline chains, no `ForEach-Object -Parallel`

## Known Intentional Exceptions

- `WriteMessageCommand` uses `new bool EnableException` (bool, not SwitchParameter) -- intentional
- `SetDbatoolsConfigCommand` uses `new SwitchParameter EnableException` -- intentional override
- `WriteMessageCommand` calls WriteVerbose/WriteWarning/WriteDebug directly -- it IS the message system
