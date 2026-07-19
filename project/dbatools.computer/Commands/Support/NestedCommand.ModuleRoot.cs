#nullable enable

namespace Dataplat.Dbatools.Commands;

internal static partial class NestedCommand
{
    // U-4 randomizer/PSModuleRoot cluster, INFRA fix. Retired bodies resolve module-relative
    // assets via "$script:PSModuleRoot\bin\..." - in the function world that hit the dbatools
    // module's script scope. A body-carrying hop executes in the CALLER'S session state, where
    // $script: resolves to the caller's script scope: the GLOBAL scope at a console, but the
    // TEST FILE'S script scope under the gate's Invoke-ManualPester - empty in both, and the
    // module's own copy is ALSO emptied by Pester 5.x session-state churn (root-caused on
    // Install-DbaInstance: path collapsed to "\bin\..."). The churn-immune source of truth is
    // [SystemHost]::ModuleBase (C# static, write-once at import), so every body-carrying hop
    // seeds the current script scope from it and restores the caller's prior value afterwards
    // (third-party callers may own a script-scoped PSModuleRoot of their own - the function
    // world never touched it, so neither may the hop). -WhatIf:$false because Set/Remove-Variable
    // honor ShouldProcess and a hop running under -WhatIf must still seed. Failures to seed fall
    // back to pre-fix behavior (SilentlyContinue) rather than killing the hop.
    // Get-Variable returns a LIVE PSVariable whose .Value mutates when the seed overwrites it,
    // so the prior VALUE is snapshotted into its own variable up front; the PSVariable object
    // itself only witnesses prior EXISTENCE (codex: restoring from .Value after the seed would
    // restore ModuleBase, not the caller's original).
    // -ErrorAction Ignore (not SilentlyContinue) everywhere: Ignore leaves NO $Error
    // bookkeeping, and the ErrorVariableBridge reconciles hop-era $Error entries into the
    // caller's bound -ErrorVariable - a phantom VariableNotFound from this best-effort seed
    // must never be delivered as if the command emitted it (codex r2).
    // The wrapper is DOT-SOURCED (InvokeScript useLocalScope:false), so these snapshot
    // variables land in the CALLER'S scope. A re-entrant hop (body invokes another compiled
    // dbatools cmdlet in the same frame) would overwrite fixed names and make the outer finally
    // restore the SEED instead of the caller's value (codex r3) - so every invocation gets
    // GUID-suffixed snapshot names, and the finally removes them to leave no scope litter.
    private static string ModuleRootSeedProlog(string seedToken)
    {
        string prior = "$__dbatoolsPriorModuleRoot_" + seedToken;
        string priorValue = "$__dbatoolsPriorModuleRootValue_" + seedToken;
        return
            prior + " = Get-Variable -Name PSModuleRoot -Scope Script -ErrorAction Ignore\n" +
            priorValue + " = if ($null -ne " + prior + ") { " + prior + ".Value } else { $null }\n" +
            "Set-Variable -Name PSModuleRoot -Scope Script -Value ([Dataplat.Dbatools.dbaSystem.SystemHost]::ModuleBase) -Force -WhatIf:$false -Confirm:$false -ErrorAction Ignore\n" +
            "try {\n";
    }

    private static string ModuleRootSeedEpilog(string seedToken)
    {
        string prior = "$__dbatoolsPriorModuleRoot_" + seedToken;
        string priorValue = "$__dbatoolsPriorModuleRootValue_" + seedToken;
        return
            "\n} finally { " +
            "if ($null -ne " + prior + ") { Set-Variable -Name PSModuleRoot -Scope Script -Value " + priorValue + " -Force -WhatIf:$false -Confirm:$false -ErrorAction Ignore } " +
            "else { Remove-Variable -Name PSModuleRoot -Scope Script -Force -WhatIf:$false -Confirm:$false -ErrorAction Ignore } " +
            "Remove-Variable -Name __dbatoolsPriorModuleRoot_" + seedToken + ", __dbatoolsPriorModuleRootValue_" + seedToken + " -Force -WhatIf:$false -Confirm:$false -ErrorAction Ignore }";
    }
}
