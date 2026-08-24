using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace KRWF.RimKata
{
    [HarmonyPatch(typeof(ColonistBar), nameof(ColonistBar.ColonistBarOnGUI))]
    public static class Patch_ColonistBar_RimKataDualWeaponIcons
    {
        private const float PrimaryAngle = 15f;
        private const float SecondaryAngle = -15f;

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
        private static readonly MethodInfo DrawWeaponIconsMethod = AccessTools.Method(
            typeof(Patch_ColonistBar_RimKataDualWeaponIcons),
            nameof(DrawWeaponIcons));

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int replacements = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(ThingIconMethod))
                {
                    instruction.operand = DrawWeaponIconsMethod;
                    replacements++;
                }

                yield return instruction;
            }

            if (replacements != 1)
            {
                Log.Warning("[RimKata] Expected one colonist-bar weapon icon call, but replaced " + replacements + ".");
            }
        }

        public static void DrawWeaponIcons(
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
            ThingWithComps secondary = pawn?.equipment?.Primary == primaryWeapon
                && RimKataWeaponSlotUtility.CanUseSecondarySlot(pawn)
                    ? RimKataWeaponSlotUtility.SecondaryWeapon(pawn)
                    : null;
            if (secondary == null)
            {
                Widgets.ThingIcon(rect, primary, alpha, rot, stackOfOne, scale, grayscale);
                return;
            }

            DrawRotatedIcon(rect, secondary, SecondaryAngle, alpha, rot, stackOfOne, scale, grayscale);
            DrawRotatedIcon(rect, primary, PrimaryAngle, alpha, rot, stackOfOne, scale, grayscale);
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
