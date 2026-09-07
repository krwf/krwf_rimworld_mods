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
        public static void Tick(Pawn pawn)
        {
            RimKataDualWeaponController.TickCombat(pawn, true);
        }

        public static void ProcessJobTrackerTick(Pawn pawn)
        {
            RimKataDualWeaponController.TickCombat(pawn, true);
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
            RimKataDualWeaponController.CancelOffenseForFire(pawn, StateFor(pawn, false));
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
            internal HashSet<IAttackTarget> hostileTargets;
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
                ClearHostileCache(entry);
                return;
            }

            RefreshHostileCache(map, entry);
            if (entry.hostileTargets == null || entry.hostileTargets.Count == 0)
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
                || !pawn.Spawned)
            {
                return;
            }

            if (Find.TickManager?.slower?.ForcedNormalSpeed != false)
            {
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

            RefreshHostileCache(map, existing);
            if (existing.hostileTargets?.Count > 0
                && existing.hostileTargets.Contains(pawn))
            {
                if (existing.receivers.Count > 0
                    && IsLiveMovingHostile(pawn, map, existing.hostileTargets))
                {
                    existing.pendingHostiles.Add(pawn);
                }
                return;
            }

            existing.pendingHostiles.Remove(pawn);
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

        private static void RefreshHostileCache(
            Map map,
            MapEntry entry)
        {
            if (entry.hostileTargets == null)
            {
                // Keep vanilla's live set for this peacetime period, including empty sets.
                entry.hostileTargets = Faction.OfPlayer != null
                    ? map.attackTargetsCache?.TargetsHostileToColony
                    : null;
            }
        }

        internal static void NotifyAttackTargetRegistered(Map map)
        {
            if (map != null
                && ByMap.TryGetValue(map, out MapEntry entry)
                && entry.hostileTargets != null
                && entry.hostileTargets.Count == 0)
            {
                // The initial vanilla emptySet can be replaced by the first hostile set.
                // RegisterTarget also follows faction/mental-state UpdateTarget calls.
                entry.hostileTargets = null;
            }
        }

        private static void ClearHostileCache(MapEntry entry)
        {
            entry.hostileTargets = null;
            entry.pendingHostiles.Clear();
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
                if (IsLiveMovingHostile(hostile, map, entry.hostileTargets))
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

        private static bool IsLiveMovingHostile(
            Pawn pawn,
            Map map,
            HashSet<IAttackTarget> hostileTargets)
        {
            return pawn != null
                && !pawn.Destroyed
                && pawn.Spawned
                && !pawn.Dead
                && pawn.Map == map
                && pawn.pather?.Moving == true
                && hostileTargets?.Contains(pawn) == true;
        }
    }

    [HarmonyPatch(typeof(AttackTargetsCache), "RegisterTarget")]
    public static class Patch_AttackTargetsCache_RimKataDormantHostileRegistration
    {
        public static void Postfix(Map ___map, IAttackTarget target)
        {
            if (target is Pawn)
            {
                RimKataDormantHostileMovementRegistry.NotifyAttackTargetRegistered(
                    ___map);
            }
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
