using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace KRWF.RimKata
{
    [HarmonyPatch(typeof(ColonistBar), nameof(ColonistBar.ColonistBarOnGUI))]
    public static class Patch_ColonistBar_RimKataDualWeaponIcons
    {
        private const float SecondaryAngle = 45f;

        private static readonly MethodInfo ThingIconMethod = AccessTools.Method(
            typeof(Widgets),
            nameof(Widgets.ThingIcon),
            new[]
            {
                typeof(Rect),
                typeof(Thing),
                typeof(float),
                typeof(Rot4?),
                typeof(bool),
                typeof(float),
                typeof(bool)
            });
        private static readonly MethodInfo DrawSecondaryWeaponIconMethod = AccessTools.Method(
            typeof(Patch_ColonistBar_RimKataDualWeaponIcons),
            nameof(DrawSecondaryWeaponIcon));

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions,
            ILGenerator generator)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            int thingIconIndex = -1;
            int thingIconCount = 0;
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].Calls(ThingIconMethod))
                {
                    thingIconIndex = i;
                    thingIconCount++;
                }
            }

            if (thingIconCount != 1 || thingIconIndex < 0)
            {
                Log.Warning(
                    "[RimKata] Expected one colonist-bar weapon icon call for the secondary overlay, but found "
                    + thingIconCount
                    + ".");
                return codes;
            }

            if (DrawSecondaryWeaponIconMethod == null)
            {
                Log.Warning("[RimKata] Could not resolve the colonist-bar secondary weapon icon helper.");
                return codes;
            }

            if (codes[thingIconIndex].blocks.Count != 0)
            {
                Log.Warning(
                    "[RimKata] Kept the vanilla colonist-bar weapon icon call unchanged because its exception metadata was not safe to augment.");
                return codes;
            }

            LocalBuilder rectLocal = generator.DeclareLocal(typeof(Rect));
            LocalBuilder primaryLocal = generator.DeclareLocal(typeof(Thing));
            LocalBuilder alphaLocal = generator.DeclareLocal(typeof(float));
            LocalBuilder rotLocal = generator.DeclareLocal(typeof(Rot4?));
            LocalBuilder stackOfOneLocal = generator.DeclareLocal(typeof(bool));
            LocalBuilder scaleLocal = generator.DeclareLocal(typeof(float));
            LocalBuilder grayscaleLocal = generator.DeclareLocal(typeof(bool));

            CodeInstruction first = new CodeInstruction(
                OpCodes.Stloc,
                grayscaleLocal);
            first.labels.AddRange(codes[thingIconIndex].labels);
            codes[thingIconIndex].labels.Clear();

            codes.InsertRange(
                thingIconIndex,
                new[]
                {
                    first,
                    new CodeInstruction(OpCodes.Stloc, scaleLocal),
                    new CodeInstruction(OpCodes.Stloc, stackOfOneLocal),
                    new CodeInstruction(OpCodes.Stloc, rotLocal),
                    new CodeInstruction(OpCodes.Stloc, alphaLocal),
                    new CodeInstruction(OpCodes.Stloc, primaryLocal),
                    new CodeInstruction(OpCodes.Stloc, rectLocal),
                    new CodeInstruction(OpCodes.Ldloc, rectLocal),
                    new CodeInstruction(OpCodes.Ldloc, primaryLocal),
                    new CodeInstruction(OpCodes.Ldloc, alphaLocal),
                    new CodeInstruction(OpCodes.Ldloc, rotLocal),
                    new CodeInstruction(OpCodes.Ldloc, stackOfOneLocal),
                    new CodeInstruction(OpCodes.Ldloc, scaleLocal),
                    new CodeInstruction(OpCodes.Ldloc, grayscaleLocal),
                    new CodeInstruction(OpCodes.Call, DrawSecondaryWeaponIconMethod),
                    new CodeInstruction(OpCodes.Ldloc, rectLocal),
                    new CodeInstruction(OpCodes.Ldloc, primaryLocal),
                    new CodeInstruction(OpCodes.Ldloc, alphaLocal),
                    new CodeInstruction(OpCodes.Ldloc, rotLocal),
                    new CodeInstruction(OpCodes.Ldloc, stackOfOneLocal),
                    new CodeInstruction(OpCodes.Ldloc, scaleLocal),
                    new CodeInstruction(OpCodes.Ldloc, grayscaleLocal)
                });

            return codes;
        }

        public static void DrawSecondaryWeaponIcon(
            Rect rect,
            Thing primary,
            float alpha,
            Rot4? rot,
            bool stackOfOne,
            float scale,
            bool grayscale)
        {
            Pawn pawn = RimKataVisualUtility.FindPawnOwner(primary);
            ThingWithComps primaryWeapon = primary as ThingWithComps;
            ThingWithComps secondary = null;
            if (pawn?.equipment?.Primary == primaryWeapon
                && RimKataVisualUtility.TryGetUiLoadout(
                    pawn,
                    out ThingWithComps cachedPrimary,
                    out ThingWithComps rawSecondary)
                && cachedPrimary == primaryWeapon
                && RimKataVisualUtility.IsSecondaryUsable(
                    pawn,
                    cachedPrimary,
                    rawSecondary))
            {
                secondary = rawSecondary;
            }

            if (secondary != null)
            {
                DrawRotatedIcon(
                    rect,
                    secondary,
                    SecondaryAngle,
                    alpha,
                    rot,
                    stackOfOne,
                    scale,
                    grayscale);
            }
        }

        private static void DrawRotatedIcon(
            Rect rect,
            Thing weapon,
            float angle,
            float alpha,
            Rot4? rot,
            bool stackOfOne,
            float scale,
            bool grayscale)
        {
            Matrix4x4 previousMatrix = GUI.matrix;
            try
            {
                UI.RotateAroundPivot(angle, rect.center);
                Widgets.ThingIcon(rect, weapon, alpha, rot, stackOfOne, scale, grayscale);
            }
            finally
            {
                GUI.matrix = previousMatrix;
            }
        }
    }
}
