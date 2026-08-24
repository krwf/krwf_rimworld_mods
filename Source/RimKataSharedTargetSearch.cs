using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace KRWF.RimKata
{
    public sealed class RimKataSharedTargetSearchState : IExposable
    {
        public bool sessionActive;
        public bool scanActive;
        public bool reachedMaximum;
        public bool idleProjectileTrigger;
        public int maximumRing;
        public int completedRing;
        public float effectiveMaximumRange;
        public float completedOuterRadius;
        public IntVec3 origin = IntVec3.Invalid;
        public int lastAdvancedTick = -1;

        public bool KeepsCombatAlive => scanActive;

        public void ExposeData()
        {
            Scribe_Values.Look(ref sessionActive, "sessionActive");
            Scribe_Values.Look(ref scanActive, "scanActive");
            Scribe_Values.Look(ref reachedMaximum, "reachedMaximum");
            Scribe_Values.Look(
                ref idleProjectileTrigger,
                "idleProjectileTrigger");
            Scribe_Values.Look(ref maximumRing, "maximumRing");
            Scribe_Values.Look(ref completedRing, "completedRing");
            Scribe_Values.Look(
                ref effectiveMaximumRange,
                "effectiveMaximumRange");
            Scribe_Values.Look(
                ref completedOuterRadius,
                "completedOuterRadius");
            Scribe_Values.Look(ref origin, "origin", IntVec3.Invalid);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                maximumRing = Mathf.Max(0, maximumRing);
                completedRing = Mathf.Max(0, completedRing);
                effectiveMaximumRange = Mathf.Max(0f, effectiveMaximumRange);
                completedOuterRadius = Mathf.Max(0f, completedOuterRadius);
                lastAdvancedTick = -1;
                if (scanActive)
                {
                    sessionActive = true;
                }
            }
        }

        public void Reset()
        {
            sessionActive = false;
            scanActive = false;
            reachedMaximum = false;
            idleProjectileTrigger = false;
            maximumRing = 0;
            completedRing = 0;
            effectiveMaximumRange = 0f;
            completedOuterRadius = 0f;
            origin = IntVec3.Invalid;
            lastAdvancedTick = -1;
        }
    }

    internal static class RimKataSharedTargetSearch
    {
        internal const float ApiRadiusPadding = 0.7f;
        private const float RadiusEpsilon = 0.001f;
        private const int TouchCandidateLimit = 8;
        private const int ShortCandidateLimit = 16;
        private const int MediumCandidateLimit = 12;
        private const int LongCandidateLimit = 8;

        private static readonly List<Thing> EligibleCandidates =
            new List<Thing>();

        internal static bool Begin(
            Pawn pawn,
            RimKataPawnCombatState combatState,
            IntVec3 origin,
            bool idleProjectileTrigger = false)
        {
            RimKataSharedTargetSearchState search =
                combatState?.sharedTargetSearch;
            if (pawn?.Map == null
                || pawn.InMentalState
                || search == null
                || !origin.IsValid
                || search.scanActive)
            {
                return false;
            }

            bool removedCandidate = Prune(pawn, combatState, false);
            if (!idleProjectileTrigger
                && ShouldSkipSaturatedCandidateSearch(
                    pawn,
                    combatState,
                    origin,
                    removedCandidate))
            {
                return false;
            }

            float maximumRange = MaximumRange(pawn, combatState);
            if (maximumRange <= 0f)
            {
                return false;
            }

            search.sessionActive = true;
            search.scanActive = true;
            search.reachedMaximum = false;
            search.idleProjectileTrigger = idleProjectileTrigger
                && !RandomAttackEnabled(pawn);
            search.effectiveMaximumRange = maximumRange;
            search.maximumRing = MaximumLogicalRing(maximumRange);
            search.completedRing = 0;
            search.completedOuterRadius = 0f;
            search.origin = origin;
            search.lastAdvancedTick = -1;
            InitializeCollectionClosure(
                pawn,
                combatState,
                combatState.primaryWeaponCycle);
            InitializeCollectionClosure(
                pawn,
                combatState,
                combatState.secondaryWeaponCycle);
            RimKataDebugHUD.RecordSearchIndicator(pawn);
            return true;
        }

        internal static bool Restart(
            Pawn pawn,
            RimKataPawnCombatState combatState,
            IntVec3 origin)
        {
            combatState?.sharedTargetSearch?.Reset();
            return Begin(pawn, combatState, origin);
        }

        internal static bool Advance(
            Pawn pawn,
            RimKataPawnCombatState combatState,
            Thing knownTarget)
        {
            RimKataSharedTargetSearchState search =
                combatState?.sharedTargetSearch;
            if (pawn?.Map == null
                || pawn.InMentalState
                || search?.sessionActive != true
                || !search.scanActive)
            {
                return false;
            }

            int currentTick = Find.TickManager?.TicksGame ?? -1;
            if (currentTick >= 0 && search.lastAdvancedTick == currentTick)
            {
                return false;
            }
            search.lastAdvancedTick = currentTick;

            Prune(pawn, combatState);
            TryAddKnownAutomaticTarget(pawn, combatState, knownTarget);

            float maximumRange = MaximumRange(pawn, combatState);
            if (maximumRange <= 0f)
            {
                Finish(combatState, true);
                return false;
            }

            search.effectiveMaximumRange = maximumRange;
            search.maximumRing = MaximumLogicalRing(maximumRange);
            int innerRing = Mathf.Max(0, search.completedRing);
            int outerRing = Mathf.Min(innerRing + 1, search.maximumRing);
            float innerRadius = innerRing <= 0
                ? -1f
                : innerRing + ApiRadiusPadding;
            float outerRadius = outerRing + ApiRadiusPadding;
            IntVec3 center = search.origin.IsValid
                ? search.origin
                : pawn.Position;

            CloseCollectionsAtBandEntry(
                pawn,
                combatState,
                outerRing);
            if (BothCandidateCollectionsClosed(combatState))
            {
                Finish(combatState, false);
                return true;
            }

            CollectAutomaticTargetsInRing(
                pawn,
                combatState,
                center,
                innerRadius,
                outerRadius);
            RimKataDebugHUD.RecordActualSearchRing(
                pawn,
                pawn.Map,
                center,
                innerRadius,
                outerRadius);

            search.completedRing = outerRing;
            search.completedOuterRadius = outerRadius;
            UpdateCollectionClosure(
                pawn,
                combatState,
                outerRing);

            bool bothClosed = SlotCollectionClosed(
                    pawn,
                    combatState,
                    combatState.primaryWeaponCycle,
                    outerRing)
                && SlotCollectionClosed(
                    pawn,
                    combatState,
                    combatState.secondaryWeaponCycle,
                    outerRing);
            bool reachedMaximum = outerRing >= search.maximumRing;
            if (bothClosed || reachedMaximum)
            {
                Finish(combatState, reachedMaximum);
            }

            return true;
        }

        internal static bool TrySelectCandidate(
            Pawn pawn,
            RimKataPawnCombatState combatState,
            Verb verb,
            Thing preferredTarget,
            out Thing target,
            out bool interception)
        {
            target = null;
            interception = false;
            if (pawn?.Map == null || combatState == null || verb == null)
            {
                return false;
            }

            RimKataWeaponCycleState cycle = CycleForVerb(combatState, verb);
            if (cycle == null)
            {
                return false;
            }

            PruneCycle(pawn, combatState, cycle, verb);
            bool randomAttack = RandomAttackEnabled(pawn);
            bool idleProjectilePriority = !randomAttack
                && (combatState.idleProjectileSearchTriggerPending
                    || combatState.sharedTargetSearch.idleProjectileTrigger);
            if (!randomAttack
                && !idleProjectilePriority
                && !(preferredTarget is Projectile)
                && IsValidForCycle(
                    pawn,
                    combatState,
                    cycle,
                    verb,
                    preferredTarget))
            {
                target = preferredTarget;
                return true;
            }

            EligibleCandidates.Clear();
            if (!idleProjectilePriority)
            {
                List<Thing> ordinary = cycle.automaticCandidates;
                for (int i = 0; i < ordinary.Count; i++)
                {
                    Thing candidate = ordinary[i];
                    if (IsValidForCycle(
                        pawn,
                        combatState,
                        cycle,
                        verb,
                        candidate))
                    {
                        EligibleCandidates.Add(candidate);
                    }
                }

                if (randomAttack
                    && !(preferredTarget is Projectile)
                    && IsValidForCycle(
                        pawn,
                        combatState,
                        cycle,
                        verb,
                        preferredTarget)
                    && !EligibleCandidates.Contains(preferredTarget))
                {
                    EligibleCandidates.Add(preferredTarget);
                }
            }

            bool includeProjectiles = randomAttack
                || idleProjectilePriority;
            if (includeProjectiles
                && RimKataMod.Settings?.explosiveInterceptionEnabled != false)
            {
                float projectileRange = FullRangeForCycle(
                    pawn,
                    cycle,
                    verb);
                pawn.Map.GetComponent<RimKataMapComponent>()?
                    .AppendValidHostileProjectiles(
                        pawn,
                        verb,
                        projectileRange * projectileRange,
                        EligibleCandidates);
            }

            if (EligibleCandidates.Count == 0)
            {
                return false;
            }

            if (randomAttack)
            {
                target = EligibleCandidates.RandomElement();
            }
            else if (idleProjectilePriority)
            {
                target = ClosestProjectile(pawn, EligibleCandidates);
            }
            else
            {
                target = ClosestOrdinaryTarget(pawn, EligibleCandidates);
            }

            interception = target is Projectile;
            return target != null;
        }

        internal static bool CompleteIdleProjectilePriorityPass(
            Pawn pawn,
            RimKataPawnCombatState combatState)
        {
            RimKataSharedTargetSearchState search =
                combatState?.sharedTargetSearch;
            if (search?.idleProjectileTrigger != true || search.scanActive)
            {
                return false;
            }

            search.idleProjectileTrigger = false;
            Prune(pawn, combatState);
            return true;
        }

        internal static void TryAddKnownAutomaticTarget(
            Pawn pawn,
            RimKataPawnCombatState combatState,
            Thing target)
        {
            if (pawn?.Map == null
                || pawn.InMentalState
                || combatState == null
                || target is Projectile
                || !RimKataTargeting.IsValidAutomaticAttackTarget(pawn, target))
            {
                return;
            }

            TryAddToCycle(
                pawn,
                combatState,
                combatState.primaryWeaponCycle,
                target);
            TryAddToCycle(
                pawn,
                combatState,
                combatState.secondaryWeaponCycle,
                target);
        }

        internal static void Prune(
            Pawn pawn,
            RimKataPawnCombatState combatState)
        {
            Prune(pawn, combatState, true);
        }

        private static bool Prune(
            Pawn pawn,
            RimKataPawnCombatState combatState,
            bool restartFinishedSearchAfterRemoval)
        {
            if (combatState == null)
            {
                return false;
            }

            bool removedCandidate = PruneCycle(
                pawn,
                combatState,
                combatState.primaryWeaponCycle,
                CombatVerbForCycle(
                    pawn,
                    combatState,
                    combatState.primaryWeaponCycle))
                | PruneCycle(
                pawn,
                combatState,
                combatState.secondaryWeaponCycle,
                CombatVerbForCycle(
                    pawn,
                    combatState,
                    combatState.secondaryWeaponCycle));

            RimKataSharedTargetSearchState search =
                combatState.sharedTargetSearch;
            if (restartFinishedSearchAfterRemoval
                && removedCandidate
                && combatState.dualEngagementActive
                && search?.sessionActive == true
                && !search.scanActive)
            {
                combatState.ResetCandidateSaturationExpansion(true);
                Restart(pawn, combatState, pawn.Position);
            }
            return removedCandidate;
        }

        internal static bool IsValidForVerb(
            Pawn pawn,
            Verb verb,
            Thing target)
        {
            if (pawn?.Map == null || verb == null || target == null)
            {
                return false;
            }

            RimKataPawnCombatState state = pawn.Map
                .GetComponent<RimKataMapComponent>()?
                .GetState(pawn, false);
            RimKataWeaponCycleState cycle = CycleForVerb(state, verb);
            return cycle != null
                && IsValidForCycle(pawn, state, cycle, verb, target);
        }

        private static void CollectAutomaticTargetsInRing(
            Pawn pawn,
            RimKataPawnCombatState combatState,
            IntVec3 center,
            float innerRadius,
            float outerRadius)
        {
            float innerSquared = innerRadius < 0f
                ? -1f
                : innerRadius * innerRadius;
            float outerSquared = outerRadius * outerRadius;
            if (outerRadius <= GenRadial.MaxRadialPatternRadius)
            {
                int startIndex = innerRadius < 0f
                    ? 0
                    : GenRadial.NumCellsInRadius(innerRadius);
                int endIndex = GenRadial.NumCellsInRadius(outerRadius);
                for (int i = startIndex; i < endIndex; i++)
                {
                    IntVec3 offset = GenRadial.RadialPattern[i];
                    float distanceSquared = offset.LengthHorizontalSquared;
                    if (distanceSquared <= innerSquared
                        || distanceSquared > outerSquared)
                    {
                        continue;
                    }

                    IntVec3 cell = center + offset;
                    if (cell.InBounds(pawn.Map))
                    {
                        CollectAutomaticTargetsInCell(
                            pawn,
                            combatState,
                            cell);
                    }
                }
                return;
            }

            int extent = Mathf.CeilToInt(outerRadius);
            int minX = Mathf.Max(0, center.x - extent);
            int maxX = Mathf.Min(pawn.Map.Size.x - 1, center.x + extent);
            int minZ = Mathf.Max(0, center.z - extent);
            int maxZ = Mathf.Min(pawn.Map.Size.z - 1, center.z + extent);
            for (int z = minZ; z <= maxZ; z++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    IntVec3 cell = new IntVec3(x, 0, z);
                    float distanceSquared = center.DistanceToSquared(cell);
                    if (distanceSquared > innerSquared
                        && distanceSquared <= outerSquared)
                    {
                        CollectAutomaticTargetsInCell(
                            pawn,
                            combatState,
                            cell);
                    }
                }
            }
        }

        private static void CollectAutomaticTargetsInCell(
            Pawn pawn,
            RimKataPawnCombatState combatState,
            IntVec3 cell)
        {
            List<Thing> things = pawn.Map.thingGrid.ThingsListAtFast(cell);
            for (int i = 0; i < things.Count; i++)
            {
                Thing candidate = things[i];
                if (candidate is Projectile
                    || !(candidate is IAttackTarget)
                    || !RimKataTargeting.IsValidAutomaticAttackTarget(
                        pawn,
                        candidate))
                {
                    continue;
                }

                TryAddToCycle(
                    pawn,
                    combatState,
                    combatState.primaryWeaponCycle,
                    candidate);
                TryAddToCycle(
                    pawn,
                    combatState,
                    combatState.secondaryWeaponCycle,
                    candidate);
            }
        }

        private static void TryAddToCycle(
            Pawn pawn,
            RimKataPawnCombatState combatState,
            RimKataWeaponCycleState cycle,
            Thing target)
        {
            if (cycle?.automaticCandidateCollectionClosed == true)
            {
                return;
            }

            Verb verb = CombatVerbForCycle(pawn, combatState, cycle);
            if (IsValidForCycle(
                pawn,
                combatState,
                cycle,
                verb,
                target))
            {
                cycle.AddAutomaticCandidate(target);
            }
        }

        private static bool PruneCycle(
            Pawn pawn,
            RimKataPawnCombatState combatState,
            RimKataWeaponCycleState cycle,
            Verb verb)
        {
            if (cycle?.automaticCandidates == null)
            {
                return false;
            }

            bool removed = false;
            for (int i = cycle.automaticCandidates.Count - 1; i >= 0; i--)
            {
                Thing candidate = cycle.automaticCandidates[i];
                if (candidate is Projectile
                    || !IsValidForCycle(
                    pawn,
                    combatState,
                    cycle,
                    verb,
                    candidate))
                {
                    cycle.RemoveAutomaticCandidate(candidate);
                    removed = true;
                }
            }
            return removed;
        }

        private static bool IsValidForCycle(
            Pawn pawn,
            RimKataPawnCombatState combatState,
            RimKataWeaponCycleState cycle,
            Verb verb,
            Thing target,
            bool useFullRange = false)
        {
            bool physicalMeleeWithRangedWeapon =
                IsCloseCombatContext(combatState)
                && RimKataMod.Settings?.closeFireEnabled == false
                && verb?.IsMeleeAttack == false;
            if (pawn?.Map == null
                || cycle?.weapon == null
                || verb == null
                || (verb.ApparelPreventsShooting()
                    && !physicalMeleeWithRangedWeapon)
                || target == null
                || target.Destroyed
                || !target.Spawned
                || target.Map != pawn.Map)
            {
                return false;
            }

            float range = target is Projectile || useFullRange
                ? FullRangeForCycle(pawn, cycle, verb)
                : RangeForCycle(pawn, combatState, cycle, verb);
            if (range <= 0f
                || pawn.Position.DistanceToSquared(target.Position)
                    > range * range)
            {
                return false;
            }

            if (target is Projectile projectile)
            {
                return !verb.IsMeleeAttack
                    && RimKataTargeting.IsValidExplosiveProjectileForVerb(
                        pawn,
                        verb,
                        projectile,
                        range * range);
            }

            if (!(target is IAttackTarget)
                || !RimKataTargeting.IsValidAutomaticAttackTarget(pawn, target)
                || target.Position.Fogged(pawn.Map))
            {
                return false;
            }

            if (!verb.IsMeleeAttack && IsCloseCombatContext(combatState))
            {
                if (!pawn.CanReachImmediate(target, PathEndMode.Touch))
                {
                    return false;
                }

                return RimKataMod.Settings?.closeFireEnabled == false
                    || RimKataEligibility
                        .IsRangedVerbAvailableInCloseCombat(pawn, verb);
            }

            return verb.CanHitTarget(target);
        }

        private static void UpdateCollectionClosure(
            Pawn pawn,
            RimKataPawnCombatState combatState,
            int outerRing)
        {
            bool primarySaturated = UpdateCycleCollectionClosure(
                pawn,
                combatState,
                combatState.primaryWeaponCycle,
                outerRing,
                out bool primaryVacancy);
            bool secondarySaturated = UpdateCycleCollectionClosure(
                pawn,
                combatState,
                combatState.secondaryWeaponCycle,
                outerRing,
                out bool secondaryVacancy);

            ReleaseConsumedCandidateLimit(
                combatState.primaryWeaponCycle,
                outerRing);
            ReleaseConsumedCandidateLimit(
                combatState.secondaryWeaponCycle,
                outerRing);

            if (!IsCloseCombatContext(combatState))
            {
                if (!combatState.candidateSaturationExpansionUsed
                    && (primarySaturated || secondarySaturated))
                {
                    if (primarySaturated)
                    {
                        TryScheduleNextCandidateLimit(
                            pawn,
                            combatState.primaryWeaponCycle,
                            outerRing);
                    }
                    if (secondarySaturated)
                    {
                        TryScheduleNextCandidateLimit(
                            pawn,
                            combatState.secondaryWeaponCycle,
                            outerRing);
                    }
                    combatState.candidateSaturationExpansionUsed = true;
                }

                if (primaryVacancy || secondaryVacancy)
                {
                    combatState.candidateSaturationExpansionUsed = false;
                }
            }
        }

        private static void CloseCollectionsAtBandEntry(
            Pawn pawn,
            RimKataPawnCombatState combatState,
            int outerRing)
        {
            int previousLimit = outerRing > 1
                ? CandidateLimitForRing(outerRing - 1)
                : CandidateLimitForRing(outerRing);
            int currentLimit = CandidateLimitForRing(outerRing);
            if (currentLimit >= previousLimit)
            {
                return;
            }

            CloseCycleCollectionAtBandEntry(
                pawn,
                combatState,
                combatState?.primaryWeaponCycle,
                outerRing);
            CloseCycleCollectionAtBandEntry(
                pawn,
                combatState,
                combatState?.secondaryWeaponCycle,
                outerRing);
        }

        private static void CloseCycleCollectionAtBandEntry(
            Pawn pawn,
            RimKataPawnCombatState combatState,
            RimKataWeaponCycleState cycle,
            int outerRing)
        {
            if (cycle?.automaticCandidateCollectionClosed != false)
            {
                return;
            }

            int limit = EffectiveCandidateLimitForRing(cycle, outerRing);
            if (CountValidStoredCandidatesThroughRing(
                    pawn,
                    combatState,
                    cycle,
                    outerRing) >= limit)
            {
                cycle.automaticCandidateCollectionClosed = true;
            }
        }

        private static bool BothCandidateCollectionsClosed(
            RimKataPawnCombatState combatState)
        {
            return (combatState?.primaryWeaponCycle == null
                    || combatState.primaryWeaponCycle
                        .automaticCandidateCollectionClosed)
                && (combatState?.secondaryWeaponCycle == null
                    || combatState.secondaryWeaponCycle
                        .automaticCandidateCollectionClosed);
        }

        private static bool ShouldSkipSaturatedCandidateSearch(
            Pawn pawn,
            RimKataPawnCombatState combatState,
            IntVec3 origin,
            bool removedCandidate)
        {
            if (combatState?.candidateSaturationExpansionUsed != true
                || IsCloseCombatContext(combatState))
            {
                return false;
            }

            if (removedCandidate)
            {
                combatState.ResetCandidateSaturationExpansion(true);
                return false;
            }

            if (HasPendingCandidateLimitOverride(combatState))
            {
                return false;
            }

            bool primarySaturated = StoredCandidatesSaturateCycle(
                pawn,
                combatState,
                combatState.primaryWeaponCycle,
                origin,
                out bool primaryUsable);
            bool secondarySaturated = StoredCandidatesSaturateCycle(
                pawn,
                combatState,
                combatState.secondaryWeaponCycle,
                origin,
                out bool secondaryUsable);
            if ((primaryUsable || secondaryUsable)
                && (!primaryUsable || primarySaturated)
                && (!secondaryUsable || secondarySaturated))
            {
                return true;
            }

            combatState.ResetCandidateSaturationExpansion(true);
            return false;
        }

        private static bool HasPendingCandidateLimitOverride(
            RimKataPawnCombatState combatState)
        {
            return combatState?.primaryWeaponCycle
                    ?.pendingCandidateLimitOverride > 0
                || combatState?.primaryWeaponCycle
                    ?.activeCandidateLimitOverride > 0
                || combatState?.secondaryWeaponCycle
                    ?.pendingCandidateLimitOverride > 0
                || combatState?.secondaryWeaponCycle
                    ?.activeCandidateLimitOverride > 0;
        }

        private static bool StoredCandidatesSaturateCycle(
            Pawn pawn,
            RimKataPawnCombatState combatState,
            RimKataWeaponCycleState cycle,
            IntVec3 origin,
            out bool usable)
        {
            Verb verb = CombatVerbForCycle(pawn, combatState, cycle);
            float range = RangeForCycle(
                pawn,
                combatState,
                cycle,
                verb);
            usable = cycle?.weapon != null && verb != null && range > 0f;
            if (!usable)
            {
                return false;
            }

            List<Thing> candidates = cycle.automaticCandidates;
            int maximumRing = MaximumLogicalRing(range);
            for (int ring = 1; ring <= maximumRing; ring++)
            {
                int limit = CandidateLimitForRing(ring);
                if (candidates == null || candidates.Count < limit)
                {
                    continue;
                }

                float outerRadius = ring + ApiRadiusPadding;
                float outerSquared = outerRadius * outerRadius;
                int count = 0;
                for (int i = 0; i < candidates.Count; i++)
                {
                    Thing candidate = candidates[i];
                    if (candidate != null
                        && origin.DistanceToSquared(candidate.Position)
                            <= outerSquared
                        && ++count >= limit)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static void InitializeCollectionClosure(
            Pawn pawn,
            RimKataPawnCombatState combatState,
            RimKataWeaponCycleState cycle)
        {
            if (cycle == null)
            {
                return;
            }

            Verb verb = CombatVerbForCycle(pawn, combatState, cycle);
            float fullRange = FullRangeForCycle(pawn, cycle, verb);
            if (IsCloseCombatContext(combatState))
            {
                cycle.activeCandidateLimitOverride = 0;
            }
            else
            {
                cycle.activeCandidateLimitOverride = Mathf.Max(
                    0,
                    cycle.pendingCandidateLimitOverride);
                cycle.pendingCandidateLimitOverride = 0;
            }
            cycle.automaticCandidateCollectionClosed = fullRange <= 0f;
        }

        private static bool UpdateCycleCollectionClosure(
            Pawn pawn,
            RimKataPawnCombatState combatState,
            RimKataWeaponCycleState cycle,
            int outerRing,
            out bool hasCandidateVacancy)
        {
            hasCandidateVacancy = false;
            if (cycle == null || cycle.automaticCandidateCollectionClosed)
            {
                return false;
            }

            Verb verb = CombatVerbForCycle(pawn, combatState, cycle);
            float cycleRange = RangeForCycle(
                pawn,
                combatState,
                cycle,
                verb);
            int limit = EffectiveCandidateLimitForRing(cycle, outerRing);
            int maximumRing = MaximumLogicalRing(cycleRange);
            bool reachedWeaponRange = cycleRange <= 0f
                || outerRing >= maximumRing;
            bool saturated = CountValidStoredCandidatesThroughRing(
                    pawn,
                    combatState,
                    cycle,
                    outerRing) >= limit;
            hasCandidateVacancy = !saturated;

            cycle.automaticCandidateCollectionClosed = reachedWeaponRange
                || saturated;
            return saturated;
        }

        private static bool SlotCollectionClosed(
            Pawn pawn,
            RimKataPawnCombatState combatState,
            RimKataWeaponCycleState cycle,
            int outerRing)
        {
            if (cycle?.weapon == null)
            {
                return true;
            }

            Verb verb = CombatVerbForCycle(pawn, combatState, cycle);
            return cycle.automaticCandidateCollectionClosed
                || outerRing >= MaximumLogicalRing(
                    RangeForCycle(
                        pawn,
                        combatState,
                        cycle,
                        verb));
        }

        private static bool TryScheduleNextCandidateLimit(
            Pawn pawn,
            RimKataWeaponCycleState cycle,
            int saturatedRing)
        {
            if (pawn?.Map == null
                || cycle?.weapon == null
                || cycle.pendingCandidateLimitOverride > 0
                || cycle.activeCandidateLimitOverride > 0)
            {
                return false;
            }

            Verb verb = RimKataWeaponSlotUtility.CombatVerb(
                pawn,
                cycle.weapon);
            float fullRange = FullRangeForCycle(pawn, cycle, verb);
            int maximumRing = MaximumLogicalRing(fullRange);
            int currentLimit = CandidateLimitForRing(saturatedRing);
            int nextLimit = 0;
            for (int ring = saturatedRing + 1; ring <= maximumRing; ring++)
            {
                int ringLimit = CandidateLimitForRing(ring);
                if (ringLimit == currentLimit)
                {
                    continue;
                }

                nextLimit = ringLimit;
                break;
            }

            if (nextLimit <= currentLimit)
            {
                return false;
            }

            cycle.pendingCandidateLimitOverride = nextLimit;
            return true;
        }

        private static int EffectiveCandidateLimitForRing(
            RimKataWeaponCycleState cycle,
            int ring)
        {
            return Mathf.Max(
                CandidateLimitForRing(ring),
                cycle?.activeCandidateLimitOverride ?? 0);
        }

        private static void ReleaseConsumedCandidateLimit(
            RimKataWeaponCycleState cycle,
            int ring)
        {
            if (cycle?.activeCandidateLimitOverride > 0
                && CandidateLimitForRing(ring)
                    >= cycle.activeCandidateLimitOverride)
            {
                cycle.activeCandidateLimitOverride = 0;
            }
        }

        private static int CountValidStoredCandidatesThroughRing(
            Pawn pawn,
            RimKataPawnCombatState combatState,
            RimKataWeaponCycleState cycle,
            int outerRing)
        {
            Verb verb = CombatVerbForCycle(pawn, combatState, cycle);
            List<Thing> candidates = cycle?.automaticCandidates;
            if (pawn?.Map == null
                || verb == null
                || candidates == null
                || outerRing <= 0)
            {
                return 0;
            }

            float outerRadius = outerRing + ApiRadiusPadding;
            float outerSquared = outerRadius * outerRadius;
            IntVec3 center = combatState?.sharedTargetSearch != null
                    && combatState.sharedTargetSearch.origin.IsValid
                ? combatState.sharedTargetSearch.origin
                : pawn.Position;
            int count = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                Thing candidate = candidates[i];
                if (candidate is Projectile
                    || !IsValidForCycle(
                        pawn,
                        combatState,
                        cycle,
                        verb,
                        candidate)
                    || center.DistanceToSquared(candidate.Position)
                        > outerSquared)
                {
                    continue;
                }

                count++;
            }
            return count;
        }

        private static int CandidateLimitForRing(int ring)
        {
            float radius = ring;
            RimKataRangeBands bands = RimKataRangeUtility.CurrentBands;
            if (radius <= bands.Touch + RadiusEpsilon)
            {
                return TouchCandidateLimit;
            }
            if (radius <= bands.Short + RadiusEpsilon)
            {
                return ShortCandidateLimit;
            }
            if (radius <= bands.Medium + RadiusEpsilon)
            {
                return MediumCandidateLimit;
            }
            return LongCandidateLimit;
        }

        private static int MaximumLogicalRing(float range)
        {
            return Mathf.Max(
                1,
                Mathf.CeilToInt(
                    Mathf.Max(0f, range - ApiRadiusPadding)));
        }

        private static float MaximumRange(
            Pawn pawn,
            RimKataPawnCombatState combatState)
        {
            RimKataWeaponCycleState primary =
                combatState?.primaryWeaponCycle;
            RimKataWeaponCycleState secondary =
                combatState?.secondaryWeaponCycle;
            return Mathf.Max(
                RangeForCycle(
                    pawn,
                    combatState,
                    primary,
                    CombatVerbForCycle(pawn, combatState, primary)),
                RangeForCycle(
                    pawn,
                    combatState,
                    secondary,
                    CombatVerbForCycle(pawn, combatState, secondary)));
        }

        private static float RangeForCycle(
            Pawn pawn,
            RimKataPawnCombatState combatState,
            RimKataWeaponCycleState cycle,
            Verb verb)
        {
            float fullRange = FullRangeForCycle(pawn, cycle, verb);
            if (fullRange <= 0f || verb?.IsMeleeAttack != false)
            {
                return fullRange;
            }

            bool closeContext = IsCloseCombatContext(combatState);
            if (closeContext)
            {
                return Mathf.Min(1.7f, fullRange);
            }

            return fullRange;
        }

        private static float FullRangeForCycle(
            Pawn pawn,
            RimKataWeaponCycleState cycle,
            Verb verb)
        {
            if (pawn?.Map == null || cycle?.weapon == null || verb == null)
            {
                return 0f;
            }

            float actualRange = Mathf.Max(
                0f,
                RimKataRangeUtility.ResolveCandidateRange(verb));
            return actualRange;
        }

        private static bool IsCloseCombatContext(
            RimKataPawnCombatState combatState)
        {
            return combatState?.dualCloseCombatActive == true;
        }

        private static Verb CombatVerbForCycle(
            Pawn pawn,
            RimKataPawnCombatState combatState,
            RimKataWeaponCycleState cycle)
        {
            return RimKataWeaponSlotUtility.CombatVerbForContext(
                pawn,
                cycle?.weapon,
                IsCloseCombatContext(combatState));
        }

        private static RimKataWeaponCycleState CycleForVerb(
            RimKataPawnCombatState combatState,
            Verb verb)
        {
            ThingWithComps weapon = verb?.EquipmentSource as ThingWithComps;
            if (weapon == null || combatState == null)
            {
                return null;
            }

            if (combatState.primaryWeaponCycle?.weapon == weapon)
            {
                return combatState.primaryWeaponCycle;
            }
            return combatState.secondaryWeaponCycle?.weapon == weapon
                ? combatState.secondaryWeaponCycle
                : null;
        }

        private static Thing ClosestOrdinaryTarget(
            Pawn pawn,
            List<Thing> candidates)
        {
            Thing selected = null;
            int bestDistance = int.MaxValue;
            for (int i = 0; i < candidates.Count; i++)
            {
                Thing candidate = candidates[i];
                if (candidate is Projectile)
                {
                    continue;
                }

                int distance = pawn.Position.DistanceToSquared(candidate.Position);
                if (distance < bestDistance)
                {
                    selected = candidate;
                    bestDistance = distance;
                }
            }
            return selected;
        }

        private static Thing ClosestProjectile(
            Pawn pawn,
            List<Thing> candidates)
        {
            Thing selected = null;
            int bestDistance = int.MaxValue;
            for (int i = 0; i < candidates.Count; i++)
            {
                Thing candidate = candidates[i];
                if (!(candidate is Projectile))
                {
                    continue;
                }

                int distance = pawn.Position.DistanceToSquared(candidate.Position);
                if (distance < bestDistance)
                {
                    selected = candidate;
                    bestDistance = distance;
                }
            }
            return selected;
        }

        private static bool RandomAttackEnabled(Pawn pawn)
        {
            return RimKataEligibility.RandomAttackEnabledForPawn(pawn);
        }

        private static void Finish(
            RimKataPawnCombatState combatState,
            bool reachedMaximum)
        {
            RimKataSharedTargetSearchState search =
                combatState?.sharedTargetSearch;
            if (search == null)
            {
                return;
            }

            search.scanActive = false;
            search.reachedMaximum = reachedMaximum;
            if (combatState.primaryWeaponCycle != null)
            {
                combatState.primaryWeaponCycle.activeCandidateLimitOverride = 0;
            }
            if (combatState.secondaryWeaponCycle != null)
            {
                combatState.secondaryWeaponCycle.activeCandidateLimitOverride = 0;
            }
        }
    }
}
