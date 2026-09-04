using HarmonyLib;
using RimWorld;
using System.Runtime.CompilerServices;
using Verse;
using Verse.AI;

namespace KRWF.RimKata
{
    internal static class RimKataPendingFollowupTickCache
    {
        private sealed class PendingMarker
        {
        }

        private static readonly ConditionalWeakTable<Pawn, PendingMarker>
            PendingPawns = new ConditionalWeakTable<Pawn, PendingMarker>();
        private static readonly ConditionalWeakTable<Pawn, PendingMarker>
            .CreateValueCallback CreateMarker = delegate { return new PendingMarker(); };

        public static bool Contains(Pawn pawn)
        {
            return pawn != null
                && PendingPawns.TryGetValue(pawn, out PendingMarker _);
        }

        public static void Mark(Pawn pawn)
        {
            if (pawn != null)
            {
                PendingPawns.GetValue(pawn, CreateMarker);
            }
        }

        public static void Clear(Pawn pawn)
        {
            if (pawn != null)
            {
                PendingPawns.Remove(pawn);
            }
        }

        public static void Synchronize(Pawn pawn, bool pending)
        {
            if (pending)
            {
                Mark(pawn);
            }
            else
            {
                Clear(pawn);
            }
        }
    }

    public static class RimKataDraftedFireController
    {
        public static void Tick(Pawn pawn)
        {
            if (pawn?.Drafted == true)
            {
                TickDualWeaponController(pawn, null, false);
                return;
            }

            StateFor(pawn, false)?.ClearDraftedMovementSearchTracking();
        }

        public static void ProcessJobTrackerTick(Pawn pawn)
        {
            if (pawn == null)
            {
                return;
            }

            if (pawn.InMentalState)
            {
                return;
            }

            if (pawn.Drafted)
            {
                RimKataPawnCombatState state = StateFor(pawn, false);
                if (state?.dedicatedFollowupJobPending == true)
                {
                    RimKataDualWeaponController
                        .TryConsumePendingDedicatedFollowupJob(pawn, state);
                }

                TickDualWeaponController(pawn, state, true);
                return;
            }

            if (RimKataPendingFollowupTickCache.Contains(pawn))
            {
                RimKataDualWeaponController.TryConsumePendingDedicatedFollowupJob(
                    pawn);
            }
        }

        private static void TickDualWeaponController(
            Pawn pawn,
            RimKataPawnCombatState state,
            bool existingStateKnown)
        {
            if (pawn == null
                || pawn.InMentalState
                || pawn.CurJobDef == RimKataDefOf.RimKata_Attack)
            {
                return;
            }

            if (!pawn.Drafted)
            {
                return;
            }

            JobDef currentJobDef = pawn.CurJobDef;
            if (!IsAutomaticFireJob(currentJobDef))
            {
                if (!existingStateKnown)
                {
                    state = StateFor(pawn, false);
                    existingStateKnown = true;
                }
                state?.ClearDraftedMovementSearchTracking();
                if (state?.dedicatedFollowupJobPending == true
                    && state.dedicatedFollowupJobPlayerForced)
                {
                    return;
                }

                ResetIfActive(pawn, state);
                return;
            }

            bool automaticRangedFireAllowed = pawn.drafter?.FireAtWill == true;
            if (!automaticRangedFireAllowed)
            {
                if (!existingStateKnown)
                {
                    state = StateFor(pawn, false);
                    existingStateKnown = true;
                }
                state?.ClearDraftedMovementSearchTracking();
                if (state == null)
                {
                    return;
                }
            }

            if (pawn.IsBurning())
            {
                if (!existingStateKnown)
                {
                    state = StateFor(pawn, false);
                    existingStateKnown = true;
                }
                state?.ClearDraftedMovementSearchTracking();
                CancelForFire(pawn, state);
                return;
            }

            bool ordinaryAttackAllowed =
                RimKataEligibility.CanBeginGunKataAttack(pawn);
            if (!ordinaryAttackAllowed)
            {
                if (!existingStateKnown)
                {
                    state = StateFor(pawn, false);
                    existingStateKnown = true;
                }
                if (!RimKataDualWeaponController.CanContinueProjectileInterception(
                        pawn,
                        state))
                {
                    ResetIfActive(pawn, state);
                    return;
                }
            }

            bool randomAttackEnabled =
                RimKataMod.Settings?.randomAttackEnabled != false;

            bool movementSearch = false;
            if (automaticRangedFireAllowed && ordinaryAttackAllowed)
            {
                state ??= StateFor(pawn, true);
                movementSearch = RimKataDualWeaponController
                    .NotifyDraftedMovementCell(
                        pawn,
                        state,
                        true);
            }
            else if (state == null)
            {
                return;
            }

            bool combatDemand = movementSearch
                || RimKataDualWeaponController.HasCombatContinuity(
                    pawn,
                    state,
                    randomAttackEnabled);

            if (!combatDemand)
            {
                ResetIfActive(pawn, state);

                return;
            }

            Thing requestedCloseTarget =
                ordinaryAttackAllowed && state.CloseAttackRequestActive
                    ? state.closeAttackRequestTarget
                    : null;
            bool closePlayerForced = false;
            bool closeKillIncappedTarget = false;
            if (requestedCloseTarget != null)
            {
                state.TryGetForcedAttackRequestContext(
                    requestedCloseTarget,
                    out closePlayerForced,
                    out closeKillIncappedTarget);
            }

            Thing immediateCloseTarget =
                ordinaryAttackAllowed
                    ? RimKataDualWeaponController.ResolveImmediateCloseTarget(
                        pawn,
                        state,
                        requestedCloseTarget,
                        closePlayerForced,
                        closeKillIncappedTarget)
                    : null;
            bool closeContext = immediateCloseTarget != null;

            if (!RimKataDualWeaponController.HasUsableWeapon(
                    pawn,
                    closeContext,
                    true))
            {
                ResetIfActive(pawn, state);

                return;
            }

            state.draftedFireActive = true;

            RimKataDualWeaponController.TickWithKnownState(
                pawn,
                state,
                immediateCloseTarget,
                closePlayerForced,
                closeKillIncappedTarget,
                closeContext,
                true,
                automaticRangedFireAllowed);
        }

