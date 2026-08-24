using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace KRWF.RimKata
{
    public static class RimKataDodgeMovementUtility
    {
        private static readonly IntVec3[] EightDirections =
        {
            IntVec3.North,
            IntVec3.NorthEast,
            IntVec3.East,
            IntVec3.SouthEast,
            IntVec3.South,
            IntVec3.SouthWest,
            IntVec3.West,
            IntVec3.NorthWest
        };

        private static readonly FieldInfo PathEndModeField = AccessTools.Field(typeof(Pawn_PathFollower), "peMode");

        public static bool IsActive(Pawn pawn)
        {
            return pawn?.Map?.GetComponent<RimKataMapComponent>()?.IsDodgeMovementActive(pawn) == true;
        }

        public static bool BlocksJob(Pawn pawn)
        {
            return pawn?.Map?.GetComponent<RimKataMapComponent>()?.IsDodgeMotionBlocking(pawn) == true;
        }

        public static bool IsVisualLocked(Pawn pawn)
        {
            return pawn?.Map?.GetComponent<RimKataMapComponent>()?.IsDodgeVisualLocked(pawn) == true;
        }

        public static bool ShouldBlockPhysicalMeleeVerb(Verb_MeleeAttack meleeVerb)
        {
            Pawn pawn = meleeVerb?.CasterPawn;
            if (pawn == null
                || RimKataMod.Settings?.closeFireEnabled == false
                || !RimKataEligibility.CanBeginGunKataAttack(pawn))
            {
                return false;
            }

            ThingWithComps sourceWeapon = meleeVerb.EquipmentSource as ThingWithComps;
            if (sourceWeapon != null)
            {
                if (!RimKataEquipmentUtility.IsWeaponEnabled(sourceWeapon.def))
                {
                    return false;
                }

                Verb combatVerb = RimKataWeaponSlotUtility.CombatVerb(pawn, sourceWeapon);
                return combatVerb != null && !combatVerb.IsMeleeAttack;
            }

            ThingWithComps primary =
                RimKataWeaponSlotUtility.PrimaryWeapon(pawn);
            Verb primaryCombatVerb =
                RimKataWeaponSlotUtility.CombatVerb(pawn, primary);
            if (RimKataEquipmentUtility.IsWeaponEnabled(primary?.def)
                && primaryCombatVerb != null
                && !primaryCombatVerb.IsMeleeAttack)
            {
                return true;
            }

            ThingWithComps secondary =
                RimKataWeaponSlotUtility.CanUseSecondarySlot(pawn)
                    ? RimKataWeaponSlotUtility.SecondaryWeapon(pawn)
                    : null;
            Verb secondaryCombatVerb =
                RimKataWeaponSlotUtility.CombatVerb(pawn, secondary);
            return RimKataEquipmentUtility.IsWeaponEnabled(secondary?.def)
                && secondaryCombatVerb != null
                && !secondaryCombatVerb.IsMeleeAttack;
        }

        private static bool HasPhysicalMovementIntent(Pawn pawn)
        {
            JobDef jobDef = pawn?.CurJobDef;
            return jobDef == JobDefOf.Goto
                || jobDef == RimKataDefOf.RimKata_Attack;
        }

        public static bool TryStartMovingFirstDodge(
            Pawn pawn,
            Projectile projectile,
            int dodgeWindowDurationTicks)
        {
            if (!TryGetCurrentMovementDirection(pawn, out IntVec3 movementDirection)
                || !TryChooseDestination(pawn, movementDirection, out IntVec3 destination))
            {
                return false;
            }

            return TryStartInternal(
                pawn,
                projectile,
                destination,
                movementDirection,
                dodgeWindowDurationTicks);
        }

        public static bool TryUpgradeMovingDodgeToTumble(Pawn pawn)
        {
            return pawn?.Map?.GetComponent<RimKataMapComponent>()
                ?.TryUpgradeDodgeMovementToTumble(pawn) == true;
        }

        public static bool TryGetCurrentMovementDirection(Pawn pawn, out IntVec3 direction)
        {
            direction = IntVec3.Invalid;
            if (!HasPhysicalMovementIntent(pawn))
            {
                return false;
            }

            Pawn_PathFollower pather = pawn?.pather;
            if (pather?.Moving != true)
            {
                return false;
            }

            if (!pather.nextCell.IsValid || pather.nextCell == pawn.Position)
            {
                return false;
            }

            IntVec3 delta = pather.nextCell - pawn.Position;
            direction = QuantizeDirection(delta.ToVector3());
            return true;
        }

        public static IntVec3 CombatDirection(Pawn pawn, Thing attacker)
        {
            if (pawn == null)
            {
                return IntVec3.North;
            }

            LocalTargetInfo aimingTarget = pawn.TargetCurrentlyAimingAt;
            if (TryDirectionTo(pawn, aimingTarget, out IntVec3 direction))
            {
                return direction;
            }

            Job job = pawn.CurJob;
            bool combatJob = job != null
                && (job.def == RimKataDefOf.RimKata_Attack
                    || job.def == JobDefOf.AttackStatic
                    || job.def == JobDefOf.AttackMelee);
            if (combatJob && TryDirectionTo(pawn, job.targetA, out direction))
            {
                return direction;
            }

            if (pawn.pather?.MovingNow == true
                && pawn.pather.nextCell.IsValid
                && pawn.pather.nextCell != pawn.Position)
            {
                return QuantizeDirection((pawn.pather.nextCell - pawn.Position).ToVector3());
            }

            if (attacker != null && attacker != pawn)
            {
                Vector3 attackVector = attacker.DrawPos - pawn.DrawPos;
                if (attackVector.sqrMagnitude > 0.001f)
                {
                    return QuantizeDirection(attackVector);
                }
            }

            return IntVec3.North;
        }

        public static void AdjacentDirections(IntVec3 combatDirection, out IntVec3 first, out IntVec3 second)
        {
            int sector = DirectionIndex(combatDirection);
            first = EightDirections[(sector + 7) & 7];
            second = EightDirections[(sector + 1) & 7];
        }

        public static bool TryChooseDestination(Pawn pawn, IntVec3 combatDirection, out IntVec3 destination)
        {
            destination = IntVec3.Invalid;
            AdjacentDirections(combatDirection, out IntVec3 left, out IntVec3 right);
            IntVec3 first = Rand.Bool ? left : right;
            IntVec3 second = first == left ? right : left;
            IntVec3 firstCell = pawn.Position + first;
            IntVec3 secondCell = pawn.Position + second;
            if (CanEnterDodgeCell(pawn, firstCell))
            {
                destination = firstCell;
                return true;
            }

            if (CanEnterDodgeCell(pawn, secondCell))
            {
                destination = secondCell;
                return true;
            }

            return false;
        }

        private static bool TryStartInternal(
            Pawn pawn,
            Projectile projectile,
            IntVec3 destination,
            IntVec3 combatDirection,
            int dodgeWindowDurationTicks)
        {
            if (pawn?.Map == null || !CanEnterDodgeCell(pawn, destination))
            {
                return false;
            }

            Pawn_PathFollower pather = pawn.pather;
            bool resumeWasMoving = pather?.Moving == true && pather.Destination.IsValid;
            LocalTargetInfo resumeDestination = resumeWasMoving
                ? pather.Destination
                : LocalTargetInfo.Invalid;
            PathEndMode resumeMode = resumeWasMoving && PathEndModeField != null
                ? (PathEndMode)PathEndModeField.GetValue(pather)
                : (resumeDestination.HasThing ? PathEndMode.Touch : PathEndMode.OnCell);
            RimKataMapComponent component = pawn.Map.GetComponent<RimKataMapComponent>();
            if (component == null)
            {
                return false;
            }

            if (component.IsDodgeVisualLocked(pawn))
            {
                return false;
            }

            if (pather.Moving)
            {
                pather.StopDead();
            }

            Job movementJob = pawn.CurJob;
            GetFailureStagger(
                pawn,
                projectile,
                out int failureStaggerTicks,
                out float failureStaggerSpeedFactor);
            bool started = component.BeginDodgeMovement(
                pawn,
                destination,
                combatDirection,
                resumeWasMoving,
                resumeDestination,
                resumeMode,
                movementJob,
                failureStaggerTicks,
                failureStaggerSpeedFactor,
                dodgeWindowDurationTicks);
            if (started)
            {
                return true;
            }

            component.CancelFailedDodgeMovementStart(pawn);
            if (resumeWasMoving
                && pawn.CurJob == movementJob
                && resumeDestination.IsValid
                && (!resumeDestination.HasThing || !resumeDestination.ThingDestroyed)
                && pawn.Spawned)
            {
                pather.StartPath(resumeDestination, resumeMode);
            }

            return false;
        }

        public static bool TryFinish(Pawn pawn, bool failed)
        {
            return pawn?.Map?.GetComponent<RimKataMapComponent>()?.TryFinishDodgeMovement(pawn, failed) == true;
        }

        private static bool TryDirectionTo(Pawn pawn, LocalTargetInfo target, out IntVec3 direction)
        {
            direction = IntVec3.Invalid;
            if (!target.IsValid || target.Cell == pawn.Position)
            {
                return false;
            }

            direction = QuantizeDirection((target.Cell - pawn.Position).ToVector3());
            return true;
        }

        private static IntVec3 QuantizeDirection(Vector3 vector)
        {
            if (vector.sqrMagnitude < 0.001f)
            {
                return IntVec3.North;
            }

            float clockwiseFromNorth = Mathf.Repeat(Mathf.Atan2(vector.x, vector.z) * Mathf.Rad2Deg, 360f);
            int sector = Mathf.RoundToInt(clockwiseFromNorth / 45f) & 7;
            return EightDirections[sector];
        }

        private static int DirectionIndex(IntVec3 direction)
        {
            for (int i = 0; i < EightDirections.Length; i++)
            {
                if (EightDirections[i] == direction)
                {
                    return i;
                }
            }

            return DirectionIndex(QuantizeDirection(direction.ToVector3()));
        }

        private static void GetFailureStagger(
            Pawn pawn,
            Projectile projectile,
            out int ticks,
            out float moveSpeedFactor)
        {
            ticks = 0;
            moveSpeedFactor = StaggerHandler.DefaultStaggerMoveSpeedFactor;
            if (!(projectile is Bullet bullet)
                || (!pawn.RaceProps.bulletStaggerIgnoreBodySize
                    && pawn.BodySize > bullet.stoppingPower + 0.001f))
            {
                return;
            }

            ticks = pawn.RaceProps.bulletStaggerDelayTicks ?? 95;
            moveSpeedFactor = pawn.RaceProps.bulletStaggerSpeedFactor
                ?? StaggerHandler.DefaultStaggerMoveSpeedFactor;
        }

        private static bool CanEnterDodgeCell(Pawn pawn, IntVec3 cell)
        {
            if (pawn?.Map == null
                || !cell.InBounds(pawn.Map)
                || !cell.WalkableBy(pawn.Map, pawn)
                || !pawn.CanReachImmediate(cell, PathEndMode.Touch))
            {
                return false;
            }

            Building_Door door = cell.GetDoor(pawn.Map);
            if (door != null && (!door.Open || door.TicksTillFullyOpened > 0))
            {
                return false;
            }

            Pawn occupyingPawn = cell.GetFirstPawn(pawn.Map);
            return occupyingPawn == null || occupyingPawn == pawn;
        }

    }

    [HarmonyPatch(typeof(Pawn_PathFollower), "PatherArrived")]
    public static class Patch_PawnPathFollower_RimKataDodgeArrived
    {
        public static bool Prefix(Pawn ___pawn)
        {
            return !RimKataDodgeMovementUtility.TryFinish(___pawn, false);
        }
    }

    [HarmonyPatch(typeof(Pawn_PathFollower), "PatherFailed")]
    public static class Patch_PawnPathFollower_RimKataDodgeFailed
    {
        public static bool Prefix(Pawn ___pawn)
        {
            return !RimKataDodgeMovementUtility.TryFinish(___pawn, true);
        }
    }

    [HarmonyPatch(typeof(JobDriver), nameof(JobDriver.DriverTick))]
    public static class Patch_JobDriver_RimKataDodgeMovement
    {
        public static bool Prefix(JobDriver __instance, Pawn ___pawn)
        {
            if (!RimKataDodgeMovementUtility.BlocksJob(___pawn))
            {
                return true;
            }

            return RimKataDodgeMovementUtility.IsActive(___pawn)
                && __instance is JobDriver_RimKataAttack;
        }
    }

    [HarmonyPatch(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.PatherTick))]
    public static class Patch_PawnPathFollower_RimKataDodgeMovement
    {
        private static readonly MethodInfo FullBodyBusyGetter = AccessTools.PropertyGetter(
            typeof(Pawn_StanceTracker),
            nameof(Pawn_StanceTracker.FullBodyBusy));
        private static readonly FieldInfo PawnField = AccessTools.Field(typeof(Pawn_PathFollower), "pawn");
        private static readonly MethodInfo GateMethod = AccessTools.Method(
            typeof(Patch_PawnPathFollower_RimKataDodgeMovement),
            nameof(GateFullBodyBusy));

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            bool patched = false;
            for (int i = 0; i < codes.Count; i++)
            {
                yield return codes[i];
                if (!patched && codes[i].Calls(FullBodyBusyGetter))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Ldfld, PawnField);
                    yield return new CodeInstruction(OpCodes.Call, GateMethod);
                    patched = true;
                }
            }

            if (!patched)
            {
                Log.Error("[RimKata] Could not place the dodge movement stance gate.");
            }
        }

        public static bool GateFullBodyBusy(bool fullBodyBusy, Pawn pawn)
        {
            return fullBodyBusy && !RimKataDodgeMovementUtility.IsActive(pawn);
        }
    }
}
