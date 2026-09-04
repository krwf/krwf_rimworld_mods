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
$rkBusyAttack = Get-CSharpBlock $rkController 'private static bool HasBusyAttackStance('
$rkCanStartWake = Get-CSharpBlock $rkController 'private static bool CanStartQueuedProjectileWake('
$rkTraversal = Get-CSharpBlock $rkCombat 'private void StartProjectileWakeTraversal()'
$rkScheduler = Get-CSharpBlock $rkCombat 'private void TickProjectileScheduler()'
if ($rkController.Contains('CanAcceptIdleProjectileSearch(') -or $rkCombat.Contains('CanAcceptIdleProjectileSearch(')) {
    throw 'The duplicate CanAcceptIdleProjectileSearch path remains.'
}
if (-not $rkScheduler.Contains('HasHostileExplosiveProjectileOnMapFor(pawn)') -or
    [regex]::Matches($rkScheduler, 'QueueIdleProjectileSearch\(\s*pawn\)').Count -ne 1) {
    throw 'Scheduler must retain its hostile-projectile gate and one queue call.'
}
if (-not $rkQueue.Contains('HasCombatContinuity(pawn, state)')) {
    throw 'Combat continuity must reuse the previously read state.'
}

# Extract the actual production methods. The surrounding engine and search/job
# operations are small stubs; this checks gates and dispatch, not RimWorld combat.
$rkHarness = @"
using System;
using System.Collections.Generic;
namespace IdleProjectileWakeChecks {
    public sealed class Map {
        public MapPawns mapPawns = new MapPawns();
        public RimKataMapComponent component = new RimKataMapComponent();
        public T GetComponent<T>() where T : class { return component as T; }
    }
    public sealed class RimKataMapComponent { public bool HasActiveExplosiveProjectiles = true; }
    public sealed class MapPawns { public List<Pawn> AllPawnsSpawned = new List<Pawn>(); }
    public sealed class JobDef { public string defName; }
    public sealed class Thing { public Map Map; public bool Spawned = true, Destroyed; }
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
    public class Verb { public bool Bursting; }
    public sealed class Verb_LaunchProjectile : Verb { }
    public sealed class ThingWithComps { public Verb verb; }
    public sealed class Pawn {
        public Map Map = new Map();
        public bool Spawned = true, IsPlayerControlled = true, access = true, awake = true;
        public bool Dead, Downed, InMentalState, burning, Drafted, inactive;
        public bool secondaryAllowed = true, primaryCandidate = true, secondaryCandidate;
        public bool continuity, acceptFollowup = true;
        public int accessChecks, beginChecks, stateReads, stateCreates, normalizations;
        public int binds, primaryCaches, secondaryCaches, refreshes, followups;
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
        public static ThingWithComps SecondaryWeapon(Pawn pawn) { return pawn.secondary; }
        public static Verb CombatVerb(Pawn pawn, ThingWithComps weapon) { return weapon == null ? null : weapon.verb; }
    }
    public sealed class Cycle { public bool cachedCandidateInterception; }
    public sealed class RimKataPawnCombatState {
        public Cycle primaryWeaponCycle = new Cycle(), secondaryWeaponCycle = new Cycle();
        public bool trigger, dedicatedFollowupJobPending;
        public int queued, consumed;
        public Job projectileWakeResumeJob;
        public void QueueIdleProjectileSearchTrigger() { queued++; trigger = true; }
        public void ConsumeIdleProjectileSearchTrigger() { consumed++; trigger = false; }
    }
    public sealed class Traversal {
        public Map map = new Map();
        public List<Pawn> projectileWakeTraversal = new List<Pawn>();
        public int projectileWakeTraversalIndex;
        public bool projectileWakeTraversalActive;
        public void Start() { StartProjectileWakeTraversal(); }
        $rkTraversal
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
        private static void BindCurrentWeapons(Pawn pawn, RimKataPawnCombatState state) { pawn.binds++; }
        private static void TryCacheSharedCandidate(Pawn pawn, RimKataPawnCombatState state, Cycle cycle, object target) {
            if (object.ReferenceEquals(cycle, state.primaryWeaponCycle)) {
                pawn.primaryCaches++;
                cycle.cachedCandidateInterception = pawn.primaryCandidate;
            } else {
                pawn.secondaryCaches++;
                cycle.cachedCandidateInterception = pawn.secondaryCandidate;
            }
        }
        private static void RefreshDualEngagementState(Pawn pawn, RimKataPawnCombatState state) { pawn.refreshes++; }
        private static void QueueDedicatedFollowupJob(Pawn pawn, object target) {
            pawn.followups++;
            pawn.state.dedicatedFollowupJobPending = pawn.acceptFollowup;
        }
        $rkQueue
        $rkCanWake
        $rkBusyAttack
        $rkCanStartWake
    }
    public static class RimKataDualWeaponController {
        public static bool CanReceiveProjectileWake(Pawn pawn) { return Controller.CanReceiveProjectileWake(pawn); }
    }
    public static class Checks {
        private static int checks;
        private static void Check(bool condition, string name) {
            if (!condition) throw new Exception(name);
            checks++;
        }
        private static Pawn PawnWithState() { return new Pawn { state = new RimKataPawnCombatState() }; }
        private static void Assault(Pawn pawn) {
            pawn.IsPlayerControlled = false;
            pawn.mindState.duty = new PawnDuty { def = DutyDefOf.AssaultColony };
        }
        private static void AcceptAI(string name, Action<Pawn> setup) {
            var pawn = PawnWithState(); pawn.IsPlayerControlled = false;
            setup(pawn);
            Controller.QueueIdleProjectileSearch(pawn);
            Check(pawn.binds == 1 && pawn.primaryCaches == 1 && pawn.followups == 1, name);
        }
        private static void Reject(string name, Action<Pawn> setup) {
            var pawn = PawnWithState();
            setup(pawn);
            Controller.QueueIdleProjectileSearch(pawn);
            Check(pawn.binds == 0 && pawn.primaryCaches == 0 && pawn.secondaryCaches == 0 && pawn.followups == 0, name);
        }
        public static int Run() {
            var traversal = new Traversal();
            var eligible = PawnWithState();
            var inactive = PawnWithState(); inactive.inactive = true;
            var ai = PawnWithState(); ai.IsPlayerControlled = false;
            var noneligible = PawnWithState(); noneligible.access = false;
            traversal.map.mapPawns.AllPawnsSpawned.AddRange(new Pawn[] { eligible, ai, noneligible, inactive, null });
            traversal.Start();
            Check(traversal.projectileWakeTraversal.Count == 2 && traversal.projectileWakeTraversal[0] == eligible && traversal.projectileWakeTraversal[1] == inactive,
                "Player holders registered; out-of-combat AI excluded and inactive holder retained for recovery");
            Check(ai.accessChecks == 0 && eligible.accessChecks == 1 && noneligible.accessChecks == 1,
                "Player-control short circuit and one access check per eligible surface pawn");
            Check(traversal.projectileWakeTraversalIndex == 0 && traversal.projectileWakeTraversalActive, "Traversal starts normally");
            eligible.access = false;
            Controller.QueueIdleProjectileSearch(eligible);
            Check(eligible.binds == 0, "Access loss after list registration rejected at processing time");
            Controller.QueueIdleProjectileSearch(inactive);
            Check(inactive.binds == 0, "Temporary inactivity rejected at processing time");
            traversal.map.mapPawns.AllPawnsSpawned.Clear(); traversal.Start();
            Check(traversal.projectileWakeTraversal.Count == 0 && !traversal.projectileWakeTraversalActive, "Empty traversal stops");

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
                Assault(p); p.secondary = new ThingWithComps { verb = new Verb_LaunchProjectile { Bursting = true } };
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
            aiTraversal.map.mapPawns.AllPawnsSpawned.AddRange(new[] { waitingAI, assaultAI, chasingAI, noAccessAI });
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
            delayedAI.secondary = new ThingWithComps { verb = new Verb_LaunchProjectile { Bursting = true } };
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
                p.primary.verb = new Verb(); p.secondary = new ThingWithComps { verb = new Verb_LaunchProjectile() }; p.secondaryAllowed = false;
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
            Check(primary.binds == 1 && primary.primaryCaches == 1 && primary.secondaryCaches == 1 && primary.state.queued == 1,
                "Normal request searches each weapon slot exactly once");
            Check(primary.followups == 1 && primary.state.projectileWakeResumeJob == primary.CurJob && primary.state.dedicatedFollowupJobPending,
                "Undrafted successful candidate preserves resume job and queues followup");
            var secondary = PawnWithState();
            secondary.primary.verb = new Verb(); secondary.primaryCandidate = false;
            secondary.secondary = new ThingWithComps { verb = new Verb_LaunchProjectile() }; secondary.secondaryCandidate = true;
            Controller.QueueIdleProjectileSearch(secondary);
            Check(secondary.binds == 1 && secondary.followups == 1, "Secondary projectile weapon alone admits interception");
            var noState = new Pawn(); Controller.QueueIdleProjectileSearch(noState);
            Check(noState.stateReads == 2 && noState.stateCreates == 1 && noState.normalizations == 0, "Missing state created only after acceptance");
            var none = PawnWithState(); none.primaryCandidate = false;
            Controller.QueueIdleProjectileSearch(none);
            Check(none.state.consumed == 1 && !none.state.trigger && none.refreshes == 1 && none.followups == 0,
                "No projectile candidate consumes trigger and does not queue job");
            var drafted = PawnWithState(); drafted.Drafted = true;
            Controller.QueueIdleProjectileSearch(drafted);
            Check(drafted.primaryCaches == 1 && drafted.followups == 0 && drafted.state.projectileWakeResumeJob == null,
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
