using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using Verse;
using Verse.AI;

namespace KRWF.RimKata
{
    public enum RimKataSecondaryRecoveryPhase
    {
        WaitingForPrimary,
        PrimaryRecovered,
        JobIssued,
        PrimaryPickupIssued
    }

    public sealed class RimKataSecondaryRecovery : IExposable
    {
        public Pawn pawn;
        public ThingWithComps primary;
        public ThingWithComps secondary;
        public RimKataSecondaryRecoveryPhase phase;
        public int nextRetryTick;

        public void ExposeData()
        {
            Scribe_References.Look(ref pawn, "pawn");
            Scribe_References.Look(ref primary, "primary");
            Scribe_References.Look(ref secondary, "secondary");
            Scribe_Values.Look(
                ref phase,
                "phase",
                RimKataSecondaryRecoveryPhase.WaitingForPrimary);
            Scribe_Values.Look(ref nextRetryTick, "nextRetryTick");
        }
    }

    public sealed class RimKataSecondaryWeaponRegistry : GameComponent
    {
        private List<Pawn> pawns = new List<Pawn>();
        private List<ThingWithComps> weapons = new List<ThingWithComps>();
        private List<RimKataSecondaryRecovery> recoveries =
            new List<RimKataSecondaryRecovery>();

        public RimKataSecondaryWeaponRegistry(Game game)
        {
        }

