using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace KRWF.RimKata
{
    internal struct RimKataDirectCloseHit
    {
        internal Pawn shooter;
        internal Thing target;
        internal Thing weapon;
        internal ThingDef projectileDef;
        internal DamageDef damageDefOverride;
        internal List<ExtraDamage> extraDamages;
        internal float stoppingPower;
        internal int damageAmount;
        internal float armorPenetration;
        internal QualityCategory weaponQuality;
        internal float angle;
        internal bool usesProjectileImpact;
        internal bool preventFriendlyFire;
        internal bool canHitNonTargetPawns;

        internal void ConfigureProjectile(Projectile projectile)
        {
            projectile.damageDefOverride = damageDefOverride;
            if (extraDamages != null)
            {
                projectile.extraDamages = new List<ExtraDamage>(extraDamages);
            }
            projectile.stoppingPower = stoppingPower;
        }

        internal void Resolve()
        {
            Map map = shooter?.Map;
            if (map == null || target?.Spawned != true || target.Map != map)
            {
                return;
            }

            bool hit = RimKataFireContext.CloseMeleeHit;
            Thing hitThing = hit ? target : null;
            IntVec3 cell = hit
                ? target.Position
                : RimKataProjectileUtility.FindCloseMissCell(shooter, target, map);
            if (usesProjectileImpact)
            {
                if (!RimKataCloseProjectilePlacement.TryPlace(this, cell, out Projectile projectile))
                {
                    return;
                }
                try
                {
                    map.GetComponent<RimKataMapComponent>()?
                        .RegisterLaunchedExplosiveProjectile(projectile);
                    // The projectile owns both direct damage and its effects. A
                    // delayed explosive survives this call with its native fuse.
                    RimKataProjectileUtility.Impact(projectile, hitThing);
                }
                catch
                {
                    if (!projectile.Destroyed)
                    {
                        projectile.Destroy();
                    }
                    throw;
                }
                return;
            }
            GenClamor.DoClamor(shooter, cell, 12f, ClamorDefOf.Impact);
            BattleLogEntry_RangedImpact log = new BattleLogEntry_RangedImpact(
                shooter, hitThing, target, weapon?.def, projectileDef, null);
            Find.BattleLog.Add(log);
            if (!hit)
            {
                SoundDefOf.BulletImpact_Ground.PlayOneShot(new TargetInfo(cell, map));
                if (cell.GetTerrain(map).takeSplashes)
                {
                    FleckMaker.WaterSplash(cell.ToVector3Shifted(), map,
                        Mathf.Sqrt(damageAmount), 4f);
                }
                else
                {
                    FleckMaker.Static(cell.ToVector3Shifted(), map, FleckDefOf.ShotHit_Dirt);
                }
                return;
            }

            // TakeDamage retains the native shield, armor and injury pipeline.
            // Even an avoided precheck reaches it so the defense scope records the result.
            DamageInfo damage = MakeDamageInfo(
                damageDefOverride ?? projectileDef.projectile.damageDef,
                damageAmount, armorPenetration);
            damage.SetWeaponQuality(weaponQuality);
            target.TakeDamage(damage).AssociateWithLog(log);
            ApplyStagger(target as Pawn);
            ApplyExtraDamages(extraDamages, log);
            ApplyExtraDamages(projectileDef.projectile.extraDamages, log);
        }

        private DamageInfo MakeDamageInfo(DamageDef def, float amount, float penetration)
        {
            return new DamageInfo(def, amount, penetration, angle, shooter, null,
                weapon?.def, DamageInfo.SourceCategory.ThingOrUnknown, target,
                !shooter.Drafted);
        }

        private void ApplyExtraDamages(List<ExtraDamage> extras, BattleLogEntry_RangedImpact log)
        {
            if (extras == null)
            {
                return;
            }
            foreach (ExtraDamage extra in extras)
            {
                if (Rand.Chance(extra.chance))
                {
                    // Bullet uses raw extra amounts and no main-packet quality override.
                    target.TakeDamage(MakeDamageInfo(extra.def, extra.amount,
                        extra.AdjustedArmorPenetration())).AssociateWithLog(log);
                }
            }
        }

        private void ApplyStagger(Pawn pawn)
        {
            if (pawn?.stances?.stagger == null
                || (RimKataDefenseUtility.TryGetCloseAttackResolution(pawn, out bool avoided)
                    && avoided)
                || (!pawn.RaceProps.bulletStaggerIgnoreBodySize
                    && pawn.BodySize > stoppingPower + 0.001f))
            {
                return;
            }
            RimKataDefenseUtility.DamageStaggerContextState scope =
                RimKataDefenseUtility.EnterDamageStaggerContext(pawn, shooter);
            try
            {
                pawn.stances.stagger.StaggerFor(
                    pawn.RaceProps.bulletStaggerDelayTicks ?? 95,
                    pawn.RaceProps.bulletStaggerSpeedFactor ?? 0.17f);
            }
            finally
            {
                RimKataDefenseUtility.ExitDamageStaggerContext(scope);
            }
        }
    }

    internal static class RimKataDirectCloseShot
    {
        internal static bool TryPrepare(Verb_LaunchProjectile verb,
            bool canHitNonTargetPawns, out RimKataDirectCloseHit hit)
        {
            hit = default(RimKataDirectCloseHit);
            if (verb == null || RimKataFireContext.ActiveVerb != verb
                || !RimKataFireContext.CloseShot || !RimKataFireContext.CloseMeleeResolution
                || RimKataFireContext.InterceptionShot || RimKataFireContext.SuppressCloseLaunch)
            {
                return false;
            }
            Pawn shooter = verb.CasterPawn;
            Thing target = RimKataFireContext.CloseTarget;
            ThingDef projectileDef = verb.Projectile;
            ProjectileProperties props = projectileDef?.projectile;
            if (shooter?.Map == null || target?.Spawned != true || target.Map != shooter.Map
                || props?.damageDef == null || projectileDef.thingClass == null
                || !typeof(Projectile).IsAssignableFrom(projectileDef.thingClass))
            {
                return false;
            }

            Thing weapon = verb.EquipmentSource;
            DamageDef damageOverride = null;
            List<ExtraDamage> extraDamages = null;
            float stoppingPower = props.stoppingPower;
            if (stoppingPower == 0f)
            {
                stoppingPower = props.damageDef.defaultStoppingPower;
            }
            CompUniqueWeapon uniqueWeapon = weapon?.TryGetComp<CompUniqueWeapon>();
            if (uniqueWeapon != null)
            {
                foreach (WeaponTraitDef trait in uniqueWeapon.TraitsListForReading)
                {
                    if (trait.damageDefOverride != null)
                    {
                        damageOverride = trait.damageDefOverride;
                    }
                    if (!trait.extraDamages.NullOrEmpty())
                    {
                        extraDamages ??= new List<ExtraDamage>();
                        extraDamages.AddRange(trait.extraDamages);
                    }
                    if (!Mathf.Approximately(trait.additionalStoppingPower, 0f))
                    {
                        stoppingPower += trait.additionalStoppingPower;
                    }
                }
            }
            DamageDef damageDef = damageOverride ?? props.damageDef;
            bool directDamage = (verb.GetType() == typeof(Verb_Shoot)
                    || verb.GetType() == typeof(Verb_LaunchProjectile))
                && projectileDef.thingClass == typeof(Bullet) && projectileDef.comps.NullOrEmpty()
                && SupportsDirectImpact(props)
                && (!(target is Pawn pawn) || pawn.RaceProps.bulletStaggerEffecterDef == null)
                && damageDef.isRanged && !damageDef.isExplosive && damageDef.igniteCellChance <= 0f;
            if (!directDamage
                && !RimKataCloseProjectilePlacement.SupportsNativeInitialization(projectileDef.thingClass))
            {
                return false;
            }

            QualityCategory quality = QualityCategory.Normal;
            weapon?.TryGetQuality(out quality);
            hit = new RimKataDirectCloseHit
            {
                shooter = shooter,
                target = target,
                weapon = weapon,
                projectileDef = projectileDef,
                damageDefOverride = damageOverride,
                extraDamages = extraDamages,
                stoppingPower = stoppingPower,
                damageAmount = directDamage ? props.GetDamageAmount(weapon) : 0,
                armorPenetration = directDamage ? props.GetArmorPenetration(weapon) : 0f,
                weaponQuality = quality,
                angle = (target.DrawPos - shooter.DrawPos).AngleFlat(),
                usesProjectileImpact = !directDamage,
                preventFriendlyFire = verb.preventFriendlyFire,
                canHitNonTargetPawns = canHitNonTargetPawns
            };

            // Read the selected ammo and its damage before consuming its last charge.
            weapon?.TryGetComp<CompChangeableProjectile>()?.Notify_ProjectileLaunched();
            weapon?.TryGetComp<CompApparelVerbOwner_Charged>()?.UsedOnce();
            return true;
        }

        private static bool SupportsDirectImpact(ProjectileProperties props)
        {
            // Other payloads use their own Impact instead of duplicating its effects.
            return props?.damageDef != null && !props.flyOverhead
                && props.explosionRadius <= 0f && props.explosionDelay <= 0
                && props.landedEffecter == null && props.soundAmbient == null
                && props.soundImpactAnticipate == null && props.soundImpact == null
                && props.preExplosionSpawnThingDef == null
                && props.postExplosionSpawnThingDef == null
                && props.postExplosionSpawnThingDefWater == null
                && props.preExplosionSpawnSingleThingDef == null
                && props.postExplosionSpawnSingleThingDef == null
                && !props.postExplosionGasType.HasValue && props.filth == null
                && props.spawnTerrain == null && props.spawnsThingDef == null
                && props.spawnsPawnKind == null && props.explosionEffect == null;
        }
    }
}
