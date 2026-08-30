using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace KRWF.RimKata
{
    public static class RimKataAttackGizmoTargetContext
    {
        [ThreadStatic] private static int depth;

        public static bool Active => depth > 0;

        public static void Invoke(
            Action<LocalTargetInfo> action,
            LocalTargetInfo target)
        {
            if (action == null)
            {
                return;
            }

            depth++;
            try
            {
                action(target);
            }
            finally
            {
                depth--;
            }
        }
    }

    public sealed class Stance_RimKataAim : Stance_RimKataLeaningAim
    {
        public Stance_RimKataAim()
        {
        }

        public Stance_RimKataAim(int ticks, LocalTargetInfo focusTarget, Verb verb)
            : base(ticks, focusTarget, verb)
        {
        }

    }

    public sealed class JobDriver_RimKataAttack : JobDriver, IRimKataResponseCooldown
    {
        private int warmupTicksRemaining = -1;
        private int cooldownTicksRemaining;
        private Thing plannedTarget;
        private bool plannedInterception;
        private bool plannedCloseAttack;
        private bool plannedCloseContext;
        private bool dualCycleStateImported;
        private bool endingJob;

        private Thing AssignedTarget => TargetThingA;
        private bool IsPlayerForced => job?.playerForced == true;
        internal bool CanAbsorbAutomaticAttackJob => !endingJob;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref warmupTicksRemaining, "rimKataWarmupTicksRemaining", -1);
            Scribe_Values.Look(ref cooldownTicksRemaining, "rimKataCooldownTicksRemaining");
            Scribe_References.Look(ref plannedTarget, "rimKataPlannedTarget");
            Scribe_Values.Look(ref plannedInterception, "rimKataPlannedInterception");
            Scribe_Values.Look(ref plannedCloseAttack, "rimKataPlannedCloseAttack");
            Scribe_Values.Look(ref plannedCloseContext, "rimKataPlannedCloseContext");
            Scribe_Values.Look(ref dualCycleStateImported, "rimKataDualCycleStateImported");
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            AddFinishAction(delegate
            {
                endingJob = true;
                ClearPlannedAttack();
                ClearAimStance();
                RimKataDualWeaponController
                    .NotifyDedicatedCombatJobFinished(pawn);
            });

            Toil initialization = ToilMaker.MakeToil(
                "RimKataCombatInitialization");
            initialization.initAction = delegate
            {
                if (!dualCycleStateImported)
                {
                    dualCycleStateImported = true;
                    RimKataDualWeaponController.ImportLegacyPrimaryState(
                        pawn,
                        cooldownTicksRemaining,
                        warmupTicksRemaining,
                        plannedTarget,
                        plannedInterception,
                        plannedCloseAttack,
                        plannedCloseContext);
                    cooldownTicksRemaining = 0;
                    warmupTicksRemaining = -1;
                    plannedTarget = null;
                    plannedInterception = false;
                    plannedCloseAttack = false;
                    plannedCloseContext = false;
                }

                EnsurePathToAssignedTarget();
            };
            initialization.defaultCompleteMode =
                ToilCompleteMode.Instant;
            yield return initialization;

            Toil combat = ToilMaker.MakeToil("RimKataCombatLoop");
            combat.tickAction = CombatTick;
            combat.defaultCompleteMode = ToilCompleteMode.Never;
            yield return combat;
        }

        public bool TryApplyResponseCooldown(
            ThingWithComps weapon,
            Verb verb,
            LocalTargetInfo focus)
        {
            if (pawn != null && pawn.IsBurning())
            {
                CancelForFire();
                return true;
            }

            return RimKataDualWeaponController.TryApplyResponseCooldown(pawn, weapon, verb, focus);
        }

        private void CombatTick()
        {
            if (pawn?.InMentalState == true)
            {
                RimKataDualWeaponController.CancelOffenseForMentalState(pawn);
                EndRimKataJobWith(JobCondition.InterruptForced);
                return;
            }

            if (RimKataDualWeaponController
                .ConsumeLoadoutInvalidatedCombatJob(pawn, job))
            {
                EndRimKataJobWith(JobCondition.InterruptForced);
                return;
            }

            if (pawn != null && pawn.IsBurning())
            {
                CancelForFire();
                EndRimKataJobWith(JobCondition.InterruptForced);
                return;
            }

            Thing assignedTarget = AssignedTarget;
            bool assignedTargetValid = IsValidAssignedTarget(assignedTarget);
            if (assignedTargetValid)
            {
                RimKataDualWeaponController.RefreshDedicatedTargetContinuity(
                    pawn,
                    assignedTarget);
            }
            RimKataDualWeaponController.NotifyDraftedMovementCell(pawn);
            if (!RimKataEligibility.CanBeginGunKataAttack(pawn))
            {
                EndRimKataJobWith(JobCondition.Succeeded);
                return;
            }

            if (!assignedTargetValid)
            {
                RimKataDualWeaponController.EnsureContinuationSearchBeforeExit(
                    pawn);
                if (TryAdoptContinuationTarget(out assignedTarget))
                {
                    assignedTargetValid = true;
                }
                else
                {
                    RimKataDualWeaponController.Tick(
                        pawn,
                        null,
                        IsPlayerForced,
                        job.killIncappedTarget,
                        false);

                    if (TryAdoptContinuationTarget(out assignedTarget))
                    {
                        assignedTargetValid = true;
                    }
                    else if (RimKataDualWeaponController.HasContinuationSearchWork(pawn))
                    {
                        pawn.pather?.StopDead();
                        return;
                    }
                }

                if (!assignedTargetValid)
                {
                    EndRimKataJobWith(JobCondition.Succeeded);
                    return;
                }
            }

            RimKataDualWeaponController
                .ReconcileCloseCombatBeforeContinuityCheck(
                    pawn,
                    assignedTarget,
                    IsPlayerForced,
                    job.killIncappedTarget);
            if (!RimKataDualWeaponController.IsDedicatedFollowupActive(pawn))
            {
                EndRimKataJobWith(JobCondition.Succeeded);
                return;
            }

            RimKataMapComponent component = pawn.Map.GetComponent<RimKataMapComponent>();

            bool assignedTargetInTouchRange = assignedTargetValid && pawn.CanReachImmediate(assignedTarget, PathEndMode.Touch);

            Thing immediateCloseTarget =
                RimKataDualWeaponController.ResolveImmediateCloseTarget(
                    pawn,
                    assignedTarget,
                    IsPlayerForced,
                    job.killIncappedTarget);

            if (RimKataDodgeMovementUtility.IsActive(pawn))
            {
                bool dodgeCloseCombat = assignedTargetInTouchRange
                    || component?.IsCloseCombatActive(pawn) == true;

                TickCombatFire(
                    assignedTarget,
                    dodgeCloseCombat,
                    assignedTargetInTouchRange || immediateCloseTarget != null);
                return;
            }

            bool closeCombatActive = assignedTargetInTouchRange || immediateCloseTarget != null;
            if (!closeCombatActive)
            {
                bool canRush = RimKataDualWeaponController.CanRushTarget(
                    pawn,
                    assignedTarget);
                if (!canRush && !CanAttackWithoutRushing(assignedTarget))
                {
                    pawn.pather?.StopDead();
                    TickAdvancingFire(assignedTarget);
                    return;
                }

                if (canRush
                    && (pawn.pather?.Moving != true
                        || pawn.pather.Destination.Thing != assignedTarget))
                {
                    EnsurePathToAssignedTarget();
                }

                TickAdvancingFire(assignedTarget);
                return;
            }

            if (assignedTargetInTouchRange)
            {
                component?.EnterCloseCombat(pawn, assignedTarget);
            }

            pawn.pather.StopDead();
            TickCloseCombat(assignedTarget);
        }

        private void TickAdvancingFire(Thing assignedTarget)
        {
            TickCombatFire(assignedTarget, false, true);
        }

        private void TickCloseCombat(Thing assignedTarget)
        {
            TickCombatFire(assignedTarget, true, true);
        }

        private void TickCombatFire(
            Thing assignedTarget,
            bool closeCombatContext,
            bool closeTargetResolved)
        {
            RimKataDualWeaponController.Tick(
                pawn,
                assignedTarget,
                IsPlayerForced,
                job.killIncappedTarget,
                closeCombatContext,
                closeTargetResolved);
        }

        private void ClearAimStance()
        {
            if (pawn?.stances?.curStance is Stance_RimKataAim)
            {
                pawn.stances.SetStance(new Stance_Mobile());
            }
        }

        private void EndRimKataJobWith(JobCondition condition)
        {
            endingJob = true;
            EndJobWith(condition);
        }

        private void ClearPlannedAttack(bool resetWarmup = true)
        {
            plannedTarget = null;
            plannedInterception = false;
            plannedCloseAttack = false;
            plannedCloseContext = false;
            if (resetWarmup)
            {
                warmupTicksRemaining = -1;
            }
        }

        private void CancelForFire()
        {
            warmupTicksRemaining = -1;
            cooldownTicksRemaining = 0;
            ClearPlannedAttack();
            ClearAimStance();
            RimKataDraftedFireController.CancelForFire(pawn);
        }

        private void EnsurePathToAssignedTarget(
            bool targetReachabilityConfirmed = false)
        {
            if (RimKataDodgeMovementUtility.IsActive(pawn))
            {
                return;
            }

            Thing target = AssignedTarget;
            if (!RimKataDualWeaponController.CanRushTarget(pawn, target))
            {
                pawn.pather?.StopDead();
                return;
            }

            if (target == null || !target.Spawned || pawn.CanReachImmediate(target, PathEndMode.Touch))
            {
                return;
            }

            if (pawn.pather.Moving && pawn.pather.Destination.Thing == target)
            {
                return;
            }

            if (!targetReachabilityConfirmed
                && !pawn.CanReach(target, PathEndMode.Touch, Danger.Deadly))
            {
                pawn.pather?.StopDead();
                return;
            }

            pawn.pather.StartPath(target, PathEndMode.Touch);
        }

        private bool CanAttackWithoutRushing(Thing target)
        {
            return RimKataWeaponSlotUtility.CanAttackTargetWithoutRushing(pawn, target);
        }

        private bool TryAdoptContinuationTarget(out Thing target)
        {
            if (!RimKataDualWeaponController.TryGetContinuationTarget(pawn, IsPlayerForced, job.killIncappedTarget, out target))
            {
                return false;
            }

            SetAssignedTarget(target);
            return true;
        }

        private void SetAssignedTarget(
            Thing target,
            bool targetReachabilityConfirmed = false)
        {
            Thing previousTarget = AssignedTarget;
            if (target == null || previousTarget == target)
            {
                return;
            }

            job.targetA = target;
            EnsurePathToAssignedTarget(targetReachabilityConfirmed);
            if (!IsPlayerForced
                && pawn?.Drafted != true
                && (job?.jobGiver is JobGiver_ConfigurableHostilityResponse
                    || job?.jobGiver is JobGiver_ReactToCloseMeleeThreat)
                && RimKataDualWeaponController
                    .CounterattackControlEnabled(pawn))
            {
                MoteMaker.MakeColonistActionOverlay(
                    pawn,
                    ThingDefOf.Mote_ColonistAttacking);
            }
        }

        internal bool TryPromoteAutomaticJobTarget(Thing target)
        {
            Thing currentTarget = AssignedTarget;
            if (endingJob
                || pawn?.Map == null
                || pawn.InMentalState
                || IsPlayerForced
                || job?.def != RimKataDefOf.RimKata_Attack
                || !RimKataEligibility.RandomAttackEnabledForPawn(pawn)
                || target == null
                || target is Projectile
                || target == currentTarget
                || !IsValidAssignedTarget(target)
                || !pawn.CanReach(
                    target,
                    PathEndMode.Touch,
                    Danger.Deadly))
            {
                return false;
            }

            SetAssignedTarget(target, true);
            return true;
        }

        private bool IsValidAssignedTarget(Thing target)
        {
            if (target == null || target.Destroyed || !target.Spawned || target.Map != pawn.Map)
            {
                return false;
            }

            if (target is Pawn targetPawn
                && (targetPawn.Dead
                    || targetPawn.IsPsychologicallyInvisible()
                    || (!IsPlayerForced && targetPawn.Crawling)
                    || (!(IsPlayerForced && job.killIncappedTarget)
                        && targetPawn.Downed)))
            {
                return false;
            }

            return IsPlayerForced
                || RimKataTargeting.IsValidAutomaticAttackTarget(
                    pawn,
                    target);
        }
    }

    [HarmonyPatch(typeof(Verb), nameof(Verb.OrderForceTarget))]
    public static class Patch_Verb_OrderForceTarget_RimKata
    {
        public static bool Prefix(Verb __instance, LocalTargetInfo target)
        {
            Pawn pawn = __instance?.CasterPawn;
            if (__instance?.IsMeleeAttack == true)
            {
                bool closeTargetAccepted = target.HasThing
                    && RimKataDualWeaponController
                        .TryNotifyPlayerMeleeCloseTarget(
                            pawn,
                            target.Thing,
                            true);
                return !closeTargetAccepted
                    || pawn?.CurJobDef != RimKataDefOf.RimKata_Attack;
            }

            if (RimKataAttackGizmoTargetContext.Active)
            {
                return true;
            }

            bool forcedIncappedTarget = target.Pawn?.Downed == true
                && RimKataDualWeaponController.CanUsePlayerWeaponCommand(
                    pawn,
                    __instance);
            bool fixedWeaponTarget = false;
            if (!forcedIncappedTarget
                && __instance?.IsMeleeAttack == false
                && target.HasThing)
            {
                fixedWeaponTarget = RimKataDualWeaponController.NotifyPlayerWeaponTarget(
                    pawn,
                    __instance,
                    target.Thing,
                    true);
            }

            if (fixedWeaponTarget
                && (pawn?.Drafted == true
                    || pawn?.CurJobDef == RimKataDefOf.RimKata_Attack))
            {
                return false;
            }

            bool playerCloseFire = target.HasThing
                && RimKataDualWeaponController.BeginPlayerRangedCloseAttack(
                    pawn,
                    __instance,
                    target.Thing);

            if (forcedIncappedTarget)
            {
                return true;
            }

            if (!playerCloseFire
                && !RimKataDualWeaponController.IsDedicatedFollowupActive(pawn))
            {
                return true;
            }

            if (pawn?.IsBurning() == true
                || !target.HasThing
                || !RimKataWeaponSlotUtility.CanAttackTargetWithoutRushing(
                    pawn,
                    target.Thing))
            {
                return true;
            }

            Verb selectedVerb = __instance;
            if (selectedVerb == null)
            {
                return true;
            }

            Job job = JobMaker.MakeJob(RimKataDefOf.RimKata_Attack, target);
            job.playerForced = true;
            job.verbToUse = selectedVerb;
            if (target.Pawn != null)
            {
                job.killIncappedTarget = target.Pawn.Downed;
            }

            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            return false;
        }
    }

    [HarmonyPatch(
        typeof(Verb),
        nameof(Verb.CanHitTarget),
        new[] { typeof(LocalTargetInfo) })]
    public static class Patch_Verb_CanHitTarget_RimKataCloseOrder
    {
        public static void Postfix(
            Verb __instance,
            LocalTargetInfo targ,
            ref bool __result)
        {
            if (__result
                || __instance?.IsMeleeAttack != false
                || !targ.HasThing
                || Find.Targeter?.targetingSource?.GetVerb != __instance)
            {
                return;
            }

            if (!RimKataDualWeaponController.CanOrderRangedCloseAttack(
                __instance.CasterPawn,
                __instance,
                targ.Thing))
            {
                return;
            }

            __result = true;
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.TryTakeOrderedJob))]
    public static class Patch_PawnJobTracker_TryTakeOrderedJob_RimKata
    {
        public static void Prefix(Pawn ___pawn, ref Job job)
        {
            if (RimKataAttackGizmoTargetContext.Active
                && RimKataDualWeaponController.TryConvertPlayerRushOrder(
                    ___pawn,
                    job))
            {
                return;
            }

            bool orderedAttack = job != null
                && job.def == JobDefOf.AttackStatic
                && job.targetA.HasThing;
            bool playerSquadRangedOrder =
                RimKataAttackGizmoTargetContext.Active
                && orderedAttack;
            Verb orderedVerb = job?.verbToUse;
            bool playerRangedCloseOrder = orderedAttack
                && job.playerForced
                && RimKataDualWeaponController.CanOrderRangedCloseAttack(
                    ___pawn,
                    orderedVerb,
                    job.targetA.Thing);
            if (!playerSquadRangedOrder
                && !RimKataDualWeaponController.IsDedicatedFollowupActive(___pawn)
                && !playerRangedCloseOrder)
            {
                return;
            }

            if (___pawn?.Drafted != true
                || job == null
                || !orderedAttack
                || ___pawn.IsBurning()
                || !RimKataWeaponSlotUtility.CanAttackTargetWithoutRushing(
                    ___pawn,
                    job.targetA.Thing))
            {
                return;
            }

            Verb verb = playerRangedCloseOrder
                ? orderedVerb
                : RimKataWeaponSlotUtility.BestRangedCombatVerb(
                    ___pawn,
                    job.targetA.Thing);
            if (verb == null)
            {
                return;
            }

            job.def = RimKataDefOf.RimKata_Attack;
            job.verbToUse = verb;
            job.killIncappedTarget = job.killIncappedTarget || job.targetA.Pawn?.Downed == true;
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    public static class Patch_PawnJobTracker_StartJob_EnemyRimKata
    {
        public static bool Prefix(
            Pawn ___pawn,
            ref Job newJob,
            ThinkNode jobGiver,
            bool fromQueue)
        {
            if (___pawn?.InMentalState == true)
            {
                RimKataDualWeaponController
                    .CancelOffenseForMentalState(___pawn);
                if (newJob?.def != RimKataDefOf.RimKata_Attack)
                {
                    return true;
                }

                JobMaker.ReturnToPool(newJob);
                newJob = null;
                return false;
            }

            Job counterattackOpeningJob = newJob;
            RimKataCounterattackOpeningResult counterattackOpeningResult =
                RimKataDualWeaponController.HandleCounterattackOpening(
                    ___pawn,
                    newJob,
                    jobGiver,
                    out Job convertedCounterattackJob);
            if (counterattackOpeningResult
                == RimKataCounterattackOpeningResult.Absorbed)
            {
                if (counterattackOpeningJob != null)
                {
                    JobMaker.ReturnToPool(counterattackOpeningJob);
                    newJob = null;
                }

                return false;
            }

            if (counterattackOpeningResult
                == RimKataCounterattackOpeningResult.Converted)
            {
                if (counterattackOpeningJob != null
                    && counterattackOpeningJob != convertedCounterattackJob)
                {
                    JobMaker.ReturnToPool(counterattackOpeningJob);
                }
                newJob = convertedCounterattackJob;
            }

            bool preserveVanillaCounterattack =
                (jobGiver is JobGiver_ConfigurableHostilityResponse
                    || jobGiver is JobGiver_ReactToCloseMeleeThreat)
                && !RimKataDualWeaponController
                    .CounterattackControlEnabled(___pawn);
            if (!preserveVanillaCounterattack)
            {
                NormalizeMeleeVerb(___pawn, newJob);
                PreferPairRangedVerb(___pawn, newJob);
            }
            NotifyMeleeTarget(___pawn, newJob);

            if (preserveVanillaCounterattack)
            {
                return true;
            }

            if (!RimKataDualWeaponController.IsDedicatedFollowupActive(___pawn))
            {
                return true;
            }

            if (NormalizeQueuedPlayerRimKataAttack(___pawn, newJob, fromQueue))
            {
                return true;
            }

            if (ShouldConvertQueuedPlayerAttack(___pawn, newJob, fromQueue, out Verb queuedVerb))
            {
                newJob.def = RimKataDefOf.RimKata_Attack;
                newJob.verbToUse = queuedVerb;
                newJob.killIncappedTarget = newJob.killIncappedTarget || newJob.targetA.Pawn?.Downed == true;
                return true;
            }

            if (!ShouldConvertEnemyAttack(___pawn, newJob, out Verb verb))
            {
                return true;
            }

            newJob.def = RimKataDefOf.RimKata_Attack;
            newJob.verbToUse = verb;
            return true;
        }

        public static void Postfix(
            Pawn ___pawn,
            Job newJob,
            ThinkNode jobGiver)
        {
            ThinkNode effectiveJobGiver = jobGiver ?? newJob?.jobGiver;
            if (___pawn?.CurJob == newJob
                && newJob?.def == RimKataDefOf.RimKata_Attack
                && !newJob.playerForced
                && ___pawn.Drafted != true
                && ___pawn.InMentalState != true
                && (effectiveJobGiver is JobGiver_ConfigurableHostilityResponse
                    || effectiveJobGiver is JobGiver_ReactToCloseMeleeThreat)
                && newJob.targetA.HasThing
                && RimKataDualWeaponController
                    .CounterattackControlEnabled(___pawn))
            {
                MoteMaker.MakeColonistActionOverlay(
                    ___pawn,
                    ThingDefOf.Mote_ColonistAttacking);
            }

            if ((newJob?.def == JobDefOf.Goto
                    || newJob?.def == JobDefOf.AttackMelee)
                && newJob.playerForced
                && ___pawn?.CurJob == newJob)
            {
                RimKataDualWeaponController.QueuePlayerMovementSearch(___pawn);
            }
        }

        private static void NormalizeMeleeVerb(Pawn pawn, Job job)
        {
            if (job?.def != JobDefOf.AttackMelee
                || pawn == null
                || (job.verbToUse != null
                    && job.verbToUse.IsMeleeAttack))
            {
                return;
            }

            job.verbToUse = null;
        }

        private static void PreferPairRangedVerb(Pawn pawn, Job job)
        {
            if (job?.def != JobDefOf.AttackStatic
                || !job.targetA.HasThing
                || pawn?.Map == null
                || !RimKataEligibility.CanBeginGunKataAttack(pawn))
            {
                return;
            }

            Verb requestedVerb = job.verbToUse;
            ThingWithComps requestedWeapon =
                requestedVerb?.EquipmentSource as ThingWithComps;
            ThingWithComps primary = RimKataWeaponSlotUtility.PrimaryWeapon(pawn);
            ThingWithComps secondary = RimKataWeaponSlotUtility.CanUseSecondarySlot(pawn)
                ? RimKataWeaponSlotUtility.SecondaryWeapon(pawn)
                : null;
            if (requestedVerb != null
                && !requestedVerb.IsMeleeAttack
                && (requestedWeapon == primary || requestedWeapon == secondary))
            {
                return;
            }

            Verb verb = RimKataWeaponSlotUtility.BestRangedCombatVerb(
                pawn,
                job.targetA.Thing);
            if (verb != null)
            {
                job.verbToUse = verb;
            }
        }

        private static void NotifyMeleeTarget(Pawn attacker, Job job)
        {
            if (job?.def == JobDefOf.AttackMelee && job.targetA.Pawn is Pawn target)
            {
                RimKataDraftedFireController.NotifyTargetedByHostile(target, attacker);
            }
        }

        private static bool NormalizeQueuedPlayerRimKataAttack(
            Pawn pawn,
            Job job,
            bool fromQueue)
        {
            if (!fromQueue
                || job?.def != RimKataDefOf.RimKata_Attack
                || job.playerForced != true
                || pawn?.Drafted != true
                || pawn.InMentalState
                || pawn.IsBurning())
            {
                return false;
            }

            Verb enabledVerb = RimKataWeaponSlotUtility.BestRangedCombatVerb(
                pawn,
                job.targetA.Thing)
                ?? RimKataWeaponSlotUtility.CombatVerb(
                    pawn,
                    RimKataWeaponSlotUtility.PrimaryWeapon(pawn));
            if (enabledVerb != null)
            {
                if (!RimKataDualWeaponController.CanRushTarget(
                        pawn,
                        job.targetA.Thing)
                    && !RimKataWeaponSlotUtility.CanAttackTargetWithoutRushing(
                        pawn,
                        job.targetA.Thing))
                {
                    job.def = enabledVerb.IsMeleeAttack
                        ? JobDefOf.AttackMelee
                        : JobDefOf.AttackStatic;
                    job.verbToUse = enabledVerb.IsMeleeAttack ? null : enabledVerb;
                    return true;
                }

                job.verbToUse = enabledVerb;
                return true;
            }

            Verb currentVerb = pawn.equipment?.Primary?.TryGetComp<CompEquippable>()?.PrimaryVerb;
            bool hasRangedVerb = currentVerb != null && !currentVerb.IsMeleeAttack;
            job.def = hasRangedVerb ? JobDefOf.AttackStatic : JobDefOf.AttackMelee;
            job.verbToUse = hasRangedVerb ? currentVerb : null;
            return true;
        }

        private static bool ShouldConvertQueuedPlayerAttack(
            Pawn pawn,
            Job job,
            bool fromQueue,
            out Verb verb)
        {
            verb = null;
            bool vanillaCombatJob = job?.def == JobDefOf.AttackStatic;
            if (!fromQueue
                || !vanillaCombatJob
                || job.playerForced != true
                || pawn?.Drafted != true
                || pawn.InMentalState
                || pawn.IsBurning()
                || !job.targetA.HasThing)
            {
                return false;
            }

            Thing target = job.targetA.Thing;
            return target != null
                && target != pawn
                && target.Spawned
                && target.Map == pawn.Map
                && (!(target is Pawn targetPawn) || !targetPawn.IsPsychologicallyInvisible())
                && (verb = RimKataWeaponSlotUtility.BestRangedCombatVerb(
                    pawn,
                    target)) != null
                && RimKataWeaponSlotUtility.CanAttackTargetWithoutRushing(
                    pawn,
                    target);
        }

        private static bool ShouldConvertEnemyAttack(Pawn pawn, Job job, out Verb verb)
        {
            verb = null;
            bool vanillaCombatJob = job?.def == JobDefOf.AttackStatic || job?.def == JobDefOf.AttackMelee;
            if (!vanillaCombatJob
                || job.playerForced
                || !IsEligibleHostileRimKataPawn(pawn)
                || !job.targetA.HasThing)
            {
                return false;
            }

            Thing target = job.targetA.Thing;
            if (!IsValidEnemyTarget(pawn, target)
                || (!RimKataDualWeaponController.CanRushTarget(pawn, target)
                    && !RimKataWeaponSlotUtility.CanAttackTargetWithoutRushing(
                        pawn,
                        target)))
            {
                return false;
            }

            if (job.def == JobDefOf.AttackMelee
                && !RimKataDualWeaponController.HasUsableWeapon(pawn, true))
            {
                return false;
            }

            verb = RimKataWeaponSlotUtility.BestRangedCombatVerb(pawn, target)
                ?? RimKataWeaponSlotUtility.CombatVerb(
                    pawn,
                    RimKataWeaponSlotUtility.PrimaryWeapon(pawn));
            return verb != null;
        }

        private static bool IsEligibleHostileRimKataPawn(Pawn pawn)
        {
            return pawn?.Faction != null
                && Faction.OfPlayer != null
                && pawn.Faction.HostileTo(Faction.OfPlayer)
                && !pawn.InMentalState
                && !pawn.IsBurning();
        }

        private static bool IsValidEnemyTarget(Pawn pawn, Thing target)
        {
            return pawn?.Map != null
                && target != null
                && target != pawn
                && target.Spawned
                && !target.Destroyed
                && target.Map == pawn.Map
                && RimKataTargeting.IsAutomaticEnemy(pawn, target)
                && (!(target is Pawn targetPawn) || (!targetPawn.Dead && !targetPawn.Downed && !targetPawn.Crawling && !targetPawn.IsPsychologicallyInvisible()));
        }
    }

    [HarmonyPatch(
        typeof(FloatMenuUtility),
        nameof(FloatMenuUtility.GetMeleeAttackAction))]
    public static class Patch_FloatMenuUtility_GetMeleeAttackAction_RimKata
    {
        public static void Postfix(
            Pawn pawn,
            LocalTargetInfo target,
            ref Action __result)
        {
            if (__result == null
                || !RimKataAttackGizmoTargetContext.Active
                || !target.HasThing)
            {
                return;
            }

            Action originalAction = __result;
            Thing targetThing = target.Thing;
            __result = delegate
            {
                bool closeTargetAccepted = RimKataDualWeaponController
                    .TryNotifyPlayerMeleeCloseTarget(
                        pawn,
                        targetThing,
                        true);
                if (!closeTargetAccepted
                    || pawn?.CurJobDef != RimKataDefOf.RimKata_Attack)
                {
                    originalAction();
                }
            };
        }
    }

    [HarmonyPatch(typeof(FloatMenuUtility), nameof(FloatMenuUtility.GetRangedAttackAction))]
    public static class Patch_FloatMenuUtility_GetRangedAttackAction_RimKata
    {
        public static bool Prefix(
            Pawn pawn,
            LocalTargetInfo target,
            ref System.Action __result,
            ref string failStr)
        {
            if (RimKataAttackGizmoTargetContext.Active)
            {
                return true;
            }

            if (!RimKataDualWeaponController.IsDedicatedFollowupActive(pawn)
                || pawn?.Drafted != true
                || pawn.IsBurning()
                || !target.IsValid
                || !target.HasThing
                || target.Pawn?.IsPsychologicallyInvisible() == true
                || !RimKataWeaponSlotUtility.CanAttackTargetWithoutRushing(
                    pawn,
                    target.Thing))
            {
                return true;
            }

            Verb verb = RimKataWeaponSlotUtility.BestRangedCombatVerb(
                pawn,
                target.Thing);
            if (verb == null)
            {
                return true;
            }

            failStr = string.Empty;
            if (!pawn.IsColonistPlayerControlled && !pawn.IsColonyMech && !pawn.IsColonySubhumanPlayerControlled)
            {
                failStr = "CannotOrderNonControlledLower".Translate();
            }
            else if (pawn.IsColonyMechPlayerControlled && !MechanitorUtility.InMechanitorCommandRange(pawn, target))
            {
                failStr = "OutOfCommandRange".Translate();
            }
            else if (pawn.WorkTagIsDisabled(WorkTags.Violent))
            {
                failStr = "IsIncapableOfViolenceLower".Translate(pawn.LabelShort, pawn);
            }
            else if (pawn == target.Thing)
            {
                failStr = "CannotAttackSelf".Translate();
            }
            else if (target.Thing is Pawn sameFactionPawn
                && (pawn.InSameExtraFaction(sameFactionPawn, ExtraFactionType.HomeFaction) || pawn.InSameExtraFaction(sameFactionPawn, ExtraFactionType.MiniFaction)))
            {
                failStr = "CannotAttackSameFactionMember".Translate();
            }
            else if (target.Thing is Pawn innocentAnimal
                && HistoryEventUtility.IsKillingInnocentAnimal(pawn, innocentAnimal)
                && !new HistoryEvent(HistoryEventDefOf.KilledInnocentAnimal, pawn.Named(HistoryEventArgsNames.Doer)).DoerWillingToDo())
            {
                failStr = "IdeoligionForbids".Translate();
            }
            else if (target.Thing is Pawn veneratedAnimal
                && pawn.Ideo != null
                && pawn.Ideo.IsVeneratedAnimal(veneratedAnimal)
                && !new HistoryEvent(HistoryEventDefOf.HuntedVeneratedAnimal, pawn.Named(HistoryEventArgsNames.Doer)).DoerWillingToDo())
            {
                failStr = "IdeoligionForbids".Translate();
            }
            else
            {
                bool fromAttackGizmo =
                    RimKataAttackGizmoTargetContext.Active;
                __result = delegate
                {
                    RimKataDualWeaponController.NotifyPlayerWeaponTarget(
                        pawn,
                        verb,
                        target.Thing,
                        fromAttackGizmo);
                };
                return false;
            }

            failStr = failStr.CapitalizeFirst();
            __result = null;
            return false;
        }

        public static void Postfix(
            Pawn pawn,
            LocalTargetInfo target,
            ref System.Action __result,
            ref string failStr)
        {
            if (__result != null
                || !RimKataAttackGizmoTargetContext.Active
                || !target.HasThing
                || failStr != "TooClose".Translate().CapitalizeFirst()
                || !RimKataDualWeaponController
                    .CanNotifyPlayerMeleeCloseTarget(
                        pawn,
                        target.Thing))
            {
                return;
            }

            __result = FloatMenuUtility.GetMeleeAttackAction(
                pawn,
                target,
                out failStr,
                false);
        }
    }

    [HarmonyPatch]
    public static class Patch_ConfigurableHostilityResponse_RimKata
    {
        public static System.Reflection.MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(JobGiver_ConfigurableHostilityResponse), "TryGetAttackNearbyEnemyJob");
        }

        public static void Postfix(
            Pawn pawn,
            ref Job __result)
        {
            if (PreferVanillaCloseMeleeThreat(pawn, ref __result))
            {
                return;
            }

            if (RejectNonAutomaticMeleeThreat(pawn, ref __result))
            {
                return;
            }

            if (pawn?.Drafted == true
                || pawn == null
                || pawn.IsBurning()
                || !RimKataEligibility.RandomAttackEnabledForPawn(pawn)
                || (__result?.def != JobDefOf.AttackStatic && __result?.def != JobDefOf.AttackMelee)
                || !__result.targetA.HasThing
                || !RimKataEligibility.CanBeginGunKataAttack(pawn))
            {
                return;
            }

            Thing target = __result.targetA.Thing;

            if (target == null
                || target.Destroyed
                || !target.Spawned
                || target.Map != pawn.Map
                || !RimKataTargeting.IsAutomaticEnemy(pawn, target))
            {
                return;
            }
            if (__result.def == JobDefOf.AttackMelee)
            {
                return;
            }

            Verb rangedVerb =
                RimKataWeaponSlotUtility.BestRangedCombatVerb(pawn, target);
            if (rangedVerb != null)
            {
                __result.verbToUse = rangedVerb;
            }
        }

        private static bool PreferVanillaCloseMeleeThreat(
            Pawn pawn,
            ref Job job)
        {
            Thing threat = pawn?.mindState?.meleeThreat;
            if (pawn?.Map == null
                || !RimKataEligibility.HasRimKataAccess(pawn)
                || RimKataDualWeaponController.CounterattackControlEnabled(pawn)
                || pawn.Drafted
                || pawn.InMentalState
                || pawn.IsBurning()
                || pawn.WorkTagIsDisabled(WorkTags.Violent)
                || PawnUtility.PlayerForcedJobNowOrSoon(pawn)
                || job == null
                || (job.def != JobDefOf.AttackStatic
                    && job.def != JobDefOf.AttackMelee)
                || job.playerForced
                || threat == null
                || threat.Destroyed
                || !threat.Spawned
                || threat.Map != pawn.Map
                || !pawn.mindState.MeleeThreatStillThreat
                || !RimKataTargeting.IsAutomaticEnemy(pawn, threat)
                || !pawn.CanReachImmediate(threat, PathEndMode.Touch))
            {
                return false;
            }

            Job currentJob = pawn.CurJob;
            if (currentJob?.def == JobDefOf.AttackMelee
                && currentJob.targetA.Thing == threat
                && !currentJob.playerForced)
            {
                if (job != currentJob)
                {
                    JobMaker.ReturnToPool(job);
                }

                job = null;
                return true;
            }

            if (job.def == JobDefOf.AttackMelee
                && job.targetA.Thing == threat)
            {
                return true;
            }

            Job previousJob = job;
            job = JobMaker.MakeJob(JobDefOf.AttackMelee, threat);
            JobMaker.ReturnToPool(previousJob);
            return true;
        }

        private static bool RejectNonAutomaticMeleeThreat(
            Pawn pawn,
            ref Job job)
        {
            if (pawn == null
                || job?.def != JobDefOf.AttackMelee
                || job.playerForced
                || !job.targetA.HasThing
                || !RimKataEligibility.CanBeginGunKataAttack(pawn)
                || RimKataTargeting.IsAutomaticEnemy(
                    pawn,
                    job.targetA.Thing))
            {
                return false;
            }

            ClearRejectedMeleeThreat(pawn, job.targetA.Thing);
            job = null;
            return true;
        }

        internal static void ClearRejectedMeleeThreat(
            Pawn pawn,
            Thing target)
        {
            if (pawn?.mindState?.meleeThreat == target)
            {
                pawn.mindState.meleeThreat = null;
            }
        }
    }

    [HarmonyPatch(typeof(JobGiver_ReactToCloseMeleeThreat), "TryGiveJob")]
    public static class Patch_JobGiverReactToCloseMeleeThreat_RimKata
    {
        public static void Postfix(Pawn pawn, ref Job __result)
        {
            if (pawn == null
                || !RimKataEligibility.CanBeginGunKataAttack(pawn))
            {
                return;
            }

            Thing threat = __result?.targetA.Thing
                ?? pawn.mindState?.meleeThreat;
            if (threat == null
                || RimKataTargeting.IsAutomaticEnemy(pawn, threat))
            {
                return;
            }

            Patch_ConfigurableHostilityResponse_RimKata
                .ClearRejectedMeleeThreat(pawn, threat);
            if (__result?.def == JobDefOf.AttackMelee
                && !__result.playerForced)
            {
                __result = null;
            }
        }
    }

    [HarmonyPatch(
        typeof(Pawn),
        nameof(Pawn.TryGetAttackVerb),
        new Type[]
        {
            typeof(Thing),
            typeof(bool),
            typeof(bool)
        })]
    public static class Patch_Pawn_TryGetAttackVerb_RimKataPairRange
    {
        public static void Postfix(
            Pawn __instance,
            Thing __0,
            ref Verb __result)
        {
            if (__instance?.Map == null
                || __instance.InMentalState
                || !RimKataEligibility.CanBeginGunKataAttack(__instance)
                || !RimKataWeaponSlotUtility.CanUseSecondarySlot(__instance)
                || (__0 != null
                    && __instance.CanReachImmediate(__0, PathEndMode.Touch)))
            {
                return;
            }

            ThingWithComps primary = RimKataWeaponSlotUtility.PrimaryWeapon(
                __instance);
            ThingWithComps secondary = RimKataWeaponSlotUtility.SecondaryWeapon(
                __instance);
            ThingWithComps resultWeapon = __result?.EquipmentSource as ThingWithComps;
            if (__result != null
                && resultWeapon != primary
                && resultWeapon != secondary)
            {
                return;
            }

            Verb pairVerb = RimKataWeaponSlotUtility.BestRangedCombatVerb(
                __instance,
                __0);
            if (pairVerb != null)
            {
                __result = pairVerb;
            }
        }
    }
}
