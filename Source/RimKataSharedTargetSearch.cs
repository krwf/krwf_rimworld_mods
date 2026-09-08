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
        public int maximumRing;
        public int completedRing;
        public float maximumCandidateCellRadius;
        public IntVec3 origin = IntVec3.Invalid;
        public int lastAdvancedTick = -1;

        public bool KeepsCombatAlive => scanActive;

        public void ExposeData()
        {
            Scribe_Values.Look(ref sessionActive, "sessionActive");
            Scribe_Values.Look(ref scanActive, "scanActive");
            Scribe_Values.Look(ref maximumRing, "maximumRing");
            Scribe_Values.Look(ref completedRing, "completedRing");
            Scribe_Values.Look(
                ref maximumCandidateCellRadius,
                "effectiveMaximumRange");
            Scribe_Values.Look(ref origin, "origin", IntVec3.Invalid);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                maximumRing = Mathf.Max(0, maximumRing);
                completedRing = Mathf.Max(0, completedRing);
                maximumCandidateCellRadius = Mathf.Max(
                    0f,
                    maximumCandidateCellRadius);
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
            maximumRing = 0;
            completedRing = 0;
            maximumCandidateCellRadius = 0f;
            origin = IntVec3.Invalid;
            lastAdvancedTick = -1;
        }
    }

    internal static class RimKataSharedTargetSearch
    {
        private const float CandidateCellRadiusPadding =
            RimKataRangeUtility.CandidateCellRadiusPadding;
        private const float RadiusEpsilon = 0.001f;
        // Deliberately covers the center cell and all eight adjacent cells.
        private const float CloseCombatRangedCandidateCellRadius = 1.7f;
        private const int TouchCandidateLimit = 8;
        private const int ShortCandidateLimit = 16;
        private const int MediumCandidateLimit = 12;
        private const int LongCandidateLimit = 8;

        private static readonly List<Thing> EligibleCandidates =
            new List<Thing>();
        private static readonly List<Thing> PrimaryRingCandidates =
            new List<Thing>();
        private static readonly List<Thing> SecondaryRingCandidates =
            new List<Thing>();
        private static readonly HashSet<Thing> VisitedMultiCellRingTargets =
            new HashSet<Thing>();

        private struct RingCandidateSlot
        {
            internal RimKataWeaponCycleState cycle;
            private Verb verb;
            private bool verbResolved;
            private float configuredRadius;
            internal float configuredRadiusSquared;
            private bool radiusResolved;
            private bool newAdmissionChecked;
            private bool newAdmissionAllowed;

            internal Verb ResolveVerb(Pawn pawn)
            {
                if (!verbResolved)
                {
                    verb = CombatVerbForCycle(pawn, cycle);
                    verbResolved = true;
                }
                return verb;
            }

            internal float ResolveConfiguredRadius(
                Pawn pawn,
                RimKataPawnCombatState combatState)
            {
                if (!radiusResolved)
                {
                    Verb resolvedVerb = ResolveVerb(pawn);
                    configuredRadius = cycle?.weapon != null && resolvedVerb != null
                        ? ResolveCandidateCellRadiusForCycle(
                            pawn, combatState, cycle, resolvedVerb)
                        : 0f;
                    configuredRadiusSquared = configuredRadius * configuredRadius;
                    radiusResolved = true;
                }
                return configuredRadius;
            }

            internal bool CanAdmitNew(
                Pawn pawn,
                RimKataPawnCombatState combatState)
            {
                if (!newAdmissionChecked)
                {
                    newAdmissionAllowed = IsSlotAvailableForNewCandidates(
                        pawn, combatState, cycle, ResolveVerb(pawn));
                    newAdmissionChecked = true;
                }
                return newAdmissionAllowed;
            }

            internal float NewCandidateRadius(
                Pawn pawn,
                RimKataPawnCombatState combatState)
            {
                // A failed new-admission gate must not replace the configured
                // shootability radius used by an already registered candidate.
                return CanAdmitNew(pawn, combatState)
                    ? ResolveConfiguredRadius(pawn, combatState)
                    : 0f;
            }
        }

        internal static bool Begin(
            Pawn pawn,
            RimKataPawnCombatState combatState,
            IntVec3 origin)
        {
            RimKataSharedTargetSearchState search =
                combatState?.sharedTargetSearch;
            if (pawn?.Map == null
                || pawn.InMentalState
                || search == null
                || !origin.IsValid)
            {
                return false;
            }

            if (!RandomAttackEnabled(pawn))
            {
                StopOrdinaryTargetSearch(combatState);
                return false;
            }

            if (search.scanActive)
            {
                return true;
            }

            if (ShouldSkipSaturatedCandidateSearch(
                    pawn,
                    combatState,
                    origin))
            {
                return true;
            }

            float maximumCellRadius =
                MaximumCandidateCellRadius(pawn, combatState);
            if (maximumCellRadius <= 0f)
            {
                return false;
            }

            search.sessionActive = true;
            search.scanActive = true;
            search.maximumCandidateCellRadius = maximumCellRadius;
            search.maximumRing = MaximumLogicalRingFromCellRadius(
                maximumCellRadius);
            search.completedRing = 0;
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
            if (Prefs.DevMode && RimKataDebugHUD.Enabled)
            {
                RimKataDebugHUD.RecordSearchIndicator(pawn);
            }
            return true;
        }

        internal static bool Restart(
            Pawn pawn,
            RimKataPawnCombatState combatState,
            IntVec3 origin)
        {
            RimKataSharedTargetSearchState search =
                combatState?.sharedTargetSearch;
            search?.Reset();
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

            if (!RandomAttackEnabled(pawn))
            {
                StopOrdinaryTargetSearch(combatState);
                return false;
            }

            int currentTick = Find.TickManager?.TicksGame ?? -1;
            if (currentTick >= 0 && search.lastAdvancedTick == currentTick)
            {
                return false;
            }
            search.lastAdvancedTick = currentTick;

            TryAddKnownAutomaticTarget(pawn, combatState, knownTarget);

            RingCandidateSlot primarySlot = new RingCandidateSlot
            {
                cycle = combatState.primaryWeaponCycle
            };
            RingCandidateSlot secondarySlot = new RingCandidateSlot
            {
                cycle = combatState.secondaryWeaponCycle
            };
            float maximumCellRadius = Mathf.Max(
                primarySlot.NewCandidateRadius(pawn, combatState),
                secondarySlot.NewCandidateRadius(pawn, combatState));
            if (maximumCellRadius <= 0f)
            {
                Finish(combatState);
                return false;
            }

            search.maximumCandidateCellRadius = maximumCellRadius;
            search.maximumRing = MaximumLogicalRingFromCellRadius(
                maximumCellRadius);
            int innerRing = Mathf.Max(0, search.completedRing);
            int outerRing = Mathf.Min(innerRing + 1, search.maximumRing);
            float innerCellRadius = innerRing <= 0
                ? -1f
                : innerRing + CandidateCellRadiusPadding;
            float outerCellRadius = Mathf.Min(
                outerRing + CandidateCellRadiusPadding,
                maximumCellRadius);
            IntVec3 center = search.origin.IsValid
                ? search.origin
                : pawn.Position;

            CloseCollectionsAtBandEntry(
                pawn,
                combatState,
                outerRing);
            if (BothCandidateCollectionsClosed(combatState))
            {
                CompleteScan(pawn, combatState, ref primarySlot, ref secondarySlot);
                return true;
            }

            CollectAutomaticTargetsInRing(
                pawn,
                combatState,
                center,
                innerCellRadius,
                outerCellRadius,
                outerRing,
                ref primarySlot,
                ref secondarySlot);

            search.completedRing = outerRing;
            UpdateCollectionClosure(
                pawn,
                combatState,
                outerRing,
                ref primarySlot,
                ref secondarySlot);

            bool bothClosed = SlotCollectionClosed(
                    pawn,
                    combatState,
                    ref primarySlot,
                    outerRing)
                && SlotCollectionClosed(
                    pawn,
                    combatState,
                    ref secondarySlot,
                    outerRing);
            bool reachedMaximum = outerRing >= search.maximumRing;
            if (bothClosed || reachedMaximum)
            {
                CompleteScan(pawn, combatState, ref primarySlot, ref secondarySlot);
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

            bool randomAttack = RandomAttackEnabled(pawn);
            bool idleProjectilePriority = !randomAttack
                && combatState.idleProjectileSearchTriggerPending;
            bool ordinaryWeaponEnabled = !idleProjectilePriority
                && cycle.weapon != null
                && RimKataEquipmentUtility.IsWeaponEnabled(cycle.weapon.def);
            return TrySelectCandidate(
                pawn,
                combatState,
                cycle,
                verb,
                preferredTarget,
                randomAttack,
                ordinaryWeaponEnabled,
                out target,
                out interception);
        }

        internal static bool TrySelectCandidate(
            Pawn pawn,
            RimKataPawnCombatState combatState,
            RimKataWeaponCycleState cycle,
            Verb verb,
            Thing preferredTarget,
            bool randomAttack,
            bool ordinaryWeaponEnabled,
            out Thing target,
            out bool interception)
        {
            target = null;
            interception = false;
            if (pawn?.Map == null
                || combatState == null
                || cycle == null
                || verb == null)
            {
                return false;
            }

            bool idleProjectilePriority = !randomAttack
                && combatState.idleProjectileSearchTriggerPending;
            ordinaryWeaponEnabled = !idleProjectilePriority
                && cycle.weapon != null
                && ordinaryWeaponEnabled;
            if (!randomAttack && !idleProjectilePriority)
            {
                if (ordinaryWeaponEnabled
                    && preferredTarget != null
                    && !(preferredTarget is Projectile))
                {
                    target = preferredTarget;
                    return true;
                }

                return false;
            }

            EligibleCandidates.Clear();
            if (!idleProjectilePriority && ordinaryWeaponEnabled)
            {
                List<Thing> ordinary = cycle.automaticCandidates;
                for (int i = 0;
                    ordinary != null && i < ordinary.Count;
                    i++)
                {
                    Thing candidate = ordinary[i];
                    if (!(candidate is Projectile))
                    {
                        EligibleCandidates.Add(candidate);
                    }
                }
            }

            bool includeProjectiles = randomAttack
                || idleProjectilePriority;
            if (includeProjectiles
                && RimKataMod.Settings?.explosiveInterceptionEnabled != false)
            {
                RimKataMapComponent mapComponent =
                    pawn.Map.GetComponent<RimKataMapComponent>();
                if (mapComponent?.HasActiveExplosiveProjectiles == true)
                {
                    float projectileRange = ProjectileRangeForCycle(
                        pawn,
                        cycle,
                        verb);
                    mapComponent.AppendValidHostileProjectiles(
                        pawn,
                        verb,
                        projectileRange * projectileRange,
                        EligibleCandidates);
                }
            }

            if (EligibleCandidates.Count == 0)
            {
                return false;
            }

            bool removedCandidate = false;
            while (EligibleCandidates.Count > 0)
            {
                int candidateIndex = randomAttack
                    ? Rand.Range(0, EligibleCandidates.Count)
                    : 0;
                Thing candidate = EligibleCandidates[candidateIndex];
                if (candidate is Projectile)
                {
                    target = candidate;
                    break;
                }

                if (!CanShootRegisteredCandidate(
                    pawn,
                    combatState,
                    cycle,
                    verb,
                    candidate))
                {
                    removedCandidate |= RemoveAutomaticCandidate(
                        combatState,
                        cycle,
                        candidate,
                        !IsLiveRegisteredCandidate(pawn, candidate));
                    EligibleCandidates.RemoveAt(candidateIndex);
                    continue;
                }

                target = candidate;
                break;
            }

            if (removedCandidate)
            {
                NotifyAutomaticCandidateCountChanged(pawn, combatState, true);
            }

            interception = target is Projectile;
            return target != null;
        }

        internal static bool TryAddKnownAutomaticTarget(
            Pawn pawn,
            RimKataPawnCombatState combatState,
            Thing target)
        {
            return TryAddKnownAutomaticTarget(
                pawn,
                combatState,
                target,
                false);
        }

        internal static bool TryAddKnownAutomaticTarget(
            Pawn pawn,
            RimKataPawnCombatState combatState,
            Thing target,
            bool randomAttackVerified)
        {
            if (pawn?.Map == null
                || pawn.InMentalState
                || combatState == null
                || (!randomAttackVerified
                    && !RandomAttackEnabled(pawn))
                || target is Projectile)
            {
                return false;
            }

            bool? newTargetValid = null;
            bool removedCandidate = false;
            bool accepted = TryAddValidatedAutomaticTargetToCycle(
                pawn,
                combatState,
                combatState.primaryWeaponCycle,
                target,
                ref newTargetValid,
                ref removedCandidate);
            accepted = TryAddValidatedAutomaticTargetToCycle(
                pawn,
                combatState,
                combatState.secondaryWeaponCycle,
                target,
                ref newTargetValid,
                ref removedCandidate)
                || accepted;
            if (removedCandidate)
            {
                NotifyAutomaticCandidateCountChanged(pawn, combatState, true);
            }
            return accepted;
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
            if (cycle == null)
            {
                return false;
            }

            if (target is Projectile projectile)
            {
                return IsValidProjectileForCycle(
                    pawn,
                    cycle,
                    verb,
                    projectile);
            }

            if (cycle.automaticCandidates?.Contains(target) == true)
            {
                return CanShootRegisteredCandidate(
                    pawn, state, cycle, verb, target);
            }

            return RimKataTargeting.IsValidAutomaticAttackTarget(pawn, target)
                && IsValidAutomaticTargetForCycle(
                    pawn,
                    state,
                    cycle,
                    verb,
                    target);
        }

        private static void CollectAutomaticTargetsInRing(
            Pawn pawn,
            RimKataPawnCombatState combatState,
            IntVec3 center,
            float innerRadius,
            float outerRadius,
            int outerRing,
            ref RingCandidateSlot primarySlot,
            ref RingCandidateSlot secondarySlot)
        {
            PrimaryRingCandidates.Clear();
            SecondaryRingCandidates.Clear();
            VisitedMultiCellRingTargets.Clear();
            try
            {
                CollectAutomaticTargetsInRingCells(
                    pawn,
                    combatState,
                    center,
                    innerRadius,
                    outerRadius,
                    ref primarySlot,
                    ref secondarySlot);
                CommitStagedRangedCandidates(
                    combatState?.primaryWeaponCycle,
                    PrimaryRingCandidates,
                    center,
                    outerRing);
                CommitStagedRangedCandidates(
                    combatState?.secondaryWeaponCycle,
                    SecondaryRingCandidates,
                    center,
                    outerRing);
            }
            finally
            {
                PrimaryRingCandidates.Clear();
                SecondaryRingCandidates.Clear();
                VisitedMultiCellRingTargets.Clear();
            }
        }

        private static void CollectAutomaticTargetsInRingCells(
            Pawn pawn,
            RimKataPawnCombatState combatState,
            IntVec3 center,
            float innerRadius,
            float outerRadius,
            ref RingCandidateSlot primarySlot,
            ref RingCandidateSlot secondarySlot)
        {
            Map map = pawn.Map;
            bool precomputedOuter = RimKataRingGeometry.TryGetCircleRowOffset(
                outerRadius, out int outerRowOffset);
            bool precomputedInner = RimKataRingGeometry.TryGetCircleRowOffset(
                innerRadius, out int innerRowOffset);
            ushort[] circleRows = RimKataRingGeometry.CircleRowHalfWidths;
            float innerSquared = innerRadius < 0f
                ? -1f
                : innerRadius * innerRadius;
            float outerSquared = outerRadius * outerRadius;
            bool needsSquaredBounds = !precomputedOuter
                || (!precomputedInner && innerRadius >= 0f);
            bool recordSearchCells = Prefs.DevMode
                && RimKataDebugHUD.SearchRangeEnabled
                && RimKataDebugHUD.TryBeginActualSearchCellRecording(
                    pawn,
                    map);

            int radiusExtent = Mathf.FloorToInt(outerRadius);
            int maximumMapXOffset = Mathf.Max(
                Mathf.Abs(center.x),
                Mathf.Abs(map.Size.x - 1 - center.x));
            int maximumMapZOffset = Mathf.Max(
                Mathf.Abs(center.z),
                Mathf.Abs(map.Size.z - 1 - center.z));
            int maximumAbsZ = Mathf.Min(radiusExtent, maximumMapZOffset);
            int outerX = Mathf.Min(radiusExtent, maximumMapXOffset);
            int innerRadiusExtent = Mathf.FloorToInt(innerRadius);
            int innerX = innerRadius < 0f
                ? -1
                : Mathf.Min(
                    innerRadiusExtent,
                    maximumMapXOffset);

            for (int absZ = 0; absZ <= maximumAbsZ; absZ++)
            {
                long zSquared = needsSquaredBounds ? (long)absZ * absZ : 0L;
                if (precomputedOuter)
                {
                    outerX = Mathf.Min(
                        circleRows[outerRowOffset + absZ], maximumMapXOffset);
                }
                else
                {
                    while (outerX >= 0
                        && (float)((long)outerX * outerX + zSquared)
                            > outerSquared)
                    {
                        outerX--;
                    }
                }

                if (outerX < 0)
                {
                    break;
                }

                if (precomputedInner)
                {
                    innerX = absZ <= innerRadiusExtent
                        ? Mathf.Min(
                            circleRows[innerRowOffset + absZ], maximumMapXOffset)
                        : -1;
                }
                else
                {
                    while (innerX >= 0
                        && (float)((long)innerX * innerX + zSquared)
                            > innerSquared)
                    {
                        innerX--;
                    }
                }

                CollectAutomaticTargetsInRingRow(
                    pawn,
                    combatState,
                    map,
                    center,
                    center.z + absZ,
                    outerX,
                    innerX,
                    recordSearchCells,
                    ref primarySlot,
                    ref secondarySlot);
                if (absZ > 0)
                {
                    CollectAutomaticTargetsInRingRow(
                        pawn,
                        combatState,
                        map,
                        center,
                        center.z - absZ,
                        outerX,
                        innerX,
                        recordSearchCells,
                        ref primarySlot,
                        ref secondarySlot);
                }
            }
        }

        private static void CollectAutomaticTargetsInRingRow(
            Pawn pawn,
            RimKataPawnCombatState combatState,
            Map map,
            IntVec3 center,
            int z,
            int outerX,
            int innerX,
            bool recordSearchCells,
            ref RingCandidateSlot primarySlot,
            ref RingCandidateSlot secondarySlot)
        {
            if ((uint)z >= (uint)map.Size.z)
            {
                return;
            }

            int minimumX = Mathf.Max(-outerX, -center.x);
            int maximumX = Mathf.Min(
                outerX,
                map.Size.x - 1 - center.x);
            if (minimumX > maximumX)
            {
                return;
            }

            if (innerX < 0)
            {
                CollectAutomaticTargetsInRingRowSegment(
                    pawn,
                    combatState,
                    map,
                    center.x,
                    z,
                    minimumX,
                    maximumX,
                    recordSearchCells,
                    ref primarySlot,
                    ref secondarySlot);
                return;
            }

            int leftEnd = Mathf.Min(maximumX, -innerX - 1);
            if (minimumX <= leftEnd)
            {
                CollectAutomaticTargetsInRingRowSegment(
                    pawn,
                    combatState,
                    map,
                    center.x,
                    z,
                    minimumX,
                    leftEnd,
                    recordSearchCells,
                    ref primarySlot,
                    ref secondarySlot);
            }
            int rightStart = Mathf.Max(minimumX, innerX + 1);
            if (rightStart <= maximumX)
            {
                CollectAutomaticTargetsInRingRowSegment(
                    pawn,
                    combatState,
                    map,
                    center.x,
                    z,
                    rightStart,
                    maximumX,
                    recordSearchCells,
                    ref primarySlot,
                    ref secondarySlot);
            }
        }

        private static void CollectAutomaticTargetsInRingRowSegment(
            Pawn pawn,
            RimKataPawnCombatState combatState,
            Map map,
            int centerX,
            int z,
            int minimumX,
            int maximumX,
            bool recordSearchCells,
            ref RingCandidateSlot primarySlot,
            ref RingCandidateSlot secondarySlot)
        {
            ThingGrid thingGrid = map.thingGrid;
            int firstCellIndex = map.cellIndices.CellToIndex(centerX + minimumX, z);
            int lastCellIndex = firstCellIndex + maximumX - minimumX;
            for (int cellIndex = firstCellIndex; cellIndex <= lastCellIndex; cellIndex++)
            {
                List<Thing> things = thingGrid.ThingsListAtFast(cellIndex);
                if (things.Count > 0)
                {
                    CollectAutomaticTargetsInCell(
                        pawn,
                        combatState,
                        things,
                        ref primarySlot,
                        ref secondarySlot);
                }
                if (recordSearchCells)
                {
                    IntVec3 cell = new IntVec3(
                        centerX + minimumX + cellIndex - firstCellIndex, 0, z);
                    RimKataDebugHUD.RecordActualSearchCell(map, cell);
                }
            }
        }

        private static void CollectAutomaticTargetsInCell(
            Pawn pawn,
            RimKataPawnCombatState combatState,
            List<Thing> things,
            ref RingCandidateSlot primarySlot,
            ref RingCandidateSlot secondarySlot)
        {
            for (int i = 0; i < things.Count; i++)
            {
                Thing candidate = things[i];
                if (!(candidate is IAttackTarget))
                {
                    continue;
                }

                // ThingGrid registers 1x1 Things only once; only larger footprints repeat.
                IntVec2 size = candidate.def.size;
                if ((size.x != 1 || size.z != 1)
                    && !VisitedMultiCellRingTargets.Add(candidate))
                {
                    continue;
                }

                // Both slots use the current shooter position, not the scan origin.
                int distanceSquared = pawn.Position.DistanceToSquared(candidate.Position);
                bool? newTargetValid = null;
                TryStageOrAddRingCandidate(
                    pawn,
                    combatState,
                    ref primarySlot,
                    candidate,
                    distanceSquared,
                    PrimaryRingCandidates,
                    ref newTargetValid);
                TryStageOrAddRingCandidate(
                    pawn,
                    combatState,
                    ref secondarySlot,
                    candidate,
                    distanceSquared,
                    SecondaryRingCandidates,
                    ref newTargetValid);
            }
        }

        private static void TryStageOrAddRingCandidate(
            Pawn pawn,
            RimKataPawnCombatState combatState,
            ref RingCandidateSlot slot,
            Thing target,
            int distanceSquared,
            List<Thing> stagedCandidates,
            ref bool? newTargetValid)
        {
            RimKataWeaponCycleState cycle = slot.cycle;
            // Selection, active plans and completion maintenance own member eviction.
            if (cycle == null
                || cycle.automaticCandidateCollectionClosed
                || cycle.automaticCandidates?.Contains(target) == true)
            {
                return;
            }

            float candidateCellRadius = slot.ResolveConfiguredRadius(pawn, combatState);
            if (candidateCellRadius <= 0f
                || distanceSquared > slot.configuredRadiusSquared)
            {
                return;
            }

            if (!slot.CanAdmitNew(pawn, combatState)
                || !IsValidNewAutomaticTarget(pawn, target, ref newTargetValid)
                || !CanHitRingCandidate(pawn, combatState, ref slot, target))
            {
                return;
            }

            if (!UsesRangedCandidateLimit(cycle))
            {
                cycle.AddAutomaticCandidate(target);
                return;
            }

            stagedCandidates.Add(target);
        }

        private static bool CanHitRingCandidate(
            Pawn pawn,
            RimKataPawnCombatState combatState,
            ref RingCandidateSlot slot,
            Thing target)
        {
            Verb verb = slot.ResolveVerb(pawn);
            return !verb.IsMeleeAttack && IsCloseCombatContext(combatState)
                ? pawn.CanReachImmediate(target, PathEndMode.Touch)
                : verb.CanHitTarget(target);
        }

        private static void CommitStagedRangedCandidates(
            RimKataWeaponCycleState cycle,
            List<Thing> stagedCandidates,
            IntVec3 center,
            int outerRing)
        {
            if (!UsesRangedCandidateLimit(cycle)
                || cycle.automaticCandidateCollectionClosed
                || stagedCandidates == null
                || stagedCandidates.Count == 0)
            {
                return;
            }

            int remaining = Mathf.Max(
                0,
                EffectiveCandidateLimitForRing(cycle, outerRing)
                    - CountStoredCandidatesThroughRing(
                        cycle,
                        center,
                        outerRing));
            if (remaining <= 0)
            {
                return;
            }

            if (stagedCandidates.Count <= remaining)
            {
                for (int i = 0; i < stagedCandidates.Count; i++)
                {
                    cycle.AddAutomaticCandidate(stagedCandidates[i]);
                }
                return;
            }

            for (int i = 0; i < remaining; i++)
            {
                int swapIndex = Rand.Range(i, stagedCandidates.Count);
                Thing selected = stagedCandidates[swapIndex];
                stagedCandidates[swapIndex] = stagedCandidates[i];
                stagedCandidates[i] = selected;
                cycle.AddAutomaticCandidate(selected);
            }
        }

        private static bool TryAddValidatedAutomaticTargetToCycle(
            Pawn pawn,
            RimKataPawnCombatState combatState,
            RimKataWeaponCycleState cycle,
            Thing target,
            ref bool? newTargetValid,
            ref bool removedCandidate)
        {
            if (cycle == null)
            {
                return false;
            }

            if (cycle.automaticCandidates?.Contains(target) == true)
            {
                Verb registeredVerb = CombatVerbForCycle(pawn, cycle);
                if (CanShootRegisteredCandidate(
                    pawn, combatState, cycle, registeredVerb, target))
                {
                    return true;
                }

                removedCandidate |= RemoveAutomaticCandidate(
                    combatState, cycle, target, false);
                return false;
            }

            if (!IsValidNewAutomaticTarget(pawn, target, ref newTargetValid))
            {
                return false;
            }

            Verb verb = CombatVerbForCycle(pawn, cycle);
            if (IsValidAutomaticTargetForCycle(
                pawn,
                combatState,
                cycle,
                verb,
                target))
            {
                return cycle.AddAutomaticCandidate(target)
                    || cycle.automaticCandidates?.Contains(target) == true;
            }

            return false;
        }

        private static bool IsValidNewAutomaticTarget(
            Pawn pawn,
            Thing target,
            ref bool? newTargetValid)
        {
            if (!newTargetValid.HasValue)
            {
                newTargetValid = RimKataTargeting.IsValidAutomaticAttackTarget(
                    pawn, target);
            }
            return newTargetValid.Value;
        }

        internal static bool IsLiveRegisteredCandidate(Pawn pawn, Thing target)
        {
            return pawn?.Map != null
                && target != null
                && !target.Destroyed
                && target.Spawned
                && target.Map == pawn.Map
                && (!(target is Pawn targetPawn)
                    || (!targetPawn.Dead
                        && !RimKataTargeting.IsIncapacitatedTarget(targetPawn)));
        }

        internal static bool CanShootRegisteredCandidate(
            Pawn pawn,
            RimKataPawnCombatState combatState,
            RimKataWeaponCycleState cycle,
            Verb verb,
            Thing target)
        {
            if (cycle?.weapon == null
                || verb == null
                || !IsLiveRegisteredCandidate(pawn, target))
            {
                return false;
            }

            // Membership carries admission; only current shooting conditions remain.
            float candidateCellRadius = ResolveCandidateCellRadiusForCycle(
                pawn, combatState, cycle, verb);
            if (candidateCellRadius <= 0f
                || pawn.Position.DistanceToSquared(target.Position)
                    > candidateCellRadius * candidateCellRadius)
            {
                return false;
            }

            return !verb.IsMeleeAttack && IsCloseCombatContext(combatState)
                ? pawn.CanReachImmediate(target, PathEndMode.Touch)
                : verb.CanHitTarget(target);
        }

        private static bool IsValidAutomaticTargetForCycle(
            Pawn pawn,
            RimKataPawnCombatState combatState,
            RimKataWeaponCycleState cycle,
            Verb verb,
            Thing target)
        {
            if (target == null)
            {
                return false;
            }

            float candidateCellRadius = CandidateCellRadiusForCycle(
                pawn,
                combatState,
                cycle,
                verb);
            if (candidateCellRadius <= 0f
                || pawn.Position.DistanceToSquared(target.Position)
                    > candidateCellRadius * candidateCellRadius)
            {
                return false;
            }

            if (!verb.IsMeleeAttack && IsCloseCombatContext(combatState))
            {
                return pawn.CanReachImmediate(target, PathEndMode.Touch);
            }

            return verb.CanHitTarget(target);
        }

        private static bool IsValidProjectileForCycle(
            Pawn pawn,
            RimKataWeaponCycleState cycle,
            Verb verb,
            Projectile projectile)
        {
            float range = ProjectileRangeForCycle(pawn, cycle, verb);
            return range > 0f
                && RimKataTargeting.IsValidExplosiveProjectileForVerb(
                    pawn,
                    verb,
                    projectile,
                    range * range);
        }

        internal static bool EvictAutomaticCandidate(
            Pawn pawn,
            RimKataPawnCombatState combatState,
            RimKataWeaponCycleState cycle,
            Thing target,
            bool requestRefill)
        {
            if (target == null || target is Projectile)
            {
                return false;
            }

            bool removed = RemoveAutomaticCandidate(
                combatState,
                cycle,
                target,
                !IsLiveRegisteredCandidate(pawn, target));
            if (removed)
            {
                NotifyAutomaticCandidateCountChanged(
                    pawn, combatState, requestRefill);
            }

            return removed;
        }

        private static void NotifyAutomaticCandidateCountChanged(
            Pawn pawn,
            RimKataPawnCombatState combatState,
            bool requestRefill)
        {
            if (combatState == null)
            {
                return;
            }

            if (combatState.sharedTargetSearch?.scanActive == true)
            {
                // Reallow cap scheduling without restarting the in-progress rings.
                combatState.ResetCandidateSaturationExpansion(false);
                return;
            }

            combatState.ResetCandidateSaturationExpansion(true);
            if (requestRefill
                && pawn?.Map != null
                && RandomAttackEnabled(pawn)
                && !IsCloseCombatContext(combatState))
            {
                Begin(pawn, combatState, pawn.Position);
            }
        }

        private static bool RemoveAutomaticCandidate(
            RimKataPawnCombatState combatState,
            RimKataWeaponCycleState cycle,
            Thing target,
            bool globallyInvalid)
        {
            if (!globallyInvalid)
            {
                return RemoveAutomaticCandidateFromCycle(cycle, target);
            }

            bool removed = RemoveAutomaticCandidateFromCycle(
                combatState?.primaryWeaponCycle,
                target);
            if (RemoveAutomaticCandidateFromCycle(
                combatState?.secondaryWeaponCycle,
                target))
            {
                removed = true;
            }
            return removed;
        }

        private static bool RemoveAutomaticCandidateFromCycle(
            RimKataWeaponCycleState cycle,
            Thing target)
        {
            if (cycle?.RemoveAutomaticCandidate(target) != true)
            {
                return false;
            }

            cycle.automaticCandidateCollectionClosed = false;
            return true;
        }

        private static void UpdateCollectionClosure(
            Pawn pawn,
            RimKataPawnCombatState combatState,
            int outerRing,
            ref RingCandidateSlot primarySlot,
            ref RingCandidateSlot secondarySlot)
        {
            bool primarySaturated = UpdateCycleCollectionClosure(
                pawn,
                combatState,
                ref primarySlot,
                outerRing,
                out bool primaryVacancy);
            bool secondarySaturated = UpdateCycleCollectionClosure(
                pawn,
                combatState,
                ref secondarySlot,
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
                            combatState,
                            ref primarySlot,
                            outerRing);
                    }
                    if (secondarySaturated)
                    {
                        TryScheduleNextCandidateLimit(
                            pawn,
                            combatState,
                            ref secondarySlot,
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
            if (!UsesRangedCandidateLimit(cycle)
                || cycle.automaticCandidateCollectionClosed)
            {
                return;
            }

            int limit = EffectiveCandidateLimitForRing(cycle, outerRing);
            if (CountStoredCandidatesThroughRing(
                    cycle,
                    SearchCenter(pawn, combatState),
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
            IntVec3 origin)
        {
            if (combatState?.candidateSaturationExpansionUsed != true
                || IsCloseCombatContext(combatState))
            {
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
            usable = UsesRangedCandidateLimit(cycle);
            if (!usable)
            {
                return false;
            }

            Verb verb = CombatVerbForCycle(pawn, cycle);
            float candidateCellRadius = CandidateCellRadiusForCycle(
                pawn,
                combatState,
                cycle,
                verb);
            usable = verb != null && candidateCellRadius > 0f;
            if (!usable)
            {
                return false;
            }

            List<Thing> candidates = cycle.automaticCandidates;
            int maximumRing = MaximumLogicalRingFromCellRadius(
                candidateCellRadius);
            for (int ring = 1; ring <= maximumRing; ring++)
            {
                int limit = CandidateLimitForRing(ring);
                if (candidates == null || candidates.Count < limit)
                {
                    continue;
                }

                float outerRadius = Mathf.Min(
                    ring + CandidateCellRadiusPadding,
                    candidateCellRadius);
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

            Verb verb = CombatVerbForCycle(pawn, cycle);
            float candidateCellRadius = CandidateCellRadiusForCycle(
                pawn,
                combatState,
                cycle,
                verb);
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
            cycle.automaticCandidateCollectionClosed =
                candidateCellRadius <= 0f;
        }

        private static bool UpdateCycleCollectionClosure(
            Pawn pawn,
            RimKataPawnCombatState combatState,
            ref RingCandidateSlot slot,
            int outerRing,
            out bool hasCandidateVacancy)
        {
            RimKataWeaponCycleState cycle = slot.cycle;
            hasCandidateVacancy = false;
            if (!UsesRangedCandidateLimit(cycle)
                || cycle.automaticCandidateCollectionClosed)
            {
                return false;
            }

            float candidateCellRadius = slot.NewCandidateRadius(pawn, combatState);
            int limit = EffectiveCandidateLimitForRing(cycle, outerRing);
            int maximumRing = MaximumLogicalRingFromCellRadius(
                candidateCellRadius);
            bool reachedWeaponRange = candidateCellRadius <= 0f
                || outerRing >= maximumRing;
            bool saturated = CountStoredCandidatesThroughRing(
                    cycle,
                    SearchCenter(pawn, combatState),
                    outerRing) >= limit;
            hasCandidateVacancy = !saturated;

            cycle.automaticCandidateCollectionClosed = reachedWeaponRange
                || saturated;
            return saturated;
        }

        private static bool SlotCollectionClosed(
            Pawn pawn,
            RimKataPawnCombatState combatState,
            ref RingCandidateSlot slot,
            int outerRing)
        {
            RimKataWeaponCycleState cycle = slot.cycle;
            if (cycle?.weapon == null)
            {
                return true;
            }

            return cycle.automaticCandidateCollectionClosed
                || outerRing >= MaximumLogicalRingFromCellRadius(
                    slot.NewCandidateRadius(pawn, combatState));
        }

        private static bool TryScheduleNextCandidateLimit(
            Pawn pawn,
            RimKataPawnCombatState combatState,
            ref RingCandidateSlot slot,
            int saturatedRing)
        {
            RimKataWeaponCycleState cycle = slot.cycle;
            if (pawn?.Map == null
                || !UsesRangedCandidateLimit(cycle)
                || cycle.pendingCandidateLimitOverride > 0
                || cycle.activeCandidateLimitOverride > 0)
            {
                return false;
            }

            float candidateCellRadius = slot.NewCandidateRadius(pawn, combatState);
            int maximumRing = MaximumLogicalRingFromCellRadius(
                candidateCellRadius);
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

        private static int CountStoredCandidatesThroughRing(
            RimKataWeaponCycleState cycle,
            IntVec3 center,
            int outerRing)
        {
            List<Thing> candidates = cycle?.automaticCandidates;
            if (candidates == null
                || !center.IsValid
                || outerRing <= 0)
            {
                return 0;
            }

            float outerRadius =
                outerRing + CandidateCellRadiusPadding;
            float outerSquared = outerRadius * outerRadius;
            int count = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                Thing candidate = candidates[i];
                if (candidate is Projectile
                    || candidate == null
                    || center.DistanceToSquared(candidate.Position)
                        > outerSquared)
                {
                    continue;
                }

                count++;
            }
            return count;
        }

        private static IntVec3 SearchCenter(
            Pawn pawn,
            RimKataPawnCombatState combatState)
        {
            return combatState?.sharedTargetSearch?.origin.IsValid == true
                ? combatState.sharedTargetSearch.origin
                : pawn?.Position ?? IntVec3.Invalid;
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

        private static bool UsesRangedCandidateLimit(
            RimKataWeaponCycleState cycle)
        {
            return cycle?.weapon?.def?.IsRangedWeapon == true;
        }

        private static int MaximumLogicalRingFromCellRadius(
            float candidateCellRadius)
        {
            return Mathf.Max(
                1,
                Mathf.CeilToInt(
                    Mathf.Max(
                        0f,
                        candidateCellRadius - CandidateCellRadiusPadding)));
        }

        private static float MaximumCandidateCellRadius(
            Pawn pawn,
            RimKataPawnCombatState combatState)
        {
            RimKataWeaponCycleState primary =
                combatState?.primaryWeaponCycle;
            RimKataWeaponCycleState secondary =
                combatState?.secondaryWeaponCycle;
            return Mathf.Max(
                CandidateCellRadiusForCycle(
                    pawn,
                    combatState,
                    primary,
                    CombatVerbForCycle(pawn, primary)),
                CandidateCellRadiusForCycle(
                    pawn,
                    combatState,
                    secondary,
                    CombatVerbForCycle(pawn, secondary)));
        }

        private static float CandidateCellRadiusForCycle(
            Pawn pawn,
            RimKataPawnCombatState combatState,
            RimKataWeaponCycleState cycle,
            Verb verb)
        {
            if (!IsSlotAvailableForNewCandidates(pawn, combatState, cycle, verb))
            {
                return 0f;
            }

            return ResolveCandidateCellRadiusForCycle(
                pawn, combatState, cycle, verb);
        }

        private static bool IsSlotAvailableForNewCandidates(
            Pawn pawn,
            RimKataPawnCombatState combatState,
            RimKataWeaponCycleState cycle,
            Verb verb)
        {
            bool closeCombatContext = IsCloseCombatContext(combatState);
            return pawn?.Map != null
                && cycle?.weapon != null
                && RimKataEquipmentUtility.IsWeaponEnabled(cycle.weapon.def)
                && verb != null
                && RimKataDualWeaponController.VerbUsable(
                    pawn,
                    verb,
                    closeCombatContext);
        }

        private static float ResolveCandidateCellRadiusForCycle(
            Pawn pawn,
            RimKataPawnCombatState combatState,
            RimKataWeaponCycleState cycle,
            Verb verb)
        {
            bool closeCombatContext = IsCloseCombatContext(combatState);
            if (closeCombatContext)
            {
                return UsesRangedCandidateLimit(cycle)
                    ? CloseCombatRangedCandidateCellRadius
                    : RimKataRangeUtility.ResolveEffectiveRange(
                        pawn,
                        cycle.weapon,
                        verb);
            }

            return AutomaticCandidateCellRadiusForCycle(pawn, cycle, verb);
        }

        private static float AutomaticCandidateCellRadiusForCycle(
            Pawn pawn,
            RimKataWeaponCycleState cycle,
            Verb verb)
        {
            if (pawn?.Map == null || cycle?.weapon == null || verb == null)
            {
                return 0f;
            }

            if (!verb.IsMeleeAttack)
            {
                return Mathf.Max(
                    0f,
                    RimKataRangeUtility.ResolveCandidateCellRadius(
                        pawn,
                        cycle.weapon,
                        verb));
            }

            return RimKataRangeUtility.ResolveEffectiveRange(
                pawn,
                cycle.weapon,
                verb);
        }

        private static float ProjectileRangeForCycle(
            Pawn pawn,
            RimKataWeaponCycleState cycle,
            Verb verb)
        {
            if (pawn?.Map == null
                || cycle?.weapon == null
                || verb == null
                || verb.IsMeleeAttack
                || !RimKataDualWeaponController.VerbUsable(
                    pawn,
                    verb,
                    false))
            {
                return 0f;
            }

            return RimKataRangeUtility.ResolveEffectiveRange(
                pawn,
                cycle.weapon,
                verb);
        }

        private static bool IsCloseCombatContext(
            RimKataPawnCombatState combatState)
        {
            return combatState?.dualCloseCombatActive == true;
        }

        private static Verb CombatVerbForCycle(
            Pawn pawn,
            RimKataWeaponCycleState cycle)
        {
            return RimKataWeaponSlotUtility.CombatVerb(
                pawn,
                cycle?.weapon);
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

        private static bool RandomAttackEnabled(Pawn pawn)
        {
            return RimKataEligibility.RandomAttackEnabledForPawn(pawn);
        }

        private static void StopOrdinaryTargetSearch(
            RimKataPawnCombatState combatState)
        {
            ClearOrdinaryCandidateList(
                combatState?.primaryWeaponCycle);
            ClearOrdinaryCandidateList(
                combatState?.secondaryWeaponCycle);
            combatState?.sharedTargetSearch?.Reset();
        }

        private static void ClearOrdinaryCandidateList(
            RimKataWeaponCycleState cycle)
        {
            cycle?.ClearStoredAutomaticCandidates();
            if (cycle == null)
            {
                return;
            }

            cycle.automaticCandidateCollectionClosed = false;
            cycle.pendingCandidateLimitOverride = 0;
            cycle.activeCandidateLimitOverride = 0;
        }

        private static void CompleteScan(
            Pawn pawn,
            RimKataPawnCombatState combatState,
            ref RingCandidateSlot primarySlot,
            ref RingCandidateSlot secondarySlot)
        {
            bool removedCandidate = CheckNextStoredCandidate(
                pawn, combatState, ref primarySlot);
            removedCandidate |= CheckNextStoredCandidate(
                pawn, combatState, ref secondarySlot);
            if (removedCandidate)
            {
                // Notify while the scan is still active: preserve the next-band
                // reservation and do not turn maintenance into another search.
                NotifyAutomaticCandidateCountChanged(pawn, combatState, false);
            }
            Finish(combatState);
        }

        private static bool CheckNextStoredCandidate(
            Pawn pawn,
            RimKataPawnCombatState combatState,
            ref RingCandidateSlot slot)
        {
            RimKataWeaponCycleState cycle = slot.cycle;
            if (cycle == null
                || !cycle.TryGetNextAutomaticCandidateForValidation(out Thing target))
            {
                return false;
            }

            if (CanShootRegisteredCandidate(
                pawn, combatState, cycle, slot.ResolveVerb(pawn), target))
            {
                return false;
            }

            return RemoveAutomaticCandidate(
                combatState,
                cycle,
                target,
                !IsLiveRegisteredCandidate(pawn, target));
        }

        private static void Finish(RimKataPawnCombatState combatState)
        {
            RimKataSharedTargetSearchState search =
                combatState?.sharedTargetSearch;
            if (search == null)
            {
                return;
            }

            search.scanActive = false;
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
