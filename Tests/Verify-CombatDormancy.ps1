$ErrorActionPreference = 'Stop'
$rkRoot = Split-Path -Parent $PSScriptRoot
$rkDraftedSource = Get-Content -LiteralPath (Join-Path $rkRoot 'Source/RimKataDraftedFire.cs') -Raw -Encoding UTF8
$rkControllerSource = Get-Content -LiteralPath (Join-Path $rkRoot 'Source/RimKataDualWeaponController.cs') -Raw -Encoding UTF8
$rkJobDriverSource = Get-Content -LiteralPath (Join-Path $rkRoot 'Source/JobDriver_RimKataAttack.cs') -Raw -Encoding UTF8

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
$rkTickCore = Get-CSharpBlock $rkControllerSource 'private static void TickCore('
$rkSharedScan = Get-CSharpBlock $rkTickCore 'if (state.sharedTargetSearch?.scanActive == true)'
$rkResolveCloseTarget = Get-CSharpBlock $rkControllerSource 'private static Thing ResolveCloseTarget('
$rkResolveTickCloseTarget = Get-CSharpBlock $rkControllerSource 'private static Thing ResolveTickCloseTarget('
$rkPublicTick = Get-CSharpBlock $rkControllerSource 'public static void Tick('
$rkKnownStateTick = Get-CSharpBlock $rkControllerSource 'internal static void TickWithKnownState('
$rkJobCombatTick = Get-CSharpBlock $rkJobDriverSource 'private void CombatTick()'
$rkJobAdvancing = Get-CSharpBlock $rkJobDriverSource 'private void TickAdvancingFire('
$rkJobClose = Get-CSharpBlock $rkJobDriverSource 'private void TickCloseCombat('
$rkJobTickFire = Get-CSharpBlock $rkJobDriverSource 'private void TickCombatFire('
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

$rkDedicatedJobIndex = $rkProcessTick.IndexOf(
    'pawn.CurJobDef == RimKataDefOf.RimKata_Attack',
    [StringComparison]::Ordinal)
$rkStateReadIndex = $rkProcessTick.IndexOf(
    'RimKataPawnCombatState state = StateFor(pawn, false);',
    [StringComparison]::Ordinal)
$rkPresenceIndex = $rkProcessTick.IndexOf(
    'RimKataCombatStatePresenceCache.Contains(pawn, map)',
    [StringComparison]::Ordinal)
if ($rkDedicatedJobIndex -lt 0 -or
    $rkStateReadIndex -le $rkDedicatedJobIndex -or
    $rkPresenceIndex -le $rkDedicatedJobIndex -or
    $rkStateReadIndex -le $rkPresenceIndex) {
    throw 'Dedicated Job and no-state dormancy gates must precede the combat-state lookup.'
}

$rkDedicatedJobGate = Get-CSharpBlock $rkProcessTick 'if (pawn.CurJobDef == RimKataDefOf.RimKata_Attack)'
if ($rkDedicatedJobGate -notmatch 'RimKataPendingFollowupTickCache\.Contains\(pawn\)' -or
    $rkDedicatedJobGate -notmatch 'TryConsumePendingDedicatedFollowupJob\(pawn\)' -or
    $rkDedicatedJobGate -match 'StateFor\(') {
    throw 'Dedicated Job fast path no longer preserves only the rare pending follow-up lookup.'
}

$rkScanAdvanceIndex = $rkSharedScan.IndexOf(
    'AdvanceSharedTargetSearch(pawn, state, assignedTarget);',
    [StringComparison]::Ordinal)
$rkScanRefreshIndex = $rkSharedScan.IndexOf(
    'RefreshDualEngagementState(pawn, state, randomAttackEnabled);',
    [StringComparison]::Ordinal)
$rkScanSnapshotIndex = $rkSharedScan.IndexOf(
    'combatContinuity = state.dualEngagementActive;',
    [StringComparison]::Ordinal)
$rkScanKnownIndex = $rkSharedScan.IndexOf(
    'combatContinuityKnown = true;',
    [StringComparison]::Ordinal)
if ($rkScanAdvanceIndex -lt 0 -or
    $rkScanRefreshIndex -le $rkScanAdvanceIndex -or
    $rkScanSnapshotIndex -le $rkScanRefreshIndex -or
    $rkScanKnownIndex -le $rkScanSnapshotIndex -or
    $rkSharedScan -match 'HasCombatContinuity\(') {
    throw 'Shared search must publish one refreshed continuity result without re-evaluating it.'
}

$rkDodgePauseIndex = $rkTickCore.IndexOf(
    'if (ShouldPauseFireForDodge(pawn))',
    [StringComparison]::Ordinal)
