using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace KRWF.RimKata
{
    internal static class RimKataCloseProjectilePlacement
    {
        private static readonly AccessTools.FieldRef<Projectile, Thing> Launcher =
            AccessTools.FieldRefAccess<Projectile, Thing>("launcher");
        private static readonly AccessTools.FieldRef<Projectile, Thing> Equipment =
            AccessTools.FieldRefAccess<Projectile, Thing>("equipment");
        private static readonly AccessTools.FieldRef<Projectile, ThingDef> EquipmentDef =
            AccessTools.FieldRefAccess<Projectile, ThingDef>("equipmentDef");
        private static readonly AccessTools.FieldRef<Projectile, QualityCategory> EquipmentQuality =
            AccessTools.FieldRefAccess<Projectile, QualityCategory>("equipmentQuality");
        private static readonly AccessTools.FieldRef<Projectile, Vector3> Origin =
            AccessTools.FieldRefAccess<Projectile, Vector3>("origin");
        private static readonly AccessTools.FieldRef<Projectile, Vector3> Destination =
            AccessTools.FieldRefAccess<Projectile, Vector3>("destination");
        private static readonly AccessTools.FieldRef<Projectile, ThingDef> TargetCoverDef =
            AccessTools.FieldRefAccess<Projectile, ThingDef>("targetCoverDef");
        private static readonly AccessTools.FieldRef<Projectile, bool> PreventFriendlyFire =
            AccessTools.FieldRefAccess<Projectile, bool>("preventFriendlyFire");
        private static readonly AccessTools.FieldRef<Projectile, int> TicksToImpact =
            AccessTools.FieldRefAccess<Projectile, int>("ticksToImpact");
        private static readonly AccessTools.FieldRef<Projectile, int> Lifetime =
            AccessTools.FieldRefAccess<Projectile, int>("lifetime");
        private static readonly AccessTools.FieldRef<Projectile, bool> Landed =
            AccessTools.FieldRefAccess<Projectile, bool>("landed");
        private static readonly AccessTools.FieldRef<Projectile, Sustainer> AmbientSustainer =
            AccessTools.FieldRefAccess<Projectile, Sustainer>("ambientSustainer");

        private static readonly Type[] LaunchParameters =
        {
            typeof(Thing), typeof(Vector3), typeof(LocalTargetInfo), typeof(LocalTargetInfo),
            typeof(ProjectileHitFlags), typeof(bool), typeof(Thing), typeof(ThingDef)
        };
        private static readonly Dictionary<Type, bool> NativeInitialization =
            new Dictionary<Type, bool>();

        internal static bool SupportsNativeInitialization(Type projectileType)
        {
            if (projectileType == null || projectileType.IsAbstract
                || !typeof(Projectile).IsAssignableFrom(projectileType))
            {
                return false;
            }
            if (!NativeInitialization.TryGetValue(projectileType, out bool supported))
            {
                // Custom Impact and SpawnSetup stay native. A Launch override can own
                // additional state that the base-field initialization cannot reproduce.
                supported = true;
                for (Type current = projectileType; current != typeof(Projectile);
                    current = current.BaseType)
                {
                    MethodInfo method = current.GetMethod(nameof(Projectile.Launch),
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                            | BindingFlags.DeclaredOnly, null, LaunchParameters, null);
                    if (method?.IsVirtual == true
                        && method.GetBaseDefinition().DeclaringType == typeof(Projectile))
                    {
                        supported = false;
                        break;
                    }
                }
                NativeInitialization.Add(projectileType, supported);
            }
            return supported;
        }

        internal static bool TryPlace(
            RimKataDirectCloseHit hit,
            IntVec3 cell,
            out Projectile projectile)
        {
            projectile = null;
            Map map = hit.shooter?.Map;
            ProjectileProperties props = hit.projectileDef?.projectile;
            if (map == null || !cell.InBounds(map) || props == null
                || !SupportsNativeInitialization(hit.projectileDef.thingClass))
            {
                return false;
            }

            Projectile placed = ThingMaker.MakeThing(hit.projectileDef) as Projectile;
            if (placed == null)
            {
                return false;
            }

            Vector3 destination = cell.ToVector3Shifted();
            Vector3 origin = hit.shooter.DrawPos;
            if ((destination - origin).Yto0().sqrMagnitude < 0.0001f)
            {
                // A same-cell impact still needs a nonzero native rotation vector.
                origin = destination - Vector3.forward.RotatedBy(hit.angle) * 0.001f;
            }
            Launcher(placed) = hit.shooter;
            Equipment(placed) = hit.weapon;
            EquipmentDef(placed) = hit.weapon?.def;
            EquipmentQuality(placed) = hit.weaponQuality;
            Origin(placed) = origin;
            Destination(placed) = destination;
            TargetCoverDef(placed) = null;
            PreventFriendlyFire(placed) = hit.preventFriendlyFire;
            placed.intendedTarget = hit.target != null
                ? new LocalTargetInfo(hit.target)
                : LocalTargetInfo.Invalid;
            bool hitsTarget = RimKataFireContext.CloseMeleeHit
                && hit.target?.Spawned == true && hit.target.Map == map;
            placed.usedTarget = hitsTarget
                ? new LocalTargetInfo(hit.target)
                : new LocalTargetInfo(cell);
            ProjectileHitFlags flags = hitsTarget
                ? ProjectileHitFlags.IntendedTarget
                : ProjectileHitFlags.None;
            if (hit.canHitNonTargetPawns)
            {
                flags |= ProjectileHitFlags.NonTargetPawns;
            }
            if (!hitsTarget || hit.target.def.Fillage == FillCategory.Full)
            {
                flags |= ProjectileHitFlags.NonTargetWorld;
            }
            placed.HitFlags = flags;
            hit.ConfigureProjectile(placed);

            // This is an impact-position object, not a flight scheduled for later.
            // Delayed explosive Impact sets landed and its own serialized detonation timer.
            TicksToImpact(placed) = 0;
            Lifetime(placed) = 0;
            Landed(placed) = false;
            GenSpawn.Spawn(placed, cell, map);
            if (!placed.Spawned || placed.Destroyed)
            {
                return false;
            }
            if (!props.soundAmbient.NullOrUndefined())
            {
                AmbientSustainer(placed) = props.soundAmbient.TrySpawnSustainer(
                    SoundInfo.InMap(placed, MaintenanceType.PerTick));
            }
            projectile = placed;
            return true;
        }
    }
}