        public static bool TryApplyResponseCooldown(
            Pawn pawn,
            ThingWithComps weapon,
            Verb selectedVerb,
            LocalTargetInfo focus)
        {
            if (pawn?.IsBurning() == true)
            {
                CancelForFire(pawn);
                return true;
            }

            if (!CanControllerPrerequisites(pawn, true))
            {
                return false;
            }

            RimKataPawnCombatState state = StateFor(pawn, true);
            state.draftedFireActive = true;
            return RimKataDualWeaponController.TryApplyResponseCooldown(pawn, weapon, selectedVerb, focus);
        }

        public static void CancelForFire(Pawn pawn)
        {
            CancelForFire(pawn, StateFor(pawn, false));
        }

        private static void CancelForFire(
            Pawn pawn,
            RimKataPawnCombatState state)
        {
            state?.CancelOffenseForFire();
            if (pawn?.stances?.curStance is Stance_RimKataAim)
            {
                pawn.stances.SetStance(new Stance_Mobile());
            }
        }

        public static bool ShouldReplacePhysicalMeleeAttack(
            Pawn pawn,
            Thing target)
        {
            return TryQueuePhysicalMeleeAttack(pawn, target);
        }

        public static bool TryQueuePhysicalMeleeAttack(Pawn pawn, Thing target)
        {
            if (pawn?.Map == null
                || pawn.InMentalState
                || target == null
                || target.Destroyed
                || !target.Spawned
                || target.Map != pawn.Map)
            {
                return false;
            }

            bool dedicatedJob = pawn.CurJobDef == RimKataDefOf.RimKata_Attack;
            bool controllerDriven = dedicatedJob
                || CanControllerPrerequisites(pawn);
            if (!controllerDriven
                || !pawn.CanReachImmediate(target, PathEndMode.Touch)
                || !RimKataDualWeaponController.HasUsableWeapon(
                    pawn,
                    true,
                    !dedicatedJob))
            {
                return false;
            }

            RimKataPawnCombatState state = StateFor(pawn, true);
            if (state == null)
            {
                return false;
            }

            bool playerForced = false;
            bool killIncappedTarget = false;
            state.TryGetForcedAttackRequestContext(
                target,
                out playerForced,
                out killIncappedTarget);
            if ((!RimKataTargeting.IsAutomaticEnemy(pawn, target)
                    && !playerForced)
                || (target is Pawn targetPawn
                    && !RimKataTargeting.IsPawnTargetStateValid(
                        targetPawn,
                        playerForced && killIncappedTarget))
                || RimKataDualWeaponController.ResolveImmediateCloseTarget(
                    pawn,
                    null,
                    target,
                    playerForced,
                    killIncappedTarget) != target)
            {
                return false;
            }

            state.RequestCloseAttack(target);
            if (pawn.Drafted)
            {
                state.draftedFireActive = true;
            }

            return state.CloseAttackRequestActive;
        }

