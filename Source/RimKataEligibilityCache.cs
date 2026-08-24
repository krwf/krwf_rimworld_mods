using System.Runtime.CompilerServices;
using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace KRWF.RimKata
{
    public static class RimKataEligibilityCache
    {
        private sealed class Entry
        {
            public bool accessKnown;
            public bool hasAccess;
            public bool rimKataGeneKnown;
            public bool hasRimKataGene;
            public bool ampouleKnown;
            public bool hasAmpoule;
            public bool psycastKnown;
            public bool hasPsycast;
            public bool roleKnown;
            public bool hasRole;
            public bool dependencyGeneKnown;
            public bool hasDependencyGene;
            public Gene_MindNumbSerumDependency dependencyGene;
            public bool mindNumbedKnown;
            public bool mindNumbed;
            public bool bondKnown;
            public bool bond;
        }

        private static readonly ConditionalWeakTable<Pawn, Entry> entries = new ConditionalWeakTable<Pawn, Entry>();
        private static readonly ConditionalWeakTable<Pawn, Entry>.CreateValueCallback CreateEntry = delegate { return new Entry(); };
        private static HediffDef mindNumbSerumDef;
        private static HediffDef psychicBondDef;
        private static bool anomalyDefsResolved;

        // !!! Debug HUD !!!
        public static bool TryGetCachedAccess(
            Pawn pawn,
            out bool cachedAccess)
        {
            cachedAccess = false;

            if (!TryGetEntry(pawn, out Entry entry))
            {
                return false;
            }

            lock (entry)
            {
                if (!entry.accessKnown)
                {
                    return false;
                }

                cachedAccess = entry.hasAccess;
                return true;
            }
        }

        // !!! Debug HUD !!!
        public static bool DebugHasRawAccessSource(Pawn pawn)
        {
            if (pawn == null)
            {
                return false;
            }

            bool gene =
                ModsConfig.BiotechActive
                && pawn.genes != null
                && RimKataDefOf.RimKata_Gene != null
                && pawn.genes.HasActiveGene(RimKataDefOf.RimKata_Gene);

            bool ampoule =
                RimKataDefOf.RimKata_A_Effect != null
                && pawn.health?.hediffSet?.HasHediff(
                    RimKataDefOf.RimKata_A_Effect) == true;

            bool psycast =
                ModsConfig.RoyaltyActive
                && RimKataDefOf.RimKata_P != null
                && pawn.abilities?.GetAbility(
                    RimKataDefOf.RimKata_P,
                    true) != null;

            bool role =
                ModsConfig.IdeologyActive
                && RimKataDefOf.RimKata_I != null
                && pawn.Ideo?.GetRole(pawn)?.def == RimKataDefOf.RimKata_I;

            GeneDef dependencyDef =
                RimKataAnomalyUtility.DependencyGeneDef;

            Gene dependencyGene =
                dependencyDef == null
                    ? null
                    : pawn.genes?.GetGene(dependencyDef);

            bool dependency = dependencyGene?.Active == true;

            return gene
                || ampoule
                || psycast
                || role
                || dependency;
        }

        public static bool HasAnyAccessSource(Pawn pawn)
        {
            if (pawn == null)
            {
                return false;
            }

            Entry entry = entries.GetValue(pawn, CreateEntry);
            lock (entry)
            {
                if (entry.accessKnown)
                {
                    return entry.hasAccess;
                }

                ResolveRimKataGene(pawn, entry);
                if (entry.hasRimKataGene)
                {
                    return StoreAccess(entry, true);
                }

                ResolveAmpoule(pawn, entry);
                if (entry.hasAmpoule)
                {
                    return StoreAccess(entry, true);
                }

                ResolvePsycast(pawn, entry);
                if (entry.hasPsycast)
                {
                    return StoreAccess(entry, true);
                }

                ResolveRole(pawn, entry);
                if (entry.hasRole)
                {
                    return StoreAccess(entry, true);
                }

                ResolveDependencyGene(pawn, entry);
                return StoreAccess(entry, entry.hasDependencyGene);
            }
        }

        public static bool HasActiveDependencyGene(Pawn pawn)
        {
            if (pawn == null)
            {
                return false;
            }

            Entry entry = entries.GetValue(pawn, CreateEntry);
            lock (entry)
            {
                ResolveDependencyGene(pawn, entry);
                return entry.hasDependencyGene;
            }
        }

        public static Gene_MindNumbSerumDependency DependencyGene(Pawn pawn)
        {
            if (pawn == null)
            {
                return null;
            }

            Entry entry = entries.GetValue(pawn, CreateEntry);
            lock (entry)
            {
                ResolveDependencyGene(pawn, entry);
                return entry.dependencyGene;
            }
        }

        public static bool IsMindNumbed(Pawn pawn)
        {
            if (pawn?.health?.hediffSet == null)
            {
                return false;
            }

            Entry entry = entries.GetValue(pawn, CreateEntry);
            lock (entry)
            {
                if (!entry.mindNumbedKnown)
                {
                    ResolveAnomalyDefs();
                    entry.mindNumbed = mindNumbSerumDef != null
                        && pawn.health.hediffSet.HasHediff(mindNumbSerumDef);
                    entry.mindNumbedKnown = true;
                }

                return entry.mindNumbed;
            }
        }

        public static bool DependencyOvercomeByBond(Pawn pawn)
        {
            if (pawn == null)
            {
                return false;
            }

            Entry entry = entries.GetValue(pawn, CreateEntry);
            lock (entry)
            {
                if (!entry.bondKnown)
                {
                    entry.bond = ScanForOvercomingBond(pawn);
                    entry.bondKnown = true;
                }

                return entry.bond;
            }
        }

        public static void InvalidateGenes(Pawn pawn)
        {
            if (!TryGetEntry(pawn, out Entry entry))
            {
                return;
            }

            lock (entry)
            {
                entry.accessKnown = false;
                entry.rimKataGeneKnown = false;
                entry.dependencyGeneKnown = false;
                entry.dependencyGene = null;
            }
        }

        public static void InvalidatePsycast(Pawn pawn)
        {
            if (TryGetEntry(pawn, out Entry entry))
            {
                lock (entry)
                {
                    entry.accessKnown = false;
                    entry.psycastKnown = false;
                }
            }
        }

        public static void InvalidateRole(Pawn pawn)
        {
            if (TryGetEntry(pawn, out Entry entry))
            {
                lock (entry)
                {
                    entry.accessKnown = false;
                    entry.roleKnown = false;
                }
            }
        }

        public static void InvalidateRelations(Pawn pawn)
        {
            if (TryGetEntry(pawn, out Entry entry))
            {
                lock (entry)
                {
                    entry.bondKnown = false;
                }
            }
        }

        public static void InvalidateHediff(Pawn pawn, HediffDef changedDef)
        {
            if (changedDef == null || !TryGetEntry(pawn, out Entry entry))
            {
                return;
            }

            lock (entry)
            {
                if (changedDef == RimKataDefOf.RimKata_A_Effect)
                {
                    entry.accessKnown = false;
                    entry.ampouleKnown = false;
                }

                if (changedDef.defName == "MindNumbSerum")
                {
                    entry.mindNumbedKnown = false;
                }

                if (changedDef.defName == "PsychicBond")
                {
                    entry.bondKnown = false;
                }
            }
        }

        private static bool TryGetEntry(Pawn pawn, out Entry entry)
        {
            entry = null;
            return pawn != null && entries.TryGetValue(pawn, out entry);
        }

        private static bool StoreAccess(Entry entry, bool value)
        {
            entry.hasAccess = value;
            entry.accessKnown = true;
            return value;
        }

        private static void ResolveRimKataGene(Pawn pawn, Entry entry)
        {
            if (entry.rimKataGeneKnown)
            {
                return;
            }

            entry.hasRimKataGene = ModsConfig.BiotechActive
                && pawn.genes != null
                && RimKataDefOf.RimKata_Gene != null
                && pawn.genes.HasActiveGene(RimKataDefOf.RimKata_Gene);
            entry.rimKataGeneKnown = true;
        }

        private static void ResolveAmpoule(Pawn pawn, Entry entry)
        {
            if (entry.ampouleKnown)
            {
                return;
            }

            entry.hasAmpoule = RimKataDefOf.RimKata_A_Effect != null
                && pawn.health?.hediffSet?.HasHediff(RimKataDefOf.RimKata_A_Effect) == true;
            entry.ampouleKnown = true;
        }

        private static void ResolvePsycast(Pawn pawn, Entry entry)
        {
            if (entry.psycastKnown)
            {
                return;
            }

            entry.hasPsycast = ModsConfig.RoyaltyActive
                && RimKataDefOf.RimKata_P != null
                && pawn.abilities?.GetAbility(RimKataDefOf.RimKata_P, true) != null;
            entry.psycastKnown = true;
        }

        private static void ResolveRole(Pawn pawn, Entry entry)
        {
            if (entry.roleKnown)
            {
                return;
            }

            entry.hasRole = ModsConfig.IdeologyActive
                && RimKataDefOf.RimKata_I != null
                && pawn.Ideo?.GetRole(pawn)?.def == RimKataDefOf.RimKata_I;
            entry.roleKnown = true;
        }

        private static void ResolveDependencyGene(Pawn pawn, Entry entry)
        {
            if (entry.dependencyGeneKnown)
            {
                return;
            }

            GeneDef geneDef = RimKataAnomalyUtility.DependencyGeneDef;
            Gene gene = geneDef == null ? null : pawn.genes?.GetGene(geneDef);
            entry.dependencyGene = gene as Gene_MindNumbSerumDependency;
            entry.hasDependencyGene = gene?.Active == true;
            entry.dependencyGeneKnown = true;
        }

        private static bool ScanForOvercomingBond(Pawn pawn)
        {
            if (pawn.relations?.DirectRelations != null)
            {
                for (int i = 0; i < pawn.relations.DirectRelations.Count; i++)
                {
                    if (IsOvercomingRelation(pawn.relations.DirectRelations[i].def))
                    {
                        return true;
                    }
                }
            }

            if (pawn.relations?.VirtualRelations != null)
            {
                for (int i = 0; i < pawn.relations.VirtualRelations.Count; i++)
                {
                    if (IsOvercomingRelation(pawn.relations.VirtualRelations[i].def))
                    {
                        return true;
                    }
                }
            }

            ResolveAnomalyDefs();
            return psychicBondDef != null && pawn.health?.hediffSet?.HasHediff(psychicBondDef) == true;
        }

        private static bool IsOvercomingRelation(PawnRelationDef relation)
        {
            return relation == PawnRelationDefOf.Lover
                || relation == PawnRelationDefOf.Fiance
                || relation == PawnRelationDefOf.Spouse;
        }

        private static void ResolveAnomalyDefs()
        {
            if (anomalyDefsResolved)
            {
                return;
            }

            mindNumbSerumDef = DefDatabase<HediffDef>.GetNamedSilentFail("MindNumbSerum");
            psychicBondDef = DefDatabase<HediffDef>.GetNamedSilentFail("PsychicBond");
            anomalyDefsResolved = true;
        }
    }

    [HarmonyPatch(typeof(Pawn_GeneTracker), "Notify_GenesChanged")]
    public static class Patch_PawnGeneTracker_RimKataEligibilityCache
    {
        public static void Postfix(Pawn ___pawn)
        {
            RimKataEligibilityCache.InvalidateGenes(___pawn);
        }
    }

    [HarmonyPatch(typeof(Pawn_AbilityTracker), nameof(Pawn_AbilityTracker.GainAbility))]
    public static class Patch_PawnAbilityTracker_Gain_RimKataEligibilityCache
    {
        public static void Postfix(Pawn ___pawn, AbilityDef __0)
        {
            if (__0 == RimKataDefOf.RimKata_P)
            {
                RimKataEligibilityCache.InvalidatePsycast(___pawn);
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_AbilityTracker), nameof(Pawn_AbilityTracker.RemoveAbility))]
    public static class Patch_PawnAbilityTracker_Remove_RimKataEligibilityCache
    {
        public static void Postfix(Pawn ___pawn, AbilityDef __0)
        {
            if (__0 == RimKataDefOf.RimKata_P)
            {
                RimKataEligibilityCache.InvalidatePsycast(___pawn);
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_IdeoTracker), nameof(Pawn_IdeoTracker.SetIdeo))]
    public static class Patch_PawnIdeoTracker_RimKataEligibilityCache
    {
        public static void Postfix(Pawn ___pawn)
        {
            RimKataEligibilityCache.InvalidateRole(___pawn);
        }
    }

    [HarmonyPatch(typeof(Precept_RoleMulti), nameof(Precept_RoleMulti.Assign))]
    public static class Patch_PreceptRoleMulti_Assign_RimKataEligibilityCache
    {
        public static void Postfix(Precept_RoleMulti __instance, Pawn p)
        {
            if (__instance?.def == RimKataDefOf.RimKata_I)
            {
                RimKataEligibilityCache.InvalidateRole(p);
            }
        }
    }

    [HarmonyPatch(typeof(Precept_RoleMulti), nameof(Precept_RoleMulti.Unassign))]
    public static class Patch_PreceptRoleMulti_Unassign_RimKataEligibilityCache
    {
        public static void Postfix(Precept_RoleMulti __instance, Pawn p)
        {
            if (__instance?.def == RimKataDefOf.RimKata_I)
            {
                RimKataEligibilityCache.InvalidateRole(p);
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.Notify_HediffChanged))]
    public static class Patch_PawnHealthTracker_RimKataEligibilityCache
    {
        public static void Postfix(Pawn ___pawn, Hediff __0)
        {
            if (__0?.Part != null)
            {
                RimKataWeaponSlotUtility.InvalidateCombatVerbCache(___pawn);
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.AddHediff), new Type[]
    {
        typeof(Hediff),
        typeof(BodyPartRecord),
        typeof(DamageInfo?),
        typeof(DamageWorker.DamageResult)
    })]

    public static class Patch_PawnHealthTracker_AddHediff_RimKataEligibilityCache
    {
        public static void Postfix(Pawn ___pawn, Hediff __0)
        {
            RimKataEligibilityCache.InvalidateHediff(___pawn, __0?.def);
        }
    }

    [HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.RemoveHediff))]
    public static class Patch_PawnHealthTracker_RemoveHediff_RimKataEligibilityCache
    {
        public static void Postfix(Pawn ___pawn, Hediff __0)
        {
            RimKataEligibilityCache.InvalidateHediff(___pawn, __0?.def);
        }
    }

    [HarmonyPatch(typeof(Pawn_RelationsTracker), "GainedOrLostDirectRelation")]
    public static class Patch_PawnRelationsTracker_RimKataEligibilityCache
    {
        public static void Postfix(Pawn ___pawn)
        {
            RimKataEligibilityCache.InvalidateRelations(___pawn);
        }
    }

    [HarmonyPatch(typeof(Pawn_RelationsTracker), nameof(Pawn_RelationsTracker.RemoveRelation))]
    public static class Patch_PawnRelationsTracker_RemoveVirtual_RimKataEligibilityCache
    {
        public static void Postfix(Pawn ___pawn)
        {
            RimKataEligibilityCache.InvalidateRelations(___pawn);
        }
    }

    [HarmonyPatch(typeof(Pawn_RelationsTracker), "CleanupVirtualRelationReferences")]
    public static class Patch_PawnRelationsTracker_CleanupVirtual_RimKataEligibilityCache
    {
        public static void Postfix(Pawn ___pawn)
        {
            RimKataEligibilityCache.InvalidateRelations(___pawn);
        }
    }
}
