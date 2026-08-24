using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace KRWF.RimKata
{
    public static class RimKataDraftedFireController
    {
        public static void Tick(Pawn pawn)
        {
            TickDualWeaponController(pawn);
        }

        private static void TickDualWeaponController(Pawn pawn)
        {
            if (pawn == null || pawn.CurJobDef == RimKataDefOf.RimKata_Attack)
            {
                return;
            }

            RimKataPawnCombatState state = StateFor(pawn, false);

            if (!pawn.Drafted)
            {
                RimKataDualWeaponController.ClearDraftedMovementTracking(pawn);
                return;
            }

            if (pawn.drafter?.FireAtWill != true || !IsAutomaticFireJob(pawn.CurJobDef))
            {
                RimKataDualWeaponController.ClearDraftedMovementTracking(pawn);
                if (state?.dedicatedFollowupJobPending == true
                    && state.dedicatedFollowupJobPlayerForced)
                {
                    return;
                }

                ResetIfActive(pawn, state);
                return;
            }

            if (pawn.IsBurning())
            {
                RimKataDualWeaponController.ClearDraftedMovementTracking(pawn);
                CancelForFire(pawn);
                return;
            }

            bool physicalDodge = state?.DodgeMovementActive == true;
            if (state?.DodgeMotionBlocksJob == true && !physicalDodge)
            {
                return;
            }

            bool movementSearch = RimKataDualWeaponController
                .NotifyDraftedMovementCell(pawn);
            state = StateFor(pawn, false);
            bool combatDemand = movementSearch
                || HasCombatDemand(pawn, state, physicalDodge);

            if (!combatDemand)
            {
                ResetIfActive(pawn, state);

                return;
            }

            if (!CanAutoFirePrerequisites(pawn))
            {
                ResetIfActive(pawn, state);
                return;
            }

            state ??= StateFor(pawn, true);

            Thing requestedCloseTarget =
                state.CloseAttackRequestActive
                    ? state.closeAttackRequestTarget
                    : null;

            Thing immediateCloseTarget =
                RimKataDualWeaponController.ResolveImmediateCloseTarget(
                    pawn,
                    requestedCloseTarget,
                    false,
                    false);
            bool closeContext = immediateCloseTarget != null;

            if (!RimKataDualWeaponController.HasUsableWeapon(pawn, closeContext))
            {
                ResetIfActive(pawn, state);

                return;
            }

            state.draftedFireActive = true;

            RimKataDualWeaponController.Tick(
                pawn,
                immediateCloseTarget,
                false,
                false,
                closeContext,
                false,
                true);
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

            if (!CanAutoFire(pawn, out Verb _))
            {
                return false;
            }

            RimKataPawnCombatState state = StateFor(pawn, true);
            state.draftedFireActive = true;
            return RimKataDualWeaponController.TryApplyResponseCooldown(pawn, weapon, selectedVerb, focus);
        }

        public static void CancelForFire(Pawn pawn)
        {
            StateFor(pawn, false)?.CancelOffenseForFire();
            if (pawn?.stances?.curStance is Stance_RimKataAim)
            {
                pawn.stances.SetStance(new Stance_Mobile());
            }
        }

        public static bool ShouldReplacePhysicalMeleeAttack(Pawn pawn)
        {
            if (!RimKataDualWeaponController.IsDedicatedFollowupActive(pawn))
            {
                return false;
            }

            if (pawn?.CurJobDef == RimKataDefOf.RimKata_Attack)
            {
                return RimKataDualWeaponController.HasUsableWeapon(pawn, true);
            }

            return IsAutomaticFireJob(pawn?.CurJobDef)
                && CanAutoFirePrerequisites(pawn)
                && TryOwnAutomaticAttack(pawn, null);
        }

        public static bool TryQueuePhysicalMeleeAttack(Pawn pawn, Thing target)
        {
            if (pawn?.Map == null
                || target == null
                || target.Destroyed
                || !target.Spawned
                || target.Map != pawn.Map
                || !pawn.CanReachImmediate(target, PathEndMode.Touch))
            {
                return false;
            }

            bool controllerDriven = RimKataDualWeaponController.IsDedicatedFollowupActive(pawn)
                && (pawn.CurJobDef == RimKataDefOf.RimKata_Attack
                    || (IsAutomaticFireJob(pawn.CurJobDef) && CanAutoFirePrerequisites(pawn)));
            if (!controllerDriven
                && RimKataDualWeaponController.TryBeginRandomMeleeControl(
                    pawn,
                    target))
            {
                return true;
            }

            if (!controllerDriven
                || (!RimKataTargeting.IsAutomaticEnemy(pawn, target)
                    && !(pawn.CurJob?.playerForced == true
                        && pawn.CurJob.targetA.Thing == target))
                || !RimKataDualWeaponController.HasUsableWeapon(pawn, true))
            {
                return false;
            }

            RimKataPawnCombatState state = StateFor(pawn, true);
            state.RequestCloseAttack(target);
            if (pawn.Drafted)
            {
                state.draftedFireActive = true;
            }

            return true;
        }

        public static void NotifyTargetedByHostile(Pawn target, Pawn attacker)
        {
            if (target == null
                || attacker == null
                || target == attacker
                || !target.Spawned
                || !attacker.Spawned
                || target.Map != attacker.Map
                || !RimKataTargeting.IsAutomaticEnemy(target, attacker)
                || attacker.Dead
                || attacker.Downed
                || attacker.Crawling
                || attacker.IsPsychologicallyInvisible()
                || !CanAutoFirePrerequisites(target))
            {
                return;
            }

            RimKataPawnCombatState state = StateFor(target, true);
            if (state != null)
            {
                state.NotifyIncomingThreat(attacker);
                if (target.CanReachImmediate(attacker, PathEndMode.Touch))
                {
                    target.Map.GetComponent<RimKataMapComponent>()?.EnterCloseCombat(target, attacker);
                }
            }
        }

        private static bool TryOwnAutomaticAttack(Pawn pawn, Thing target)
        {
            bool closeCombatContext = RimKataDualWeaponController
                .ResolveImmediateCloseTarget(
                    pawn,
                    target,
                    false,
                    false) != null;
            bool replace = RimKataDualWeaponController.HasUsableWeapon(
                pawn,
                closeCombatContext);
            if (replace)
            {
                RimKataPawnCombatState state = StateFor(pawn, true);
                state.RequestAutomaticAttack(target);
                RimKataDualWeaponController.RegisterAutomaticTarget(
                    pawn,
                    target);
            }

            return replace;
        }

        private static bool CanAutoFire(Pawn pawn, out Verb verb)
        {
            verb = null;
            return CanAutoFirePrerequisites(pawn) && TryGetAutoFireVerb(pawn, out verb);
        }

        private static bool CanAutoFirePrerequisites(Pawn pawn)
        {
            return pawn?.Drafted == true
                && !pawn.IsBurning()
                && pawn.drafter?.FireAtWill == true
                && IsAutomaticFireJob(pawn.CurJobDef)
                && RimKataEligibility.CanBeginGunKataAttack(pawn);
        }

        private static bool TryGetAutoFireVerb(Pawn pawn, out Verb verb)
        {
            verb = null;
            if (pawn == null)
            {
                return false;
            }

            ThingWithComps primary = RimKataWeaponSlotUtility.PrimaryWeapon(pawn);

            Verb primaryVerb = RimKataWeaponSlotUtility.CombatVerb(pawn, primary);

            if (primaryVerb != null && !primaryVerb.IsMeleeAttack)
            {
                verb = primaryVerb;
                return true;
            }

            ThingWithComps secondary =
                RimKataWeaponSlotUtility.CanUseSecondarySlot(pawn)
                    ? RimKataWeaponSlotUtility.SecondaryWeapon(pawn)
                    : null;

            Verb secondaryVerb = RimKataWeaponSlotUtility.CombatVerb(pawn, secondary);

            if (secondaryVerb != null && !secondaryVerb.IsMeleeAttack)
            {
                verb = secondaryVerb;
                return true;
            }

            return false;
        }

        private static bool HasCombatDemand(
            Pawn pawn,
            RimKataPawnCombatState state,
            bool physicalDodge)
        {
            return physicalDodge
                || state?.IncomingThreatActive == true
                || state?.AutomaticAttackRequestActive == true
                || state?.CloseAttackRequestActive == true
                || state?.primaryWeaponCycle?.CombatActive == true
                || state?.secondaryWeaponCycle?.CombatActive == true
                || state?.primaryWeaponCycle?.DedicatedActive == true
                || state?.secondaryWeaponCycle?.DedicatedActive == true
                || state?.openingRingSearchActive == true
                || state?.sharedTargetSearch?.KeepsCombatAlive == true
                || state?.DraftedMovementSearchTriggerPending == true
                || state?.idleProjectileSearchTriggerPending == true
                || state?.dedicatedFollowupJobPending == true;
        }

        // !!! Debug HUD !!!
        public static bool DebugHasCombatDemand(Pawn pawn)
        {
            if (pawn == null)
            {
                return false;
            }

            RimKataPawnCombatState state = StateFor(pawn, false);
            bool physicalDodge = state?.DodgeMovementActive == true;

            return physicalDodge
                || state?.DebugIncomingThreatStored == true
                || state?.DebugAutomaticAttackRequestStored == true
                || state?.DebugCloseAttackRequestStored == true
                || state?.primaryWeaponCycle?.CombatActive == true
                || state?.secondaryWeaponCycle?.CombatActive == true
                || state?.primaryWeaponCycle?.DedicatedActive == true
                || state?.secondaryWeaponCycle?.DedicatedActive == true
                || state?.openingRingSearchActive == true
                || state?.sharedTargetSearch?.KeepsCombatAlive == true
                || state?.DraftedMovementSearchTriggerPending == true
                || state?.idleProjectileSearchTriggerPending == true
                || state?.dedicatedFollowupJobPending == true;
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

            if (state.ResponsePoseActive)
                reasons += "P";

            if (state.DebugIncomingThreatStored)
                reasons += "I";

            if (state.DebugAutomaticAttackRequestStored)
                reasons += "A";

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

            if (state.sharedTargetSearch?.scanActive == true)
                reasons += "S";

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
            if (state?.primaryWeaponCycle?.vanillaOpeningPending == true)
            {
                return;
            }

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
            RimKataDualWeaponController.TryConsumePendingDedicatedFollowupJob(___pawn);
            RimKataDraftedFireController.Tick(___pawn);
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