$rkContinuityUseIndex = $rkTickCore.IndexOf(
    'if (!(combatContinuityKnown',
    [StringComparison]::Ordinal)
$rkContinuityFallbackIndex = $rkTickCore.IndexOf(
    ': HasCombatContinuity(',
    $rkContinuityUseIndex,
    [StringComparison]::Ordinal)
if ($rkDodgePauseIndex -lt 0 -or
    $rkContinuityUseIndex -le $rkDodgePauseIndex -or
    $rkContinuityFallbackIndex -le $rkContinuityUseIndex -or
    [regex]::Matches($rkTickCore, 'HasCombatContinuity\(').Count -ne 1) {
    throw 'Shared-search continuity reuse moved across the dodge pause or lost its non-scan fallback.'
}

if ($rkResolveTickCloseTarget -notmatch '!ordinaryAttackAllowed[\s\S]*?return null;' -or
    $rkResolveTickCloseTarget -notmatch 'closeTargetResolutionKnown[\s\S]*?\? resolvedCloseTarget[\s\S]*?: ResolveCloseTarget\(' -or
    [regex]::Matches($rkTickCore, 'ResolveTickCloseTarget\(').Count -ne 1 -or
    $rkTickCore -match 'ResolveCloseTarget\(' -or
    $rkTickCore -notmatch 'bool closeCombatContext\s*=\s*closeTarget\s*!=\s*null;') {
    throw 'TickCore no longer honors the exact resolved close target, including a resolved null.'
}

if ($rkResolveCloseTarget -notmatch 'Thing requested\s*=\s*state\?\.closeAttackRequestTarget;' -or
    $rkResolveCloseTarget -match 'state\?\.CloseAttackRequestActive' -or
    $rkDraftedTick -notmatch 'Thing requestedCloseTarget\s*=\s*ordinaryAttackAllowed[\s\S]*?\? state\.closeAttackRequestTarget[\s\S]*?: null;' -or
    $rkDraftedTick -match 'ordinaryAttackAllowed\s*&&\s*state\.CloseAttackRequestActive') {
    throw 'Immediate close-target resolution regained duplicate request validation.'
}

if ($rkPublicTick -notmatch 'Thing resolvedCloseTarget\s*=\s*closeTargetResolved[\s\S]*?&& closeCombatContext[\s\S]*?\? assignedTarget[\s\S]*?: null;' -or
    $rkKnownStateTick -notmatch 'Thing resolvedCloseTarget,[\s\S]*?bool closeTargetResolutionKnown' -or
    $rkKnownStateTick -match 'IsImmediateCloseTarget|ResolveCloseTarget') {
    throw 'Public and known-state tick entry points lost the explicit close-target resolution contract.'
}

if ($rkDraftedTick -notmatch 'TickWithKnownState\([\s\S]*?immediateCloseTarget,[\s\S]*?closePlayerForced,[\s\S]*?closeKillIncappedTarget,[\s\S]*?immediateCloseTarget,[\s\S]*?true,[\s\S]*?automaticRangedFireAllowed\);' -or
    $rkJobCombatTick -notmatch 'TickCombatFire\([\s\S]*?state,[\s\S]*?assignedTarget,[\s\S]*?immediateCloseTarget,[\s\S]*?true\);' -or
    $rkJobCombatTick -notmatch 'TickCloseCombat\(state, assignedTarget, immediateCloseTarget\);' -or
    $rkJobAdvancing -notmatch 'TickCombatFire\(state, assignedTarget, null, true\);' -or
    $rkJobClose -notmatch 'TickCombatFire\([\s\S]*?state,[\s\S]*?assignedTarget,[\s\S]*?resolvedCloseTarget,[\s\S]*?true\);' -or
    $rkJobTickFire -notmatch 'TickWithKnownState\([\s\S]*?resolvedCloseTarget,[\s\S]*?closeTargetResolutionKnown,[\s\S]*?true\);') {
    throw 'DraftedFire or JobDriver stopped forwarding the close target resolved earlier in the same tick.'
}

$rkHarness = @"
using System;

namespace CombatDormancyChecks
{
    public sealed class Map
    {
    }

    public sealed class JobDef
    {
    }

    public sealed class Thing
    {
    }

    public static class RimKataDefOf
    {
        public static readonly JobDef RimKata_Attack = new JobDef();
    }

    public sealed class Pather
    {
        public bool Moving;
    }

