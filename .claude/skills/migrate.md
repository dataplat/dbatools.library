# /migrate - Migrate a PS1 function to a C# cmdlet

Migrate a PowerShell function to a C# binary cmdlet in dbatools.library.

## Arguments

$ARGUMENTS is the name or path of the PS1 function to migrate. Examples:
- `/migrate Get-DbaDatabase`
- `/migrate c:\github\dbatools-ralph\public\Get-DbaDatabase.ps1`

## Instructions

### Step 1: Locate and read the PS1 source

If a path was given, read it directly. If a function name was given, search for it in `c:\github\dbatools-ralph\public\` or `c:\github\dbatools-ralph\functions\`.

Read the entire PS1 function file. Do NOT proceed without reading the source.

### Step 2: Analyze the PS1 function

Identify and catalog:

**Parameters:**
- Name, type, mandatory, position
- ValueFromPipeline / ValueFromPipelineByPropertyName
- ValidateSet, ValidateNotNullOrEmpty, ValidateRange, etc.
- Alias attributes
- Parameter sets (DefaultParameterSetName)
- Switch parameters

**Execution pattern:**
- Does it use `$SqlInstance` / `$SqlCredential`? → inherit DbaInstanceCmdlet
- Does it use `ShouldProcess`? → add `SupportsShouldProcess = true`
- Does it use `foreach ($instance in $SqlInstance)`? → needs instance loop pattern
- Does it use `Test-Bound`? → needs TestBound() calls
- Does it use `Test-FunctionInterrupt`? → needs TestFunctionInterrupt() calls

**Message patterns:**
- `Write-Message -Level Verbose` → `WriteMessage(MessageLevel.Verbose, ...)`
- `Write-Message -Level Warning` → `WriteMessage(MessageLevel.Warning, ...)`
- `Write-Message -Level Debug` → `WriteMessage(MessageLevel.Debug, ...)`
- `Stop-Function -Message ... -Continue` → `StopFunction(...); TestFunctionInterrupt(); continue;`
- `Stop-Function -Message ...` (without Continue) → `StopFunction(...); return;`

### Step 3: Generate the C# scaffold

Create a new file in `project/dbatools/Commands/` named `{Verb}{Noun}Command.cs`.

Use this structure — adapt based on analysis:

```csharp
using System;
using System.Management.Automation;

namespace Dataplat.Dbatools.Commands
{
    /// <summary>
    /// {Description from PS1 help or function purpose}.
    /// </summary>
    [Cmdlet("{Verb}", "{Noun}")]
    public class {Verb}{Noun}Command : {BaseClass}
    {
        // Parameters here — see translation rules below

        protected override void ProcessRecord()
        {
            // Implementation scaffold here
        }
    }
}
```

### Step 4: Translate parameters

Apply these rules from the PS1 to C# migration map:

| PS1 | C# |
|---|---|
| `[Parameter(Mandatory)]` | `[Parameter(Mandatory = true)]` |
| `[Parameter(Mandatory, ValueFromPipeline)]` | `[Parameter(Mandatory = true, ValueFromPipeline = true)]` |
| `[switch]$Foo` | `public SwitchParameter Foo { get; set; }` |
| `[string[]]$Database` | `public string[] Database { get; set; }` |
| `[DbaInstanceParameter[]]$SqlInstance` | Inherited from DbaInstanceCmdlet |
| `[PSCredential]$SqlCredential` | Inherited from DbaInstanceCmdlet |
| `[ValidateSet('A','B')]` | `[ValidateSet("A", "B")]` |
| `[Alias('OldName')]` | `[Alias("OldName")]` |

### Step 5: Translate the execution body

**Instance loop pattern** (most common for DbaInstanceCmdlet):
```csharp
protected override void ProcessRecord()
{
    foreach (var instance in SqlInstance)
    {
        try
        {
            // TODO: Connect to instance
            // TODO: Implementation logic
            WriteObject(result);
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

**ShouldProcess pattern** (for Set/Remove/New/Clear/Disable/Enable verbs):
```csharp
if (ShouldProcess(target, String.Format("{0} {1}", action, targetName)))
{
    // perform action
}
```

**Key translation rules:**
- `"Value is $var"` → `String.Format("Value is {0}", var)` (NEVER use `$"..."`)
- `Write-Output $obj` → `WriteObject(obj)`
- `Test-Bound 'ParamName'` → `TestBound("ParamName")`
- `throw "msg"` → `StopFunction(message)` (in ProcessRecord)
- `$PSBoundParameters.ContainsKey('X')` → `MyInvocation.BoundParameters.ContainsKey("X")`

### Step 6: Leave TODO comments

Do NOT try to translate complex PS1 logic line-by-line. Instead:
- Scaffold the correct structure with parameters and patterns
- Add `// TODO:` comments for implementation sections
- Include the PS1 line numbers as reference: `// TODO: Translate lines 45-67 from PS1 source`

### Step 7: Report what was generated

After creating the scaffold, tell the user:
1. What file was created
2. Which base class was chosen and why
3. How many parameters were translated
4. What patterns were detected (ShouldProcess, instance loop, etc.)
5. What TODO items remain for manual implementation
6. Whether the .psd1 CmdletsToExport needs updating
7. Whether Pester tests exist at `c:\github\dbatools-ralph\tests\{CommandName}.Tests.ps1` (the full migration workflow will require running them)
