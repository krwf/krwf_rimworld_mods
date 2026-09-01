using RimWorld;
using UnityEngine;
using Verse;

namespace KRWF.RimKata
{
    public enum RimKataChanceKind
    {
        RangedDodge,
        MeleeResponse,
        MeleeDodge,
        ExplosiveInterception
    }

    public static class RimKataCombatMath
    {
        public static float ConfiguredChance(Pawn pawn, RimKataChanceKind kind)
        {
            RimKataSettings settings = RimKataMod.Settings;
            if (settings == null)
            {
                return 0f;
            }

            float baseChance;
            switch (kind)
            {
                case RimKataChanceKind.RangedDodge:
                    baseChance = settings.GetRangedDodgeChance(pawn);
                    break;
                case RimKataChanceKind.MeleeResponse:
                    baseChance = settings.GetMeleeResponseChance(pawn);
                    break;
                case RimKataChanceKind.MeleeDodge:
                    baseChance = settings.GetMeleeDodgeChance(pawn);
                    break;
                case RimKataChanceKind.ExplosiveInterception:
                    baseChance = settings.GetInterceptionChance(pawn);
                    break;
                default:
                    baseChance = 0f;
                    break;
            }

            return FinalChance(pawn, kind, baseChance);
        }

        public static bool RollConfiguredChance(Pawn pawn, RimKataChanceKind kind)
        {
            return Rand.Chance(ConfiguredChance(pawn, kind));
        }

        public static float FinalChance(Pawn pawn, RimKataChanceKind kind, float baseChance)
        {
            float multiplier = 1f;
            RimKataSettings settings = RimKataMod.Settings;
            if (settings != null && RimKataSerumUtility.IsMindNumbed(pawn))
            {
                switch (kind)
                {
                    case RimKataChanceKind.RangedDodge:
                        multiplier = settings.GetSerumDodgeMultiplier(pawn);
                        break;
                    case RimKataChanceKind.MeleeResponse:
                        multiplier = settings.GetSerumResponseMultiplier(pawn);
                        break;
                    case RimKataChanceKind.ExplosiveInterception:
                        multiplier = settings.GetSerumInterceptionMultiplier(pawn);
                        break;
                }
            }

            return Mathf.Clamp01(baseChance * multiplier);
        }

        public static float MovingHitChance(Pawn pawn, float vanillaFinalHitChance)
        {
            float multiplier = RimKataMod.Settings?.GetMovingAccuracyMultiplier(pawn) ?? 1f;
            return Mathf.Clamp01(vanillaFinalHitChance * multiplier);
        }

        public static bool MovingAccuracyIsModified(Pawn pawn)
        {
            return !Mathf.Approximately(RimKataMod.Settings?.GetMovingAccuracyMultiplier(pawn) ?? 1f, 1f);
        }

        public static bool RollCloseMeleeNonMiss(Pawn attacker, Thing target)
        {
            return Rand.Chance(CloseMeleeNonMissChance(attacker, target));
        }

        public static bool RollCloseRangedNonMiss(Pawn attacker, Verb verb, LocalTargetInfo target)
        {
            if (attacker == null
                || verb == null
                || !target.IsValid)
            {
                return false;
            }

            ShotReport report = ShotReport.HitReportFor(attacker, verb, target);

            return Rand.Chance(Mathf.Clamp01(report.TotalEstimatedHitChance));
        }

        public static bool RollCloseMeleeDodge(Pawn target)
        {
            return Rand.Chance(CloseMeleeDodgeChance(target));
        }

        public static float AddConfiguredMeleeDodgeBonus(Pawn target, float vanillaChance)
        {
            if (!RimKataEligibility.CanRollMeleeDodge(target))
            {
                return vanillaChance;
            }

            return ApplyConfiguredMeleeDodgeBonus(target, vanillaChance);
        }

        private static float ApplyConfiguredMeleeDodgeBonus(
            Pawn target,
            float vanillaChance)
        {
            float bonusMultiplier = RimKataMod.Settings?.GetMeleeDodgeBonusMultiplier(target) ?? 1f;
            return Mathf.Clamp01(vanillaChance * bonusMultiplier);
        }

        public static float AddConfiguredMeleeDodgeBonusToVanilla(
            float vanillaChance,
            LocalTargetInfo target)
        {
            return AddConfiguredMeleeDodgeBonus(target.Pawn, vanillaChance);
        }

        public static float CloseMeleeNonMissChance(Pawn attacker, Thing target)
        {
            if (attacker == null || target == null)
            {
                return 0f;
            }

            if (IsMeleeTargetImmobile(target))
            {
                return 1f;
            }

            float chance = attacker.GetStatValue(StatDefOf.MeleeHitChance);
            if (ModsConfig.IdeologyActive)
            {
                chance += MeleeLightingOffset(
                    attacker,
                    target,
                    StatDefOf.MeleeHitChanceOutdoorsLitOffset,
                    StatDefOf.MeleeHitChanceOutdoorsDarkOffset,
                    StatDefOf.MeleeHitChanceIndoorsDarkOffset,
                    StatDefOf.MeleeHitChanceIndoorsLitOffset);
            }

            return chance;
        }

        public static bool RollMeleeParry(Pawn defender, Pawn attacker)
        {
            return Rand.Chance(MeleeParryChance(defender, attacker));
        }

