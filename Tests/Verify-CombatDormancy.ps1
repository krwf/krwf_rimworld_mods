$ErrorActionPreference = 'Stop'
$rkRoot = Split-Path -Parent $PSScriptRoot
$rkDraftedSource = Get-Content -LiteralPath (Join-Path $rkRoot 'Source/RimKataDraftedFire.cs') -Raw -Encoding UTF8
$rkControllerSource = Get-Content -LiteralPath (Join-Path $rkRoot 'Source/RimKataDualWeaponController.cs') -Raw -Encoding UTF8

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

$rkProcessTick = Get-CSharpBlock $rkDraftedSource 'public static void ProcessJobTrackerTick('
$rkDraftedTick = Get-CSharpBlock $rkDraftedSource 'private static void TickDualWeaponController('
$rkNoDemand = Get-CSharpBlock $rkDraftedTick 'if (!combatDemand)'
$rkCycleWork = Get-CSharpBlock $rkControllerSource 'private static bool HasCycleTargetWork('
$rkExecuteCycle = Get-CSharpBlock $rkControllerSource 'private static int ExecuteCycle('

$rkResetIndex = $rkNoDemand.IndexOf('ResetIfActive(pawn, state);', [StringComparison]::Ordinal)
$rkMovementClearIndex = $rkNoDemand.IndexOf('state.ClearDraftedMovementSearchTracking();', $rkResetIndex, [StringComparison]::Ordinal)
if ($rkResetIndex -lt 0 -or $rkMovementClearIndex -le $rkResetIndex -or
    $rkNoDemand.Substring($rkResetIndex, $rkMovementClearIndex - $rkResetIndex) -notmatch 'pawn\.pather\?\.Moving\s*!=\s*true') {
    throw 'No-demand stationary movement tracking is not cleared after controller reset.'
}

$rkActiveGateIndex = $rkCycleWork.IndexOf('cycle?.DedicatedActive != true', [StringComparison]::Ordinal)
$rkVerbIndex = $rkCycleWork.IndexOf('RimKataWeaponSlotUtility.CombatVerb(', [StringComparison]::Ordinal)
if ($rkActiveGateIndex -lt 0 -or $rkVerbIndex -le $rkActiveGateIndex) {
    throw 'DedicatedActive must reject targetless cycles before CombatVerb resolution.'
}

$rkVanillaOpeningIndex = $rkExecuteCycle.IndexOf('bool firedFromVanillaOpening', [StringComparison]::Ordinal)
$rkMovementBlockIndex = $rkExecuteCycle.IndexOf('if (MovementBlocksFire(', [StringComparison]::Ordinal)
$rkSpeedRequestIndex = $rkExecuteCycle.IndexOf('RimKataVerbUtility.RequestNormalSpeedForCombat(', [StringComparison]::Ordinal)
$rkActionIndex = $rkExecuteCycle.IndexOf('bool acted;', [StringComparison]::Ordinal)
if ($rkMovementBlockIndex -lt 0 -or $rkVanillaOpeningIndex -le $rkMovementBlockIndex -or
    $rkSpeedRequestIndex -le $rkVanillaOpeningIndex -or
    $rkActionIndex -le $rkSpeedRequestIndex -or
    $rkExecuteCycle -notmatch '!firedFromVanillaOpening[\s\S]*?cycle\.burstShotsRemaining\s*<=\s*0') {
    throw 'Cycle speed request is not limited to a new non-vanilla-opening attack.'
}

$rkHarness = @"
using System;

namespace CombatDormancyChecks
{
    public sealed class Pather
    {
        public bool Moving;
    }

    public sealed class Pawn
    {
        public bool Drafted;
        public bool InMentalState;
        public bool pending;
        public int stateReads;
        public int controllerTicks;
        public int pendingConsumes;
        public bool controllerExistingStateKnown;
        public string order = "";
        public Pather pather = new Pather();
        public RimKataPawnCombatState state;
        public RimKataPawnCombatState controllerState;
    }