    public sealed class Pawn
    {
        public bool Drafted;
        public bool InMentalState;
        public bool pending;
        public bool presence;
        public int stateReads;
        public int consumerStateReads;
        public int pendingReads;
        public int pendingConsumerCalls;
        public int pendingMarkerClears;
        public int presenceReads;
        public int controllerTicks;
        public int pendingConsumes;
        public bool controllerExistingStateKnown;
        public string order = "";
        public Map Map = new Map();
        public JobDef CurJobDef;
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
            if (pawn != null) pawn.pendingReads++;
            return pawn != null && pawn.pending;
        }
    }

    public static class RimKataCombatStatePresenceCache
    {
        public static bool Contains(Pawn pawn, Map map)
        {
            if (pawn != null) pawn.presenceReads++;
            return pawn != null && map != null && pawn.presence;
        }
    }

    public static class RimKataDualWeaponController
    {
        public static int closeResolverCalls;
        public static Thing closeResolverResult;

        public static void TryConsumePendingDedicatedFollowupJob(
            Pawn pawn,
            RimKataPawnCombatState state)
        {
            pawn.pendingConsumes++;
            pawn.order += "C";
        }

        public static void TryConsumePendingDedicatedFollowupJob(Pawn pawn)
        {
            pawn.pendingConsumerCalls++;
            pawn.consumerStateReads++;
            if (pawn.state?.dedicatedFollowupJobPending != true)
            {
                pawn.pending = false;
                pawn.pendingMarkerClears++;
                return;
            }

            pawn.pendingConsumes++;
            pawn.order += "C";
        }

        private static Thing ResolveCloseTarget(
            Pawn pawn,
            RimKataPawnCombatState state,
            Thing assignedTarget,
            bool playerForced,
            bool killIncappedTarget)
        {
            closeResolverCalls++;
            return closeResolverResult;
        }

        $rkResolveTickCloseTarget

        public static Thing ResolveTickCloseTargetForTest(
            Pawn pawn,
            RimKataPawnCombatState state,
            Thing assignedTarget,
            bool ordinaryAttackAllowed,
            Thing resolvedCloseTarget,
            bool closeTargetResolutionKnown)
        {
            return ResolveTickCloseTarget(
                pawn,
                state,
                assignedTarget,
                false,
                false,
                ordinaryAttackAllowed,
                resolvedCloseTarget,
                closeTargetResolutionKnown);
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
            Check(idle.presenceReads == 1 && idle.stateReads == 0
                    && idle.controllerTicks == 0,
                "stationary drafted pawn without marker skips state lookup");

            var moving = new Pawn { Drafted = true };
            moving.pather.Moving = true;
            RimKataDraftedFireController.ProcessJobTrackerTick(moving);
            Check(moving.presenceReads == 0 && moving.stateReads == 1
                && moving.controllerTicks == 1 && moving.controllerState == null
                && moving.controllerExistingStateKnown,
                "moving drafted pawn without state retains controller wake path");

            var retained = new Pawn {
                Drafted = true,
                presence = true,
                state = new RimKataPawnCombatState()
            };
            RimKataDraftedFireController.ProcessJobTrackerTick(retained);
            Check(retained.presenceReads == 1 && retained.stateReads == 1
                && retained.controllerTicks == 1 && retained.controllerState == retained.state,
                "existing combat state retains controller processing while stationary");

            var staleMarker = new Pawn { Drafted = true, presence = true };
            RimKataDraftedFireController.ProcessJobTrackerTick(staleMarker);
            Check(staleMarker.presenceReads == 1 && staleMarker.stateReads == 1
                    && staleMarker.controllerTicks == 0,
                "stale presence marker falls back to a safe null-state return");

            var followup = new Pawn {
                Drafted = true,
                presence = true,
                state = new RimKataPawnCombatState { dedicatedFollowupJobPending = true }
            };
            RimKataDraftedFireController.ProcessJobTrackerTick(followup);
            Check(followup.pendingConsumes == 1 && followup.controllerTicks == 1 && followup.order == "CT",
                "drafted pending followup is consumed before controller processing");

            var dedicated = new Pawn {
                Drafted = true,
                presence = true,
                CurJobDef = RimKataDefOf.RimKata_Attack,
                state = new RimKataPawnCombatState()
            };
            RimKataDraftedFireController.ProcessJobTrackerTick(dedicated);
            Check(dedicated.pendingReads == 1 && dedicated.stateReads == 0
                    && dedicated.presenceReads == 0 && dedicated.controllerTicks == 0,
                "dedicated combat Job skips state and drafted-fire controller work");

            var dedicatedFollowup = new Pawn {
                Drafted = true,
                pending = true,
                CurJobDef = RimKataDefOf.RimKata_Attack,
                state = new RimKataPawnCombatState {
                    dedicatedFollowupJobPending = true
                }
            };
            RimKataDraftedFireController.ProcessJobTrackerTick(dedicatedFollowup);
            Check(dedicatedFollowup.pendingReads == 1
                    && dedicatedFollowup.pendingConsumerCalls == 1
                    && dedicatedFollowup.consumerStateReads == 1
                    && dedicatedFollowup.pendingConsumes == 1
                    && dedicatedFollowup.stateReads == 0
                    && dedicatedFollowup.controllerTicks == 0,
                "dedicated combat Job still services a marked follow-up request");

            var stalePending = new Pawn {
                Drafted = true,
                pending = true,
                CurJobDef = RimKataDefOf.RimKata_Attack
            };
            RimKataDraftedFireController.ProcessJobTrackerTick(stalePending);
            RimKataDraftedFireController.ProcessJobTrackerTick(stalePending);
            Check(stalePending.pendingReads == 2
                    && stalePending.pendingConsumerCalls == 1
                    && stalePending.consumerStateReads == 1
                    && stalePending.pendingMarkerClears == 1
                    && stalePending.pendingConsumes == 0
                    && stalePending.controllerTicks == 0,
                "stale dedicated follow-up marker clears on its single consumer lookup");

            var undrafted = new Pawn {
                pending = true,
                state = new RimKataPawnCombatState {
                    dedicatedFollowupJobPending = true
                }
            };
            RimKataDraftedFireController.ProcessJobTrackerTick(undrafted);
            Check(undrafted.stateReads == 0 && undrafted.consumerStateReads == 1
                    && undrafted.pendingConsumes == 1 && undrafted.controllerTicks == 0,
                "undrafted event-driven followup path is preserved");

            var undraftedDedicated = new Pawn {
                pending = true,
                CurJobDef = RimKataDefOf.RimKata_Attack,
                state = new RimKataPawnCombatState {
                    dedicatedFollowupJobPending = true
                }
            };
            RimKataDraftedFireController.ProcessJobTrackerTick(undraftedDedicated);
            Check(undraftedDedicated.pendingReads == 1
                    && undraftedDedicated.consumerStateReads == 1
                    && undraftedDedicated.pendingConsumes == 1
                    && undraftedDedicated.controllerTicks == 0,
                "undrafted dedicated Job preserves marked follow-up handling");

            var mental = new Pawn { Drafted = true, InMentalState = true };
            RimKataDraftedFireController.ProcessJobTrackerTick(mental);
            Check(mental.pendingReads == 0 && mental.presenceReads == 0
                    && mental.stateReads == 0 && mental.controllerTicks == 0,
                "mental-state early return remains ahead of combat work");

            var assigned = new Thing();
            var resolved = new Thing();
            var dynamicResult = new Thing();
            var closePawn = new Pawn();
            var closeState = new RimKataPawnCombatState();

            RimKataDualWeaponController.closeResolverCalls = 0;
            Check(RimKataDualWeaponController.ResolveTickCloseTargetForTest(
                    closePawn, closeState, assigned, false, resolved, true) == null
                    && RimKataDualWeaponController.closeResolverCalls == 0,
                "disabled ordinary weapon ignores even a resolved close target");

            RimKataDualWeaponController.closeResolverCalls = 0;
            Check(RimKataDualWeaponController.ResolveTickCloseTargetForTest(
                    closePawn, closeState, assigned, true, null, true) == null
                    && RimKataDualWeaponController.closeResolverCalls == 0,
                "resolved null close target does not invoke dynamic resolution");

            RimKataDualWeaponController.closeResolverCalls = 0;
            Check(RimKataDualWeaponController.ResolveTickCloseTargetForTest(
                    closePawn, closeState, assigned, true, resolved, true) == resolved
                    && RimKataDualWeaponController.closeResolverCalls == 0,
                "resolved close target stays distinct from the assigned target");

            RimKataDualWeaponController.closeResolverCalls = 0;
            RimKataDualWeaponController.closeResolverResult = dynamicResult;
            Check(RimKataDualWeaponController.ResolveTickCloseTargetForTest(
                    closePawn, closeState, assigned, true, null, false) == dynamicResult
                    && RimKataDualWeaponController.closeResolverCalls == 1,
                "unknown close target retains exactly one dynamic resolution");

            return checks;
        }
    }
}
"@

Add-Type -TypeDefinition $rkHarness -Language CSharp
$rkPassed = [CombatDormancyChecks.Checks]::Run()
"PASS: $rkPassed executable dormancy assertions + 11 source-boundary checks; production tick method with minimal fixtures, not an in-game performance test."
