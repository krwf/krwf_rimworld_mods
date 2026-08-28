using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;

namespace KRWF.RimKata
{
    public enum RimKataWeaponSlot
    {
        Primary,
        Secondary
    }

    public sealed class RimKataWeaponCycleState : IExposable
    {
        public ThingWithComps weapon;
        public int cooldownTicksRemaining;
        public int warmupTicksRemaining = -1;
        public int warmupTotalTicks;
        public int openingWarmupBonusTicks;
        public bool openingWarmupPending;
        public bool openingSupportDelayConsumed;
        public Thing cachedCandidateTarget;
        public bool cachedCandidateInterception;
        public List<Thing> automaticCandidates = new List<Thing>();
        public bool automaticCandidateCollectionClosed;
        public int pendingCandidateLimitOverride;
        public int activeCandidateLimitOverride;
        public Thing lastFiredTarget;
        public bool firedInCurrentOpening;
        public bool cooldownFromVanillaOpening;
        public int burstShotsRemaining;
        public int burstTicksUntilNextShot;
        public Thing focusedTarget;
        public bool focusedTargetFromAttackGizmo;
        public Thing plannedTarget;
        public IntVec3 plannedTargetCell = IntVec3.Invalid;
        public bool plannedInterception;
        public bool plannedCloseAttack;
        public bool plannedCloseContext;
        public Verb plannedActionVerb;
        public Thing visualTarget;
        public int visualAimTicksRemaining;
        private int lastTimerTick = -1;

        public bool HasPlan => plannedTarget != null;
        public bool IsWarming => warmupTicksRemaining > 0;
        public bool Active => weapon != null
            && (cooldownTicksRemaining > 0
            || warmupTicksRemaining > 0
            || openingWarmupPending
             || cachedCandidateTarget != null
             || HasAutomaticCandidates
            || focusedTarget != null
            || HasPlan
            || visualAimTicksRemaining > 0);

        public bool CombatActive => weapon != null
            && (openingWarmupPending
             || cachedCandidateTarget != null
             || HasAutomaticCandidates
            || focusedTarget != null
            || HasPlan);

        public bool DedicatedActive => weapon != null
            && (cachedCandidateTarget != null
                 || HasAutomaticCandidates
                || focusedTarget != null
                || HasPlan);

        public void ExposeData()
        {
            Scribe_References.Look(ref weapon, "weapon");
            Scribe_Values.Look(ref cooldownTicksRemaining, "cooldownTicksRemaining");
            Scribe_Values.Look(ref warmupTicksRemaining, "warmupTicksRemaining", -1);
            Scribe_Values.Look(ref warmupTotalTicks, "warmupTotalTicks");
            Scribe_Values.Look(ref openingWarmupBonusTicks, "openingWarmupBonusTicks");
            Scribe_Values.Look(ref openingWarmupPending, "openingWarmupPending");
            Scribe_Values.Look(
                ref openingSupportDelayConsumed,
                "openingSupportDelayConsumed");
            Scribe_References.Look(ref cachedCandidateTarget, "cachedCandidateTarget");
            Scribe_Collections.Look(
                ref automaticCandidates,
                "automaticCandidates",
                LookMode.Reference);
            Scribe_Values.Look(
                ref automaticCandidateCollectionClosed,
                "automaticCandidateCollectionClosed");
            Scribe_Values.Look(
                ref pendingCandidateLimitOverride,
                "pendingCandidateLimitOverride");
            Scribe_Values.Look(
                ref activeCandidateLimitOverride,
                "activeCandidateLimitOverride");
            Scribe_Values.Look(
                ref cachedCandidateInterception,
                "cachedCandidateInterception");
            Scribe_References.Look(ref lastFiredTarget, "lastFiredTarget");
            Scribe_Values.Look(ref firedInCurrentOpening, "firedInCurrentOpening");
            Scribe_Values.Look(
                ref cooldownFromVanillaOpening,
                "cooldownFromVanillaOpening");
            Scribe_Values.Look(ref burstShotsRemaining, "burstShotsRemaining");
            Scribe_Values.Look(ref burstTicksUntilNextShot, "burstTicksUntilNextShot");
            Scribe_References.Look(ref focusedTarget, "focusedTarget");
            Scribe_Values.Look(
                ref focusedTargetFromAttackGizmo,
                "focusedTargetFromAttackGizmo");
            Scribe_References.Look(ref plannedTarget, "plannedTarget");
            Scribe_Values.Look(
                ref plannedTargetCell,
                "plannedTargetCell",
                IntVec3.Invalid);
            Scribe_Values.Look(ref plannedInterception, "plannedInterception");
            Scribe_Values.Look(ref plannedCloseAttack, "plannedCloseAttack");
            Scribe_Values.Look(ref plannedCloseContext, "plannedCloseContext");
            Scribe_References.Look(ref visualTarget, "visualTarget");
            Scribe_Values.Look(ref visualAimTicksRemaining, "visualAimTicksRemaining");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                automaticCandidates ??= new List<Thing>();
                for (int i = automaticCandidates.Count - 1; i >= 0; i--)
                {
                    if (automaticCandidates[i] == null)
                    {
                        automaticCandidates.RemoveAt(i);
                    }
                }
                lastTimerTick = -1;
                plannedActionVerb = null;
                cooldownTicksRemaining = Mathf.Max(0, cooldownTicksRemaining);
                pendingCandidateLimitOverride = Mathf.Max(
                    0,
                    pendingCandidateLimitOverride);
                activeCandidateLimitOverride = Mathf.Max(
                    0,
                    activeCandidateLimitOverride);
                if (HasPlan && warmupTicksRemaining <= 0)
                {
                    warmupTicksRemaining = 1;
                    warmupTotalTicks = Mathf.Max(1, warmupTotalTicks);
                }

                if (plannedInterception
                    && !(plannedTarget is Projectile))
                {
                    ClearPlan(false);
                    warmupTicksRemaining = -1;
                    warmupTotalTicks = 0;
                    openingWarmupBonusTicks = 0;
                    openingWarmupPending = false;
                }

                if (cachedCandidateInterception
                    && !(cachedCandidateTarget is Projectile))
                {
                    cachedCandidateTarget = null;
                    cachedCandidateInterception = false;
                }
            }
        }

        public bool Bind(ThingWithComps newWeapon)
        {
            if (weapon == newWeapon)
            {
                return false;
            }

            Reset();
            weapon = newWeapon;
            return true;
        }

        public void TickTimers()
        {
            int currentTick = Find.TickManager?.TicksGame ?? -1;
            if (currentTick >= 0 && lastTimerTick == currentTick)
            {
                return;
            }

            lastTimerTick = currentTick;
            if (cooldownTicksRemaining > 0)
            {
                cooldownTicksRemaining--;
                if (cooldownTicksRemaining <= 0
                    && burstShotsRemaining <= 0)
                {
                    cooldownFromVanillaOpening = false;
                }
            }
            else if (burstShotsRemaining <= 0
                && !openingWarmupPending)
            {
                cooldownFromVanillaOpening = false;
            }

            if (warmupTicksRemaining > 0)
            {
                warmupTicksRemaining--;
            }

            if (visualAimTicksRemaining > 0)
            {
                visualAimTicksRemaining--;
                if (visualAimTicksRemaining <= 0 && !HasPlan)
                {
                    visualTarget = null;
                }
            }

            if (burstTicksUntilNextShot > 0)
            {
                burstTicksUntilNextShot--;
            }
        }

        public void ClearPlan(bool resetWarmup = true)
        {
            plannedTarget = null;
            plannedTargetCell = IntVec3.Invalid;
            plannedInterception = false;
            plannedCloseAttack = false;
            plannedCloseContext = false;
            plannedActionVerb = null;
            burstShotsRemaining = 0;
            burstTicksUntilNextShot = 0;
            if (resetWarmup
                && !openingWarmupPending)
            {
                warmupTicksRemaining = -1;
                warmupTotalTicks = 0;
            }
        }

        public bool HasAutomaticCandidates => automaticCandidates != null
            && automaticCandidates.Count > 0;

        public bool AddAutomaticCandidate(Thing target)
        {
            if (target == null)
            {
                return false;
            }

            automaticCandidates ??= new List<Thing>();
            if (automaticCandidates.Contains(target))
            {
                return false;
            }

            automaticCandidates.Add(target);
            return true;
        }

        public void RemoveAutomaticCandidate(Thing target)
        {
            automaticCandidates?.Remove(target);
            if (cachedCandidateTarget == target)
            {
                cachedCandidateTarget = null;
                cachedCandidateInterception = false;
            }
        }

        public void ClearAutomaticCandidates()
        {
            automaticCandidates?.Clear();
            automaticCandidateCollectionClosed = false;
            pendingCandidateLimitOverride = 0;
            activeCandidateLimitOverride = 0;
            if (!(cachedCandidateTarget is Projectile))
            {
                cachedCandidateTarget = null;
                cachedCandidateInterception = false;
            }
        }

        public void Reset()
        {
            weapon = null;

            cooldownTicksRemaining = 0;

            openingWarmupBonusTicks = 0;
            openingWarmupPending = false;
            openingSupportDelayConsumed = false;

            cachedCandidateTarget = null;
            cachedCandidateInterception = false;
            automaticCandidates?.Clear();
            automaticCandidateCollectionClosed = false;
            pendingCandidateLimitOverride = 0;
            activeCandidateLimitOverride = 0;
            focusedTarget = null;
            focusedTargetFromAttackGizmo = false;
            lastFiredTarget = null;
            firedInCurrentOpening = false;
            cooldownFromVanillaOpening = false;

            visualTarget = null;
            visualAimTicksRemaining = 0;
            lastTimerTick = -1;

            ClearPlan();
        }

