using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace KRWF.RimKata
{
    public enum RimKataCandidateRangeMode
    {
        Short,
        Medium,
        Long,
        Unlimited,
        Custom
    }

    public enum RimKataCreepJoinerGeneChoice
    {
        MindNumbSerumDependency,
        RimKata
    }

    public sealed class RimKataSettingsProfile : IExposable
    {
        private static readonly FieldInfo[] ProfileFields = typeof(RimKataSettings)
            .GetFields(BindingFlags.Instance | BindingFlags.Public)
            .Where(field => field.DeclaringType == typeof(RimKataSettings)
                && IsSupportedType(field.FieldType))
            .OrderBy(field => field.Name, StringComparer.Ordinal)
            .ToArray();
        private static readonly Dictionary<string, FieldInfo> ProfileFieldsByName =
            ProfileFields.ToDictionary(field => field.Name, StringComparer.Ordinal);

        private List<string> entries = new List<string>();

        public static RimKataSettingsProfile Capture(RimKataSettings settings)
        {
            RimKataSettingsProfile profile = new RimKataSettingsProfile();
            if (settings == null)
            {
                return profile;
            }

            for (int i = 0; i < ProfileFields.Length; i++)
            {
                FieldInfo field = ProfileFields[i];
                profile.entries.Add(field.Name + "=" + Serialize(field.GetValue(settings), field.FieldType));
            }

            return profile;
        }

        public void ApplyTo(RimKataSettings settings)
        {
            if (settings == null || entries == null)
            {
                return;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                string entry = entries[i];
                int separator = entry?.IndexOf('=') ?? -1;
                if (separator <= 0
                    || !ProfileFieldsByName.TryGetValue(entry.Substring(0, separator), out FieldInfo field)
                    || !TryDeserialize(entry.Substring(separator + 1), field.FieldType, out object value))
                {
                    continue;
                }

                field.SetValue(settings, value);
            }
        }

        public void FillMissingFrom(RimKataSettingsProfile defaults)
        {
            entries ??= new List<string>();
            if (defaults?.entries == null)
            {
                return;
            }

            HashSet<string> existingNames = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < entries.Count; i++)
            {
                int separator = entries[i]?.IndexOf('=') ?? -1;
                if (separator > 0)
                {
                    existingNames.Add(entries[i].Substring(0, separator));
                }
            }

            for (int i = 0; i < defaults.entries.Count; i++)
            {
                string entry = defaults.entries[i];
                int separator = entry?.IndexOf('=') ?? -1;
                if (separator > 0 && existingNames.Add(entry.Substring(0, separator)))
                {
                    entries.Add(entry);
                }
            }
        }

        public void ExposeData()
        {
            Scribe_Collections.Look(ref entries, "entries", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && entries == null)
            {
                entries = new List<string>();
            }
        }

        private static bool IsSupportedType(Type type)
        {
            return type == typeof(float)
                || type == typeof(int)
                || type == typeof(bool)
                || type.IsEnum;
        }

        private static string Serialize(object value, Type type)
        {
            if (type == typeof(float))
            {
                return ((float)value).ToString("R", CultureInfo.InvariantCulture);
            }

            if (type == typeof(bool))
            {
                return (bool)value ? "1" : "0";
            }

            if (type.IsEnum)
            {
                return Convert.ToInt32(value, CultureInfo.InvariantCulture)
                    .ToString(CultureInfo.InvariantCulture);
            }

            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static bool TryDeserialize(string text, Type type, out object value)
        {
            if (type == typeof(float)
                && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float floatValue))
            {
                value = floatValue;
                return true;
            }

            if (type == typeof(int)
                && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intValue))
            {
                value = intValue;
                return true;
            }

            if (type == typeof(bool) && (text == "0" || text == "1"))
            {
                value = text == "1";
                return true;
            }

            if (type.IsEnum
                && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int enumValue)
                && Enum.IsDefined(type, enumValue))
            {
                value = Enum.ToObject(type, enumValue);
                return true;
            }

            value = null;
            return false;
        }
    }

    public sealed class RimKataSettings : ModSettings
    {
        private const int CurrentSettingsModelVersion = 1;
        private int settingsModelVersion = CurrentSettingsModelVersion;
        private bool opProfileActive;
        private RimKataSettingsProfile opProfile;
        private RimKataSettingsProfile normalProfileBackup;
        private const float LegacyRangedDodgeChancePercent = 25f;
        private const float LegacyMeleeResponseChancePercent = 25f;
        private const float LegacyMeleeDodgeChancePercent = 25f;
        private const float LegacyInterceptionChancePercent = 30f;
        private const float LegacyInterceptionCriticalChancePercent = 10f;
        private const float LegacyMovingAccuracyMultiplierPercent = 100f;
        private const float LegacyArmorCooldownReductionPercent = 50f;
        private const int LegacyRangedDodgeDurationTicks = 30;
        private const float LegacyResponseAccidentalFireChancePercent = 20f;
        private const float LegacyResponseCooldownReductionPercent = 70f;
        private const float LegacySerumMultiplierPercent = 100f;

        public const float DefaultRangedDodgeChancePercent = 85f;
        public const float DefaultRangedDodgeChanceGrowthPerLevelPercent = 3f;
        public const float DefaultRangedDodgeChanceMinimumPercent = 30f;
        public const bool DefaultRangedDodgeChanceFixed = false;

        public const float DefaultMeleeResponseChancePercent = 85f;
        public const float DefaultMeleeResponseChanceGrowthPerLevelPercent = 3f;
        public const float DefaultMeleeResponseChanceMinimumPercent = 30f;
        public const bool DefaultMeleeResponseChanceFixed = false;

        public const float DefaultMeleeDodgeChancePercent = 40f;
        public const float DefaultMeleeDodgeChanceGrowthPerLevelPercent = 3f;
        public const float DefaultMeleeDodgeChanceMinimumPercent = 0f;
        public const bool DefaultMeleeDodgeChanceFixed = false;

        public const float DefaultInterceptionChancePercent = 50f;
        public const float DefaultInterceptionChanceGrowthPerLevelPercent = 3f;
        public const float DefaultInterceptionChanceMinimumPercent = 10f;
        public const bool DefaultInterceptionChanceFixed = false;

        public const float DefaultInterceptionCriticalChancePercent = 70f;
        public const float DefaultInterceptionCriticalChanceGrowthPerLevelPercent = 3f;
        public const float DefaultInterceptionCriticalChanceMinimumPercent = 30f;
        public const bool DefaultInterceptionCriticalChanceFixed = false;

        public const float DefaultMovingAccuracyMultiplierPercent = 90f;
        public const float DefaultMovingAccuracyMultiplierGrowthPerLevelPercent = 3f;
        public const float DefaultMovingAccuracyMultiplierMinimumPercent = 40f;
        public const bool DefaultMovingAccuracyMultiplierFixed = false;

        public const float DefaultArmorCooldownReductionPercent = 30f;
        public const float DefaultArmorCooldownReductionGrowthPerLevelPercent = 2f;
        public const float DefaultArmorCooldownReductionMinimumPercent = 10f;
        public const bool DefaultArmorCooldownReductionFixed = false;

        public const int DefaultRangedDodgeDurationTicks = 30;
        public const float DefaultRangedDodgeDurationGrowthPerLevelTicks = 1f;
        public const int DefaultRangedDodgeDurationBaseTicks = 40;
        public const bool DefaultRangedDodgeDurationFixed = true;
        public const int MinimumRangedDodgeDurationTicks = 1;
        public const int MaximumRangedDodgeDurationTicks = 600;

        public const RimKataCandidateRangeMode DefaultCandidateRangeMode = RimKataCandidateRangeMode.Short;
        public const float DefaultCustomCandidateRange = 0f;
        public const float MinimumCustomCandidateRange = 1f;
        public const float MaximumCustomCandidateRange = 999f;
        public const float DefaultResponseWeaponDurabilityLossChancePercent = 0f;
        public const int DefaultResponseWeaponDurabilityLossAmount = 1;
        public const int MinimumResponseWeaponDurabilityLossAmount = 1;
        public const int MaximumResponseWeaponDurabilityLossAmount = 999;
        public const float DefaultCreepJoinerDependencyGeneChancePercent = 0f;
        public const RimKataCreepJoinerGeneChoice DefaultCreepJoinerGeneChoice = RimKataCreepJoinerGeneChoice.RimKata;
        public const float DefaultAiSecondaryWeaponChancePercent = 0f;

        public const float DefaultResponseDisarmChancePercent = 3f;
        public const float DefaultResponseDisarmChanceGrowthPerLevelPercent = 0.1f;
        public const float DefaultResponseDisarmChanceMinimumPercent = 2f;
        public const bool DefaultResponseDisarmChanceFixed = false;

        public const float DefaultResponseAccidentalFireChancePercent = 20f;
        public const float DefaultResponseAccidentalFireChanceGrowthPerLevelPercent = 2f;
        public const float DefaultResponseAccidentalFireChanceMinimumPercent = 20f;
        public const bool DefaultResponseAccidentalFireChanceFixed = true;

        public const float DefaultResponseCooldownReductionPercent = 70f;
        public const float DefaultResponseCooldownReductionGrowthPerLevelPercent = 3f;
        public const float DefaultResponseCooldownReductionMinimumPercent = 20f;
        public const bool DefaultResponseCooldownReductionFixed = false;

        public const float DefaultSerumDodgeMultiplierPercent = 120f;
        public const float DefaultSerumResponseMultiplierPercent = 120f;
        public const float DefaultSerumInterceptionMultiplierPercent = 120f;
        public const float DefaultSerumMultiplierPercent = DefaultSerumDodgeMultiplierPercent;
        public const float DefaultSerumDodgeMultiplierGrowthPerLevelPercent = 3f;
        public const float DefaultSerumDodgeMultiplierMinimumPercent = 100f;
        public const bool DefaultSerumDodgeMultiplierFixed = true;
        public const float DefaultSerumResponseMultiplierGrowthPerLevelPercent = 3f;
        public const float DefaultSerumResponseMultiplierMinimumPercent = 100f;
        public const bool DefaultSerumResponseMultiplierFixed = true;
        public const float DefaultSerumInterceptionMultiplierGrowthPerLevelPercent = 3f;
        public const float DefaultSerumInterceptionMultiplierMinimumPercent = 100f;
        public const bool DefaultSerumInterceptionMultiplierFixed = true;

        public const bool DefaultSecondaryWeaponEnabled = true;
        public const bool DefaultSingleShotConversionEnabled = true;
        public const bool DefaultRandomAttackEnabled = true;
        public const bool DefaultExplosiveInterceptionEnabled = true;
        public const bool DefaultMovingFireEnabled = true;
        public const bool DefaultCloseFireEnabled = true;
        public const bool DefaultTargetRushEnabled = true;
        public const bool DefaultResponseEnabled = true;
        public const bool DefaultRangedDodgeEnabled = true;
        public const bool DefaultTumbleEnabled = true;
        public const bool DefaultAccessRestrictionsDisabled = false;

        public static readonly string[] DefaultEnabledWeaponDefNames =
        {
            "Gun_Revolver",
            "Gun_Autopistol",
            "Gun_MachinePistol",
            "Gun_Revolver_Unique"
        };

        public static readonly string[] DefaultEnabledArmorDefNames =
        {
            "Apparel_CollarShirt",
            "Apparel_ShirtRuffle",
            "Apparel_PsyfocusShirt"
        };

        public float rangedDodgeChancePercent = DefaultRangedDodgeChancePercent;
        public float rangedDodgeChanceGrowthPerLevelPercent = DefaultRangedDodgeChanceGrowthPerLevelPercent;
        public float rangedDodgeChanceMinimumPercent = DefaultRangedDodgeChanceMinimumPercent;
        public bool rangedDodgeChanceFixed = DefaultRangedDodgeChanceFixed;

        public float meleeResponseChancePercent = DefaultMeleeResponseChancePercent;
        public float meleeResponseChanceGrowthPerLevelPercent = DefaultMeleeResponseChanceGrowthPerLevelPercent;
        public float meleeResponseChanceMinimumPercent = DefaultMeleeResponseChanceMinimumPercent;
        public bool meleeResponseChanceFixed = DefaultMeleeResponseChanceFixed;

        public float meleeDodgeChancePercent = DefaultMeleeDodgeChancePercent;
        public float meleeDodgeChanceGrowthPerLevelPercent = DefaultMeleeDodgeChanceGrowthPerLevelPercent;
        public float meleeDodgeChanceMinimumPercent = DefaultMeleeDodgeChanceMinimumPercent;
        public bool meleeDodgeChanceFixed = DefaultMeleeDodgeChanceFixed;

        public float interceptionChancePercent = DefaultInterceptionChancePercent;
        public float interceptionChanceGrowthPerLevelPercent = DefaultInterceptionChanceGrowthPerLevelPercent;
        public float interceptionChanceMinimumPercent = DefaultInterceptionChanceMinimumPercent;
        public bool interceptionChanceFixed = DefaultInterceptionChanceFixed;

        public float interceptionCriticalChancePercent = DefaultInterceptionCriticalChancePercent;
        public float interceptionCriticalChanceGrowthPerLevelPercent = DefaultInterceptionCriticalChanceGrowthPerLevelPercent;
        public float interceptionCriticalChanceMinimumPercent = DefaultInterceptionCriticalChanceMinimumPercent;
        public bool interceptionCriticalChanceFixed = DefaultInterceptionCriticalChanceFixed;

        public float movingAccuracyMultiplierPercent = DefaultMovingAccuracyMultiplierPercent;
        public float movingAccuracyMultiplierGrowthPerLevelPercent = DefaultMovingAccuracyMultiplierGrowthPerLevelPercent;
        public float movingAccuracyMultiplierMinimumPercent = DefaultMovingAccuracyMultiplierMinimumPercent;
        public bool movingAccuracyMultiplierFixed = DefaultMovingAccuracyMultiplierFixed;

        public float armorCooldownReductionPercent = DefaultArmorCooldownReductionPercent;
        public float armorCooldownReductionGrowthPerLevelPercent = DefaultArmorCooldownReductionGrowthPerLevelPercent;
        public float armorCooldownReductionMinimumPercent = DefaultArmorCooldownReductionMinimumPercent;
        public bool armorCooldownReductionFixed = DefaultArmorCooldownReductionFixed;

        public int rangedDodgeDurationTicks = DefaultRangedDodgeDurationTicks;
        public float rangedDodgeDurationGrowthPerLevelTicks = DefaultRangedDodgeDurationGrowthPerLevelTicks;
        public int rangedDodgeDurationBaseTicks = DefaultRangedDodgeDurationBaseTicks;
        public bool rangedDodgeDurationFixed = DefaultRangedDodgeDurationFixed;

        public RimKataCandidateRangeMode candidateRangeMode = DefaultCandidateRangeMode;
        public float customCandidateRange = DefaultCustomCandidateRange;
        public float responseWeaponDurabilityLossChancePercent = DefaultResponseWeaponDurabilityLossChancePercent;
        public int responseWeaponDurabilityLossAmount = DefaultResponseWeaponDurabilityLossAmount;
        public float creepJoinerDependencyGeneChancePercent = DefaultCreepJoinerDependencyGeneChancePercent;
        public RimKataCreepJoinerGeneChoice creepJoinerGeneChoice = DefaultCreepJoinerGeneChoice;
        public float aiSecondaryWeaponChancePercent = DefaultAiSecondaryWeaponChancePercent;

        public float responseDisarmChancePercent = DefaultResponseDisarmChancePercent;
        public float responseDisarmChanceGrowthPerLevelPercent = DefaultResponseDisarmChanceGrowthPerLevelPercent;
        public float responseDisarmChanceMinimumPercent = DefaultResponseDisarmChanceMinimumPercent;
        public bool responseDisarmChanceFixed = DefaultResponseDisarmChanceFixed;

        public float responseAccidentalFireChancePercent = DefaultResponseAccidentalFireChancePercent;
        public float responseAccidentalFireChanceGrowthPerLevelPercent = DefaultResponseAccidentalFireChanceGrowthPerLevelPercent;
        public float responseAccidentalFireChanceMinimumPercent = DefaultResponseAccidentalFireChanceMinimumPercent;
        public bool responseAccidentalFireChanceFixed = DefaultResponseAccidentalFireChanceFixed;

        public float responseCooldownReductionPercent = DefaultResponseCooldownReductionPercent;
        public float responseCooldownReductionGrowthPerLevelPercent = DefaultResponseCooldownReductionGrowthPerLevelPercent;
        public float responseCooldownReductionMinimumPercent = DefaultResponseCooldownReductionMinimumPercent;
        public bool responseCooldownReductionFixed = DefaultResponseCooldownReductionFixed;

        public float serumDodgeMultiplierPercent = DefaultSerumDodgeMultiplierPercent;
        public float serumDodgeMultiplierGrowthPerLevelPercent = DefaultSerumDodgeMultiplierGrowthPerLevelPercent;
        public float serumDodgeMultiplierMinimumPercent = DefaultSerumDodgeMultiplierMinimumPercent;
        public bool serumDodgeMultiplierFixed = DefaultSerumDodgeMultiplierFixed;

        public float serumResponseMultiplierPercent = DefaultSerumResponseMultiplierPercent;
        public float serumResponseMultiplierGrowthPerLevelPercent = DefaultSerumResponseMultiplierGrowthPerLevelPercent;
        public float serumResponseMultiplierMinimumPercent = DefaultSerumResponseMultiplierMinimumPercent;
        public bool serumResponseMultiplierFixed = DefaultSerumResponseMultiplierFixed;

        public float serumInterceptionMultiplierPercent = DefaultSerumInterceptionMultiplierPercent;
        public float serumInterceptionMultiplierGrowthPerLevelPercent = DefaultSerumInterceptionMultiplierGrowthPerLevelPercent;
        public float serumInterceptionMultiplierMinimumPercent = DefaultSerumInterceptionMultiplierMinimumPercent;
        public bool serumInterceptionMultiplierFixed = DefaultSerumInterceptionMultiplierFixed;

        public bool secondaryWeaponEnabled = DefaultSecondaryWeaponEnabled;
        public bool singleShotConversionEnabled = DefaultSingleShotConversionEnabled;
        public bool randomAttackEnabled = DefaultRandomAttackEnabled;
        public bool explosiveInterceptionEnabled = DefaultExplosiveInterceptionEnabled;
        public bool movingFireEnabled = DefaultMovingFireEnabled;
        public bool closeFireEnabled = DefaultCloseFireEnabled;
        public bool targetRushEnabled = DefaultTargetRushEnabled;
        public bool responseEnabled = DefaultResponseEnabled;
        public bool rangedDodgeEnabled = DefaultRangedDodgeEnabled;
        public bool tumbleEnabled = DefaultTumbleEnabled;
        public bool accessRestrictionsDisabled = DefaultAccessRestrictionsDisabled;
        public bool enableFriendlyPawnEffects = true;
        public bool enableHostilePawnEffects = true;

        public List<string> enabledWeaponDefNames = DefaultEnabledWeaponDefNames.ToList();
        public List<string> enabledArmorDefNames = DefaultEnabledArmorDefNames.ToList();
        public List<string> twoHandWeaponDefNames = new List<string>();
        public List<string> oneHandWeaponOverrideDefNames = new List<string>();

        public float RangedDodgeChance => ChanceFromPercent(rangedDodgeChancePercent);
        public float MeleeResponseChance => ChanceFromPercent(meleeResponseChancePercent);
        public float MeleeDodgeChance => ChanceFromPercent(meleeDodgeChancePercent);
        public float InterceptionChance => ChanceFromPercent(interceptionChancePercent);
        public float InterceptionCriticalChance => ChanceFromPercent(interceptionCriticalChancePercent);
        public float MovingAccuracyMultiplier => MultiplierFromPercent(movingAccuracyMultiplierPercent);
        public float ArmorCooldownFactor => 1f - ChanceFromPercent(armorCooldownReductionPercent);
        public float ResponseAccidentalFireChance => ChanceFromPercent(responseAccidentalFireChancePercent);
        public float ResponseCooldownFactor => 1f - ChanceFromPercent(responseCooldownReductionPercent);
        public float SerumDodgeMultiplier => MultiplierFromPercent(serumDodgeMultiplierPercent);
        public float SerumResponseMultiplier => MultiplierFromPercent(serumResponseMultiplierPercent);
        public float SerumInterceptionMultiplier => MultiplierFromPercent(serumInterceptionMultiplierPercent);

        public int GetRangedDodgeDurationTicks(Pawn pawn)
        {
            if (rangedDodgeDurationFixed)
            {
                return Mathf.Clamp(rangedDodgeDurationTicks, MinimumRangedDodgeDurationTicks, MaximumRangedDodgeDurationTicks);
            }

            return Mathf.Clamp(
                Mathf.RoundToInt(
                    rangedDodgeDurationBaseTicks
                    - rangedDodgeDurationGrowthPerLevelTicks
                    * SkillLevel(pawn, SkillDefOf.Melee)),
                MinimumRangedDodgeDurationTicks,
                MaximumRangedDodgeDurationTicks);
        }

        public float GetResponseAccidentalFireChance(Pawn pawn) => ChanceFromPercent(ResolvePercent(
            pawn, responseAccidentalFireChanceFixed, responseAccidentalFireChancePercent,
            responseAccidentalFireChanceMinimumPercent, responseAccidentalFireChanceGrowthPerLevelPercent, SkillDefOf.Melee));
        public float GetRangedDodgeChance(Pawn pawn) => ChanceFromPercent(ResolvePercent(
            pawn, rangedDodgeChanceFixed, rangedDodgeChancePercent,
            rangedDodgeChanceMinimumPercent, rangedDodgeChanceGrowthPerLevelPercent, SkillDefOf.Melee));
        public float GetMeleeResponseChance(Pawn pawn) => ChanceFromPercent(ResolvePercent(
            pawn, meleeResponseChanceFixed, meleeResponseChancePercent,
            meleeResponseChanceMinimumPercent, meleeResponseChanceGrowthPerLevelPercent, SkillDefOf.Melee));
        public float GetResponseDisarmChance(Pawn pawn) => ChanceFromPercent(ResolvePercent(
            pawn, responseDisarmChanceFixed, responseDisarmChancePercent,
            responseDisarmChanceMinimumPercent, responseDisarmChanceGrowthPerLevelPercent, SkillDefOf.Melee));
        public float GetMeleeDodgeChance(Pawn pawn) => ChanceFromPercent(ResolvePercent(
            pawn, meleeDodgeChanceFixed, meleeDodgeChancePercent,
            meleeDodgeChanceMinimumPercent, meleeDodgeChanceGrowthPerLevelPercent, SkillDefOf.Melee));
        public float GetInterceptionChance(Pawn pawn) => ChanceFromPercent(ResolvePercent(
            pawn, interceptionChanceFixed, interceptionChancePercent,
            interceptionChanceMinimumPercent, interceptionChanceGrowthPerLevelPercent, SkillDefOf.Shooting));
        public float GetInterceptionCriticalChance(Pawn pawn) => ChanceFromPercent(ResolvePercent(
            pawn, interceptionCriticalChanceFixed, interceptionCriticalChancePercent,
            interceptionCriticalChanceMinimumPercent, interceptionCriticalChanceGrowthPerLevelPercent, SkillDefOf.Shooting));
        public float GetMovingAccuracyMultiplier(Pawn pawn) => MultiplierFromPercent(ResolvePercent(
            pawn, movingAccuracyMultiplierFixed, movingAccuracyMultiplierPercent,
            movingAccuracyMultiplierMinimumPercent, movingAccuracyMultiplierGrowthPerLevelPercent, SkillDefOf.Shooting));
        public float GetArmorCooldownFactor(Pawn pawn) => 1f - ChanceFromPercent(ResolvePercent(
            pawn, armorCooldownReductionFixed, armorCooldownReductionPercent,
            armorCooldownReductionMinimumPercent, armorCooldownReductionGrowthPerLevelPercent, SkillDefOf.Shooting));
        public float GetResponseCooldownFactor(Pawn pawn) => 1f - ChanceFromPercent(ResolvePercent(
            pawn, responseCooldownReductionFixed, responseCooldownReductionPercent,
            responseCooldownReductionMinimumPercent, responseCooldownReductionGrowthPerLevelPercent, SkillDefOf.Melee));
        public float GetSerumDodgeMultiplier(Pawn pawn) => MultiplierFromPercent(ResolvePercent(
            pawn, serumDodgeMultiplierFixed, serumDodgeMultiplierPercent,
            serumDodgeMultiplierMinimumPercent, serumDodgeMultiplierGrowthPerLevelPercent, SkillDefOf.Melee));
        public float GetSerumResponseMultiplier(Pawn pawn) => MultiplierFromPercent(ResolvePercent(
            pawn, serumResponseMultiplierFixed, serumResponseMultiplierPercent,
            serumResponseMultiplierMinimumPercent, serumResponseMultiplierGrowthPerLevelPercent, SkillDefOf.Melee));
        public float GetSerumInterceptionMultiplier(Pawn pawn) => MultiplierFromPercent(ResolvePercent(
            pawn, serumInterceptionMultiplierFixed, serumInterceptionMultiplierPercent,
            serumInterceptionMultiplierMinimumPercent, serumInterceptionMultiplierGrowthPerLevelPercent, SkillDefOf.Melee));
        public float ResponseWeaponDurabilityLossChance => ChanceFromPercent(responseWeaponDurabilityLossChancePercent);
        public float CreepJoinerDependencyGeneChance => ChanceFromPercent(creepJoinerDependencyGeneChancePercent);
        public float AiSecondaryWeaponChance => ChanceFromPercent(aiSecondaryWeaponChancePercent);
        public bool OpProfileActive => opProfileActive;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref settingsModelVersion, "settingsModelVersion", 0);
            Scribe_Values.Look(ref opProfileActive, "opProfileActive", false);
            Scribe_Deep.Look(ref opProfile, "opProfile");
            Scribe_Deep.Look(ref normalProfileBackup, "normalProfileBackup");
            bool migrateLegacyScalarModel = Scribe.mode == LoadSaveMode.LoadingVars
                && settingsModelVersion < CurrentSettingsModelVersion;

            Scribe_Values.Look(ref rangedDodgeChancePercent, "rangedDodgeChancePercent", migrateLegacyScalarModel ? LegacyRangedDodgeChancePercent : DefaultRangedDodgeChancePercent);
            Scribe_Values.Look(ref rangedDodgeChanceGrowthPerLevelPercent, "rangedDodgeChanceGrowthPerLevelPercent", DefaultRangedDodgeChanceGrowthPerLevelPercent);
            Scribe_Values.Look(ref rangedDodgeChanceMinimumPercent, "rangedDodgeChanceMinimumPercent", DefaultRangedDodgeChanceMinimumPercent);
            LookFixedMode(ref rangedDodgeChanceFixed, "rangedDodgeChanceFixed", DefaultRangedDodgeChanceFixed, "rangedDodgeChancePercent");
            LookRenamedFloat(ref meleeResponseChancePercent, "meleeResponseChancePercent", "meleeCounterChancePercent", migrateLegacyScalarModel ? LegacyMeleeResponseChancePercent : DefaultMeleeResponseChancePercent);
            Scribe_Values.Look(ref meleeResponseChanceGrowthPerLevelPercent, "meleeResponseChanceGrowthPerLevelPercent", DefaultMeleeResponseChanceGrowthPerLevelPercent);
            Scribe_Values.Look(ref meleeResponseChanceMinimumPercent, "meleeResponseChanceMinimumPercent", DefaultMeleeResponseChanceMinimumPercent);
            LookFixedMode(ref meleeResponseChanceFixed, "meleeResponseChanceFixed", DefaultMeleeResponseChanceFixed, "meleeResponseChancePercent", "meleeCounterChancePercent");
            Scribe_Values.Look(ref meleeDodgeChancePercent, "meleeDodgeChancePercent", migrateLegacyScalarModel ? LegacyMeleeDodgeChancePercent : DefaultMeleeDodgeChancePercent);
            Scribe_Values.Look(ref meleeDodgeChanceGrowthPerLevelPercent, "meleeDodgeChanceGrowthPerLevelPercent", DefaultMeleeDodgeChanceGrowthPerLevelPercent);
            Scribe_Values.Look(ref meleeDodgeChanceMinimumPercent, "meleeDodgeChanceMinimumPercent", DefaultMeleeDodgeChanceMinimumPercent);
            LookFixedMode(ref meleeDodgeChanceFixed, "meleeDodgeChanceFixed", DefaultMeleeDodgeChanceFixed, "meleeDodgeChancePercent");
            Scribe_Values.Look(ref interceptionChancePercent, "interceptionChancePercent", migrateLegacyScalarModel ? LegacyInterceptionChancePercent : DefaultInterceptionChancePercent);
            Scribe_Values.Look(ref interceptionChanceGrowthPerLevelPercent, "interceptionChanceGrowthPerLevelPercent", DefaultInterceptionChanceGrowthPerLevelPercent);
            Scribe_Values.Look(ref interceptionChanceMinimumPercent, "interceptionChanceMinimumPercent", DefaultInterceptionChanceMinimumPercent);
            LookFixedMode(ref interceptionChanceFixed, "interceptionChanceFixed", DefaultInterceptionChanceFixed, "interceptionChancePercent");
            Scribe_Values.Look(ref interceptionCriticalChancePercent, "interceptionCriticalChancePercent", migrateLegacyScalarModel ? LegacyInterceptionCriticalChancePercent : DefaultInterceptionCriticalChancePercent);
            Scribe_Values.Look(ref interceptionCriticalChanceGrowthPerLevelPercent, "interceptionCriticalChanceGrowthPerLevelPercent", DefaultInterceptionCriticalChanceGrowthPerLevelPercent);
            Scribe_Values.Look(ref interceptionCriticalChanceMinimumPercent, "interceptionCriticalChanceMinimumPercent", DefaultInterceptionCriticalChanceMinimumPercent);
            LookFixedMode(ref interceptionCriticalChanceFixed, "interceptionCriticalChanceFixed", DefaultInterceptionCriticalChanceFixed, "interceptionCriticalChancePercent");
            Scribe_Values.Look(ref movingAccuracyMultiplierPercent, "movingAccuracyMultiplierPercent", migrateLegacyScalarModel ? LegacyMovingAccuracyMultiplierPercent : DefaultMovingAccuracyMultiplierPercent);
            Scribe_Values.Look(ref movingAccuracyMultiplierGrowthPerLevelPercent, "movingAccuracyMultiplierGrowthPerLevelPercent", DefaultMovingAccuracyMultiplierGrowthPerLevelPercent);
            Scribe_Values.Look(ref movingAccuracyMultiplierMinimumPercent, "movingAccuracyMultiplierMinimumPercent", DefaultMovingAccuracyMultiplierMinimumPercent);
            LookFixedMode(ref movingAccuracyMultiplierFixed, "movingAccuracyMultiplierFixed", DefaultMovingAccuracyMultiplierFixed, "movingAccuracyMultiplierPercent");
            Scribe_Values.Look(ref armorCooldownReductionPercent, "armorCooldownReductionPercent", migrateLegacyScalarModel ? LegacyArmorCooldownReductionPercent : DefaultArmorCooldownReductionPercent);
            Scribe_Values.Look(ref armorCooldownReductionGrowthPerLevelPercent, "armorCooldownReductionGrowthPerLevelPercent", DefaultArmorCooldownReductionGrowthPerLevelPercent);
            Scribe_Values.Look(ref armorCooldownReductionMinimumPercent, "armorCooldownReductionMinimumPercent", DefaultArmorCooldownReductionMinimumPercent);
            LookFixedMode(ref armorCooldownReductionFixed, "armorCooldownReductionFixed", DefaultArmorCooldownReductionFixed, "armorCooldownReductionPercent");
            Scribe_Values.Look(ref rangedDodgeDurationTicks, "rangedDodgeDurationTicks", migrateLegacyScalarModel ? LegacyRangedDodgeDurationTicks : DefaultRangedDodgeDurationTicks);
            LookRenamedFloat(ref rangedDodgeDurationGrowthPerLevelTicks, "rangedDodgeDurationGrowthPerLevelTicks", "rangedDodgeDurationGrowthPerLevelPercent", DefaultRangedDodgeDurationGrowthPerLevelTicks);
            Scribe_Values.Look(ref rangedDodgeDurationBaseTicks, "rangedDodgeDurationBaseTicks", DefaultRangedDodgeDurationBaseTicks);
            LookFixedMode(ref rangedDodgeDurationFixed, "rangedDodgeDurationFixed", DefaultRangedDodgeDurationFixed, "rangedDodgeDurationTicks");
            Scribe_Values.Look(ref candidateRangeMode, "candidateRangeMode", DefaultCandidateRangeMode);
            Scribe_Values.Look(ref customCandidateRange, "customCandidateRange", DefaultCustomCandidateRange);
            Scribe_Values.Look(ref responseWeaponDurabilityLossChancePercent, "responseWeaponDurabilityLossChancePercent", DefaultResponseWeaponDurabilityLossChancePercent);
            Scribe_Values.Look(ref responseWeaponDurabilityLossAmount, "responseWeaponDurabilityLossAmount", DefaultResponseWeaponDurabilityLossAmount);
            Scribe_Values.Look(ref creepJoinerDependencyGeneChancePercent, "creepJoinerDependencyGeneChancePercent", DefaultCreepJoinerDependencyGeneChancePercent);
            Scribe_Values.Look(ref creepJoinerGeneChoice, "creepJoinerGeneChoice", DefaultCreepJoinerGeneChoice);
            Scribe_Values.Look(ref aiSecondaryWeaponChancePercent, "aiSecondaryWeaponChancePercent", DefaultAiSecondaryWeaponChancePercent);
            Scribe_Values.Look(ref responseDisarmChancePercent, "responseDisarmChancePercent", DefaultResponseDisarmChancePercent);
            Scribe_Values.Look(ref responseDisarmChanceGrowthPerLevelPercent, "responseDisarmChanceGrowthPerLevelPercent", DefaultResponseDisarmChanceGrowthPerLevelPercent);
            Scribe_Values.Look(ref responseDisarmChanceMinimumPercent, "responseDisarmChanceMinimumPercent", DefaultResponseDisarmChanceMinimumPercent);
            LookFixedMode(ref responseDisarmChanceFixed, "responseDisarmChanceFixed", DefaultResponseDisarmChanceFixed, "responseDisarmChancePercent");
            Scribe_Values.Look(ref responseAccidentalFireChancePercent, "responseAccidentalFireChancePercent", migrateLegacyScalarModel ? LegacyResponseAccidentalFireChancePercent : DefaultResponseAccidentalFireChancePercent);
            Scribe_Values.Look(ref responseAccidentalFireChanceGrowthPerLevelPercent, "responseAccidentalFireChanceGrowthPerLevelPercent", DefaultResponseAccidentalFireChanceGrowthPerLevelPercent);
            Scribe_Values.Look(ref responseAccidentalFireChanceMinimumPercent, "responseAccidentalFireChanceMinimumPercent", DefaultResponseAccidentalFireChanceMinimumPercent);
            LookFixedMode(ref responseAccidentalFireChanceFixed, "responseAccidentalFireChanceFixed", DefaultResponseAccidentalFireChanceFixed, "responseAccidentalFireChancePercent");
            LookRenamedFloat(ref responseCooldownReductionPercent, "responseCooldownReductionPercent", "counterCooldownReductionPercent", migrateLegacyScalarModel ? LegacyResponseCooldownReductionPercent : DefaultResponseCooldownReductionPercent);
            Scribe_Values.Look(ref responseCooldownReductionGrowthPerLevelPercent, "responseCooldownReductionGrowthPerLevelPercent", DefaultResponseCooldownReductionGrowthPerLevelPercent);
            Scribe_Values.Look(ref responseCooldownReductionMinimumPercent, "responseCooldownReductionMinimumPercent", DefaultResponseCooldownReductionMinimumPercent);
            LookFixedMode(ref responseCooldownReductionFixed, "responseCooldownReductionFixed", DefaultResponseCooldownReductionFixed, "responseCooldownReductionPercent", "counterCooldownReductionPercent");
            Scribe_Values.Look(ref serumDodgeMultiplierPercent, "serumDodgeMultiplierPercent", migrateLegacyScalarModel ? LegacySerumMultiplierPercent : DefaultSerumDodgeMultiplierPercent);
            Scribe_Values.Look(ref serumDodgeMultiplierGrowthPerLevelPercent, "serumDodgeMultiplierGrowthPerLevelPercent", DefaultSerumDodgeMultiplierGrowthPerLevelPercent);
            Scribe_Values.Look(ref serumDodgeMultiplierMinimumPercent, "serumDodgeMultiplierMinimumPercent", DefaultSerumDodgeMultiplierMinimumPercent);
            LookFixedMode(ref serumDodgeMultiplierFixed, "serumDodgeMultiplierFixed", DefaultSerumDodgeMultiplierFixed, "serumDodgeMultiplierPercent");
            LookRenamedFloat(ref serumResponseMultiplierPercent, "serumResponseMultiplierPercent", "serumCounterMultiplierPercent", migrateLegacyScalarModel ? LegacySerumMultiplierPercent : DefaultSerumResponseMultiplierPercent);
            Scribe_Values.Look(ref serumResponseMultiplierGrowthPerLevelPercent, "serumResponseMultiplierGrowthPerLevelPercent", DefaultSerumResponseMultiplierGrowthPerLevelPercent);
            Scribe_Values.Look(ref serumResponseMultiplierMinimumPercent, "serumResponseMultiplierMinimumPercent", DefaultSerumResponseMultiplierMinimumPercent);
            LookFixedMode(ref serumResponseMultiplierFixed, "serumResponseMultiplierFixed", DefaultSerumResponseMultiplierFixed, "serumResponseMultiplierPercent", "serumCounterMultiplierPercent");
            Scribe_Values.Look(ref serumInterceptionMultiplierPercent, "serumInterceptionMultiplierPercent", migrateLegacyScalarModel ? LegacySerumMultiplierPercent : DefaultSerumInterceptionMultiplierPercent);
            Scribe_Values.Look(ref serumInterceptionMultiplierGrowthPerLevelPercent, "serumInterceptionMultiplierGrowthPerLevelPercent", DefaultSerumInterceptionMultiplierGrowthPerLevelPercent);
            Scribe_Values.Look(ref serumInterceptionMultiplierMinimumPercent, "serumInterceptionMultiplierMinimumPercent", DefaultSerumInterceptionMultiplierMinimumPercent);
            LookFixedMode(ref serumInterceptionMultiplierFixed, "serumInterceptionMultiplierFixed", DefaultSerumInterceptionMultiplierFixed, "serumInterceptionMultiplierPercent");
            Scribe_Values.Look(ref secondaryWeaponEnabled, "secondaryWeaponEnabled", DefaultSecondaryWeaponEnabled);
            Scribe_Values.Look(ref singleShotConversionEnabled, "singleShotConversionEnabled", DefaultSingleShotConversionEnabled);
            Scribe_Values.Look(ref randomAttackEnabled, "randomAttackEnabled", DefaultRandomAttackEnabled);
            Scribe_Values.Look(ref movingFireEnabled, "movingFireEnabled", DefaultMovingFireEnabled);
            Scribe_Values.Look(ref explosiveInterceptionEnabled, "explosiveInterceptionEnabled", DefaultExplosiveInterceptionEnabled);
            Scribe_Values.Look(ref closeFireEnabled, "closeFireEnabled", DefaultCloseFireEnabled);
            Scribe_Values.Look(ref targetRushEnabled, "targetRushEnabled", DefaultTargetRushEnabled);
            Scribe_Values.Look(ref accessRestrictionsDisabled, "accessRestrictionsDisabled", DefaultAccessRestrictionsDisabled);
            Scribe_Values.Look(ref responseEnabled, "responseEnabled", DefaultResponseEnabled);
            Scribe_Values.Look(ref rangedDodgeEnabled, "rangedDodgeEnabled", DefaultRangedDodgeEnabled);
            Scribe_Values.Look(ref tumbleEnabled, "tumbleEnabled", DefaultTumbleEnabled);
            Scribe_Values.Look(ref enableFriendlyPawnEffects, "enableFriendlyPawnEffects", true);
            Scribe_Values.Look(ref enableHostilePawnEffects, "enableHostilePawnEffects", true);
            Scribe_Collections.Look(ref enabledWeaponDefNames, "enabledWeaponDefNames", LookMode.Value);
            Scribe_Collections.Look(ref enabledArmorDefNames, "enabledArmorDefNames", LookMode.Value);
            Scribe_Collections.Look(ref twoHandWeaponDefNames, "twoHandWeaponDefNames", LookMode.Value);
            Scribe_Collections.Look(ref oneHandWeaponOverrideDefNames, "oneHandWeaponOverrideDefNames", LookMode.Value);

            if (migrateLegacyScalarModel)
            {
                rangedDodgeDurationFixed = true;
                responseAccidentalFireChanceFixed = true;
                rangedDodgeChanceFixed = true;
                meleeResponseChanceFixed = true;
                meleeDodgeChanceFixed = true;
                interceptionChanceFixed = true;
                interceptionCriticalChanceFixed = true;
                movingAccuracyMultiplierFixed = true;
                armorCooldownReductionFixed = true;
                responseCooldownReductionFixed = true;
                serumDodgeMultiplierFixed = true;
                serumResponseMultiplierFixed = true;
                serumInterceptionMultiplierFixed = true;
                settingsModelVersion = CurrentSettingsModelVersion;
            }

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                rangedDodgeDurationTicks = Mathf.Clamp(rangedDodgeDurationTicks, MinimumRangedDodgeDurationTicks, MaximumRangedDodgeDurationTicks);
                rangedDodgeDurationBaseTicks = Mathf.Clamp(rangedDodgeDurationBaseTicks, MinimumRangedDodgeDurationTicks, MaximumRangedDodgeDurationTicks);
                if (!Enum.IsDefined(typeof(RimKataCandidateRangeMode), candidateRangeMode))
                {
                    candidateRangeMode = DefaultCandidateRangeMode;
                }

                if (float.IsNaN(customCandidateRange)
                    || float.IsInfinity(customCandidateRange)
                    || customCandidateRange <= 0f)
                {
                    customCandidateRange = DefaultCustomCandidateRange;
                }
                else
                {
                    customCandidateRange = Mathf.Clamp(customCandidateRange, MinimumCustomCandidateRange, MaximumCustomCandidateRange);
                }

                if (float.IsNaN(responseWeaponDurabilityLossChancePercent)
                    || float.IsInfinity(responseWeaponDurabilityLossChancePercent))
                {
                    responseWeaponDurabilityLossChancePercent = DefaultResponseWeaponDurabilityLossChancePercent;
                }
                else
                {
                    responseWeaponDurabilityLossChancePercent = Mathf.Clamp(responseWeaponDurabilityLossChancePercent, 0f, 100f);
                }
                responseWeaponDurabilityLossAmount = Mathf.Clamp(
                    responseWeaponDurabilityLossAmount,
                    MinimumResponseWeaponDurabilityLossAmount,
                    MaximumResponseWeaponDurabilityLossAmount);
                creepJoinerDependencyGeneChancePercent = SanitizePercent(
                    creepJoinerDependencyGeneChancePercent,
                    DefaultCreepJoinerDependencyGeneChancePercent);
                aiSecondaryWeaponChancePercent = SanitizePercent(
                    aiSecondaryWeaponChancePercent,
                    DefaultAiSecondaryWeaponChancePercent);
                if (!Enum.IsDefined(typeof(RimKataCreepJoinerGeneChoice), creepJoinerGeneChoice))
                {
                    creepJoinerGeneChoice = DefaultCreepJoinerGeneChoice;
                }
                responseDisarmChancePercent = SanitizePercent(
                    responseDisarmChancePercent,
                    DefaultResponseDisarmChancePercent);
                responseDisarmChanceGrowthPerLevelPercent = SanitizeNonNegative(
                    responseDisarmChanceGrowthPerLevelPercent,
                    DefaultResponseDisarmChanceGrowthPerLevelPercent);
                responseDisarmChanceMinimumPercent = SanitizePercent(
                    responseDisarmChanceMinimumPercent,
                    DefaultResponseDisarmChanceMinimumPercent);
                enabledWeaponDefNames = SanitizeDefNames(enabledWeaponDefNames);
                enabledArmorDefNames = SanitizeDefNames(enabledArmorDefNames);
                twoHandWeaponDefNames = SanitizeDefNames(twoHandWeaponDefNames);
                oneHandWeaponOverrideDefNames = SanitizeDefNames(oneHandWeaponOverrideDefNames);

                if (opProfile == null)
                {
                    opProfile = CreateOpProfileDefaults();
                }
                else
                {
                    opProfile.FillMissingFrom(CreateOpProfileDefaults());
                }

                if (!opProfileActive)
                {
                    normalProfileBackup = null;
                }
                else if (normalProfileBackup == null)
                {
                    normalProfileBackup = CreateNormalProfileDefaults();
                }
                else
                {
                    normalProfileBackup.FillMissingFrom(CreateNormalProfileDefaults());
                }
            }
        }

        public void ResetNumericDefaults()
        {
            rangedDodgeChancePercent = DefaultRangedDodgeChancePercent;
            rangedDodgeChanceGrowthPerLevelPercent = DefaultRangedDodgeChanceGrowthPerLevelPercent;
            rangedDodgeChanceMinimumPercent = DefaultRangedDodgeChanceMinimumPercent;
            rangedDodgeChanceFixed = DefaultRangedDodgeChanceFixed;
            meleeResponseChancePercent = DefaultMeleeResponseChancePercent;
            meleeResponseChanceGrowthPerLevelPercent = DefaultMeleeResponseChanceGrowthPerLevelPercent;
            meleeResponseChanceMinimumPercent = DefaultMeleeResponseChanceMinimumPercent;
            meleeResponseChanceFixed = DefaultMeleeResponseChanceFixed;
            meleeDodgeChancePercent = DefaultMeleeDodgeChancePercent;
            meleeDodgeChanceGrowthPerLevelPercent = DefaultMeleeDodgeChanceGrowthPerLevelPercent;
            meleeDodgeChanceMinimumPercent = DefaultMeleeDodgeChanceMinimumPercent;
            meleeDodgeChanceFixed = DefaultMeleeDodgeChanceFixed;
            interceptionChancePercent = DefaultInterceptionChancePercent;
            interceptionChanceGrowthPerLevelPercent = DefaultInterceptionChanceGrowthPerLevelPercent;
            interceptionChanceMinimumPercent = DefaultInterceptionChanceMinimumPercent;
            interceptionChanceFixed = DefaultInterceptionChanceFixed;
            interceptionCriticalChancePercent = DefaultInterceptionCriticalChancePercent;
            interceptionCriticalChanceGrowthPerLevelPercent = DefaultInterceptionCriticalChanceGrowthPerLevelPercent;
            interceptionCriticalChanceMinimumPercent = DefaultInterceptionCriticalChanceMinimumPercent;
            interceptionCriticalChanceFixed = DefaultInterceptionCriticalChanceFixed;
            movingAccuracyMultiplierPercent = DefaultMovingAccuracyMultiplierPercent;
            movingAccuracyMultiplierGrowthPerLevelPercent = DefaultMovingAccuracyMultiplierGrowthPerLevelPercent;
            movingAccuracyMultiplierMinimumPercent = DefaultMovingAccuracyMultiplierMinimumPercent;
            movingAccuracyMultiplierFixed = DefaultMovingAccuracyMultiplierFixed;
            armorCooldownReductionPercent = DefaultArmorCooldownReductionPercent;
            armorCooldownReductionGrowthPerLevelPercent = DefaultArmorCooldownReductionGrowthPerLevelPercent;
            armorCooldownReductionMinimumPercent = DefaultArmorCooldownReductionMinimumPercent;
            armorCooldownReductionFixed = DefaultArmorCooldownReductionFixed;
            rangedDodgeDurationTicks = DefaultRangedDodgeDurationTicks;
            rangedDodgeDurationGrowthPerLevelTicks = DefaultRangedDodgeDurationGrowthPerLevelTicks;
            rangedDodgeDurationBaseTicks = DefaultRangedDodgeDurationBaseTicks;
            rangedDodgeDurationFixed = DefaultRangedDodgeDurationFixed;
            candidateRangeMode = DefaultCandidateRangeMode;
            customCandidateRange = DefaultCustomCandidateRange;
            responseWeaponDurabilityLossChancePercent = DefaultResponseWeaponDurabilityLossChancePercent;
            responseWeaponDurabilityLossAmount = DefaultResponseWeaponDurabilityLossAmount;
            creepJoinerDependencyGeneChancePercent = DefaultCreepJoinerDependencyGeneChancePercent;
            creepJoinerGeneChoice = DefaultCreepJoinerGeneChoice;
            aiSecondaryWeaponChancePercent = DefaultAiSecondaryWeaponChancePercent;
            responseDisarmChancePercent = DefaultResponseDisarmChancePercent;
            responseDisarmChanceGrowthPerLevelPercent = DefaultResponseDisarmChanceGrowthPerLevelPercent;
            responseDisarmChanceMinimumPercent = DefaultResponseDisarmChanceMinimumPercent;
            responseDisarmChanceFixed = DefaultResponseDisarmChanceFixed;
            responseAccidentalFireChancePercent = DefaultResponseAccidentalFireChancePercent;
            responseAccidentalFireChanceGrowthPerLevelPercent = DefaultResponseAccidentalFireChanceGrowthPerLevelPercent;
            responseAccidentalFireChanceMinimumPercent = DefaultResponseAccidentalFireChanceMinimumPercent;
            responseAccidentalFireChanceFixed = DefaultResponseAccidentalFireChanceFixed;
            responseCooldownReductionPercent = DefaultResponseCooldownReductionPercent;
            responseCooldownReductionGrowthPerLevelPercent = DefaultResponseCooldownReductionGrowthPerLevelPercent;
            responseCooldownReductionMinimumPercent = DefaultResponseCooldownReductionMinimumPercent;
            responseCooldownReductionFixed = DefaultResponseCooldownReductionFixed;
            serumDodgeMultiplierPercent = DefaultSerumDodgeMultiplierPercent;
            serumDodgeMultiplierGrowthPerLevelPercent = DefaultSerumDodgeMultiplierGrowthPerLevelPercent;
            serumDodgeMultiplierMinimumPercent = DefaultSerumDodgeMultiplierMinimumPercent;
            serumDodgeMultiplierFixed = DefaultSerumDodgeMultiplierFixed;
            serumResponseMultiplierPercent = DefaultSerumResponseMultiplierPercent;
            serumResponseMultiplierGrowthPerLevelPercent = DefaultSerumResponseMultiplierGrowthPerLevelPercent;
            serumResponseMultiplierMinimumPercent = DefaultSerumResponseMultiplierMinimumPercent;
            serumResponseMultiplierFixed = DefaultSerumResponseMultiplierFixed;
            serumInterceptionMultiplierPercent = DefaultSerumInterceptionMultiplierPercent;
            serumInterceptionMultiplierGrowthPerLevelPercent = DefaultSerumInterceptionMultiplierGrowthPerLevelPercent;
            serumInterceptionMultiplierMinimumPercent = DefaultSerumInterceptionMultiplierMinimumPercent;
            serumInterceptionMultiplierFixed = DefaultSerumInterceptionMultiplierFixed;
            secondaryWeaponEnabled = DefaultSecondaryWeaponEnabled;
            singleShotConversionEnabled = DefaultSingleShotConversionEnabled;
            randomAttackEnabled = DefaultRandomAttackEnabled;
            movingFireEnabled = DefaultMovingFireEnabled;
            closeFireEnabled = DefaultCloseFireEnabled;
            targetRushEnabled = DefaultTargetRushEnabled;
            accessRestrictionsDisabled = DefaultAccessRestrictionsDisabled;
            enableFriendlyPawnEffects = true;
            enableHostilePawnEffects = true;
            opProfileActive = false;
            normalProfileBackup = null;
            opProfile = CreateOpProfileDefaults();
        }

        public void ToggleOpProfile()
        {
            if (!opProfileActive)
            {
                normalProfileBackup = RimKataSettingsProfile.Capture(this);

                opProfile ??= CreateOpProfileDefaults();

                opProfile.ApplyTo(this);
                opProfileActive = true;
                return;
            }

            opProfile = RimKataSettingsProfile.Capture(this);
            (normalProfileBackup ?? CreateNormalProfileDefaults()).ApplyTo(this);
            normalProfileBackup = null;
            opProfileActive = false;
        }

        private void ApplyOpProfileDefaults()
        {
            rangedDodgeChanceFixed = true;
            rangedDodgeChancePercent = 99f;
            rangedDodgeChanceGrowthPerLevelPercent = 4f;
            rangedDodgeChanceMinimumPercent = 60f;
            meleeResponseChanceFixed = true;
            meleeResponseChancePercent = 99f;
            meleeResponseChanceGrowthPerLevelPercent = 4f;
            meleeResponseChanceMinimumPercent = 60f;
            responseDisarmChanceFixed = true;
            responseDisarmChancePercent = 20f;
            responseDisarmChanceGrowthPerLevelPercent = 2f;
            responseDisarmChanceMinimumPercent = 10f;
            meleeDodgeChanceFixed = true;
            meleeDodgeChancePercent = 99f;
            meleeDodgeChanceGrowthPerLevelPercent = 4f;
            meleeDodgeChanceMinimumPercent = 60f;
            interceptionChanceFixed = true;
            interceptionChancePercent = 100f;
            interceptionChanceGrowthPerLevelPercent = 4f;
            interceptionChanceMinimumPercent = 60f;
            interceptionCriticalChanceFixed = true;
            interceptionCriticalChancePercent = 80f;
            interceptionCriticalChanceGrowthPerLevelPercent = 4f;
            interceptionCriticalChanceMinimumPercent = 60f;
            movingAccuracyMultiplierFixed = true;
            movingAccuracyMultiplierPercent = 300f;
            movingAccuracyMultiplierGrowthPerLevelPercent = 20f;
            movingAccuracyMultiplierMinimumPercent = 100f;
            armorCooldownReductionFixed = true;
            armorCooldownReductionPercent = 99f;
            armorCooldownReductionGrowthPerLevelPercent = 4f;
            armorCooldownReductionMinimumPercent = 60f;
            rangedDodgeDurationFixed = true;
            rangedDodgeDurationTicks = 20;
            rangedDodgeDurationGrowthPerLevelTicks = 1;
            rangedDodgeDurationBaseTicks = 30;
            candidateRangeMode = (RimKataCandidateRangeMode)0;
            customCandidateRange = 0f;
            responseWeaponDurabilityLossChancePercent = 0f;
            responseWeaponDurabilityLossAmount = 0;
            creepJoinerDependencyGeneChancePercent = 0f;
            creepJoinerGeneChoice = DefaultCreepJoinerGeneChoice;
            aiSecondaryWeaponChancePercent = 0f;
            responseAccidentalFireChanceFixed = true;
            responseAccidentalFireChancePercent = 80f;
            responseAccidentalFireChanceGrowthPerLevelPercent = 3f;
            responseAccidentalFireChanceMinimumPercent = 30f;
            responseCooldownReductionFixed = true;
            responseCooldownReductionPercent = 80f;
            responseCooldownReductionGrowthPerLevelPercent = 4f;
            responseCooldownReductionMinimumPercent = 60f;
            serumDodgeMultiplierFixed = true;
            serumDodgeMultiplierPercent = 500f;
            serumDodgeMultiplierGrowthPerLevelPercent = 20f;
            serumDodgeMultiplierMinimumPercent = 300f;
            serumResponseMultiplierFixed = true;
            serumResponseMultiplierPercent = 500f;
            serumResponseMultiplierGrowthPerLevelPercent = 20f;
            serumResponseMultiplierMinimumPercent = 300f;
            serumInterceptionMultiplierFixed = true;
            serumInterceptionMultiplierPercent = 500f;
            serumInterceptionMultiplierGrowthPerLevelPercent = 20f;
            serumInterceptionMultiplierMinimumPercent = 300f;
            secondaryWeaponEnabled = DefaultSecondaryWeaponEnabled;
            singleShotConversionEnabled = DefaultSingleShotConversionEnabled;
            randomAttackEnabled = DefaultRandomAttackEnabled;
            movingFireEnabled = DefaultMovingFireEnabled;
            closeFireEnabled = DefaultCloseFireEnabled;
            targetRushEnabled = DefaultTargetRushEnabled;
            accessRestrictionsDisabled = DefaultAccessRestrictionsDisabled;
        }

        private static RimKataSettingsProfile CreateNormalProfileDefaults()
        {
            return RimKataSettingsProfile.Capture(new RimKataSettings());
        }

        private static RimKataSettingsProfile CreateOpProfileDefaults()
        {
            RimKataSettings defaults = new RimKataSettings();
            defaults.ApplyOpProfileDefaults();
            return RimKataSettingsProfile.Capture(defaults);
        }

        private static float ResolvePercent(Pawn pawn, bool fixedValue, float fixedPercent, float minimumPercent, float growthPerLevelPercent, SkillDef skill)
        {
            return fixedValue
                ? fixedPercent
                : minimumPercent + growthPerLevelPercent * SkillLevel(pawn, skill);
        }

        private static int SkillLevel(Pawn pawn, SkillDef skill)
        {
            return pawn?.skills?.GetSkill(skill)?.Level ?? 0;
        }

        private static float ChanceFromPercent(float percent) => Mathf.Clamp01(percent / 100f);
        private static float MultiplierFromPercent(float percent) => Mathf.Max(0f, percent / 100f);

        private static float SanitizePercent(float value, float defaultValue)
        {
            return float.IsNaN(value) || float.IsInfinity(value)
                ? defaultValue
                : Mathf.Clamp(value, 0f, 100f);
        }

        private static float SanitizeNonNegative(float value, float defaultValue)
        {
            return float.IsNaN(value) || float.IsInfinity(value)
                ? defaultValue
                : Mathf.Max(0f, value);
        }

        private static void LookRenamedFloat(ref float value, string key, string legacyKey, float defaultValue)
        {
            bool loadLegacyKey = Scribe.mode == LoadSaveMode.LoadingVars && Scribe.loader.curXmlParent?[key] == null;
            Scribe_Values.Look(ref value, loadLegacyKey ? legacyKey : key, defaultValue);
        }

        private static void LookFixedMode(
            ref bool value,
            string key,
            bool defaultValue,
            string scalarKey,
            string legacyScalarKey = null)
        {
            bool migrateOldScalarAsFixed = Scribe.mode == LoadSaveMode.LoadingVars && Scribe.loader.curXmlParent?[key] == null && (Scribe.loader.curXmlParent?[scalarKey] != null || (!legacyScalarKey.NullOrEmpty() && Scribe.loader.curXmlParent?[legacyScalarKey] != null));
            Scribe_Values.Look(ref value, key, defaultValue);
            if (migrateOldScalarAsFixed)
            {
                value = true;
            }
        }

        private static List<string> SanitizeDefNames(List<string> source)
        {
            if (source == null)
            {
                return new List<string>();
            }

            return source.Where(name => !name.NullOrEmpty())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
        }
    }
}
