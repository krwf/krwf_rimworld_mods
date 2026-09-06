$ErrorActionPreference = 'Stop'
$rkRoot = Split-Path -Parent $PSScriptRoot
$rkCombat = Get-Content -LiteralPath (Join-Path $rkRoot 'Source/RimKataCombatState.cs') -Raw -Encoding UTF8
$rkController = Get-Content -LiteralPath (Join-Path $rkRoot 'Source/RimKataDualWeaponController.cs') -Raw -Encoding UTF8

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
$rkCanWakeNow = Get-CSharpBlock $rkController 'internal static bool CanReceiveIdleProjectileWakeNow('
$rkProjectileWakeRange = Get-CSharpBlock $rkController 'internal static float ProjectileWakeRange('
$rkTrySeed = Get-CSharpBlock $rkController 'private static bool TrySeedIdleProjectileCandidate('
$rkBusyAttack = Get-CSharpBlock $rkController 'private static bool HasBusyAttackStance('
$rkCanStartWake = Get-CSharpBlock $rkController 'private static bool CanStartQueuedProjectileWake('
$rkTraversal = Get-CSharpBlock $rkCombat 'private void StartProjectileWakeTraversal()'
$rkPotentialWake = Get-CSharpBlock $rkCombat 'private bool CanPotentiallyWakeForProjectile('
$rkPotentialWakeRange = Get-CSharpBlock $rkCombat 'private static float PotentialProjectileWakeRange('
$rkExactProjectileProbe = Get-CSharpBlock $rkCombat 'internal bool TryGetValidHostileProjectile('
$rkScheduler = Get-CSharpBlock $rkCombat 'private void TickProjectileScheduler()'
if ($rkController.Contains('CanAcceptIdleProjectileSearch(') -or $rkCombat.Contains('CanAcceptIdleProjectileSearch(')) {
    throw 'The duplicate CanAcceptIdleProjectileSearch path remains.'
}
if ($rkScheduler.Contains('HasHostileExplosiveProjectileOnMapFor(pawn)') -or
    [regex]::Matches($rkScheduler, 'QueueIdleProjectileSearch\(\s*pawn\)').Count -ne 1 -or
    $rkScheduler -notmatch 'projectileWakeTraversalIndex\s*<\s*projectileWakeTraversal\.Count[\s\S]*?projectileWakeTraversalIndex\+\+[\s\S]*?QueueIdleProjectileSearch\(pawn\)' -or
    $rkScheduler -match '\b(?:for|while)\s*\(') {
    throw 'Scheduler must queue at most one prefiltered pawn per tick without repeating the hostile-only probe.'
}
if (-not $rkQueue.Contains('HasCombatContinuity(pawn, state)')) {
    throw 'Combat continuity must reuse the previously read state.'
}
$rkWakePrefilter = $rkTraversal + $rkPotentialWake + $rkPotentialWakeRange
if ($rkWakePrefilter.Contains('CanIntercept(') -or
    $rkWakePrefilter.Contains('TryFindShootLineFromTo(') -or
    $rkWakePrefilter.Contains('TryGetValidHostileProjectile(') -or
    $rkWakePrefilter.Contains('StateFor(')) {
    throw 'Wake prefilter must not perform exact line or trajectory validation.'
}
if (-not $rkPotentialWake.Contains('SecondaryWeaponWithVerifiedAccess(') -or
    -not $rkPotentialWake.Contains('CanReceiveIdleProjectileWakeNow(pawn)') -or
    -not $rkPotentialWake.Contains('IsPotentialExplosiveProjectile(') -or
    -not $rkPotentialWake.Contains('IsEnemyProjectileLauncher(') -or
    -not $rkPotentialWake.Contains('DistanceToSquared(') -or
    [regex]::Matches(
        $rkPotentialWake + $rkQueue,
        'CanReceiveIdleProjectileWakeNow\(pawn\)').Count -ne 2 -or
    $rkCanWakeNow -match 'StateFor\(|GetState\(|HasCombatContinuity\(') {
    throw 'Traversal and queue must share one state-free receiver gate, then prefilter registered weapons by cheap hostility and range checks.'
}

$rkPrimaryProbeIndex = $rkQueue.IndexOf(
    'mapComponent.TryGetValidHostileProjectile(',
    [StringComparison]::Ordinal)