        // !!! Debug HUD !!!
        public char DebugState
        {
            get
            {
                if (weapon == null)
                {
                    return 'W';
                }

                if (burstShotsRemaining > 0)
                {
                    return 'F';
                }

                if (warmupTicksRemaining > 0)
                {
                    return 'A';
                }

                if (cooldownTicksRemaining > 0)
                {
                    return 'C';
                }

                return 'W';
            }
        }
    }

    public struct RimKataWeaponVisualData
    {
        public ThingWithComps weapon;
        public LocalTargetInfo target;
        public bool warming;
        public int warmupTicksRemaining;
        public int warmupTotalTicks;
        public int cooldownTicksRemaining;
    }

    public struct RimKataVanillaOpeningAttempt
    {
        public bool prepared;
        public ThingWithComps weapon;
        public Thing target;
    }

    public static class RimKataDualWeaponController
    {
        [ThreadStatic] private static Pawn activePhysicalMeleePawn;
        [ThreadStatic] private static RimKataWeaponCycleState activePhysicalMeleeCycle;
        [ThreadStatic] private static Verb pendingVanillaOpeningVerb;

        public static void Tick(
            Pawn pawn,
            Thing assignedTarget,
            bool playerForced,
            bool killIncappedTarget,
            bool closeCombatContext,
            bool closeTargetResolved = false,
            bool allowAutomaticRangedFire = true)
        {
            if (pawn?.InMentalState == true)
            {
                CancelOffenseForMentalState(pawn);
                return;
            }

            if (pawn?.Map == null || !RimKataEligibility.CanBeginGunKataAttack(pawn))
            {
                Reset(pawn, true);
                return;
            }

            allowAutomaticRangedFire = allowAutomaticRangedFire
                && (!pawn.Drafted
                    || pawn.drafter?.FireAtWill == true);

            RimKataPawnCombatState state = StateFor(pawn, true);
            int currentTick = Find.TickManager.TicksGame;
            if (state.dualLastDrivenTick == currentTick)
            {
                return;
            }

            ThingWithComps primaryWeapon = RimKataWeaponSlotUtility.PrimaryWeapon(pawn);
            ThingWithComps secondaryWeapon = RimKataWeaponSlotUtility.CanUseSecondarySlot(pawn)
                ? RimKataWeaponSlotUtility.SecondaryWeapon(pawn)
                : null;
            if (state.primaryWeaponCycle.weapon != primaryWeapon && state.secondaryWeaponCycle.weapon == primaryWeapon && secondaryWeapon == null)
            {
                RimKataWeaponCycleState promoted = state.secondaryWeaponCycle;
                state.secondaryWeaponCycle = state.primaryWeaponCycle;
                state.primaryWeaponCycle = promoted;
                state.secondaryWeaponCycle.Reset();
            }

            bool loadoutChanged = state.primaryWeaponCycle.Bind(primaryWeapon) | state.secondaryWeaponCycle.Bind(secondaryWeapon);
            if (loadoutChanged)
            {
                state.sharedTargetSearch?.Reset();
            }

            if (NormalizeInvalidInterceptionState(pawn, state)
                && allowAutomaticRangedFire)
            {
                state.QueueIdleProjectileSearchTrigger();
            }

            Thing closeTarget = closeTargetResolved
                && closeCombatContext
                && IsImmediateCloseTarget(
                    pawn,
                    assignedTarget,
                    playerForced,
                    killIncappedTarget)
                    ? assignedTarget
                    : ResolveCloseTarget(
                        pawn,
                        state,
                        assignedTarget,
                        playerForced,
                        killIncappedTarget);
            closeCombatContext = closeTarget != null;
            if (closeCombatContext)
            {
                assignedTarget = closeTarget;
            }
            HandleCloseCombatTransition(
                pawn,
                state,
                closeCombatContext,
                closeTarget);

            if (state.idleProjectileSearchTriggerPending)
            {
                if (allowAutomaticRangedFire)
                {
                    TryCacheSharedCandidate(
                        pawn,
                        state,
                        state.primaryWeaponCycle,
                        assignedTarget);
                    TryCacheSharedCandidate(
                        pawn,
                        state,
                        state.secondaryWeaponCycle,
                        assignedTarget);
                }
                state.ConsumeIdleProjectileSearchTrigger();
                RefreshDualEngagementState(pawn, state);
            }

            if (!allowAutomaticRangedFire && !closeCombatContext)
            {
                SuppressNewAutomaticRangedTargeting(pawn, state);
            }

            RimKataSharedTargetSearch.Prune(pawn, state);
            if (state.AutomaticCandidateCountsChanged())
            {
                state.ResetCandidateSaturationExpansion(true);
                bool hasStoredCandidates =
                    state.primaryWeaponCycle?.HasAutomaticCandidates == true
                    || state.secondaryWeaponCycle?.HasAutomaticCandidates == true;
                bool projectileOnlySearchWork =
                    HasProjectileOnlySearchWork(pawn, state, assignedTarget);
                state.CaptureAutomaticCandidateCounts();
                if ((state.dualEngagementActive || hasStoredCandidates)
                    && !projectileOnlySearchWork
                    && state.sharedTargetSearch?.scanActive != true)
                {
                    RimKataSharedTargetSearch.Begin(
                        pawn,
                        state,
                        pawn.Position);
                }
            }

            if (!closeCombatContext
                && state.AutomaticAttackRequestActive
                && (assignedTarget == null
                    || assignedTarget.Destroyed
                    || !assignedTarget.Spawned))
            {
                assignedTarget = state.automaticAttackRequestTarget;
            }

            if (state.sharedTargetSearch?.scanActive == true)
            {
                AdvanceSharedTargetSearch(pawn, state, assignedTarget);
                RefreshDualEngagementState(pawn, state);
                if (state.dualEngagementActive)
                {
                    if (pawn.Drafted)
                    {
                        state.draftedFireActive = true;
                    }
                }
            }

            if (ShouldPauseFireForDodge(pawn))
            {
                state.dualLastDrivenTick = currentTick;
                return;
            }

            if (!HasCombatContinuity(pawn))
            {
                UpdateBodyAimStance(pawn, state);
                return;
            }

            state.dualLastDrivenTick = currentTick;

            ImportLegacyDraftedState(state);

            state.primaryWeaponCycle.TickTimers();
            state.secondaryWeaponCycle.TickTimers();
            RearmOpeningOwnerIfBothWaiting(state);
            if (MovementBlocksFire(pawn))
            {
                InterruptCycleForMovement(
                    pawn,
                    state.primaryWeaponCycle,
                    closeCombatContext);
                InterruptCycleForMovement(
                    pawn,
                    state.secondaryWeaponCycle,
                    closeCombatContext);
                return;
            }

            bool blockedByStance = StanceBlocksRimKata(pawn);
            PrepareCycle(pawn, state, state.primaryWeaponCycle, assignedTarget, playerForced, killIncappedTarget, closeCombatContext, blockedByStance, allowAutomaticRangedFire);
            PrepareCycle(pawn, state, state.secondaryWeaponCycle, assignedTarget, playerForced, killIncappedTarget, closeCombatContext, blockedByStance, allowAutomaticRangedFire);
            RefreshDualEngagementState(pawn, state);

            if (!blockedByStance && ReadyToAct(state.primaryWeaponCycle))
            {
                ExecuteCycle(pawn, state.primaryWeaponCycle, assignedTarget, playerForced, killIncappedTarget, closeCombatContext, allowAutomaticRangedFire);
            }

            if (!blockedByStance && ReadyToAct(state.secondaryWeaponCycle))
            {
                ExecuteCycle(pawn, state.secondaryWeaponCycle, assignedTarget, playerForced, killIncappedTarget, closeCombatContext, allowAutomaticRangedFire);
            }

            RefreshDualEngagementState(pawn, state);
            UpdateBodyAimStance(pawn, state);
        }
        // !!! Debug HUD !!!
        public static bool TryGetDebugState(
            Pawn pawn,
            out char primaryState,
            out char secondaryState,
            out bool dualEngagementActive,
            out bool combatActive)
        {
            primaryState = 'W';
            secondaryState = 'W';
            dualEngagementActive = false;

            RimKataPawnCombatState state = StateFor(pawn, false);

            if (state != null)
            {
                primaryState = state.primaryWeaponCycle?.DebugState ?? 'W';
                secondaryState = state.secondaryWeaponCycle?.DebugState ?? 'W';
                dualEngagementActive = state.dualEngagementActive;
            }

            combatActive = state?.dualEngagementActive == true;

            return state != null || combatActive;
        }

        internal static bool DebugTryGetExistingUsingState(
            Pawn pawn,
            out bool usingRimKata)
        {
            RimKataPawnCombatState state = StateFor(pawn, false);
            usingRimKata = state?.dualEngagementActive == true;
            return state != null;
        }

        public static void GetDebugWeaponState(
            Pawn pawn,
            ThingWithComps weapon,
            out char debugState,
            out bool vanillaOpeningState)
        {
            debugState = 'W';
            vanillaOpeningState = false;
            if (pawn == null || weapon == null)
            {
                return;
            }

            RimKataPawnCombatState state = StateFor(pawn, false);
            RimKataWeaponCycleState cycle = CycleForWeapon(state, weapon);
            Verb verb = RimKataWeaponSlotUtility.CombatVerb(pawn, weapon);
            if (verb == null)
            {
                return;
            }

            if (verb.Bursting)
            {
                debugState = 'F';
                vanillaOpeningState = RimKataFireContext.ActiveVerb != verb;
                return;
            }

            if (pawn.stances?.curStance is Stance_Warmup warmup
                && warmup.verb == verb)
            {
                debugState = 'A';
                vanillaOpeningState = true;
                return;
            }

            if (pawn.stances?.curStance is Stance_Cooldown cooldown
                && cooldown.verb == verb)
            {
                debugState = 'C';
                vanillaOpeningState = true;
                return;
            }

            debugState = cycle?.DebugState ?? 'W';
            vanillaOpeningState = cycle?.openingWarmupPending == true
                || (cycle?.cooldownFromVanillaOpening == true
                    && (cycle.cooldownTicksRemaining > 0
                        || cycle.burstShotsRemaining > 0));
        }

        public static bool DebugSharedSearchActive(Pawn pawn)
        {
            RimKataPawnCombatState state = StateFor(pawn, false);

            return state?.sharedTargetSearch?.scanActive == true;
        }

        public static bool NotifyPlayerWeaponTarget(
            Pawn pawn,
            Verb verb,
            Thing target,
            bool fromAttackGizmo = false)
        {
            if (!CanUsePlayerWeaponCommand(pawn, verb)
                || target == null
                || target.Destroyed
                || !target.Spawned
                || target.Map != pawn.Map)
            {
                return false;
            }

            ThingWithComps weapon = verb.EquipmentSource as ThingWithComps;
            RimKataPawnCombatState state = StateFor(pawn, true);
            BindCurrentWeapons(pawn, state);
            RimKataWeaponCycleState cycle = CycleForWeapon(state, weapon);
            if (cycle == null)
            {
                return false;
            }

            cycle.focusedTarget = target;
            cycle.focusedTargetFromAttackGizmo = fromAttackGizmo;

            cycle.visualTarget = target;
            cycle.visualAimTicksRemaining = Mathf.Max(
                cycle.visualAimTicksRemaining,
                2);
            state.engagementOwnerWeapon = weapon;
            RefreshDualEngagementState(pawn, state);
            state.dualLastDrivenTick = -1;
            if (pawn.Drafted)
            {
                state.draftedFireActive = true;
            }

            return true;
        }

        public static bool CanUsePlayerWeaponCommand(Pawn pawn, Verb verb)
        {
            if (pawn?.Map == null
                || pawn.InMentalState
                || verb == null
                || verb.IsMeleeAttack
                || !pawn.IsPlayerControlled
                || !RimKataEligibility.CanBeginGunKataAttack(pawn))
            {
                return false;
            }

            ThingWithComps weapon = verb.EquipmentSource as ThingWithComps;
            ThingWithComps primary = RimKataWeaponSlotUtility.PrimaryWeapon(pawn);
            ThingWithComps secondary = RimKataWeaponSlotUtility.CanUseSecondarySlot(pawn)
                ? RimKataWeaponSlotUtility.SecondaryWeapon(pawn)
                : null;
            return weapon != null
                && (weapon == primary || weapon == secondary)
                && verb.CasterPawn == pawn;
        }

        public static bool HasFocusedWeaponTarget(Pawn pawn)
        {
            RimKataPawnCombatState state = StateFor(pawn, false);
            return IsLiveFocusedTarget(pawn, state?.primaryWeaponCycle)
                || IsLiveFocusedTarget(pawn, state?.secondaryWeaponCycle);
        }

        public static bool TryGetFocusedWeaponTarget(
            Pawn pawn,
            ThingWithComps weapon,
            out Thing target,
            out bool fromAttackGizmo)
        {
            target = null;
            fromAttackGizmo = false;
            RimKataPawnCombatState state = StateFor(pawn, false);
            RimKataWeaponCycleState cycle = CycleForWeapon(state, weapon);
            if (!IsLiveFocusedTarget(pawn, cycle))
            {
                return false;
            }

            target = cycle.focusedTarget;
            fromAttackGizmo = cycle.focusedTargetFromAttackGizmo;
            return true;
        }

        public static bool TryNotifyPlayerMeleeCloseTarget(
            Pawn pawn,
            Thing target,
            bool fromAttackGizmo)
        {
            if (!CanNotifyPlayerMeleeCloseTarget(pawn, target))
            {
                return false;
            }

            RimKataPawnCombatState state = StateFor(pawn, true);
            state.RequestCloseAttack(target, fromAttackGizmo);
            RefreshDualEngagementState(pawn, state);
            state.dualLastDrivenTick = -1;
            if (pawn.Drafted)
            {
                state.draftedFireActive = true;
            }

            return state.CloseAttackRequestActive;
        }

        public static bool CanNotifyPlayerMeleeCloseTarget(
            Pawn pawn,
            Thing target)
        {
            return pawn?.Map != null
                && !pawn.InMentalState
                && pawn.IsPlayerControlled
                && RimKataEligibility.CanBeginGunKataAttack(pawn)
                && HasUsableWeapon(pawn, true)
                && target != null
                && target != pawn
                && !target.Destroyed
                && target.Spawned
                && target.Map == pawn.Map
                && RimKataTargeting.IsAutomaticEnemy(pawn, target)
                && target is Pawn targetPawn
                && !targetPawn.Dead
                && !targetPawn.Downed
                && !targetPawn.Crawling
                && !targetPawn.IsPsychologicallyInvisible()
                && pawn.CanReachImmediate(target, PathEndMode.Touch);
        }

        public static bool TryGetAttackGizmoCloseTarget(
            Pawn pawn,
            out Thing target)
        {
            target = null;
            RimKataPawnCombatState state = StateFor(pawn, false);
            if (state?.closeAttackRequestFromAttackGizmo != true
                || !state.CloseAttackRequestActive)
            {
                return false;
            }

            target = state.closeAttackRequestTarget;
            return target != null;
        }

        private static bool IsLiveFocusedTarget(
            Pawn pawn,
            RimKataWeaponCycleState cycle)
        {
            Thing target = cycle?.focusedTarget;
            return pawn?.Map != null
                && target != null
                && !target.Destroyed
                && target.Spawned
                && target.Map == pawn.Map
                && (!(target is Pawn targetPawn)
                    || (!targetPawn.Dead
                        && !targetPawn.Downed
                        && !targetPawn.Crawling
                        && !targetPawn.IsPsychologicallyInvisible()));
        }

        public static bool CanOrderRangedCloseAttack(
            Pawn pawn,
            Verb verb,
            Thing target)
        {
            if (pawn?.Map == null
                || pawn.InMentalState
                || verb == null
                || verb.IsMeleeAttack
                || target == null
                || target == pawn
                || target.Destroyed
                || !target.Spawned
                || target.Map != pawn.Map
                || !target.HostileTo(pawn)
                || target is Pawn targetPawn
                    && (targetPawn.Dead
                        || targetPawn.Crawling
                        || targetPawn.IsPsychologicallyInvisible())
                || !pawn.CanReachImmediate(target, PathEndMode.Touch)
                || !RimKataEligibility.IsRangedVerbAvailableInCloseCombat(
                    pawn,
                    verb))
            {
                return false;
            }

            ThingWithComps weapon = verb.EquipmentSource as ThingWithComps;
            ThingWithComps primary = RimKataWeaponSlotUtility.PrimaryWeapon(pawn);
            ThingWithComps secondary = RimKataWeaponSlotUtility.SecondaryWeapon(pawn);
            return weapon != null
                && (weapon == primary || weapon == secondary);
        }

        public static bool BeginPlayerRangedCloseAttack(
            Pawn pawn,
            Verb verb,
            Thing target)
        {
            if (pawn?.InMentalState == true
                || !CanOrderRangedCloseAttack(pawn, verb, target)
                || !RimKataEligibility.CanBeginGunKataAttack(pawn))
            {
                return false;
            }

            RimKataPawnCombatState state = StateFor(pawn, true);
            BindCurrentWeapons(pawn, state);
            RimKataWeaponCycleState cycle = CycleForWeapon(
                state,
                verb.EquipmentSource as ThingWithComps);
            if (cycle == null)
            {
                return false;
            }

            state.RequestCloseAttack(target);
            HandleCloseCombatTransition(
                pawn,
                state,
                true,
                target);
            cycle.cachedCandidateTarget = null;
            cycle.cachedCandidateInterception = false;
            SetCandidate(cycle, target, false, true, true, true);
            RefreshDualEngagementState(pawn, state);
            state.dualLastDrivenTick = -1;
            if (pawn.Drafted)
            {
                state.draftedFireActive = true;
            }

            pawn.Map.GetComponent<RimKataMapComponent>()?
                .EnterCloseCombat(pawn, target);
            return true;
        }

        public static bool NotifyDraftedMovementCell(Pawn pawn)
        {
            bool dedicatedJob = pawn?.CurJobDef == RimKataDefOf.RimKata_Attack;
            if (pawn?.Map == null
                || (pawn.Drafted != true && !dedicatedJob))
            {
                return false;
            }

            RimKataPawnCombatState state = StateFor(pawn, true);
            IntVec3 currentCell = pawn.Position;
            IntVec3 previousCell = state.draftedMovementSearchCell;
            bool movingFireEnabled = MovingFireEnabledForPawn(pawn);
            bool movedToAnotherCell = previousCell.IsValid
                && previousCell != currentCell;
            state.draftedMovementSearchCell = currentCell;
            if (movingFireEnabled
                && (pawn.pather?.MovingNow == true || movedToAnotherCell))
            {
                state.RefreshMovementFireContinuity();
            }

            if (!movingFireEnabled)
            {
                state.ConsumeDraftedMovementSearchTrigger();
                return false;
            }

            if (movedToAnotherCell)
            {
                BindCurrentWeapons(pawn, state);
            }

            bool searchInProgress = MovementSearchInProgress(state);
            if (state.DraftedMovementSearchTriggerPending)
            {
                if (searchInProgress)
                {
                    state.ConsumeDraftedMovementSearchTrigger();
                    return true;
                }

                if (TryBeginMovementSearch(pawn, state, currentCell))
                {
                    state.ConsumeDraftedMovementSearchTrigger();
                    return true;
                }

                if (LongestAutomaticRangeVerb(pawn) == null)
                {
                    state.ConsumeDraftedMovementSearchTrigger();
                    return false;
                }

                return true;
            }

            if (!movedToAnotherCell)
            {
                return false;
            }

            if (searchInProgress)
            {
                return true;
            }

            return TryBeginMovementSearch(pawn, state, currentCell);
        }

        public static void QueuePlayerMovementSearch(Pawn pawn)
        {
            if (pawn?.Map == null
                || pawn.InMentalState
                || pawn.Drafted != true
                || pawn.drafter?.FireAtWill != true
                || (pawn.CurJobDef != JobDefOf.Goto
                    && pawn.CurJobDef != JobDefOf.AttackMelee)
                || pawn.CurJob?.playerForced != true
                || !MovingFireEnabledForPawn(pawn)
                || !RimKataEligibility.CanBeginGunKataAttack(pawn)
                || (RimKataWeaponSlotUtility.PrimaryWeapon(pawn) == null
                    && RimKataWeaponSlotUtility.SecondaryWeapon(pawn) == null))
            {
                return;
            }

            RimKataPawnCombatState state = StateFor(pawn, true);
            BindCurrentWeapons(pawn, state);
            state.ConsumeDraftedMovementSearchTrigger();
            RimKataSharedTargetSearch.Begin(pawn, state, pawn.Position);
            state.draftedFireActive = true;
        }

        public static void QueueIdleProjectileSearch(Pawn pawn)
        {
            if (!CanAcceptIdleProjectileSearch(pawn))
            {
                return;
            }

            RimKataPawnCombatState state = StateFor(pawn, true);
            BindCurrentWeapons(pawn, state);
            state.QueueIdleProjectileSearchTrigger();
            TryCacheSharedCandidate(
                pawn,
                state,
                state.primaryWeaponCycle,
                null);
            TryCacheSharedCandidate(
                pawn,
                state,
                state.secondaryWeaponCycle,
                null);
            bool cachedProjectile =
                state.primaryWeaponCycle?.cachedCandidateInterception == true
                || state.secondaryWeaponCycle?.cachedCandidateInterception
                    == true;
            if (!cachedProjectile)
            {
                state.ConsumeIdleProjectileSearchTrigger();
                RefreshDualEngagementState(pawn, state);
                return;
            }

            RefreshDualEngagementState(pawn, state);
            if (pawn.Drafted)
            {
                return;
            }

            state.projectileWakeResumeJob = pawn.CurJob;
            QueueDedicatedFollowupJob(pawn, null);
            if (!state.dedicatedFollowupJobPending)
            {
                state.projectileWakeResumeJob = null;
            }
        }

        public static bool CanAcceptIdleProjectileSearch(Pawn pawn)
        {
            if (pawn?.Map == null
                || !pawn.Spawned
                || pawn.Dead
                || pawn.Downed
                || !pawn.Awake()
                || pawn.InMentalState
                || pawn.IsBurning()
                || !pawn.IsPlayerControlled
                || !RimKataEligibility.CanBeginGunKataAttack(pawn))
            {
                return false;
            }

            bool moving = pawn.pather?.MovingNow == true;
            Job currentJob = pawn.CurJob;
            if (pawn.Drafted)
            {
                if (pawn.drafter?.FireAtWill != true || moving)
                {
                    return false;
                }
            }
            else if (currentJob?.playerForced == true
                || pawn.carryTracker?.CarriedThing != null)
            {
                return false;
            }

            JobDef jobDef = pawn.CurJobDef;
            if (!moving
                && jobDef != JobDefOf.Wait
                && jobDef != JobDefOf.Wait_Combat
                && jobDef?.defName != "Wait_MaintainPosture"
                && jobDef?.defName != "GotoWander"
                && jobDef?.defName != "Wait_Wander")
            {
                return false;
            }

            RimKataPawnCombatState state = StateFor(pawn, false);
            if (state != null)
            {
                NormalizeInvalidInterceptionState(pawn, state);
                RimKataSharedTargetSearch.Prune(pawn, state);
            }
            if (HasCombatContinuity(pawn))
            {
                return false;
            }

            ThingWithComps primary = RimKataWeaponSlotUtility.PrimaryWeapon(pawn);
            ThingWithComps secondary =
                RimKataWeaponSlotUtility.CanUseSecondarySlot(pawn)
                    ? RimKataWeaponSlotUtility.SecondaryWeapon(pawn)
                    : null;
            Verb primaryVerb = RimKataWeaponSlotUtility.CombatVerb(pawn, primary);
            Verb secondaryVerb = RimKataWeaponSlotUtility.CombatVerb(pawn, secondary);
            return primaryVerb is Verb_LaunchProjectile
                || secondaryVerb is Verb_LaunchProjectile;
        }

        private static bool MovementSearchInProgress(RimKataPawnCombatState state)
        {
            return state?.sharedTargetSearch?.scanActive == true;
        }

        private static bool TryBeginMovementSearch(
            Pawn pawn,
            RimKataPawnCombatState state,
            IntVec3 origin)
        {
            BindCurrentWeapons(pawn, state);
            if (!RimKataSharedTargetSearch.Begin(pawn, state, origin))
            {
                return false;
            }
            return true;
        }

        public static void ClearDraftedMovementTracking(Pawn pawn)
        {
            StateFor(pawn, false)?.ClearDraftedMovementSearchTracking();
        }

        private static void SuppressNewAutomaticRangedTargeting(
            Pawn pawn,
            RimKataPawnCombatState state)
        {
            if (state == null)
            {
                return;
            }

            bool primaryNeedsSharedSearch =
                SuppressNewAutomaticRangedTargeting(
                    pawn,
                    state.primaryWeaponCycle);
            bool secondaryNeedsSharedSearch =
                SuppressNewAutomaticRangedTargeting(
                    pawn,
                    state.secondaryWeaponCycle);
            if (primaryNeedsSharedSearch || secondaryNeedsSharedSearch)
            {
                return;
            }

            state.sharedTargetSearch?.Reset();
        }

        private static bool SuppressNewAutomaticRangedTargeting(
            Pawn pawn,
            RimKataWeaponCycleState cycle)
        {
            if (cycle?.weapon == null)
            {
                return false;
            }

            Verb verb = RimKataWeaponSlotUtility.CombatVerb(
                pawn,
                cycle.weapon);
            if (verb?.IsMeleeAttack == true)
            {
                return true;
            }

            cycle.ClearAutomaticCandidates();
            cycle.cachedCandidateTarget = null;
            cycle.cachedCandidateInterception = false;
            return false;
        }

        private static bool MovingFireEnabledForPawn(Pawn pawn)
        {
            return RimKataMod.Settings?.movingFireEnabled != false;
        }

        internal static bool CounterattackControlEnabled(Pawn pawn)
        {
            return pawn != null
                && RimKataEligibility.CanBeginGunKataAttack(pawn)
                && (RimKataEligibility.RandomAttackEnabledForPawn(pawn)
                    || RimKataMod.Settings?.targetRushEnabled != false);
        }

        internal static bool ShouldPauseFireForDodge(Pawn pawn)
        {
            return RimKataMod.Settings?.movingFireEnabled == false
                && RimKataDodgeMovementUtility.IsVisualLocked(pawn);
        }

        public static bool ShouldSuppressVanillaCast(
            Pawn pawn,
            Verb verb,
            LocalTargetInfo target)
        {
            if (pawn?.InMentalState == true)
            {
                return false;
            }

            if (RimKataAutomaticCastSuppression.ActiveFor(pawn))
            {
                return true;
            }

            if (pawn?.Map == null
                || verb == null
                || !target.IsValid
                || !target.HasThing
                || RimKataFireContext.ActiveVerb != null)
            {
                return false;
            }

            ThingWithComps weapon = verb.EquipmentSource as ThingWithComps;
            ThingWithComps primary = RimKataWeaponSlotUtility.PrimaryWeapon(pawn);
            ThingWithComps secondary = RimKataWeaponSlotUtility.CanUseSecondarySlot(pawn)
                ? RimKataWeaponSlotUtility.SecondaryWeapon(pawn)
                : null;
            if (weapon == null || (weapon != primary && weapon != secondary))
            {
                return false;
            }

            if (!CounterattackControlEnabled(pawn)
                && IsConfigurableCounterattackOpening(pawn, pawn.CurJob))
            {
                return false;
            }

            RimKataPawnCombatState state = StateFor(pawn, false);
            RimKataWeaponCycleState cycle = CycleForWeapon(state, weapon);
            if (cycle?.cooldownTicksRemaining > 0)
            {
                return true;
            }

            if (IsDedicatedFollowupActive(pawn))
            {
                return true;
            }

            return !verb.IsMeleeAttack
                && MovingFireEnabledForPawn(pawn)
                && (pawn.pather?.MovingNow == true
                    || state?.MovementFireContinuityActive == true);
        }

        public static void RequestWeaponSwap(Pawn pawn)
        {
            if (pawn?.Map == null)
            {
                return;
            }

            ThingWithComps primary = RimKataWeaponSlotUtility.PrimaryWeapon(pawn);
            ThingWithComps secondary = RimKataWeaponSlotUtility.SecondaryWeapon(pawn);

            if (primary == null || secondary == null)
            {
                return;
            }

            RimKataPawnCombatState state = StateFor(pawn, true);
            BindCurrentWeapons(pawn, state);
            if (IsWeaponSwapBlocked(pawn))
            {
                return;
            }

            state.weaponSwapPending = false;
            RimKataWeaponSlotUtility.TrySwapPrimarySecondary(pawn);
        }

        public static bool IsWeaponSwapBlocked(Pawn pawn)
        {
            if (pawn?.Map == null)
            {
                return true;
            }

            RimKataPawnCombatState state = StateFor(pawn, false);
            bool cycleBusy = state?.primaryWeaponCycle?.Active == true
                || state?.secondaryWeaponCycle?.Active == true;
            bool matchingBusyStance =
                pawn.stances?.curStance is Stance_Busy busy
                && busy.verb?.EquipmentSource is ThingWithComps busyWeapon
                && (busyWeapon == RimKataWeaponSlotUtility.PrimaryWeapon(pawn)
                    || busyWeapon == RimKataWeaponSlotUtility.SecondaryWeapon(pawn));
            return cycleBusy
                || state?.dedicatedFollowupJobPending == true
                || state?.ResponsePoseActive == true
                || pawn.CurJobDef == RimKataDefOf.RimKata_Attack
                || matchingBusyStance;
        }

        public static bool DebugWeaponSwapPending(Pawn pawn)
        {
            return StateFor(pawn, false)?.weaponSwapPending == true;
        }

        public static bool TryApplyResponseCooldown(
            Pawn pawn,
            ThingWithComps weapon,
            Verb verb,
            LocalTargetInfo focus)
        {
            if (pawn?.Map == null
                || weapon == null
                || verb == null)
            {
                return false;
            }

            RimKataPawnCombatState state = StateFor( pawn, true);
            BindCurrentWeapons(pawn, state);
            RimKataWeaponCycleState cycle = CycleForWeapon(state, weapon);
            Verb boundVerb = RimKataWeaponSlotUtility.CombatVerb(
                pawn,
                weapon);
            if (cycle == null
                || boundVerb == null
                || boundVerb.EquipmentSource != weapon
                || verb.EquipmentSource != weapon)
            {
                return false;
            }

            verb = boundVerb;

            cycle.cooldownTicksRemaining = RimKataCombatMath.CooldownTicksForSingleShot(verb, pawn, true);
            cycle.cooldownFromVanillaOpening = false;

            cycle.openingWarmupBonusTicks = 0;
            cycle.openingWarmupPending = false;


            cycle.ClearPlan();

            RimKataSharedTargetSearch.Prune(pawn, state);
            bool responseTargetQueued = false;
            if (focus.HasThing
                && cycle.automaticCandidates?.Contains(focus.Thing) == true)
            {
                cycle.cachedCandidateTarget = focus.Thing;
                cycle.cachedCandidateInterception = false;
                responseTargetQueued = true;
            }

            if (responseTargetQueued)
            {
                cycle.visualTarget = focus.Thing;
                cycle.lastFiredTarget = focus.Thing;
                cycle.visualAimTicksRemaining = Mathf.Max(
                    1,
                    cycle.cooldownTicksRemaining);
            }
            else
            {
                cycle.visualTarget = null;
                cycle.visualAimTicksRemaining = 0;
            }

            RefreshDualEngagementState(pawn, state);

            return true;
        }

        public static bool IsResponseTargetQueued(
            Pawn pawn,
            ThingWithComps weapon,
            LocalTargetInfo focus)
        {
            if (pawn?.Map == null
                || weapon == null
                || !focus.HasThing)
            {
                return false;
            }

            RimKataPawnCombatState state = StateFor(pawn, false);
            RimKataWeaponCycleState cycle = CycleForWeapon(state, weapon);
            return cycle?.cachedCandidateTarget == focus.Thing
                && cycle.automaticCandidates?.Contains(focus.Thing) == true
                && !cycle.cachedCandidateInterception;
        }

        public static RimKataVanillaOpeningAttempt PrepareVanillaOpening(
            Pawn pawn,
            Verb verb,
            LocalTargetInfo castTarget)
        {
            RimKataVanillaOpeningAttempt attempt = default(RimKataVanillaOpeningAttempt);
            if (pawn?.Map == null
                || pawn.InMentalState
                || verb == null
                || !castTarget.IsValid
                || !castTarget.HasThing
                || RimKataFireContext.ActiveVerb != null
                || !RimKataEligibility.CanBeginGunKataAttack(pawn))
            {
                return attempt;
            }

            if (!verb.IsMeleeAttack
                && !RimKataEligibility.RandomAttackEnabledForPawn(pawn)
                && IsConfigurableCounterattackOpening(pawn, pawn.CurJob))
            {
                return attempt;
            }

            ThingWithComps primaryWeapon = RimKataWeaponSlotUtility.PrimaryWeapon(pawn);
            ThingWithComps firedWeapon = verb.EquipmentSource as ThingWithComps;
            ThingWithComps secondaryWeapon = RimKataWeaponSlotUtility.CanUseSecondarySlot(pawn)
                ? RimKataWeaponSlotUtility.SecondaryWeapon(pawn)
                : null;
            if (primaryWeapon == null
                || firedWeapon == null
                || (firedWeapon != primaryWeapon && firedWeapon != secondaryWeapon)
                || verb.CasterPawn != pawn
                || verb.EquipmentSource != firedWeapon)
            {
                return attempt;
            }

            RimKataPawnCombatState state = StateFor(pawn, false);
            if (IsDedicatedFollowupActive(pawn))
            {
                return attempt;
            }

            Thing currentTarget = castTarget.Thing;
            bool playerForced = pawn.CurJob?.playerForced == true;
            bool killIncappedTarget = pawn.CurJob?.killIncappedTarget == true;
            bool closeContext = pawn.CanReachImmediate(
                currentTarget,
                PathEndMode.Touch);
            if (!ValidOpeningTarget(
                pawn,
                currentTarget,
                playerForced,
                killIncappedTarget,
                closeContext))
            {
                return attempt;
            }

            attempt.prepared = true;
            attempt.weapon = firedWeapon;
            attempt.target = currentTarget;
            pendingVanillaOpeningVerb = verb;
            return attempt;
        }

        public static void CommitVanillaOpening(
            Pawn pawn,
            Verb verb,
            RimKataVanillaOpeningAttempt attempt)
        {
            if (!attempt.prepared
                || pawn?.Map == null
                || pawn.InMentalState
                || verb == null
                || attempt.weapon == null
                || attempt.target == null)
            {
                if (pawn?.InMentalState == true)
                {
                    CancelOffenseForMentalState(pawn);
                }
                return;
            }

            Thing target = attempt.target;
            bool playerForced = pawn.CurJob?.playerForced == true;
            bool killIncappedTarget = pawn.CurJob?.killIncappedTarget == true;
            bool closeContext = pawn.CanReachImmediate(target, PathEndMode.Touch);
            if (verb.EquipmentSource != attempt.weapon
                || !ValidOpeningTarget(
                    pawn,
                    target,
                    playerForced,
                    killIncappedTarget,
                    closeContext))
            {
                return;
            }

            Stance_Warmup warmup = pawn.stances?.curStance as Stance_Warmup;
            bool matchingWarmup = warmup?.verb == verb
                && warmup.focusTarg.HasThing
                && warmup.focusTarg.Thing == target;
            Stance_Cooldown cooldown = pawn.stances?.curStance as Stance_Cooldown;
            bool matchingCooldown = cooldown?.verb == verb
                && cooldown.focusTarg.HasThing
                && cooldown.focusTarg.Thing == target;
            if (!matchingWarmup && !matchingCooldown)
            {
                return;
            }

            RimKataPawnCombatState state = StateFor(pawn, true);
            BindCurrentWeapons(pawn, state);
            RimKataWeaponCycleState openingCycle = CycleForWeapon(
                state,
                attempt.weapon);
            if (openingCycle == null)
            {
                return;
            }

            state.engagementOwnerWeapon = attempt.weapon;

            RimKataSharedTargetSearch.TryAddKnownAutomaticTarget(
                pawn,
                state,
                target);
            RimKataSharedTargetSearch.Begin(
                pawn,
                state,
                pawn.Position);

            if (matchingWarmup)
            {
                SetCandidate(
                    openingCycle,
                    target,
                    false,
                    closeContext,
                    closeContext,
                    true);
                openingCycle.plannedActionVerb = verb;
                openingCycle.warmupTotalTicks = Mathf.Max(0, warmup.ticksLeft);
                openingCycle.warmupTicksRemaining =
                    openingCycle.warmupTotalTicks;
            }
            else
            {
                int cooldownTicks = Mathf.Max(1, cooldown.ticksLeft);
                openingCycle.cooldownTicksRemaining = cooldownTicks;
                openingCycle.cooldownFromVanillaOpening = true;
                openingCycle.firedInCurrentOpening = true;
                openingCycle.lastFiredTarget = target;
                openingCycle.visualTarget = target;
                openingCycle.visualAimTicksRemaining = Mathf.Max(
                    RimKataCombatTuning.PostShotAimTicks,
                    cooldownTicks + 2);
                RecordFirstFiredWeapon(state, attempt.weapon);
            }

            RefreshDualEngagementState(pawn, state);
            state.dualLastDrivenTick = Find.TickManager.TicksGame;
            pawn.stances.SetStance(new Stance_Mobile());
            if (verb.IsMeleeAttack)
            {
                UpdateBodyAimStance(pawn, state);
            }
            QueueDedicatedFollowupJob(pawn, target);
        }

        public static void FinishVanillaOpeningAttempt(Verb verb)
        {
            if (pendingVanillaOpeningVerb == verb)
            {
                pendingVanillaOpeningVerb = null;
            }
        }

        private static bool ValidOpeningTarget(
            Pawn pawn,
            Thing target,
            bool playerForced,
            bool killIncappedTarget,
            bool closeContext)
        {
            if (pawn?.Map == null
                || target == null
                || target.Destroyed
                || !target.Spawned
                || target.Map != pawn.Map
                || (!playerForced
                    && !RimKataTargeting.IsAutomaticEnemy(pawn, target)))
            {
                return false;
            }

            if (target is Pawn targetPawn
                && (targetPawn.Dead
                    || targetPawn.Crawling
                    || targetPawn.IsPsychologicallyInvisible()
                    || (targetPawn.Downed
                        && !(playerForced && killIncappedTarget))))
            {
                return false;
            }

            if (closeContext)
            {
                return pawn.CanReachImmediate(target, PathEndMode.Touch);
            }

            return RimKataWeaponSlotUtility.CanAttackTargetWithoutRushing(
                pawn,
                target);
        }

        private static bool TargetWithinAutomaticSearchRange(
            Pawn pawn,
            Thing target)
        {
            if (pawn?.Map == null
                || target == null
                || !target.Spawned
                || target.Map != pawn.Map)
            {
                return false;
            }

            float range = RimKataTargeting.MaximumAutomaticSearchRange(pawn);
            return range > 0f
                && pawn.Position.DistanceToSquared(target.Position)
                    <= range * range;
        }

        public static bool ShouldConvertVanillaOpeningToSingleShot(Verb verb)
        {
            Pawn pawn = verb?.CasterPawn;
            ThingWithComps weapon = verb?.EquipmentSource as ThingWithComps;
            if (RimKataMod.Settings?.singleShotConversionEnabled != true
                || pawn?.Map == null
                || weapon == null
                || verb.IsMeleeAttack
                || RimKataFireContext.ActiveVerb != null)
            {
                return false;
            }

            return pendingVanillaOpeningVerb == verb;
        }

        private static void AdvanceSharedTargetSearch(
            Pawn pawn,
            RimKataPawnCombatState state,
            Thing currentTarget)
        {
            if (pawn?.Map == null
                || state?.sharedTargetSearch?.sessionActive != true)
            {
                return;
            }

            RimKataSharedTargetSearch.Advance(pawn, state, currentTarget);
            CancelProjectileWakeResumeForCombat(pawn, state);

            TryCacheSharedCandidate(
                pawn,
                state,
                state.primaryWeaponCycle,
                currentTarget);
            TryCacheSharedCandidate(
                pawn,
                state,
                state.secondaryWeaponCycle,
                currentTarget);
        }

        private static void CancelProjectileWakeResumeForCombat(
            Pawn pawn,
            RimKataPawnCombatState state)
        {
            Job resumeJob = state?.projectileWakeResumeJob;
            if (pawn?.jobs?.jobQueue == null
                || resumeJob == null
                || ((state.primaryWeaponCycle?.automaticCandidates?.Count ?? 0)
                        == 0
                    && (state.secondaryWeaponCycle?.automaticCandidates?.Count
                            ?? 0) == 0))
            {
                return;
            }

            pawn.jobs.jobQueue.RemoveAll(
                pawn,
                queuedJob => queuedJob == resumeJob);
            state.projectileWakeResumeJob = null;
        }

        private static bool TryCacheSharedCandidate(
            Pawn pawn,
            RimKataPawnCombatState state,
            RimKataWeaponCycleState cycle,
            Thing preferredTarget)
        {
            if (pawn?.Map == null
                || state == null
                || cycle == null
                || cycle.HasPlan
                || cycle.openingWarmupPending
                || cycle.burstShotsRemaining > 0)
            {
                return false;
            }

            bool closeContext = cycle.plannedCloseContext
                || state?.dualCloseCombatActive == true;
            Verb verb = RimKataWeaponSlotUtility.CombatVerbForContext(
                pawn,
                cycle.weapon,
                closeContext);
            if (FocusedTargetUsableNow(
                pawn,
                cycle,
                verb,
                closeContext))
            {
                return false;
            }

            Thing cachedTarget = cycle.cachedCandidateTarget;
            if (cachedTarget != null)
            {
                bool cachedTargetValid = cycle.cachedCandidateInterception
                    ? cachedTarget is Projectile
                        && RimKataSharedTargetSearch.IsValidForVerb(
                            pawn,
                            verb,
                            cachedTarget)
                    : ValidCurrentTargetForVerb(
                        pawn,
                        verb,
                        cachedTarget,
                        false,
                        false,
                        closeContext);
                if (cachedTargetValid)
                {
                    return false;
                }

                cycle.cachedCandidateTarget = null;
                cycle.cachedCandidateInterception = false;
            }

            Thing retainedTarget = cycle.lastFiredTarget;
            if (!RimKataEligibility.RandomAttackEnabledForPawn(pawn)
                && retainedTarget != null
                && !(retainedTarget is Projectile))
            {
                if (ValidCurrentTargetForVerb(
                        pawn,
                        verb,
                        retainedTarget,
                        false,
                        false,
                        closeContext))
                {
                    if (cycle.cachedCandidateInterception)
                    {
                        return false;
                    }

                    cycle.cachedCandidateTarget = null;
                    cycle.cachedCandidateInterception = false;
                    return TrySetKnownTarget(
                        pawn,
                        cycle,
                        verb,
                        retainedTarget,
                        false,
                        false,
                        closeContext,
                        false,
                        true);
                }

                cycle.lastFiredTarget = null;
            }

            if (!RimKataSharedTargetSearch.TrySelectCandidate(
                pawn,
                state,
                verb,
                preferredTarget,
                out Thing candidate,
                out bool interception))
            {
                return false;
            }

            cycle.cachedCandidateTarget = candidate;
            cycle.cachedCandidateInterception = interception;
            return true;
        }

        private static Verb LongestAutomaticRangeVerb(Pawn pawn)
        {
            ThingWithComps primary = RimKataWeaponSlotUtility.PrimaryWeapon(pawn);
            ThingWithComps secondary = RimKataWeaponSlotUtility.CanUseSecondarySlot(pawn)
                ? RimKataWeaponSlotUtility.SecondaryWeapon(pawn)
                : null;
            Verb primaryVerb = RimKataWeaponSlotUtility.CombatVerb(pawn, primary);
            Verb secondaryVerb = RimKataWeaponSlotUtility.CombatVerb(pawn, secondary);
            float primaryRange = primaryVerb != null && !primaryVerb.IsMeleeAttack
                ? RimKataRangeUtility.ResolveCandidateRange(primaryVerb)
                : -1f;
            float secondaryRange = secondaryVerb != null && !secondaryVerb.IsMeleeAttack
                ? RimKataRangeUtility.ResolveCandidateRange(secondaryVerb)
                : -1f;
            return secondaryRange > primaryRange
                ? secondaryVerb
                : primaryRange >= 0f
                    ? primaryVerb
                    : null;
        }

        public static void NotifyDefensiveCombatEvent(Pawn pawn, Thing attacker)
        {
            if (pawn?.Map == null
                || pawn.InMentalState
                || attacker == null
                || attacker.Destroyed
                || !attacker.Spawned
                || attacker.Map != pawn.Map
                || !RimKataTargeting.IsAutomaticEnemy(pawn, attacker)
                || (attacker is Pawn attackerPawn
                    && (attackerPawn.Dead
                        || attackerPawn.Downed
                        || attackerPawn.Crawling
                        || attackerPawn.IsPsychologicallyInvisible()))
                || !RimKataEligibility.CanBeginGunKataAttack(pawn))
            {
                return;
            }

            RimKataPawnCombatState state = StateFor(pawn, true);
            BindCurrentWeapons(pawn, state);
            if (attacker is Pawn incomingPawn)
            {
                state.NotifyIncomingThreat(incomingPawn);
            }
            else
            {
                state.RequestAutomaticAttack(attacker);
            }
            state.dualLastDrivenTick = -1;
            bool closeContext = pawn.CanReachImmediate(attacker, PathEndMode.Touch);
            if (closeContext)
            {
                pawn.Map.GetComponent<RimKataMapComponent>()?.EnterCloseCombat(pawn, attacker);
                state.RequestCloseAttack(attacker);
            }

            if (RimKataEligibility.RandomAttackEnabledForPawn(pawn))
            {
                RimKataSharedTargetSearch.TryAddKnownAutomaticTarget(
                    pawn,
                    state,
                    attacker);
            }
            RefreshDualEngagementState(pawn, state);

            TryCacheSharedCandidate(
                pawn,
                state,
                state.primaryWeaponCycle,
                attacker);
            TryCacheSharedCandidate(
                pawn,
                state,
                state.secondaryWeaponCycle,
                attacker);
            RefreshDualEngagementState(pawn, state);
            if (!state.dualEngagementActive)
            {
                return;
            }

            state.dualLastDrivenTick = Find.TickManager.TicksGame;
            if (pawn.Drafted
                || RimKataEligibility.RandomAttackEnabledForPawn(pawn))
            {
                QueueDedicatedFollowupJob(pawn, attacker);
            }
        }

        public static bool RegisterAutomaticTarget(Pawn pawn, Thing target)
        {
            if (pawn?.Map == null
                || pawn.InMentalState
                || target == null
                || target.Destroyed
                || !target.Spawned
                || target.Map != pawn.Map)
            {
                return false;
            }

            RimKataPawnCombatState state = StateFor(pawn, true);
            BindCurrentWeapons(pawn, state);
            RimKataSharedTargetSearch.TryAddKnownAutomaticTarget(
                pawn,
                state,
                target);
            bool registered =
                state.primaryWeaponCycle?.HasAutomaticCandidates == true
                || state.secondaryWeaponCycle?.HasAutomaticCandidates == true;
            if (registered)
            {
                state.dualEngagementActive = true;
                state.dualLastDrivenTick = -1;
                if (pawn.Drafted)
                {
                    state.draftedFireActive = true;
                }
            }
            return registered;
        }

        public static bool IsDedicatedFollowupActive(Pawn pawn)
        {
            return pawn?.InMentalState != true
                && HasCombatContinuity(pawn);
        }

        internal static bool IsDedicatedCloseCombatActive(Pawn pawn)
        {
            return StateFor(pawn, false)?.dualCloseCombatActive == true;
        }

        public static void ReconcileCloseCombatBeforeContinuityCheck(
            Pawn pawn,
            Thing assignedTarget,
            bool playerForced,
            bool killIncappedTarget)
        {
            RimKataPawnCombatState state = StateFor(pawn, false);
            if (pawn?.Map == null
                || state?.dualCloseCombatActive != true)
            {
                return;
            }

            Thing liveCloseTarget = ResolveCloseTarget(
                pawn,
                state,
                assignedTarget,
                playerForced,
                killIncappedTarget);
            HandleCloseCombatTransition(
                pawn,
                state,
                liveCloseTarget != null,
                liveCloseTarget);
        }

        private static bool HasCycleTargetWork(
            Pawn pawn,
            RimKataPawnCombatState state,
            RimKataWeaponCycleState cycle)
        {
            if (pawn?.Map == null || cycle?.weapon == null)
            {
                return false;
            }

            bool closeContext = cycle.plannedCloseContext
                || state?.dualCloseCombatActive == true;
            Verb verb = RimKataWeaponSlotUtility.CombatVerbForContext(
                pawn,
                cycle.weapon,
                closeContext);
            if (verb == null)
            {
                return false;
            }

            List<Thing> automaticCandidates = cycle.automaticCandidates;
            for (int i = 0;
                automaticCandidates != null && i < automaticCandidates.Count;
                i++)
            {
                if (RimKataSharedTargetSearch.IsValidForVerb(
                    pawn,
                    verb,
                    automaticCandidates[i]))
                {
                    return true;
                }
            }

            if (cycle.focusedTarget != null
                && ValidCurrentTargetForVerb(
                    pawn,
                    verb,
                    cycle.focusedTarget,
                    true,
                    false,
                    closeContext))
            {
                return true;
            }

            Thing cachedTarget = cycle.cachedCandidateTarget;
            bool cachedTargetValid = cachedTarget != null
                && (cycle.cachedCandidateInterception
                    ? RimKataSharedTargetSearch.IsValidForVerb(
                        pawn,
                        verb,
                        cachedTarget)
                    : RimKataEligibility.RandomAttackEnabledForPawn(pawn)
                        ? RimKataSharedTargetSearch.IsValidForVerb(
                            pawn,
                            verb,
                            cachedTarget)
                        : ValidCurrentTargetForVerb(
                            pawn,
                            verb,
                            cachedTarget,
                            false,
                            false,
                            closeContext));
            if (cachedTargetValid)
            {
                return true;
            }

            Thing plannedTarget = cycle.plannedTarget;
            if (plannedTarget == null)
            {
                return false;
            }

            if (cycle.plannedInterception)
            {
                return RimKataSharedTargetSearch.IsValidForVerb(
                    pawn,
                    verb,
                    plannedTarget);
            }

            bool playerForced = cycle.focusedTarget == plannedTarget
                || (pawn.CurJob?.playerForced == true
                    && pawn.CurJob.targetA.Thing == plannedTarget);
            return ValidCurrentTargetForVerb(
                pawn,
                verb,
                plannedTarget,
                playerForced,
                pawn.CurJob?.killIncappedTarget == true,
                cycle.plannedCloseContext);
        }

        private static bool NormalizeInvalidInterceptionState(
            Pawn pawn,
            RimKataPawnCombatState state)
        {
            return NormalizeInvalidInterceptionCycle(
                    pawn,
                    state,
                    state?.primaryWeaponCycle)
                | NormalizeInvalidInterceptionCycle(
                    pawn,
                    state,
                    state?.secondaryWeaponCycle);
        }

        private static bool NormalizeInvalidInterceptionCycle(
            Pawn pawn,
            RimKataPawnCombatState state,
            RimKataWeaponCycleState cycle)
        {
            if (pawn?.Map == null || cycle == null)
            {
                return false;
            }

            bool changed = false;
            bool invalidPlannedInterception = cycle.plannedInterception
                && !IsActiveInterceptionTarget(
                    pawn,
                    cycle.plannedTarget as Projectile);
            if (invalidPlannedInterception)
            {
                Verb verb = RimKataWeaponSlotUtility.CombatVerbForContext(
                    pawn,
                    cycle.weapon,
                    cycle.plannedCloseContext
                        || state?.dualCloseCombatActive == true);
                ApplyInterruptedBurstCooldown(pawn, cycle, verb);
                ClearTargetPreservingCycle(cycle);

                changed = true;
            }

            if (cycle.cachedCandidateInterception
                && !IsActiveInterceptionTarget(
                    pawn,
                    cycle.cachedCandidateTarget as Projectile))
            {
                cycle.cachedCandidateTarget = null;
                cycle.cachedCandidateInterception = false;
                changed = true;
            }

            if (cycle.visualTarget is Projectile visualProjectile
                && !IsActiveInterceptionTarget(pawn, visualProjectile))
            {
                cycle.visualTarget = null;
                cycle.visualAimTicksRemaining = 0;
                changed = true;
            }

            return changed;
        }

        private static bool IsActiveInterceptionTarget(
            Pawn pawn,
            Projectile projectile)
        {
            return projectile?.Map == pawn?.Map
                && RimKataTargeting.IsInterceptionTargetActive(projectile);
        }

        private static bool HasAnyCycleTargetWork(
            Pawn pawn,
            RimKataPawnCombatState state)
        {
            return HasCycleTargetWork(
                    pawn,
                    state,
                    state?.primaryWeaponCycle)
                || HasCycleTargetWork(
                    pawn,
                    state,
                    state?.secondaryWeaponCycle);
        }

        private static void RefreshDualEngagementState(
            Pawn pawn,
            RimKataPawnCombatState state)
        {
            if (state == null)
            {
                return;
            }

            bool wasActive = state.dualEngagementActive;
            state.dualEngagementActive = EvaluateCombatContinuity(pawn, state);
            if (wasActive && !state.dualEngagementActive)
            {
                state.ResetCandidateSaturationExpansion(true);
            }
        }

        public static bool HasCombatContinuity(Pawn pawn)
        {
            RimKataPawnCombatState state = StateFor(pawn, false);
            if (pawn?.InMentalState == true || state == null)
            {
                return false;
            }

            RefreshDualEngagementState(pawn, state);
            return state.dualEngagementActive;
        }

        private static bool EvaluateCombatContinuity(
            Pawn pawn,
            RimKataPawnCombatState state)
        {
            if (pawn?.Map == null || pawn.InMentalState || state == null)
            {
                return false;
            }

            bool liveCloseTarget = state.dualCloseCombatActive
                && IsImmediateCloseTarget(
                    pawn,
                    state.dualCloseTarget,
                    pawn.CurJob?.playerForced == true,
                    pawn.CurJob?.killIncappedTarget == true);
            bool movementContinuation = state.dualEngagementActive
                && state.MovementFireContinuityActive;
            return liveCloseTarget
                || state.sharedTargetSearch?.KeepsCombatAlive == true
                || movementContinuation
                || state.DodgeMovementActive
                || state.DraftedMovementSearchTriggerPending
                || state.idleProjectileSearchTriggerPending
                || state.dedicatedFollowupJobPending
                || HasDedicatedTargetContinuity(pawn, state)
                || HasAnyCycleTargetWork(pawn, state)
                || state.AutomaticAttackRequestActive
                || state.CloseAttackRequestActive;
        }

        private static bool HasDedicatedTargetContinuity(
            Pawn pawn,
            RimKataPawnCombatState state)
        {
            int currentTick = Find.TickManager?.TicksGame ?? -1;
            Job currentJob = pawn?.CurJob;
            Thing target = state?.dedicatedContinuityTarget;
            return state != null
                && currentTick >= 0
                && currentTick <= state.dedicatedContinuityUntilTick
                && currentJob?.def == RimKataDefOf.RimKata_Attack
                && currentJob.targetA.Thing == target
                && !PermanentlyInvalidCycleTarget(
                    pawn,
                    target,
                    target,
                    currentJob.playerForced,
                    currentJob.killIncappedTarget,
                    false);
        }

        public static void RefreshDedicatedTargetContinuity(
            Pawn pawn,
            Thing target)
        {
            RimKataPawnCombatState state = StateFor(pawn, false);
            if (state == null
                || pawn?.CurJobDef != RimKataDefOf.RimKata_Attack
                || target == null)
            {
                return;
            }

            bool cycleWork = HasAnyCycleTargetWork(pawn, state);
            bool canAttackWithoutRushing =
                RimKataWeaponSlotUtility.CanAttackTargetWithoutRushing(
                    pawn,
                    target);
            if (!cycleWork
                && !canAttackWithoutRushing
                && !CanMaintainRushContinuity(pawn, target))
            {
                return;
            }

            state.dedicatedContinuityTarget = target;
            state.dedicatedContinuityUntilTick =
                (Find.TickManager?.TicksGame ?? 0) + 3;
            RefreshDualEngagementState(pawn, state);
        }

        private static bool CanMaintainRushContinuity(Pawn pawn, Thing target)
        {
            if (!CanRushTarget(pawn, target))
            {
                return false;
            }

            if (pawn.CanReachImmediate(target, PathEndMode.Touch)
                || (pawn.pather?.Moving == true
                    && pawn.pather.Destination.Thing == target))
            {
                return true;
            }

            return pawn.CanReach(target, PathEndMode.Touch, Danger.Deadly);
        }

        public static void NotifyDraftStatusChanged(Pawn pawn)
        {
            RimKataPawnCombatState state = StateFor(pawn, false);
            if (state == null)
            {
                return;
            }

            state.CancelDraftedFire(false);
            state.ClearDraftedMovementSearchTracking();
            CancelUnfiredWarmupForDraftChange(state.primaryWeaponCycle);
            CancelUnfiredWarmupForDraftChange(state.secondaryWeaponCycle);
            state.ClearDedicatedFollowupJobRequest();
            RearmOpeningOwnerIfBothWaiting(state);
            RimKataSharedTargetSearch.Prune(pawn, state);
            RefreshDualEngagementState(pawn, state);
            state.dualLastDrivenTick = -1;
        }

        private static void CancelUnfiredWarmupForDraftChange(
            RimKataWeaponCycleState cycle)
        {
            if (cycle == null || cycle.burstShotsRemaining > 0)
            {
                return;
            }

            cycle.plannedTarget = null;
            cycle.plannedTargetCell = IntVec3.Invalid;
            cycle.plannedInterception = false;
            cycle.plannedCloseAttack = false;
            cycle.plannedCloseContext = false;
            cycle.plannedActionVerb = null;
            cycle.warmupTicksRemaining = -1;
            cycle.warmupTotalTicks = 0;
            cycle.openingWarmupBonusTicks = 0;
            cycle.openingWarmupPending = false;
            cycle.focusedTarget = null;
            cycle.focusedTargetFromAttackGizmo = false;
        }

        public static void QueueDedicatedFollowupJob(Pawn pawn, Thing target)
        {
            RimKataPawnCombatState state = StateFor(pawn, false);
            if (pawn?.Map == null
                || pawn.InMentalState
                || state == null
                || state.dedicatedFollowupJobStartInProgress
                || IsProtectedPlayerForcedJob(pawn.CurJob)
                || !IsDedicatedFollowupActive(pawn))
            {
                return;
            }

            state.QueueDedicatedFollowupJob(target, pawn.CurJob);
        }

        public static void NotifyDedicatedCombatJobFinished(Pawn pawn)
        {
            RimKataPawnCombatState state = StateFor(pawn, false);
            if (state != null)
            {
                state.projectileWakeResumeJob = null;
            }
        }

        public static void TryConsumePendingDedicatedFollowupJob(Pawn pawn)
        {
            RimKataPawnCombatState state = StateFor(pawn, false);
            if (pawn?.InMentalState == true)
            {
                CancelOffenseForMentalState(pawn);
                return;
            }

            if (state?.dedicatedFollowupJobPending != true)
            {
                return;
            }

            int currentTick = Find.TickManager?.TicksGame ?? -1;
            int requestedTick = state.dedicatedFollowupJobRequestedTick;
            if (currentTick < 0 || requestedTick > currentTick)
            {
                return;
            }

            Thing target = state.dedicatedFollowupJobTarget;
            Job sourceJob = state.dedicatedFollowupJobSourceJob;
            ThinkNode sourceJobGiver = sourceJob?.jobGiver;
            Thing sourceTarget = sourceJob?.targetA.Thing;
            if (!(sourceJobGiver is JobGiver_ConfigurableHostilityResponse)
                && !(sourceJobGiver is JobGiver_ReactToCloseMeleeThreat))
            {
                sourceJobGiver = null;
            }
            bool playerForced = state.dedicatedFollowupJobPlayerForced;
            bool killIncappedTarget = state.dedicatedFollowupJobKillIncappedTarget;
            state.ClearDedicatedFollowupJobRequest();
            if (requestedTick < 0
                || currentTick > requestedTick + 1
                || !CanConsumeDedicatedFollowupRequest(
                    pawn,
                    sourceJob,
                    target)
                || (pawn.Drafted
                    && !playerForced
                    && pawn.drafter?.FireAtWill != true))
            {
                if (state.projectileWakeResumeJob == sourceJob)
                {
                    state.projectileWakeResumeJob = null;
                }
                return;
            }

            TryEnterDedicatedFollowupJob(
                pawn,
                target,
                playerForced,
                killIncappedTarget,
                sourceJobGiver,
                sourceTarget);
        }

        private static bool CanConsumeDedicatedFollowupRequest(
            Pawn pawn,
            Job sourceJob,
            Thing target)
        {
            Job currentJob = pawn?.CurJob;
            if (IsProtectedPlayerForcedJob(currentJob))
            {
                return false;
            }

            if (currentJob == sourceJob || currentJob == null)
            {
                return true;
            }

            JobDef currentDef = currentJob.def;
            if (currentDef == RimKataDefOf.RimKata_Attack)
            {
                return true;
            }

            if (currentDef == JobDefOf.AttackStatic
                || currentDef == JobDefOf.AttackMelee)
            {
                return target != null
                    && currentJob.targetA.Thing == target;
            }

            return currentDef == JobDefOf.Goto
                || currentDef == JobDefOf.Wait
                || currentDef == JobDefOf.Wait_Combat
                || currentDef == JobDefOf.Wait_MaintainPosture;
        }

        public static void RefreshPendingDedicatedFollowupAim(
            Pawn pawn,
            Verb sourceVerb,
            LocalTargetInfo sourceTarget)
        {
            if (pawn?.InMentalState == true)
            {
                CancelOffenseForMentalState(pawn);
                return;
            }

            RimKataPawnCombatState state = StateFor(pawn, false);
            if (state?.dedicatedFollowupJobPending != true
                || state.dedicatedFollowupJobStartInProgress
                || sourceVerb == null
                || !sourceTarget.HasThing
                || state.dedicatedFollowupJobTarget != sourceTarget.Thing
                || !IsDedicatedFollowupActive(pawn)
                || pawn?.stances?.FullBodyBusy == true)
            {
                return;
            }

            RimKataWeaponCycleState sourceCycle = CycleForWeapon(
                state,
                sourceVerb.EquipmentSource as ThingWithComps);
            if (sourceCycle?.lastFiredTarget != sourceTarget.Thing
                || RimKataWeaponSlotUtility.CombatVerb(
                    pawn,
                    sourceCycle.weapon) != sourceVerb)
            {
                return;
            }

            UpdateBodyAimStance(pawn, state);
        }

        public static void TryEnterDedicatedFollowupJob(Pawn pawn, Thing target)
        {
            TryEnterDedicatedFollowupJob(
                pawn,
                target,
                null,
                null,
                null,
                null);
        }

        private static void TryEnterDedicatedFollowupJob(
            Pawn pawn,
            Thing target,
            bool? playerForcedOverride,
            bool? killIncappedTargetOverride,
            ThinkNode counterattackJobGiver,
            Thing counterattackSourceTarget)
        {
            if (pawn?.InMentalState == true)
            {
                CancelOffenseForMentalState(pawn);
                return;
            }

            RimKataPawnCombatState state = StateFor(pawn, false);
            int currentTick = Find.TickManager?.TicksGame ?? -1;
            if (state?.dedicatedFollowupJobStartInProgress == true
                || (currentTick >= 0
                    && state?.dedicatedFollowupJobLastStartTick == currentTick))
            {
                return;
            }

            Job currentJob = pawn?.CurJob;
            if (IsProtectedPlayerForcedJob(currentJob))
            {
                state?.ClearDedicatedFollowupJobRequest();
                if (state?.projectileWakeResumeJob == currentJob)
                {
                    state.projectileWakeResumeJob = null;
                }
                return;
            }

            bool resumeCurrentJobAfterProjectile = pawn?.Drafted != true
                && currentJob != null
                && state?.projectileWakeResumeJob == currentJob;

            bool playerForced = playerForcedOverride
                ?? currentJob?.playerForced == true;
            bool killIncappedTarget = killIncappedTargetOverride
                ?? currentJob?.killIncappedTarget == true;
            bool validTarget = !PermanentlyInvalidCycleTarget(
                pawn,
                target,
                target,
                playerForced,
                killIncappedTarget,
                false);
            if (!validTarget)
            {
                TryGetContinuationTarget(
                    pawn,
                    playerForced,
                    killIncappedTarget,
                    out target);
            }

            validTarget = !PermanentlyInvalidCycleTarget(
                pawn,
                target,
                target,
                playerForced,
                killIncappedTarget,
                false);
            bool searchOnly = pawn?.Map != null
                && HasContinuationSearchWork(pawn);
            if (pawn?.Map == null
                || (!validTarget && !searchOnly)
                || pawn.CurJobDef == RimKataDefOf.RimKata_Attack
                || !IsDedicatedFollowupActive(pawn))
            {
                if (pawn?.CurJobDef == RimKataDefOf.RimKata_Attack)
                {
                    state?.ClearDedicatedFollowupJobRequest();
                }

                return;
            }

            Job job = validTarget
                ? JobMaker.MakeJob(RimKataDefOf.RimKata_Attack, target)
                : JobMaker.MakeJob(RimKataDefOf.RimKata_Attack);
            job.playerForced = playerForced;
            job.killIncappedTarget = validTarget
                ? playerForced
                    && killIncappedTarget
                    && target is Pawn targetPawn
                    && targetPawn.Downed
                : killIncappedTarget;
            job.verbToUse = RimKataWeaponSlotUtility.CombatVerb(
                pawn,
                state?.engagementOwnerWeapon)
                ?? RimKataWeaponSlotUtility.BestRangedCombatVerb(
                    pawn,
                    validTarget ? target : null)
                ?? RimKataWeaponSlotUtility.CombatVerb(
                    pawn,
                    RimKataWeaponSlotUtility.PrimaryWeapon(pawn));
            bool counterattackTargetChanged = counterattackJobGiver != null
                && validTarget
                && counterattackSourceTarget != target;
            if (counterattackTargetChanged)
            {
                job.jobGiver = counterattackJobGiver;
            }
            state.ClearDedicatedFollowupJobRequest();
            state.dedicatedFollowupJobStartInProgress = true;
            state.dedicatedFollowupJobLastStartTick = currentTick;
            try
            {
                if (job.playerForced)
                {
                    pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                }
                else
                {
                    pawn.jobs.StartJob(
                        job,
                        JobCondition.InterruptForced,
                        null,
                        resumeCurrentJobAfterProjectile,
                        true,
                        null,
                        JobTag.Misc,
                        false,
                        false,
                        null,
                        false,
                        true,
                        false);

                    if (pawn.CurJob == job
                        && counterattackJobGiver != null
                        && !counterattackTargetChanged)
                    {
                        job.jobGiver = counterattackJobGiver;
                    }

                    if (resumeCurrentJobAfterProjectile
                        && pawn.CurJob != job)
                    {
                        state.projectileWakeResumeJob = null;
                    }
                }
            }
            finally
            {
                state.dedicatedFollowupJobStartInProgress = false;
            }
        }

        private static bool IsProtectedPlayerForcedJob(Job job)
        {
            return job?.playerForced == true;
        }

        public static bool TryAbsorbCounterattackOpening(
            Pawn pawn,
            Job sourceJob,
            ThinkNode jobGiver)
        {
            if (pawn?.InMentalState == true
                || (!(jobGiver is JobGiver_ConfigurableHostilityResponse)
                    && !(jobGiver is JobGiver_ReactToCloseMeleeThreat)))
            {
                return false;
            }

            Job currentJob = pawn?.CurJob;
            if (IsProtectedPlayerForcedJob(currentJob)
                && IsAutomaticThreatResponseJob(pawn, sourceJob))
            {
                return AbsorbAutomaticThreatIntoProtectedJob(
                    pawn,
                    sourceJob.targetA.Thing);
            }

            if (pawn.Drafted == true
                && sourceJob?.def == JobDefOf.AttackMelee
                && IsAutomaticThreatResponseJob(pawn, sourceJob))
            {
                return AbsorbAutomaticThreatIntoProtectedJob(
                    pawn,
                    sourceJob.targetA.Thing);
            }

            if (!CounterattackControlEnabled(pawn)
                || !IsConfigurableCounterattackOpening(pawn, sourceJob))
            {
                return false;
            }

            if (currentJob?.def != RimKataDefOf.RimKata_Attack
                || currentJob.playerForced
                || !(pawn.jobs?.curDriver is JobDriver_RimKataAttack driver)
                || !driver.CanAbsorbAutomaticAttackJob)
            {
                return false;
            }

            if (!RimKataEligibility.RandomAttackEnabledForPawn(pawn))
            {
                return true;
            }

            Thing target = sourceJob.targetA.Thing;
            RimKataPawnCombatState state = StateFor(pawn, true);
            BindCurrentWeapons(pawn, state);
            RimKataSharedTargetSearch.Begin(pawn, state, pawn.Position);
            RimKataSharedTargetSearch.TryAddKnownAutomaticTarget(
                pawn,
                state,
                target);
            if (sourceJob.def == JobDefOf.AttackMelee
                && pawn.CanReachImmediate(target, PathEndMode.Touch))
            {
                state.RequestCloseAttack(target);
            }

            RefreshDualEngagementState(pawn, state);
            return true;
        }

        public static bool TryAbsorbPathBlockedMeleeJob(
            Pawn pawn,
            Job sourceJob,
            JobCondition lastJobEndCondition,
            ThinkNode jobGiver,
            bool fromQueue)
        {
            Job currentJob = pawn?.CurJob;
            if (jobGiver != null
                || fromQueue
                || lastJobEndCondition != JobCondition.Incompletable
                || currentJob?.playerForced != true
                || currentJob.def != JobDefOf.Goto
                || sourceJob?.playerForced == true
                || sourceJob.def != JobDefOf.AttackMelee
                || sourceJob.maxNumMeleeAttacks != 1
                || sourceJob.expiryInterval != 300
                || !IsAutomaticThreatResponseJob(pawn, sourceJob))
            {
                return false;
            }

            Thing target = sourceJob.targetA.Thing;
            if (!(target is Pawn)
                || !pawn.Position.AdjacentTo8WayOrInside(target.Position))
            {
                return false;
            }

            if (pawn.pather != null
                && pawn.pather.nextCell == target.Position)
            {
                return false;
            }

            RimKataPawnCombatState state = StateFor(pawn, true);
            int currentTick = Find.TickManager?.TicksGame ?? -1;
            if (state.absorbedPathBlockedGotoJobId == currentJob.loadID
                && state.absorbedPathBlockedThreat == target
                && currentTick >= 0
                && state.absorbedPathBlockedRefreshTick >= 0
                && currentTick - state.absorbedPathBlockedRefreshTick < 15)
            {
                return true;
            }

            bool absorbed = AbsorbAutomaticThreatIntoProtectedJob(pawn, target);
            if (absorbed)
            {
                state.absorbedPathBlockedGotoJobId = currentJob.loadID;
                state.absorbedPathBlockedThreat = target;
                state.absorbedPathBlockedRefreshTick = currentTick;
            }

            return absorbed;
        }

        private static bool AbsorbAutomaticThreatIntoProtectedJob(
            Pawn pawn,
            Thing threat)
        {
            if (!RimKataEligibility.RandomAttackEnabledForPawn(pawn))
            {
                return true;
            }

            RimKataPawnCombatState state = StateFor(pawn, true);
            BindCurrentWeapons(pawn, state);
            state.RequestAutomaticAttack(threat);
            if (threat is Pawn incomingPawn)
            {
                state.NotifyIncomingThreat(incomingPawn);
            }

            if (pawn.CanReachImmediate(threat, PathEndMode.Touch))
            {
                pawn.Map.GetComponent<RimKataMapComponent>()
                    ?.EnterCloseCombat(pawn, threat);
                state.RequestCloseAttack(threat);
            }

            RimKataSharedTargetSearch.Begin(pawn, state, pawn.Position);
            RimKataSharedTargetSearch.TryAddKnownAutomaticTarget(
                pawn,
                state,
                threat);
            RefreshDualEngagementState(pawn, state);
            state.dualLastDrivenTick = -1;
            if (pawn.Drafted)
            {
                state.draftedFireActive = true;
            }

            return true;
        }

        private static bool IsAutomaticThreatResponseJob(Pawn pawn, Job job)
        {
            Thing target = job?.targetA.Thing;
            return pawn?.Map != null
                && !pawn.InMentalState
                && job?.playerForced != true
                && (job.def == JobDefOf.AttackStatic
                    || job.def == JobDefOf.AttackMelee)
                && target != null
                && target.Spawned
                && !target.Destroyed
                && target.Map == pawn.Map
                && RimKataEligibility.CanBeginGunKataAttack(pawn)
                && RimKataTargeting.IsAutomaticEnemy(pawn, target)
                && (!(target is Pawn targetPawn)
                    || (!targetPawn.Dead
                        && !targetPawn.Downed
                        && !targetPawn.Crawling
                        && !targetPawn.IsPsychologicallyInvisible()));
        }

        public static bool TryConvertCounterattackOpening(
            Pawn pawn,
            Job sourceJob,
            ThinkNode jobGiver,
            out Job convertedJob)
        {
            convertedJob = sourceJob;
            if ((!(jobGiver is JobGiver_ConfigurableHostilityResponse)
                    && !(jobGiver is JobGiver_ReactToCloseMeleeThreat))
                || !CounterattackControlEnabled(pawn))
            {
                return false;
            }

            if (!IsConfigurableCounterattackOpening(pawn, sourceJob))
            {
                return false;
            }

            Thing target = sourceJob.targetA.Thing;
            RimKataPawnCombatState state = StateFor(pawn, false);
            Thing openingJobTarget = ResolveCloseTarget(
                    pawn,
                    state,
                    target,
                    false,
                    false)
                ?? target;
            if (!TargetWithinAutomaticSearchRange(
                    pawn,
                    openingJobTarget)
                && !RimKataWeaponSlotUtility.CanAttackTargetWithoutRushing(
                    pawn,
                    openingJobTarget))
            {
                return false;
            }

            state ??= StateFor(pawn, true);
            BindCurrentWeapons(pawn, state);
            Verb ownerVerb =
                RimKataWeaponSlotUtility.BestRangedCombatVerb(
                    pawn,
                    openingJobTarget)
                ?? sourceJob.verbToUse;
            ThingWithComps ownerWeapon =
                ownerVerb?.EquipmentSource as ThingWithComps;
            if (CycleForWeapon(state, ownerWeapon) == null)
            {
                ownerWeapon = RimKataWeaponSlotUtility.PrimaryWeapon(pawn);
                ownerVerb = RimKataWeaponSlotUtility.CombatVerb(
                    pawn,
                    ownerWeapon);
            }

            state.engagementOwnerWeapon = ownerWeapon;
            RimKataSharedTargetSearch.Begin(pawn, state, pawn.Position);
            RimKataSharedTargetSearch.TryAddKnownAutomaticTarget(
                pawn,
                state,
                target);
            if (openingJobTarget != target)
            {
                RimKataSharedTargetSearch.TryAddKnownAutomaticTarget(
                    pawn,
                    state,
                    openingJobTarget);
            }
            if (sourceJob.def == JobDefOf.AttackMelee
                && pawn.CanReachImmediate(
                    openingJobTarget,
                    PathEndMode.Touch))
            {
                state.RequestCloseAttack(openingJobTarget);
            }
            RefreshDualEngagementState(pawn, state);
            state.dualLastDrivenTick = -1;

            Job rimKataJob = JobMaker.MakeJob(
                RimKataDefOf.RimKata_Attack,
                openingJobTarget);
            rimKataJob.playerForced = false;
            rimKataJob.killIncappedTarget = false;
            rimKataJob.verbToUse = ownerVerb
                ?? RimKataWeaponSlotUtility.CombatVerb(
                    pawn,
                    RimKataWeaponSlotUtility.PrimaryWeapon(pawn));
            convertedJob = rimKataJob;
            return true;
        }

        private static bool IsConfigurableCounterattackOpening(
            Pawn pawn,
            Job job)
        {
            Thing target = job?.targetA.Thing;
            return pawn?.Map != null
                && !pawn.InMentalState
                && pawn.Drafted != true
                && pawn.playerSettings?.UsesConfigurableHostilityResponse == true
                && pawn.playerSettings.hostilityResponse
                    == HostilityResponseMode.Attack
                && job.playerForced != true
                && (job.def == JobDefOf.AttackStatic
                    || job.def == JobDefOf.AttackMelee)
                && target != null
                && target.Spawned
                && !target.Destroyed
                && target.Map == pawn.Map
                && RimKataEligibility.CanBeginGunKataAttack(pawn)
                && RimKataTargeting.IsAutomaticEnemy(pawn, target)
                && (!(target is Pawn targetPawn)
                    || (!targetPawn.Dead
                        && !targetPawn.Downed
                        && !targetPawn.Crawling
                        && !targetPawn.IsPsychologicallyInvisible()));
        }

        public static bool CanRushTarget(Pawn pawn, Thing target)
        {
            return RimKataMod.Settings?.targetRushEnabled != false
                && pawn?.Map != null
                && pawn.Drafted != true
                && !pawn.InMentalState
                && pawn.CurJobDef == RimKataDefOf.RimKata_Attack
                && pawn.CurJob?.playerForced != true
                && pawn.CurJob.targetA.Thing == target
                && target != null
                && !target.Destroyed
                && target.Spawned
                && target.Map == pawn.Map
                && TargetWithinAutomaticSearchRange(pawn, target)
                && RimKataTargeting.IsAutomaticEnemy(pawn, target)
                && (!(target is Pawn targetPawn)
                    || (!targetPawn.Dead
                        && !targetPawn.Downed
                        && !targetPawn.Crawling
                        && !targetPawn.IsPsychologicallyInvisible()));
        }

        private static void ResetUnfiredOpeningTimer(
            RimKataWeaponCycleState cycle)
        {
            if (cycle == null)
            {
                return;
            }

            cycle.ClearPlan(false);
            cycle.warmupTicksRemaining = -1;
            cycle.warmupTotalTicks = 0;
            cycle.cachedCandidateTarget = null;
            cycle.cachedCandidateInterception = false;
            cycle.visualTarget = null;
            cycle.visualAimTicksRemaining = 0;
        }

        private static void ClearTargetPreservingCycle(
            RimKataWeaponCycleState cycle)
        {
            if (cycle == null)
            {
                return;
            }

            Thing previousTarget = cycle.plannedTarget;
            cycle.ClearPlan(false);
            if (cycle.openingWarmupPending
                && !cycle.firedInCurrentOpening)
            {
                cycle.openingWarmupBonusTicks = 0;
                cycle.openingWarmupPending = false;
            }

            if (cycle.visualTarget == previousTarget)
            {
                cycle.visualTarget = null;
                cycle.visualAimTicksRemaining = 0;
            }
        }

        private static RimKataWeaponCycleState OtherCycle(
            RimKataPawnCombatState state,
            RimKataWeaponCycleState cycle)
        {
            if (state == null || cycle == null)
            {
                return null;
            }

            return cycle == state.primaryWeaponCycle
                ? state.secondaryWeaponCycle
                : cycle == state.secondaryWeaponCycle
                    ? state.primaryWeaponCycle
                    : null;
        }

        private static void RecordFirstFiredWeapon(
            RimKataPawnCombatState state,
            ThingWithComps weapon)
        {
            if (state != null
                && state.engagementOwnerWeapon == null
                && weapon != null)
            {
                state.engagementOwnerWeapon = weapon;
            }
        }

        private static bool ValidCurrentTargetForVerb(
            Pawn pawn,
            Verb verb,
            Thing target,
            bool playerForced,
            bool killIncappedTarget,
            bool closeContext)
        {
            if (pawn?.Map == null
                || verb == null
                || target == null
                || target.Destroyed
                || !target.Spawned
                || target.Map != pawn.Map
                || (!playerForced
                    && !RimKataTargeting.IsAutomaticEnemy(pawn, target))
                || !VerbUsable(pawn, verb, closeContext))
            {
                return false;
            }

            if (target is Pawn targetPawn
                && (targetPawn.Dead
                    || targetPawn.Crawling
                    || targetPawn.IsPsychologicallyInvisible()
                    || (targetPawn.Downed
                        && !(playerForced && killIncappedTarget))))
            {
                return false;
            }

            return CanHitTargetForCombatContext(
                pawn,
                verb,
                target,
                closeContext);
        }

        private static bool CanHitTargetForCombatContext(
            Pawn pawn,
            Verb verb,
            Thing target,
            bool closeCombatContext)
        {
            if (verb == null || target == null)
            {
                return false;
            }

            if (verb.IsMeleeAttack)
            {
                return verb.CanHitTarget(target);
            }

            return closeCombatContext
                ? pawn?.CanReachImmediate(target, PathEndMode.Touch) == true
                : verb.CanHitTarget(target);
        }

        public static bool TryTakeVanillaMeleeCooldown(
            Pawn pawn,
            Verb verb,
            LocalTargetInfo focus)
        {
            if (pawn?.Map == null
                || pawn.InMentalState
                || verb == null
                || !verb.IsMeleeAttack
                || !RimKataEligibility.HasRimKataAccess(pawn)
                || !RimKataEquipmentUtility.HasEnabledArmor(pawn))
            {
                return false;
            }

            if (activePhysicalMeleePawn == pawn
                && activePhysicalMeleeCycle != null
                && RimKataFireContext.ActiveVerb == verb)
            {
                return true;
            }

            ThingWithComps weapon = verb.EquipmentSource as ThingWithComps;
            if (weapon == null
                || weapon.Destroyed
                || pawn.equipment?.AllEquipmentListForReading?.Contains(weapon) != true
                || !RimKataEquipmentUtility.IsWeaponEnabled(weapon.def))
            {
                return false;
            }

            RimKataPawnCombatState state = StateFor(pawn, true);
            BindCurrentWeapons(pawn, state);
            RimKataWeaponCycleState cycle = CycleForWeapon(state, weapon);
            if (cycle == null)
            {
                return false;
            }

            int cooldown = RimKataCombatMath.CooldownTicksForSingleShot(verb, pawn, false);

            cycle.cooldownTicksRemaining = Mathf.Max(cycle.cooldownTicksRemaining, cooldown);
            cycle.cooldownFromVanillaOpening = false;
            cycle.lastFiredTarget = focus.HasThing ? focus.Thing : null;
            cycle.visualTarget = cycle.lastFiredTarget;
            cycle.visualAimTicksRemaining = Mathf.Max(2, cooldown + 2);
            RimKataSharedTargetSearch.Begin(
                pawn,
                state,
                pawn.Position);

            return true;
        }

        public static bool HasUsableWeapon(Pawn pawn, bool closeCombatContext)
        {
            if (pawn == null || !RimKataEligibility.CanBeginGunKataAttack(pawn))
            {
                return false;
            }

            ThingWithComps primary = RimKataWeaponSlotUtility.PrimaryWeapon(pawn);
            Verb primaryVerb = RimKataWeaponSlotUtility.CombatVerbForContext(
                pawn,
                primary,
                closeCombatContext);
            if (primaryVerb != null && VerbUsable(pawn, primaryVerb, closeCombatContext))
            {
                return true;
            }

            ThingWithComps secondary = RimKataWeaponSlotUtility.CanUseSecondarySlot(pawn)
                ? RimKataWeaponSlotUtility.SecondaryWeapon(pawn)
                : null;
            Verb secondaryVerb = RimKataWeaponSlotUtility.CombatVerbForContext(
                pawn,
                secondary,
                closeCombatContext);
            return secondaryVerb != null && VerbUsable(pawn, secondaryVerb, closeCombatContext);
        }

        public static Thing ResolveImmediateCloseTarget(
            Pawn pawn,
            Thing assignedTarget,
            bool playerForced,
            bool killIncappedTarget)
        {
            return ResolveCloseTarget(
                pawn,
                StateFor(pawn, false),
                assignedTarget,
                playerForced,
                killIncappedTarget);
        }

        public static bool TryGetContinuationTarget(
            Pawn pawn,
            bool playerForced,
            bool killIncappedTarget,
            out Thing target)
        {
            RimKataPawnCombatState state = StateFor(pawn, false);
            if (TryGetContinuationTarget(pawn, state?.primaryWeaponCycle, playerForced, killIncappedTarget, out target)
                || TryGetContinuationTarget(pawn, state?.secondaryWeaponCycle, playerForced, killIncappedTarget, out target))
            {
                return true;
            }

            target = null;
            return false;
        }

        public static bool IsCloseExitSearchRequired(Pawn pawn)
        {
            RimKataPawnCombatState state = StateFor(pawn, false);
            return ResolveCloseTarget(
                    pawn,
                    state,
                    null,
                    false,
                    false) != null
                || state?.DraftedMovementSearchTriggerPending == true
                || state?.sharedTargetSearch?.KeepsCombatAlive == true
                || CycleHasContinuationSearchWork(state?.primaryWeaponCycle)
                || CycleHasContinuationSearchWork(state?.secondaryWeaponCycle);
        }

        public static bool HasContinuationSearchWork(Pawn pawn)
        {
            RimKataPawnCombatState state = StateFor(pawn, false);
            return ResolveCloseTarget(
                    pawn,
                    state,
                    null,
                    false,
                    false) != null
                || state?.DraftedMovementSearchTriggerPending == true
                || state?.sharedTargetSearch?.KeepsCombatAlive == true
                || CycleHasContinuationSearchWork(state?.primaryWeaponCycle)
                || CycleHasContinuationSearchWork(state?.secondaryWeaponCycle);
        }

        public static bool EnsureContinuationSearchBeforeExit(Pawn pawn)
        {
            RimKataPawnCombatState state = StateFor(pawn, false);
            if (pawn?.Map == null || state == null)
            {
                return false;
            }

            if (state.dualCloseCombatActive)
            {
                Thing liveCloseTarget = ResolveCloseTarget(
                    pawn,
                    state,
                    null,
                    false,
                    false);
                if (liveCloseTarget != null)
                {
                    return true;
                }

                HandleCloseCombatTransition(
                    pawn,
                    state,
                    false,
                    null);
            }

            if (MovementSearchInProgress(state))
            {
                return true;
            }

            if (state.DraftedMovementSearchTriggerPending)
            {
                if (TryBeginMovementSearch(pawn, state, pawn.Position))
                {
                    state.ConsumeDraftedMovementSearchTrigger();
                    return true;
                }
            }

            BindCurrentWeapons(pawn, state);
            if (state.primaryWeaponCycle?.HasAutomaticCandidates == true
                || state.secondaryWeaponCycle?.HasAutomaticCandidates == true)
            {
                return true;
            }

            if (HasProjectileOnlySearchWork(pawn, state))
            {
                return true;
            }

            if (state.projectileWakeResumeJob != null
                && !HasNonProjectileSearchDemand(pawn, state))
            {
                return false;
            }

            return state.sharedTargetSearch?.scanActive == true
                || RimKataSharedTargetSearch.Begin(
                    pawn,
                    state,
                    pawn.Position);
        }

        public static void ImportLegacyPrimaryState(
            Pawn pawn,
            int cooldownTicks,
            int warmupTicks,
            Thing plannedTarget,
            bool interception,
            bool closeAttack,
            bool closeContext)
        {
            RimKataPawnCombatState state = StateFor(pawn, true);
            if (state == null)
            {
                return;
            }

            state.primaryWeaponCycle.Bind(RimKataWeaponSlotUtility.PrimaryWeapon(pawn));
            RimKataWeaponCycleState cycle = state.primaryWeaponCycle;
            cycle.cooldownTicksRemaining = Mathf.Max(cycle.cooldownTicksRemaining, Mathf.Max(0, cooldownTicks));
            if (cycle.plannedTarget == null && plannedTarget != null)
            {
                cycle.plannedTarget = plannedTarget;
                cycle.visualTarget = plannedTarget;
                cycle.plannedInterception = interception;
                cycle.plannedCloseAttack = closeAttack;
                cycle.plannedCloseContext = closeContext;
                cycle.plannedActionVerb = null;
                cycle.warmupTicksRemaining = Mathf.Max(1, warmupTicks);
                cycle.warmupTotalTicks = cycle.warmupTicksRemaining;
            }
        }

        public static bool TryGetVisualData(
            Pawn pawn,
            ThingWithComps weapon,
            out RimKataWeaponVisualData data)
        {
            data = default(RimKataWeaponVisualData);
            RimKataPawnCombatState state = StateFor(pawn, false);
            RimKataWeaponCycleState cycle = CycleForWeapon(state, weapon);
            if (cycle == null || cycle.weapon != weapon)
            {
                return false;
            }

            Thing liveVisualTarget = IsLiveVisualTarget(
                    pawn,
                    cycle.visualTarget)
                ? cycle.visualTarget
                : null;
            if (cycle.visualTarget != null && liveVisualTarget == null)
            {
                cycle.visualTarget = null;
            }

            Thing livePlannedTarget = IsLiveVisualTarget(
                    pawn,
                    cycle.plannedTarget)
                ? cycle.plannedTarget
                : null;
            Thing targetThing = cycle.cooldownTicksRemaining > 0
                && liveVisualTarget != null
                ? liveVisualTarget
                : livePlannedTarget ?? liveVisualTarget;
            LocalTargetInfo target = targetThing != null && targetThing.Spawned
                ? new LocalTargetInfo(targetThing)
                : LocalTargetInfo.Invalid;
            data = new RimKataWeaponVisualData
            {
                weapon = weapon,
                target = target,
                warming = cycle.IsWarming,
                warmupTicksRemaining = Mathf.Max(0, cycle.warmupTicksRemaining),
                warmupTotalTicks = Mathf.Max(0, cycle.warmupTotalTicks),
                cooldownTicksRemaining = Mathf.Max(0, cycle.cooldownTicksRemaining)
            };
            return target.IsValid || cycle.cooldownTicksRemaining > 0;
        }

        private static bool IsLiveVisualTarget(Pawn pawn, Thing target)
        {
            return pawn?.Map != null
                && target != null
                && !target.Destroyed
                && target.Spawned
                && target.Map == pawn.Map
                && (!(target is Projectile projectile)
                    || RimKataTargeting
                        .IsInterceptionTargetActive(projectile))
                && (!(target is Pawn targetPawn) || !targetPawn.Dead);
        }

        public static bool TryGetIndicatorVisualData(
            Pawn pawn,
            ThingWithComps weapon,
            out RimKataWeaponVisualData data,
            out bool claimsVanillaRangedCooldown)
        {
            bool hasInternal = TryGetVisualData(pawn, weapon, out data);
            claimsVanillaRangedCooldown = false;
            Verb verb = RimKataWeaponSlotUtility.CombatVerb(pawn, weapon);
            if (verb == null
                || verb.IsMeleeAttack
                || !(pawn?.stances?.curStance is Stance_Cooldown cooldown)
                || cooldown.verb != verb
                || cooldown.ticksLeft <= 0
                || !cooldown.focusTarg.IsValid
                || verb.verbProps?.drawAimPie != true)
            {
                return hasInternal;
            }

            data.weapon = weapon;
            data.target = cooldown.focusTarg;
            data.warming = false;
            data.warmupTicksRemaining = 0;
            data.warmupTotalTicks = 0;
            data.cooldownTicksRemaining = Mathf.Max(
                data.cooldownTicksRemaining,
                cooldown.ticksLeft);
            claimsVanillaRangedCooldown = true;
            return true;
        }

        public static bool TryGetNextAim(
            Pawn pawn,
            out ThingWithComps weapon,
            out LocalTargetInfo target)
        {
            weapon = null;
            target = LocalTargetInfo.Invalid;
            RimKataPawnCombatState state = StateFor(pawn, false);
            if (state == null)
            {
                return false;
            }

            RimKataWeaponCycleState first = ChooseBodyAimCycle(pawn, state);
            if (first?.weapon == null)
            {
                return false;
            }

            if (!TryGetVisualData(pawn, first.weapon, out RimKataWeaponVisualData visual)
                || !visual.target.IsValid)
            {
                return false;
            }

            weapon = first.weapon;
            target = visual.target;
            return true;
        }

        public static void NotifyLoadoutChanged(Pawn pawn)
        {
            Reset(pawn, true);
            RimKataPawnCombatState state = StateFor(pawn, false);
            if (state == null)
            {
                return;
            }

            if (pawn.CurJobDef == RimKataDefOf.RimKata_Attack)
            {
                state.loadoutInvalidatedCombatJob = pawn.CurJob;
            }

            if (!WeaponStillHeld(pawn, state.responsePoseWeapon))
            {
                state.CancelResponsePose();
            }

            if (!WeaponStillHeld(pawn, state.deflectionWeapon))
            {
                state.CancelDeflection();
            }
        }

        public static bool ConsumeLoadoutInvalidatedCombatJob(
            Pawn pawn,
            Job job)
        {
            RimKataPawnCombatState state = StateFor(pawn, false);
            Job invalidatedJob = state?.loadoutInvalidatedCombatJob;
            if (invalidatedJob == null)
            {
                return false;
            }

            if (invalidatedJob != job)
            {
                if (pawn?.CurJob != invalidatedJob)
                {
                    state.loadoutInvalidatedCombatJob = null;
                }

                return false;
            }

            state.loadoutInvalidatedCombatJob = null;
            return true;
        }

        private static bool WeaponStillHeld(Pawn pawn, ThingWithComps weapon)
        {
            return weapon != null
                && !weapon.Destroyed
                && pawn?.equipment?.AllEquipmentListForReading?.Contains(weapon)
                    == true;
        }

        public static void TickIdleCycleTimers(Pawn pawn)
        {
            RimKataPawnCombatState state = StateFor(pawn, false);
            int currentTick = Find.TickManager.TicksGame;
            if (state == null
                || state.dualLastDrivenTick == currentTick
                || ShouldPauseFireForDodge(pawn))
            {
                return;
            }

            state.primaryWeaponCycle?.TickTimers();
            state.secondaryWeaponCycle?.TickTimers();
        }

        public static void DeactivateNonJobCycleWork(Pawn pawn)
        {
            RimKataPawnCombatState state = StateFor(pawn, false);
            if (state == null)
            {
                return;
            }

            NormalizeInvalidInterceptionState(pawn, state);

            RimKataSharedTargetSearch.Prune(pawn, state);
            RefreshDualEngagementState(pawn, state);
            if (state.dualEngagementActive)
            {
                RearmOpeningOwnerIfBothWaiting(state);
                if (state.sharedTargetSearch?.scanActive == true
                    && !state.dedicatedFollowupJobPending
                    && pawn?.Drafted != true)
                {
                    QueueDedicatedFollowupJob(pawn, null);
                }
                return;
            }

            CancelUnfiredWarmupForDraftChange(state.primaryWeaponCycle);
            CancelUnfiredWarmupForDraftChange(state.secondaryWeaponCycle);
            state.dualEngagementActive = false;
            state.ResetCandidateSaturationExpansion(true);
            RearmOpeningOwnerIfBothWaiting(state);
        }

        public static void CancelOffenseForMentalState(Pawn pawn)
        {
            RimKataPawnCombatState state = StateFor(pawn, false);
            if (state == null || state.mentalStateOffenseSuppressed)
            {
                return;
            }

            state.mentalStateOffenseSuppressed = true;
            state.CancelDraftedFire(false);
            state.ClearDraftedMovementSearchTracking();
            state.CancelCloseCombat();
            state.incomingThreatSource = null;
            state.incomingThreatTicksRemaining = 0;
            state.ClearCloseAttackRequest();
            state.automaticAttackRequestTarget = null;
            state.automaticAttackRequestTicksRemaining = 0;
            state.dedicatedContinuityTarget = null;
            state.dedicatedContinuityUntilTick = -1;
            state.absorbedPathBlockedGotoJobId = -1;
            state.absorbedPathBlockedThreat = null;
            state.absorbedPathBlockedRefreshTick = -1;
            state.loadoutInvalidatedCombatJob = null;
            state.weaponSwapPending = false;
            Reset(pawn, false);

            if (pawn?.stances?.curStance is Stance_RimKataAim)
            {
                pawn.stances.SetStance(new Stance_Mobile());
            }
        }

        public static void Reset(Pawn pawn, bool clearCooldowns)
        {

            RimKataPawnCombatState state = StateFor(pawn, false);
            if (state == null)
            {
                return;
            }

            state.dualEngagementActive = false;
            state.dualLastDrivenTick = -1;
            state.dualCloseCombatActive = false;

            state.dualCloseTarget = null;
            state.engagementOwnerWeapon = null;
            state.sharedTargetSearch?.Reset();
            state.ClearDedicatedFollowupJobRequest();
            state.projectileWakeResumeJob = null;
            if (clearCooldowns)
            {
                state.primaryWeaponCycle.Reset();
                state.secondaryWeaponCycle.Reset();
            }
            else
            {
                ResetCyclePreservingCooldown(state.primaryWeaponCycle);
                ResetCyclePreservingCooldown(state.secondaryWeaponCycle);
                state.dualLastDrivenTick = Find.TickManager.TicksGame;
            }
            state.ResetCandidateSaturationExpansion(true);
        }

        private static void ResetCyclePreservingCooldown(
            RimKataWeaponCycleState cycle)
        {
            if (cycle == null)
            {
                return;
            }

            ThingWithComps weapon = cycle.weapon;
            int cooldown = Mathf.Max(0, cycle.cooldownTicksRemaining);
            bool cooldownFromVanillaOpening = cooldown > 0
                && cycle.cooldownFromVanillaOpening;
            cycle.Reset();
            cycle.Bind(weapon);
            cycle.cooldownTicksRemaining = cooldown;
            cycle.cooldownFromVanillaOpening = cooldownFromVanillaOpening;
        }

        private static bool TryGetContinuationTarget(
            Pawn pawn,
            RimKataWeaponCycleState cycle,
            bool playerForced,
            bool killIncappedTarget,
            out Thing target)
        {
            target = cycle?.plannedTarget ?? cycle?.cachedCandidateTarget;
            Verb verb = RimKataWeaponSlotUtility.CombatVerbForContext(
                pawn,
                cycle?.weapon,
                cycle?.plannedCloseContext == true);
            if (pawn?.Map == null
                || cycle == null
                || verb == null
                || cycle.plannedInterception
                || cycle.cachedCandidateInterception
                || target == null
                || target.Destroyed
                || !target.Spawned
                || target.Map != pawn.Map
                || !RimKataTargeting.IsValidAutomaticAttackTarget(
                    pawn,
                    target)
                || !RimKataSharedTargetSearch.IsValidForVerb(
                    pawn,
                    verb,
                    target)
                || !pawn.CanReach(
                    target,
                    PathEndMode.Touch,
                    Danger.Deadly))
            {
                target = null;
                return false;
            }

            return true;
        }

        private static bool CycleHasContinuationSearchWork(RimKataWeaponCycleState cycle)
        {
            return cycle != null
                && (cycle.HasAutomaticCandidates
                    || cycle.plannedInterception
                    || cycle.cachedCandidateInterception);
        }

        private static bool HasProjectileOnlySearchWork(
            Pawn pawn,
            RimKataPawnCombatState state,
            Thing assignedTarget = null)
        {
            if (pawn?.Map == null
                || state == null
                || HasNonProjectileSearchDemand(
                    pawn,
                    state,
                    assignedTarget))
            {
                return false;
            }

            return HasActiveInterceptionWork(pawn, state.primaryWeaponCycle)
                || HasActiveInterceptionWork(pawn, state.secondaryWeaponCycle);
        }

        private static bool HasNonProjectileSearchDemand(
            Pawn pawn,
            RimKataPawnCombatState state,
            Thing assignedTarget = null)
        {
            return state != null
                && (state.dualCloseCombatActive
                    || IsLiveNonProjectileTarget(pawn, assignedTarget)
                    || IsLiveNonProjectileTarget(
                        pawn,
                        state.automaticAttackRequestTarget)
                    || IsLiveNonProjectileTarget(
                        pawn,
                        state.closeAttackRequestTarget)
                    || HasNonProjectileCycleWork(
                        pawn,
                        state,
                        state.primaryWeaponCycle)
                    || HasNonProjectileCycleWork(
                        pawn,
                        state,
                        state.secondaryWeaponCycle));
        }

        private static bool HasNonProjectileCycleWork(
            Pawn pawn,
            RimKataPawnCombatState state,
            RimKataWeaponCycleState cycle)
        {
            if (cycle == null || cycle.HasAutomaticCandidates)
            {
                return cycle?.HasAutomaticCandidates == true;
            }

            bool closeContext = cycle.plannedCloseContext
                || state?.dualCloseCombatActive == true;
            Verb verb = RimKataWeaponSlotUtility.CombatVerbForContext(
                pawn,
                cycle.weapon,
                closeContext);
            return FocusedTargetUsableNow(
                    pawn,
                    cycle,
                    verb,
                    closeContext)
                || (!cycle.cachedCandidateInterception
                    && IsLiveNonProjectileTarget(
                        pawn,
                        cycle.cachedCandidateTarget))
                || (!cycle.plannedInterception
                    && IsLiveNonProjectileTarget(pawn, cycle.plannedTarget));
        }

        private static bool HasActiveInterceptionWork(
            Pawn pawn,
            RimKataWeaponCycleState cycle)
        {
            return cycle != null
                && ((cycle.plannedInterception
                        && IsActiveInterceptionTarget(
                            pawn,
                            cycle.plannedTarget as Projectile))
                    || (cycle.cachedCandidateInterception
                        && IsActiveInterceptionTarget(
                            pawn,
                            cycle.cachedCandidateTarget as Projectile)));
        }

        private static bool IsLiveNonProjectileTarget(Pawn pawn, Thing target)
        {
            return target != null
                && !(target is Projectile)
                && !target.Destroyed
                && target.Spawned
                && target.Map == pawn?.Map
                && (!(target is Pawn targetPawn)
                    || (!targetPawn.Dead
                        && !targetPawn.Downed
                        && !targetPawn.Crawling
                        && !targetPawn.IsPsychologicallyInvisible()));
        }

        private static Thing ResolveCloseTarget(
            Pawn pawn,
            RimKataPawnCombatState state,
            Thing assignedTarget,
            bool playerForced,
            bool killIncappedTarget)
        {
            Thing requested = state?.CloseAttackRequestActive == true
                ? state.closeAttackRequestTarget
                : null;
            if (IsImmediateCloseTarget(
                pawn,
                requested,
                playerForced,
                killIncappedTarget))
            {
                return requested;
            }

            if (IsImmediateCloseTarget(
                pawn,
                assignedTarget,
                playerForced,
                killIncappedTarget))
            {
                return assignedTarget;
            }

            Thing trigger = state?.closeCombatTrigger;
            if (IsImmediateCloseTarget(pawn, trigger, false, false))
            {
                return trigger;
            }

            return ClosestStoredImmediateCloseTarget(pawn, state);
        }

        private static Thing ClosestStoredImmediateCloseTarget(
            Pawn pawn,
            RimKataPawnCombatState state)
        {
            Thing closest = null;
            int closestDistance = int.MaxValue;
            SelectClosestStoredImmediateCloseTarget(
                pawn,
                state?.primaryWeaponCycle,
                ref closest,
                ref closestDistance);
            SelectClosestStoredImmediateCloseTarget(
                pawn,
                state?.secondaryWeaponCycle,
                ref closest,
                ref closestDistance);
            return closest;
        }

        private static void SelectClosestStoredImmediateCloseTarget(
            Pawn pawn,
            RimKataWeaponCycleState cycle,
            ref Thing closest,
            ref int closestDistance)
        {
            List<Thing> candidates = cycle?.automaticCandidates;
            if (candidates == null)
            {
                return;
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                Thing candidate = candidates[i];
                if (!IsImmediateCloseTarget(pawn, candidate, false, false))
                {
                    continue;
                }

                int distance = pawn.Position.DistanceToSquared(
                    candidate.Position);
                if (distance < closestDistance)
                {
                    closest = candidate;
                    closestDistance = distance;
                }
            }
        }

        private static bool IsImmediateCloseTarget(
            Pawn pawn,
            Thing target,
            bool playerForced,
            bool killIncappedTarget)
        {
            return pawn?.Map != null
                && target != null
                && !target.Destroyed
                && target.Spawned
                && target.Map == pawn.Map
                && (playerForced
                    || RimKataTargeting.IsAutomaticEnemy(pawn, target))
                && target is Pawn targetPawn
                && !targetPawn.Dead
                && (!targetPawn.Downed
                    || (playerForced && killIncappedTarget))
                && (!targetPawn.Crawling || playerForced)
                && (playerForced
                    || !targetPawn.IsPsychologicallyInvisible())
                && pawn.CanReachImmediate(target, PathEndMode.Touch);
        }

        private static void HandleCloseCombatTransition(
            Pawn pawn,
            RimKataPawnCombatState state,
            bool closeCombatContext,
            Thing closeTarget)
        {
            bool restartCloseContext = closeCombatContext
                && (!state.dualCloseCombatActive
                    || state.dualCloseTarget == null);
            bool changedCloseTarget = closeCombatContext
                && (!state.dualCloseCombatActive || state.dualCloseTarget != closeTarget);
            if (changedCloseTarget)
            {
                state.dualCloseCombatActive = true;
                state.dualCloseTarget = closeTarget;
                state.EnterCloseCombat(closeTarget);
                if (restartCloseContext)
                {
                    SanitizeCycleForCloseCombat(
                        pawn,
                        state.primaryWeaponCycle);
                    SanitizeCycleForCloseCombat(
                        pawn,
                        state.secondaryWeaponCycle);
                    RimKataSharedTargetSearch.Restart(
                        pawn,
                        state,
                        pawn.Position);
                }
                return;
            }

            if (closeCombatContext || !state.dualCloseCombatActive)
            {
                return;
            }

            state.dualCloseCombatActive = false;
            state.dualCloseTarget = null;
            state.CancelCloseCombat();
            state.ClearCloseAttackRequest();
            state.ResetCandidateSaturationExpansion(true);
            RimKataSharedTargetSearch.Restart(pawn, state, pawn.Position);
        }

        private static void SanitizeCycleForCloseCombat(
            Pawn pawn,
            RimKataWeaponCycleState cycle)
        {
            Verb verb = RimKataWeaponSlotUtility.CombatVerbForContext(
                pawn,
                cycle?.weapon,
                true);
            if (cycle == null)
            {
                return;
            }

            if (pawn?.stances?.curStance is Stance_RimKataAim aim
                && aim.verb?.EquipmentSource == cycle.weapon
                && aim.verb != verb)
            {
                pawn.stances.SetStance(new Stance_Mobile());
            }

            if (verb == null)
            {
                cycle.ClearAutomaticCandidates();
                cycle.ClearPlan();
                cycle.focusedTarget = null;
                cycle.focusedTargetFromAttackGizmo = false;
                cycle.visualTarget = null;
                cycle.visualAimTicksRemaining = 0;
                return;
            }

            if (cycle.cachedCandidateTarget != null
                && !RimKataSharedTargetSearch.IsValidForVerb(
                    pawn,
                    verb,
                    cycle.cachedCandidateTarget))
            {
                cycle.cachedCandidateTarget = null;
                cycle.cachedCandidateInterception = false;
            }

            if (cycle.plannedTarget != null
                && !cycle.plannedInterception
                && !RimKataSharedTargetSearch.IsValidForVerb(
                    pawn,
                    verb,
                    cycle.plannedTarget))
            {
                ClearTargetPreservingCycle(cycle);
            }

            if (cycle.visualTarget != null
                && !(cycle.visualTarget is Projectile)
                && !RimKataSharedTargetSearch.IsValidForVerb(
                    pawn,
                    verb,
                    cycle.visualTarget))
            {
                cycle.visualTarget = null;
                cycle.visualAimTicksRemaining = 0;
            }
        }

        private static void ImportLegacyDraftedState(RimKataPawnCombatState state)
        {
            RimKataWeaponCycleState cycle = state.primaryWeaponCycle;
            if (cycle == null
                || cycle.Active
                || (state.draftedCooldownTicksRemaining <= 0
                    && state.draftedPlannedTarget == null))
            {
                return;
            }

            cycle.cooldownTicksRemaining = Mathf.Max(0, state.draftedCooldownTicksRemaining);
            cycle.warmupTicksRemaining = state.draftedPlannedTarget != null
                ? Mathf.Max(1, state.draftedWarmupTicksRemaining)
                : -1;
            cycle.warmupTotalTicks = Mathf.Max(0, cycle.warmupTicksRemaining);
            cycle.plannedTarget = state.draftedPlannedTarget;
            cycle.visualTarget = state.draftedPlannedTarget;
            cycle.plannedInterception = state.draftedPlannedInterception;
            cycle.plannedCloseAttack = state.draftedPlannedCloseAttack;
            cycle.plannedCloseContext = state.draftedPlannedCloseContext;
            cycle.plannedActionVerb = null;
            state.draftedCooldownTicksRemaining = 0;
            state.draftedWarmupTicksRemaining = -1;
            state.draftedPlannedTarget = null;
            state.draftedPlannedInterception = false;
            state.draftedPlannedCloseAttack = false;
            state.draftedPlannedCloseContext = false;
        }

        private static void BindCurrentWeapons(Pawn pawn, RimKataPawnCombatState state)
        {
            state.primaryWeaponCycle.Bind(RimKataWeaponSlotUtility.PrimaryWeapon(pawn));
            state.secondaryWeaponCycle.Bind(
                RimKataWeaponSlotUtility.CanUseSecondarySlot(pawn)
                    ? RimKataWeaponSlotUtility.SecondaryWeapon(pawn)
                    : null);
        }

        private static bool PrepareCycle(
            Pawn pawn,
            RimKataPawnCombatState state,
            RimKataWeaponCycleState cycle,
            Thing assignedTarget,
            bool playerForced,
            bool killIncappedTarget,
            bool closeCombatContext,
            bool blockedByStance,
            bool allowAutomaticRangedFire = true)
        {
            Verb verb = RimKataWeaponSlotUtility.CombatVerbForContext(
                pawn,
                cycle.weapon,
                closeCombatContext);
            if (cycle.weapon == null
                || !RimKataEquipmentUtility.IsWeaponEnabled(cycle.weapon.def)
                || verb == null
                || !VerbUsable(pawn, verb, closeCombatContext))
            {
                ApplyInterruptedBurstCooldown(pawn, cycle, verb);
                cycle.ClearPlan();
                return false;
            }

            bool newAutomaticRangedAttacksBlocked =
                !allowAutomaticRangedFire
                && !closeCombatContext
                && !verb.IsMeleeAttack;

            if (cycle.HasPlan
                && cycle.plannedTarget?.Spawned == true
                && cycle.plannedTargetCell.IsValid
                && cycle.plannedTargetCell != cycle.plannedTarget.Position)
            {
                cycle.plannedTargetCell = cycle.plannedTarget.Position;
                if (!cycle.plannedInterception)
                {
                    RimKataSharedTargetSearch.Begin(
                        pawn,
                        state,
                        pawn.Position);
                }
            }

            bool focusedTargetControlsCycle = PrepareFocusedTarget(
                pawn,
                cycle,
                verb,
                closeCombatContext);
            bool dedicatedAssignedTarget = pawn?.CurJobDef
                    == RimKataDefOf.RimKata_Attack
                && pawn.CurJob.targetA.Thing == assignedTarget;
            if (focusedTargetControlsCycle && !cycle.HasPlan)
            {
                return true;
            }

            if (newAutomaticRangedAttacksBlocked
                && !focusedTargetControlsCycle
                && !cycle.HasPlan)
            {
                return cycle.cooldownTicksRemaining > 0
                    || cycle.visualAimTicksRemaining > 0;
            }

            if (!focusedTargetControlsCycle
                && cycle.cachedCandidateTarget != null
                && PermanentlyInvalidCycleTarget(
                    pawn,
                    cycle.cachedCandidateTarget,
                    cycle.cachedCandidateTarget,
                    false,
                    false,
                    cycle.cachedCandidateInterception))
            {
                Thing invalidCachedTarget = cycle.cachedCandidateTarget;
                cycle.cachedCandidateTarget = null;
                cycle.cachedCandidateInterception = false;
                cycle.RemoveAutomaticCandidate(invalidCachedTarget);
            }

            if (!focusedTargetControlsCycle)
            {
                InterruptMovingFireOutsideAutomaticRange(
                    pawn,
                    cycle,
                    verb,
                    cycle.plannedTarget ?? cycle.visualTarget);
            }

            if (!focusedTargetControlsCycle
                && !cycle.HasPlan
                && cycle.cachedCandidateTarget == null)
            {
                TryCacheSharedCandidate(
                    pawn,
                    state,
                    cycle,
                    cycle.lastFiredTarget ?? assignedTarget);
            }
            if (cycle.cooldownTicksRemaining > 1)
            {
                return true;
            }

            if (!focusedTargetControlsCycle
                && !cycle.HasPlan
                && cycle.cachedCandidateTarget != null
                && cycle.cooldownTicksRemaining <= 1)
            {
                TryPromoteCachedCandidate(
                    pawn,
                    cycle,
                    verb,
                    killIncappedTarget,
                    closeCombatContext);
            }

            PromoteApproachingShotToCloseContext(pawn, cycle, verb, closeCombatContext);
            if (!ValidPlan(pawn, cycle, verb, assignedTarget, playerForced, killIncappedTarget, closeCombatContext))
            {
                Thing invalidTarget = cycle.plannedTarget ?? assignedTarget;
                ApplyInterruptedBurstCooldown(pawn, cycle, verb);
                ClearTargetPreservingCycle(cycle);

                if (invalidTarget != null
                    && !(invalidTarget is Projectile))
                {
                    cycle.RemoveAutomaticCandidate(invalidTarget);
                }

                if (!focusedTargetControlsCycle
                    && !newAutomaticRangedAttacksBlocked
                    && cycle.cooldownTicksRemaining <= 1)
                {
                    TryCacheSharedCandidate(
                        pawn,
                        state,
                        cycle,
                        cycle.lastFiredTarget ?? assignedTarget);
                    TryPromoteCachedCandidate(
                        pawn,
                        cycle,
                        verb,
                        killIncappedTarget,
                        closeCombatContext);
                }
            }

            if (newAutomaticRangedAttacksBlocked
                && !focusedTargetControlsCycle
                && !cycle.HasPlan)
            {
                return cycle.cooldownTicksRemaining > 0
                    || cycle.visualAimTicksRemaining > 0;
            }

            if (!focusedTargetControlsCycle
                && !cycle.HasPlan
                && cycle.cooldownTicksRemaining <= 1)
            {
                if (cycle.cachedCandidateTarget == null)
                {
                    TryCacheSharedCandidate(
                        pawn,
                        state,
                        cycle,
                        cycle.lastFiredTarget ?? assignedTarget);
                }

                if (cycle.cachedCandidateTarget != null)
                {
                    TryPromoteCachedCandidate(
                        pawn,
                        cycle,
                        verb,
                        killIncappedTarget,
                        closeCombatContext);
                }

                if (!cycle.HasPlan)
                {
                    TrySetKnownTarget(
                        pawn,
                        cycle,
                        verb,
                        assignedTarget,
                        playerForced,
                        killIncappedTarget,
                        closeCombatContext,
                        !playerForced && !dedicatedAssignedTarget,
                        cycle.cooldownTicksRemaining <= 0);
                }
            }

            if (!focusedTargetControlsCycle
                && InterruptMovingFireOutsideAutomaticRange(
                    pawn,
                    cycle,
                    verb,
                    cycle.plannedTarget))
            {
                return true;
            }

            if (cycle.cooldownTicksRemaining > 0 || blockedByStance)
            {
                return true;
            }

            if (cycle.HasPlan && cycle.warmupTicksRemaining < 0)
            {
                if (!closeCombatContext
                    && (!newAutomaticRangedAttacksBlocked
                        || focusedTargetControlsCycle))
                {
                    RimKataSharedTargetSearch.Begin(
                        pawn,
                        state,
                        pawn.Position);
                }

                cycle.plannedActionVerb = ResolveCycleActionVerb(
                    pawn,
                    cycle,
                    verb,
                    closeCombatContext);
                if (cycle.plannedActionVerb == null)
                {
                    ClearTargetPreservingCycle(cycle);
                    return false;
                }

                int normalWarmup = RimKataCombatMath.WarmupTicksForSingleShot(
                    cycle.plannedActionVerb);
                int openingBonus = ResolveOpeningSupportBonus(
                    pawn,
                    state,
                    cycle);

                int totalWarmup = normalWarmup + openingBonus;
                cycle.warmupTotalTicks = totalWarmup;
                cycle.warmupTicksRemaining = totalWarmup;
                if (cycle.warmupTicksRemaining > 0)
                {
                    return true;
                }
            }

            return true;
        }

        private static bool TryPromoteCachedCandidate(
            Pawn pawn,
            RimKataWeaponCycleState cycle,
            Verb verb,
            bool killIncappedTarget,
            bool closeCombatContext)
        {
            Thing cachedTarget = cycle?.cachedCandidateTarget;
            bool cachedInterception =
                cycle?.cachedCandidateInterception == true;
            if (cycle == null || cachedTarget == null)
            {
                return false;
            }

            cycle.cachedCandidateTarget = null;
            cycle.cachedCandidateInterception = false;
            bool promoted = cachedInterception
                ? CanAssignInterceptionTarget(
                    pawn,
                    cycle,
                    verb,
                    cachedTarget)
                : TrySetKnownTarget(
                    pawn,
                    cycle,
                    verb,
                    cachedTarget,
                    false,
                    killIncappedTarget,
                    closeCombatContext,
                    RimKataEligibility.RandomAttackEnabledForPawn(pawn),
                    false);
            if (cachedInterception && promoted)
            {
                SetCandidate(
                    cycle,
                    cachedTarget,
                    true,
                    false,
                    false,
                    false);
            }
            else if (!promoted)
            {
                if (!(cachedTarget is Projectile))
                {
                    cycle.RemoveAutomaticCandidate(cachedTarget);
                }
                ClearTargetPreservingCycle(cycle);
            }

            return promoted;
        }

        private static int ResolveOpeningSupportBonus(
            Pawn pawn,
            RimKataPawnCombatState state,
            RimKataWeaponCycleState cycle)
        {
            if (state == null || cycle?.weapon == null)
            {
                return 0;
            }

            if (cycle.openingWarmupPending)
            {
                return Mathf.Max(0, cycle.openingWarmupBonusTicks);
            }

            if (cycle.openingSupportDelayConsumed)
            {
                return 0;
            }

            if (state.engagementOwnerWeapon == null)
            {
                state.engagementOwnerWeapon = cycle.weapon;
                return 0;
            }

            if (state.engagementOwnerWeapon == cycle.weapon)
            {
                return 0;
            }

            RimKataWeaponCycleState ownerCycle = CycleForWeapon(
                state,
                state.engagementOwnerWeapon);
            Verb ownerVerb = RimKataWeaponSlotUtility.CombatVerbForContext(
                pawn,
                ownerCycle?.weapon,
                state?.dualCloseCombatActive == true);
            if (ownerVerb == null)
            {
                return 0;
            }

            int bonus = Mathf.Max(
                1,
                Mathf.CeilToInt(
                    RimKataCombatMath.CooldownTicksForSingleShot(
                        ownerVerb,
                        pawn,
                        false) * 0.5f));
            cycle.openingWarmupBonusTicks = bonus;
            cycle.openingWarmupPending = true;
            cycle.openingSupportDelayConsumed = true;
            return bonus;
        }

        private static void RearmOpeningOwnerIfBothWaiting(
            RimKataPawnCombatState state)
        {
            if (state?.primaryWeaponCycle?.DebugState != 'W'
                || state.secondaryWeaponCycle?.DebugState != 'W')
            {
                return;
            }

            state.engagementOwnerWeapon = null;
            state.primaryWeaponCycle.openingWarmupBonusTicks = 0;
            state.primaryWeaponCycle.openingWarmupPending = false;
            state.primaryWeaponCycle.openingSupportDelayConsumed = false;
            state.secondaryWeaponCycle.openingWarmupBonusTicks = 0;
            state.secondaryWeaponCycle.openingWarmupPending = false;
            state.secondaryWeaponCycle.openingSupportDelayConsumed = false;
        }

        private static bool PrepareFocusedTarget(
            Pawn pawn,
            RimKataWeaponCycleState cycle,
            Verb verb,
            bool closeCombatContext)
        {
            Thing target = cycle?.focusedTarget;
            if (target == null)
            {
                return false;
            }

            if (PermanentlyInvalidCycleTarget(
                pawn,
                target,
                target,
                true,
                false,
                false))
            {
                cycle.focusedTarget = null;
                cycle.focusedTargetFromAttackGizmo = false;
                return false;
            }

            if (!FocusedTargetUsableNow(
                pawn,
                cycle,
                verb,
                closeCombatContext))
            {
                if (cycle.HasPlan && cycle.plannedTarget == target)
                {
                    ClearTargetPreservingCycle(cycle);
                }
                return false;
            }

            cycle.cachedCandidateTarget = null;
            cycle.cachedCandidateInterception = false;

            if (cycle.HasPlan && cycle.plannedTarget != target)
            {
                ClearTargetPreservingCycle(cycle);
            }

            if (!cycle.HasPlan && cycle.cooldownTicksRemaining <= 1)
            {
                TrySetKnownTarget(
                    pawn,
                    cycle,
                    verb,
                    target,
                    true,
                    false,
                    closeCombatContext,
                    false,
                    true);
            }

            if (!cycle.HasPlan)
            {
                cycle.visualTarget = target;
                cycle.visualAimTicksRemaining = Mathf.Max(
                    cycle.visualAimTicksRemaining,
                    2);
            }

            return true;
        }

        private static bool FocusedTargetUsableNow(
            Pawn pawn,
            RimKataWeaponCycleState cycle,
            Verb verb,
            bool closeCombatContext)
        {
            return cycle?.focusedTarget != null
                && ValidCurrentTargetForVerb(
                    pawn,
                    verb,
                    cycle.focusedTarget,
                    true,
                    false,
                    closeCombatContext);
        }

        private static void PromoteApproachingShotToCloseContext(
            Pawn pawn,
            RimKataWeaponCycleState cycle,
            Verb verb,
            bool closeCombatContext)
        {            
            if (!closeCombatContext
                || verb == null
                || verb.IsMeleeAttack
                || cycle == null
                || !cycle.HasPlan
                || cycle.plannedInterception
                || cycle.plannedCloseContext
                || !pawn.CanReachImmediate(cycle.plannedTarget, PathEndMode.Touch))
            {
                return;
            }

            cycle.plannedCloseContext = true;
            cycle.plannedCloseAttack = true;
        }

        private static void SetCandidate(
            RimKataWeaponCycleState cycle,
            Thing target,
            bool interception,
            bool closeAttack,
            bool closeContext,
            bool updateVisualTarget)
        {
            cycle.plannedTarget = target;
            cycle.plannedTargetCell = target?.Spawned == true
                ? target.Position
                : IntVec3.Invalid;
            cycle.plannedInterception = interception;
            cycle.plannedCloseAttack = closeAttack;
            cycle.plannedCloseContext = closeContext;
            cycle.plannedActionVerb = null;
            if (updateVisualTarget)
            {
                cycle.visualTarget = target;
            }
        }
                
        private static bool TrySetKnownTarget(
            Pawn pawn,
            RimKataWeaponCycleState cycle,
            Verb verb,
            Thing assignedTarget,
            bool playerForced,
            bool killIncappedTarget,
            bool closeCombatContext,
            bool automaticRangeRequired,
            bool updateVisualTarget)
        {
            if (pawn?.Map == null
                || cycle == null
                || verb == null
                || assignedTarget == null
                || assignedTarget.Destroyed
                || !assignedTarget.Spawned
                || assignedTarget.Map != pawn.Map
                || (!playerForced
                    && !RimKataTargeting.IsAutomaticEnemy(
                        pawn,
                        assignedTarget)))
            {
                return false;
            }

            if (assignedTarget is Pawn targetPawn
                && (targetPawn.Dead
                    || targetPawn.Crawling
                    || targetPawn.IsPsychologicallyInvisible()
                    || (targetPawn.Downed && !(playerForced && killIncappedTarget))))
            {
                return false;
            }

            if (verb.IsMeleeAttack || closeCombatContext)
            {
                if (!CanHitTargetForCombatContext(
                        pawn,
                        verb,
                        assignedTarget,
                        closeCombatContext))
                {
                    return false;
                }

                SetCandidate(cycle, assignedTarget, false, true, true, updateVisualTarget);
                return true;
            }

            if (automaticRangeRequired)
            {
                if (!RimKataSharedTargetSearch.IsValidForVerb(
                    pawn,
                    verb,
                    assignedTarget))
                {
                    return false;
                }
            }

            if (!verb.CanHitTarget(assignedTarget))
            {
                return false;
            }

            SetCandidate(cycle, assignedTarget, false, false, false, updateVisualTarget);
            return true;
        }

        private static bool CanAssignInterceptionTarget(
            Pawn pawn,
            RimKataWeaponCycleState cycle,
            Verb verb,
            Thing target)
        {
            if (pawn?.Map == null
                || verb == null
                || cycle == null
                || target == null
                || target.Destroyed
                || !target.Spawned
                || target.Map != pawn.Map
                || FocusedTargetUsableNow(
                    pawn,
                    cycle,
                    verb,
                    false)
                || cycle.cachedCandidateTarget != null
                || cycle.HasPlan
                || cycle.openingWarmupPending
                || cycle.burstShotsRemaining > 0)
            {
                return false;
            }

            if (verb.IsMeleeAttack)
            {
                return pawn.CanReachImmediate(target, PathEndMode.Touch);
            }

            if (target is Projectile)
            {
                return RimKataSharedTargetSearch.IsValidForVerb(
                    pawn,
                    verb,
                    target);
            }

            float range = RimKataRangeUtility.ResolveCandidateApiRange(verb);
            if (pawn.Position.DistanceToSquared(target.Position) > range * range)
            {
                return false;
            }

            if (verb.CanHitTarget(target))
            {
                return true;
            }

            return pawn.CanReachImmediate(target, PathEndMode.Touch)
                && RimKataEligibility.IsRangedVerbAvailableInCloseCombat(pawn, verb);
        }

        private static bool ValidPlan(
            Pawn pawn,
            RimKataWeaponCycleState cycle,
            Verb verb,
            Thing assignedTarget,
            bool playerForced,
            bool killIncappedTarget,
            bool closeCombatContext)
        {
            Thing target = cycle.plannedTarget;
            if (target == null
                || target.Destroyed
                || !target.Spawned
                || target.Map != pawn.Map)
            {
                return false;
            }

            if (cycle.plannedInterception)
            {
                return target is Projectile
                    && RimKataSharedTargetSearch.IsValidForVerb(
                        pawn,
                        verb,
                        target);
            }

            if (!verb.IsMeleeAttack && cycle.plannedCloseContext != closeCombatContext)
            {
                return false;
            }

            if (target is Pawn targetPawn
                && (targetPawn.Dead
                    || targetPawn.Crawling
                    || targetPawn.IsPsychologicallyInvisible()
                    || (targetPawn.Downed
                        && !(playerForced
                            && killIncappedTarget
                            && target == assignedTarget))))
            {
                return false;
            }

            bool playerFocusedTarget = target == cycle.focusedTarget;
            if (!RimKataTargeting.IsAutomaticEnemy(pawn, target)
                && !(playerForced && target == assignedTarget)
                && !playerFocusedTarget)
            {
                return false;
            }

            return CanHitTargetForCombatContext(
                pawn,
                verb,
                target,
                cycle.plannedCloseAttack);
        }

        private static bool PermanentlyInvalidCycleTarget(
            Pawn pawn,
            Thing target,
            Thing assignedTarget,
            bool playerForced,
            bool killIncappedTarget,
            bool interception)
        {
            if (pawn?.Map == null
                || target == null
                || target.Destroyed
                || !target.Spawned
                || target.Map != pawn.Map)
            {
                return true;
            }

            if (interception)
            {
                return !(target is Projectile projectile)
                    || !RimKataTargeting.IsInterceptionTargetActive(
                        projectile);
            }

            bool forcedAssignedTarget = playerForced && target == assignedTarget;
            if (!RimKataTargeting.IsAutomaticEnemy(pawn, target)
                && !forcedAssignedTarget)
            {
                return true;
            }

            return target is Pawn targetPawn
                && (targetPawn.Dead
                    || targetPawn.Crawling
                    || targetPawn.IsPsychologicallyInvisible()
                    || (targetPawn.Downed
                        && !(forcedAssignedTarget && killIncappedTarget)));
        }

        private static bool VerbUsable(Pawn pawn, Verb verb, bool closeCombatContext)
        {
            if (verb.IsMeleeAttack)
            {
                return verb.Available();
            }

            if (UsesPhysicalMeleeAction(verb, closeCombatContext))
            {
                return pawn?.kindDef?.canMeleeAttack == true
                    && pawn.meleeVerbs != null;
            }

            if (verb.ApparelPreventsShooting())
            {
                return false;
            }

            return closeCombatContext
                ? RimKataEligibility.IsRangedVerbAvailableInCloseCombat(pawn, verb)
                : verb.Available();
        }

        private static bool UsesPhysicalMeleeAction(
            Verb slotVerb,
            bool closeCombatContext)
        {
            return closeCombatContext
                && RimKataMod.Settings?.closeFireEnabled == false
                && slotVerb != null
                && !slotVerb.IsMeleeAttack;
        }

        private static Verb ResolveCycleActionVerb(
            Pawn pawn,
            RimKataWeaponCycleState cycle,
            Verb slotVerb,
            bool closeCombatContext)
        {
            if (!UsesPhysicalMeleeAction(slotVerb, closeCombatContext))
            {
                return slotVerb;
            }

            Thing target = cycle?.plannedTarget;
            return target != null
                ? pawn?.meleeVerbs?.TryGetMeleeVerb(target)
                : null;
        }

        private static bool ReadyToAct(RimKataWeaponCycleState cycle)
        {
            return cycle?.weapon != null
                && cycle.cooldownTicksRemaining <= 0
                && cycle.burstTicksUntilNextShot <= 0
                && cycle.HasPlan
                && cycle.warmupTicksRemaining == 0;
        }

        private static int ExecuteCycle(
            Pawn pawn,
            RimKataWeaponCycleState cycle,
            Thing assignedTarget,
            bool playerForced,
            bool killIncappedTarget,
            bool closeCombatContext,
            bool allowAutomaticRangedFire = true)
        {
            Verb verb = RimKataWeaponSlotUtility.CombatVerbForContext(
                pawn,
                cycle.weapon,
                closeCombatContext);
            if (verb == null)
            {
                ClearTargetPreservingCycle(cycle);
                return -1;
            }

            if (!ValidPlan(
                pawn,
                cycle,
                verb,
                assignedTarget,
                playerForced,
                killIncappedTarget,
                closeCombatContext))
            {
                HandleInvalidPlanAtExecution(
                    pawn,
                    cycle,
                    verb,
                    assignedTarget,
                    playerForced,
                    killIncappedTarget,
                    closeCombatContext);
                return -1;
            }

            bool physicalMeleeAction = UsesPhysicalMeleeAction(
                verb,
                closeCombatContext);
            Verb actionVerb = cycle.plannedActionVerb
                ?? ResolveCycleActionVerb(
                    pawn,
                    cycle,
                    verb,
                    closeCombatContext);
            if (actionVerb == null)
            {
                ClearTargetPreservingCycle(cycle);
                return -1;
            }
            cycle.plannedActionVerb = actionVerb;

            if (!allowAutomaticRangedFire
                && !actionVerb.IsMeleeAttack)
            {
                return -2;
            }

            LocalTargetInfo target = TargetInfo(cycle);
            if (!target.IsValid)
            {
                ClearTargetPreservingCycle(cycle);
                return -1;
            }

            if (!FocusedTargetUsableNow(
                    pawn,
                    cycle,
                    verb,
                    closeCombatContext)
                && InterruptMovingFireOutsideAutomaticRange(
                    pawn,
                    cycle,
                    verb,
                    cycle.plannedTarget))
            {
                return -1;
            }

            if (MovementBlocksFire(pawn))
            {
                ApplyInterruptedBurstCooldown(pawn, cycle, verb);
                cycle.ClearPlan();
                return -1;
            }

            bool firedFromVanillaOpening = cycle.openingWarmupPending
                || (cycle.burstShotsRemaining > 0
                    && cycle.cooldownFromVanillaOpening);
            bool acted;
            Projectile interceptedProjectile = cycle.plannedTarget as Projectile;
            if (actionVerb.IsMeleeAttack)
            {
                if (physicalMeleeAction)
                {
                    Pawn previousPawn = activePhysicalMeleePawn;
                    RimKataWeaponCycleState previousCycle = activePhysicalMeleeCycle;
                    activePhysicalMeleePawn = pawn;
                    activePhysicalMeleeCycle = cycle;
                    try
                    {
                        acted = RimKataVerbUtility.FireSingleShot(
                            actionVerb,
                            target,
                            false,
                            false);
                    }
                    finally
                    {
                        activePhysicalMeleePawn = previousPawn;
                        activePhysicalMeleeCycle = previousCycle;
                    }
                }
                else
                {
                    acted = RimKataVerbUtility.FireSingleShot(
                        actionVerb,
                        target,
                        false,
                        false);
                }
            }
            else if (cycle.plannedInterception)
            {
                acted = RimKataVerbUtility.FireSingleShot(actionVerb, target, pawn.pather?.MovingNow == true, false, true, false, false, RimKataCloseDefensePrecheck.None, interceptedProjectile);
            }
            else if (cycle.plannedCloseAttack)
            {
                bool rangedHit = RimKataCombatMath.RollCloseRangedNonMiss(pawn, actionVerb, target);

                RimKataCloseDefensePrecheck precheck = RimKataDefenseUtility.PrecheckCloseGunfire(pawn, cycle.plannedTarget, actionVerb, rangedHit);

                bool accidentalShot = precheck == RimKataCloseDefensePrecheck.ResponseSucceededWithAccidentalShot;

                acted =
                    precheck
                        == RimKataCloseDefensePrecheck.ResponseSucceeded
                    || RimKataVerbUtility.FireSingleShot(
                        actionVerb,
                        target,
                        false,
                        true,
                        false,
                        true,
                        accidentalShot
                            ? false
                            : rangedHit,
                        precheck);
            }
            else
            {
                acted = RimKataVerbUtility.FireSingleShot(actionVerb, target, pawn.pather?.MovingNow == true, false);
            }

            if (!acted)
            {
                ApplyInterruptedBurstCooldown(pawn, cycle, actionVerb);
                cycle.ClearPlan();
                return -1;
            }

            cycle.cooldownFromVanillaOpening = firedFromVanillaOpening;

            RimKataPawnCombatState state = StateFor(pawn, false);
            cycle.firedInCurrentOpening = firedFromVanillaOpening;
            RecordFirstFiredWeapon(state, cycle.weapon);
            bool allowAutomaticContinuation = allowAutomaticRangedFire
                || playerForced
                || closeCombatContext
                || verb.IsMeleeAttack
                || cycle.focusedTargetFromAttackGizmo;

            cycle.openingWarmupBonusTicks = 0;
            cycle.openingWarmupPending = false;

            bool useFullBurst = RimKataMod.Settings?.singleShotConversionEnabled == false && !actionVerb.IsMeleeAttack;
            if (useFullBurst && cycle.burstShotsRemaining <= 0)
            {
                cycle.burstShotsRemaining = Mathf.Max(1, actionVerb.BurstShotCount);
            }

            if (cycle.burstShotsRemaining > 0)
            {
                cycle.burstShotsRemaining--;
            }

            if (useFullBurst && cycle.burstShotsRemaining > 0)
            {
                cycle.burstTicksUntilNextShot = Mathf.Max(1, actionVerb.TicksBetweenBurstShots);
                cycle.visualTarget = cycle.plannedTarget;
                cycle.visualAimTicksRemaining = Mathf.Max(cycle.visualAimTicksRemaining, cycle.burstTicksUntilNextShot + 2);
                return -2;
            }

            Thing firedTarget = cycle.plannedTarget;
            int cooldown = RimKataCombatMath.CooldownTicksForSingleShot(actionVerb, pawn, false);
            cycle.cooldownTicksRemaining = cooldown;
            cycle.lastFiredTarget = firedTarget;
            cycle.visualTarget = firedTarget;
            cycle.visualAimTicksRemaining = Mathf.Max(RimKataCombatTuning.PostShotAimTicks, cooldown + 2);
            cycle.ClearPlan();

            if (!allowAutomaticContinuation)
            {
                cycle.cachedCandidateTarget = null;
                cycle.cachedCandidateInterception = false;
                return cooldown;
            }

            if (state != null)
            {
                RimKataSharedTargetSearch.Begin(
                    pawn,
                    state,
                    pawn.Position);
            }

            return cooldown;
        }

        private static void HandleInvalidPlanAtExecution(
            Pawn pawn,
            RimKataWeaponCycleState cycle,
            Verb verb,
            Thing assignedTarget,
            bool playerForced,
            bool killIncappedTarget,
            bool closeCombatContext)
        {
            Thing invalidTarget = cycle.plannedTarget ?? assignedTarget;
            ApplyInterruptedBurstCooldown(pawn, cycle, verb);
            ClearTargetPreservingCycle(cycle);

            if (invalidTarget != null
                && !(invalidTarget is Projectile))
            {
                cycle.RemoveAutomaticCandidate(invalidTarget);
            }
        }

        private static void ApplyInterruptedBurstCooldown(
            Pawn pawn,
            RimKataWeaponCycleState cycle,
            Verb verb)
        {
            if (cycle?.burstShotsRemaining > 0 && pawn != null && verb != null)
            {
                cycle.cooldownTicksRemaining = Mathf.Max(cycle.cooldownTicksRemaining, RimKataCombatMath.CooldownTicksForSingleShot(verb, pawn, false));
            }
        }

        private static bool StanceBlocksRimKata(Pawn pawn)
        {
            return pawn?.stances?.stunner?.Stunned == true;
        }

        private static bool MovementBlocksFire(Pawn pawn)
        {
            if (RimKataDodgeMovementUtility.IsActive(pawn))
            {
                return false;
            }

            return RimKataMod.Settings?.movingFireEnabled == false && pawn?.pather?.MovingNow == true;
        }

        private static bool InterruptMovingFireOutsideAutomaticRange(
            Pawn pawn,
            RimKataWeaponCycleState cycle,
            Verb verb,
            Thing target)
        {
            if (pawn?.pather?.MovingNow != true
                || verb == null
                || verb.IsMeleeAttack
                || cycle == null
                || target == null
                || TargetWithinAutomaticSearchRange(pawn, target))
            {
                return false;
            }

            if (pawn.CurJobDef == RimKataDefOf.RimKata_Attack
                && pawn.CurJob.targetA.Thing == target
                && verb.CanHitTarget(target))
            {
                return false;
            }

            if (!RimKataEligibility.RandomAttackEnabledForPawn(pawn)
                && !(target is Projectile)
                && verb.CanHitTarget(target))
            {
                return false;
            }

            ApplyInterruptedBurstCooldown(pawn, cycle, verb);
            bool focusedTargetUsable = FocusedTargetUsableNow(
                pawn,
                cycle,
                verb,
                false);
            ClearTargetPreservingCycle(cycle);

            if (cycle.cachedCandidateTarget != null
                && !TargetWithinAutomaticSearchRange(
                    pawn,
                    cycle.cachedCandidateTarget))
            {
                cycle.cachedCandidateTarget = null;
                cycle.cachedCandidateInterception = false;
            }

            if (pawn.stances?.curStance is Stance_RimKataAim aim
                && aim.verb == verb)
            {
                pawn.stances.SetStance(new Stance_Mobile());
            }

            if (!focusedTargetUsable
                && cycle.cachedCandidateTarget == null)
            {
                RimKataPawnCombatState state = StateFor(pawn, false);
                if (state != null)
                {
                    RimKataSharedTargetSearch.Begin(
                        pawn,
                        state,
                        pawn.Position);
                }
            }

            return true;
        }

        private static void InterruptCycleForMovement(
            Pawn pawn,
            RimKataWeaponCycleState cycle,
            bool closeCombatContext)
        {
            if (cycle == null || !cycle.HasPlan)
            {
                return;
            }

            Verb verb = RimKataWeaponSlotUtility.CombatVerbForContext(
                pawn,
                cycle.weapon,
                closeCombatContext);
            ApplyInterruptedBurstCooldown(pawn, cycle, verb);
            if (cycle.openingWarmupPending
                && !cycle.firedInCurrentOpening)
            {
                ResetUnfiredOpeningTimer(cycle);
            }
            else
            {
                cycle.ClearPlan();
            }
        }

        private static LocalTargetInfo TargetInfo(RimKataWeaponCycleState cycle)
        {
            if (cycle.plannedInterception && cycle.plannedTarget is Projectile projectile)
            {
                return new LocalTargetInfo(projectile.ExactPosition.ToIntVec3());
            }

            return cycle.plannedTarget != null
                ? new LocalTargetInfo(cycle.plannedTarget)
                : LocalTargetInfo.Invalid;
        }

        private static void UpdateBodyAimStance(Pawn pawn, RimKataPawnCombatState state)
        {
            if ((state.responsePoseLookAtFocus
                    && state.TryGetLiveResponsePoseFocus(
                        out LocalTargetInfo _))
                || StanceBlocksRimKata(pawn))
            {
                return;
            }

            if (state.dualCloseCombatActive
                && RimKataMod.Settings?.closeFireEnabled == false
                && pawn.stances?.curStance is Stance_RimKataAim closeAim
                && closeAim.verb?.IsMeleeAttack == false)
            {
                pawn.stances.SetStance(new Stance_Mobile());
            }

            if (!TryGetNextAim(
                    pawn,
                    out ThingWithComps weapon,
                    out LocalTargetInfo target))
            {
                if (pawn.stances?.curStance is Stance_RimKataAim oldAim
                    && oldAim.focusTarg.HasThing
                    && !IsLiveVisualTarget(pawn, oldAim.focusTarg.Thing))
                {
                    pawn.stances.SetStance(new Stance_Mobile());
                }

                return;
            }

            Verb slotVerb = RimKataWeaponSlotUtility.CombatVerbForContext(
                pawn,
                weapon,
                state?.dualCloseCombatActive == true);
            RimKataWeaponCycleState aimCycle = CycleForWeapon(state, weapon);
            bool physicalMeleeAction = UsesPhysicalMeleeAction(
                slotVerb,
                state?.dualCloseCombatActive == true);
            Verb verb = aimCycle?.plannedActionVerb ?? slotVerb;
            if (physicalMeleeAction)
            {
                if (verb?.IsMeleeAttack != true
                    && pawn.stances?.curStance is Stance_RimKataAim currentAim
                    && currentAim.verb?.IsMeleeAttack == true
                    && currentAim.focusTarg.Equals(target))
                {
                    verb = currentAim.verb;
                }

                if (verb?.IsMeleeAttack != true && target.HasThing)
                {
                    verb = pawn.meleeVerbs?.TryGetMeleeVerb(target.Thing);
                }

                if (verb == null)
                {
                    if (target.IsValid
                        && target.Cell.IsValid
                        && target.Cell != pawn.Position)
                    {
                        pawn.rotationTracker.FaceCell(target.Cell);
                    }

                    return;
                }
            }

            if (verb == null)
            {
                return;
            }

            if (pawn.stances.curStance is Stance_RimKataAim current && current.verb == verb && current.focusTarg.Equals(target))
            {
                current.ticksLeft = Mathf.Max(current.ticksLeft, 2);
                current.RefreshLeanNow();
                return;
            }

            if (pawn.stances.curStance is Stance_Busy)
            {
                return;
            }

            Stance_RimKataAim aim = new Stance_RimKataAim(2, target, verb);
            pawn.stances.SetStance(aim);
            aim.RefreshLeanNow();
        }

        private static RimKataWeaponCycleState ChooseBodyAimCycle(
            Pawn pawn,
            RimKataPawnCombatState state)
        {
            RimKataWeaponCycleState primary = state.primaryWeaponCycle;
            RimKataWeaponCycleState secondary = state.secondaryWeaponCycle;
            bool primaryHasAim = IsLiveVisualTarget(pawn, primary?.plannedTarget)
                || IsLiveVisualTarget(pawn, primary?.visualTarget);
            bool secondaryHasAim = IsLiveVisualTarget(pawn, secondary?.plannedTarget)
                || IsLiveVisualTarget(pawn, secondary?.visualTarget);
            if (!secondaryHasAim)
            {
                return primaryHasAim ? primary : null;
            }

            if (!primaryHasAim)
            {
                return secondary;
            }

            int primaryEta = Mathf.Max(0, primary.cooldownTicksRemaining) + Mathf.Max(0, primary.warmupTicksRemaining);
            int secondaryEta = Mathf.Max(0, secondary.cooldownTicksRemaining) + Mathf.Max(0, secondary.warmupTicksRemaining);
            if (primary.warmupTicksRemaining < 0)
            {
                primaryEta += RimKataCombatMath.WarmupTicksForSingleShot(
                    RimKataWeaponSlotUtility.CombatVerbForContext(
                        pawn,
                        primary.weapon,
                        state?.dualCloseCombatActive == true));
            }

            if (secondary.warmupTicksRemaining < 0)
            {
                secondaryEta += RimKataCombatMath.WarmupTicksForSingleShot(
                    RimKataWeaponSlotUtility.CombatVerbForContext(
                        pawn,
                        secondary.weapon,
                        state?.dualCloseCombatActive == true));
            }

            return primaryEta <= secondaryEta ? primary : secondary;
        }

        private static RimKataWeaponCycleState CycleForWeapon(
            RimKataPawnCombatState state,
            ThingWithComps weapon)
        {
            if (state == null || weapon == null)
            {
                return null;
            }

            if (state.primaryWeaponCycle?.weapon == weapon)
            {
                return state.primaryWeaponCycle;
            }

            return state.secondaryWeaponCycle?.weapon == weapon
                ? state.secondaryWeaponCycle
                : null;
        }

        private static RimKataPawnCombatState StateFor(Pawn pawn, bool create)
        {
            return pawn?.Map?.GetComponent<RimKataMapComponent>()?.GetState(pawn, create);
        }

    }

    [HarmonyPatch(
        typeof(Verb),
        nameof(Verb.TryStartCastOn),
        new Type[]
        {
            typeof(LocalTargetInfo),
            typeof(LocalTargetInfo),
            typeof(bool),
            typeof(bool),
            typeof(bool),
            typeof(bool)
        })]
    public static class Patch_Verb_TryStartCastOn_RimKataOpening
    {
        public static bool Prefix(
            Verb __instance,
            LocalTargetInfo __0,
            ref bool __result,
            out RimKataVanillaOpeningAttempt __state)
        {
            __state = default(RimKataVanillaOpeningAttempt);
            if (RimKataDualWeaponController.ShouldSuppressVanillaCast(
                __instance?.CasterPawn,
                __instance,
                __0))
            {
                __result = false;
                return false;
            }

            __state = RimKataDualWeaponController.PrepareVanillaOpening(
                __instance?.CasterPawn,
                __instance,
                __0);
            return true;
        }

        public static void Postfix(
            Verb __instance,
            bool __result,
            RimKataVanillaOpeningAttempt __state)
        {
            try
            {
                if (__state.prepared && __result)
                {
                    RimKataDualWeaponController.CommitVanillaOpening(
                        __instance?.CasterPawn,
                        __instance,
                        __state);
                }
            }
            finally
            {
                RimKataDualWeaponController.FinishVanillaOpeningAttempt(
                    __instance);
            }
        }

        public static Exception Finalizer(
            Verb __instance,
            Exception __exception)
        {
            RimKataDualWeaponController.FinishVanillaOpeningAttempt(
                __instance);
            return __exception;
        }
    }

    [HarmonyPatch(
        typeof(Pawn_DraftController),
        nameof(Pawn_DraftController.Drafted),
        MethodType.Setter)]
    public static class Patch_PawnDraftController_RimKataCycleReset
    {
        public static void Prefix(Pawn_DraftController __instance, out bool __state)
        {
            __state = __instance?.Drafted == true;
        }

        public static void Postfix(
            Pawn_DraftController __instance,
            bool __0,
            bool __state)
        {
            if (__state != __0)
            {
                RimKataDualWeaponController.NotifyDraftStatusChanged(__instance?.pawn);
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_StanceTracker), nameof(Pawn_StanceTracker.SetStance))]
    public static class Patch_PawnStanceTracker_RimKataCooldown
    {
        public static bool Prefix(
            Stance newStance,
            Pawn ___pawn)
        {
            if (!(newStance is Stance_Cooldown cooldown))
            {
                return true;
            }

            if (cooldown.verb == null)
            {
                return true;
            }

            if (cooldown.verb.IsMeleeAttack
                && RimKataDualWeaponController.TryTakeVanillaMeleeCooldown(
                    ___pawn,
                    cooldown.verb,
                    cooldown.focusTarg))
            {
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(Stance_Warmup), "Expire")]
    public static class Patch_StanceWarmup_RimKataPendingFollowupAim
    {
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(Stance_Warmup __instance)
        {
            RimKataDualWeaponController.RefreshPendingDedicatedFollowupAim(
                __instance?.stanceTracker?.pawn,
                __instance?.verb,
                __instance?.focusTarg ?? LocalTargetInfo.Invalid);
        }
    }

    [HarmonyPatch(typeof(Stance_Busy), "Expire")]
    public static class Patch_StanceCooldown_RimKataPendingFollowupAim
    {
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(Stance_Busy __instance)
        {
            if (__instance?.GetType() != typeof(Stance_Cooldown))
            {
                return;
            }

            RimKataDualWeaponController.RefreshPendingDedicatedFollowupAim(
                __instance.stanceTracker?.pawn,
                __instance.verb,
                __instance.focusTarg);
        }
    }
}
