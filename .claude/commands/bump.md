---
description: Bump dbatools.library module, main assembly, and CSV package versions
argument-hint: "[PowerShell arguments for build/bump-version.ps1]"
---

Run the repository version bump workflow.

1. Run `pwsh -NoProfile -File build/bump-version.ps1 $ARGUMENTS` from the repository root.
2. Inspect the version diff for:
   - `dbatools.library.psd1` `ModuleVersion`
   - `project/dbatools/dbatools.csproj` `AssemblyVersion` and `FileVersion`
   - `project/Dataplat.Dbatools.Csv/Dataplat.Dbatools.Csv.csproj` `Version`
3. Build the versioned projects:
   - `dotnet build project/dbatools/dbatools.csproj`
   - `dotnet build project/Dataplat.Dbatools.Csv/Dataplat.Dbatools.Csv.csproj`
4. Report the old and new versions and any build failures.

Default bump behavior is implemented in `build/bump-version.ps1`: the module version becomes today's `yyyy.M.d`, the main DLL version increments the fourth segment, and the CSV package/DLL version increments the patch segment.
