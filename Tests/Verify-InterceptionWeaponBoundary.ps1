$ErrorActionPreference = 'Stop'
$rkRoot = Split-Path -Parent $PSScriptRoot
$rkController = Get-Content -LiteralPath (Join-Path $rkRoot 'Source/RimKataDualWeaponController.cs') -Raw -Encoding UTF8
$rkEligibility = Get-Content -LiteralPath (Join-Path $rkRoot 'Source/RimKataEligibility.cs') -Raw -Encoding UTF8
$rkSearch = Get-Content -LiteralPath (Join-Path $rkRoot 'Source/RimKataSharedTargetSearch.cs') -Raw -Encoding UTF8
$rkCombat = Get-Content -LiteralPath (Join-Path $rkRoot 'Source/RimKataCombatState.cs') -Raw -Encoding UTF8
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
$rkQueue = Get-CSharpBlock $rkController 'public static void QueueIdleProjectileSearch('
$rkCanWake = Get-CSharpBlock $rkController 'internal static bool CanReceiveProjectileWake('
$rkBusyAttack = Get-CSharpBlock $rkController 'private static bool HasBusyAttackStance('
$rkCanStartWake = Get-CSharpBlock $rkController 'private static bool CanStartQueuedProjectileWake('
$rkClearQueued = Get-CSharpBlock $rkController 'private static void ClearQueuedInterceptionCandidate('
$rkCanConsumePending = Get-CSharpBlock $rkController 'private static bool CanConsumePendingDedicatedFollowupRequest('
$rkConsumePending = Get-CSharpBlock $rkController 'internal static void TryConsumePendingDedicatedFollowupJob('
$rkRejectedWake = Get-CSharpBlock $rkConsumePending 'if (!requestReady)'
$rkContinue = Get-CSharpBlock $rkController 'internal static bool CanContinueWeaponCycles('
$rkContinueInterception = Get-CSharpBlock $rkController 'internal static bool CanContinueProjectileInterception('
$rkActiveWork = Get-CSharpBlock $rkController 'private static bool HasActiveInterceptionWork('
$rkActiveTarget = Get-CSharpBlock $rkController 'private static bool IsActiveInterceptionTarget('
$rkCycleWork = Get-CSharpBlock $rkController 'private static bool HasCycleTargetWork('
$rkNormalizeWrapper = Get-CSharpBlock $rkController 'private static bool NormalizeUnavailableCycleWork('
$rkAfterWrapper = $rkController.Substring($rkController.IndexOf($rkNormalizeWrapper, [StringComparison]::Ordinal) + $rkNormalizeWrapper.Length)
$rkNormalize = Get-CSharpBlock $rkAfterWrapper 'private static bool NormalizeUnavailableCycleWork('
$rkClearTarget = Get-CSharpBlock $rkController 'private static void ClearTargetPreservingCycle('
$rkClearPlan = Get-CSharpBlock $rkController 'public void ClearPlan('
$rkClearCandidates = Get-CSharpBlock $rkController 'public void ClearAutomaticCandidates('
$rkValidPlan = Get-CSharpBlock $rkController 'private static bool ValidPlan('
$rkPrepare = Get-CSharpBlock $rkController 'private static bool PrepareCycle('
$rkCanBegin = Get-CSharpBlock $rkEligibility 'public static bool CanBeginGunKataAttack('
$rkCanIntercept = Get-CSharpBlock $rkEligibility 'internal static bool CanUseProjectileInterception('
$rkCanOperate = Get-CSharpBlock $rkEligibility 'private static bool CanOperateCombatWeapon('
$rkConscious = Get-CSharpBlock $rkEligibility 'private static bool IsConsciousAndMobile('
$rkSelect = Get-CSharpBlock $rkSearch 'internal static bool TrySelectCandidate('
$rkRange = Get-CSharpBlock $rkSearch 'private static float ProjectileRangeForCycle('
$rkCycle = Get-CSharpBlock $rkSearch 'private static RimKataWeaponCycleState CycleForVerb('
$rkAppend = Get-CSharpBlock $rkCombat 'internal void AppendValidHostileProjectiles('
$rkPrepareRules = @(
    '(?s)!ordinaryWeaponEnabled\s*&&\s*\(!\(verb is Verb_LaunchProjectile\).*?\|\| !HasActiveInterceptionWork\(pawn, cycle\)',
    'focusedTargetControlsCycle\s*=\s*ordinaryWeaponEnabled\s*&&\s*PrepareFocusedTarget',
    'dedicatedAssignedTarget\s*=\s*ordinaryWeaponEnabled\s*&&\s*assignedTarget',
    'directAssignedTarget\s*=\s*ordinaryWeaponEnabled\s*&&\s*assignedTarget'
)
foreach ($rkRule in $rkPrepareRules) {
    if ($rkPrepare -notmatch $rkRule) { throw "PrepareCycle source boundary missing: $rkRule" }
}
$rkQueuedDropRules = @(
    '(?s)state.projectileWakeResumeJob != null\s*&&\s*state.projectileWakeResumeJob == sourceJob',
    '(?s)ConsumeIdleProjectileSearchTrigger\(\).*?ClearQueuedInterceptionCandidate\(state.primaryWeaponCycle\).*?ClearQueuedInterceptionCandidate\(state.secondaryWeaponCycle\).*?state.projectileWakeResumeJob = null',
    '\A(?![\s\S]*(?:ClearPlan|ClearAutomaticCandidates)\()[\s\S]*\z'
)
foreach ($rkRule in $rkQueuedDropRules) {
    if ($rkRejectedWake -notmatch $rkRule) { throw "Rejected queued interception cleanup boundary missing: $rkRule" }
}

