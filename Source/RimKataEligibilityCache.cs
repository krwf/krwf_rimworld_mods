using System.Runtime.CompilerServices;
using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace KRWF.RimKata
{
    public static class RimKataEligibilityCache
    {
        private const int AccessUnknown = 0;
        private const int AccessDenied = 1;
        private const int AccessGranted = 2;

        private sealed class Entry
        {
            public volatile int accessState;
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

        private sealed class RegisteredUser
        {
            public volatile ThingWithComps secondaryWeapon;
        }

        private static readonly ConditionalWeakTable<Pawn, Entry> entries = new ConditionalWeakTable<Pawn, Entry>();
        private static readonly ConditionalWeakTable<Pawn, Entry>.CreateValueCallback CreateEntry = delegate { return new Entry(); };
        private static readonly ConditionalWeakTable<Pawn, RegisteredUser>
            registeredUsers = new ConditionalWeakTable<Pawn, RegisteredUser>();
        private static readonly ConditionalWeakTable<Pawn, RegisteredUser>.CreateValueCallback
            CreateRegisteredUser = delegate { return new RegisteredUser(); };
        private static HediffDef mindNumbSerumDef;
        private static HediffDef psychicBondDef;
        private static bool anomalyDefsResolved;

        // Read-only cache probe. A miss must not resolve a previously unseen Pawn.
        public static bool TryGetCachedAccess(
            Pawn pawn,
            out bool cachedAccess)
        {
            cachedAccess = false;

            if (!TryGetEntry(pawn, out Entry entry))
            {
                return false;
            }

            int accessState = entry.accessState;
            if (accessState == AccessUnknown)
            {
                return false;
            }

            cachedAccess = accessState == AccessGranted;
            return true;
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
                && RimKataDefOf.RimKata_G != null
                && pawn.genes.HasActiveGene(RimKataDefOf.RimKata_G);

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

            if (registeredUsers.TryGetValue(pawn, out RegisteredUser _))
            {
                return true;
            }

            Entry entry = entries.GetValue(pawn, CreateEntry);
            int accessState = entry.accessState;
            if (accessState == AccessDenied)
            {
                return false;
            }

            if (accessState == AccessGranted)
            {
                UpdateRegisteredUser(pawn, true);
                return true;
            }

            lock (entry)
            {
                accessState = entry.accessState;
                if (accessState != AccessUnknown)
                {
                    bool cachedAccess = accessState == AccessGranted;
                    UpdateRegisteredUser(pawn, cachedAccess);
                    return cachedAccess;
                }

                bool hasAccess = ResolveAccess(pawn, entry);
                UpdateRegisteredUser(pawn, hasAccess);
                return StoreAccess(entry, hasAccess);
            }
        }

        public static bool IsRegisteredUser(Pawn pawn)
        {
            return pawn != null
                && registeredUsers.TryGetValue(
                    pawn,
                    out RegisteredUser _);
        }

        public static bool TryGetRegisteredSecondaryWeapon(
            Pawn pawn,
            out ThingWithComps secondaryWeapon)
        {
            secondaryWeapon = null;
            if (pawn == null
                || !registeredUsers.TryGetValue(
                    pawn,
                    out RegisteredUser registeredUser))
            {
                return false;
            }

            secondaryWeapon = registeredUser.secondaryWeapon;
            return true;
        }

        public static void NotifySecondaryWeaponChanged(
            Pawn pawn,
            ThingWithComps secondaryWeapon)
        {
            if (pawn != null
                && registeredUsers.TryGetValue(
                    pawn,
                    out RegisteredUser registeredUser))
            {
                registeredUser.secondaryWeapon = secondaryWeapon;
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
            ThingWithComps registeredSecondary =
                BeginAccessInvalidation(pawn);
            if (TryGetEntry(pawn, out Entry entry))
            {
                lock (entry)
                {
                    entry.accessState = AccessUnknown;
                    entry.rimKataGeneKnown = false;
                    entry.dependencyGeneKnown = false;
                    entry.dependencyGene = null;
                }
            }

            FinishAccessInvalidation(pawn, registeredSecondary);
        }

        public static void InvalidatePsycast(Pawn pawn)
        {
            ThingWithComps registeredSecondary =
                BeginAccessInvalidation(pawn);
            if (TryGetEntry(pawn, out Entry entry))
            {
                lock (entry)
                {
                    entry.accessState = AccessUnknown;
                    entry.psycastKnown = false;
                }
            }

            FinishAccessInvalidation(pawn, registeredSecondary);
        }

        public static void InvalidateRole(Pawn pawn)
        {
            ThingWithComps registeredSecondary =
                BeginAccessInvalidation(pawn);
            if (TryGetEntry(pawn, out Entry entry))
            {
                lock (entry)
                {
                    entry.accessState = AccessUnknown;
                    entry.roleKnown = false;
                }
            }

            FinishAccessInvalidation(pawn, registeredSecondary);
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
            if (changedDef == null)
            {
                return;
            }

            ThingWithComps registeredSecondary =
                changedDef == RimKataDefOf.RimKata_A_Effect
                    ? BeginAccessInvalidation(pawn)
                    : null;

            if (TryGetEntry(pawn, out Entry entry))
            {
                lock (entry)
                {
                    if (changedDef == RimKataDefOf.RimKata_A_Effect)
                    {
                        entry.accessState = AccessUnknown;
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

            if (changedDef == RimKataDefOf.RimKata_A_Effect)
            {
                FinishAccessInvalidation(pawn, registeredSecondary);
            }
        }

        private static ThingWithComps BeginAccessInvalidation(Pawn pawn)
        {
            ThingWithComps registeredSecondary = pawn?.Spawned == true
                ? RimKataSecondaryWeaponRegistry.CurrentRegistry
                    ?.GetRegistered(pawn)
                : null;
            RemoveRegisteredUser(pawn);
            return registeredSecondary;
        }

        private static void FinishAccessInvalidation(
            Pawn pawn,
            ThingWithComps registeredSecondary)
        {
            if (pawn?.Spawned != true || registeredSecondary == null)
            {
                return;
            }

            if (RimKataEligibility.HasRimKataAccess(pawn))
            {
                NotifySecondaryWeaponChanged(pawn, registeredSecondary);
            }
            else
            {
                RimKataWeaponSlotUtility.ValidateLoadout(pawn);
            }
        }

        private static bool TryGetEntry(Pawn pawn, out Entry entry)
        {
            entry = null;
            return pawn != null && entries.TryGetValue(pawn, out entry);
        }

        private static bool StoreAccess(Entry entry, bool value)
        {
            entry.accessState = value
                ? AccessGranted
                : AccessDenied;
            return value;
        }

        private static bool ResolveAccess(Pawn pawn, Entry entry)
        {
            ResolveRimKataGene(pawn, entry);
            if (entry.hasRimKataGene)
            {
                return true;
            }

            ResolveAmpoule(pawn, entry);
            if (entry.hasAmpoule)
            {
                return true;
            }

            ResolvePsycast(pawn, entry);
            if (entry.hasPsycast)
            {
                return true;
            }

            ResolveRole(pawn, entry);
            if (entry.hasRole)
            {
                return true;
            }

            ResolveDependencyGene(pawn, entry);
            return entry.hasDependencyGene;
        }

        private static void UpdateRegisteredUser(Pawn pawn, bool hasAccess)
        {
            if (pawn == null)
            {
                return;
            }

            if (hasAccess)
            {
                RegisteredUser registeredUser = registeredUsers.GetValue(
                    pawn,
                    CreateRegisteredUser);
                registeredUser.secondaryWeapon =
                    RimKataSecondaryWeaponRegistry.CurrentRegistry?.Get(pawn);
            }
            else
            {
                registeredUsers.Remove(pawn);
            }
        }

        private static void RemoveRegisteredUser(Pawn pawn)
        {
            if (pawn != null)
            {
                registeredUsers.Remove(pawn);
            }
        }

        private static void ResolveRimKataGene(Pawn pawn, Entry entry)
        {
            if (entry.rimKataGeneKnown)
            {
                return;
            }

            entry.hasRimKataGene = ModsConfig.BiotechActive
                && pawn.genes != null
                && RimKataDefOf.RimKata_G != null
                && pawn.genes.HasActiveGene(RimKataDefOf.RimKata_G);
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
            if (__0?.def?.defName == "MindNumbSerum")
            {
                Gene_MindNumbSerumDependency gene =
                    RimKataAnomalyUtility.DependencyGene(___pawn);
                if (gene?.Active == true)
                {
                    gene.ResetWithoutSerumTicks();
                }
            }
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