        public static RimKataSecondaryWeaponRegistry CurrentRegistry => Current.Game?.GetComponent<RimKataSecondaryWeaponRegistry>();

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref pawns, "rimKataSecondaryWeaponPawns", LookMode.Reference);
            Scribe_Collections.Look(ref weapons, "rimKataSecondaryWeapons", LookMode.Reference);
            Scribe_Collections.Look(
                ref recoveries,
                "rimKataSecondaryRecoveries",
                LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                pawns ??= new List<Pawn>();
                weapons ??= new List<ThingWithComps>();
                recoveries ??= new List<RimKataSecondaryRecovery>();
                while (pawns.Count > weapons.Count)
                {
                    pawns.RemoveAt(pawns.Count - 1);
                }

                while (weapons.Count > pawns.Count)
                {
                    weapons.RemoveAt(weapons.Count - 1);
                }

                Cleanup();
                for (int i = recoveries.Count - 1; i >= 0; i--)
                {
                    RimKataSecondaryRecovery recovery = recoveries[i];
                    if (recovery == null)
                    {
                        recoveries.RemoveAt(i);
                    }
                    else if (recovery.phase == RimKataSecondaryRecoveryPhase.JobIssued)
                    {
                        recovery.phase = RimKataSecondaryRecoveryPhase.PrimaryRecovered;
                    }
                }

                CleanupRecoveries();
            }
        }

        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame % 600 == 0)
            {
                Cleanup();
                CleanupRecoveries();
                for (int i = pawns.Count - 1; i >= 0; i--)
                {
                    RimKataWeaponSlotUtility.ValidateLoadout(pawns[i]);
                }
            }
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            Cleanup();
            CleanupRecoveries();
            for (int i = pawns.Count - 1; i >= 0; i--)
            {
                Pawn pawn = pawns[i];
                RimKataWeaponSlotUtility.ValidateLoadout(pawn);
                RimKataEligibilityCache.NotifySecondaryWeaponChanged(
                    pawn,
                    Get(pawn));
            }
        }

        public ThingWithComps Get(Pawn pawn)
        {
            ThingWithComps weapon = GetRegistered(pawn);
            if (weapon == null || pawn.equipment.Primary == weapon)
            {
                return null;
            }

            return weapon;
        }

        public ThingWithComps GetRegistered(Pawn pawn)
        {
            int index = pawns.IndexOf(pawn);
            if (index < 0)
            {
                return null;
            }

            ThingWithComps weapon = weapons[index];
            if (!StillHeld(pawn, weapon))
            {
                RemoveAt(index);
                return null;
            }

            return weapon;
        }

        public void Set(Pawn pawn, ThingWithComps weapon)
        {
            if (pawn == null || weapon == null)
            {
                return;
            }

            int index = pawns.IndexOf(pawn);
            if (index < 0)
            {
                pawns.Add(pawn);
                weapons.Add(weapon);
            }
            else
            {
                weapons[index] = weapon;
            }

            RimKataEligibilityCache.NotifySecondaryWeaponChanged(
                pawn,
                weapon);
        }

        public void Clear(Pawn pawn, ThingWithComps expectedWeapon = null)
        {
            int index = pawns.IndexOf(pawn);
            if (index >= 0 && (expectedWeapon == null || weapons[index] == expectedWeapon))
            {
                RemoveAt(index);
            }
        }

        public void RecordDroppedLoadout(
            Pawn pawn,
            ThingWithComps primary,
            ThingWithComps secondary)
        {
            if (pawn == null
                || primary == null
                || secondary == null
                || primary == secondary
                || primary.Destroyed
                || secondary.Destroyed)
            {
                return;
            }

            Clear(pawn, secondary);
            RemoveRecovery(pawn);
            recoveries.Add(new RimKataSecondaryRecovery
            {
                pawn = pawn,
                primary = primary,
                secondary = secondary,
                phase = RimKataSecondaryRecoveryPhase.WaitingForPrimary
            });
        }

        public bool IsExpectedAutomaticPrimaryRecovery(
            Pawn pawn,
            ThingWithComps incoming)
        {
            RimKataSecondaryRecovery recovery = FindRecovery(pawn);
            Job job = pawn?.CurJob;
            return recovery?.phase == RimKataSecondaryRecoveryPhase.PrimaryPickupIssued
                && recovery.primary == incoming
                && job?.def == JobDefOf.Equip
                && job.playerForced != true
                && job.GetTarget(TargetIndex.A).Thing == incoming;
        }

        public void NotifyAutomaticPrimaryPickupIssued(Pawn pawn, Job job)
        {
            RimKataSecondaryRecovery recovery = FindRecovery(pawn);
            if (recovery?.phase != RimKataSecondaryRecoveryPhase.WaitingForPrimary
                || job?.def != JobDefOf.Equip
                || job.playerForced
                || job.GetTarget(TargetIndex.A).Thing != recovery.primary
                || pawn?.mindState?.droppedWeapon != recovery.primary)
            {
                return;
            }

            recovery.phase = RimKataSecondaryRecoveryPhase.PrimaryPickupIssued;
        }

        public void ConfirmAutomaticPrimaryRecovery(
            Pawn pawn,
            ThingWithComps incoming)
        {
            RimKataSecondaryRecovery recovery = FindRecovery(pawn);
            if (recovery?.phase != RimKataSecondaryRecoveryPhase.PrimaryPickupIssued
                || recovery.primary != incoming
                || pawn?.equipment?.Primary != incoming)
            {
                return;
            }

            recovery.phase = RimKataSecondaryRecoveryPhase.PrimaryRecovered;
            recovery.nextRetryTick = Find.TickManager?.TicksGame ?? 0;
        }

        public Job TryMakeSecondaryRecoveryJob(Pawn pawn)
        {
            RimKataSecondaryRecovery recovery = FindRecovery(pawn);
            if (recovery?.phase != RimKataSecondaryRecoveryPhase.PrimaryRecovered)
            {
                return null;
            }

            if (pawn == null || pawn.Dead)
            {
                RemoveRecovery(pawn);
                return null;
            }

            int now = Find.TickManager?.TicksGame ?? 0;
            if (now < recovery.nextRetryTick
                || pawn.Spawned != true
                || pawn.Downed
                || pawn.Awake() != true)
            {
                return null;
            }

            if (pawn.equipment?.Primary != recovery.primary)
            {
                RemoveRecovery(pawn);
                return null;
            }

            ThingWithComps currentSecondary = Get(pawn);
            if (currentSecondary == recovery.secondary)
            {
                RemoveRecovery(pawn);
                return null;
            }

            if (currentSecondary != null
                || recovery.secondary == null
                || recovery.secondary.Destroyed)
            {
                RemoveRecovery(pawn);
                return null;
            }

            ThingWithComps secondary = recovery.secondary;
            if (!secondary.Spawned
                || secondary.Map != pawn.Map
                || !RimKataWeaponSlotUtility.CanUseSecondarySlot(pawn)
                || !RimKataWeaponSlotUtility.CanEquipAsSecondary(pawn, secondary)
                || !EquipmentUtility.CanEquip(secondary, pawn)
                || !pawn.CanReserveAndReach(
                    secondary,
                    PathEndMode.Touch,
                    Danger.Deadly))
            {
                recovery.nextRetryTick = now + 60;
                return null;
            }

            Job job = JobMaker.MakeJob(
                RimKataDefOf.RimKata_EquipSecondary,
                secondary);
            job.ignoreForbidden = true;
            recovery.phase = RimKataSecondaryRecoveryPhase.JobIssued;
            return job;
        }

        public void NotifySecondaryRecoveryJobFinished(
            Pawn pawn,
            ThingWithComps weapon,
            bool equipped)
        {
            RimKataSecondaryRecovery recovery = FindRecovery(pawn);
            if (recovery == null || recovery.secondary != weapon)
            {
                return;
            }

            if (equipped && Get(pawn) == weapon)
            {
                RemoveRecovery(pawn);
                return;
            }

            if (recovery.phase == RimKataSecondaryRecoveryPhase.JobIssued)
            {
                recovery.phase = RimKataSecondaryRecoveryPhase.PrimaryRecovered;
                recovery.nextRetryTick = (Find.TickManager?.TicksGame ?? 0) + 60;
            }
        }

        private void Cleanup()
        {
            for (int i = pawns.Count - 1; i >= 0; i--)
            {
                if (!StillSecondary(pawns[i], weapons[i]))
                {
                    RemoveAt(i);
                }
            }
        }

        private void CleanupRecoveries()
        {
            for (int i = recoveries.Count - 1; i >= 0; i--)
            {
                RimKataSecondaryRecovery recovery = recoveries[i];
                Pawn pawn = recovery?.pawn;
                if (pawn == null
                    || pawn.Dead
                    || recovery.primary == null
                    || recovery.primary.Destroyed
                    || recovery.secondary == null
                    || recovery.secondary.Destroyed)
                {
                    recoveries.RemoveAt(i);
                    continue;
                }

                if (recovery.phase == RimKataSecondaryRecoveryPhase.WaitingForPrimary)
                {
                    if (pawn.equipment?.Primary == recovery.primary)
                    {
                        recovery.phase = RimKataSecondaryRecoveryPhase.PrimaryRecovered;
                        recovery.nextRetryTick = Find.TickManager?.TicksGame ?? 0;
                    }
                    else
                    {
                        continue;
                    }
                }

                if (recovery.phase == RimKataSecondaryRecoveryPhase.PrimaryPickupIssued)
                {
                    if (pawn.equipment?.Primary == recovery.primary)
                    {
                        recovery.phase = RimKataSecondaryRecoveryPhase.PrimaryRecovered;
                        recovery.nextRetryTick = Find.TickManager?.TicksGame ?? 0;
                    }

                    Job job = pawn.CurJob;
                    bool exactPickupStillRunning = pawn.equipment?.Primary != recovery.primary
                        && job?.def == JobDefOf.Equip
                        && job.playerForced != true
                        && job.GetTarget(TargetIndex.A).Thing == recovery.primary;
                    if (exactPickupStillRunning)
                    {
                        continue;
                    }

                    if (pawn.equipment?.Primary != recovery.primary
                        && pawn.mindState?.droppedWeapon == recovery.primary)
                    {
                        recovery.phase = RimKataSecondaryRecoveryPhase.WaitingForPrimary;
                        continue;
                    }

                    if (pawn.equipment?.Primary != recovery.primary)
                    {
                        recoveries.RemoveAt(i);
                        continue;
                    }
                }

                if (pawn.equipment?.Primary != recovery.primary)
                {
                    recoveries.RemoveAt(i);
                    continue;
                }

                if (StillHeld(pawn, recovery.secondary))
                {
                    recoveries.RemoveAt(i);
                    continue;
                }

                if (recovery.phase == RimKataSecondaryRecoveryPhase.JobIssued
                    && (pawn.CurJobDef != RimKataDefOf.RimKata_EquipSecondary
                        || pawn.CurJob?.GetTarget(TargetIndex.A).Thing
                            != recovery.secondary))
                {
                    recovery.phase = RimKataSecondaryRecoveryPhase.PrimaryRecovered;
                    recovery.nextRetryTick =
                        (Find.TickManager?.TicksGame ?? 0) + 60;
                }
            }
        }

        private RimKataSecondaryRecovery FindRecovery(Pawn pawn)
        {
            for (int i = 0; i < recoveries.Count; i++)
            {
                RimKataSecondaryRecovery recovery = recoveries[i];
                if (recovery?.pawn == pawn)
                {
                    return recovery;
                }
            }

            return null;
        }

        private void RemoveRecovery(Pawn pawn)
        {
            for (int i = recoveries.Count - 1; i >= 0; i--)
            {
                if (recoveries[i]?.pawn == pawn)
                {
                    recoveries.RemoveAt(i);
                }
            }
        }

        private static bool StillSecondary(Pawn pawn, ThingWithComps weapon)
        {
            return StillHeld(pawn, weapon) && pawn.equipment.Primary != weapon;
        }

        private static bool StillHeld(Pawn pawn, ThingWithComps weapon)
        {
            return pawn?.equipment != null
                && weapon != null
                && !weapon.Destroyed
                && pawn.equipment.AllEquipmentListForReading.Contains(weapon);
        }

        private void RemoveAt(int index)
        {
            Pawn pawn = pawns[index];
            pawns.RemoveAt(index);
            weapons.RemoveAt(index);
            RimKataEligibilityCache.NotifySecondaryWeaponChanged(
                pawn,
                null);
        }
    }

    [HarmonyPatch(typeof(Pawn_HealthTracker), "MakeDowned")]
    public static class Patch_PawnHealthTracker_RimKataSecondaryRecovery
    {
        public struct DownedLoadout
        {
            public Pawn pawn;
            public ThingWithComps primary;
            public ThingWithComps secondary;
        }

        public static void Prefix(Pawn ___pawn, out DownedLoadout __state)
        {
            __state = default;
            ThingWithComps primary =
                RimKataWeaponSlotUtility.PrimaryWeapon(___pawn);
            ThingWithComps secondary =
                RimKataWeaponSlotUtility.SecondaryWeapon(___pawn);
            if (primary == null || secondary == null)
            {
                return;
            }

            __state = new DownedLoadout
            {
                pawn = ___pawn,
                primary = primary,
                secondary = secondary
            };
        }

        public static void Postfix(DownedLoadout __state)
        {
            Pawn pawn = __state.pawn;
            if (pawn?.Downed != true
                || pawn.mindState?.droppedWeapon != __state.primary
                || pawn.equipment?.AllEquipmentListForReading.Contains(
                    __state.primary) == true
                || pawn.equipment?.AllEquipmentListForReading.Contains(
                    __state.secondary) == true)
            {
                return;
            }

            RimKataSecondaryWeaponRegistry.CurrentRegistry
                ?.RecordDroppedLoadout(
                    pawn,
                    __state.primary,
                    __state.secondary);
        }
    }

    [HarmonyPatch(typeof(JobGiver_PickupDroppedWeapon), "TryGiveJob")]
    public static class Patch_JobGiverPickupDroppedWeapon_RimKataSecondaryRecovery
    {
        public static void Postfix(Pawn pawn, ref Job __result)
        {
            RimKataSecondaryWeaponRegistry registry =
                RimKataSecondaryWeaponRegistry.CurrentRegistry;
            if (__result != null)
            {
                registry?.NotifyAutomaticPrimaryPickupIssued(pawn, __result);
                return;
            }

            __result = registry?.TryMakeSecondaryRecoveryJob(pawn);
        }
    }

    public static class RimKataWeaponSlotUtility
    {
        private sealed class CachedCombatVerb
        {
            public Verb verb;
            public int nullResolvedTick = -1;
        }

        private sealed class PawnCombatVerbCache
        {
            public readonly Dictionary<ThingWithComps, CachedCombatVerb> byWeapon = new Dictionary<ThingWithComps, CachedCombatVerb>();
        }

        private static readonly ConditionalWeakTable<Pawn, PawnCombatVerbCache> CombatVerbCaches = new ConditionalWeakTable<Pawn, PawnCombatVerbCache>();
        private static readonly ConditionalWeakTable<Pawn, PawnCombatVerbCache>.CreateValueCallback CreateCombatVerbCache = delegate { return new PawnCombatVerbCache(); };

        public static ThingWithComps PrimaryWeapon(Pawn pawn)
        {
            return pawn?.equipment?.Primary;
        }

        public static ThingWithComps SecondaryWeapon(Pawn pawn)
        {
            return RimKataSecondaryWeaponRegistry.CurrentRegistry?.Get(pawn);
        }

        public static bool IsSecondaryWeapon(Pawn pawn, Thing thing)
        {
            return thing != null && SecondaryWeapon(pawn) == thing;
        }

        public static Verb PrimaryVerb(ThingWithComps weapon)
        {
            CompEquippable equippable = weapon?.TryGetComp<CompEquippable>();
            Verb primary = equippable?.PrimaryVerb;
            if (primary != null)
            {
                return primary;
            }

            List<Verb> verbs = equippable?.AllVerbs;
            if (verbs != null)
            {
                for (int i = 0; i < verbs.Count; i++)
                {
                    if (verbs[i]?.IsMeleeAttack == true)
                    {
                        return verbs[i];
                    }
                }
            }

            return null;
        }

        public static Verb CombatVerb(Pawn pawn, ThingWithComps weapon)
        {
            Verb verb = PrimaryVerb(weapon);
            if (verb != null)
            {
                return verb;
            }

            if (pawn == null || weapon == null)
            {
                return null;
            }

            PawnCombatVerbCache cache = CombatVerbCaches.GetValue(pawn, CreateCombatVerbCache);
            lock (cache)
            {
                int currentTick = Find.TickManager?.TicksGame ?? -1;
                if (cache.byWeapon.TryGetValue(weapon, out CachedCombatVerb cached))
                {
                    if (cached.verb != null)
                    {
                        return cached.verb;
                    }

                    if (cached.nullResolvedTick == currentTick)
                    {
                        return null;
                    }
                }
                else
                {
                    cached = new CachedCombatVerb();
                    cache.byWeapon.Add(weapon, cached);
                }

                cached.verb = ResolveMeleeCombatVerb(pawn, weapon);
                cached.nullResolvedTick = cached.verb == null ? currentTick : -1;
                return cached.verb;
            }
        }

        public static Verb CombatVerbForContext(
            Pawn pawn,
            ThingWithComps weapon,
            bool closeCombatContext)
        {
            return CombatVerb(pawn, weapon);
        }

        private static Verb ResolveMeleeCombatVerb(Pawn pawn, ThingWithComps weapon)
        {
            List<VerbEntry> meleeVerbs =
                pawn.meleeVerbs?.GetUpdatedAvailableVerbsList(false);
            if (meleeVerbs == null)
            {
                return null;
            }

            for (int i = 0; i < meleeVerbs.Count; i++)
            {
                Verb verb = meleeVerbs[i].verb;
                if (verb?.IsMeleeAttack == true
                    && verb.EquipmentSource == weapon)
                {
                    return verb;
                }
            }

            return null;
        }

        public static void InvalidateCombatVerbCache(Pawn pawn)
        {
            if (pawn != null)
            {
                CombatVerbCaches.Remove(pawn);
            }
        }

        public static bool CanUseSecondarySlot(Pawn pawn)
        {
            ThingWithComps primary = PrimaryWeapon(pawn);
            return RimKataMod.Settings?.secondaryWeaponEnabled != false
                && RimKataEligibility.HasRimKataAccess(pawn)
                && RimKataEquipmentUtility.IsWeaponEnabled(primary?.def)
                && RimKataGripUtility.GripTypeFor(primary?.def) == RimKataGripType.OneHand;
        }

        public static bool CanAttackTargetWithoutRushing(Pawn pawn, Thing target)
        {
            if (pawn == null || target == null || !target.Spawned || target.Map != pawn.Map)
            {
                return false;
            }

            ThingWithComps primary = PrimaryWeapon(pawn);
            if (CanWeaponAttackTargetWithoutRushing(pawn, primary, target))
            {
                return true;
            }

            ThingWithComps secondary = CanUseSecondarySlot(pawn)
                ? SecondaryWeapon(pawn)
                : null;
            return CanWeaponAttackTargetWithoutRushing(pawn, secondary, target);
        }

        public static bool CanWeaponAttackTargetWithoutRushing(
            Pawn pawn,
            ThingWithComps weapon,
            Thing target)
        {
            if (!RimKataEquipmentUtility.IsWeaponEnabled(weapon?.def))
            {
                return false;
            }

            Verb verb = CombatVerb(pawn, weapon);
            if (verb == null)
            {
                return false;
            }

            bool adjacent = pawn.CanReachImmediate(target, PathEndMode.Touch);
            if (verb.IsMeleeAttack)
            {
                return adjacent && verb.Available();
            }

            bool available = adjacent
                ? RimKataEligibility.IsRangedVerbAvailableInCloseCombat(pawn, verb)
                : verb.Available();
            return available
                && !verb.ApparelPreventsShooting()
                && (adjacent || verb.CanHitTarget(target));
        }

        public static Verb BestRangedCombatVerb(Pawn pawn, Thing target = null)
        {
            if (pawn == null)
            {
                return null;
            }

            ThingWithComps primary = PrimaryWeapon(pawn);
            ThingWithComps secondary = CanUseSecondarySlot(pawn)
                ? SecondaryWeapon(pawn)
                : null;
            Verb primaryVerb = CombatVerb(pawn, primary);
            Verb secondaryVerb = CombatVerb(pawn, secondary);

            bool primaryValid = RangedVerbCanAttack(pawn, primaryVerb, target);
            bool secondaryValid = RangedVerbCanAttack(pawn, secondaryVerb, target);
            if (primaryValid && secondaryValid)
            {
                if (target != null)
                {
                    return primaryVerb;
                }

                return secondaryVerb.EffectiveRange > primaryVerb.EffectiveRange
                    ? secondaryVerb
                    : primaryVerb;
            }

            return primaryValid
                ? primaryVerb
                : secondaryValid
                    ? secondaryVerb
                    : null;
        }

        private static bool RangedVerbCanAttack(Pawn pawn, Verb verb, Thing target)
        {
            if (verb == null
                || verb.IsMeleeAttack
                || verb.ApparelPreventsShooting())
            {
                return false;
            }

            if (target == null)
            {
                return verb.Available();
            }

            bool adjacent = pawn.CanReachImmediate(target, PathEndMode.Touch);
            bool available = adjacent
                ? RimKataEligibility.IsRangedVerbAvailableInCloseCombat(pawn, verb)
                : verb.Available();
            return available && (adjacent || verb.CanHitTarget(target));
        }

        public static bool CanEquipAsSecondary(Pawn pawn, ThingWithComps weapon)
        {
            return pawn != null
                && weapon != null
                && weapon.def?.equipmentType == EquipmentType.Primary
                && weapon != PrimaryWeapon(pawn)
                && weapon != SecondaryWeapon(pawn)
                && CanUseSecondarySlot(pawn)
                && RimKataEquipmentUtility.IsWeaponEnabled(weapon.def)
                && RimKataGripUtility.GripTypeFor(weapon.def) == RimKataGripType.OneHand;
        }

        public static bool TryEquipSecondary(
            Pawn pawn,
            ThingWithComps weapon,
            bool destroyExisting = false)
        {
            if (!CanEquipAsSecondary(pawn, weapon))
            {
                return false;
            }

            ThingWithComps existing = SecondaryWeapon(pawn);
            bool existingDestroyed = false;
            if (existing != null && destroyExisting)
            {
                ThingOwner equipmentOwner = pawn.equipment.GetDirectlyHeldThings();
                if (!equipmentOwner.Remove(existing))
                {
                    return false;
                }

                RimKataSecondaryWeaponRegistry.CurrentRegistry?.Clear(pawn, existing);
                if (!existing.Destroyed)
                {
                    existing.Destroy(DestroyMode.Vanish);
                }
                existingDestroyed = true;
            }
            else if (existing != null && !pawn.equipment.TryDropEquipment(existing, out ThingWithComps _, pawn.Position, false))
            {
                return false;
            }

            ThingOwner owner = pawn.equipment.GetDirectlyHeldThings();
            bool added = owner.TryAdd(weapon, false);
            if (added)
            {
                RimKataSecondaryWeaponRegistry.CurrentRegistry?.Set(pawn, weapon);
                NotifyLoadoutChanged(pawn);
            }
            else if (existingDestroyed)
            {
                NotifyLoadoutChanged(pawn);
            }

            return added;
        }

        public static void ValidateLoadout(Pawn pawn, bool dropInvalidSecondary = true)
        {
            ThingWithComps primary = PrimaryWeapon(pawn);
            ThingWithComps secondary = SecondaryWeapon(pawn);
            if (secondary == null)
            {
                return;
            }

            bool valid = RimKataMod.Settings?.secondaryWeaponEnabled != false
                && RimKataEligibility.HasRimKataAccess(pawn)
                && RimKataEquipmentUtility.IsWeaponEnabled(primary?.def)
                && RimKataEquipmentUtility.IsWeaponEnabled(secondary.def)
                && RimKataGripUtility.GripTypeFor(primary.def) == RimKataGripType.OneHand
                && RimKataGripUtility.GripTypeFor(secondary.def) == RimKataGripType.OneHand;
            if (valid || !dropInvalidSecondary)
            {
                return;
            }

            MoveOutOfEquipment(pawn, secondary);
            RimKataSecondaryWeaponRegistry.CurrentRegistry?.Clear(pawn, secondary);

            NotifyLoadoutChanged(pawn);
        }

        public static bool TrySwapPrimarySecondary(Pawn pawn)
        {
            if (pawn?.equipment == null)
            {
                return false;
            }

            ThingWithComps primary = PrimaryWeapon(pawn);
            ThingWithComps secondary = SecondaryWeapon(pawn);

            if (primary == null || secondary == null)
            {
                return false;
            }

            List<ThingWithComps> equipment = pawn.equipment.AllEquipmentListForReading;

            int primaryIndex = equipment.IndexOf(primary);
            int secondaryIndex = equipment.IndexOf(secondary);

            if (primaryIndex < 0 || secondaryIndex < 0)
            {
                return false;
            }

            equipment[primaryIndex] = secondary;
            equipment[secondaryIndex] = primary;

            RimKataSecondaryWeaponRegistry.CurrentRegistry?.Set(pawn, primary);

            NotifyLoadoutChanged(pawn);
            return true;
        }

        public static void NotifyLoadoutChanged(Pawn pawn)
        {
            InvalidateCombatVerbCache(pawn);
            RimKataDualWeaponController.NotifyLoadoutChanged(pawn);
        }

        public static void NormalizeAllSpawnedLoadouts()
        {
            if (Current.Game == null)
            {
                return;
            }

            List<Map> maps = Find.Maps;
            for (int mapIndex = 0; mapIndex < maps.Count; mapIndex++)
            {
                IReadOnlyList<Pawn> pawns = maps[mapIndex].mapPawns.AllPawnsSpawned;
                for (int pawnIndex = pawns.Count - 1; pawnIndex >= 0; pawnIndex--)
                {
                    ValidateLoadout(pawns[pawnIndex]);
                }
            }
        }

        public static void NotifyCombatFeaturesChanged()
        {
            if (Current.Game == null)
            {
                return;
            }

            List<Map> maps = Find.Maps;
            for (int mapIndex = 0; mapIndex < maps.Count; mapIndex++)
            {
                IReadOnlyList<Pawn> pawns = maps[mapIndex].mapPawns.AllPawnsSpawned;
                for (int pawnIndex = pawns.Count - 1; pawnIndex >= 0; pawnIndex--)
                {
                    Pawn pawn = pawns[pawnIndex];
                    ValidateLoadout(pawn);
                    RimKataDualWeaponController.Reset(pawn, true);
                }
            }
        }

        private static void MoveOutOfEquipment(Pawn pawn, ThingWithComps weapon)
        {
            if (pawn?.equipment == null || weapon == null)
            {
                return;
            }

            if (pawn.Spawned)
            {
                pawn.equipment.TryDropEquipment(weapon, out ThingWithComps _, pawn.Position, false);
                return;
            }

            ThingOwner owner = pawn.equipment.GetDirectlyHeldThings();
            owner.Remove(weapon);
            if (pawn.inventory?.innerContainer?.TryAdd(weapon, false) != true)
            {
                owner.TryAdd(weapon, false);
            }
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SpawnSetup))]
    public static class Patch_Pawn_RimKataAiSecondaryWeapon
    {
        public static void Prefix(Pawn __instance, out bool __state)
        {
            __state = __instance?.EverSeenByPlayer == true;
        }

        public static void Postfix(Pawn __instance, bool respawningAfterLoad, bool __state)
        {
            RimKataSettings settings = RimKataMod.Settings;
            if (respawningAfterLoad
                || __state
                || settings?.secondaryWeaponEnabled == false
                || settings?.aiSecondaryWeaponChancePercent <= 0f)
            {
                return;
            }

            Pawn pawn = __instance;
            LongEventHandler.ExecuteWhenFinished(delegate
            {
                TryGenerateSecondaryWeapon(pawn);
            });
        }

        private static void TryGenerateSecondaryWeapon(Pawn pawn)
        {
            if (pawn?.Spawned != true
                || pawn.Map == null
                || pawn.IsPlayerControlled
                || pawn.Faction == Faction.OfPlayer
                || pawn.HostFaction == Faction.OfPlayer
                || pawn.RaceProps?.Humanlike != true
                || pawn.equipment == null)
            {
                return;
            }

            RimKataSettings settings = RimKataMod.Settings;
            if (settings == null
                || settings.secondaryWeaponEnabled == false
                || settings.aiSecondaryWeaponChancePercent <= 0f
                || !RimKataEligibility.HasRimKataAccess(pawn)
                || !RimKataWeaponSlotUtility.CanUseSecondarySlot(pawn)
                || RimKataWeaponSlotUtility.SecondaryWeapon(pawn) != null
                || HasUnregisteredPrimaryEquipment(pawn)
                || (settings.AiSecondaryWeaponChance < 1f && !Rand.Chance(settings.AiSecondaryWeaponChance)))
            {
                return;
            }

            List<ThingDef> candidates = RimKataEquipmentUtility.EnabledOneHandGeneratableWeapons;

            if (candidates.Count == 0)
            {
                return;
            }

            ThingDef selectedDef = candidates.RandomElement();
            ThingWithComps weapon = ThingMaker.MakeThing(selectedDef, GenStuff.RandomStuffFor(selectedDef)) as ThingWithComps;
            if (weapon == null)
            {
                return;
            }

            PawnGenerator.PostProcessGeneratedGear(weapon, pawn);
            if ((!EquipmentUtility.CanEquip(weapon, pawn) || !RimKataWeaponSlotUtility.TryEquipSecondary(pawn, weapon)) && !weapon.Destroyed)
            {
                weapon.Destroy(DestroyMode.Vanish);
            }
        }

        private static bool HasUnregisteredPrimaryEquipment(Pawn pawn)
        {
            ThingWithComps primary = pawn.equipment.Primary;
            List<ThingWithComps> equipment = pawn.equipment.AllEquipmentListForReading;
            for (int i = 0; i < equipment.Count; i++)
            {
                ThingWithComps item = equipment[i];
                if (item != primary && item?.def?.equipmentType == EquipmentType.Primary)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public sealed class JobDriver_RimKataEquipSecondary : JobDriver
    {
        private const TargetIndex WeaponIndex = TargetIndex.A;
        private bool equipped;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.GetTarget(WeaponIndex), job, 1, 1, null, errorOnFailed);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref equipped, "rimKataSecondaryEquipped");
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            AddFinishAction(delegate
            {
                RimKataSecondaryWeaponRegistry.CurrentRegistry
                    ?.NotifySecondaryRecoveryJobFinished(
                        pawn,
                        job.GetTarget(WeaponIndex).Thing as ThingWithComps,
                        equipped);
            });

            this.FailOnDestroyedNullOrForbidden(WeaponIndex);
            this.FailOnBurningImmobile(WeaponIndex);
            yield return Toils_Goto.GotoThing(WeaponIndex, PathEndMode.Touch);

            Toil equip = ToilMaker.MakeToil("RimKataEquipSecondary");
            equip.initAction = delegate
            {
                ThingWithComps source = job.GetTarget(WeaponIndex).Thing as ThingWithComps;
                if (!RimKataWeaponSlotUtility.CanEquipAsSecondary(pawn, source))
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                ThingWithComps weapon = source;
                if (source.stackCount > 1)
                {
                    weapon = source.SplitOff(1) as ThingWithComps;
                }

                if (weapon?.Spawned == true)
                {
                    weapon.DeSpawn();
                }

                if (!RimKataWeaponSlotUtility.TryEquipSecondary(pawn, weapon))
                {
                    if (weapon != null && !weapon.Spawned && pawn.Spawned)
                    {
                        GenPlace.TryPlaceThing(weapon, pawn.Position, pawn.Map, ThingPlaceMode.Near);
                    }

                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                equipped = true;
            };
            equip.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return equip;
        }
    }

    public sealed class FloatMenuOptionProvider_RimKataEquipSecondary : FloatMenuOptionProvider_Equip
    {
        protected override FloatMenuOption GetSingleOptionFor(Thing clickedThing, FloatMenuContext context)
        {
            Pawn pawn = context.FirstSelectedPawn;
            ThingWithComps weapon = clickedThing as ThingWithComps;
            if (!RimKataWeaponSlotUtility.CanEquipAsSecondary(pawn, weapon))
            {
                return null;
            }

            FloatMenuOption option = base.GetSingleOptionFor(clickedThing, context);
            if (option == null)
            {
                return null;
            }

            if (option.Disabled)
            {
                option.Label += " [RimKata]";
                return option;
            }

            option.Label = "Equip".Translate(clickedThing.LabelShort) + " [RimKata]";
            Action startJob = delegate
            {
                clickedThing.SetForbidden(false, true);
                Job job = JobMaker.MakeJob(RimKataDefOf.RimKata_EquipSecondary, clickedThing);
                pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc, false);
                FleckMaker.Static(clickedThing.DrawPos, clickedThing.MapHeld, FleckDefOf.FeedbackEquip, 1f);
                PlayerKnowledgeDatabase.KnowledgeDemonstrated(ConceptDefOf.EquippingWeapons, KnowledgeAmount.Total);
            };

            option.action = delegate
            {
                string confirmation = EquipmentUtility.GetPersonaWeaponConfirmationText(clickedThing, pawn);
                if (confirmation.NullOrEmpty())
                {
                    startJob();
                }
                else
                {
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(confirmation, startJob, false, null, WindowLayer.Dialog));
                }
            };
            return option;
        }
    }

    public sealed class FloatMenuOptionProvider_RimKataDropSecondary
    : FloatMenuOptionProvider
    {
        protected override bool Drafted => true;

        protected override bool Undrafted => true;

        protected override bool Multiselect => false;

        protected override bool CanSelfTarget => true;

        protected override bool AppliesInt(FloatMenuContext context)
        {
            Pawn pawn = context.FirstSelectedPawn;

            return pawn != null && RimKataWeaponSlotUtility.SecondaryWeapon(pawn) != null;
        }

        protected override FloatMenuOption GetSingleOptionFor(
            Pawn clickedPawn,
            FloatMenuContext context)
        {
            if (clickedPawn != context.FirstSelectedPawn)
            {
                return null;
            }

            ThingWithComps secondary = RimKataWeaponSlotUtility.SecondaryWeapon(clickedPawn);

            if (secondary == null)
            {
                return null;
            }

            if (clickedPawn.IsQuestLodger()
                && !EquipmentUtility.QuestLodgerCanUnequip(secondary, clickedPawn))
            {
                return new FloatMenuOption("CannotDrop".Translate(secondary.Label, secondary) + ": " + "QuestRelated".Translate().CapitalizeFirst(), null);
            }

            Action action = delegate
            {
                if (RimKataWeaponSlotUtility.SecondaryWeapon(clickedPawn) != secondary)
                {
                    return;
                }

                clickedPawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.DropEquipment, secondary), JobTag.Misc);
            };

            return new FloatMenuOption("Drop".Translate( secondary.Label, secondary), action, secondary, Color.white, MenuOptionPriority.Default, null, clickedPawn);
        }
    }

    [HarmonyPatch]
    public static class Patch_PawnEquipmentTracker_RimKataMakeRoom
    {
        private sealed class PendingReplacement
        {
            public ThingWithComps secondary;
            public ThingWithComps expectedNewWeapon;
            public int tick;
        }

        private static readonly Dictionary<Pawn_EquipmentTracker, PendingReplacement> Pending = new Dictionary<Pawn_EquipmentTracker, PendingReplacement>();

        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(Pawn_EquipmentTracker),
                nameof(Pawn_EquipmentTracker.MakeRoomFor),
                new[] 
                { 
                    typeof(ThingWithComps), 
                    typeof(ThingWithComps).MakeByRefType() 
                });
        }

        public static bool Prefix(
            Pawn_EquipmentTracker __instance,
            Pawn ___pawn,
            ThingWithComps eq,
            ref ThingWithComps __1)
        {
            Pending.Remove(__instance);
            ThingWithComps primary = __instance.Primary;
            ThingWithComps secondary = RimKataWeaponSlotUtility.SecondaryWeapon(___pawn);
            if (primary == null || eq == null)
            {
                return true;
            }

            bool primaryEnabled = RimKataEquipmentUtility.IsWeaponEnabled(primary.def);
            bool incomingEnabled = RimKataEquipmentUtility.IsWeaponEnabled(eq.def);
            bool incomingTwoHanded = RimKataGripUtility.GripTypeFor(eq.def) == RimKataGripType.TwoHand;

            if (!primaryEnabled && secondary != null)
            {
                __instance.TryDropEquipment(secondary, out ThingWithComps _, ___pawn.Position, false);
                RimKataWeaponSlotUtility.NotifyLoadoutChanged(___pawn);
                return true;
            }

            if (RimKataEligibility.HasRimKataAccess(___pawn) && primaryEnabled && !incomingEnabled)
            {
                DropEnabledWeaponsToSides(___pawn, __instance, primary, secondary, out __1);
                RimKataWeaponSlotUtility.NotifyLoadoutChanged(___pawn);
                return false;
            }

            if (incomingEnabled && incomingTwoHanded && secondary != null)
            {
                __instance.TryDropEquipment(primary, out __1, ___pawn.Position, false);
                __instance.TryDropEquipment(secondary, out ThingWithComps _, ___pawn.Position, false);
                RimKataWeaponSlotUtility.NotifyLoadoutChanged(___pawn);
                return false;
            }

            if (incomingEnabled && !incomingTwoHanded && secondary != null)
            {
                Pending[__instance] = new PendingReplacement
                {
                    secondary = secondary,
                    expectedNewWeapon = eq,
                    tick = Find.TickManager?.TicksGame ?? -1
                };
            }

            return true;
        }

        public static bool TryConsumePending(
            Pawn_EquipmentTracker tracker,
            ThingWithComps incoming,
            out ThingWithComps secondary)
        {
            secondary = null;
            if (!Pending.TryGetValue(tracker, out PendingReplacement pending))
            {
                return false;
            }

            Pending.Remove(tracker);
            int now = Find.TickManager?.TicksGame ?? -1;
            if (pending.expectedNewWeapon != incoming || (pending.tick >= 0 && now != pending.tick) || tracker.Primary != pending.secondary)
            {
                return false;
            }

            secondary = pending.secondary;
            return true;
        }

        private static void DropEnabledWeaponsToSides(
            Pawn pawn,
            Pawn_EquipmentTracker tracker,
            ThingWithComps primary,
            ThingWithComps secondary,
            out ThingWithComps droppedPrimary)
        {
            IntVec3 right = pawn.Position + pawn.Rotation.RighthandCell;
            IntVec3 left = pawn.Position - pawn.Rotation.RighthandCell;
            bool primaryRight = Rand.Bool;
            TryDropAtSide(pawn, tracker, primary, out droppedPrimary, primaryRight ? right : left, primaryRight ? left : right);
            if (secondary != null)
            {
                TryDropAtSide(pawn, tracker, secondary, out ThingWithComps _, primaryRight ? left : right, primaryRight ? right : left);
            }
        }

        private static bool TryDropAtSide(
            Pawn pawn,
            Pawn_EquipmentTracker tracker,
            ThingWithComps equipment,
            out ThingWithComps dropped,
            IntVec3 preferred,
            IntVec3 opposite)
        {
            if (TryDropExactly(pawn, tracker, equipment, preferred, out dropped) || TryDropExactly(pawn, tracker, equipment, opposite, out dropped))
            {
                return true;
            }

            return tracker.TryDropEquipment(equipment, out dropped, pawn.Position, false);
        }

        private static bool TryDropExactly(
            Pawn pawn,
            Pawn_EquipmentTracker tracker,
            ThingWithComps equipment,
            IntVec3 cell,
            out ThingWithComps dropped)
        {
            dropped = null;
            if (!cell.InBounds(pawn.Map))
            {
                return false;
            }

            bool result = tracker.GetDirectlyHeldThings().TryDrop(equipment, cell, pawn.Map, ThingPlaceMode.Near, out Thing raw, null, candidate => candidate == cell, true);
            dropped = raw as ThingWithComps;
            if (result && raw != null)
            {
                raw.SetForbidden(false, false);
            }

            return result;
        }
    }

    [HarmonyPatch(typeof(Pawn_EquipmentTracker), nameof(Pawn_EquipmentTracker.GetGizmos))]
    public static class Patch_PawnEquipmentTracker_RimKataSecondaryGizmo
    {
        private const string SecondaryGizmoLabel = "RimKata";

        public static void Postfix(Pawn ___pawn, ref IEnumerable<Gizmo> __result)
        {
            if (___pawn == null || __result == null)
            {
                return;
            }

            __result = MarkSecondaryWeaponGizmos(___pawn, __result);
        }

        private static IEnumerable<Gizmo> MarkSecondaryWeaponGizmos(Pawn pawn, IEnumerable<Gizmo> gizmos)
        {
            ThingWithComps primary = RimKataWeaponSlotUtility.PrimaryWeapon(pawn);

            ThingWithComps secondary = RimKataWeaponSlotUtility.SecondaryWeapon(pawn);

            if (RimKataMultiSelectAttackGizmoUtility
                .ShouldUseUnifiedAttackGizmo())
            {
                foreach (Gizmo gizmo in gizmos)
                {
                    if (gizmo is Command_VerbTarget attackCommand)
                    {
                        ThingWithComps commandWeapon =
                            attackCommand.verb?.EquipmentSource;
                        if (commandWeapon != null
                            && (commandWeapon == primary
                                || commandWeapon == secondary))
                        {
                            continue;
                        }
                    }

                    yield return gizmo;
                }

                yield break;
            }

            if (secondary == null)
            {
                foreach (Gizmo gizmo in gizmos)
                {
                    yield return gizmo;
                }

                yield break;
            }

            List<Gizmo> all = new List<Gizmo>(gizmos);
            List<Gizmo> primaryCommands = new List<Gizmo>();
            List<Gizmo> secondaryCommands = new List<Gizmo>();

            int insertionIndex = -1;

            for (int i = 0; i < all.Count; i++)
            {
                if (!(all[i] is Command_VerbTarget command))
                {
                    continue;
                }

                ThingWithComps weapon = command.verb?.EquipmentSource;

                if (weapon == primary)
                {
                    if (insertionIndex < 0)
                    {
                        insertionIndex = i;
                    }

                    primaryCommands.Add(command);
                }
                else if (weapon == secondary)
                {
                    if (insertionIndex < 0)
                    {
                        insertionIndex = i;
                    }

                    command.defaultLabel = SecondaryGizmoLabel;
                    secondaryCommands.Add(command);
                }
            }

            bool inserted = false;
            for (int i = 0; i < all.Count; i++)
            {
                Gizmo gizmo = all[i];

                bool pairCommand = false;
                if (gizmo is Command_VerbTarget command)
                {
                    ThingWithComps weapon = command.verb?.EquipmentSource;
                    pairCommand = weapon == primary || weapon == secondary;
                }

                if (!inserted && i == insertionIndex)
                {
                    inserted = true;                    
                    for (int j = 0; j < primaryCommands.Count; j++)
                    {
                        yield return primaryCommands[j];
                    }

                    for (int j = 0; j < secondaryCommands.Count; j++)
                    {
                        yield return secondaryCommands[j];
                    }
                }

                if (pairCommand)
                {
                    continue;
                }

                yield return gizmo;
            }
        }
    }

    [HarmonyPatch(typeof(Command_VerbTarget), nameof(Command_VerbTarget.ProcessInput))]
    public static class Patch_CommandVerbTarget_RimKataSecondarySwap
    {
        [ThreadStatic] private static bool replayingAttack;

        public static bool Prefix(Command_VerbTarget __instance)
        {
            return replayingAttack || !IsRimKataSecondaryCommand(__instance);
        }

        public static bool IsRimKataSecondaryCommand(
            Command_VerbTarget command)
        {
            Verb verb = command?.verb;
            Pawn pawn = verb?.CasterPawn;
            return pawn != null
                && command.defaultLabel == "RimKata"
                && verb.EquipmentSource != null
                && RimKataWeaponSlotUtility.IsSecondaryWeapon(
                    pawn,
                    verb.EquipmentSource);
        }

        public static void OpenMenu(
            Command_VerbTarget representative,
            List<Gizmo> group)
        {
            if (!IsRimKataSecondaryCommand(representative))
            {
                return;
            }

            List<Command_VerbTarget> commands =
                new List<Command_VerbTarget>();
            if (group != null)
            {
                for (int i = 0; i < group.Count; i++)
                {
                    if (group[i] is Command_VerbTarget command
                        && IsRimKataSecondaryCommand(command)
                        && !commands.Contains(command))
                    {
                        commands.Add(command);
                    }
                }
            }

            if (!commands.Contains(representative))
            {
                commands.Add(representative);
            }

            List<Pawn> pawns = new List<Pawn>();
            for (int i = 0; i < commands.Count; i++)
            {
                Pawn pawn = commands[i].verb?.CasterPawn;
                if (pawn != null && !pawns.Contains(pawn))
                {
                    pawns.Add(pawn);
                }
            }

            List<FloatMenuOption> options = new List<FloatMenuOption>
            {
                new FloatMenuOption(
                    "KRWF_RimKata_SecondaryMenuAttack".Translate(),
                    delegate
                    {
                        ReplayAttackCommands(representative, commands);
                    })
            };

            bool swapBlocked = pawns.Count == 0;
            for (int i = 0; i < pawns.Count && !swapBlocked; i++)
            {
                Pawn pawn = pawns[i];
                swapBlocked = RimKataWeaponSlotUtility.PrimaryWeapon(pawn) == null
                    || RimKataWeaponSlotUtility.SecondaryWeapon(pawn) == null
                    || RimKataDualWeaponController.IsWeaponSwapBlocked(pawn);
            }

            if (swapBlocked)
            {
                options.Add(
                    new FloatMenuOption(
                        "KRWF_RimKata_SecondaryMenuSwapCombat".Translate(),
                        null));
            }
            else
            {
                options.Add(
                    new FloatMenuOption(
                        "KRWF_RimKata_SecondaryMenuSwap".Translate(),
                        delegate
                        {
                            for (int i = 0; i < pawns.Count; i++)
                            {
                                Pawn pawn = pawns[i];
                                if (RimKataWeaponSlotUtility.PrimaryWeapon(pawn) != null
                                    && RimKataWeaponSlotUtility.SecondaryWeapon(pawn) != null
                                    && !RimKataDualWeaponController.IsWeaponSwapBlocked(pawn))
                                {
                                    RimKataDualWeaponController.RequestWeaponSwap(pawn);
                                }
                            }
                        }));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static void ReplayAttackCommands(
            Command_VerbTarget representative,
            List<Command_VerbTarget> commands)
        {
            replayingAttack = true;
            try
            {
                for (int i = 0; i < commands.Count; i++)
                {
                    Command_VerbTarget command = commands[i];
                    if (command == representative
                        || command.Disabled
                        || !IsRimKataSecondaryCommand(command))
                    {
                        continue;
                    }

                    command.ProcessInput(Event.current);
                }

                if (!representative.Disabled
                    && IsRimKataSecondaryCommand(representative))
                {
                    representative.ProcessInput(Event.current);
                }
            }
            finally
            {
                replayingAttack = false;
            }
        }
    }

    [HarmonyPatch(typeof(Gizmo), nameof(Gizmo.ProcessGroupInput))]
    public static class Patch_Gizmo_RimKataSecondaryMenu
    {
        public static void Postfix(
            Gizmo __instance,
            List<Gizmo> group)
        {
            if (__instance is Command_VerbTarget command)
            {
                Patch_CommandVerbTarget_RimKataSecondarySwap.OpenMenu(
                    command,
                    group);
            }
        }
    }

    [HarmonyPatch(typeof(VerbTracker), "CreateVerbTargetCommand")]
    public static class Patch_VerbTracker_RimKataUndraftedSecondaryGizmo
    {
        public static void Postfix(
            Verb verb,
            ref Command_VerbTarget __result)
        {
            if (__result == null || verb == null || !verb.CasterIsPawn)
            {
                return;
            }

            Pawn pawn = verb.CasterPawn;
            ThingWithComps weapon = verb.EquipmentSource;

            if (pawn == null || weapon == null || pawn.Drafted || !RimKataEligibility.HasRimKataAccess(pawn) || !RimKataWeaponSlotUtility.IsSecondaryWeapon(pawn, weapon))
            {
                return;
            }

            string notDraftedReason = "IsNotDrafted".Translate(pawn.LabelShort, pawn);

            if (!__result.Disabled || __result.disabledReason != notDraftedReason)
            {
                return;
            }

            __result.Disabled = false;
            __result.disabledReason = null;
        }
    }

    [HarmonyPatch(typeof(Command_VerbTarget), nameof(Command_VerbTarget.GroupsWith))]
    public static class Patch_CommandVerbTarget_RimKataSecondaryGrouping
    {
        public static void Postfix(
            Command_VerbTarget __instance,
            Gizmo other,
            ref bool __result)
        {
            if (!__result || !(other is Command_VerbTarget otherCommand))
            {
                return;
            }

            if (IsSecondaryCommand(__instance) != IsSecondaryCommand(otherCommand))
            {
                __result = false;
            }
        }

        private static bool IsSecondaryCommand(Command_VerbTarget command)
        {
            Verb verb = command?.verb;
            Pawn pawn = verb?.CasterPawn;
            return pawn != null && verb.EquipmentSource != null && RimKataWeaponSlotUtility.IsSecondaryWeapon(pawn, verb.EquipmentSource);
        }
    }

    [HarmonyPatch(typeof(PawnAttackGizmoUtility), "ShouldUseMeleeAttackGizmo")]
    public static class Patch_PawnAttackGizmoUtility_RimKataMeleeAttackGizmo
    {
        public static bool Prefix(
            Pawn pawn,
            ref bool __result)
        {
            if (pawn != null && RimKataMod.Settings?.targetRushEnabled == true && RimKataEligibility.HasRimKataAccess(pawn) && RimKataEquipmentUtility.IsPrimaryWeaponEnabled(pawn))
            {
                __result = false;
                return false;
            }

            return true;
        }

        public static void Postfix(
            Pawn pawn,
            ref bool __result)
        {
            if (RimKataMultiSelectAttackGizmoUtility
                .ShouldUseUnifiedAttackGizmo())
            {
                __result = false;
            }
            else if (pawn?.Drafted == true)
            {
                __result = true;
            }
        }
    }

    public static class RimKataMultiSelectAttackGizmoUtility
    {
        public static bool ShouldUseUnifiedAttackGizmo()
        {
            List<Pawn> selectedPawns = Find.Selector?.SelectedPawns;
            if (selectedPawns == null || selectedPawns.Count < 2)
            {
                return false;
            }

            int selectedPlayerPawns = 0;
            bool hasUsableSecondary = false;
            for (int i = 0; i < selectedPawns.Count; i++)
            {
                Pawn pawn = selectedPawns[i];
                if (pawn?.Spawned != true
                    || !pawn.IsPlayerControlled)
                {
                    continue;
                }

                selectedPlayerPawns++;
                if (!hasUsableSecondary
                    && RimKataAutomaticRangeVisualUtility
                        .CanDrawAutomaticSearchRange(pawn)
                    && RimKataWeaponSlotUtility.CanUseSecondarySlot(pawn)
                    && RimKataWeaponSlotUtility.SecondaryWeapon(pawn) != null)
                {
                    hasUsableSecondary = true;
                }
            }

            return selectedPlayerPawns >= 2 && hasUsableSecondary;
        }

        public static bool HasSelectedPawnWithAutomaticSearchRange()
        {
            List<Pawn> selectedPawns = Find.Selector?.SelectedPawns;
            if (selectedPawns == null)
            {
                return false;
            }

            for (int i = 0; i < selectedPawns.Count; i++)
            {
                Pawn pawn = selectedPawns[i];
                if (pawn?.Spawned == true
                    && pawn.IsPlayerControlled
                    && RimKataAutomaticRangeVisualUtility
                        .CanDrawAutomaticSearchRange(pawn))
                {
                    return true;
                }
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(PawnAttackGizmoUtility), "ShouldUseSquadAttackGizmo")]
    public static class Patch_PawnAttackGizmoUtility_RimKataSquadAttack
    {
        public static void Postfix(ref bool __result)
        {
            if (!__result
                && RimKataMultiSelectAttackGizmoUtility
                    .ShouldUseUnifiedAttackGizmo())
            {
                __result = true;
            }
        }
    }

    [HarmonyPatch(typeof(PawnAttackGizmoUtility), "AtLeastOneSelectedPlayerPawnHasRangedWeapon")]
    public static class Patch_PawnAttackGizmoUtility_RimKataRangedWeapon
    {
        public static void Postfix(ref bool __result)
        {
            if (__result)
            {
                return;
            }

            List<object> selected = Find.Selector.SelectedObjectsListForReading;

            for (int i = 0; i < selected.Count; i++)
            {
                if (!(selected[i] is Pawn pawn) || !pawn.IsPlayerControlled)
                {
                    continue;
                }

                ThingWithComps secondary = RimKataWeaponSlotUtility.SecondaryWeapon(pawn);

                if (secondary?.def?.IsRangedWeapon == true)
                {
                    __result = true;
                    return;
                }
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_EquipmentTracker), nameof(Pawn_EquipmentTracker.AddEquipment))]
    public static class Patch_PawnEquipmentTracker_RimKataRestoreSecondary
    {
        public static bool Prefix(
            Pawn_EquipmentTracker __instance,
            Pawn ___pawn,
            ThingWithComps newEq,
            out bool __state)
        {
            RimKataSecondaryWeaponRegistry registry =
                RimKataSecondaryWeaponRegistry.CurrentRegistry;
            __state = registry?.IsExpectedAutomaticPrimaryRecovery(
                ___pawn,
                newEq) == true;

            if (Patch_PawnEquipmentTracker_RimKataMakeRoom.TryConsumePending(__instance, newEq, out ThingWithComps secondary))
            {
                return !TryInsertPrimaryBeforeSecondary(__instance, ___pawn, newEq, secondary);
            }

            secondary = registry?.GetRegistered(___pawn);
            bool registeredSecondaryWasPromoted = newEq?.def?.equipmentType == EquipmentType.Primary && secondary != null && __instance.Primary == secondary;
            if (!registeredSecondaryWasPromoted)
            {
                return true;
            }

            bool canRemainSecondary = RimKataWeaponSlotUtility.CanUseSecondarySlot(___pawn) && RimKataEquipmentUtility.IsWeaponEnabled(newEq.def) && RimKataGripUtility.GripTypeFor(newEq.def) == RimKataGripType.OneHand;
            if (canRemainSecondary)
            {
                return !TryInsertPrimaryBeforeSecondary(__instance, ___pawn, newEq, secondary);
            }

            MovePromotedSecondaryOut(__instance, ___pawn, secondary);
            registry?.Clear(___pawn, secondary);
            RimKataWeaponSlotUtility.NotifyLoadoutChanged(___pawn);
            return true;
        }

        public static void Postfix(
            Pawn_EquipmentTracker __instance,
            Pawn ___pawn,
            ThingWithComps newEq,
            bool __state)
        {
            if (__state && __instance.Primary == newEq)
            {
                RimKataSecondaryWeaponRegistry.CurrentRegistry
                    ?.ConfirmAutomaticPrimaryRecovery(___pawn, newEq);
            }
        }

        private static bool TryInsertPrimaryBeforeSecondary(
            Pawn_EquipmentTracker tracker,
            Pawn pawn,
            ThingWithComps incoming,
            ThingWithComps secondary)
        {
            ThingOwner owner = tracker.GetDirectlyHeldThings();
            if (!owner.TryAdd(incoming, false))
            {
                return false;
            }

            List<ThingWithComps> equipment = tracker.AllEquipmentListForReading;
            int secondaryIndex = equipment.IndexOf(secondary);
            int incomingIndex = equipment.IndexOf(incoming);
            if (secondaryIndex < 0 || incomingIndex < 0)
            {
                owner.Remove(incoming);
                if (pawn.Spawned)
                {
                    GenPlace.TryPlaceThing(incoming, pawn.Position, pawn.Map, ThingPlaceMode.Near);
                }

                return false;
            }

            equipment.RemoveAt(incomingIndex);
            equipment.Insert(secondaryIndex, incoming);
            RimKataSecondaryWeaponRegistry.CurrentRegistry?.Set(pawn, secondary);
            if (pawn.mindState != null)
            {
                pawn.mindState.droppedWeapon = null;
            }

            RimKataWeaponSlotUtility.NotifyLoadoutChanged(pawn);
            return true;
        }

        private static void MovePromotedSecondaryOut(
            Pawn_EquipmentTracker tracker,
            Pawn pawn,
            ThingWithComps secondary)
        {
            if (pawn.Spawned)
            {
                tracker.TryDropEquipment(secondary, out ThingWithComps _, pawn.Position, false);
                return;
            }

            ThingOwner owner = tracker.GetDirectlyHeldThings();
            owner.Remove(secondary);
            if (pawn.inventory?.innerContainer?.TryAdd(secondary, false) != true)
            {
                owner.TryAdd(secondary, false);
            }
        }
    }

    [HarmonyPatch]
    public static class Patch_DebugToolsPawns_RimKataSecondaryWeapon
    {
        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(DebugToolsPawns), "Options_SetPrimary", new[] { typeof(Pawn) });
        }

        public static void Postfix(Pawn pawn, List<DebugMenuOption> __result)
        {
            if (__result == null)
            {
                return;
            }

            bool canUseSecondary = RimKataWeaponSlotUtility.CanUseSecondarySlot(pawn);
            string label = canUseSecondary ? "[RimKata]" : "[RimKata] NO";
            Action action = canUseSecondary
                ? (Action)(() => Find.WindowStack.Add(new Dialog_DebugOptionListLister(SecondaryWeaponOptions(pawn), null)))
                : () => { };
            __result.Insert(Math.Min(1, __result.Count), new DebugMenuOption(label, DebugMenuOptionMode.Action, action));
        }

        private static List<DebugMenuOption> SecondaryWeaponOptions(Pawn pawn)
        {
            List<DebugMenuOption> options = new List<DebugMenuOption>();
            List<ThingDef> defs = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < defs.Count; i++)
            {
                ThingDef def = defs[i];
                if (def?.equipmentType != EquipmentType.Primary || !RimKataEquipmentUtility.IsWeaponEnabled(def) || RimKataGripUtility.GripTypeFor(def) != RimKataGripType.OneHand)
                {
                    continue;
                }

                ThingDef capturedDef = def;
                options.Add(new DebugMenuOption(capturedDef.defName, DebugMenuOptionMode.Action, () => EquipSecondary(pawn, capturedDef)));
            }

            options.Sort((left, right) => string.Compare(left.label, right.label, StringComparison.Ordinal));
            return options;
        }

        private static void EquipSecondary(Pawn pawn, ThingDef weaponDef)
        {
            if (!RimKataWeaponSlotUtility.CanUseSecondarySlot(pawn) || weaponDef == null || !RimKataEquipmentUtility.IsWeaponEnabled(weaponDef) || RimKataGripUtility.GripTypeFor(weaponDef) != RimKataGripType.OneHand)
            {
                return;
            }

            ThingWithComps weapon = ThingMaker.MakeThing(weaponDef, GenStuff.RandomStuffFor(weaponDef)) as ThingWithComps;
            if (weapon != null && !RimKataWeaponSlotUtility.TryEquipSecondary(pawn, weapon, true) && !weapon.Destroyed)
            {
                weapon.Destroy(DestroyMode.Vanish);
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_DraftController), "GetGizmos")]
    public static class Patch_PawnDraftController_RimKataFireAtWill
    {
        public static void Postfix(Pawn_DraftController __instance, ref IEnumerable<Gizmo> __result)
        {
            if (__instance == null
                || __result == null)
            {
                return;
            }

            Pawn pawn = __instance.pawn;
            if (pawn == null
                || !__instance.Drafted)
            {
                return;
            }

            ThingWithComps primary = RimKataWeaponSlotUtility.PrimaryWeapon(pawn);

            if (primary?.def?.IsRangedWeapon == true)
            {
                return;
            }

            if (!RimKataWeaponSlotUtility.CanUseSecondarySlot(pawn))
            {
                return;
            }

            ThingWithComps secondary = RimKataWeaponSlotUtility.SecondaryWeapon(pawn);
            if (secondary?.def?.IsRangedWeapon != true)
            {
                return;
            }

            __result = AppendFireAtWillGizmo(__instance, __result);
        }

        private static IEnumerable<Gizmo>
            AppendFireAtWillGizmo(Pawn_DraftController drafter, IEnumerable<Gizmo> source)

        {
            foreach (Gizmo gizmo in source)
            {
                yield return gizmo;
            }

            yield return new Command_Toggle
            {
                hotKey = KeyBindingDefOf.Misc6,
                isActive = () => drafter.FireAtWill,
                toggleAction = delegate { drafter.FireAtWill = !drafter.FireAtWill; },
                icon = TexCommand.FireAtWill,
                defaultLabel = "CommandFireAtWillLabel".Translate(),
                defaultDesc = "CommandFireAtWillDesc".Translate(),
                tutorTag = "FireAtWillToggle"
            };
        }
    }
}
