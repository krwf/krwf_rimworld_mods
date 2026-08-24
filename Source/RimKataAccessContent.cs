using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace KRWF.RimKata
{
    public sealed class Graphic_RimKataStackCount : Graphic_StackCount
    {
        public override Graphic SubGraphicFor(Thing thing)
        {
            if (thing == null || subGraphics == null || subGraphics.Length == 0)
            {
                return base.SubGraphicFor(thing);
            }

            int index = Mathf.Clamp(thing.stackCount - 1, 0, subGraphics.Length - 1);
            return subGraphics[index];
        }
    }

    public sealed class CompProperties_RimKataAccessAbility : CompProperties_AbilityEffect
    {
        public CompProperties_RimKataAccessAbility()
        {
            compClass = typeof(CompAbilityEffect_RimKataAccess);
        }
    }

    public sealed class CompAbilityEffect_RimKataAccess : CompAbilityEffect
    {
        public override bool ShouldHideGizmo => true;
    }

    [HarmonyPatch]
    public static class Patch_HediffPsylink_RimKataPExistingAbility
    {
        public static MethodBase TargetMethod()
        {
            Type closure = AccessTools.Inner(typeof(Hediff_Psylink), "<>c__DisplayClass5_0");
            return AccessTools.Method(closure, "<TryGiveAbilityOfLevel>b__0");
        }

        public static void Postfix(Ability __0, ref bool __result)
        {
            if (__0?.def == RimKataDefOf.RimKata_P)
            {
                __result = false;
            }
        }
    }

    [HarmonyPatch]
    public static class Patch_HediffPsylink_RimKataPNaturalCandidate
    {
        public static MethodBase TargetMethod()
        {
            Type closure = AccessTools.Inner(typeof(Hediff_Psylink), "<>c__DisplayClass5_0");
            return AccessTools.Method(closure, "<TryGiveAbilityOfLevel>b__1");
        }

        public static void Postfix(AbilityDef __0, ref bool __result)
        {
            if (__0 == RimKataDefOf.RimKata_P)
            {
                __result = false;
            }
        }
    }
}
