using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
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
        internal static bool IsDraftedCombatSequenceActiveForUi(
            Pawn pawn,
            JobDef jobDef)
        {
            Map map = pawn?.Map;
            if (pawn?.Drafted != true
                || map == null
                || pawn.InMentalState
                || !IsAutomaticFireJob(jobDef)
                || !RimKataCombatStatePresenceCache.Contains(pawn, map))
            {
                return false;
            }

            return map.GetComponent<RimKataMapComponent>()
                ?.IsDualEngagementActive(pawn) == true;
        }

        public static void Tick(Pawn pawn)
        {
            if (pawn?.Drafted == true)
            {
                TickDualWeaponController(pawn, null, false, false);
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

            // The dedicated JobDriver owns the combat tick.  This postfix only
            // needs to service the rare hand-off request while that Job is live.
            if (pawn.CurJobDef == RimKataDefOf.RimKata_Attack)
            {
                if (RimKataPendingFollowupTickCache.Contains(pawn))
                {
                    RimKataDualWeaponController
                        .TryConsumePendingDedicatedFollowupJob(pawn);
                }

                return;
            }

            if (pawn.Drafted)
            {
                bool moving = pawn.pather?.Moving == true;
                Map map = pawn.Map;
                bool statePresent =
                    RimKataCombatStatePresenceCache.Contains(pawn, map);
                if (!statePresent
                    && (!moving
                        || !RimKataDualWeaponController
                            .HasAutomaticMovementSearchPotential(pawn)))
                {
                    return;
                }

                RimKataPawnCombatState state = StateFor(pawn, false);
                if (state?.dedicatedFollowupJobPending == true)
                {
                    RimKataDualWeaponController
                        .TryConsumePendingDedicatedFollowupJob(pawn, state);
                }

                // New combat work is published by the attack, movement,
                // defensive-response, and projectile-wake entry points before
                // it reaches this per-tick driver.  A state-less moving pawn
                // only falls through when the map actually has an automatic
                // attack or interception candidate to wake for.
                if (state == null && !moving)
                {
                    return;
                }

                TickDualWeaponController(pawn, state, true, true);
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
            bool existingStateKnown,
            bool mentalStateKnownFalse)
        {
            if (pawn == null
                || (!mentalStateKnownFalse && pawn.InMentalState)
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

            if (!existingStateKnown)
            {
                state = StateFor(pawn, false);
            }

            // This adapter preserves the player's current Job and command context.
            // Eligibility, movement search, continuity and weapon plans belong to
            // the shared controller, exactly as they do for a dedicated combat Job.
            Thing requestedCloseTarget = state?.closeAttackRequestTarget;
            bool closePlayerForced = false;
            bool closeKillIncappedTarget = false;
            if (requestedCloseTarget != null)
            {
                state.TryGetForcedAttackRequestContext(
                    requestedCloseTarget,
                    out closePlayerForced,
                    out closeKillIncappedTarget);
            }

            RimKataDualWeaponController.TickWithKnownState(
                pawn,
                state,
                null,
                closePlayerForced,
                closeKillIncappedTarget,
                null,
                false,
                automaticRangedFireAllowed,
                false);
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

            if (!CanControllerPrerequisites(pawn))
            {
                return false;
            }

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

        private static bool CanControllerPrerequisites(Pawn pawn)
        {
            return pawn?.Drafted == true
                && IsAutomaticFireJob(pawn.CurJobDef)
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

        internal static bool IsAutomaticFireJob(JobDef jobDef)
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
                && !(newStance is Stance_RimKataAim))
            {
                return;
            }

            Stance_Busy busy = newStance as Stance_Busy;
            Verb verb = busy?.verb;
            Pawn target = busy?.focusTarg.Pawn;
            RimKataDraftedFireController.NotifyTargetedByHostile(target, ___pawn);
        }
    }

    internal static class RimKataDormantHostileMovementRegistry
    {
        private sealed class MapEntry
        {
            internal readonly HashSet<Pawn> receivers =
                new HashSet<Pawn>();
            internal readonly HashSet<Pawn> pendingHostiles =
                new HashSet<Pawn>();
            internal readonly List<Pawn> hostileSnapshot =
                new List<Pawn>();
            internal readonly List<Pawn> receiverSnapshot =
                new List<Pawn>();
        }

        private static readonly ConditionalWeakTable<Map, MapEntry> ByMap =
            new ConditionalWeakTable<Map, MapEntry>();
        private static readonly ConditionalWeakTable<Map, MapEntry>
            .CreateValueCallback CreateEntry = delegate { return new MapEntry(); };

        internal static void NotifyAccessChanged(Pawn pawn, bool hasAccess)
        {
            Map map = pawn?.Map;
            if (map == null)
            {
                return;
            }

            if (hasAccess && IsLiveReceiverMember(pawn, map))
            {
                ByMap.GetValue(map, CreateEntry).receivers.Add(pawn);
            }
            else if (ByMap.TryGetValue(map, out MapEntry entry))
            {
                entry.receivers.Remove(pawn);
            }
        }

        internal static void NotifyDraftStatusChanged(Pawn pawn)
        {
            Map map = pawn?.Map;
            if (map == null)
            {
                return;
            }

            if (IsLiveReceiverMember(pawn, map)
                && RimKataEligibility.HasRimKataAccess(pawn))
            {
                ByMap.GetValue(map, CreateEntry).receivers.Add(pawn);
            }
            else if (ByMap.TryGetValue(map, out MapEntry entry))
            {
                entry.receivers.Remove(pawn);
            }
        }

        internal static void NotifyPathStarted(Pawn pawn)
        {
            NotifyPathMovement(pawn);
        }

        internal static void NotifyPathCellEntered(Pawn pawn)
        {
            NotifyPathMovement(pawn);
        }

        internal static void NotifyPathStopped(Pawn pawn)
        {
            Map map = pawn?.Map;
            if (map != null
                && ByMap.TryGetValue(map, out MapEntry entry))
            {
                entry.receivers.Remove(pawn);
                entry.pendingHostiles.Remove(pawn);
            }
        }

        internal static void ProcessPending(Map map)
        {
            if (map == null
                || !ByMap.TryGetValue(map, out MapEntry entry))
            {
                return;
            }

            if (Find.TickManager?.slower?.ForcedNormalSpeed != false)
            {
                entry.pendingHostiles.Clear();
                return;
            }

            if (entry.pendingHostiles.Count == 0)
            {
                return;
            }

            BuildLiveReceiverSnapshot(map, entry);
            BuildPendingHostileSnapshot(map, entry);
            for (int i = 0;
                i < entry.receiverSnapshot.Count
                    && entry.hostileSnapshot.Count > 0;
                i++)
            {
                RimKataDualWeaponController.TryReceiveDormantMovingHostiles(
                    entry.receiverSnapshot[i],
                    entry.hostileSnapshot);
            }

            entry.pendingHostiles.Clear();
            entry.hostileSnapshot.Clear();
            entry.receiverSnapshot.Clear();
        }

        private static void NotifyPathMovement(Pawn pawn)
        {
            Map map = pawn?.Map;
            if (map == null
                || !pawn.Spawned
                || Find.TickManager?.slower?.ForcedNormalSpeed != false)
            {
                return;
            }

            bool hostileToPlayer = Faction.OfPlayer != null
                && pawn.HostileTo(Faction.OfPlayer);
            if (hostileToPlayer)
            {
                if (IsLiveMovingHostile(pawn, map)
                    && ByMap.TryGetValue(map, out MapEntry entry)
                    && entry.receivers.Count > 0)
                {
                    entry.pendingHostiles.Add(pawn);
                }
                return;
            }

            if (!ByMap.TryGetValue(map, out MapEntry existing))
            {
                if (!IsLiveReceiverMember(pawn, map)
                    || !RimKataEligibility.HasRimKataAccess(pawn))
                {
                    return;
                }

                existing = ByMap.GetValue(map, CreateEntry);
            }
            else
            {
                existing.pendingHostiles.Remove(pawn);
            }

            if (IsLiveReceiverMember(pawn, map)
                && RimKataEligibility.HasRimKataAccess(pawn))
            {
                existing.receivers.Add(pawn);
            }
            else
            {
                existing.receivers.Remove(pawn);
            }
        }

        private static void BuildLiveReceiverSnapshot(
            Map map,
            MapEntry entry)
        {
            entry.receiverSnapshot.Clear();
            foreach (Pawn receiver in entry.receivers)
            {
                entry.receiverSnapshot.Add(receiver);
            }

            for (int i = entry.receiverSnapshot.Count - 1; i >= 0; i--)
            {
                Pawn receiver = entry.receiverSnapshot[i];
                if (IsLiveReceiverMember(receiver, map))
                {
                    continue;
                }

                entry.receiverSnapshot.RemoveAt(i);
                entry.receivers.Remove(receiver);
            }
        }

        private static void BuildPendingHostileSnapshot(
            Map map,
            MapEntry entry)
        {
            entry.hostileSnapshot.Clear();
            foreach (Pawn hostile in entry.pendingHostiles)
            {
                if (IsLiveMovingHostile(hostile, map))
                {
                    entry.hostileSnapshot.Add(hostile);
                }
            }
        }

        private static bool IsLiveReceiverMember(Pawn pawn, Map map)
        {
            return pawn != null
                && !pawn.Destroyed
                && pawn.Spawned
                && !pawn.Dead
                && pawn.Map == map
                && pawn.IsPlayerControlled
                && pawn.Drafted
                && pawn.pather?.Moving == true;
        }

        private static bool IsLiveMovingHostile(Pawn pawn, Map map)
        {
            return pawn != null
                && !pawn.Destroyed
                && pawn.Spawned
                && !pawn.Dead
                && pawn.Map == map
                && pawn.pather?.Moving == true
                && Faction.OfPlayer != null
                && pawn.HostileTo(Faction.OfPlayer);
        }
    }

    [HarmonyPatch(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.StartPath))]
    public static class Patch_PawnPathFollower_RimKataDormantPathStarted
    {
        public static void Postfix(Pawn ___pawn)
        {
            if (___pawn?.pather?.Moving == true)
            {
                RimKataDormantHostileMovementRegistry.NotifyPathStarted(
                    ___pawn);
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_PathFollower), "TryEnterNextPathCell")]
    public static class Patch_PawnPathFollower_RimKataDormantPathCell
    {
        public static void Prefix(Pawn ___pawn, out IntVec3 __state)
        {
            __state = ___pawn?.Position ?? IntVec3.Invalid;
        }

        public static void Postfix(Pawn ___pawn, IntVec3 __state)
        {
            if (___pawn?.Spawned == true
                && __state.IsValid
                && ___pawn.Position != __state)
            {
                RimKataDormantHostileMovementRegistry.NotifyPathCellEntered(
                    ___pawn);
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.StopDead))]
    public static class Patch_PawnPathFollower_RimKataDormantPathStopped
    {
        public static void Postfix(Pawn ___pawn)
        {
            RimKataDormantHostileMovementRegistry.NotifyPathStopped(___pawn);
        }
    }
}
