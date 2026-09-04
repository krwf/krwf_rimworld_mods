using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace KRWF.RimKata
{
    [StaticConstructorOnStartup]
    public static class RimKataBootstrap
    {
        static RimKataBootstrap()
        {
            Harmony harmony;
            try
            {
                harmony = new Harmony("krwf.rimkata");
            }
            catch (Exception exception)
            {
                Log.Error("[RimKata] Could not create the Harmony instance.\n" + exception);
                return;
            }

            try
            {
                harmony.PatchAll(Assembly.GetExecutingAssembly());
            }
            catch (Exception exception)
            {
                Log.Error("[RimKata] Core Harmony patching failed; initialization will continue.\n" + exception);
            }

            try
            {
                Patch_Projectile_Impact_Context.Apply(harmony);
            }
            catch (Exception exception)
            {
                Log.Error("[RimKata] Projectile.Impact patch discovery failed; initialization will continue.\n" + exception);
            }
        }
    }

    public sealed class RimKataMod : Mod
    {
        private sealed class RuntimeSettingsSnapshot
        {
            private readonly RimKataSettingsProfile scalarSettings;
            private readonly bool opProfileActive;
            private readonly string[] enabledWeaponDefNames;
            private readonly string[] enabledArmorDefNames;
            private readonly string[] twoHandWeaponDefNames;
            private readonly string[] oneHandWeaponOverrideDefNames;

            private RuntimeSettingsSnapshot(RimKataSettings settings)
            {
                scalarSettings = RimKataSettingsProfile.Capture(settings);
                opProfileActive = settings?.OpProfileActive == true;
                enabledWeaponDefNames = CaptureList(settings?.enabledWeaponDefNames);
                enabledArmorDefNames = CaptureList(settings?.enabledArmorDefNames);
                twoHandWeaponDefNames = CaptureList(settings?.twoHandWeaponDefNames);
                oneHandWeaponOverrideDefNames = CaptureList(settings?.oneHandWeaponOverrideDefNames);
            }

            public static RuntimeSettingsSnapshot Capture(RimKataSettings settings)
            {
                return new RuntimeSettingsSnapshot(settings);
            }

            public bool Matches(RimKataSettings settings)
            {
                return settings != null
                    && scalarSettings.Matches(settings)
                    && opProfileActive == settings.OpProfileActive
                    && ListMatches(enabledWeaponDefNames, settings.enabledWeaponDefNames)
                    && ListMatches(enabledArmorDefNames, settings.enabledArmorDefNames)
                    && ListMatches(twoHandWeaponDefNames, settings.twoHandWeaponDefNames)
                    && ListMatches(oneHandWeaponOverrideDefNames, settings.oneHandWeaponOverrideDefNames);
            }

            private static string[] CaptureList(List<string> values)
            {
                return values == null
                    ? null
                    : values.OrderBy(value => value, StringComparer.Ordinal).ToArray();
            }

            private static bool ListMatches(string[] snapshot, List<string> current)
            {
                if (snapshot == null || current == null)
                {
                    return snapshot == null && current == null;
                }

                if (snapshot.Length != current.Count)
                {
                    return false;
                }

                string[] currentValues = CaptureList(current);
                for (int i = 0; i < snapshot.Length; i++)
                {
                    if (!string.Equals(snapshot[i], currentValues[i], StringComparison.Ordinal))
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        private static RimKataMod instance;
        private readonly RimKataSettingsUiBuffers uiBuffers = new RimKataSettingsUiBuffers();
        private Vector2 scrollPosition;
        private RuntimeSettingsSnapshot settingsBeforeEdit;

        public static RimKataSettings Settings { get; private set; }

        public RimKataMod(ModContentPack content) : base(content)
        {
            instance = this;
            Settings = GetSettings<RimKataSettings>();
            uiBuffers.SyncFrom(Settings);
        }

        public override string SettingsCategory()
        {
            return "KRWF_RimKata_SettingsCategory".Translate();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            settingsBeforeEdit ??= RuntimeSettingsSnapshot.Capture(Settings);
            RimKataSettingsDrawer.Draw(inRect, Settings, uiBuffers, ref scrollPosition);
        }

        public override void WriteSettings()
        {
            bool settingsChanged = settingsBeforeEdit != null
                && !settingsBeforeEdit.Matches(Settings);
            base.WriteSettings();
            RimKataEquipmentUtility.InvalidateCaches();
            if (settingsChanged)
            {
                RimKataWeaponSlotUtility.NormalizeAllSpawnedLoadouts();
            }

            settingsBeforeEdit = null;
        }

        internal static void ApplyCombatFeatureSettingsChange()
        {
            RimKataEquipmentUtility.InvalidateCaches();
            RimKataWeaponSlotUtility.NotifyCombatFeaturesChanged();
            RefreshSettingsSnapshot();
        }

        internal static void ApplyEligibilitySettingsChange()
        {
            RimKataEquipmentUtility.InvalidateCaches();
            RimKataWeaponSlotUtility.NormalizeAllSpawnedLoadouts();
            RefreshSettingsSnapshot();
        }

        private static void RefreshSettingsSnapshot()
        {
            if (instance?.settingsBeforeEdit != null)
            {
                instance.settingsBeforeEdit = RuntimeSettingsSnapshot.Capture(Settings);
            }
        }
    }

    [HarmonyPatch(typeof(Dialog_ModSettings), MethodType.Constructor, typeof(Mod))]
    public static class Patch_DialogModSettings_RimKataWindowBehavior
    {
        public static void Postfix(Dialog_ModSettings __instance, Mod mod)
        {
            if (mod is RimKataMod)
            {
                __instance.resizeable = true;
                __instance.draggable = true;
            }
        }
    }

    [HarmonyPatch(typeof(Dialog_ModSettings), nameof(Dialog_ModSettings.InitialSize), MethodType.Getter)]
    public static class Patch_DialogModSettings_RimKataInitialSize
    {
        public static void Postfix(Mod ___mod, ref Vector2 __result)
        {
            if (___mod is RimKataMod)
            {
                __result = RimKataSettingsDrawer.RecommendedWindowSize();
            }
        }
    }
}