$rkSecondaryProbeIndex = if ($rkPrimaryProbeIndex -ge 0) {
    $rkQueue.IndexOf(
        'mapComponent.TryGetValidHostileProjectile(',
        $rkPrimaryProbeIndex + 1,
        [StringComparison]::Ordinal)
} else { -1 }
$rkNoExactCandidateIndex = $rkQueue.IndexOf(
    'if (primaryProjectile == null && secondaryProjectile == null)',
    [StringComparison]::Ordinal)
$rkStateCreateIndex = $rkQueue.IndexOf(
    'StateFor(pawn, true)',
    [StringComparison]::Ordinal)
if ($rkExactProjectileProbe -notmatch 'IsValidExplosiveProjectileForVerb\(' -or
    $rkExactProjectileProbe -notmatch 'RimKataInterceptionTrajectory\.CanIntercept\(' -or
    $rkPrimaryProbeIndex -lt 0 -or
    $rkSecondaryProbeIndex -le $rkPrimaryProbeIndex -or
    $rkNoExactCandidateIndex -le $rkSecondaryProbeIndex -or
    $rkStateCreateIndex -le $rkNoExactCandidateIndex) {
    throw 'Combat state may be created only after a primary or registered-secondary exact projectile/trajectory probe succeeds.'
}

if ($rkQueue -notmatch 'SecondaryWeaponWithVerifiedAccess\(pawn\)' -or
    $rkQueue -notmatch 'TrySeedIdleProjectileCandidate\([\s\S]*?state\.primaryWeaponCycle,[\s\S]*?primary,[\s\S]*?primaryProjectile\)' -or
    $rkQueue -notmatch 'TrySeedIdleProjectileCandidate\([\s\S]*?state\.secondaryWeaponCycle,[\s\S]*?secondary,[\s\S]*?secondaryProjectile\)' -or
    $rkTrySeed -notmatch 'cycle\.weapon\s*!=\s*expectedWeapon' -or
    $rkTrySeed -notmatch 'cycle\.cachedCandidateTarget\s*=\s*projectile' -or
    $rkTrySeed -notmatch 'cycle\.cachedCandidateInterception\s*=\s*true') {
    throw 'Exact projectile results must seed only the matching bound primary or registered-secondary cycle.'
}