    public sealed class RimKataPawnCombatState
    {
        public bool dedicatedFollowupJobPending;
    }

    public static class RimKataPendingFollowupTickCache
    {
        public static bool Contains(Pawn pawn)
        {
            return pawn != null && pawn.pending;
        }
    }

    public static class RimKataDualWeaponController
    {
        public static void TryConsumePendingDedicatedFollowupJob(
            Pawn pawn,
            RimKataPawnCombatState state)
        {
            pawn.pendingConsumes++;
            pawn.order += "C";
        }

        public static void TryConsumePendingDedicatedFollowupJob(Pawn pawn)
        {
            pawn.pendingConsumes++;
            pawn.order += "C";
        }
    }

    public static class RimKataDraftedFireController
    {
        private static RimKataPawnCombatState StateFor(Pawn pawn, bool create)
        {
            pawn.stateReads++;
            if (create && pawn.state == null)
            {
                pawn.state = new RimKataPawnCombatState();
            }
            return pawn.state;
        }

        private static void TickDualWeaponController(
            Pawn pawn,
            RimKataPawnCombatState state,
            bool existingStateKnown)
        {
            pawn.controllerTicks++;
            pawn.controllerState = state;
            pawn.controllerExistingStateKnown = existingStateKnown;
            pawn.order += "T";
        }

        $rkProcessTick
    }

    public static class Checks
    {
        private static int checks;

        private static void Check(bool condition, string name)
        {
            if (!condition) throw new Exception("FAIL: " + name);
            checks++;
        }

        public static int Run()
        {
            var idle = new Pawn { Drafted = true };
            RimKataDraftedFireController.ProcessJobTrackerTick(idle);
            Check(idle.stateReads == 1 && idle.controllerTicks == 0,
                "stationary drafted pawn without state stops at cheap lookup");

            var moving = new Pawn { Drafted = true };
            moving.pather.Moving = true;
            RimKataDraftedFireController.ProcessJobTrackerTick(moving);
            Check(moving.stateReads == 1 && moving.controllerTicks == 1 && moving.controllerState == null
                && moving.controllerExistingStateKnown,
                "moving drafted pawn without state retains controller wake path");

            var retained = new Pawn {
                Drafted = true,
                state = new RimKataPawnCombatState()
            };
            RimKataDraftedFireController.ProcessJobTrackerTick(retained);
            Check(retained.stateReads == 1 && retained.controllerTicks == 1 && retained.controllerState == retained.state,
                "existing combat state retains controller processing while stationary");

            var followup = new Pawn {
                Drafted = true,
                state = new RimKataPawnCombatState { dedicatedFollowupJobPending = true }
            };
            RimKataDraftedFireController.ProcessJobTrackerTick(followup);
            Check(followup.pendingConsumes == 1 && followup.controllerTicks == 1 && followup.order == "CT",
                "drafted pending followup is consumed before controller processing");

            var undrafted = new Pawn { pending = true };
            RimKataDraftedFireController.ProcessJobTrackerTick(undrafted);
            Check(undrafted.stateReads == 0 && undrafted.pendingConsumes == 1 && undrafted.controllerTicks == 0,
                "undrafted event-driven followup path is preserved");

            var mental = new Pawn { Drafted = true, InMentalState = true };
            RimKataDraftedFireController.ProcessJobTrackerTick(mental);
            Check(mental.stateReads == 0 && mental.controllerTicks == 0,
                "mental-state early return remains ahead of combat work");

            return checks;
        }
    }
}
"@

Add-Type -TypeDefinition $rkHarness -Language CSharp
$rkPassed = [CombatDormancyChecks.Checks]::Run()
"PASS: $rkPassed executable dormancy assertions + 3 source-boundary checks; production tick method with minimal fixtures, not an in-game performance test."