        public static float MeleeParryChance(Pawn defender, Pawn attacker)
        {
            if (defender == null || attacker == null)
            {
                return 0f;
            }

            float chance = defender.GetStatValue(StatDefOf.MeleeHitChance);
            if (ModsConfig.IdeologyActive)
            {
                chance += MeleeLightingOffset(
                    defender,
                    attacker,
                    StatDefOf.MeleeHitChanceOutdoorsLitOffset,
                    StatDefOf.MeleeHitChanceOutdoorsDarkOffset,
                    StatDefOf.MeleeHitChanceIndoorsDarkOffset,
                    StatDefOf.MeleeHitChanceIndoorsLitOffset);
            }

            RimKataSettings settings = RimKataMod.Settings;
            chance *= settings?.GetMeleeResponseBonusMultiplier(defender) ?? 1f;
            if (settings != null && RimKataSerumUtility.IsMindNumbed(defender))
            {
                chance *= settings.GetSerumResponseMultiplier(defender);
            }

            return Mathf.Clamp01(chance);
        }

        public static float CloseMeleeDodgeChance(Pawn target)
        {
            return CloseMeleeDodgeChanceCore(target, false);
        }

        internal static float CloseMeleeDodgeChanceVerified(Pawn target)
        {
            return CloseMeleeDodgeChanceCore(target, true);
        }

        private static float CloseMeleeDodgeChanceCore(
            Pawn target,
            bool defenseEligibilityVerified)
        {
            if (target == null || IsMeleeTargetImmobile(target))
            {
                return 0f;
            }

            if (target.stances?.curStance is Stance_Busy busy && busy.verb != null && !busy.verb.verbProps.IsMeleeAttack)
            {
                return 0f;
            }

            float chance = target.GetStatValue(StatDefOf.MeleeDodgeChance);
            if (ModsConfig.IdeologyActive)
            {
                chance += MeleeLightingOffset(
                    target,
                    target,
                    StatDefOf.MeleeDodgeChanceOutdoorsLitOffset,
                    StatDefOf.MeleeDodgeChanceOutdoorsDarkOffset,
                    StatDefOf.MeleeDodgeChanceIndoorsDarkOffset,
                    StatDefOf.MeleeDodgeChanceIndoorsLitOffset);
            }

            return defenseEligibilityVerified
                ? ApplyConfiguredMeleeDodgeBonus(target, chance)
                : AddConfiguredMeleeDodgeBonus(target, chance);
        }

        private static bool IsMeleeTargetImmobile(Thing target)
        {
            if (target?.def?.category == ThingCategory.Pawn && target is Pawn targetPawn && !targetPawn.Downed)
            {
                return targetPawn.GetPosture() != PawnPosture.Standing;
            }

            return true;
        }

        private static float MeleeLightingOffset(
            Pawn statPawn,
            Thing target,
            StatDef outdoorsLit,
            StatDef outdoorsDark,
            StatDef indoorsDark,
            StatDef indoorsLit)
        {
            if (DarknessCombatUtility.IsOutdoorsAndLit(target))
            {
                return statPawn.GetStatValue(outdoorsLit);
            }

            if (DarknessCombatUtility.IsOutdoorsAndDark(target))
            {
                return statPawn.GetStatValue(outdoorsDark);
            }

            if (DarknessCombatUtility.IsIndoorsAndDark(target))
            {
                return statPawn.GetStatValue(indoorsDark);
            }

            if (DarknessCombatUtility.IsIndoorsAndLit(target))
            {
                return statPawn.GetStatValue(indoorsLit);
            }

            return 0f;
        }

        public static int WarmupTicksForSingleShot(Verb verb)
        {
            if (verb == null)
            {
                return 0;
            }

            float aimingFactor = verb.CasterPawn?.GetStatValue(StatDefOf.AimingDelayFactor) ?? 1f;
            float warmupSeconds = Mathf.Max(0f, verb.WarmupTime);

            return Mathf.Max(0, Mathf.RoundToInt(warmupSeconds * aimingFactor * 60f));
        }

        public static int CooldownTicksForSingleShot(Verb verb, Pawn pawn, bool afterSuccessfulResponse)
        {
            if (verb?.verbProps == null || pawn == null)
            {
                return 0;
            }

            int originalBurstCount = RimKataMod.Settings?.singleShotConversionEnabled == false
                ? 1
                : Mathf.Max(1, verb.BurstShotCount);
            return CooldownTicksForSingleShot(
                verb,
                pawn,
                afterSuccessfulResponse,
                originalBurstCount);
        }

        public static int CooldownTicksForSingleShot(
            Verb verb,
            Pawn pawn,
            bool afterSuccessfulResponse,
            int originalBurstCount)
        {
            if (verb?.verbProps == null || pawn == null)
            {
                return 0;
            }

            originalBurstCount = Mathf.Max(1, originalBurstCount);
            float ticks = verb.verbProps.AdjustedCooldownTicks(verb, pawn) / (float)originalBurstCount;
            RimKataSettings settings = RimKataMod.Settings;
            if (settings != null && RimKataEquipmentUtility.HasEnabledArmor(pawn))
            {
                ticks *= settings.GetArmorCooldownFactor(pawn);
            }

            if (settings != null && afterSuccessfulResponse && RimKataEquipmentUtility.IsWeaponEnabled(verb.EquipmentSource?.def))
            {
                ticks *= settings.GetResponseCooldownFactor(pawn);
            }

            return Mathf.Max(1, Mathf.RoundToInt(ticks));
        }
    }

    public static class RimKataCombatTuning
    {
        public const int StandardDodgeDurationTicks = 30;
        public const int AdditionalDodgeTumbleDurationTicks = 24;
        public const int AdditionalDodgeWatchdogTicks = 180;
        public const int AdditionalDodgeLandingTicks = 2;
        public const int CombatRequestGraceTicks = 20;
        public const int MovingFireContinuityTicks = 2;
        public const float TumbleDegreesPerTick = 15f;
    }

    public static class RimKataSerumUtility
    {
        public static bool IsMindNumbed(Pawn pawn)
        {
            return RimKataEligibilityCache.IsMindNumbed(pawn);
        }
    }
}
