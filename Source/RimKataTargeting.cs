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
    public static class RimKataTargeting
    {
        private static readonly FieldInfo LandedField = AccessTools.Field(typeof(Projectile), "landed");

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

        public static bool IsCombatCapableCrawling(Pawn pawn)
        {
            return pawn?.Downed == true
                && pawn.Crawling
                && pawn.CanAttackWhileCrawling;
        }

        public static bool IsIncapacitatedTarget(Pawn pawn)
        {
            return pawn?.Downed == true
                && !IsCombatCapableCrawling(pawn);
        }

        public static bool IsPawnTargetStateValid(
            Pawn pawn,
            bool allowIncapacitated = false)
        {
            return pawn != null
                && !pawn.Dead
                && !pawn.IsPsychologicallyInvisible()
                && (!pawn.Downed
                    || IsCombatCapableCrawling(pawn)
                    || allowIncapacitated);
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
                            pawn,
                            primaryWeapon,
                            primaryVerb))
                    : 0f;

            float secondaryRange =
                secondaryVerb != null
                && !secondaryVerb.IsMeleeAttack
                    ? Mathf.Max(
                        0f,
                        RimKataRangeUtility.ResolveCandidateRange(
                            pawn,
                            secondaryWeapon,
                            secondaryVerb))
                    : 0f;

            return Mathf.Max(primaryRange, secondaryRange);
        }

        private static bool IsValidAttackTarget(Pawn shooter, Thing target)
        {
            if (!(target is IAttackTarget)
                || !IsAutomaticEnemy(shooter, target)
                || target.Position.Fogged(shooter.Map))
            {
                return false;
            }

            return !(target is Pawn targetPawn)
                || IsPawnTargetStateValid(targetPawn);
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
            if (pawn == null || projectile == null)
            {
                return false;
            }

            bool defenderHostileToPlayer =
                pawn.Faction?.HostileTo(Faction.OfPlayer) == true;
            bool launchedByPlayer =
                projectile.Launcher?.Faction == Faction.OfPlayer;
            return defenderHostileToPlayer
                ? launchedByPlayer
                : !launchedByPlayer;
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
