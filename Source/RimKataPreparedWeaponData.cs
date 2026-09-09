using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace KRWF.RimKata
{
    // The bound Verb owns its live attack state. This shared copy contains only
    // definition data and the fixed inputs of the existing single-shot timing rule.
    internal sealed class RimKataPreparedVerbProperties : VerbProperties
    {
        internal readonly VerbProperties OriginalProperties;
        internal readonly int ConfigurationRevision;
        internal readonly int OriginalBurstCount;
        internal readonly int OriginalBurstSpacing;
        internal readonly float OriginalWarmupSeconds;
        internal readonly int TimingBurstCount;
        internal readonly float TotalBurstSpacingTicks;
        internal readonly float ExperienceCycleCorrectionSeconds;

        internal RimKataPreparedVerbProperties(
            VerbProperties original,
            int revision,
            bool convertSingleShot,
            int originalBurstCount,
            int originalBurstSpacing,
            FieldInfo[] definitionFields)
        {
            for (int i = 0; i < definitionFields.Length; i++)
            {
                FieldInfo field = definitionFields[i];
                field.SetValue(this, field.GetValue(original));
            }

            OriginalProperties = original;
            ConfigurationRevision = revision;
            OriginalBurstCount = Mathf.Max(1, originalBurstCount);
            OriginalBurstSpacing = Mathf.Max(0, originalBurstSpacing);
            OriginalWarmupSeconds = Mathf.Max(0f, original.warmupTime);
            TimingBurstCount = convertSingleShot && !original.IsMeleeAttack
                ? OriginalBurstCount
                : 1;
            TotalBurstSpacingTicks = (TimingBurstCount - 1f) * OriginalBurstSpacing;

            // Every controller request is one native attack. When conversion is
            // disabled the slot retains the original burst length and spacing.
            burstShotCount = 1;
            ticksBetweenBurstShots = OriginalBurstSpacing;
            warmupTime = OriginalWarmupSeconds / TimingBurstCount;
            ExperienceCycleCorrectionSeconds = OriginalWarmupSeconds - warmupTime
                + (OriginalBurstCount - 1f) * OriginalBurstSpacing / 60f;
        }
    }

    internal static class RimKataPreparedWeaponData
    {
        private static readonly AccessTools.FieldRef<Verb, int?> CachedBurstShotCount =
            AccessTools.FieldRefAccess<Verb, int?>("cachedBurstShotCount");
        private static readonly AccessTools.FieldRef<Verb, int?> CachedTicksBetweenBurstShots =
            AccessTools.FieldRefAccess<Verb, int?>("cachedTicksBetweenBurstShots");
        private static readonly FieldInfo[] DefinitionFields = typeof(VerbProperties)
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly Dictionary<VerbProperties, RimKataPreparedVerbProperties> PreparedDefinitions =
            new Dictionary<VerbProperties, RimKataPreparedVerbProperties>();
        private static readonly Dictionary<VerbProperties, List<RimKataPreparedVerbProperties>> PreparedVariants =
            new Dictionary<VerbProperties, List<RimKataPreparedVerbProperties>>();
        private static int preparedRevision = int.MinValue;

        // Called after definitions are available and when equipment settings change.
        // No Pawn or map scan is needed to prepare the allowed definition list.
        internal static void RefreshDefinitions()
        {
            PreparedDefinitions.Clear();
            PreparedVariants.Clear();
            preparedRevision = RimKataEquipmentUtility.WeaponConfigurationRevision;
            List<string> selected = RimKataMod.Settings?.enabledWeaponDefNames;
            if (selected == null)
            {
                return;
            }

            for (int i = 0; i < selected.Count; i++)
            {
                string defName = selected[i];
                if (defName.NullOrEmpty())
                {
                    continue;
                }

                ThingDef definition = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
                List<VerbProperties> verbs = definition?.Verbs;
                if (verbs == null)
                {
                    continue;
                }

                for (int j = 0; j < verbs.Count; j++)
                {
                    Prepare(verbs[j]);
                }
            }
        }

        internal static RimKataPreparedVerbProperties Bind(Verb verb)
        {
            if (verb?.verbProps == null)
            {
                return null;
            }

            int revision = RimKataEquipmentUtility.WeaponConfigurationRevision;
            RimKataPreparedVerbProperties current = verb.verbProps as RimKataPreparedVerbProperties;
            if (current != null && current.ConfigurationRevision == revision)
            {
                return current;
            }

            if (preparedRevision != revision)
            {
                RefreshDefinitions();
            }

            if (current != null)
            {
                // A settings revision rebinds from the real native definition,
                // never from the one-shot getter values installed below.
                Restore(verb);
            }
            VerbProperties original = verb.verbProps;
            int originalBurstCount = Mathf.Max(1, verb.BurstShotCount);
            int originalBurstSpacing = Mathf.Max(0, verb.TicksBetweenBurstShots);
            RimKataPreparedVerbProperties prepared = PrepareRuntimeVariant(
                original, originalBurstCount, originalBurstSpacing);
            verb.verbProps = prepared;
            // Native getters cache their values and apply unique-weapon traits.
            // Install the prepared results so neither an old burst nor a second
            // application of those traits can override the copied one-shot data.
            CachedBurstShotCount(verb) = 1;
            CachedTicksBetweenBurstShots(verb) = prepared.OriginalBurstSpacing;
            return prepared;
        }

        internal static void Restore(Verb verb)
        {
            if (verb?.verbProps is RimKataPreparedVerbProperties prepared)
            {
                verb.verbProps = prepared.OriginalProperties;
                CachedBurstShotCount(verb) = null;
                CachedTicksBetweenBurstShots(verb) = null;
            }
        }

        internal static int GetOriginalBurstCount(Verb verb)
        {
            return verb?.verbProps is RimKataPreparedVerbProperties prepared
                ? prepared.OriginalBurstCount
                : Mathf.Max(1, verb?.BurstShotCount ?? 1);
        }

        internal static int GetOriginalBurstSpacing(Verb verb)
        {
            return verb?.verbProps is RimKataPreparedVerbProperties prepared
                ? prepared.OriginalBurstSpacing
                : Mathf.Max(0, verb?.TicksBetweenBurstShots ?? 0);
        }

        internal static bool AdjustShootingExperienceCycleTime(Verb verb, ref float cycleSeconds)
        {
            if (!(verb?.verbProps is RimKataPreparedVerbProperties prepared))
            {
                return false;
            }

            if (prepared.OriginalBurstCount > 1)
            {
                // Native shooting experience runs once for each requested shot,
                // including the retained full-burst mode. Divide its original
                // cycle contribution without querying cooldown stats a second time.
                cycleSeconds = (cycleSeconds + prepared.ExperienceCycleCorrectionSeconds)
                    / prepared.OriginalBurstCount;
            }

            return true;
        }

        private static RimKataPreparedVerbProperties Prepare(VerbProperties original)
        {
            if (original == null)
            {
                return null;
            }

            if (!PreparedDefinitions.TryGetValue(original, out RimKataPreparedVerbProperties prepared))
            {
                prepared = new RimKataPreparedVerbProperties(
                    original,
                    preparedRevision,
                    RimKataMod.Settings?.singleShotConversionEnabled != false,
                    original.burstShotCount,
                    original.ticksBetweenBurstShots,
                    DefinitionFields);
                PreparedDefinitions.Add(original, prepared);
            }

            return prepared;
        }

        private static RimKataPreparedVerbProperties PrepareRuntimeVariant(
            VerbProperties original, int burstCount, int burstSpacing)
        {
            RimKataPreparedVerbProperties prepared = Prepare(original);
            if (prepared.OriginalBurstCount == burstCount
                && prepared.OriginalBurstSpacing == burstSpacing)
            {
                return prepared;
            }

            // Native unique weapons can have fixed count/spacing trait changes.
            // Resolve them once at binding and share matching immutable variants.
            if (!PreparedVariants.TryGetValue(original, out List<RimKataPreparedVerbProperties> variants))
            {
                variants = new List<RimKataPreparedVerbProperties>();
                PreparedVariants.Add(original, variants);
            }
            for (int i = 0; i < variants.Count; i++)
            {
                RimKataPreparedVerbProperties variant = variants[i];
                if (variant.OriginalBurstCount == burstCount
                    && variant.OriginalBurstSpacing == burstSpacing)
                {
                    return variant;
                }
            }

            prepared = new RimKataPreparedVerbProperties(
                original,
                preparedRevision,
                RimKataMod.Settings?.singleShotConversionEnabled != false,
                burstCount,
                burstSpacing,
                DefinitionFields);
            variants.Add(prepared);
            return prepared;
        }
    }
}
