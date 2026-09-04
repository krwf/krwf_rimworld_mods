$ErrorActionPreference = 'Stop'
$rkRoot = Split-Path -Parent $PSScriptRoot
$rkSource = Get-Content -LiteralPath (Join-Path $rkRoot 'Source/RimKataSecondaryWeapon.cs') -Raw -Encoding UTF8
function Get-CSharpBlock([string] $source, [string] $marker) {
    $start = $source.IndexOf($marker, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing source block: $marker" }
    $open = $source.IndexOf('{', $start)
    $depth = 0
    for ($index = $open; $index -lt $source.Length; $index++) {
        if ($source[$index] -eq '{') { $depth++ }
        if ($source[$index] -eq '}') {
            $depth--
            if ($depth -eq 0) { return $source.Substring($start, $index - $start + 1) }
        }
    }
    throw "Unclosed source block: $marker"
}
$rkPatch = Get-CSharpBlock $rkSource 'public static class Patch_DebugToolsPawns_RimKataSecondaryWeapon'
$rkPostfix = Get-CSharpBlock $rkPatch 'public static void Postfix('
$rkWrap = Get-CSharpBlock $rkPatch 'private static Action WrapPrimaryWeaponOption('
$rkLookup = Get-CSharpBlock $rkPatch 'public static bool TryGetActivePrimaryReplacement('
$rkScope = Get-CSharpBlock $rkPatch 'private sealed class PrimaryReplacementScope'

# Exercise actual option wrapping and replacement scopes against a value-type
# DebugMenuOption and tiny engine stubs. This is not a RimWorld equipment test.
$rkHarness = @"
using System;
using System.Collections.Generic;
namespace DebugPrimaryReplacementChecks {
    public enum EquipmentType { Primary, Other }
    public enum DebugMenuOptionMode { Action, Other }
    public struct DebugMenuOption {
        public string label;
        public DebugMenuOptionMode mode;
        public Action method;
        public DebugMenuOption(string label, DebugMenuOptionMode mode, Action method) {
            this.label = label; this.mode = mode; this.method = method;
        }
    }
    public sealed class ThingDef { public EquipmentType equipmentType = EquipmentType.Primary; }
    public sealed class ThingWithComps { public ThingDef def = new ThingDef(); public bool Destroyed; }
    public sealed class Pawn { public Pawn_EquipmentTracker equipment = new Pawn_EquipmentTracker(); }
    public sealed class Pawn_EquipmentTracker { public ThingWithComps Primary; }
    public sealed class RimKataSecondaryWeaponRegistry {
        public static readonly RimKataSecondaryWeaponRegistry CurrentRegistry = new RimKataSecondaryWeaponRegistry();
        public readonly Dictionary<Pawn, ThingWithComps> weapons = new Dictionary<Pawn, ThingWithComps>();
        public ThingWithComps GetRegistered(Pawn pawn) {
            return pawn != null && weapons.TryGetValue(pawn, out ThingWithComps weapon) ? weapon : null;
        }
    }
    public static class RimKataWeaponSlotUtility { public static bool CanUseSecondarySlot(Pawn pawn) { return pawn != null; } }
    public sealed class Dialog_DebugOptionListLister { public Dialog_DebugOptionListLister(List<DebugMenuOption> options, object unused) { } }
    public sealed class WindowStack { public void Add(object window) { } }
    public static class Find { public static readonly WindowStack WindowStack = new WindowStack(); }
    public static class Patch {
        $rkScope
        [ThreadStatic] private static PrimaryReplacementScope activePrimaryReplacement;
        private static List<DebugMenuOption> SecondaryWeaponOptions(Pawn pawn) { return new List<DebugMenuOption>(); }
        $rkPostfix
        $rkWrap
        $rkLookup
    }
    public static class Checks {
        private static int checks;
        private static void Check(bool condition, string name) {
            if (!condition) throw new Exception(name);
            checks++;
        }
        private static bool Active(Pawn pawn, ThingWithComps incoming, out ThingWithComps secondary) {
            return Patch.TryGetActivePrimaryReplacement(pawn.equipment, pawn, incoming, out secondary);
        }
        private static List<DebugMenuOption> Menu(Pawn pawn, Action action, Action remove = null) {
            var options = new List<DebugMenuOption> {
                new DebugMenuOption("*Remove primary", DebugMenuOptionMode.Action, remove),
                new DebugMenuOption("New primary", DebugMenuOptionMode.Action, action)
            };
            Patch.Postfix(pawn, options);
            return options;
        }
        public static int Run() {
            var registry = RimKataSecondaryWeaponRegistry.CurrentRegistry;
            var pawn = new Pawn();
            var oldSecondary = new ThingWithComps();
            var latestSecondary = new ThingWithComps();
            var incoming = new ThingWithComps();
            registry.weapons[pawn] = oldSecondary;
            int calls = 0, removes = 0;
            bool capturedAfterClear = false;
            Action original = () => {
                calls++;
                pawn.equipment.Primary = latestSecondary;
                registry.weapons.Remove(pawn);
                capturedAfterClear = Active(pawn, incoming, out ThingWithComps captured) && captured == latestSecondary;
            };
            Action remove = () => removes++;
            var options = Menu(pawn, original, remove);
            Check(options[2].method != original, "Wrapped Action is written back into struct option list");
            Check(options[0].method == remove && options[1].label == "[RimKata]", "Remove-primary option remains unwrapped and inserted entry stays separate");
            registry.weapons[pawn] = latestSecondary;
            options[2].method();
            Check(calls == 1 && capturedAfterClear, "Execution-time secondary survives registry clear and original runs exactly once");
            Check(!Active(pawn, incoming, out _), "Normal completion clears active replacement scope");
            options[0].method();
            Check(removes == 1, "Remove-only action still executes normally");

            int unregisteredCalls = 0;
            bool unregisteredScope = true;
            var unregistered = new Pawn();
            Menu(unregistered, () => { unregisteredCalls++; unregisteredScope = Active(unregistered, incoming, out _); })[2].method();
            Check(unregisteredCalls == 1 && !unregisteredScope, "No-secondary pawn executes original without opening a scope");

            registry.weapons[pawn] = latestSecondary;
            var other = new Pawn(); var otherSecondary = new ThingWithComps();
            registry.weapons[other] = otherSecondary;
            other.equipment.Primary = otherSecondary;
            var expected = new InvalidOperationException("original action failed");
            bool innerScope = false, outerRestored = false, sameException = false;
            var innerMenu = Menu(other, () => { innerScope = Active(other, incoming, out ThingWithComps selected) && selected == otherSecondary; throw expected; });
            var outerMenu = Menu(pawn, () => {
                try { innerMenu[2].method(); } catch (InvalidOperationException error) { sameException = error == expected; }
                outerRestored = Active(pawn, incoming, out ThingWithComps selected) && selected == latestSecondary;
            });
            outerMenu[2].method();
            Check(innerScope && sameException && outerRestored, "Nested throwing action restores the previous scope and preserves its exception");
            Check(!Active(pawn, incoming, out _) && !Active(other, incoming, out _), "Outer completion leaves no leaked scope");
            bool escapedSame = false;
            try { innerMenu[2].method(); } catch (InvalidOperationException error) { escapedSame = error == expected; }
            Check(escapedSame && !Active(other, incoming, out _), "Standalone exception also restores empty scope");

            int untouchedCalls = 0;
            Action untouched = () => untouchedCalls++;
            var skipped = new List<DebugMenuOption> {
                new DebugMenuOption("Remove", DebugMenuOptionMode.Action, remove),
                new DebugMenuOption("Not an Action", DebugMenuOptionMode.Other, untouched),
                new DebugMenuOption("Missing callback", DebugMenuOptionMode.Action, null)
            };
            Patch.Postfix(pawn, skipped);
            Check(skipped[2].method == untouched && skipped[3].method == null && untouchedCalls == 0,
                "Non-action and null callbacks are not wrapped or executed");
            Patch.Postfix(pawn, null);
            Check(!Active(pawn, incoming, out _), "Null option result is harmless");
            return checks;
        }
    }
}
"@
Add-Type -TypeDefinition $rkHarness -Language CSharp
$rkPassed = [DebugPrimaryReplacementChecks.Checks]::Run()
"PASS: $rkPassed assertions; production debug option/scope methods with engine stubs. Not an in-game equipment test."
