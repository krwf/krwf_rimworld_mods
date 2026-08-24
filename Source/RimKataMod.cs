using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace KRWF.RimKata
{
    public sealed class RimKataMod : Mod
    {
        private readonly RimKataSettingsUiBuffers uiBuffers = new RimKataSettingsUiBuffers();
        private Vector2 scrollPosition;

        public static RimKataSettings Settings { get; private set; }

        public RimKataMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<RimKataSettings>();
            uiBuffers.SyncFrom(Settings);
        }

        public override string SettingsCategory()
        {
            return "KRWF_RimKata_SettingsCategory".Translate();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            RimKataSettingsDrawer.Draw(inRect, Settings, uiBuffers, ref scrollPosition);
        }

        public override void WriteSettings()
        {
            base.WriteSettings();
            RimKataEquipmentUtility.InvalidateCaches();
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