# Extract the actual production methods. The surrounding engine and search/job
# operations are small stubs; this checks gates and dispatch, not RimWorld combat.
$rkHarness = @"
using System;
using System.Collections.Generic;
namespace IdleProjectileWakeChecks {
    public sealed class RimKataSettings {
        public bool explosiveInterceptionEnabled = true;
    }
    public static class RimKataMod {
        public static RimKataSettings Settings = new RimKataSettings();
    }
    public struct IntVec3 {
        public int x, z;
        public IntVec3(int x, int z) { this.x = x; this.z = z; }
        public float DistanceToSquared(IntVec3 other) {
            int dx = x - other.x, dz = z - other.z;
            return dx * dx + dz * dz;
        }
    }
    public static class Mathf {
        public static float Max(float left, float right) { return left > right ? left : right; }
    }
    public sealed class Map {
        public MapPawns mapPawns = new MapPawns();
        public RimKataMapComponent component;
        public Map() { component = new RimKataMapComponent(this); }
        public T GetComponent<T>() where T : class { return component as T; }
    }
    public sealed class RimKataMapComponent {
        private readonly Map map;
        public readonly HashSet<Projectile> activeExplosiveProjectiles =
            new HashSet<Projectile>();
        public bool HasActiveExplosiveProjectiles {
            get { return activeExplosiveProjectiles.Count > 0; }
        }
        public RimKataMapComponent(Map map) { this.map = map; }
        $rkExactProjectileProbe
    }
    public sealed class MapPawns { public List<Pawn> AllPawnsSpawned = new List<Pawn>(); }
    public sealed class JobDef { public string defName; }
    public class Thing { public Map Map; public bool Spawned = true, Destroyed; public IntVec3 Position; }
    public sealed class Projectile : Thing {
        public bool potential = true, hostile = true, trajectory = true;
    }
    public sealed class ThingDef { public bool enabled = true; }
    public sealed class Job { public bool playerForced; public JobDef def; public ThinkNode jobGiver; }
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
    public class Verb {
        public bool Bursting, IsMeleeAttack, available = true, apparelBlocks;
        public float effectiveRange = 10f;
    }
    public sealed class Verb_LaunchProjectile : Verb { }
    public sealed class ThingWithComps { public Verb verb; public ThingDef def = new ThingDef(); }
    public sealed class Pawn {
        public Map Map = new Map();
        public IntVec3 Position;
        public bool Spawned = true, IsPlayerControlled = true, access = true, awake = true;
        public bool Dead, Downed, InMentalState, burning, Drafted, inactive;
        public bool secondaryAllowed = true, secondaryRegistered;
        public bool primaryCandidate = true, secondaryCandidate;
        public bool continuity, acceptFollowup = true;
        public int accessChecks, beginChecks, stateReads, stateCreates, normalizations;
        public int binds, refreshes, followups, secondaryRegistryReads, exactProbes;
        public Pather pather = new Pather();
        public Drafter drafter = new Drafter();
        public CarryTracker carryTracker = new CarryTracker();
        public MindState mindState = new MindState();
        public PawnStanceTracker stances = new PawnStanceTracker();
        public Lord lord;
        public Lord GetLord() { return lord; }
        public Job CurJob = new Job { def = JobDefOf.Wait };
        public JobDef CurJobDef { get { return CurJob == null ? null : CurJob.def; } }
        public ThingWithComps primary = new ThingWithComps { verb = new Verb_LaunchProjectile() };
        public ThingWithComps secondary;
        public ThingWithComps registeredSecondary;
        public RimKataPawnCombatState state;
        public bool Awake() { return awake; }
        public bool IsBurning() { return burning; }
    }
    public static class RimKataEligibility {
        public static bool HasRimKataAccess(Pawn pawn) { pawn.accessChecks++; return pawn.access; }
        public static bool CanBeginGunKataAttack(Pawn pawn) {
            pawn.beginChecks++;
            return pawn.access && !pawn.inactive;
        }
        public static bool CanUseProjectileInterception(Pawn pawn) { return CanBeginGunKataAttack(pawn); }
    }
    public static class RimKataWeaponSlotUtility {
        public static ThingWithComps PrimaryWeapon(Pawn pawn) { return pawn.primary; }
        public static bool CanUseSecondarySlot(Pawn pawn) { return pawn.secondaryAllowed; }
        public static bool CanUseSecondarySlot(Pawn pawn, ThingWithComps primary, bool accessVerified) {
            return pawn.secondaryAllowed && primary != null;
        }
        public static ThingWithComps SecondaryWeapon(Pawn pawn) { return pawn.secondary; }
        public static ThingWithComps SecondaryWeaponWithVerifiedAccess(Pawn pawn) {
            pawn.secondaryRegistryReads++;
            return pawn.secondaryRegistered ? pawn.registeredSecondary : null;
        }
        public static Verb CombatVerb(Pawn pawn, ThingWithComps weapon) { return weapon == null ? null : weapon.verb; }
    }
    public static class RimKataRangeUtility {
        public static float ResolveEffectiveRange(Pawn pawn, ThingWithComps weapon, Verb verb) {
            return verb == null ? 0f : verb.effectiveRange;
        }
    }
    public static class RimKataTargeting {
        public static bool IsPotentialExplosiveProjectile(Projectile projectile, Map map) {
            return projectile != null && projectile.potential && projectile.Spawned
                && !projectile.Destroyed && object.ReferenceEquals(projectile.Map, map);
        }
        public static bool IsEnemyProjectileLauncher(Pawn pawn, Projectile projectile) {
            return projectile != null && projectile.hostile;
        }
        public static bool IsValidExplosiveProjectileForVerb(
            Pawn pawn,
            Verb verb,
            Projectile projectile,
            float rangeSquared) {
            return IsPotentialExplosiveProjectile(projectile, pawn == null ? null : pawn.Map)
                && IsEnemyProjectileLauncher(pawn, projectile)
                && pawn.Position.DistanceToSquared(projectile.Position) <= rangeSquared;
        }
    }
    public static class RimKataInterceptionTrajectory {
        public static bool CanIntercept(
            Pawn pawn,
            Verb verb,
            Projectile projectile,
            int burstShotIndex,
            float rangeSquared) {
            pawn.exactProbes++;
            if (!projectile.trajectory) return false;
            if (object.ReferenceEquals(verb, pawn.primary == null ? null : pawn.primary.verb)) {
                return pawn.primaryCandidate;
            }
            if (object.ReferenceEquals(verb, pawn.registeredSecondary == null ? null : pawn.registeredSecondary.verb)) {
                return pawn.secondaryCandidate;
            }
            return false;
        }
    }
    public sealed class RimKataWeaponCycleState {
        public ThingWithComps weapon;
        public Thing cachedCandidateTarget;
        public bool cachedCandidateInterception;
        public bool HasPlan, openingWarmupPending;
        public int burstShotsRemaining;
    }
    public sealed class RimKataPawnCombatState {
        public RimKataWeaponCycleState primaryWeaponCycle =
            new RimKataWeaponCycleState();
        public RimKataWeaponCycleState secondaryWeaponCycle =
            new RimKataWeaponCycleState();
        public bool trigger, dedicatedFollowupJobPending;
        public int queued, consumed;
        public Job projectileWakeResumeJob;
        public void QueueIdleProjectileSearchTrigger() { queued++; trigger = true; }
        public void ConsumeIdleProjectileSearchTrigger() { consumed++; trigger = false; }
    }
    public sealed class Traversal {
        public Map map = new Map();
        public List<Pawn> projectileWakeTraversal = new List<Pawn>();
        public HashSet<Projectile> activeExplosiveProjectiles;
        public int projectileWakeTraversalIndex;
        public bool projectileWakeTraversalActive;
        public Traversal() {
            activeExplosiveProjectiles = map.component.activeExplosiveProjectiles;
        }
        public void Start() { StartProjectileWakeTraversal(); }
        $rkTraversal
        $rkPotentialWake
        $rkPotentialWakeRange
    }
    public static class Controller {
        public static bool CanStartWakeForCheck(Pawn pawn) { return CanStartQueuedProjectileWake(pawn); }
        private static RimKataPawnCombatState StateFor(Pawn pawn, bool create) {
            pawn.stateReads++;
            if (pawn.state == null && create) { pawn.stateCreates++; pawn.state = new RimKataPawnCombatState(); }
            return pawn.state;
        }
        private static void NormalizeInvalidInterceptionState(Pawn pawn, RimKataPawnCombatState state) { pawn.normalizations++; }
        private static bool HasCombatContinuity(Pawn pawn, RimKataPawnCombatState state) {
            if (!object.ReferenceEquals(pawn.state, state)) throw new Exception("Wrong continuity state");
            return pawn.continuity;
        }
        private static void BindCurrentWeapons(
            Pawn pawn,
            RimKataPawnCombatState state,
            bool accessVerified) {
            pawn.binds++;
            state.primaryWeaponCycle.weapon = pawn.primary;
            state.secondaryWeaponCycle.weapon =
                pawn.secondaryRegistered ? pawn.registeredSecondary : null;
        }
        private static void RefreshDualEngagementState(Pawn pawn, RimKataPawnCombatState state) { pawn.refreshes++; }
        private static void QueueDedicatedFollowupJob(Pawn pawn, object target) {
            pawn.followups++;
            pawn.state.dedicatedFollowupJobPending = pawn.acceptFollowup;
        }
        private static bool VerbUsable(
            Pawn pawn,
            Verb verb,
            bool closeCombatContext) {
            return verb != null && verb.available && !verb.apparelBlocks;
        }
        $rkQueue
        $rkCanWake
        $rkCanWakeNow
        $rkProjectileWakeRange
        $rkTrySeed
        $rkBusyAttack
        $rkCanStartWake
    }
    public static class RimKataDualWeaponController {
        public static bool CanReceiveProjectileWake(Pawn pawn) { return Controller.CanReceiveProjectileWake(pawn); }
        public static bool CanReceiveIdleProjectileWakeNow(Pawn pawn) {
            return Controller.CanReceiveIdleProjectileWakeNow(pawn);
        }
        public static float ProjectileWakeRange(Pawn pawn, ThingWithComps weapon, Verb verb) {
            return Controller.ProjectileWakeRange(pawn, weapon, verb);
        }
        public static bool VerbUsable(Pawn pawn, Verb verb, bool closeCombatContext) {
            return verb != null && verb.available && !verb.apparelBlocks;
        }
    }
    public static class Checks {
        private static int checks;
        private static void Check(bool condition, string name) {
            if (!condition) throw new Exception(name);
            checks++;
        }
        private static Projectile AddProjectileToMap(
            Map map,
            int x,
            bool hostile = true,
            bool trajectory = true) {
            var projectile = new Projectile {
                Map = map,
                Position = new IntVec3(x, 0),
                hostile = hostile,
                trajectory = trajectory
            };
            map.component.activeExplosiveProjectiles.Add(projectile);
            return projectile;
        }
        private static Pawn PawnWithState() {
            var pawn = new Pawn { state = new RimKataPawnCombatState() };
            AddProjectileToMap(pawn.Map, 5);
            return pawn;
        }
        private static Pawn PawnWithoutState() {
            var pawn = new Pawn();
            AddProjectileToMap(pawn.Map, 5);
            return pawn;
        }
        private static void AddPawn(Traversal traversal, Pawn pawn) {
            if (pawn != null) pawn.Map = traversal.map;
            traversal.map.mapPawns.AllPawnsSpawned.Add(pawn);
        }
        private static Projectile AddProjectile(Traversal traversal, int x, bool hostile = true) {
            return AddProjectileToMap(traversal.map, x, hostile);
        }
        private static Traversal TraversalFor(Pawn pawn, int projectileX = 5, bool hostile = true) {
            var traversal = new Traversal();
            AddPawn(traversal, pawn);
            AddProjectile(traversal, projectileX, hostile);
            return traversal;
        }
        private static void Assault(Pawn pawn) {
            pawn.IsPlayerControlled = false;
            pawn.mindState.duty = new PawnDuty { def = DutyDefOf.AssaultColony };
        }
        private static void AcceptAI(string name, Action<Pawn> setup) {
            var pawn = PawnWithState(); pawn.IsPlayerControlled = false;
            setup(pawn);
            Controller.QueueIdleProjectileSearch(pawn);
            Check(pawn.binds == 1
                && pawn.state.primaryWeaponCycle.cachedCandidateInterception
                && pawn.followups == 1, name);
        }
        private static void Reject(string name, Action<Pawn> setup) {
            var pawn = PawnWithState();
            setup(pawn);
            Controller.QueueIdleProjectileSearch(pawn);
            Check(pawn.binds == 0
                && pawn.state.primaryWeaponCycle.cachedCandidateTarget == null
                && pawn.state.secondaryWeaponCycle.cachedCandidateTarget == null
                && pawn.followups == 0, name);
        }
        public static int Run() {
            var traversal = new Traversal();
            var eligible = PawnWithState();
            var inactive = PawnWithState(); inactive.inactive = true;
            var ai = PawnWithState(); ai.IsPlayerControlled = false;
            var noneligible = PawnWithState(); noneligible.access = false;
            AddPawn(traversal, eligible);
            AddPawn(traversal, ai);
            AddPawn(traversal, noneligible);
            AddPawn(traversal, inactive);
            AddPawn(traversal, null);
            AddProjectile(traversal, 5);
            traversal.Start();
            Check(traversal.projectileWakeTraversal.Count == 1 && traversal.projectileWakeTraversal[0] == eligible,
                "Only currently eligible in-range projectile user enters traversal");
            Check(ai.beginChecks == 0 && eligible.beginChecks == 1 && noneligible.beginChecks == 1
                && inactive.beginChecks == 1,
                "Receiver gate short-circuits AI before one interception eligibility check per player pawn");
            Check(eligible.secondaryRegistryReads == 1
                && ai.secondaryRegistryReads == 0
                && noneligible.secondaryRegistryReads == 0
                && inactive.secondaryRegistryReads == 0,
                "Only a state-free accepted receiver reaches registered-secondary prefilter lookup");
            Check(traversal.projectileWakeTraversalIndex == 0 && traversal.projectileWakeTraversalActive, "Traversal starts normally");

            var noCombatState = new Pawn();
            var noStateTraversal = TraversalFor(noCombatState);
            noStateTraversal.Start();
            Check(noStateTraversal.projectileWakeTraversal.Count == 1
                && noCombatState.state == null && noCombatState.stateCreates == 0
                && noCombatState.exactProbes == 0,
                "Potential projectile wake admission creates no state and runs no exact trajectory probe");

            eligible.access = false;
            Controller.QueueIdleProjectileSearch(eligible);
            Check(eligible.binds == 0, "Access loss after list registration rejected at processing time");
            Controller.QueueIdleProjectileSearch(inactive);
            Check(inactive.binds == 0, "Temporary inactivity rejected at processing time");
            traversal.map.mapPawns.AllPawnsSpawned.Clear(); traversal.Start();
            Check(traversal.projectileWakeTraversal.Count == 0 && !traversal.projectileWakeTraversalActive, "Empty traversal stops");

            var friendlyProjectilePawn = PawnWithState();
            var friendlyTraversal = TraversalFor(friendlyProjectilePawn, 5, false);
            friendlyTraversal.Start();
            Check(!friendlyTraversal.projectileWakeTraversalActive,
                "Friendly explosive projectile does not create a wake traversal");

            var outOfRangePawn = PawnWithState();
            var outOfRangeTraversal = TraversalFor(outOfRangePawn, 11);
            outOfRangeTraversal.Start();
            Check(!outOfRangeTraversal.projectileWakeTraversalActive,
                "Projectile outside every usable weapon range does not create traversal");

            var unusablePrimary = PawnWithState();
            unusablePrimary.primary.verb.available = false;
            var unusableTraversal = TraversalFor(unusablePrimary);
            unusableTraversal.Start();
            Check(!unusableTraversal.projectileWakeTraversalActive,
                "Unavailable primary weapon does not create traversal");

            var secondaryOnly = PawnWithState();
            secondaryOnly.primary.verb.effectiveRange = 3f;
            secondaryOnly.registeredSecondary = new ThingWithComps {
                verb = new Verb_LaunchProjectile { effectiveRange = 12f }
            };
            secondaryOnly.secondaryRegistered = true;
            var secondaryTraversal = TraversalFor(secondaryOnly, 9);
            secondaryTraversal.Start();
            Check(secondaryTraversal.projectileWakeTraversal.Count == 1,
                "Registered usable secondary admits an in-range hostile projectile");

            var unregisteredSecondary = PawnWithState();
            unregisteredSecondary.primary.verb.effectiveRange = 3f;
            unregisteredSecondary.registeredSecondary = new ThingWithComps {
                verb = new Verb_LaunchProjectile { effectiveRange = 12f }
            };
            var unregisteredTraversal = TraversalFor(unregisteredSecondary, 9);
            unregisteredTraversal.Start();
            Check(!unregisteredTraversal.projectileWakeTraversalActive,
                "Unregistered secondary is not resolved during wake prefilter");

            var disabledPrimary = PawnWithState();
            disabledPrimary.primary.def.enabled = false;
            var disabledTraversal = TraversalFor(disabledPrimary);
            disabledTraversal.Start();
            Check(disabledTraversal.projectileWakeTraversalActive,
                "Allowed-equipment setting does not disable established projectile interception");

            var burstingSecondary = PawnWithState();
            burstingSecondary.registeredSecondary = new ThingWithComps {
                verb = new Verb_LaunchProjectile { Bursting = true, effectiveRange = 12f }
            };
            burstingSecondary.secondaryRegistered = true;
            var burstingTraversal = TraversalFor(burstingSecondary);
            burstingTraversal.Start();
            Check(!burstingTraversal.projectileWakeTraversalActive,
                "A currently bursting registered secondary blocks wake admission");

            Reject("AI cannot wake out of combat", p => p.IsPlayerControlled = false);
            Reject("Preparing raid wander does not wake", p => {
                p.IsPlayerControlled = false; p.pather.MovingNow = true;
                p.mindState.duty = new PawnDuty { def = new DutyDef() };
                p.CurJob.def = new JobDef { defName = "GotoWander" };
            });
            Reject("Stale enemy reference alone does not authorize AI wake", p => {
                p.IsPlayerControlled = false; p.mindState.enemyTarget = new Thing { Map = p.Map };
            });
            AcceptAI("Assault begins without a personal target", p => { Assault(p); p.CurJob.def = JobDefOf.Goto; });
            AcceptAI("Issued fight job authorizes stationary combat wait", p => {
                p.CurJob.jobGiver = new JobGiver_AIFightEnemy(); p.CurJob.def = JobDefOf.Wait_Combat;
            });
            AcceptAI("Issued pursuit job authorizes stationary Goto", p => {
                p.CurJob.jobGiver = new JobGiver_AIGotoTarget(); p.CurJob.def = JobDefOf.Goto;
            });
            AcceptAI("Issued nearest-hostile pursuit authorizes Goto", p => {
                p.CurJob.jobGiver = new JobGiver_AIGotoNearestHostile(); p.CurJob.def = JobDefOf.Goto;
            });
            AcceptAI("Sapper assault escort is included", p => {
                p.lord = new Lord { CurLordToil = new LordToil_AssaultColonySappers() }; p.CurJob.def = JobDefOf.Goto;
            });
            AcceptAI("Breacher assault escort is included", p => {
                p.lord = new Lord { CurLordToil = new LordToil_AssaultColonyBreaching() }; p.CurJob.def = JobDefOf.Goto;
            });
            Reject("AI existing aim is not interrupted", p => {
                Assault(p); p.stances.curStance = new Stance_Warmup { verb = p.primary.verb, ticksLeft = 20 };
            });
            Reject("AI existing attack cooldown is not interrupted", p => {
                Assault(p); p.stances.curStance = new Stance_Busy { verb = p.primary.verb, ticksLeft = 15 };
            });
            Reject("AI primary burst is not interrupted", p => { Assault(p); p.primary.verb.Bursting = true; });
            Reject("AI secondary burst is not interrupted", p => {
                Assault(p);
                p.registeredSecondary = new ThingWithComps {
                    verb = new Verb_LaunchProjectile { Bursting = true }
                };
                p.secondary = p.registeredSecondary;
                p.secondaryRegistered = true;
            });
            Reject("Nonqualified assault AI remains excluded", p => { Assault(p); p.access = false; });
            Reject("AI carried items retain existing block", p => { Assault(p); p.carryTracker.CarriedThing = new object(); });
            Reject("AI forced work retains existing block", p => { Assault(p); p.CurJob.playerForced = true; });
            AcceptAI("Expired AI aim no longer blocks wake", p => {
                Assault(p); p.stances.curStance = new Stance_Warmup { verb = p.primary.verb, ticksLeft = 0 };
            });
            var aiTraversal = new Traversal();
            var waitingAI = PawnWithState(); waitingAI.IsPlayerControlled = false;
            var assaultAI = PawnWithState(); Assault(assaultAI);
            var chasingAI = PawnWithState(); chasingAI.IsPlayerControlled = false;
            chasingAI.CurJob.jobGiver = new JobGiver_AIGotoTarget();
            var noAccessAI = PawnWithState(); Assault(noAccessAI); noAccessAI.access = false;
            AddPawn(aiTraversal, waitingAI);
            AddPawn(aiTraversal, assaultAI);
            AddPawn(aiTraversal, chasingAI);
            AddPawn(aiTraversal, noAccessAI);
            AddProjectile(aiTraversal, 5);
            aiTraversal.Start();
            Check(aiTraversal.projectileWakeTraversal.Count == 2
                && aiTraversal.projectileWakeTraversal.Contains(assaultAI)
                && aiTraversal.projectileWakeTraversal.Contains(chasingAI), "Traversal includes only qualified combat-ready AI");
            assaultAI.mindState.duty = null;
            Controller.QueueIdleProjectileSearch(assaultAI);
            Check(assaultAI.binds == 0, "AI combat readiness is rechecked after traversal registration");
            var delayedAI = PawnWithState(); Assault(delayedAI);
            Controller.QueueIdleProjectileSearch(delayedAI);
            Check(delayedAI.state.dedicatedFollowupJobPending && Controller.CanStartWakeForCheck(delayedAI),
                "Queued assault interception initially remains ready");
            delayedAI.stances.curStance = new Stance_Warmup { verb = delayedAI.primary.verb, ticksLeft = 12 };
            Check(!Controller.CanStartWakeForCheck(delayedAI), "Aim starting after queue blocks delayed interception");
            ((Stance_Busy)delayedAI.stances.curStance).ticksLeft = 0;
            Check(Controller.CanStartWakeForCheck(delayedAI), "Expired queued attack stance no longer blocks");
            delayedAI.primary.verb.Bursting = true;
            Check(!Controller.CanStartWakeForCheck(delayedAI), "Primary burst starting after queue blocks delayed interception");
            delayedAI.primary.verb.Bursting = false;
            delayedAI.secondary = new ThingWithComps {
                verb = new Verb_LaunchProjectile { Bursting = true }
            };
            Check(!Controller.CanStartWakeForCheck(delayedAI), "Secondary burst starting after queue blocks delayed interception");
            delayedAI.secondary = null; delayedAI.mindState.duty = null;
            Check(!Controller.CanStartWakeForCheck(delayedAI), "Leaving combat after queue cancels delayed AI interception");
            Reject("Forced undrafted job preserved", p => p.CurJob.playerForced = true);
            Reject("Carried item blocks idle interception", p => p.carryTracker.CarriedThing = new object());
            Reject("Drafted movement blocks wake", p => { p.Drafted = true; p.pather.MovingNow = true; });
            Reject("Drafted fire-at-will disabled", p => { p.Drafted = true; p.drafter.FireAtWill = false; });
            Reject("Existing combat continuity blocks idle wake", p => p.continuity = true);
            Reject("Stationary active work preserved", p => p.CurJob.def = new JobDef { defName = "DoWork" });
            Reject("Nonprojectile weapons cannot intercept", p => p.primary.verb = new Verb());
            Reject("Disallowed secondary slot cannot admit projectile", p => {
                p.primary.verb = new Verb();
                p.registeredSecondary = new ThingWithComps {
                    verb = new Verb_LaunchProjectile()
                };
                p.secondaryRegistered = true;
                p.secondaryAllowed = false;
            });
            Reject("Unspawned", p => p.Spawned = false);
            Reject("Downed", p => p.Downed = true);
            Reject("Asleep", p => p.awake = false);
            Reject("Mental state", p => p.InMentalState = true);
            Reject("Burning", p => p.burning = true);

            var primary = PawnWithState();
            Controller.QueueIdleProjectileSearch(primary);
            Check(primary.beginChecks == 1 && primary.stateReads == 1 && primary.normalizations == 1 && primary.stateCreates == 0,
                "Existing state and eligibility reused within one request");
            Check(primary.binds == 1
                && primary.exactProbes == 1
                && primary.state.primaryWeaponCycle.cachedCandidateTarget is Projectile
                && primary.state.primaryWeaponCycle.cachedCandidateInterception
                && primary.state.secondaryWeaponCycle.cachedCandidateTarget == null
                && primary.state.queued == 1,
                "Exact primary result seeds only its matching bound cycle");
            Check(primary.followups == 1 && primary.state.projectileWakeResumeJob == primary.CurJob && primary.state.dedicatedFollowupJobPending,
                "Undrafted successful candidate preserves resume job and queues followup");
            var secondary = PawnWithState();
            secondary.primary.verb = new Verb();
            secondary.primaryCandidate = false;
            secondary.registeredSecondary = new ThingWithComps {
                verb = new Verb_LaunchProjectile()
            };
            secondary.secondary = secondary.registeredSecondary;
            secondary.secondaryRegistered = true;
            secondary.secondaryCandidate = true;
            Controller.QueueIdleProjectileSearch(secondary);
            Check(secondary.binds == 1
                && secondary.state.primaryWeaponCycle.cachedCandidateTarget == null
                && secondary.state.secondaryWeaponCycle.cachedCandidateTarget is Projectile
                && secondary.state.secondaryWeaponCycle.cachedCandidateInterception
                && secondary.followups == 1,
                "Registered secondary exact result seeds only its matching cycle");
            var noState = PawnWithoutState(); Controller.QueueIdleProjectileSearch(noState);
            Check(noState.stateReads == 2 && noState.stateCreates == 1 && noState.normalizations == 0, "Missing state created only after acceptance");
            var failedNoState = PawnWithoutState();
            failedNoState.primaryCandidate = false;
            Controller.QueueIdleProjectileSearch(failedNoState);
            Check(failedNoState.exactProbes == 1
                && failedNoState.stateReads == 1
                && failedNoState.stateCreates == 0
                && failedNoState.state == null
                && failedNoState.binds == 0,
                "Failed exact trajectory probe cannot create combat state");
            var none = PawnWithState(); none.primaryCandidate = false;
            Controller.QueueIdleProjectileSearch(none);
            Check(none.state.queued == 0 && none.state.consumed == 0
                && none.binds == 0 && none.refreshes == 0 && none.followups == 0,
                "Failed exact trajectory probe leaves existing combat state untouched");
            var drafted = PawnWithState(); drafted.Drafted = true;
            Controller.QueueIdleProjectileSearch(drafted);
            Check(drafted.state.primaryWeaponCycle.cachedCandidateInterception
                && drafted.followups == 0 && drafted.state.projectileWakeResumeJob == null,
                "Drafted candidate does not replace job");
            var refused = PawnWithState(); refused.acceptFollowup = false;
            Controller.QueueIdleProjectileSearch(refused);
            Check(refused.followups == 1 && refused.state.projectileWakeResumeJob == null, "Refused followup clears resume reference");
            var movingWorker = PawnWithState(); movingWorker.pather.MovingNow = true;
            movingWorker.CurJob.def = new JobDef { defName = "DoWork" };
            Controller.QueueIdleProjectileSearch(movingWorker);
            Check(movingWorker.followups == 1, "Unforced empty-handed moving work remains permitted");
            return checks;
        }
    }
}
"@
Add-Type -TypeDefinition $rkHarness -Language CSharp
$rkPassed = [IdleProjectileWakeChecks.Checks]::Run()
"PASS: $rkPassed assertions; production traversal/queue methods with engine stubs, plus scheduler source checks. Not an in-game test."
