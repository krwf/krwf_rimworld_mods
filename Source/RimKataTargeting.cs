using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace KRWF.RimKata
{
    public struct RimKataAttackCandidate
    {
        public Thing thing;
        public Projectile explosiveProjectile;

        public bool IsInterception => explosiveProjectile != null;
        public LocalTargetInfo Target => IsInterception
            ? new LocalTargetInfo(explosiveProjectile.ExactPosition.ToIntVec3())
            : new LocalTargetInfo(thing);
    }

    public static class RimKataTargeting
    {
        private static readonly FieldInfo LandedField = AccessTools.Field(typeof(Projectile), "landed");
        private static readonly List<RimKataAttackCandidate> Candidates = new List<RimKataAttackCandidate>();
        private static readonly HashSet<Thing> CandidateThings = new HashSet<Thing>();
        private static readonly List<Thing> AdjacentTargets = new List<Thing>();
        private static readonly List<Thing> NearbyThings = new List<Thing>();
        private static readonly List<Projectile> InterceptionCandidates = new List<Projectile>();
        private static readonly HashSet<Thing> NearbyThingSet = new HashSet<Thing>();
        private static Pawn nearbyCachePawn;
        private static Map nearbyCacheMap;
        private static IntVec3 nearbyCacheCenter = IntVec3.Invalid;
        private static int nearbyCacheTick = -1;
        private static float nearbyCacheRange = -1f;

        public static bool IsAutomaticEnemy(Pawn pawn, Thing target)
        {
            if (pawn?.Map == null
                || target == null
                || target == pawn
                || target.Destroyed
                || !target.Spawned
                || target.Map != pawn.Map
                || !target.HostileTo(pawn))
            {
                return false;
            }

            Faction pawnFaction = pawn.Faction;
            Faction targetFaction = target.Faction;
            return pawnFaction == null
                || targetFaction == null
                || pawnFaction.HostileTo(targetFaction);
        }

        public static bool TryChooseAdvancingCandidate(Pawn pawn, Verb verb, Thing assignedTarget, bool playerForced, out RimKataAttackCandidate candidate)
        {
            candidate = default(RimKataAttackCandidate);
            if (pawn?.Map == null || verb == null)
            {
                return false;
            }

            Candidates.Clear();
            CandidateThings.Clear();
            float range = RimKataRangeUtility.ResolveCandidateRange(verb);
            float rangeSquared = range * range;
            EnsureNearbyThings(pawn, range);
            for (int i = 0; i < NearbyThings.Count; i++)
            {
                Thing target = NearbyThings[i];
                if (!(target is IAttackTarget) || !IsValidAttackTarget(pawn, target) || pawn.Position.DistanceToSquared(target.Position) > rangeSquared || !verb.CanHitTarget(target) || !CandidateThings.Add(target))
                {
                    continue;
                }

                Candidates.Add(new RimKataAttackCandidate { thing = target });
            }

            if (assignedTarget != null
                && assignedTarget.Spawned
                && !assignedTarget.Destroyed
                && (playerForced || IsAutomaticEnemy(pawn, assignedTarget))
                && pawn.Position.DistanceToSquared(assignedTarget.Position) <= rangeSquared
                && verb.CanHitTarget(assignedTarget)
                && CandidateThings.Add(assignedTarget))
            {
                Candidates.Add(new RimKataAttackCandidate { thing = assignedTarget });
            }

            if (verb is Verb_LaunchProjectile)
            {
                for (int i = 0; i < NearbyThings.Count; i++)
                {
                    Projectile projectile = NearbyThings[i] as Projectile;
                    if (!IsValidExplosiveProjectile(pawn, verb, projectile, rangeSquared)
                        || !CandidateThings.Add(projectile))
                    {
                        continue;
                    }

                    Candidates.Add(new RimKataAttackCandidate { explosiveProjectile = projectile });
                }
            }

            if (Candidates.Count == 0)
            {
                return false;
            }

            candidate = SelectCandidate(pawn, assignedTarget);
            Candidates.Clear();
            return true;
        }

        public static bool TryChooseAutomaticFollowupTarget(
            Pawn pawn,
            Verb verb,
            Thing currentAttackTarget,
            out Thing target)
        {
            target = null;
            if (pawn?.Map == null || verb == null)
            {
                return false;
            }

            Candidates.Clear();
            CandidateThings.Clear();
            float range = RimKataRangeUtility.ResolveCandidateRange(verb);
            float rangeSquared = range * range;
            if (RimKataMod.Settings?.randomAttackEnabled == false
                && currentAttackTarget is IAttackTarget
                && !(currentAttackTarget is Projectile)
                && IsValidAttackTarget(pawn, currentAttackTarget)
                && pawn.Position.DistanceToSquared(currentAttackTarget.Position)
                    <= rangeSquared
                && verb.CanHitTarget(currentAttackTarget))
            {
                target = currentAttackTarget;
                return true;
            }

            EnsureNearbyThings(pawn, range);
            for (int i = 0; i < NearbyThings.Count; i++)
            {
                Thing candidateTarget = NearbyThings[i];
                if (candidateTarget is Projectile
                    || !(candidateTarget is IAttackTarget)
                    || !IsValidAttackTarget(pawn, candidateTarget)
                    || pawn.Position.DistanceToSquared(candidateTarget.Position) > rangeSquared
                    || !verb.CanHitTarget(candidateTarget)
                    || !CandidateThings.Add(candidateTarget))
                {
                    continue;
                }

                Candidates.Add(new RimKataAttackCandidate { thing = candidateTarget });
            }

            if (Candidates.Count == 0)
            {
                return false;
            }

            RimKataAttackCandidate selected = SelectCandidate(pawn, null);
            target = selected.thing;
            Candidates.Clear();
            return target != null;
        }

        public static int MaximumCandidateSearchRadius(Pawn pawn, Verb verb)
        {
            if (pawn?.Map == null || verb == null)
            {
                return 0;
            }

            float configuredRange = Mathf.Max(0f, RimKataRangeUtility.ResolveCandidateRange(verb));
            IntVec3 position = pawn.Position;
            Map map = pawn.Map;
            float farthestCornerSquared = Mathf.Max(
                position.DistanceToSquared(new IntVec3(0, 0, 0)),
                position.DistanceToSquared(new IntVec3(map.Size.x - 1, 0, 0)),
                position.DistanceToSquared(new IntVec3(0, 0, map.Size.z - 1)),
                position.DistanceToSquared(new IntVec3(map.Size.x - 1, 0, map.Size.z - 1)));
            float mapRadius = Mathf.Sqrt(farthestCornerSquared);
            return Mathf.CeilToInt(Mathf.Min(configuredRange, mapRadius));
        }

        public static float MaximumAutomaticSearchRange(Pawn pawn)
        {
            if (pawn?.Map == null)
            {
                return 0f;
            }

            ThingWithComps primaryWeapon = RimKataWeaponSlotUtility.PrimaryWeapon(pawn);
            Verb primaryVerb = RimKataWeaponSlotUtility.CombatVerb(pawn, primaryWeapon);

            ThingWithComps secondaryWeapon =
                RimKataWeaponSlotUtility.CanUseSecondarySlot(pawn)
                    ? RimKataWeaponSlotUtility.SecondaryWeapon(pawn)
                    : null;

            Verb secondaryVerb = RimKataWeaponSlotUtility.CombatVerb(pawn,secondaryWeapon);

            float primaryRange =
                primaryVerb != null
                && !primaryVerb.IsMeleeAttack
                    ? Mathf.Max(
                        0f,
                        RimKataRangeUtility.ResolveCandidateRange(
                            primaryVerb))
                    : 0f;

            float secondaryRange =
                secondaryVerb != null
                && !secondaryVerb.IsMeleeAttack
                    ? Mathf.Max(
                        0f,
                        RimKataRangeUtility.ResolveCandidateRange(
                            secondaryVerb))
                    : 0f;

            return Mathf.Max(primaryRange, secondaryRange);
        }

        public static bool TryChooseAdvancingCandidateInRing(
            Pawn pawn,
            Verb verb,
            Thing assignedTarget,
            bool playerForced,
            float innerRadius,
            float outerRadius,
            out RimKataAttackCandidate candidate)
        {
            candidate = default(RimKataAttackCandidate);
            if (pawn?.Map == null || verb == null || outerRadius < 0f)
            {
                return false;
            }

            float maximumRange = RimKataRangeUtility.ResolveCandidateRange(verb);
            outerRadius = Mathf.Min(Mathf.Max(0f, outerRadius), maximumRange);
            innerRadius = Mathf.Min(innerRadius, outerRadius);
            float innerSquared = innerRadius < 0f ? -1f : innerRadius * innerRadius;
            float outerSquared = outerRadius * outerRadius;
            Candidates.Clear();
            CandidateThings.Clear();

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

                    if (distanceSquared <= innerSquared || distanceSquared > outerSquared)
                    {
                        continue;
                    }

                    IntVec3 cell = pawn.Position + offset;
                    if (cell.InBounds(pawn.Map))
                    {
                        CollectRingCandidates(pawn, verb, cell, outerSquared);
                    }
                }
            }
            else
            {
                int extent = Mathf.CeilToInt(outerRadius);
                int minX = Mathf.Max(0, pawn.Position.x - extent);
                int maxX = Mathf.Min(pawn.Map.Size.x - 1, pawn.Position.x + extent);
                int minZ = Mathf.Max(0, pawn.Position.z - extent);
                int maxZ = Mathf.Min(pawn.Map.Size.z - 1, pawn.Position.z + extent);
                for (int z = minZ; z <= maxZ; z++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        IntVec3 cell = new IntVec3(x, 0, z);
                        float distanceSquared = pawn.Position.DistanceToSquared(cell);
                        if (distanceSquared > innerSquared && distanceSquared <= outerSquared)
                        {
                            CollectRingCandidates(pawn, verb, cell, outerSquared);
                        }
                    }
                }
            }

            if (assignedTarget != null
                && assignedTarget.Spawned
                && !assignedTarget.Destroyed
                && (playerForced || IsAutomaticEnemy(pawn, assignedTarget)))
            {
                float assignedDistanceSquared = pawn.Position.DistanceToSquared(assignedTarget.Position);
                if (assignedDistanceSquared > innerSquared
                    && assignedDistanceSquared <= outerSquared
                    && verb.CanHitTarget(assignedTarget)
                    && CandidateThings.Add(assignedTarget))
                {
                    Candidates.Add(new RimKataAttackCandidate { thing = assignedTarget });
                }
            }

            if (Candidates.Count == 0)
            {
                return false;
            }

            candidate = SelectCandidate(pawn, assignedTarget);
            Candidates.Clear();
            return true;
        }

        public static bool TryChooseAutomaticFollowupTargetInRing(
            Pawn pawn,
            Verb verb,
            Thing currentAttackTarget,
            float innerRadius,
            float outerRadius,
            out Thing target)
        {
            return TryChooseAutomaticFollowupTargetInRing(
                pawn,
                verb,
                currentAttackTarget,
                pawn?.Position ?? IntVec3.Invalid,
                innerRadius,
                outerRadius,
                out target);
        }

        public static bool TryChooseAutomaticFollowupTargetInRing(
            Pawn pawn,
            Verb verb,
            Thing currentAttackTarget,
            IntVec3 center,
            float innerRadius,
            float outerRadius,
            out Thing target)
        {
            target = null;
            if (pawn?.Map == null
                || verb == null
                || verb.IsMeleeAttack
                || !center.IsValid
                || outerRadius < 0f)
            {
                return false;
            }

            float maximumRange = RimKataRangeUtility.ResolveCandidateRange(verb);
            outerRadius = Mathf.Min(Mathf.Max(0f, outerRadius), maximumRange);
            innerRadius = Mathf.Min(innerRadius, outerRadius);
            float innerSquared = innerRadius < 0f ? -1f : innerRadius * innerRadius;
            float outerSquared = outerRadius * outerRadius;
            Candidates.Clear();
            CandidateThings.Clear();

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
                    if (distanceSquared <= innerSquared || distanceSquared > outerSquared)
                    {
                        continue;
                    }

                    IntVec3 cell = center + offset;
                    if (cell.InBounds(pawn.Map))
                    {
                        CollectAutomaticFollowupTargetsInCell(
                            pawn,
                            verb,
                            currentAttackTarget,
                            cell);
                    }
                }
            }
            else
            {
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
                        if (distanceSquared > innerSquared && distanceSquared <= outerSquared)
                        {
                            CollectAutomaticFollowupTargetsInCell(
                                pawn,
                                verb,
                                currentAttackTarget,
                                cell);
                        }
                    }
                }
            }

            if (Candidates.Count == 0)
            {
                return false;
            }

            target = SelectCandidate(pawn, null).thing;
            Candidates.Clear();
            return target != null;
        }

        private static void CollectAutomaticFollowupTargetsInCell(
            Pawn pawn,
            Verb verb,
            Thing currentAttackTarget,
            IntVec3 cell)
        {
            List<Thing> things = pawn.Map.thingGrid.ThingsListAtFast(cell);
            for (int i = 0; i < things.Count; i++)
            {
                Thing candidate = things[i];
                if (candidate is Projectile
                    || !(candidate is IAttackTarget)
                    || !IsValidAttackTarget(pawn, candidate)
                    || !verb.CanHitTarget(candidate)
                    || !CandidateThings.Add(candidate))
                {
                    continue;
                }

                Candidates.Add(new RimKataAttackCandidate { thing = candidate });
            }
        }

        public static bool TryChooseExplosiveProjectileInRing(
            Pawn pawn,
            Verb verb,
            float innerRadius,
            float outerRadius,
            out Projectile projectile)
        {
            return TryChooseExplosiveProjectileInRing(
                pawn,
                verb,
                pawn?.Position ?? IntVec3.Invalid,
                innerRadius,
                outerRadius,
                out projectile);
        }

        public static bool TryChooseExplosiveProjectileInRing(
            Pawn pawn,
            Verb verb,
            IntVec3 center,
            float innerRadius,
            float outerRadius,
            out Projectile projectile)
        {
            projectile = null;
            if (pawn?.Map == null
                || !(verb is Verb_LaunchProjectile)
                || !center.IsValid
                || outerRadius <= 0f)
            {
                return false;
            }

            float maximumRange = RimKataRangeUtility.ResolveCandidateRange(verb);
            outerRadius = Mathf.Min(outerRadius, maximumRange);
            innerRadius = Mathf.Min(innerRadius, outerRadius);
            float innerSquared = innerRadius <= 0f ? -1f : innerRadius * innerRadius;
            float outerSquared = outerRadius * outerRadius;
            InterceptionCandidates.Clear();

            if (outerRadius <= GenRadial.MaxRadialPatternRadius)
            {
                int cellCount = GenRadial.NumCellsInRadius(outerRadius);
                for (int i = 0; i < cellCount; i++)
                {
                    IntVec3 offset = GenRadial.RadialPattern[i];
                    float distanceSquared = offset.LengthHorizontalSquared;
                    if (distanceSquared <= innerSquared || distanceSquared > outerSquared)
                    {
                        continue;
                    }

                    IntVec3 cell = center + offset;
                    if (cell.InBounds(pawn.Map))
                    {
                        CollectRingInterceptions(pawn, verb, cell, outerSquared);
                    }
                }
            }
            else
            {
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
                        if (distanceSquared > innerSquared && distanceSquared <= outerSquared)
                        {
                            CollectRingInterceptions(pawn, verb, cell, outerSquared);
                        }
                    }
                }
            }

            if (InterceptionCandidates.Count == 0)
            {
                return false;
            }

            projectile = InterceptionCandidates.RandomElement();
            InterceptionCandidates.Clear();
            return true;
        }

        private static void CollectRingInterceptions(
            Pawn pawn,
            Verb verb,
            IntVec3 cell,
            float outerRangeSquared)
        {
            List<Thing> things = pawn.Map.thingGrid.ThingsListAtFast(cell);
            for (int i = 0; i < things.Count; i++)
            {
                Projectile candidate = things[i] as Projectile;
                if (IsValidExplosiveProjectile(pawn, verb, candidate, outerRangeSquared))
                {
                    InterceptionCandidates.Add(candidate);
                }
            }
        }

        private static void CollectRingCandidates(
            Pawn pawn,
            Verb verb,
            IntVec3 cell,
            float outerRangeSquared)
        {
            List<Thing> things = pawn.Map.thingGrid.ThingsListAtFast(cell);
            for (int i = 0; i < things.Count; i++)
            {
                Thing thing = things[i];

                bool validAttackTarget = thing is IAttackTarget && IsValidAttackTarget(pawn, thing);

                bool closeRangedTarget = validAttackTarget && !verb.IsMeleeAttack && pawn.CanReachImmediate(thing, PathEndMode.Touch) && RimKataEligibility.IsRangedVerbAvailableInCloseCombat(pawn, verb);

                bool canHit = verb.CanHitTarget(thing) || closeRangedTarget;

                if (validAttackTarget && canHit && CandidateThings.Add(thing))
                {
                    Candidates.Add(new RimKataAttackCandidate { thing = thing });
                }

                if (verb is Verb_LaunchProjectile
                    && thing is Projectile projectile
                    && IsValidExplosiveProjectile(pawn, verb, projectile, outerRangeSquared)
                    && CandidateThings.Add(projectile))
                {
                    Candidates.Add(new RimKataAttackCandidate { explosiveProjectile = projectile });
                }
            }
        }

        public static bool TryChooseCloseCandidate(
            Pawn pawn,
            Verb verb,
            Thing assignedTarget,
            bool playerForced,
            bool killIncappedTarget,
            out RimKataAttackCandidate candidate)
        {
            candidate = default(RimKataAttackCandidate);
            if (pawn?.Map == null || verb == null)
            {
                return false;
            }

            CollectAdjacentTargets(pawn, assignedTarget, playerForced, killIncappedTarget);
            if (AdjacentTargets.Count == 0)
            {
                return false;
            }

            Candidates.Clear();
            CandidateThings.Clear();
            for (int i = 0; i < AdjacentTargets.Count; i++)
            {
                Thing target = AdjacentTargets[i];
                if (CandidateThings.Add(target))
                {
                    Candidates.Add(new RimKataAttackCandidate { thing = target });
                }
            }

            if (verb is Verb_LaunchProjectile)
            {
                float range = RimKataRangeUtility.ResolveCandidateRange(verb);
                float rangeSquared = range * range;
                EnsureNearbyThings(pawn, range);
                for (int i = 0; i < NearbyThings.Count; i++)
                {
                    Projectile projectile = NearbyThings[i] as Projectile;
                    if (IsValidExplosiveProjectile(pawn, verb, projectile, rangeSquared)
                        && CandidateThings.Add(projectile))
                    {
                        Candidates.Add(new RimKataAttackCandidate { explosiveProjectile = projectile });
                    }
                }
            }

            candidate = SelectCandidate(pawn, assignedTarget);
            Candidates.Clear();
            AdjacentTargets.Clear();
            return true;
        }

        private static void EnsureNearbyThings(Pawn pawn, float requestedRange)
        {
            Map map = pawn?.Map;
            if (map == null)
            {
                NearbyThings.Clear();
                NearbyThingSet.Clear();
                return;
            }

            float range = Mathf.Max(0f, requestedRange);
            int tick = Find.TickManager.TicksGame;
            bool sameOrigin = nearbyCachePawn == pawn
                && nearbyCacheMap == map
                && nearbyCacheCenter == pawn.Position
                && nearbyCacheTick == tick;
            if (sameOrigin && nearbyCacheRange + 0.001f >= range)
            {
                return;
            }

            if (sameOrigin
                && nearbyCacheRange >= 0f
                && nearbyCacheRange <= GenRadial.MaxRadialPatternRadius
                && range <= GenRadial.MaxRadialPatternRadius)
            {
                int oldCellCount = GenRadial.NumCellsInRadius(nearbyCacheRange);
                int newCellCount = GenRadial.NumCellsInRadius(range);
                nearbyCacheRange = range;
                for (int i = oldCellCount; i < newCellCount; i++)
                {
                    IntVec3 cell = pawn.Position + GenRadial.RadialPattern[i];
                    if (cell.InBounds(map))
                    {
                        CollectNearbyCell(map, cell);
                    }
                }

                return;
            }

            nearbyCachePawn = pawn;
            nearbyCacheMap = map;
            nearbyCacheCenter = pawn.Position;
            nearbyCacheTick = tick;
            nearbyCacheRange = range;
            NearbyThings.Clear();
            NearbyThingSet.Clear();

            float rangeSquared = range * range;
            if (range <= GenRadial.MaxRadialPatternRadius)
            {
                int cellCount = GenRadial.NumCellsInRadius(range);
                for (int i = 0; i < cellCount; i++)
                {
                    IntVec3 cell = pawn.Position + GenRadial.RadialPattern[i];
                    if (cell.InBounds(map))
                    {
                        CollectNearbyCell(map, cell);
                    }
                }

                return;
            }

            int extent = Mathf.CeilToInt(range);
            int minX = Mathf.Max(0, pawn.Position.x - extent);
            int maxX = Mathf.Min(map.Size.x - 1, pawn.Position.x + extent);
            int minZ = Mathf.Max(0, pawn.Position.z - extent);
            int maxZ = Mathf.Min(map.Size.z - 1, pawn.Position.z + extent);
            for (int z = minZ; z <= maxZ; z++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    IntVec3 cell = new IntVec3(x, 0, z);
                    if (pawn.Position.DistanceToSquared(cell) <= rangeSquared)
                    {
                        CollectNearbyCell(map, cell);
                    }
                }
            }
        }

        private static void CollectNearbyCell(Map map, IntVec3 cell)
        {
            List<Thing> things = map.thingGrid.ThingsListAtFast(cell);
            for (int i = 0; i < things.Count; i++)
            {
                Thing thing = things[i];
                if ((thing is IAttackTarget || thing is Projectile)
                    && NearbyThingSet.Add(thing))
                {
                    NearbyThings.Add(thing);
                }
            }
        }

        public static bool TryChooseAdjacentTarget(
            Pawn pawn,
            Thing assignedTarget,
            bool playerForced,
            bool killIncappedTarget,
            out Thing target)
        {
            target = null;
            if (pawn?.Map == null)
            {
                return false;
            }

            CollectAdjacentTargets(pawn, assignedTarget, playerForced, killIncappedTarget);
            if (AdjacentTargets.Count == 0)
            {
                return false;
            }

            target = SelectAdjacentTarget(assignedTarget);
            AdjacentTargets.Clear();
            return true;
        }

        public static bool HasAdjacentCombatTarget(
            Pawn pawn,
            Thing assignedTarget,
            bool playerForced,
            bool killIncappedTarget)
        {
            if (pawn?.Map == null)
            {
                return false;
            }

            CollectAdjacentTargets(pawn, assignedTarget, playerForced, killIncappedTarget);
            bool found = AdjacentTargets.Count > 0;
            AdjacentTargets.Clear();
            return found;
        }

        private static RimKataAttackCandidate SelectCandidate(Pawn pawn, Thing assignedTarget)
        {
            if (RimKataMod.Settings?.randomAttackEnabled != false)
            {
                return Candidates.RandomElement();
            }

            if (assignedTarget != null)
            {
                for (int i = 0; i < Candidates.Count; i++)
                {
                    if (Candidates[i].thing == assignedTarget)
                    {
                        return Candidates[i];
                    }
                }
            }

            int bestIndex = 0;
            int bestDistance = int.MaxValue;
            int bestThingId = int.MaxValue;
            for (int i = 0; i < Candidates.Count; i++)
            {
                Thing thing = Candidates[i].IsInterception
                    ? Candidates[i].explosiveProjectile
                    : Candidates[i].thing;
                if (thing == null)
                {
                    continue;
                }

                int distance = pawn.Position.DistanceToSquared(thing.Position);
                if (distance < bestDistance
                    || (distance == bestDistance && thing.thingIDNumber < bestThingId))
                {
                    bestIndex = i;
                    bestDistance = distance;
                    bestThingId = thing.thingIDNumber;
                }
            }

            return Candidates[bestIndex];
        }

        private static Thing SelectAdjacentTarget(Thing assignedTarget)
        {
            if (RimKataMod.Settings?.randomAttackEnabled != false)
            {
                return AdjacentTargets.RandomElement();
            }

            if (assignedTarget != null && AdjacentTargets.Contains(assignedTarget))
            {
                return assignedTarget;
            }

            Thing selected = AdjacentTargets[0];
            for (int i = 1; i < AdjacentTargets.Count; i++)
            {
                if (AdjacentTargets[i].thingIDNumber < selected.thingIDNumber)
                {
                    selected = AdjacentTargets[i];
                }
            }

            return selected;
        }

        private static void CollectAdjacentTargets(
            Pawn pawn,
            Thing assignedTarget,
            bool playerForced,
            bool killIncappedTarget)
        {
            AdjacentTargets.Clear();
            for (int i = 0; i < GenAdj.AdjacentCells.Length; i++)
            {
                IntVec3 cell = pawn.Position + GenAdj.AdjacentCells[i];
                if (!cell.InBounds(pawn.Map))
                {
                    continue;
                }

                List<Thing> things = cell.GetThingList(pawn.Map);
                for (int j = 0; j < things.Count; j++)
                {
                    Thing thing = things[j];
                    if (thing == pawn || thing.Destroyed || !thing.Spawned)
                    {
                        continue;
                    }

                    if (!pawn.CanReachImmediate(thing, PathEndMode.Touch))
                    {
                        continue;
                    }

                    bool assignedForcedTarget = playerForced && thing == assignedTarget;
                    if (!assignedForcedTarget && !(thing is Pawn))
                    {
                        continue;
                    }

                    if (!assignedForcedTarget && !IsAutomaticEnemy(pawn, thing))
                    {
                        continue;
                    }

                    bool allowedDownedAssignedTarget = assignedForcedTarget && killIncappedTarget && thing == assignedTarget;
                    if (thing is Pawn targetPawn
                        && (targetPawn.Dead
                            || targetPawn.Crawling
                            || (targetPawn.Downed && !allowedDownedAssignedTarget)
                            || targetPawn.IsPsychologicallyInvisible()))
                    {
                        continue;
                    }

                    if (!AdjacentTargets.Contains(thing))
                    {
                        AdjacentTargets.Add(thing);
                    }
                }
            }
        }

        private static bool IsValidAttackTarget(Pawn shooter, Thing target)
        {
            if (target == null
                || target == shooter
                || target.Destroyed
                || !target.Spawned
                || !IsAutomaticEnemy(shooter, target))
            {
                return false;
            }

            return !(target is Pawn targetPawn) || (!targetPawn.Dead && !targetPawn.Downed && !targetPawn.Crawling && !targetPawn.IsPsychologicallyInvisible());
        }

        internal static bool IsValidAutomaticAttackTarget(
            Pawn shooter,
            Thing target)
        {
            return IsValidAttackTarget(shooter, target);
        }

        private static bool IsValidExplosiveProjectile(Pawn pawn, Verb verb, Projectile projectile, float rangeSquared)
        {
            if (RimKataMod.Settings?.explosiveInterceptionEnabled == false)
            {
                return false;
            }
            if (projectile == null
                || !projectile.Spawned
                || projectile.Destroyed
                || projectile.Map != pawn?.Map
                || projectile.def.projectile?.explosionRadius <= 0f
                || (bool)LandedField.GetValue(projectile)
                || projectile.Launcher == null
                || !IsEnemyProjectileLauncher(pawn, projectile))
            {
                return false;
            }

            IntVec3 cell = projectile.ExactPosition.ToIntVec3();
            if (!cell.InBounds(pawn.Map)
                || pawn.Position.DistanceToSquared(cell) > rangeSquared
                || !verb.TryFindShootLineFromTo(pawn.Position, cell, out ShootLine line))
            {
                return false;
            }

            foreach (IntVec3 point in line.Points())
            {
                if (point.AnyGas(pawn.Map, GasType.BlindSmoke))
                {
                    return false;
                }
            }

            return true;
        }

        internal static bool IsValidExplosiveProjectileForVerb(
            Pawn pawn,
            Verb verb,
            Projectile projectile,
            float rangeSquared)
        {
            return IsValidExplosiveProjectile(
                pawn,
                verb,
                projectile,
                rangeSquared);
        }

        internal static bool IsEnemyProjectileLauncher(
            Pawn pawn,
            Projectile projectile)
        {
            Thing launcher = projectile?.Launcher;
            if (pawn == null
                || launcher == null
                || launcher == pawn
                || !launcher.HostileTo(pawn))
            {
                return false;
            }

            Faction pawnFaction = pawn.Faction;
            Faction launcherFaction = launcher.Faction;
            return pawnFaction == null
                || launcherFaction == null
                || pawnFaction.HostileTo(launcherFaction);
        }

        internal static bool IsPotentialExplosiveProjectile(
            Projectile projectile,
            Map map)
        {
            return projectile != null
                && map != null
                && projectile.Spawned
                && !projectile.Destroyed
                && projectile.Map == map
                && projectile.def.projectile?.explosionRadius > 0f
                && !(bool)LandedField.GetValue(projectile);
        }

        public static bool IsInterceptionTargetActive(Projectile projectile)
        {
            return projectile != null
                && projectile.Spawned
                && !projectile.Destroyed
                && projectile.def.projectile?.explosionRadius > 0f
                && !(bool)LandedField.GetValue(projectile);
        }
    }

    public static class RimKataInterceptionUtility
    {
        public static bool Resolve(Pawn pawn, Projectile projectile)
        {
            if (pawn?.Map == null
                || projectile == null
                || !projectile.Spawned
                || projectile.Destroyed)
            {
                return false;
            }

            if (!RimKataCombatMath.RollConfiguredChance(pawn, RimKataChanceKind.ExplosiveInterception))
            {
                return false;
            }

            bool detonateAtCurrentPosition = Rand.Chance(RimKataMod.Settings?.GetInterceptionCriticalChance(pawn) ?? 0f);

            IntVec3 currentCell = projectile.ExactPosition.ToIntVec3();
            if (!currentCell.InBounds(projectile.Map))
            {
                return false;
            }

            if (detonateAtCurrentPosition)
            {
                if (!RimKataProjectileUtility.PrepareImmediateImpact(
                        projectile,
                        currentCell))
                {
                    return false;
                }

                RimKataProjectileUtility.DetonateNow(projectile);
                return true;
            }

            List<IntVec3> cells = GenAdj.AdjacentCellsAndInside
                .Select(offset => currentCell + offset)
                .Where(cell => cell.InBounds(projectile.Map))
                .ToList();

            if (cells.Count == 0)
            {
                return false;
            }

            IntVec3 impactCell = cells.RandomElement();

            if (!RimKataProjectileUtility.PrepareImmediateImpact(projectile, impactCell))
            {
                return false;
            }

            RimKataProjectileUtility.Impact(projectile, null);
            return true;
        }
    }
}
