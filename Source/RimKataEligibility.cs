using RimWorld;
using Verse;

namespace KRWF.RimKata
{
    public static class RimKataEligibility
    {
        public static bool HasRimKataAccess(Pawn pawn)
        {
            if (pawn == null || !FactionEffectsEnabled(pawn))
            {
                return false;
            }

            if (RimKataMod.Settings?.accessRestrictionsDisabled == true)
            {
                return true;
            }

            return RimKataEligibilityCache.HasAnyAccessSource(pawn);
        }

        public static bool HasActiveGene(Pawn pawn)
        {
            return HasRimKataAccess(pawn);
        }

        public static bool RandomAttackEnabledForPawn(Pawn pawn)
        {
            return pawn != null
                && RimKataMod.Settings?.randomAttackEnabled != false
                && HasRimKataAccess(pawn);
        }

        public static bool FactionEffectsEnabled(Pawn pawn)
        {
            if (pawn == null)
            {
                return false;
            }

            RimKataSettings settings = RimKataMod.Settings;
            if (settings == null)
            {
                return true;
            }

            bool hostileToPlayer = IsHostileToPlayerFaction(pawn);
            return hostileToPlayer
                ? settings.enableHostilePawnEffects
                : settings.enableFriendlyPawnEffects;
        }

        public static bool IsHostileToPlayerFaction(Pawn pawn)
        {
            Faction playerFaction = Faction.OfPlayer;
            return pawn != null
                && playerFaction != null
                && pawn.Faction?.HostileTo(playerFaction) == true;
        }

        public static bool IsWorking(Pawn pawn)
        {
            return pawn != null
                && !pawn.Drafted
                && pawn.CurJob?.workGiverDef != null;
        }

        public static bool CanUseDefense(Pawn pawn)
        {
            return HasRimKataAccess(pawn)
                && IsConsciousAndMobile(pawn)
                && !IsWorking(pawn)
                && pawn.stances?.stunner != null
                && !pawn.stances.stunner.Stunned
                && pawn.health.capacities.CapableOf(PawnCapacityDefOf.Moving);
        }

        public static bool CanRollRangedDodge(Pawn pawn, bool shieldAbsorbed)
        {
            return RimKataMod.Settings?.rangedDodgeEnabled != false
                && !shieldAbsorbed
                && CanUseDefense(pawn);
        }

        public static bool CanRollMeleeDodge(Pawn pawn)
        {
            return CanUseDefense(pawn);
        }

        public static bool CanUseMeleeResponse(Pawn pawn)
        {
            return CanUseDefense(pawn)
                && pawn.kindDef?.canMeleeAttack == true
                && pawn.meleeVerbs != null
                && pawn.meleeVerbs.GetUpdatedAvailableVerbsList(false).Count > 0;
        }

        public static bool CanBeginGunKataAttack(Pawn pawn)
        {
            return HasRimKataAccess(pawn)
                && IsConsciousAndMobile(pawn)
                && !pawn.WorkTagIsDisabled(WorkTags.Violent)
                && RimKataEquipmentUtility.IsPrimaryWeaponEnabled(pawn);
        }

        public static bool CanUseGunKataAttacks(Pawn pawn)
        {
            return CanBeginGunKataAttack(pawn)
                && !IsWorking(pawn)
                && pawn.CurJob?.def == RimKataDefOf.RimKata_Attack;
        }

        public static bool TryGetEnabledRangedVerb(Pawn pawn, out Verb verb)
        {
            if (!TryGetEnabledCombatVerb(pawn, out verb))
            {
                return false;
            }

            return !verb.IsMeleeAttack;
        }

        public static bool TryGetEnabledCombatVerb(Pawn pawn, out Verb verb)
        {
            verb = null;
            if (!CanBeginGunKataAttack(pawn))
            {
                return false;
            }

            verb = RimKataWeaponSlotUtility.CombatVerb(pawn, pawn.equipment?.Primary);
            return verb != null;
        }

        public static bool CanShootWithPrimaryWeapon(Pawn pawn, out Verb verb)
        {
            verb = null;
            if (!CanUseGunKataAttacks(pawn) || !TryGetEnabledRangedVerb(pawn, out verb))
            {
                return false;
            }

            return verb != null
                && verb.Available()
                && !verb.ApparelPreventsShooting();
        }

        public static bool CanShootWithPrimaryWeaponInCloseCombat(Pawn pawn, out Verb verb)
        {
            verb = null;
            if (!CanUseGunKataAttacks(pawn) || !TryGetEnabledRangedVerb(pawn, out verb))
            {
                return false;
            }

            if (verb == null || verb.ApparelPreventsShooting())
            {
                return false;
            }

            return IsRangedVerbAvailableInCloseCombat(pawn, verb);
        }

        public static bool IsRangedVerbAvailableInCloseCombat(Pawn pawn, Verb verb)
        {
            if (RimKataMod.Settings?.closeFireEnabled == false
                || pawn == null
                || verb == null
                || verb.IsMeleeAttack
                || verb.ApparelPreventsShooting())
            {
                return false;
            }

            bool hostileNpc = Faction.OfPlayer != null
                && pawn.Faction?.HostileTo(Faction.OfPlayer) == true;
            if (!hostileNpc || !(verb is Verb_LaunchProjectile) || verb.verbProps == null)
            {
                return verb.Available();
            }

            bool ignoredMeleeThreat = verb.verbProps.ai_ProjectileLaunchingIgnoresMeleeThreats;
            try
            {
                verb.verbProps.ai_ProjectileLaunchingIgnoresMeleeThreats = true;
                return verb.Available();
            }
            finally
            {
                verb.verbProps.ai_ProjectileLaunchingIgnoresMeleeThreats = ignoredMeleeThreat;
            }
        }

        private static bool IsConsciousAndMobile(Pawn pawn)
        {
            return pawn != null
                && pawn.Spawned
                && !pawn.Dead
                && !pawn.Downed
                && pawn.Awake();
        }
    }
}