        public static void NotifyTargetedByHostile(Pawn target, Pawn attacker)
        {
            if (target == null
                || attacker == null
                || target == attacker
                || !target.Spawned
                || !attacker.Spawned
                || target.Map != attacker.Map
                || target.Drafted != true
                || !RimKataTargeting.IsAutomaticEnemy(target, attacker)
                || !RimKataTargeting.IsPawnTargetStateValid(attacker)
                || !CanControllerPrerequisites(target))
            {
                return;
            }

            RimKataPawnCombatState state = StateFor(target, true);
            if (state != null)
            {
                state.NotifyIncomingThreat(attacker);
            }
        }

        private static bool CanControllerPrerequisites(
            Pawn pawn,
            bool burningKnownFalse = false)
        {
            return pawn?.Drafted == true
                && !pawn.InMentalState
                && IsAutomaticFireJob(pawn.CurJobDef)
                && (burningKnownFalse || !pawn.IsBurning())
                && RimKataEligibility.CanBeginGunKataAttack(pawn);
        }

        // !!! Debug HUD !!!
        public static string DebugCombatDemandReasons(Pawn pawn)
        {
            if (pawn == null)
            {
                return "-";
            }

            RimKataPawnCombatState state = StateFor(pawn, false);
            if (state == null)
            {
                return "-";
            }

            string reasons = "";

            if (state.DodgeMovementActive)
                reasons += "D";

            if (state.DebugIncomingThreatStored)
                reasons += "I";

            if (state.DebugCloseAttackRequestStored)
                reasons += "C";

            if (state.primaryWeaponCycle?.CombatActive == true)
                reasons += "1";

            if (state.secondaryWeaponCycle?.CombatActive == true)
                reasons += "2";

            if (state.MovementFireContinuityActive)
                reasons += "M";

            if (state.DraftedMovementSearchTriggerPending)
                reasons += "Q";

            Thing enemyTarget = pawn.mindState?.enemyTarget;
            if (enemyTarget != null
                && enemyTarget.Spawned
                && enemyTarget.Map == pawn.Map
                && RimKataTargeting.IsAutomaticEnemy(pawn, enemyTarget))
            {
                reasons += "E";
            }

            return reasons.Length > 0 ? reasons : "-";
        }

        private static bool IsAutomaticFireJob(JobDef jobDef)
        {
            return jobDef == null
                || jobDef == JobDefOf.Goto
                || jobDef == JobDefOf.Wait
                || jobDef == JobDefOf.Wait_Combat
                || jobDef == JobDefOf.Wait_MaintainPosture
                || jobDef == JobDefOf.AttackMelee;
        }

        private static RimKataPawnCombatState StateFor(Pawn pawn, bool create)
        {
            return pawn?.Map?.GetComponent<RimKataMapComponent>()?.GetState(pawn, create);
        }

        private static void ClearAimStance(Pawn pawn)
        {
            if (pawn?.stances?.curStance is Stance_RimKataAim)
            {
                pawn.stances.SetStance(new Stance_Mobile());
            }
        }

        private static void ResetIfActive(Pawn pawn, RimKataPawnCombatState state)
        {
            if (state == null
                || (!state.DraftedFireActive
                    && !state.WeaponCyclesActive
                    && !(pawn?.stances?.curStance is Stance_RimKataAim)))
            {
                return;
            }

            state.CancelDraftedFire(false);
            RimKataDualWeaponController.DeactivateNonJobCycleWork(pawn);
            ClearAimStance(pawn);
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.JobTrackerTick))]
    public static class Patch_PawnJobTracker_DraftedRimKataFire
    {
        public static void Postfix(Pawn ___pawn)
        {
            RimKataDraftedFireController.ProcessJobTrackerTick(___pawn);
        }
    }

    [HarmonyPatch(typeof(Pawn_MeleeVerbs), nameof(Pawn_MeleeVerbs.TryMeleeAttack))]
    public static class Patch_PawnMeleeVerbs_WaitCombatRimKata
    {
        public static bool Prefix(Pawn ___pawn, Thing target, ref bool __result)
        {
            RimKataDraftedFireController.NotifyTargetedByHostile(target as Pawn, ___pawn);
            if (!RimKataDraftedFireController.TryQueuePhysicalMeleeAttack(___pawn, target))
            {
                return true;
            }

            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn_StanceTracker), nameof(Pawn_StanceTracker.SetStance))]
    public static class Patch_PawnStanceTracker_RimKataHostileAim
    {
        public static void Postfix(
            Pawn ___pawn,
            Stance newStance)
        {
            if (!(newStance is Stance_Warmup)
                && !(newStance is Stance_RimKataLeaningAim))
            {
                return;
            }

            Stance_Busy busy = newStance as Stance_Busy;
            Verb verb = busy?.verb;
            Pawn target = busy?.focusTarg.Pawn;
            RimKataDraftedFireController.NotifyTargetedByHostile(target, ___pawn);
        }
    }
}