# Production eligibility, queue, candidate selection, range boundary and append
# methods execute together. Engine, trajectory math and normal-target validation
# are fixtures with counters: this verifies policy/dispatch, not live combat.
$rkHarness = @"
using System;
using System.Collections.Generic;
namespace InterceptionWeaponBoundaryChecks {
    public enum WorkTags { Violent }
    public struct IntVec3 { public static IntVec3 Invalid = new IntVec3(); }
    public class Thing { public Map Map; public bool valid = true, Spawned = true, Destroyed; }
    public sealed class ThingDef { public bool allowed = true; }
    public sealed class ThingWithComps : Thing { public ThingDef def = new ThingDef(); public Verb verb; }
    public class Verb { public Thing EquipmentSource; public bool IsMeleeAttack, Bursting, usable = true; }
    public sealed class Verb_LaunchProjectile : Verb { }
    public sealed class Projectile : Thing { public bool hostile = true; }
    public sealed class JobDef { public string defName; }
    public struct LocalTargetInfo { public Thing Thing; }
    public sealed class Job { public bool playerForced, killIncappedTarget; public JobDef def; public LocalTargetInfo targetA; public ThinkNode jobGiver; }
    public sealed class DutyDef { }
    public static class DutyDefOf { public static readonly DutyDef AssaultColony = new DutyDef(); }
    public sealed class PawnDuty { public DutyDef def; }
    public sealed class MindState { public PawnDuty duty; public Thing enemyTarget; }
    public class Stance { }
    public class Stance_Busy : Stance { public Verb verb; public int ticksLeft; }
    public sealed class Stance_Warmup : Stance_Busy { }
    public sealed class PawnStanceTracker { public Stance curStance; }
    public class ThinkNode { }
    public sealed class JobGiver_AIFightEnemy : ThinkNode { }
    public sealed class JobGiver_AIGotoTarget : ThinkNode { }
    public sealed class JobGiver_AIGotoNearestHostile : ThinkNode { }
    public class LordToil { }
    public sealed class LordToil_AssaultColonySappers : LordToil { }
    public sealed class LordToil_AssaultColonyBreaching : LordToil { }
    public sealed class Lord { public LordToil CurLordToil; }
    public static class JobDefOf {
        public static readonly JobDef Wait = new JobDef { defName = "Wait" };
        public static readonly JobDef Wait_Combat = new JobDef { defName = "Wait_Combat" };
        public static readonly JobDef Goto = new JobDef { defName = "Goto" };
        public static readonly JobDef AttackStatic = new JobDef { defName = "AttackStatic" };
        public static readonly JobDef AttackMelee = new JobDef { defName = "AttackMelee" };
    }
    public sealed class Pather { public bool MovingNow; }
    public sealed class Drafter { public bool FireAtWill = true; }
    public sealed class CarryTracker { public object CarriedThing; }
    public sealed class Pawn : Thing {
        public bool IsPlayerControlled = true, access = true, awake = true;
        public bool Dead, Downed, InMentalState, burning, Drafted, inactive, violenceDisabled, continuity;
        public bool secondaryAllowed = true;
        public int verbReads, rangeReads, trajectoryChecks, candidateChecks, binds, followups, caches, interrupted;
        public Pather pather = new Pather();
        public Drafter drafter = new Drafter();
        public CarryTracker carryTracker = new CarryTracker();
        public MindState mindState = new MindState();
        public PawnStanceTracker stances = new PawnStanceTracker();
        public Lord lord;
        public Lord GetLord() { return lord; }
        public Job CurJob = new Job { def = JobDefOf.Wait };
        public JobDef CurJobDef { get { return CurJob == null ? null : CurJob.def; } }
        public ThingWithComps primary, secondary;
        public RimKataPawnCombatState state;
        public IntVec3 Position;
        public bool Awake() { return awake; }
        public bool IsBurning() { return burning; }
        public bool WorkTagIsDisabled(WorkTags tag) { return violenceDisabled; }
    }
    public sealed class Settings { public bool explosiveInterceptionEnabled = true, randomAttackEnabled; }
    public static class RimKataMod { public static Settings Settings = new Settings(); }
    public static class RimKataEligibility {
        public static bool RandomAttackEnabledForPawn(Pawn pawn) { return RimKataMod.Settings.randomAttackEnabled; }
        public static bool HasActiveRimKataAccess(Pawn pawn) {
            return pawn != null && pawn.access && !pawn.inactive;
        }
        $rkCanBegin
        $rkCanIntercept
        $rkCanOperate
        $rkConscious
    }
    public static class RimKataEquipmentUtility {
        public static bool IsPrimaryWeaponEnabled(Pawn pawn) { return pawn?.primary?.def.allowed == true; }
        public static bool IsWeaponEnabled(ThingDef def) { return def != null && def.allowed; }
    }
    public static class RimKataWeaponSlotUtility {
        public static ThingWithComps PrimaryWeapon(Pawn pawn) { return pawn.primary; }
        public static ThingWithComps SecondaryWeapon(Pawn pawn) { return pawn.secondary; }
        public static bool CanUseSecondarySlot(Pawn pawn) { return pawn.secondaryAllowed; }
        public static Verb CombatVerb(Pawn pawn, ThingWithComps weapon) { pawn.verbReads++; return weapon?.verb; }
    }
    public static class RimKataRangeUtility {
        public static float ResolveEffectiveRange(Pawn pawn, ThingWithComps weapon, Verb verb) {
            pawn.rangeReads++; return 30f;
        }
    }
    public static class RimKataTargeting {
        public static bool IsInterceptionTargetActive(Projectile target) { return target?.valid == true; }
        public static bool IsPawnTargetStateValid(Pawn target, bool explicitDowned = false) { return target.valid; }
        public static bool IsAutomaticEnemy(Pawn pawn, Thing target) { return target.valid; }
        public static bool IsValidAutomaticAttackTarget(Pawn pawn, Thing target) { return target?.valid == true; }
        public static bool IsValidExplosiveProjectileForVerb(Pawn pawn, Verb verb, Projectile projectile, float rangeSquared) {
            pawn.candidateChecks++; return projectile.valid && projectile.hostile && projectile.Map == pawn.Map;
        }
    }
    public static class RimKataInterceptionTrajectory {
        public static bool CanIntercept(Pawn pawn, Verb verb, Projectile target, int delay, float rangeSquared) {
            pawn.trajectoryChecks++; return true;
        }
    }
    public sealed class Map {
        public readonly RimKataMapComponent component;
        public Map() { component = new RimKataMapComponent(this); }
        public T GetComponent<T>() where T : class { return component as T; }
    }
    public sealed class RimKataMapComponent {
        private readonly Map map;
        public readonly List<Projectile> activeExplosiveProjectiles = new List<Projectile>();
        public RimKataMapComponent(Map map) { this.map = map; }
        public bool HasActiveExplosiveProjectiles { get { return activeExplosiveProjectiles.Count != 0; } }
        public bool HasHostileExplosiveProjectileOnMapFor(Pawn pawn) {
            return activeExplosiveProjectiles.Exists(p => p.valid && p.hostile && p.Map == pawn.Map);
        }
        $rkAppend
    }
    public sealed class RimKataWeaponCycleState {
        public ThingWithComps weapon;
        public bool cachedCandidateInterception;
        public bool plannedInterception;
        public Thing cachedCandidateTarget;
        public Thing plannedTarget;
        public List<Thing> automaticCandidates = new List<Thing>();
        public bool automaticCandidateCollectionClosed, focusedTargetFromAttackGizmo, plannedCloseContext, plannedCloseAttack;
        public bool openingWarmupPending, firedInCurrentOpening;
        public int pendingCandidateLimitOverride, activeCandidateLimitOverride, warmupTotalTicks;
        public int warmupTicksRemaining = -1, burstShotsRemaining, burstTicksUntilNextShot, openingWarmupBonusTicks, visualAimTicksRemaining;
        public Thing focusedTarget, lastFiredTarget, visualTarget;
        public Verb plannedActionVerb;
        public IntVec3 plannedTargetCell;
        public bool HasPlan { get { return plannedTarget != null; } }
        public bool HasAutomaticCandidates { get { return automaticCandidates != null && automaticCandidates.Count > 0; } }
        public bool DedicatedActive { get { return weapon != null
            && (cachedCandidateTarget != null || HasAutomaticCandidates
                || focusedTarget != null || HasPlan); } }
        $rkClearPlan
        $rkClearCandidates
    }
    public sealed class RimKataPawnCombatState {
        public RimKataWeaponCycleState primaryWeaponCycle = new RimKataWeaponCycleState();
        public RimKataWeaponCycleState secondaryWeaponCycle = new RimKataWeaponCycleState();
        public bool idleProjectileSearchTriggerPending, dedicatedFollowupJobPending, dualCloseCombatActive;
        public int dedicatedFollowupJobRequestedTick;
        public Job dedicatedFollowupJobSourceJob;
        public Thing dedicatedFollowupJobTarget;
        public bool dedicatedFollowupJobPlayerForced;
        public Job projectileWakeResumeJob;
        public void QueueIdleProjectileSearchTrigger() { idleProjectileSearchTriggerPending = true; }
        public void ConsumeIdleProjectileSearchTrigger() { idleProjectileSearchTriggerPending = false; }
        public void ResetCandidateSaturationExpansion(bool value) { }
    }
    public static class Rand { public static int Range(int minimum, int maximum) { return minimum; } }
    public static class RimKataSharedTargetSearch {
        public static bool IsValidForVerb(Pawn pawn, Verb verb, Thing target) { return target?.valid == true && target.Map == pawn.Map; }
        private static readonly List<Thing> EligibleCandidates = new List<Thing>();
        private static bool RandomAttackEnabled(Pawn pawn) { return RimKataMod.Settings.randomAttackEnabled; }
        private static bool IsCloseCombatContext(RimKataPawnCombatState state) { return state.dualCloseCombatActive; }
        private static void Restart(Pawn pawn, RimKataPawnCombatState state, IntVec3 position) { }
        private static bool RemoveAutomaticCandidate(RimKataPawnCombatState state, RimKataWeaponCycleState cycle, Thing target, bool global) {
            return cycle.automaticCandidates.Remove(target);
        }
        private static bool IsValidAutomaticTargetForCycle(Pawn pawn, RimKataPawnCombatState state, RimKataWeaponCycleState cycle, Verb verb, Thing target) {
            return RimKataEquipmentUtility.IsWeaponEnabled(cycle.weapon.def) && target.valid;
        }
        $rkSelect
        $rkRange
        $rkCycle
    }
    public static class RimKataDualWeaponController {
        public static bool CanConsumePendingForCheck(Pawn pawn) { return CanConsumePendingDedicatedFollowupRequest(pawn, pawn.state, 0); }
        public static void ClearQueuedForCheck(RimKataWeaponCycleState cycle) { ClearQueuedInterceptionCandidate(cycle); }
        private static bool CanConsumeDedicatedFollowupRequest(Pawn pawn, Job source, Thing target) { return true; }
        public static bool NormalizeForCheck(Pawn pawn) {
            return NormalizeUnavailableCycleWork(pawn, pawn.state, pawn.state.primaryWeaponCycle);
        }
        public static bool HasWorkForCheck(Pawn pawn) { return HasCycleTargetWork(pawn, pawn.state, pawn.state.primaryWeaponCycle); }
        public static bool ValidPlanForCheck(Pawn pawn) {
            return ValidPlan(pawn, pawn.state.primaryWeaponCycle, pawn.primary.verb, null, false, false, false);
        }
        private static bool FocusedTargetUsableNow(Pawn pawn, RimKataWeaponCycleState cycle, Verb verb, bool close) {
            return cycle.focusedTarget?.valid == true;
        }
        private static bool ValidCurrentTargetForVerb(Pawn pawn, Verb verb, Thing target, bool forced, bool downed, bool close) {
            return target?.valid == true;
        }
        private static bool CanHitTargetForCombatContext(Pawn pawn, Verb verb, Thing target, bool close) { return target.valid; }
        private static void ApplyInterruptedBurstCooldown(Pawn pawn, RimKataWeaponCycleState cycle, Verb verb) { pawn.interrupted++; }
        public static bool VerbUsable(Pawn pawn, Verb verb, bool requireMelee) { return verb?.usable == true; }
        private static RimKataPawnCombatState StateFor(Pawn pawn, bool create) {
            if (pawn.state == null && create) pawn.state = new RimKataPawnCombatState();
            return pawn.state;
        }
        private static void NormalizeInvalidInterceptionState(Pawn pawn, RimKataPawnCombatState state) { }
        private static bool HasCombatContinuity(Pawn pawn, RimKataPawnCombatState state) { return pawn.continuity; }
        private static void BindCurrentWeapons(Pawn pawn, RimKataPawnCombatState state) {
            pawn.binds++;
            state.primaryWeaponCycle.weapon = pawn.primary;
            state.secondaryWeaponCycle.weapon = pawn.secondary;
        }
        private static void TryCacheSharedCandidate(Pawn pawn, RimKataPawnCombatState state, RimKataWeaponCycleState cycle, Thing preferred) {
            pawn.caches++;
            Thing target; bool interception;
            if (RimKataSharedTargetSearch.TrySelectCandidate(pawn, state, cycle.weapon?.verb, preferred, out target, out interception)) {
                cycle.cachedCandidateTarget = target;
                cycle.cachedCandidateInterception = interception;
            }
        }
        private static void RefreshDualEngagementState(Pawn pawn, RimKataPawnCombatState state) { }
        private static void QueueDedicatedFollowupJob(Pawn pawn, Thing target) {
            pawn.followups++;
            pawn.state.dedicatedFollowupJobPending = true;
        }
        $rkQueue
        $rkCanWake
        $rkBusyAttack
        $rkCanStartWake
        $rkClearQueued
        $rkCanConsumePending
        $rkContinue
        $rkContinueInterception
        $rkActiveWork
        $rkActiveTarget
        $rkCycleWork
        $rkNormalizeWrapper
        $rkNormalize
        $rkClearTarget
        $rkValidPlan
    }
    public static class Checks {
        private static int checks;
        private static void Check(bool value, string name) {
            if (!value) throw new Exception(name);
            checks++;
        }
        private static Pawn Create(bool allowed, bool withProjectile) {
            RimKataMod.Settings = new Settings();
            var pawn = new Pawn { Map = new Map(), state = new RimKataPawnCombatState() };
            pawn.primary = new ThingWithComps { def = new ThingDef { allowed = allowed } };
            pawn.primary.verb = new Verb_LaunchProjectile { EquipmentSource = pawn.primary };
            pawn.state.primaryWeaponCycle.weapon = pawn.primary;
            if (withProjectile) pawn.Map.component.activeExplosiveProjectiles.Add(new Projectile { Map = pawn.Map });
            return pawn;
        }
        private static bool Select(Pawn pawn, Thing preferred, out Thing target, out bool interception) {
            return RimKataSharedTargetSearch.TrySelectCandidate(pawn, pawn.state, pawn.primary.verb, preferred, out target, out interception);
        }
        public static int Run() {
            var unapproved = Create(false, true);
            Check(!RimKataEligibility.CanBeginGunKataAttack(unapproved), "Unapproved ordinary attack remains forbidden");
            Check(RimKataEligibility.CanUseProjectileInterception(unapproved), "Unapproved weapon can enter interception eligibility");
            RimKataDualWeaponController.QueueIdleProjectileSearch(unapproved);
            Check(unapproved.state.primaryWeaponCycle.cachedCandidateInterception && unapproved.followups == 1,
                "Unapproved gun wakes to intercept with random attack disabled");
            Check(unapproved.rangeReads == 1 && unapproved.trajectoryChecks == 1, "Accepted projectile range and crossing checked once");
            Check(unapproved.state.projectileWakeResumeJob == unapproved.CurJob, "Existing idle resume behavior retained");
            Check(RimKataDualWeaponController.CanContinueWeaponCycles(unapproved, unapproved.state),
                "Existing unapproved primary interception keeps the shared cycle alive");
            unapproved.state.primaryWeaponCycle.cachedCandidateInterception = false;
            Check(!RimKataDualWeaponController.CanContinueWeaponCycles(unapproved, unapproved.state),
                "No actual interception means no unapproved continuation permission");
            unapproved.state.primaryWeaponCycle.plannedInterception = true;
            unapproved.state.primaryWeaponCycle.plannedTarget = new Projectile { Map = unapproved.Map };
            Check(RimKataDualWeaponController.CanContinueWeaponCycles(unapproved, unapproved.state),
                "Planned live interception retains continuation after candidate promotion");
            unapproved.state.primaryWeaponCycle.plannedTarget.valid = false;
            Check(!RimKataDualWeaponController.CanContinueWeaponCycles(unapproved, unapproved.state),
                "Dead interception target cannot authorize unapproved continuation");

            var randomIdle = Create(false, true);
            RimKataMod.Settings.randomAttackEnabled = true;
            randomIdle.state.primaryWeaponCycle.automaticCandidates.Add(new Thing());
            RimKataDualWeaponController.QueueIdleProjectileSearch(randomIdle);
            Check(randomIdle.state.primaryWeaponCycle.cachedCandidateInterception && randomIdle.followups == 1,
                "Unapproved idle gun intercepts with random attack enabled without selecting ordinary candidate");

            var empty = Create(false, false);
            RimKataDualWeaponController.QueueIdleProjectileSearch(empty);
            Check(empty.verbReads == 0 && empty.rangeReads == 0 && empty.trajectoryChecks == 0,
                "Empty map skips candidate Verb, range and trajectory work");
            Check(empty.binds == 0 && empty.caches == 0 && empty.followups == 0, "Empty map does not create an interception cycle");
            empty.state.idleProjectileSearchTriggerPending = true;
            Thing target; bool interception;
            Check(!Select(empty, null, out target, out interception), "Empty shared selection returns no target");
            Check(empty.rangeReads == 0 && empty.trajectoryChecks == 0, "Direct shared selection also skips empty-map range work");
            var emptyAllowed = Create(true, false);
            RimKataDualWeaponController.QueueIdleProjectileSearch(emptyAllowed);
            Check(emptyAllowed.verbReads == 0 && emptyAllowed.rangeReads == 0 && emptyAllowed.trajectoryChecks == 0,
                "Approved gun also performs no empty-map interception work");

            var targetlessRetained = Create(true, false);
            Check(!RimKataDualWeaponController.HasWorkForCheck(targetlessRetained),
                "Targetless retained weapon cycle contributes no combat work");
            Check(targetlessRetained.verbReads == 0,
                "Targetless retained weapon cycle skips CombatVerb resolution");

            var preferred = Create(false, false);
            var ordinary = new Thing();
            Check(!Select(preferred, ordinary, out target, out interception), "Unapproved preferred ordinary target rejected");
            RimKataMod.Settings.randomAttackEnabled = true;
            preferred.state.primaryWeaponCycle.automaticCandidates.Add(ordinary);
            Check(!Select(preferred, null, out target, out interception), "Unapproved cached ordinary candidate rejected");

            var allowed = Create(true, true);
            Check(RimKataEligibility.CanBeginGunKataAttack(allowed), "Approved normal attack eligibility retained");
            Check(RimKataDualWeaponController.CanContinueWeaponCycles(allowed, allowed.state),
                "Approved combat continuity does not require an interception target");
            RimKataDualWeaponController.QueueIdleProjectileSearch(allowed);
            Check(allowed.state.primaryWeaponCycle.cachedCandidateInterception && allowed.followups == 1,
                "Approved idle interception retained");
            allowed.state.idleProjectileSearchTriggerPending = false;
            Check(Select(allowed, ordinary, out target, out interception) && target == ordinary && !interception,
                "Approved explicit normal target retained with random attack disabled");
            RimKataMod.Settings.randomAttackEnabled = true;
            allowed.state.primaryWeaponCycle.automaticCandidates.Add(ordinary);
            Check(Select(allowed, null, out target, out interception) && target == ordinary && !interception,
                "Approved random normal candidate retained");

            var ai = Create(false, true); ai.IsPlayerControlled = false;
            RimKataDualWeaponController.QueueIdleProjectileSearch(ai);
            Check(ai.binds == 0 && ai.followups == 0, "Out-of-combat AI still cannot wake to intercept");
            var assaultAI = Create(false, true); assaultAI.IsPlayerControlled = false;
            assaultAI.mindState.duty = new PawnDuty { def = DutyDefOf.AssaultColony };
            assaultAI.CurJob.def = JobDefOf.Goto;
            RimKataDualWeaponController.QueueIdleProjectileSearch(assaultAI);
            Check(assaultAI.state.primaryWeaponCycle.cachedCandidateInterception && assaultAI.followups == 1
                && !RimKataEligibility.CanBeginGunKataAttack(assaultAI),
                "Combat-ready AI unapproved gun gains interception only with random attack disabled");
            var preparingAI = Create(false, true); preparingAI.IsPlayerControlled = false;
            preparingAI.pather.MovingNow = true; preparingAI.CurJob.def = new JobDef { defName = "GotoWander" };
            RimKataDualWeaponController.QueueIdleProjectileSearch(preparingAI);
            Check(preparingAI.verbReads == 0 && preparingAI.rangeReads == 0 && preparingAI.trajectoryChecks == 0,
                "Preparing AI performs no interception candidate work");
            var pendingAI = Create(false, true); pendingAI.IsPlayerControlled = false;
            pendingAI.mindState.duty = new PawnDuty { def = DutyDefOf.AssaultColony };
            pendingAI.state.dedicatedFollowupJobPending = true;
            pendingAI.state.projectileWakeResumeJob = pendingAI.CurJob;
            pendingAI.state.dedicatedFollowupJobSourceJob = pendingAI.CurJob;
            pendingAI.stances.curStance = new Stance_Warmup { verb = pendingAI.primary.verb, ticksLeft = 10 };
            Check(!RimKataDualWeaponController.CanConsumePendingForCheck(pendingAI),
                "Actual pending-request gate rejects late aim for matching interception source");
            pendingAI.state.projectileWakeResumeJob = null;
            Check(RimKataDualWeaponController.CanConsumePendingForCheck(pendingAI),
                "Late-aim interception guard is not applied to an ordinary followup request");
            pendingAI.state.projectileWakeResumeJob = new Job { def = JobDefOf.Wait };
            Check(RimKataDualWeaponController.CanConsumePendingForCheck(pendingAI),
                "A different resume source does not broaden the interception-only guard");
            pendingAI.state.projectileWakeResumeJob = pendingAI.CurJob;
            ((Stance_Busy)pendingAI.stances.curStance).ticksLeft = 0;
            Check(RimKataDualWeaponController.CanConsumePendingForCheck(pendingAI),
                "Matching queued interception resumes once attack stance expires");
            var ordinaryCache = new Thing { Map = pendingAI.Map };
            var retainedPlan = new Thing { Map = pendingAI.Map };
            var clearCycle = pendingAI.state.primaryWeaponCycle;
            clearCycle.cachedCandidateTarget = ordinaryCache;
            clearCycle.plannedTarget = retainedPlan; clearCycle.warmupTicksRemaining = 11;
            clearCycle.automaticCandidates.Add(ordinaryCache);
            RimKataDualWeaponController.ClearQueuedForCheck(clearCycle);
            Check(clearCycle.cachedCandidateTarget == ordinaryCache && clearCycle.plannedTarget == retainedPlan
                && clearCycle.warmupTicksRemaining == 11 && clearCycle.automaticCandidates.Count == 1,
                "Queued-interception cleanup leaves normal cache, plan, warmup and candidate list untouched");
            clearCycle.cachedCandidateTarget = pendingAI.Map.component.activeExplosiveProjectiles[0];
            clearCycle.cachedCandidateInterception = true;
            RimKataDualWeaponController.ClearQueuedForCheck(clearCycle);
            Check(clearCycle.cachedCandidateTarget == null && !clearCycle.cachedCandidateInterception
                && clearCycle.plannedTarget == retainedPlan && clearCycle.warmupTicksRemaining == 11
                && clearCycle.automaticCandidates.Count == 1,
                "Queued-interception cleanup removes only interception cache and flag");
            var disabled = Create(false, true); RimKataMod.Settings.explosiveInterceptionEnabled = false;
            RimKataDualWeaponController.QueueIdleProjectileSearch(disabled);
            Check(disabled.verbReads == 0 && disabled.rangeReads == 0 && disabled.followups == 0,
                "Disabled interception avoids candidate work");
            var inactive = Create(false, true); inactive.inactive = true;
            Check(!RimKataEligibility.CanUseProjectileInterception(inactive), "Inactive pawn cannot intercept");
            var noAccess = Create(false, true); noAccess.access = false;
            Check(!RimKataEligibility.CanUseProjectileInterception(noAccess), "Nonqualified pawn cannot intercept");
            var pacifist = Create(false, true); pacifist.violenceDisabled = true;
            Check(!RimKataEligibility.CanUseProjectileInterception(pacifist), "Violence restriction retained");

            var aiming = Create(false, true);
            var cycle = aiming.state.primaryWeaponCycle;
            var projectile = aiming.Map.component.activeExplosiveProjectiles[0];
            cycle.plannedTarget = cycle.visualTarget = projectile;
            cycle.plannedInterception = true;
            cycle.warmupTicksRemaining = 17; cycle.warmupTotalTicks = 30; cycle.visualAimTicksRemaining = 17;
            Check(!RimKataDualWeaponController.NormalizeForCheck(aiming), "Clean unapproved interception plan does not normalize away");
            Check(cycle.plannedTarget == projectile && cycle.plannedInterception && cycle.warmupTicksRemaining == 17
                && cycle.warmupTotalTicks == 30 && cycle.visualTarget == projectile && cycle.visualAimTicksRemaining == 17,
                "Interception aim, target, warmup and visual remain unchanged");
            Check(aiming.interrupted == 0, "Preserved interception incurs no interruption cooldown");
            Check(RimKataDualWeaponController.HasWorkForCheck(aiming), "Planned unapproved interception contributes real cycle work");
            Check(RimKataDualWeaponController.ValidPlanForCheck(aiming), "Unapproved projectile plan passes actual ValidPlan boundary");

            var mixed = Create(false, true);
            cycle = mixed.state.primaryWeaponCycle;
            projectile = mixed.Map.component.activeExplosiveProjectiles[0];
            var stale = new Thing { Map = mixed.Map };
            cycle.cachedCandidateTarget = projectile; cycle.cachedCandidateInterception = true;
            cycle.plannedTarget = cycle.focusedTarget = cycle.visualTarget = stale;
            cycle.focusedTargetFromAttackGizmo = true;
            cycle.warmupTicksRemaining = 12; cycle.warmupTotalTicks = 20;
            cycle.automaticCandidates.Add(stale);
            RimKataMod.Settings.randomAttackEnabled = true;
            Check(RimKataDualWeaponController.NormalizeForCheck(mixed), "Mixed stale ordinary work is reported changed");
            Check(cycle.cachedCandidateTarget == projectile && cycle.cachedCandidateInterception,
                "Cached interception survives ordinary work cleanup");
            Check(!cycle.HasPlan && cycle.focusedTarget == null && !cycle.focusedTargetFromAttackGizmo
                && !cycle.HasAutomaticCandidates && cycle.warmupTicksRemaining == -1 && cycle.warmupTotalTicks == 0,
                "Unapproved ordinary plan, focus, candidates and warmup are removed");
            Check(cycle.visualTarget == null && mixed.interrupted == 0,
                "Stale ordinary aim is cleared without interrupting cached interception");
            Check(RimKataDualWeaponController.HasWorkForCheck(mixed), "Cached interception still contributes actual cycle work");

            var ordinaryOnly = Create(false, false);
            cycle = ordinaryOnly.state.primaryWeaponCycle;
            cycle.plannedTarget = new Thing { Map = ordinaryOnly.Map };
            cycle.warmupTicksRemaining = 10; cycle.warmupTotalTicks = 15;
            Check(!RimKataDualWeaponController.ValidPlanForCheck(ordinaryOnly), "Actual ValidPlan blocks unapproved ordinary plan");
            Check(!RimKataDualWeaponController.HasWorkForCheck(ordinaryOnly), "Ordinary unapproved plan cannot keep cycle alive");
            Check(RimKataDualWeaponController.NormalizeForCheck(ordinaryOnly) && !cycle.HasPlan
                && cycle.warmupTicksRemaining == -1 && ordinaryOnly.interrupted == 1,
                "Unapproved ordinary-only cycle is fully cleared");

            var approvedPlan = Create(true, false);
            cycle = approvedPlan.state.primaryWeaponCycle;
            cycle.plannedTarget = new Thing { Map = approvedPlan.Map };
            cycle.warmupTicksRemaining = 9; cycle.warmupTotalTicks = 14;
            Check(!RimKataDualWeaponController.NormalizeForCheck(approvedPlan) && cycle.HasPlan && cycle.warmupTicksRemaining == 9,
                "Approved ordinary warmup remains unchanged");
            Check(RimKataDualWeaponController.ValidPlanForCheck(approvedPlan) && RimKataDualWeaponController.HasWorkForCheck(approvedPlan),
                "Approved ordinary validation and actual cycle work remain true");

            var interceptionOff = Create(false, true);
            cycle = interceptionOff.state.primaryWeaponCycle;
            cycle.cachedCandidateTarget = interceptionOff.Map.component.activeExplosiveProjectiles[0];
            cycle.cachedCandidateInterception = true;
            RimKataMod.Settings.explosiveInterceptionEnabled = false;
            Check(RimKataDualWeaponController.NormalizeForCheck(interceptionOff) && cycle.cachedCandidateTarget == null
                && !cycle.cachedCandidateInterception, "Disabled interception clears unapproved cached work");
            return checks;
        }
    }
}
"@
Add-Type -TypeDefinition $rkHarness -Language CSharp
$rkPassed = [InterceptionWeaponBoundaryChecks.Checks]::Run()
"PASS: $rkPassed executable interception boundary assertions + $($rkPrepareRules.Count) PrepareCycle / $($rkQueuedDropRules.Count) rejected-wake source-boundary checks; production methods with engine/trajectory fixtures, not an in-game test."
