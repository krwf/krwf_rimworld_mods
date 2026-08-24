using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace KRWF.RimKata
{
    [HarmonyPatch(
        typeof(BackCompatibility),
        nameof(BackCompatibility.BackCompatibleDefName),
        new[] 
        { 
            typeof(Type), 
            typeof(string), 
            typeof(bool), 
            typeof(System.Xml.XmlNode) 
        })]
    public static class Patch_BackCompatibility_RimKataDefNames
    {
        public static void Postfix(Type __0, string __1, ref string __result)
        {
            string replacement = ReplacementFor(__0, __1);
            if (replacement != null)
            {
                __result = replacement;
            }
        }

        private static string ReplacementFor(Type defType, string oldName)
        {
            if (defType == typeof(GeneDef))
            {
                if (oldName == "KRWF_RimKata_Gene") return "RimKata_Gene";
                if (oldName == "KRWF_MindNumbSerumDependency") return "ChemicalDependency_MindNumbSerum";
            }
            else if (defType == typeof(ThingDef))
            {
                if (oldName == "KRWF_RimKata_A") return "RimKata_A";
                if (oldName == "Psytrainer_KRWF_RimKata_P") return "Psytrainer_RimKata_P";
            }
            else if (defType == typeof(HediffDef) && oldName == "KRWF_RimKata_A_Effect")
            {
                return "RimKata_A_Effect";
            }
            else if (defType == typeof(AbilityDef) && oldName == "KRWF_RimKata_P")
            {
                return "RimKata_P";
            }
            else if (defType == typeof(JobDef))
            {
                if (oldName == "KRWF_RimKata_Attack") return "RimKata_Attack";
                if (oldName == "KRWF_RimKata_EquipSecondary") return "RimKata_EquipSecondary";
            }
            else if (defType == typeof(StatDef) && oldName == "KRWF_RimKata_CooldownMultiplier")
            {
                return "RimKata_CooldownMultiplier";
            }
            else if (defType == typeof(ThoughtDef))
            {
                if (oldName == "KRWF_MindNumbSerumWithdrawal") return "MindNumbSerumWithdrawal";
                if (oldName == "KRWF_MindNumbSerumDependencyOvercome") return "MindNumbSerumDependencyOvercome";
            }
            else if (defType == typeof(ResearchProjectDef) && oldName == "KRWF_RimKata_A_Production")
            {
                return "PenoxycylineProduction";
            }
            return null;
        }
    }
}
