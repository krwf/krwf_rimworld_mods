using RimWorld;
using System;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace KRWF.RimKata
{
    public enum RimKataChanceKind
    {
        RangedDodge,
        MeleeResponse,
        MeleeDodge
    }

    public static class RimKataCombatMath
    {
        private static readonly Func<Verb_MeleeAttack, LocalTargetInfo, float> NativeMeleeHitChance =
            AccessTools.MethodDelegate<Func<Verb_MeleeAttack, LocalTargetInfo, float>>(
                AccessTools.Method(typeof(Verb_MeleeAttack), "GetNonMissChance"));
        private static readonly Func<Verb_MeleeAttack, LocalTargetInfo, float> NativeMeleeDodgeChance =
            AccessTools.MethodDelegate<Func<Verb_MeleeAttack, LocalTargetInfo, float>>(
                AccessTools.Method(typeof(Verb_MeleeAttack), "GetDodgeChance"));
        private static readonly AccessTools.FieldRef<Verb, bool> MeleeSurpriseAttack =
            AccessTools.FieldRefAccess<Verb, bool>("surpriseAttack");
        private static readonly AccessTools.FieldRef<Pawn_MeleeVerbs, Verb> SelectedMeleeVerb =
            AccessTools.FieldRefAccess<Pawn_MeleeVerbs, Verb>("curMeleeVerb");

        [ThreadStatic]
        private static Pawn verifiedMeleeDodgeTarget;

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
                }
            }

            return Mathf.Clamp01(baseChance * multiplier);
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
            Pawn pawn = target.Pawn;
            return pawn != null && ReferenceEquals(pawn, verifiedMeleeDodgeTarget)
                ? ApplyConfiguredMeleeDodgeBonus(pawn, vanillaChance)
                : AddConfiguredMeleeDodgeBonus(pawn, vanillaChance);
        }

        public static float MeleeParryChance(Pawn defender, Pawn attacker)
        {
            if (defender == null || attacker == null)
            {
                return 0f;
            }

            float chance = ReadMeleeProbability(
                ResolveMeleeProbabilityVerb(defender),
                attacker,
                NativeMeleeHitChance);

            RimKataSettings settings = RimKataMod.Settings;
            chance *= settings?.GetMeleeResponseBonusMultiplier(defender) ?? 1f;
            if (settings != null && RimKataSerumUtility.IsMindNumbed(defender))
            {
                chance *= settings.GetSerumResponseMultiplier(defender);
            }

            return Mathf.Clamp01(chance);
        }

        internal static float CloseMeleeDodgeChanceVerified(Pawn target)
        {
            return CloseMeleeDodgeChanceCore(target);
        }

        private static float CloseMeleeDodgeChanceCore(Pawn target)
        {
            if (target == null)
            {
                return 0f;
            }

            Pawn previousVerifiedTarget = verifiedMeleeDodgeTarget;
            verifiedMeleeDodgeTarget = target;
            try
            {
                // GetDodgeChance already includes the existing vanilla dodge bonus hook.
                return ReadMeleeProbability(
                    ResolveMeleeProbabilityVerb(target),
                    target,
                    NativeMeleeDodgeChance);
            }
            finally
            {
                verifiedMeleeDodgeTarget = previousVerifiedTarget;
            }
        }

        private static Verb_MeleeAttack ResolveMeleeProbabilityVerb(Pawn pawn)
        {
            // Read an owned instance without choosing an attack, checking weapon permissions,
            // or consuming random numbers. The native getters do not use its tool or weapon.
            if (pawn?.meleeVerbs != null
                && SelectedMeleeVerb(pawn.meleeVerbs) is Verb_MeleeAttack selected
                && selected.CasterPawn == pawn)
            {
                return selected;
            }

            var verbs = pawn?.verbTracker?.AllVerbs;
            if (verbs != null)
            {
                for (int i = 0; i < verbs.Count; i++)
                {
                    if (verbs[i] is Verb_MeleeAttack melee && melee.CasterPawn == pawn)
                    {
                        return melee;
                    }
                }
            }

            return null;
        }

        private static float ReadMeleeProbability(
            Verb_MeleeAttack verb,
            Pawn target,
            Func<Verb_MeleeAttack, LocalTargetInfo, float> calculator)
        {
            if (verb == null)
            {
                return 0f;
            }

            // Parry and RimKata close gunfire are non-surprise actions. Borrow only the
            // probability getter; restore this instance's prior attack context even on failure.
            bool previousSurpriseAttack = MeleeSurpriseAttack(verb);
            MeleeSurpriseAttack(verb) = false;
            try
            {
                return calculator(verb, target);
            }
            finally
            {
                MeleeSurpriseAttack(verb) = previousSurpriseAttack;
            }
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
