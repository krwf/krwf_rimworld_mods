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

    public enum RimKataRushAuthority
    {
        None,
        Counterattack
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
        public bool postShotCacheAttempted;
        public Thing lastFiredTarget;
        public Thing vanillaOpeningTarget;
        public Thing vanillaFollowupTarget;
        public bool vanillaOpeningPending;
        public bool vanillaOpeningSingleShotClaimed;
        public bool vanillaOpeningFirstShotSearchTriggered;
        public bool vanillaOpeningCloseContext;
        public IntVec3 vanillaOpeningTargetCell = IntVec3.Invalid;
        public bool firedInCurrentOpening;
        public bool cooldownFromVanillaOpening;
        public bool skipNextWarmup;
        public int candidateRetryTicks;
        public int burstShotsRemaining;
        public int burstTicksUntilNextShot;
        public int interceptionSearchRadius;
        public bool progressiveSearchMode;
        public bool progressiveSearchActive;
        public bool progressiveSearchExhausted;
        public int progressiveSearchRadius;
        public IntVec3 progressiveSearchOrigin = IntVec3.Invalid;
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
        public bool progressiveSearchJustExhausted;
        private int lastTimerTick = -1;

        public bool HasPlan => plannedTarget != null;
        public bool IsWarming => warmupTicksRemaining > 0;
        public bool Active => weapon != null
            && (cooldownTicksRemaining > 0
            || warmupTicksRemaining > 0
            || openingWarmupPending
             || vanillaOpeningPending
             || cachedCandidateTarget != null
             || HasAutomaticCandidates
            || focusedTarget != null
            || HasPlan
            || progressiveSearchActive
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
                || HasPlan
                || progressiveSearchMode
                || progressiveSearchActive);

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
            Scribe_Values.Look(ref postShotCacheAttempted, "postShotCacheAttempted");
            Scribe_References.Look(ref lastFiredTarget, "lastFiredTarget");
            Scribe_References.Look(ref vanillaOpeningTarget, "vanillaOpeningTarget");
            Scribe_References.Look(ref vanillaFollowupTarget, "vanillaFollowupTarget");
            Scribe_Values.Look(ref vanillaOpeningPending, "vanillaOpeningPending");
            Scribe_Values.Look(
                ref vanillaOpeningSingleShotClaimed,
                "vanillaOpeningSingleShotClaimed");
            Scribe_Values.Look(
                ref vanillaOpeningFirstShotSearchTriggered,
                "vanillaOpeningFirstShotSearchTriggered");
            Scribe_Values.Look(ref vanillaOpeningCloseContext, "vanillaOpeningCloseContext");
            Scribe_Values.Look(ref vanillaOpeningTargetCell, "vanillaOpeningTargetCell", IntVec3.Invalid);
            Scribe_Values.Look(ref firedInCurrentOpening, "firedInCurrentOpening");
            Scribe_Values.Look(
                ref cooldownFromVanillaOpening,
                "cooldownFromVanillaOpening");
            Scribe_Values.Look(ref skipNextWarmup, "skipNextWarmup");
            Scribe_Values.Look(ref candidateRetryTicks, "candidateRetryTicks");
            Scribe_Values.Look(ref burstShotsRemaining, "burstShotsRemaining");
            Scribe_Values.Look(ref burstTicksUntilNextShot, "burstTicksUntilNextShot");
            Scribe_Values.Look(ref interceptionSearchRadius, "interceptionSearchRadius");
            Scribe_Values.Look(ref progressiveSearchMode, "progressiveSearchMode");
            Scribe_Values.Look(ref progressiveSearchActive, "progressiveSearchActive");
            Scribe_Values.Look(ref progressiveSearchExhausted, "progressiveSearchExhausted");
            Scribe_Values.Look(ref progressiveSearchRadius, "progressiveSearchRadius");
            Scribe_Values.Look(ref progressiveSearchOrigin, "progressiveSearchOrigin", IntVec3.Invalid);
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
                skipNextWarmup = false;
                cooldownTicksRemaining = Mathf.Max(0, cooldownTicksRemaining);
                pendingCandidateLimitOverride = Mathf.Max(
                    0,
                    pendingCandidateLimitOverride);
                activeCandidateLimitOverride = Mathf.Max(
                    0,
                    activeCandidateLimitOverride);
                StopProgressiveSearch();
                if (HasPlan && warmupTicksRemaining <= 0)
                {
                    warmupTicksRemaining = 1;
                    warmupTotalTicks = Mathf.Max(1, warmupTotalTicks);
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
                && !openingWarmupPending
                && !vanillaOpeningPending)
            {
                cooldownFromVanillaOpening = false;
            }

            if (openingWarmupPending
                && warmupTicksRemaining > 0)
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
            postShotCacheAttempted = false;
            lastFiredTarget = null;
            ClearVanillaOpening();
            firedInCurrentOpening = false;
            cooldownFromVanillaOpening = false;
            skipNextWarmup = false;

            candidateRetryTicks = 0;

            visualTarget = null;
            visualAimTicksRemaining = 0;
            interceptionSearchRadius = 0;
            lastTimerTick = -1;

            StopProgressiveSearch();
            ClearPlan();
        }

        public void ShiftProgressiveSearch(IntVec3 offset, IntVec3 fallbackCenter)
        {
            if (!progressiveSearchMode && !progressiveSearchActive)
            {
                return;
            }

            progressiveSearchOrigin = progressiveSearchOrigin.IsValid
                ? progressiveSearchOrigin + offset
                : fallbackCenter;
        }

        public void ClearVanillaOpening()
        {
            vanillaOpeningTarget = null;
            vanillaFollowupTarget = null;
            vanillaOpeningPending = false;
            vanillaOpeningSingleShotClaimed = false;
            vanillaOpeningFirstShotSearchTriggered = false;
            vanillaOpeningCloseContext = false;
            vanillaOpeningTargetCell = IntVec3.Invalid;
        }

        public bool BeginProgressiveSearch(IntVec3 origin)
        {
            if (progressiveSearchMode
                || progressiveSearchActive
                || focusedTarget != null
                || cachedCandidateTarget != null
                || HasPlan
                || openingWarmupPending
                || vanillaOpeningPending
                || burstShotsRemaining > 0)
            {
                return false;
            }

            progressiveSearchMode = true;
            progressiveSearchActive = true;
            progressiveSearchExhausted = false;
            progressiveSearchJustExhausted = false;
            progressiveSearchRadius = 0;
            progressiveSearchOrigin = origin;
            candidateRetryTicks = 1;
            return true;
        }

        public void ExhaustProgressiveSearch(IntVec3 origin)
        {
            progressiveSearchMode = false;
            progressiveSearchActive = false;
            progressiveSearchExhausted = true;
            progressiveSearchJustExhausted = true;
            progressiveSearchRadius = 0;
            progressiveSearchOrigin = origin;
            candidateRetryTicks = 0;
        }

        public void RearmTargetSearch()
        {
            progressiveSearchExhausted = false;
            progressiveSearchOrigin = IntVec3.Invalid;
            candidateRetryTicks = 0;
        }

        public void StopProgressiveSearch()
        {
            progressiveSearchMode = false;
            progressiveSearchActive = false;
            progressiveSearchExhausted = false;
            progressiveSearchJustExhausted = false;
            progressiveSearchRadius = 0;
            progressiveSearchOrigin = IntVec3.Invalid;
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
    }

    public static class RimKataDualWeaponController
    {
        private const int CandidateRetryTicks = 3;
        [ThreadStatic] private static Pawn activePhysicalMeleePawn;
        [ThreadStatic] private static RimKataWeaponCycleState activePhysicalMeleeCycle;

        public static void Tick(
            Pawn pawn,
            Thing assignedTarget,
            bool playerForced,
            bool killIncappedTarget,
            bool closeCombatContext,
            bool progressiveSearchOnly = false,
            bool closeTargetResolved = false)
        {
            if (pawn?.Map == null || !RimKataEligibility.CanBeginGunKataAttack(pawn))
            {
                Reset(pawn, true);
                return;
            }

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

            if (state.idleProjectileSearchTriggerPending)
            {
                RimKataSharedTargetSearch.Begin(
                    pawn,
                    state,
                    pawn.Position,
                    true);
                state.ConsumeIdleProjectileSearchTrigger();
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

            if (state.VanillaOpeningPending && closeCombatContext)
            {
                RimKataWeaponCycleState openingCycle = OpeningCycle(state);
                if (openingCycle?.vanillaOpeningTarget != closeTarget
                    || !openingCycle.vanillaOpeningCloseContext)
                {
                    Verb openingVerb = RimKataWeaponSlotUtility.CombatVerb(
                        pawn,
                        openingCycle?.weapon);
                    CancelOpeningOwner(pawn, state, openingCycle);
                    if (pawn.stances?.curStance is Stance_Warmup warmup
                        && warmup.verb == openingVerb)
                    {
                        pawn.stances.SetStance(new Stance_Mobile());
                    }
                }
            }

            if (state.VanillaOpeningPending)
            {
                if (loadoutChanged)
                {
                    CancelOpeningSession(state);
                    return;
                }

                state.dualLastDrivenTick = currentTick;
                TickVanillaOpening(
                    pawn,
                    state,
                    playerForced,
                    killIncappedTarget);
                return;
            }

            RimKataSharedTargetSearch.Prune(
                pawn,
                state);

            if (!closeCombatContext
                && state.AutomaticAttackRequestActive
                && (assignedTarget == null
                    || assignedTarget.Destroyed
                    || !assignedTarget.Spawned))
            {
                assignedTarget = state.automaticAttackRequestTarget;
            }
            else if (!closeCombatContext
                && state.IncomingThreatActive
                && (assignedTarget == null
                    || assignedTarget.Destroyed
                    || !assignedTarget.Spawned))
            {
                assignedTarget = state.incomingThreatSource;
            }

            if (state.sharedTargetSearch?.scanActive == true)
            {
                AdvanceOpeningRingSearch(pawn, state, assignedTarget);
                RefreshDualEngagementState(pawn, state);
                if (state.dualEngagementActive)
                {
                    if (pawn.Drafted)
                    {
                        state.draftedFireActive = true;
                    }
                }
            }

            if (!state.dualEngagementActive)
            {
                UpdateBodyAimStance(pawn, state);
                return;
            }
            if (!HasEngagementContinuity(pawn, state))
            {
                state.dualEngagementActive = false;
                state.ResetCandidateSaturationExpansion(true);
                state.ClearCounterattackRimKataSession();
                return;
            }

            state.primaryWeaponCycle.progressiveSearchJustExhausted = false;
            state.secondaryWeaponCycle.progressiveSearchJustExhausted = false;
            bool searchShouldRearm = state.dualLastDrivenTick < 0 || currentTick - state.dualLastDrivenTick > 30;
            state.dualLastDrivenTick = currentTick;
            if (searchShouldRearm || loadoutChanged)
            {
                state.primaryWeaponCycle.RearmTargetSearch();
                state.secondaryWeaponCycle.RearmTargetSearch();
            }

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
            Verb primaryVerb = RimKataWeaponSlotUtility.CombatVerbForContext(
                pawn,
                state.primaryWeaponCycle.weapon,
                closeCombatContext);

            bool primarySearchOwner = primaryVerb != null && !primaryVerb.IsMeleeAttack;
            bool primaryProgressiveSearch = progressiveSearchOnly && primarySearchOwner;
            bool secondaryProgressiveSearch = progressiveSearchOnly && !primarySearchOwner;

            PrepareCycle(pawn, state, state.primaryWeaponCycle, assignedTarget, playerForced, killIncappedTarget, closeCombatContext, blockedByStance, primaryProgressiveSearch);
            PrepareCycle(pawn, state, state.secondaryWeaponCycle, assignedTarget, playerForced, killIncappedTarget, closeCombatContext, blockedByStance, secondaryProgressiveSearch);
            if (TryPromotePreparedCounterattackJobTarget(
                pawn,
                state,
                out Thing promotedCounterattackTarget))
            {
                assignedTarget = promotedCounterattackTarget;
            }
            RefreshDualEngagementState(pawn, state);

            if (!blockedByStance && ReadyToAct(state.primaryWeaponCycle))
            {
                ExecuteCycle(pawn, state.primaryWeaponCycle, assignedTarget, playerForced, killIncappedTarget, closeCombatContext);
            }

            if (!blockedByStance && ReadyToAct(state.secondaryWeaponCycle))
            {
                ExecuteCycle(pawn, state.secondaryWeaponCycle, assignedTarget, playerForced, killIncappedTarget, closeCombatContext);
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

            combatActive = state?.dualEngagementActive == true
                && !state.VanillaOpeningPending;

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
            vanillaOpeningState = state?.VanillaOpeningPending == true
                || cycle?.openingWarmupPending == true
                || (cycle?.cooldownFromVanillaOpening == true
                    && (cycle.cooldownTicksRemaining > 0
                        || cycle.burstShotsRemaining > 0));
        }

        public static bool DebugProgressiveSearchActive(Pawn pawn)
        {
            RimKataPawnCombatState state = StateFor(pawn, false);

            return state?.sharedTargetSearch?.scanActive == true
                ;
        }

        public static bool NotifyPlayerWeaponTarget(
            Pawn pawn,
            Verb verb,
            Thing target,
            bool fromAttackGizmo = false)
        {
            if (pawn?.Map == null
                || verb == null
                || verb.IsMeleeAttack
                || target == null
                || target.Destroyed
                || !target.Spawned
                || target.Map != pawn.Map
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
            if (weapon == null
                || (weapon != primary && weapon != secondary)
                || verb.CasterPawn != pawn)
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

            bool changedTarget = cycle.focusedTarget != target
                || cycle.focusedTargetFromAttackGizmo != fromAttackGizmo;
            cycle.focusedTarget = target;
            cycle.focusedTargetFromAttackGizmo = fromAttackGizmo;
            cycle.cachedCandidateTarget = null;
            cycle.cachedCandidateInterception = false;
            cycle.postShotCacheAttempted = false;
            cycle.StopProgressiveSearch();
            if (changedTarget)
            {
                cycle.ClearPlan();
                cycle.warmupTicksRemaining = -1;
                cycle.warmupTotalTicks = 0;
            }

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
                        || targetPawn.Downed
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
            if (!CanOrderRangedCloseAttack(pawn, verb, target)
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
            cycle.StopProgressiveSearch();
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

                if (MovementSearchBlockedByCloseCombat(pawn, state))
                {
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

            if (MovementSearchBlockedByCloseCombat(pawn, state))
            {
                return false;
            }

            return TryBeginMovementSearch(pawn, state, currentCell);
        }

        public static void QueuePlayerMovementSearch(Pawn pawn)
        {
            if (pawn?.Map == null
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
            state.QueueIdleProjectileSearchTrigger();
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
                || pawn.Drafted != true
                || pawn.drafter?.FireAtWill != true
                || pawn.pather?.MovingNow == true
                || !pawn.IsPlayerControlled
                || !RimKataEligibility.CanBeginGunKataAttack(pawn))
            {
                return false;
            }

            JobDef jobDef = pawn.CurJobDef;
            if (jobDef != JobDefOf.Wait
                && jobDef != JobDefOf.Wait_Combat
                && jobDef?.defName != "Wait_MaintainPosture")
            {
                return false;
            }

            RimKataPawnCombatState state = StateFor(pawn, false);
            if (state != null
                && (state.dualEngagementActive
                    || state.VanillaOpeningPending
                    || state.sharedTargetSearch?.KeepsCombatAlive == true
                    || state.DraftedMovementSearchTriggerPending
                    || state.idleProjectileSearchTriggerPending
                    || state.MovementFireContinuityActive
                    || state.IncomingThreatActive
                    || state.AutomaticAttackRequestActive
                    || state.CloseAttackRequestActive
                    || state.primaryWeaponCycle?.DedicatedActive == true
                    || state.secondaryWeaponCycle?.DedicatedActive == true
                    || state.primaryWeaponCycle?.focusedTarget != null
                    || state.secondaryWeaponCycle?.focusedTarget != null))
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

        private static bool MovementSearchBlockedByCloseCombat(
            Pawn pawn,
            RimKataPawnCombatState state)
        {
            return false;
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
            state.dualLastDrivenTick = Find.TickManager?.TicksGame ?? -1;
            return true;
        }

        private static bool CanOwnMovementSearch(
            Pawn pawn,
            RimKataWeaponCycleState cycle)
        {
            Verb verb = RimKataWeaponSlotUtility.CombatVerb(
                pawn,
                cycle?.weapon);
            return cycle != null
                && verb != null
                && !verb.IsMeleeAttack
                && cycle.focusedTarget == null
                && cycle.cachedCandidateTarget == null
                && !cycle.HasPlan
                && !cycle.openingWarmupPending
                && !cycle.vanillaOpeningPending
                && cycle.burstShotsRemaining <= 0;
        }

        public static void ClearDraftedMovementTracking(Pawn pawn)
        {
            StateFor(pawn, false)?.ClearDraftedMovementSearchTracking();
        }

        private static bool MovingFireEnabledForPawn(Pawn pawn)
        {
            return RimKataMod.Settings?.movingFireEnabled != false;
        }

        public static bool ShouldSuppressVanillaCast(
            Pawn pawn,
            Verb verb,
            LocalTargetInfo target)
        {
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

            if (!RimKataEligibility.RandomAttackEnabledForPawn(pawn)
                && IsConfigurableCounterattackJob(pawn, pawn.CurJob))
            {
                return false;
            }

            RimKataPawnCombatState state = StateFor(pawn, false);
            RimKataWeaponCycleState cycle = CycleForWeapon(state, weapon);
            if (cycle?.cooldownTicksRemaining > 0)
            {
                return true;
            }

            bool pendingCounterattackOpening =
                IsPendingCounterattackOpening(
                    pawn,
                    state,
                    target.Thing);
            if (IsDedicatedFollowupActive(pawn)
                && !pendingCounterattackOpening)
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
                || state?.VanillaOpeningPending == true
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
            cycle.skipNextWarmup = false;

            cycle.openingWarmupBonusTicks = 0;
            cycle.openingWarmupPending = false;

            cycle.candidateRetryTicks = 0;
            cycle.interceptionSearchRadius = 0;

            cycle.StopProgressiveSearch();
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

            cycle.postShotCacheAttempted = true;
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
                || verb == null
                || !castTarget.IsValid
                || !castTarget.HasThing
                || RimKataFireContext.ActiveVerb != null
                || !RimKataEligibility.CanBeginGunKataAttack(pawn))
            {
                return attempt;
            }

            if (!RimKataEligibility.RandomAttackEnabledForPawn(pawn)
                && IsConfigurableCounterattackJob(pawn, pawn.CurJob))
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

            RimKataPawnCombatState state = StateFor(pawn, true);
            BindCurrentWeapons(pawn, state);
            if (IsDedicatedFollowupActive(pawn)
                && !IsPendingCounterattackOpening(
                    pawn,
                    state,
                    castTarget.Thing))
            {
                return attempt;
            }

            RimKataWeaponCycleState openingCycle = CycleForWeapon(state, firedWeapon);
            RimKataWeaponCycleState supportCycle = OtherCycle(state, openingCycle);
            Verb supportVerb = RimKataWeaponSlotUtility.CombatVerb(pawn, supportCycle?.weapon);
            if (openingCycle == null)
            {
                return attempt;
            }

            attempt.prepared = true;
            attempt.weapon = firedWeapon;

            Thing currentTarget = castTarget.Thing;
            bool playerForced = pawn.CurJob?.playerForced == true;
            bool killIncappedTarget = pawn.CurJob?.killIncappedTarget == true;
            bool closeContext = verb.IsMeleeAttack
                || pawn.CanReachImmediate(currentTarget, PathEndMode.Touch);
            if (!ValidOpeningTarget(
                pawn,
                currentTarget,
                playerForced,
                killIncappedTarget,
                closeContext))
            {
                attempt.prepared = false;
                return attempt;
            }

            ResetCycleForNewOpening(openingCycle);
            ResetCycleForNewOpening(supportCycle);
            state.engagementOwnerWeapon = null;
            state.ClearOpeningRingSearch();
            state.ClearRushAuthority();

            if (RimKataMod.Settings?.targetRushEnabled != false
                && pawn.Drafted != true
                && state.IsPendingCounterattack(currentTarget)
                && TargetWithinAutomaticSearchRange(pawn, currentTarget))
            {
                state.SetRushAuthority(RimKataRushAuthority.Counterattack, currentTarget);
                state.pendingCounterattackTarget = null;
                state.pendingCounterattackTicksRemaining = 0;
            }

            openingCycle.vanillaOpeningTarget = currentTarget;
            openingCycle.vanillaFollowupTarget = null;
            openingCycle.vanillaOpeningPending = true;
            openingCycle.vanillaOpeningCloseContext = closeContext;
            openingCycle.vanillaOpeningTargetCell = currentTarget.Position;
            state.engagementOwnerWeapon = firedWeapon;

            TryStartOpeningRingSearch(pawn, state);
            RimKataSharedTargetSearch.TryAddKnownAutomaticTarget(
                pawn,
                state,
                currentTarget);

            TryArmOpeningSupport(
                pawn,
                state,
                openingCycle,
                supportCycle,
                supportVerb,
                currentTarget,
                playerForced,
                killIncappedTarget,
                closeContext,
                PredictedOpeningBonusSourceTicks(pawn, verb));
            RefreshDualEngagementState(pawn, state);
            state.dualLastDrivenTick = Find.TickManager.TicksGame;
            return attempt;
        }

        public static void CommitVanillaOpening(Pawn pawn, Verb verb)
        {
            RimKataPawnCombatState state = StateFor(pawn, false);
            RimKataWeaponCycleState openingCycle = CycleForWeapon(
                state,
                verb?.EquipmentSource as ThingWithComps);
            if (openingCycle?.vanillaOpeningPending != true
                || !(pawn?.stances?.curStance is Stance_Warmup warmup)
                || warmup.verb != verb
                || !warmup.focusTarg.HasThing
                || openingCycle.vanillaOpeningTarget != warmup.focusTarg.Thing)
            {
                return;
            }

            RefreshDualEngagementState(pawn, state);
            state.dualLastDrivenTick = Find.TickManager.TicksGame;
            if (state.rushAuthority != RimKataRushAuthority.Counterattack
                || state.rushAuthorityTarget != openingCycle.vanillaOpeningTarget)
            {
                return;
            }

            Thing target = openingCycle.vanillaOpeningTarget;
            bool closeContext = openingCycle.vanillaOpeningCloseContext;
            openingCycle.ClearVanillaOpening();
            SetCandidate(
                openingCycle,
                target,
                false,
                closeContext,
                closeContext,
                true);
            openingCycle.warmupTotalTicks = Mathf.Max(0, warmup.ticksLeft);
            openingCycle.warmupTicksRemaining = openingCycle.warmupTotalTicks;
            pawn.stances.SetStance(new Stance_Mobile());
            QueueDedicatedFollowupJob(pawn, target);
        }

        public static void NotifyVanillaOpeningTargetCell(
            Pawn pawn,
            Verb verb,
            Thing target)
        {
            RimKataPawnCombatState state = StateFor(pawn, false);
            RimKataWeaponCycleState openingCycle = CycleForWeapon(
                state,
                verb?.EquipmentSource as ThingWithComps);
            if (openingCycle?.vanillaOpeningPending != true
                || target == null
                || target.Destroyed
                || !target.Spawned
                || openingCycle.vanillaOpeningTarget != target
                || openingCycle.vanillaOpeningTargetCell == target.Position)
            {
                return;
            }

            openingCycle.vanillaOpeningTargetCell = target.Position;
            Verb openingVerb = RimKataWeaponSlotUtility.CombatVerb(
                pawn,
                openingCycle.weapon);
            openingCycle.vanillaOpeningCloseContext = openingVerb?.IsMeleeAttack == true
                || pawn.CanReachImmediate(target, PathEndMode.Touch);
            TryStartOpeningRingSearch(pawn, state);
            RevalidateUnfiredOpeningSupport(
                pawn,
                state,
                openingCycle,
                OtherCycle(state, openingCycle),
                target,
                pawn.CurJob?.playerForced == true,
                pawn.CurJob?.killIncappedTarget == true);
        }

        public static void NotifyVanillaOpeningFirstShot(
            Pawn pawn,
            Verb verb)
        {
            RimKataPawnCombatState state = StateFor(pawn, false);
            RimKataWeaponCycleState openingCycle = CycleForWeapon(
                state,
                verb?.EquipmentSource as ThingWithComps);
            if (openingCycle?.vanillaOpeningPending != true
                || openingCycle.vanillaOpeningFirstShotSearchTriggered)
            {
                return;
            }

            openingCycle.vanillaOpeningFirstShotSearchTriggered = true;
            TryStartOpeningRingSearch(pawn, state);
        }

        private static void TickVanillaOpening(
            Pawn pawn,
            RimKataPawnCombatState state,
            bool playerForced,
            bool killIncappedTarget)
        {
            RimKataWeaponCycleState openingCycle = OpeningCycle(state);
            RimKataWeaponCycleState supportCycle = OtherCycle(state, openingCycle);
            Thing target = openingCycle?.vanillaOpeningTarget;
            if (openingCycle == null
                || supportCycle == null
                || !ValidOpeningTarget(
                    pawn,
                    target,
                    playerForced,
                    killIncappedTarget,
                    openingCycle.vanillaOpeningCloseContext))
            {
                CancelOpeningOwner(pawn, state, openingCycle);
                return;
            }

            if (openingCycle.vanillaOpeningTargetCell != target.Position)
            {
                openingCycle.vanillaOpeningTargetCell = target.Position;
                Verb openingVerb = RimKataWeaponSlotUtility.CombatVerb(
                    pawn,
                    openingCycle.weapon);
                openingCycle.vanillaOpeningCloseContext = openingVerb?.IsMeleeAttack == true
                    || pawn.CanReachImmediate(target, PathEndMode.Touch);
                TryStartOpeningRingSearch(pawn, state);
                RevalidateUnfiredOpeningSupport(
                    pawn,
                    state,
                    openingCycle,
                    supportCycle,
                    target,
                    playerForced,
                    killIncappedTarget);
            }

            AdvanceOpeningRingSearch(pawn, state, target);
            RefreshDualEngagementState(pawn, state);
            supportCycle.TickTimers();
            if (!supportCycle.firedInCurrentOpening
                && supportCycle.openingWarmupPending)
            {
                Verb supportVerb = RimKataWeaponSlotUtility.CombatVerb(
                    pawn,
                    supportCycle.weapon);
                if (!supportCycle.openingWarmupPending
                    || !supportCycle.HasPlan
                    || !ValidCurrentTargetForVerb(
                        pawn,
                        supportVerb,
                        target,
                        playerForced,
                        killIncappedTarget,
                        openingCycle.vanillaOpeningCloseContext))
                {
                    if (supportCycle.openingWarmupPending || supportCycle.HasPlan)
                    {
                        ResetCycleForNewOpening(supportCycle);
                    }
                    return;
                }
            }

            bool closeContext = openingCycle.vanillaOpeningCloseContext;
            Thing supportAssignedTarget = supportCycle.openingWarmupPending
                ? target
                : null;
            bool viable = PrepareCycle(
                pawn,
                state,
                supportCycle,
                supportAssignedTarget,
                playerForced && supportAssignedTarget != null,
                killIncappedTarget,
                closeContext,
                false,
                false);
            bool openingFiresThisTick =
                pawn.stances?.curStance is Stance_Warmup openingWarmup
                && openingWarmup.verb == RimKataWeaponSlotUtility.CombatVerb(
                    pawn,
                    openingCycle.weapon)
                && openingWarmup.ticksLeft <= 1;
            if (viable
                && ReadyToAct(supportCycle)
                && !openingFiresThisTick)
            {
                ExecuteCycle(
                    pawn,
                    supportCycle,
                    supportAssignedTarget,
                    playerForced && supportAssignedTarget != null,
                    killIncappedTarget,
                    closeContext);
            }
        }

        private static void RevalidateUnfiredOpeningSupport(
            Pawn pawn,
            RimKataPawnCombatState state,
            RimKataWeaponCycleState openingCycle,
            RimKataWeaponCycleState supportCycle,
            Thing target,
            bool playerForced,
            bool killIncappedTarget)
        {
            if (supportCycle?.firedInCurrentOpening == true)
            {
                return;
            }

            Verb supportVerb = RimKataWeaponSlotUtility.CombatVerb(
                pawn,
                supportCycle?.weapon);
            if (supportCycle?.HasPlan == true
                && supportCycle.plannedCloseContext
                    != openingCycle.vanillaOpeningCloseContext)
            {
                ResetCycleForNewOpening(supportCycle);
            }

            if (!ValidCurrentTargetForVerb(
                pawn,
                supportVerb,
                target,
                playerForced,
                killIncappedTarget,
                openingCycle.vanillaOpeningCloseContext))
            {
                ResetCycleForNewOpening(supportCycle);
                return;
            }

            if (!supportCycle.openingWarmupPending || !supportCycle.HasPlan)
            {
                TryArmOpeningSupport(
                    pawn,
                    state,
                    openingCycle,
                    supportCycle,
                    supportVerb,
                    target,
                    playerForced,
                    killIncappedTarget,
                    openingCycle.vanillaOpeningCloseContext,
                    PredictedOpeningBonusSourceTicks(
                        pawn,
                        RimKataWeaponSlotUtility.CombatVerb(
                            pawn,
                            openingCycle.weapon)));
            }
        }

        private static bool TryArmOpeningSupport(
            Pawn pawn,
            RimKataPawnCombatState state,
            RimKataWeaponCycleState openingCycle,
            RimKataWeaponCycleState supportCycle,
            Verb supportVerb,
            Thing target,
            bool playerForced,
            bool killIncappedTarget,
            bool closeContext,
            int openingBonusSourceTicks)
        {
            if (supportCycle == null
                || supportVerb == null
                || supportCycle.firedInCurrentOpening
                || supportCycle.openingSupportDelayConsumed
                || !ValidCurrentTargetForVerb(
                    pawn,
                    supportVerb,
                    target,
                    playerForced,
                    killIncappedTarget,
                    closeContext)
                || (!closeContext
                    && !RimKataSharedTargetSearch.IsValidForVerb(
                        pawn,
                        supportVerb,
                        target)))
            {
                return false;
            }

            SetCandidate(
                supportCycle,
                target,
                false,
                closeContext,
                closeContext,
                true);
            int openingBonus = Mathf.Max(
                0,
                Mathf.CeilToInt(openingBonusSourceTicks * 0.5f));
            int normalWarmup = RimKataCombatMath.WarmupTicksForSingleShot(
                supportVerb);
            supportCycle.openingWarmupBonusTicks = openingBonus;
            supportCycle.openingWarmupPending = true;
            supportCycle.openingSupportDelayConsumed = true;
            supportCycle.warmupTotalTicks = normalWarmup + openingBonus;
            supportCycle.warmupTicksRemaining = supportCycle.warmupTotalTicks;
            supportCycle.visualTarget = target;
            return true;
        }

        private static int PredictedOpeningBonusSourceTicks(Pawn pawn, Verb verb)
        {
            return Mathf.Max(
                1,
                RimKataCombatMath.CooldownTicksForSingleShot(
                    verb,
                    pawn,
                    false));
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

        private static bool IsPendingCounterattackOpening(
            Pawn pawn,
            RimKataPawnCombatState state,
            Thing target)
        {
            Job job = pawn?.CurJob;
            return pawn?.Drafted != true
                && target != null
                && state?.IsPendingCounterattack(target) == true
                && job?.targetA.Thing == target
                && (job.def == JobDefOf.AttackStatic
                    || job.def == JobDefOf.AttackMelee);
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

            RimKataPawnCombatState state = StateFor(pawn, false);
            RimKataWeaponCycleState cycle = CycleForWeapon(
                state,
                weapon);
            LocalTargetInfo target = verb.CurrentTarget;
            if ((!target.IsValid || !target.HasThing)
                && pawn.stances?.curStance is Stance_Warmup activeWarmup
                && activeWarmup.verb == verb)
            {
                target = activeWarmup.focusTarg;
            }
            if (cycle?.vanillaOpeningPending != true
                || cycle.vanillaOpeningSingleShotClaimed
                || cycle.weapon != weapon
                || !target.IsValid
                || !target.HasThing
                || cycle.vanillaOpeningTarget != target.Thing
                || target.Thing.Destroyed
                || !target.Thing.Spawned
                || target.Thing.Map != pawn.Map)
            {
                return false;
            }

            bool matchingWarmup = !(pawn.stances?.curStance is Stance_Warmup warmup)
                || (warmup.verb == verb
                    && warmup.focusTarg.HasThing
                    && warmup.focusTarg.Thing == target.Thing);
            if (matchingWarmup)
            {
                cycle.vanillaOpeningSingleShotClaimed = true;
            }

            return matchingWarmup;
        }

        private static void TryStartOpeningRingSearch(
            Pawn pawn,
            RimKataPawnCombatState state)
        {
            if (pawn?.Map == null
                || state == null)
            {
                return;
            }

            if (RimKataSharedTargetSearch.Begin(
                pawn,
                state,
                pawn.Position))
            {
                state.openingRingSearchActive =
                    state.sharedTargetSearch.scanActive;
                state.openingRingSearchRadius = Mathf.CeilToInt(
                    state.sharedTargetSearch.completedOuterRadius);
                state.openingRingSearchOrigin =
                    state.sharedTargetSearch.origin;
            }
        }

        private static void AdvanceOpeningRingSearch(
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
            state.openingRingSearchActive = state.sharedTargetSearch.scanActive;
            state.openingRingSearchRadius = Mathf.CeilToInt(
                state.sharedTargetSearch.completedOuterRadius);
            state.openingRingSearchOrigin = state.sharedTargetSearch.origin;

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
            if (RimKataSharedTargetSearch
                .CompleteIdleProjectilePriorityPass(pawn, state))
            {
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
        }

        private static bool TryCacheSharedCandidate(
            Pawn pawn,
            RimKataPawnCombatState state,
            RimKataWeaponCycleState cycle,
            Thing preferredTarget)
        {
            if (pawn?.Map == null
                || state?.sharedTargetSearch?.sessionActive != true
                || cycle == null
                || cycle.focusedTarget != null
                || cycle.cachedCandidateTarget != null
                || cycle.HasPlan
                || cycle.openingWarmupPending
                || cycle.vanillaOpeningPending
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
            cycle.StopProgressiveSearch();
            state.openingRingSearchCandidate = candidate;
            return true;
        }

        private static bool BeginProgressiveSearch(
            Pawn pawn,
            RimKataWeaponCycleState cycle,
            IntVec3 origin)
        {
            RimKataPawnCombatState state = StateFor(pawn, false);
            return pawn?.Map != null
                && cycle != null
                && state != null
                && RimKataSharedTargetSearch.Begin(
                    pawn,
                    state,
                    origin);
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

        public static void RestoreVanillaOpening(
            Pawn pawn,
            RimKataVanillaOpeningAttempt attempt)
        {
            RimKataPawnCombatState state = StateFor(pawn, false);
            RimKataWeaponCycleState openingCycle = CycleForWeapon(state, attempt.weapon);
            if (!attempt.prepared
                || openingCycle?.vanillaOpeningPending != true)
            {
                return;
            }

            CancelOpeningSession(state);
        }

        public static void CancelVanillaOpening(
            Pawn pawn,
            Verb verb,
            LocalTargetInfo castTarget)
        {
            RimKataPawnCombatState state = StateFor(pawn, false);
            RimKataWeaponCycleState openingCycle = CycleForWeapon(
                state,
                verb?.EquipmentSource as ThingWithComps);
            if (openingCycle?.vanillaOpeningPending == true
                && (!castTarget.HasThing || openingCycle.vanillaOpeningTarget == castTarget.Thing))
            {
                CancelOpeningOwner(pawn, state, openingCycle);
            }
        }

        public static bool TryBeginFromVanillaCooldown(
            Pawn pawn,
            Verb verb,
            Thing firedTarget,
            int vanillaCooldownTicks)
        {
            if (pawn?.Map == null
                || verb == null
                || firedTarget == null
                || RimKataFireContext.ActiveVerb != null)
            {
                return false;
            }

            ThingWithComps firedWeapon = verb.EquipmentSource as ThingWithComps;
            RimKataPawnCombatState state = StateFor(pawn, false);
            RimKataWeaponCycleState firedCycle = CycleForWeapon(state, firedWeapon);
            if (firedWeapon == null
                || firedCycle?.vanillaOpeningPending != true
                || firedCycle.vanillaOpeningTarget != firedTarget)
            {
                return false;
            }

            bool singleShotClaimed = firedCycle.vanillaOpeningSingleShotClaimed
                || RimKataVanillaSingleShotContext.ActiveFor(verb);
            if (verb.Bursting && !singleShotClaimed)
            {
                return false;
            }

            bool closeContext = firedCycle.vanillaOpeningCloseContext;
            bool playerForced = pawn.CurJob?.playerForced == true;
            bool killIncappedTarget = pawn.CurJob?.killIncappedTarget == true;
            RimKataWeaponCycleState supportCycle = OtherCycle(state, firedCycle);
            firedCycle.ClearVanillaOpening();

            int fullCooldown = Mathf.Max(1, vanillaCooldownTicks);
            if (RimKataVanillaSingleShotContext.TryGetOriginalBurstCount(
                verb,
                out int originalBurstCount))
            {
                fullCooldown = RimKataCombatMath.CooldownTicksForSingleShot(
                    verb,
                    pawn,
                    false,
                    originalBurstCount);
            }
            firedCycle.cooldownTicksRemaining = Mathf.Max(firedCycle.cooldownTicksRemaining, fullCooldown);
            firedCycle.cooldownFromVanillaOpening = true;
            firedCycle.StopProgressiveSearch();
            firedCycle.ClearPlan();
            firedCycle.firedInCurrentOpening = true;
            firedCycle.lastFiredTarget = firedTarget;
            RecordFirstFiredWeapon(state, firedCycle.weapon);
            firedCycle.visualTarget = firedTarget;
            firedCycle.visualAimTicksRemaining = Mathf.Max(RimKataCombatTuning.PostShotAimTicks, fullCooldown + 2);
            firedCycle.postShotCacheAttempted = false;
            bool cachedFollowup = CacheCandidateAfterAttack(
                pawn,
                firedCycle,
                verb,
                firedTarget,
                playerForced,
                killIncappedTarget,
                closeContext);
            if (!cachedFollowup
                && !verb.IsMeleeAttack
                && !closeContext
                && !state.openingRingSearchActive)
            {
                BeginProgressiveSearch(pawn, firedCycle, pawn.Position);
            }
            else if (cachedFollowup)
            {
                state.ClearOpeningRingSearch();
            }

            if (supportCycle?.openingWarmupPending == true
                && supportCycle.plannedTarget == firedTarget)
            {
                int actualOpeningBonus = Mathf.Max(
                    0,
                    Mathf.CeilToInt(fullCooldown * 0.5f));
                int bonusDelta = actualOpeningBonus
                    - supportCycle.openingWarmupBonusTicks;
                supportCycle.openingWarmupBonusTicks = actualOpeningBonus;
                supportCycle.warmupTotalTicks = Mathf.Max(
                    0,
                    supportCycle.warmupTotalTicks + bonusDelta);
                supportCycle.warmupTicksRemaining = Mathf.Max(
                    0,
                    supportCycle.warmupTicksRemaining + bonusDelta);
            }

            RimKataSharedTargetSearch.Prune(pawn, state);
            RefreshDualEngagementState(pawn, state);
            state.dualLastDrivenTick = Find.TickManager.TicksGame;
            return true;
        }

        public static void NotifyDefensiveCombatEvent(Pawn pawn, Thing attacker)
        {
            if (pawn?.Map == null
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

            RimKataSharedTargetSearch.Begin(
                pawn,
                state,
                pawn.Position);
            RimKataSharedTargetSearch.TryAddKnownAutomaticTarget(
                pawn,
                state,
                attacker);
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
                || target == null
                || target.Destroyed
                || !target.Spawned
                || target.Map != pawn.Map)
            {
                return false;
            }

            RimKataPawnCombatState state = StateFor(pawn, true);
            BindCurrentWeapons(pawn, state);
            RimKataSharedTargetSearch.Begin(
                pawn,
                state,
                pawn.Position);
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

        public static bool TryBeginRandomMeleeControl(
            Pawn pawn,
            Thing target)
        {
            if (pawn?.Map == null
                || target == null
                || target.Destroyed
                || !target.Spawned
                || target.Map != pawn.Map
                || pawn.CurJobDef != JobDefOf.AttackMelee
                || !RimKataEligibility.RandomAttackEnabledForPawn(pawn)
                || !RimKataTargeting.IsAutomaticEnemy(pawn, target)
                || !pawn.CanReachImmediate(target, PathEndMode.Touch)
                || !HasUsableWeapon(pawn, true))
            {
                return false;
            }

            RimKataPawnCombatState state = StateFor(pawn, true);
            BindCurrentWeapons(pawn, state);
            state.RequestCloseAttack(target);
            HandleCloseCombatTransition(
                pawn,
                state,
                true,
                target);
            if (!RegisterAutomaticTarget(pawn, target))
            {
                HandleCloseCombatTransition(
                    pawn,
                    state,
                    false,
                    null);
                return false;
            }

            QueueDedicatedFollowupJob(pawn, target);
            return true;
        }

        public static bool IsDedicatedFollowupActive(Pawn pawn)
        {
            RimKataPawnCombatState state = StateFor(pawn, false);
            return state?.dualEngagementActive == true
                && !state.VanillaOpeningPending
                && HasEngagementContinuity(pawn, state);
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

        public static bool IsVanillaOpeningActive(Pawn pawn)
        {
            return StateFor(pawn, false)?.VanillaOpeningPending == true;
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

            if (cycle.cachedCandidateTarget != null
                && RimKataSharedTargetSearch.IsValidForVerb(
                    pawn,
                    verb,
                    cycle.cachedCandidateTarget))
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
            bool activeScanLatch = state.dualEngagementActive
                && state.sharedTargetSearch?.scanActive == true;
            bool liveCloseTarget = state.dualCloseCombatActive
                && IsImmediateCloseTarget(
                    pawn,
                    state.dualCloseTarget,
                    pawn?.CurJob?.playerForced == true,
                    pawn?.CurJob?.killIncappedTarget == true);
            state.dualEngagementActive = HasAnyCycleTargetWork(pawn, state)
                || liveCloseTarget
                || state.RushAuthorized
                || activeScanLatch;
            if (wasActive && !state.dualEngagementActive)
            {
                state.ResetCandidateSaturationExpansion(true);
            }
            if (!state.dualEngagementActive
                && !state.VanillaOpeningPending)
            {
                state.ClearCounterattackRimKataSession();
            }
        }

        private static bool HasEngagementContinuity(
            Pawn pawn,
            RimKataPawnCombatState state)
        {
            if (state == null)
            {
                return false;
            }

            if (HasAnyCycleTargetWork(pawn, state)
                || state.RushAuthorized
                || state.sharedTargetSearch?.KeepsCombatAlive == true)
            {
                return true;
            }
            return false;
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
            if (!cycleWork
                && !RimKataWeaponSlotUtility.CanAttackTargetWithoutRushing(
                    pawn,
                    target))
            {
                return;
            }

            state.dedicatedContinuityTarget = target;
            state.dedicatedContinuityUntilTick =
                (Find.TickManager?.TicksGame ?? 0) + 3;
        }

        private static bool HasCurrentDedicatedTarget(
            Pawn pawn,
            RimKataPawnCombatState state)
        {
            Job job = pawn?.CurJob;
            Thing target = job?.targetA.Thing;
            if (job?.def != RimKataDefOf.RimKata_Attack
                || pawn.Map == null
                || target == null
                || target.Destroyed
                || !target.Spawned
                || target.Map != pawn.Map
                || (!job.playerForced
                    && !RimKataTargeting.IsAutomaticEnemy(pawn, target)))
            {
                return false;
            }

            if (target is Pawn targetPawn
                && (targetPawn.Dead
                    || targetPawn.IsPsychologicallyInvisible()
                    || (!job.playerForced && targetPawn.Crawling)
                    || (!(job.playerForced && job.killIncappedTarget)
                        && targetPawn.Downed)))
            {
                return false;
            }

            if (RimKataWeaponSlotUtility.CanAttackTargetWithoutRushing(
                    pawn,
                    target))
            {
                state.dedicatedContinuityTarget = target;
                state.dedicatedContinuityUntilTick =
                    (Find.TickManager?.TicksGame ?? 0) + 3;
                return true;
            }

            int currentTick = Find.TickManager?.TicksGame ?? -1;
            return currentTick >= 0
                && state.dedicatedContinuityTarget == target
                && currentTick <= state.dedicatedContinuityUntilTick;
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
            if (pawn?.Drafted == true)
            {
                state.ClearCounterattackForDraft();
            }
            else
            {
                state.ClearRushAuthority();
            }
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
            cycle.ClearVanillaOpening();
            cycle.focusedTarget = null;
            cycle.focusedTargetFromAttackGizmo = false;
        }

        public static void QueueDedicatedFollowupJob(Pawn pawn, Thing target)
        {
            RimKataPawnCombatState state = StateFor(pawn, false);
            if (pawn?.Map == null
                || state == null
                || state.dedicatedFollowupJobStartInProgress
                || IsProtectedPlayerForcedJob(pawn.CurJob)
                || !IsDedicatedFollowupActive(pawn))
            {
                return;
            }

            state.QueueDedicatedFollowupJob(target, pawn.CurJob);
        }

        public static void TryConsumePendingDedicatedFollowupJob(Pawn pawn)
        {
            RimKataPawnCombatState state = StateFor(pawn, false);
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
                return;
            }

            TryEnterDedicatedFollowupJob(
                pawn,
                target,
                playerForced,
                killIncappedTarget);
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
            TryEnterDedicatedFollowupJob(pawn, target, null, null);
        }

        private static void TryEnterDedicatedFollowupJob(
            Pawn pawn,
            Thing target,
            bool? playerForcedOverride,
            bool? killIncappedTargetOverride)
        {
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
                return;
            }

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
                        false,
                        true,
                        null,
                        JobTag.Misc,
                        false,
                        false,
                        null,
                        false,
                        true,
                        false);
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

        public static void MarkPendingCounterattack(Pawn pawn, Thing target)
        {
            if (pawn?.Map == null
                || pawn.Drafted
                || target == null
                || target.Destroyed
                || !target.Spawned
                || target.Map != pawn.Map
                || !RimKataTargeting.IsAutomaticEnemy(pawn, target)
                || (target is Pawn targetPawn
                    && (targetPawn.Dead
                        || targetPawn.Downed
                        || targetPawn.Crawling
                        || targetPawn.IsPsychologicallyInvisible())))
            {
                return;
            }

            StateFor(pawn, true)?.MarkPendingCounterattack(target);
        }

        public static void MarkStartedCounterattackJob(Pawn pawn, Job job)
        {
            if (RimKataEligibility.RandomAttackEnabledForPawn(pawn)
                && IsConfigurableCounterattackJob(pawn, job))
            {
                MarkPendingCounterattack(pawn, job.targetA.Thing);
            }
        }

        public static bool ConsumeCounterattackMote(Pawn pawn)
        {
            if (pawn?.Drafted == true
                || !RimKataEligibility.RandomAttackEnabledForPawn(pawn))
            {
                return false;
            }

            RimKataPawnCombatState state = StateFor(pawn, true);
            return state?.ConsumeCounterattackMote() == true;
        }

        public static bool TryRestoreCounterattackJobTarget(
            Pawn pawn,
            Job job,
            out Thing target)
        {
            target = null;
            RimKataPawnCombatState state = StateFor(pawn, false);
            if (pawn?.Map == null
                || pawn.Drafted
                || job?.def != RimKataDefOf.RimKata_Attack
                || job.playerForced
                || state?.counterattackRimKataSessionActive != true)
            {
                return false;
            }

            Thing previousJobTarget = state.counterattackJobTarget
                ?? job.targetA.Thing;
            Thing storedTarget = state.counterattackJobTarget;
            bool repairLoadedTarget =
                state.ConsumeCounterattackTargetLoadRepair();
            if (!IsValidCounterattackJobTarget(pawn, storedTarget))
            {
                storedTarget = job.targetA.Thing;
                if (!IsValidCounterattackJobTarget(pawn, storedTarget))
                {
                    storedTarget = state.rushAuthority ==
                            RimKataRushAuthority.Counterattack
                        ? state.rushAuthorityTarget
                        : null;
                }

                if (IsValidCounterattackJobTarget(pawn, storedTarget))
                {
                    state.counterattackJobTarget = storedTarget;
                }
            }

            if (!IsValidCounterattackJobTarget(pawn, storedTarget))
            {
                return false;
            }

            if (repairLoadedTarget
                && state.dualCloseCombatActive
                && !IsImmediateCloseTarget(
                    pawn,
                    storedTarget,
                    false,
                    false))
            {
                Thing closeTarget = ResolveCloseTarget(
                    pawn,
                    state,
                    storedTarget,
                    false,
                    false);
                if (closeTarget != null && closeTarget != storedTarget)
                {
                    storedTarget = closeTarget;
                    state.counterattackJobTarget = storedTarget;
                }
            }

            if (job.targetA.Thing != storedTarget)
            {
                job.targetA = storedTarget;
            }

            if (previousJobTarget != storedTarget)
            {
                MoteMaker.MakeColonistActionOverlay(
                    pawn,
                    ThingDefOf.Mote_ColonistAttacking);
            }

            if (CanAuthorizeCounterattackRush(pawn, storedTarget))
            {
                state.SetRushAuthority(
                    RimKataRushAuthority.Counterattack,
                    storedTarget);
            }
            else if (state.rushAuthority ==
                RimKataRushAuthority.Counterattack)
            {
                state.ClearRushAuthority();
            }

            target = storedTarget;
            return true;
        }

        public static bool TryAbsorbStartedCounterattackJob(
            Pawn pawn,
            Job sourceJob,
            ThinkNode jobGiver)
        {
            if ((!(jobGiver is JobGiver_ConfigurableHostilityResponse)
                    && !(jobGiver is JobGiver_ReactToCloseMeleeThreat)))
            {
                return false;
            }

            Job currentJob = pawn?.CurJob;
            if (IsProtectedPlayerForcedJob(currentJob)
                && IsAutomaticCounterattackJob(pawn, sourceJob))
            {
                return AbsorbAutomaticThreatIntoProtectedJob(
                    pawn,
                    sourceJob.targetA.Thing);
            }

            if (!RimKataEligibility.RandomAttackEnabledForPawn(pawn)
                || !IsConfigurableCounterattackJob(pawn, sourceJob))
            {
                return false;
            }

            RimKataPawnCombatState state = StateFor(pawn, false);
            if (state?.counterattackRimKataSessionActive != true
                || currentJob?.def != RimKataDefOf.RimKata_Attack
                || currentJob.playerForced
                || !(pawn.jobs?.curDriver is JobDriver_RimKataAttack driver)
                || !driver.CanAbsorbCounterattackJob)
            {
                return false;
            }

            bool liveJobTarget = TryRestoreCounterattackJobTarget(
                pawn,
                currentJob,
                out _);
            if (!liveJobTarget && !HasEngagementContinuity(pawn, state))
            {
                return false;
            }

            Thing target = sourceJob.targetA.Thing;
            BindCurrentWeapons(pawn, state);
            TryStartOpeningRingSearch(pawn, state);
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
                || !IsAutomaticCounterattackJob(pawn, sourceJob))
            {
                return false;
            }

            Thing target = sourceJob.targetA.Thing;
            if (!(target is Pawn)
                || !pawn.Position.AdjacentTo8WayOrInside(target.Position))
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

            TryStartOpeningRingSearch(pawn, state);
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

        private static bool IsAutomaticCounterattackJob(Pawn pawn, Job job)
        {
            Thing target = job?.targetA.Thing;
            return pawn?.Map != null
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

        public static bool IsCounterattackRimKataSessionActive(Pawn pawn)
        {
            return pawn?.Drafted != true
                && RimKataEligibility.RandomAttackEnabledForPawn(pawn)
                && StateFor(pawn, false)?.counterattackRimKataSessionActive
                    == true;
        }

        public static bool TryGetCounterattackRushTarget(
            Pawn pawn,
            out Thing target)
        {
            RimKataPawnCombatState state = StateFor(pawn, false);
            target = state?.counterattackRimKataSessionActive == true
                && state.rushAuthority == RimKataRushAuthority.Counterattack
                && state.RushAuthorized
                    ? state.rushAuthorityTarget
                    : null;
            return target != null;
        }

        public static bool TryConvertStartedCounterattackJob(
            Pawn pawn,
            Job sourceJob,
            ThinkNode jobGiver,
            out Job convertedJob)
        {
            convertedJob = sourceJob;
            if ((!(jobGiver is JobGiver_ConfigurableHostilityResponse)
                    && !(jobGiver is JobGiver_ReactToCloseMeleeThreat))
                || !RimKataEligibility.RandomAttackEnabledForPawn(pawn))
            {
                return false;
            }

            if (!IsConfigurableCounterattackJob(pawn, sourceJob))
            {
                return false;
            }

            Thing target = sourceJob.targetA.Thing;
            RimKataPawnCombatState state = StateFor(pawn, true);
            bool continuingSession = state.counterattackRimKataSessionActive
                && (HasEngagementContinuity(pawn, state)
                    || IsValidCounterattackJobTarget(
                        pawn,
                        state.counterattackJobTarget));
            if (continuingSession)
            {
                Thing previousJobTarget = state.counterattackJobTarget;
                Thing jobTarget = IsValidCounterattackJobTarget(
                        pawn,
                        previousJobTarget)
                    ? previousJobTarget
                    : ResolveCloseTarget(
                            pawn,
                            state,
                            target,
                            false,
                            false)
                        ?? target;
                if (!CanAuthorizeCounterattackRush(pawn, jobTarget))
                {
                    return false;
                }

                BindCurrentWeapons(pawn, state);
                TryStartOpeningRingSearch(pawn, state);
                RimKataSharedTargetSearch.TryAddKnownAutomaticTarget(
                    pawn,
                    state,
                    target);
                ActivateCounterattackRush(
                    pawn,
                    state,
                    jobTarget,
                    sourceJob.verbToUse);
                if (previousJobTarget != jobTarget)
                {
                    state.QueueCounterattackMote();
                }
                RefreshDualEngagementState(pawn, state);
                state.dualLastDrivenTick = -1;

                Job continuingJob = JobMaker.MakeJob(
                    RimKataDefOf.RimKata_Attack,
                    jobTarget);
                continuingJob.playerForced = false;
                continuingJob.killIncappedTarget = false;
                continuingJob.verbToUse = sourceJob.verbToUse
                    ?? RimKataWeaponSlotUtility.CombatVerb(
                        pawn,
                        state.engagementOwnerWeapon)
                    ?? RimKataWeaponSlotUtility.CombatVerb(
                        pawn,
                        RimKataWeaponSlotUtility.PrimaryWeapon(pawn));
                convertedJob = continuingJob;
                return true;
            }

            Thing openingJobTarget = ResolveCloseTarget(
                    pawn,
                    state,
                    target,
                    false,
                    false)
                ?? target;
            if (!CanAuthorizeCounterattackRush(pawn, openingJobTarget))
            {
                return false;
            }

            state.ClearCounterattackRimKataSession();
            state.BeginCounterattackRimKataSession();
            state.MarkPendingCounterattack(target);
            BindCurrentWeapons(pawn, state);
            ActivateCounterattackRush(
                pawn,
                state,
                openingJobTarget,
                sourceJob.verbToUse);

            Job rimKataJob = JobMaker.MakeJob(
                RimKataDefOf.RimKata_Attack,
                openingJobTarget);
            rimKataJob.playerForced = false;
            rimKataJob.killIncappedTarget = false;
            rimKataJob.verbToUse = sourceJob.verbToUse
                ?? RimKataWeaponSlotUtility.CombatVerb(
                    pawn,
                    state.engagementOwnerWeapon)
                ?? RimKataWeaponSlotUtility.CombatVerb(
                    pawn,
                    RimKataWeaponSlotUtility.PrimaryWeapon(pawn));
            convertedJob = rimKataJob;
            return true;
        }

        public static void RecoverCurrentCounterattackJob(Pawn pawn)
        {
            Job job = pawn?.CurJob;
            if ((!(job?.jobGiver is JobGiver_ConfigurableHostilityResponse)
                    && !(job?.jobGiver is JobGiver_ReactToCloseMeleeThreat))
                || !RimKataEligibility.RandomAttackEnabledForPawn(pawn))
            {
                return;
            }

            if (!IsConfigurableCounterattackJob(pawn, job))
            {
                return;
            }

            Thing sourceTarget = job.targetA.Thing;
            RimKataPawnCombatState state = StateFor(pawn, true);
            Thing previousJobTarget = state.counterattackJobTarget;
            bool continuingSession = state.counterattackRimKataSessionActive;
            bool repairLoadedTarget =
                state.ConsumeCounterattackTargetLoadRepair();
            Thing target = continuingSession
                && !repairLoadedTarget
                && IsValidCounterattackJobTarget(pawn, previousJobTarget)
                    ? previousJobTarget
                    : ResolveCloseTarget(
                            pawn,
                            state,
                            sourceTarget,
                            false,
                            false)
                        ?? (IsValidCounterattackJobTarget(
                                pawn,
                                previousJobTarget)
                            ? previousJobTarget
                            : sourceTarget);
            state.BeginCounterattackRimKataSession();
            if (state.IsPendingCounterattack(target) != true)
            {
                state.MarkPendingCounterattack(target);
            }

            if (!CanAuthorizeCounterattackRush(pawn, target))
            {
                return;
            }

            BindCurrentWeapons(pawn, state);
            ActivateCounterattackRush(pawn, state, target, job.verbToUse);
            if (continuingSession && previousJobTarget != target)
            {
                state.QueueCounterattackMote();
            }
            QueueDedicatedFollowupJob(pawn, target);
        }

        private static bool CanAuthorizeCounterattackRush(
            Pawn pawn,
            Thing target)
        {
            return pawn?.Drafted != true
                && RimKataMod.Settings?.targetRushEnabled != false
                && TargetWithinAutomaticSearchRange(pawn, target);
        }

        private static bool IsValidCounterattackJobTarget(
            Pawn pawn,
            Thing target)
        {
            return pawn?.Map != null
                && target != null
                && !target.Destroyed
                && target.Spawned
                && target.Map == pawn.Map
                && RimKataTargeting.IsAutomaticEnemy(pawn, target)
                && (!(target is Pawn targetPawn)
                    || (!targetPawn.Dead
                        && !targetPawn.Downed
                        && !targetPawn.Crawling
                        && !targetPawn.IsPsychologicallyInvisible()));
        }

        private static bool TryTransitionCounterattackJobTarget(
            Pawn pawn,
            RimKataPawnCombatState state,
            Thing target,
            bool showMote)
        {
            Job job = pawn?.CurJob;
            if (pawn?.Map == null
                || state?.counterattackRimKataSessionActive != true
                || job?.def != RimKataDefOf.RimKata_Attack
                || job.playerForced
                || !IsValidCounterattackJobTarget(pawn, target))
            {
                return false;
            }

            Thing previousTarget = state.counterattackJobTarget
                ?? job.targetA.Thing;
            bool changed = previousTarget != target;
            state.counterattackJobTarget = target;
            if (job.targetA.Thing != target)
            {
                job.targetA = target;
            }

            if (CanAuthorizeCounterattackRush(pawn, target))
            {
                state.SetRushAuthority(
                    RimKataRushAuthority.Counterattack,
                    target);
            }
            else if (state.rushAuthority ==
                RimKataRushAuthority.Counterattack)
            {
                state.ClearRushAuthority();
            }

            RimKataSharedTargetSearch.TryAddKnownAutomaticTarget(
                pawn,
                state,
                target);
            RefreshDualEngagementState(pawn, state);
            if (changed && showMote)
            {
                MoteMaker.MakeColonistActionOverlay(
                    pawn,
                    ThingDefOf.Mote_ColonistAttacking);
            }

            return true;
        }

        private static bool TryPromotePreparedCounterattackJobTarget(
            Pawn pawn,
            RimKataPawnCombatState state,
            out Thing target)
        {
            target = null;
            Job job = pawn?.CurJob;
            if (state?.counterattackRimKataSessionActive != true
                || job?.def != RimKataDefOf.RimKata_Attack
                || job.playerForced
                || IsValidCounterattackJobTarget(pawn, job.targetA.Thing))
            {
                return false;
            }

            Thing primaryTarget = PreparedCounterattackTarget(
                pawn,
                state,
                state.primaryWeaponCycle,
                out int primaryTicks);
            Thing secondaryTarget = PreparedCounterattackTarget(
                pawn,
                state,
                state.secondaryWeaponCycle,
                out int secondaryTicks);
            target = primaryTarget != null
                && (secondaryTarget == null || primaryTicks <= secondaryTicks)
                    ? primaryTarget
                    : secondaryTarget;
            return target != null
                && TryTransitionCounterattackJobTarget(
                    pawn,
                    state,
                    target,
                    true);
        }

        private static Thing PreparedCounterattackTarget(
            Pawn pawn,
            RimKataPawnCombatState state,
            RimKataWeaponCycleState cycle,
            out int ticksUntilAttack)
        {
            ticksUntilAttack = int.MaxValue;
            Thing target = cycle?.plannedTarget;
            Verb verb = RimKataWeaponSlotUtility.CombatVerbForContext(
                pawn,
                cycle?.weapon,
                cycle?.plannedCloseContext == true
                    || state?.dualCloseCombatActive == true);
            if (target == null
                || cycle.plannedInterception
                || verb == null
                || !RimKataSharedTargetSearch.IsValidForVerb(
                    pawn,
                    verb,
                    target))
            {
                return null;
            }

            ticksUntilAttack = Mathf.Max(0, cycle.cooldownTicksRemaining)
                + EstimatedPreparedWarmupTicks(pawn, state, cycle, verb)
                + Mathf.Max(0, cycle.burstTicksUntilNextShot);
            return target;
        }

        private static int EstimatedPreparedWarmupTicks(
            Pawn pawn,
            RimKataPawnCombatState state,
            RimKataWeaponCycleState cycle,
            Verb verb)
        {
            if (cycle.warmupTicksRemaining >= 0)
            {
                return cycle.warmupTicksRemaining;
            }

            int warmup = RimKataCombatMath.WarmupTicksForSingleShot(verb);
            if (cycle.openingWarmupPending)
            {
                return warmup
                    + Mathf.Max(0, cycle.openingWarmupBonusTicks);
            }

            if (cycle.openingSupportDelayConsumed
                || state?.engagementOwnerWeapon == null
                || state.engagementOwnerWeapon == cycle.weapon)
            {
                return warmup;
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
                return warmup;
            }

            return warmup + Mathf.Max(
                1,
                Mathf.CeilToInt(
                    RimKataCombatMath.CooldownTicksForSingleShot(
                        ownerVerb,
                        pawn,
                        false) * 0.5f));
        }

        private static void ActivateCounterattackRush(
            Pawn pawn,
            RimKataPawnCombatState state,
            Thing target,
            Verb requestedVerb)
        {
            ThingWithComps ownerWeapon =
                requestedVerb?.EquipmentSource as ThingWithComps;
            if (CycleForWeapon(state, ownerWeapon) == null)
            {
                ownerWeapon = RimKataWeaponSlotUtility.PrimaryWeapon(pawn);
            }

            state.SetRushAuthority(
                RimKataRushAuthority.Counterattack,
                target);
            state.counterattackJobTarget = target;
            state.pendingCounterattackTarget = null;
            state.pendingCounterattackTicksRemaining = 0;
            state.engagementOwnerWeapon = ownerWeapon;
            TryStartOpeningRingSearch(pawn, state);
            RimKataSharedTargetSearch.TryAddKnownAutomaticTarget(
                pawn,
                state,
                target);
            RefreshDualEngagementState(pawn, state);
            state.dualLastDrivenTick = -1;
        }

        private static bool IsConfigurableCounterattackJob(
            Pawn pawn,
            Job job)
        {
            Thing target = job?.targetA.Thing;
            return pawn?.Map != null
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
            RimKataPawnCombatState state = StateFor(pawn, false);
            return RimKataMod.Settings?.targetRushEnabled != false
                && state?.RushAuthorized == true
                && state.rushAuthorityTarget == target;
        }

        private static void CancelOpeningOwner(
            Pawn pawn,
            RimKataPawnCombatState state,
            RimKataWeaponCycleState openingCycle)
        {
            if (state == null || openingCycle == null)
            {
                return;
            }

            Thing target = openingCycle.vanillaOpeningTarget;
            bool closeContext = openingCycle.vanillaOpeningCloseContext;
            RimKataWeaponCycleState supportCycle = OtherCycle(state, openingCycle);
            Verb openingVerb = RimKataWeaponSlotUtility.CombatVerb(
                pawn,
                openingCycle.weapon);
            if (ValidCurrentTargetForVerb(
                pawn,
                openingVerb,
                target,
                pawn?.CurJob?.playerForced == true,
                pawn?.CurJob?.killIncappedTarget == true,
                closeContext))
            {
                CancelOpeningSession(state);
                return;
            }

            ResetCycleForNewOpening(openingCycle);

            bool supportCanContinue = supportCycle?.firedInCurrentOpening == true
                && HasCycleTargetWork(pawn, state, supportCycle);
            if (!supportCanContinue
                && supportCycle?.openingWarmupPending == true)
            {
                Verb supportVerb = RimKataWeaponSlotUtility.CombatVerb(
                    pawn,
                    supportCycle.weapon);
                supportCanContinue = ValidCurrentTargetForVerb(
                    pawn,
                    supportVerb,
                    target,
                    pawn?.CurJob?.playerForced == true,
                    pawn?.CurJob?.killIncappedTarget == true,
                    closeContext);
            }

            if (!supportCanContinue)
            {
                ResetCycleForNewOpening(supportCycle);
            }

            RefreshDualEngagementState(pawn, state);
            if (!state.dualEngagementActive)
            {
                state.ClearOpeningRingSearch();
                state.engagementOwnerWeapon = null;
                state.ClearRushAuthority();
                state.dualLastDrivenTick = -1;
            }
        }

        private static void CancelOpeningSession(RimKataPawnCombatState state)
        {
            if (state == null)
            {
                return;
            }

            ResetCycleForNewOpening(state.primaryWeaponCycle);
            ResetCycleForNewOpening(state.secondaryWeaponCycle);
            state.engagementOwnerWeapon = null;
            state.ClearRushAuthority();
            state.ClearOpeningRingSearch();
            RefreshDualEngagementState(state.pawn, state);
            state.dualLastDrivenTick = -1;
        }

        private static void ResetCycleForNewOpening(
            RimKataWeaponCycleState cycle)
        {
            if (cycle == null)
            {
                return;
            }

            ThingWithComps weapon = cycle.weapon;
            Thing focusedTarget = cycle.focusedTarget;
            bool focusedTargetFromAttackGizmo =
                cycle.focusedTargetFromAttackGizmo;
            int cooldownTicksRemaining = Mathf.Max(
                0,
                cycle.cooldownTicksRemaining);
            bool cooldownFromVanillaOpening = cooldownTicksRemaining > 0
                && cycle.cooldownFromVanillaOpening;
            List<Thing> automaticCandidates = cycle.automaticCandidates != null
                ? new List<Thing>(cycle.automaticCandidates)
                : null;
            bool automaticCandidateCollectionClosed =
                cycle.automaticCandidateCollectionClosed;
            int pendingCandidateLimitOverride =
                cycle.pendingCandidateLimitOverride;
            int activeCandidateLimitOverride =
                cycle.activeCandidateLimitOverride;
            bool openingSupportDelayConsumed =
                cycle.openingSupportDelayConsumed;
            cycle.Reset();
            cycle.Bind(weapon);
            cycle.cooldownTicksRemaining = cooldownTicksRemaining;
            cycle.cooldownFromVanillaOpening = cooldownFromVanillaOpening;
            if (automaticCandidates != null)
            {
                for (int i = 0; i < automaticCandidates.Count; i++)
                {
                    cycle.AddAutomaticCandidate(automaticCandidates[i]);
                }
            }
            cycle.automaticCandidateCollectionClosed =
                automaticCandidateCollectionClosed;
            cycle.pendingCandidateLimitOverride =
                pendingCandidateLimitOverride;
            cycle.activeCandidateLimitOverride =
                activeCandidateLimitOverride;
            cycle.openingSupportDelayConsumed =
                openingSupportDelayConsumed;
            cycle.focusedTarget = focusedTarget;
            cycle.focusedTargetFromAttackGizmo =
                focusedTargetFromAttackGizmo;
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

        private static void CancelUnfiredOpening(
            RimKataWeaponCycleState cycle)
        {
            ResetUnfiredOpeningTimer(cycle);
            if (cycle == null)
            {
                return;
            }

            cycle.openingWarmupBonusTicks = 0;
            cycle.openingWarmupPending = false;
        }

        private static RimKataWeaponCycleState OpeningCycle(
            RimKataPawnCombatState state)
        {
            if (state?.primaryWeaponCycle?.vanillaOpeningPending == true)
            {
                return state.primaryWeaponCycle;
            }

            return state?.secondaryWeaponCycle?.vanillaOpeningPending == true
                ? state.secondaryWeaponCycle
                : null;
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

        private static void ArmDedicatedFollowup(
            Pawn pawn,
            RimKataPawnCombatState state,
            RimKataWeaponCycleState cycle,
            Verb verb,
            Thing target,
            bool closeContext,
            int openingBonusSourceTicks)
        {
            cycle.StopProgressiveSearch();
            cycle.ClearPlan();
            cycle.cachedCandidateTarget = null;
            cycle.cachedCandidateInterception = false;
            cycle.skipNextWarmup = false;
            SetCandidate(
                cycle,
                target,
                false,
                closeContext,
                closeContext,
                true);

            int openingBonus = Mathf.Max(
                0,
                Mathf.CeilToInt(openingBonusSourceTicks * 0.5f));
            int normalWarmup = RimKataCombatMath.WarmupTicksForSingleShot(verb);
            cycle.openingWarmupBonusTicks = openingBonus;
            cycle.openingWarmupPending = true;
            cycle.warmupTotalTicks = normalWarmup + openingBonus;
            cycle.warmupTicksRemaining = cycle.warmupTotalTicks;
            cycle.visualTarget = target;

            if (closeContext)
            {
                state.dualCloseCombatActive = true;
                state.dualCloseTarget = target;
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

            if (verb.IsMeleeAttack || closeContext)
            {
                return pawn.CanReachImmediate(target, PathEndMode.Touch);
            }

            return verb.CanHitTarget(target);
        }

        private static bool ValidCachedFollowup(
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
                    || (targetPawn.Downed && !(playerForced && killIncappedTarget))))
            {
                return false;
            }

            if (verb.IsMeleeAttack || closeContext)
            {
                return pawn.CanReachImmediate(target, PathEndMode.Touch);
            }

            float range = RimKataRangeUtility.ResolveCandidateRange(verb);
            return pawn.Position.DistanceToSquared(target.Position) <= range * range
                && verb.CanHitTarget(target);
        }

        public static bool TryTakeVanillaMeleeCooldown(
            Pawn pawn,
            Verb verb,
            LocalTargetInfo focus)
        {
            if (pawn?.Map == null
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

            if (cycle.vanillaOpeningPending)
            {
                cycle.ClearVanillaOpening();
                RimKataWeaponCycleState supportCycle = OtherCycle(state, cycle);
                if (supportCycle?.openingWarmupPending == true
                    && !supportCycle.firedInCurrentOpening)
                {
                    CancelUnfiredOpening(supportCycle);
                }
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

            target = state?.RushAuthorized == true
                ? state.rushAuthorityTarget
                : null;
            if (target != null
                && !PermanentlyInvalidCycleTarget(
                    pawn,
                    target,
                    target,
                    playerForced,
                    killIncappedTarget,
                    false))
            {
                return true;
            }

            state?.ClearRushAuthority();
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
                || state?.openingRingSearchActive == true
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
                || state?.openingRingSearchActive == true
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
                if (MovementSearchBlockedByCloseCombat(pawn, state))
                {
                    return true;
                }

                if (TryBeginMovementSearch(pawn, state, pawn.Position))
                {
                    state.ConsumeDraftedMovementSearchTrigger();
                    return true;
                }
            }

            BindCurrentWeapons(pawn, state);
            Verb searchVerb = LongestAutomaticRangeVerb(pawn);
            RimKataWeaponCycleState searchCycle = CycleForWeapon(
                state,
                searchVerb?.EquipmentSource as ThingWithComps);
            if (!CanOwnMovementSearch(pawn, searchCycle))
            {
                return false;
            }

            if (searchCycle.progressiveSearchExhausted
                && searchCycle.progressiveSearchOrigin == pawn.Position)
            {
                return false;
            }

            if (searchCycle.progressiveSearchExhausted)
            {
                searchCycle.RearmTargetSearch();
            }

            if (!BeginProgressiveSearch(pawn, searchCycle, pawn.Position))
            {
                return false;
            }

            state.dualLastDrivenTick = Find.TickManager?.TicksGame ?? -1;
            return true;
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

            Thing targetThing = cycle.cooldownTicksRemaining > 0
                && cycle.visualTarget != null
                && cycle.visualTarget.Spawned
                && !cycle.visualTarget.Destroyed
                ? cycle.visualTarget
                : cycle.plannedTarget ?? cycle.visualTarget;
            LocalTargetInfo target = targetThing != null && targetThing.Spawned
                ? new LocalTargetInfo(targetThing)
                : LocalTargetInfo.Invalid;
            if (cycle.plannedInterception && cycle.plannedTarget is Projectile projectile)
            {
                target = new LocalTargetInfo(projectile.ExactPosition.ToIntVec3());
            }

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
                || state.VanillaOpeningPending
                || state.dualLastDrivenTick == currentTick)
            {
                return;
            }

            state.primaryWeaponCycle?.TickTimers();
            state.secondaryWeaponCycle?.TickTimers();
        }

        public static void DeactivateNonJobCycleWork(Pawn pawn)
        {
            RimKataPawnCombatState state = StateFor(pawn, false);
            if (state == null
                || state.VanillaOpeningPending)
            {
                return;
            }

            RimKataSharedTargetSearch.Prune(pawn, state);
            RefreshDualEngagementState(pawn, state);
            if (state.dualEngagementActive)
            {
                RearmOpeningOwnerIfBothWaiting(state);
                return;
            }

            CancelUnfiredWarmupForDraftChange(state.primaryWeaponCycle);
            CancelUnfiredWarmupForDraftChange(state.secondaryWeaponCycle);
            state.dualEngagementActive = false;
            state.ResetCandidateSaturationExpansion(true);
            RearmOpeningOwnerIfBothWaiting(state);
            state.ClearRushAuthority();
            state.ClearOpeningRingSearch();
            state.ClearCounterattackRimKataSession();
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
            state.ClearRushAuthority();
            state.ClearCounterattackRimKataSession();
            state.ClearOpeningRingSearch();
            state.sharedTargetSearch?.Reset();
            state.ClearDedicatedFollowupJobRequest();
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
            if (pawn?.Map == null
                || cycle == null
                || cycle.plannedInterception
                || cycle.cachedCandidateInterception
                || target == null
                || target.Destroyed
                || !target.Spawned
                || target.Map != pawn.Map
                || (!playerForced
                    && !RimKataTargeting.IsAutomaticEnemy(pawn, target)))
            {
                target = null;
                return false;
            }

            if (target is Pawn targetPawn
                && (targetPawn.Dead
                    || targetPawn.Crawling
                    || targetPawn.IsPsychologicallyInvisible()
                    || (targetPawn.Downed && !(playerForced && killIncappedTarget))))
            {
                target = null;
                return false;
            }

            return true;
        }

        private static bool CycleHasContinuationSearchWork(RimKataWeaponCycleState cycle)
        {
            return cycle != null
                && (cycle.progressiveSearchMode
                    || cycle.progressiveSearchActive
                    || cycle.plannedInterception
                    || cycle.cachedCandidateInterception);
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
            bool enteringCloseCombat = closeCombatContext
                && !state.dualCloseCombatActive;
            bool changedCloseTarget = closeCombatContext
                && (!state.dualCloseCombatActive || state.dualCloseTarget != closeTarget);
            if (changedCloseTarget)
            {
                if (enteringCloseCombat)
                {
                    TryTransitionCounterattackJobTarget(
                        pawn,
                        state,
                        closeTarget,
                        true);
                }
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
                cycle.ClearPlan();
            }

            if (cycle.focusedTarget != null
                && !RimKataSharedTargetSearch.IsValidForVerb(
                    pawn,
                    verb,
                    cycle.focusedTarget))
            {
                cycle.focusedTarget = null;
                cycle.focusedTargetFromAttackGizmo = false;
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
            bool progressiveSearchOnly)
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
                cycle.StopProgressiveSearch();
                cycle.ClearPlan();
                return false;
            }

            if (cycle.HasPlan
                && cycle.plannedTarget?.Spawned == true
                && cycle.plannedTargetCell.IsValid
                && cycle.plannedTargetCell != cycle.plannedTarget.Position)
            {
                cycle.plannedTargetCell = cycle.plannedTarget.Position;
                RimKataSharedTargetSearch.Begin(
                    pawn,
                    state,
                    pawn.Position);
            }

            bool focusedTargetControlsCycle = PrepareFocusedTarget(
                pawn,
                cycle,
                verb,
                closeCombatContext);
            if (focusedTargetControlsCycle && !cycle.HasPlan)
            {
                return true;
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
                cycle.cachedCandidateTarget = null;
                cycle.cachedCandidateInterception = false;
                RimKataSharedTargetSearch.Begin(
                    pawn,
                    state,
                    pawn.Position);
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

            if (!focusedTargetControlsCycle
                && progressiveSearchOnly
                && !cycle.HasPlan
                && !cycle.progressiveSearchMode
                && !cycle.progressiveSearchExhausted)
            {
                BeginProgressiveSearch(pawn, cycle, pawn.Position);
            }

            if (!focusedTargetControlsCycle
                && !verb.IsMeleeAttack
                && !cycle.HasPlan
                && cycle.progressiveSearchExhausted)
            {
                bool knownTargetReady = TrySetKnownTarget(
                    pawn,
                    cycle,
                    verb,
                    assignedTarget,
                    playerForced,
                    killIncappedTarget,
                    closeCombatContext,
                    !playerForced,
                    cycle.cooldownTicksRemaining <= 0);

                if (!knownTargetReady)
                {
                    if (cycle.progressiveSearchOrigin.IsValid
                        && cycle.progressiveSearchOrigin != pawn.Position)
                    {
                        BeginProgressiveSearch(pawn, cycle, pawn.Position);
                    }
                    else
                    {
                        return cycle.cooldownTicksRemaining > 0;
                    }
                }
            }

            if (!focusedTargetControlsCycle
                && !cycle.HasPlan
                && cycle.progressiveSearchMode)
            {
                if (cycle.candidateRetryTicks > 0)
                {
                    cycle.candidateRetryTicks--;
                    return cycle.cooldownTicksRemaining > 0;
                }

                if (!cycle.progressiveSearchActive)
                {
                    cycle.progressiveSearchActive = true;
                    cycle.progressiveSearchRadius = 0;
                }

                if (TryAdvanceProgressiveSearch(pawn, state, cycle, assignedTarget, playerForced, closeCombatContext))
                {
                }
                else if (cycle.progressiveSearchActive
                    && (cycle.cooldownTicksRemaining > 1
                        || !TrySetKnownTarget(
                            pawn,
                            cycle,
                            verb,
                            assignedTarget,
                            playerForced,
                            killIncappedTarget,
                            closeCombatContext,
                            !playerForced,
                            cycle.cooldownTicksRemaining <= 0)))
                {
                    return true;
                }
                else
                {
                    if (!cycle.HasPlan
                        && !cycle.progressiveSearchActive
                        && TryReuseLastFiredTarget(
                            pawn,
                            cycle,
                            verb))
                    {
                        return true;
                    }

                    cycle.ExhaustProgressiveSearch(pawn.Position);
                    return cycle.cooldownTicksRemaining > 0;
                }
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
                Thing cachedTarget = cycle.cachedCandidateTarget;
                bool cachedInterception =
                    cycle.cachedCandidateInterception;
                cycle.cachedCandidateTarget = null;
                cycle.cachedCandidateInterception = false;
                bool promoted = cachedInterception
                    ? CanAssignProgressiveTarget(
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
                        true,
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

                if (!promoted)
                {
                    cycle.ClearPlan();
                }
            }

            PromoteApproachingShotToCloseContext(pawn, cycle, verb, closeCombatContext);
            if (!ValidPlan(pawn, cycle, verb, assignedTarget, playerForced, killIncappedTarget, closeCombatContext))
            {
                Thing invalidTarget = cycle.plannedTarget ?? assignedTarget;
                bool cancelOpening = cycle.openingWarmupPending
                    && !cycle.firedInCurrentOpening
                    && PermanentlyInvalidCycleTarget(
                        pawn,
                        invalidTarget,
                        assignedTarget,
                        playerForced,
                        killIncappedTarget,
                        cycle.plannedInterception);
                ApplyInterruptedBurstCooldown(pawn, cycle, verb);
                if (cycle.openingWarmupPending
                    && !cycle.firedInCurrentOpening)
                {
                    if (cancelOpening)
                    {
                        CancelUnfiredOpening(cycle);
                    }
                    else
                    {
                        ResetUnfiredOpeningTimer(cycle);
                    }
                }
                else
                {
                    cycle.ClearPlan();
                }
                bool finalCooldownTick = cycle.cooldownTicksRemaining == 1;
                if (!finalCooldownTick && cycle.candidateRetryTicks > 0)
                {
                    cycle.candidateRetryTicks--;
                    return false;
                }

                if (!focusedTargetControlsCycle
                    && !cycle.progressiveSearchMode)
                {
                    BeginProgressiveSearch(pawn, cycle, pawn.Position);
                }
            }

            if (!focusedTargetControlsCycle
                && !cycle.HasPlan
                && cycle.cachedCandidateTarget == null
                && !cycle.progressiveSearchMode
                && cycle.cooldownTicksRemaining <= 1)
            {
                TrySetKnownTarget(
                    pawn,
                    cycle,
                    verb,
                    assignedTarget,
                    playerForced,
                    killIncappedTarget,
                    closeCombatContext,
                    !playerForced,
                    cycle.cooldownTicksRemaining <= 0);
            }

            if (InterruptMovingFireOutsideAutomaticRange(
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
                RimKataSharedTargetSearch.Begin(
                    pawn,
                    state,
                    pawn.Position);

                cycle.skipNextWarmup = false;
                cycle.plannedActionVerb = ResolveCycleActionVerb(
                    pawn,
                    cycle,
                    verb,
                    closeCombatContext);
                if (cycle.plannedActionVerb == null)
                {
                    cycle.ClearPlan();
                    cycle.candidateRetryTicks = CandidateRetryTicks;
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

            if (cycle.HasPlan
                && !cycle.openingWarmupPending
                && cycle.warmupTicksRemaining > 0)
            {
                cycle.warmupTicksRemaining--;
            }

            return true;
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

            if (closeCombatContext)
            {
                return false;
            }

            bool movingOutsideAutomaticRange = pawn.pather?.MovingNow == true
                && !TargetWithinAutomaticSearchRange(pawn, target);
            if (movingOutsideAutomaticRange
                || !ValidCurrentTargetForVerb(
                    pawn,
                    verb,
                    target,
                    true,
                    false,
                    false))
            {
                if (cycle.HasPlan && cycle.plannedTarget == target)
                {
                    cycle.ClearPlan();
                }
                return false;
            }

            cycle.cachedCandidateTarget = null;
            cycle.cachedCandidateInterception = false;
            cycle.StopProgressiveSearch();

            if (cycle.HasPlan && cycle.plannedTarget != target)
            {
                cycle.ClearPlan();
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
                if (cycle.openingWarmupPending
                    && !cycle.firedInCurrentOpening)
                {
                    ResetUnfiredOpeningTimer(cycle);
                }
                else
                {
                    cycle.warmupTicksRemaining = -1;
                    cycle.warmupTotalTicks = 0;
                }

                cycle.visualTarget = target;
                cycle.visualAimTicksRemaining = Mathf.Max(
                    cycle.visualAimTicksRemaining,
                    2);
            }

            return true;
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
            cycle.warmupTicksRemaining = -1;
            cycle.warmupTotalTicks = 0;
            cycle.candidateRetryTicks = 0;
            cycle.StopProgressiveSearch();
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
                if (!pawn.CanReachImmediate(assignedTarget, PathEndMode.Touch))
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

        private static bool TryAdvanceProgressiveSearch(
            Pawn pawn,
            RimKataPawnCombatState state,
            RimKataWeaponCycleState searchCycle,
            Thing assignedTarget,
            bool playerForced,
            bool closeCombatContext)
        {
            if (pawn?.Map == null
                || state == null
                || searchCycle == null
                || closeCombatContext)
            {
                searchCycle?.StopProgressiveSearch();
                return false;
            }

            Verb searchVerb = RimKataWeaponSlotUtility.CombatVerb(
                pawn,
                searchCycle.weapon);
            if (searchVerb == null || searchVerb.IsMeleeAttack)
            {
                searchCycle.StopProgressiveSearch();
                return false;
            }

            if (state.sharedTargetSearch?.sessionActive != true
                && !RimKataSharedTargetSearch.Begin(
                    pawn,
                    state,
                    searchCycle.progressiveSearchOrigin.IsValid
                        ? searchCycle.progressiveSearchOrigin
                        : pawn.Position))
            {
                searchCycle.StopProgressiveSearch();
                return false;
            }
            if (TryCacheSharedCandidate(
                pawn,
                state,
                searchCycle,
                searchCycle.lastFiredTarget ?? assignedTarget))
            {
                return true;
            }

            searchCycle.progressiveSearchActive =
                state.sharedTargetSearch?.scanActive == true;
            return false;
        }

        private static bool TryReuseLastFiredTarget(
            Pawn pawn,
            RimKataWeaponCycleState cycle,
            Verb verb)
        {
            Thing target = cycle?.lastFiredTarget;
            if (!ValidCachedFollowup(
                pawn,
                verb,
                target,
                false,
                false,
                false)
                || !RimKataSharedTargetSearch.IsValidForVerb(
                    pawn,
                    verb,
                    target))
            {
                return false;
            }

            return TrySetKnownTarget(
                pawn,
                cycle,
                verb,
                target,
                false,
                false,
                false,
                true,
                false);
        }

        private static bool CanAssignProgressiveTarget(
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
                || cycle.focusedTarget != null
                || cycle.cachedCandidateTarget != null
                || cycle.HasPlan
                || cycle.openingWarmupPending
                || cycle.vanillaOpeningPending
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

            float range = RimKataRangeUtility.ResolveCandidateRange(verb);
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

            if (verb.IsMeleeAttack || cycle.plannedCloseAttack)
            {
                return pawn.CanReachImmediate(target, PathEndMode.Touch);
            }

            return !closeCombatContext && verb.CanHitTarget(target);
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

        private static bool CacheCandidateAfterAttack(
            Pawn pawn,
            RimKataWeaponCycleState cycle,
            Verb verb,
            Thing firedTarget,
            bool playerForced,
            bool killIncappedTarget,
            bool closeCombatContext)
        {
            if (pawn?.Map == null
                || cycle == null
                || verb == null)
            {
                return false;
            }

            if (cycle.postShotCacheAttempted)
            {
                return cycle.cachedCandidateTarget != null;
            }

            cycle.postShotCacheAttempted = true;
            cycle.cachedCandidateTarget = null;
            cycle.cachedCandidateInterception = false;
            RimKataPawnCombatState state = StateFor(pawn, false);
            if (state != null)
            {
                RimKataSharedTargetSearch.Begin(
                    pawn,
                    state,
                    pawn.Position);
            }

            Thing focusedTarget = cycle.focusedTarget;
            if (focusedTarget != null)
            {
                bool focusAppliesNow = !closeCombatContext;
                if (PermanentlyInvalidCycleTarget(
                    pawn,
                    focusedTarget,
                    focusedTarget,
                    true,
                    false,
                    false))
                {
                    cycle.focusedTarget = null;
                    cycle.focusedTargetFromAttackGizmo = false;
                }
                else if (focusAppliesNow)
                {
                    if (ValidCurrentTargetForVerb(
                        pawn,
                        verb,
                        focusedTarget,
                        true,
                        false,
                        closeCombatContext))
                    {
                        cycle.cachedCandidateTarget = focusedTarget;
                        cycle.cachedCandidateInterception = false;
                    }

                    return true;
                }
            }

            if (!RimKataEligibility.RandomAttackEnabledForPawn(pawn)
                && ValidCachedFollowup(
                    pawn,
                    verb,
                    firedTarget,
                    playerForced,
                    killIncappedTarget,
                    closeCombatContext)
                && RimKataSharedTargetSearch.IsValidForVerb(
                    pawn,
                    verb,
                    firedTarget))
            {
                cycle.cachedCandidateTarget = firedTarget;
                cycle.cachedCandidateInterception = false;
                if (state != null)
                {
                    RimKataSharedTargetSearch.TryAddKnownAutomaticTarget(
                        pawn,
                        state,
                        firedTarget);
                }
                return true;
            }

            if (state == null)
            {
                return false;
            }

            RimKataSharedTargetSearch.TryAddKnownAutomaticTarget(
                pawn,
                state,
                firedTarget);
            if (!RimKataSharedTargetSearch.TrySelectCandidate(
                pawn,
                state,
                verb,
                firedTarget,
                out Thing nextTarget,
                out bool interception))
            {
                return false;
            }

            cycle.cachedCandidateTarget = nextTarget;
            cycle.cachedCandidateInterception = interception;
            return cycle.cachedCandidateTarget != null;
        }

        private static int ExecuteCycle(
            Pawn pawn,
            RimKataWeaponCycleState cycle,
            Thing assignedTarget,
            bool playerForced,
            bool killIncappedTarget,
            bool closeCombatContext)
        {
            Verb verb = RimKataWeaponSlotUtility.CombatVerbForContext(
                pawn,
                cycle.weapon,
                closeCombatContext);
            if (verb == null)
            {
                cycle.ClearPlan();
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
                cycle.ClearPlan();
                cycle.candidateRetryTicks = CandidateRetryTicks;
                return -1;
            }
            cycle.plannedActionVerb = actionVerb;

            LocalTargetInfo target = TargetInfo(cycle);
            if (!target.IsValid)
            {
                cycle.ClearPlan();
                return -1;
            }

            if (InterruptMovingFireOutsideAutomaticRange(
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
                || cycle.vanillaOpeningPending
                || (cycle.burstShotsRemaining > 0
                    && cycle.cooldownFromVanillaOpening);
            bool firstShotOfSequence = cycle.burstShotsRemaining <= 0;
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
                cycle.candidateRetryTicks = CandidateRetryTicks;
                return -1;
            }

            cycle.cooldownFromVanillaOpening = firedFromVanillaOpening;

            RimKataPawnCombatState state = StateFor(pawn, false);
            if (state?.VanillaOpeningPending == true)
            {
                cycle.firedInCurrentOpening = true;
            }
            RecordFirstFiredWeapon(state, cycle.weapon);
            if (firstShotOfSequence && state != null)
            {
                RimKataSharedTargetSearch.Begin(
                    pawn,
                    state,
                    pawn.Position);
            }

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
            cycle.postShotCacheAttempted = false;

            if (!CacheCandidateAfterAttack(
                pawn,
                cycle,
                verb,
                firedTarget,
                playerForced,
                killIncappedTarget,
                closeCombatContext))
            {
                if (!verb.IsMeleeAttack
                    && !closeCombatContext
                    && state?.openingRingSearchActive != true)
                {
                    BeginProgressiveSearch(pawn, cycle, pawn.Position);
                }
                else
                {
                    cycle.candidateRetryTicks = CandidateRetryTicks;
                }
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
            bool permanentlyInvalid = PermanentlyInvalidCycleTarget(
                pawn,
                invalidTarget,
                assignedTarget,
                playerForced,
                killIncappedTarget,
                cycle.plannedInterception);

            ApplyInterruptedBurstCooldown(pawn, cycle, verb);
            if (cycle.openingWarmupPending
                && !cycle.firedInCurrentOpening)
            {
                if (permanentlyInvalid)
                {
                    CancelUnfiredOpening(cycle);
                }
                else
                {
                    ResetUnfiredOpeningTimer(cycle);
                }
            }
            else
            {
                cycle.ClearPlan();
            }

            cycle.candidateRetryTicks = 0;
            RimKataPawnCombatState state = StateFor(pawn, false);
            if (!permanentlyInvalid
                || verb.IsMeleeAttack
                || closeCombatContext
                || cycle.openingWarmupPending
                || cycle.vanillaOpeningPending
                || state == null
                || state.VanillaOpeningPending)
            {
                return;
            }

            RimKataSharedTargetSearch.Begin(
                pawn,
                state,
                pawn.Position);
            RimKataSharedTargetSearch.Prune(
                pawn,
                state);
            TryCacheSharedCandidate(
                pawn,
                state,
                cycle,
                cycle.lastFiredTarget ?? assignedTarget);
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

            ApplyInterruptedBurstCooldown(pawn, cycle, verb);
            if (cycle.openingWarmupPending
                && !cycle.firedInCurrentOpening)
            {
                if (cycle.focusedTarget != null)
                {
                    ResetUnfiredOpeningTimer(cycle);
                }
                else
                {
                    CancelUnfiredOpening(cycle);
                }
            }
            else
            {
                cycle.ClearPlan();
                cycle.visualTarget = null;
                cycle.visualAimTicksRemaining = 0;
            }

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

            if (cycle.focusedTarget == null
                && cycle.cachedCandidateTarget == null)
            {
                BeginProgressiveSearch(pawn, cycle, pawn.Position);
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
                && pawn.stances?.curStance is Stance_RimKataAim closeAim)
            {
                ThingWithComps closeAimWeapon =
                    closeAim.verb?.EquipmentSource as ThingWithComps;
                Verb closeAimCombatVerb = RimKataWeaponSlotUtility.CombatVerb(
                    pawn,
                    closeAimWeapon);
                if (closeAimCombatVerb?.IsMeleeAttack == false)
                {
                    pawn.stances.SetStance(new Stance_Mobile());
                }
            }

            if (!TryGetNextAim(
                    pawn,
                    out ThingWithComps weapon,
                    out LocalTargetInfo target))
            {
                return;
            }

            Verb slotVerb = RimKataWeaponSlotUtility.CombatVerbForContext(
                pawn,
                weapon,
                state?.dualCloseCombatActive == true);
            if (UsesPhysicalMeleeAction(
                slotVerb,
                state?.dualCloseCombatActive == true))
            {
                return;
            }

            RimKataWeaponCycleState aimCycle = CycleForWeapon(state, weapon);
            Verb verb = aimCycle?.plannedActionVerb ?? slotVerb;
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
            bool primaryHasAim = primary?.plannedTarget != null || primary?.visualTarget != null;
            bool secondaryHasAim = secondary?.plannedTarget != null || secondary?.visualTarget != null;
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
            if (!__state.prepared)
            {
                return;
            }

            if (!__result)
            {
                RimKataDualWeaponController.RestoreVanillaOpening(
                    __instance?.CasterPawn,
                    __state);
                return;
            }

            RimKataDualWeaponController.CommitVanillaOpening(
                __instance?.CasterPawn,
                __instance);
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

    [HarmonyPatch(typeof(Stance_Warmup), nameof(Stance_Warmup.StanceTick))]
    public static class Patch_StanceWarmup_RimKataTargetCell
    {
        public static void Prefix(Stance_Warmup __instance)
        {
            if (__instance?.focusTarg.HasThing == true)
            {
                Pawn pawn = __instance.stanceTracker?.pawn;
                Thing target = __instance.focusTarg.Thing;
                RimKataDualWeaponController.NotifyVanillaOpeningTargetCell(
                    pawn,
                    __instance.verb,
                    target);
                if (RimKataDualWeaponController.IsVanillaOpeningActive(pawn))
                {
                    RimKataDualWeaponController.Tick(
                        pawn,
                        target,
                        pawn.CurJob?.playerForced == true,
                        pawn.CurJob?.killIncappedTarget == true,
                        false);
                }
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_StanceTracker), nameof(Pawn_StanceTracker.SetStance))]
    public static class Patch_PawnStanceTracker_RimKataCooldown
    {
        public static bool Prefix(
            Stance newStance,
            Pawn ___pawn,
            ref bool __state)
        {
            __state = false;
            if (!(newStance is Stance_Cooldown cooldown))
            {
                if (!(newStance is Stance_Warmup)
                    && ___pawn?.stances?.curStance is Stance_Warmup nonCooldownInterruptedWarmup)
                {
                    RimKataDualWeaponController.CancelVanillaOpening(
                        ___pawn,
                        nonCooldownInterruptedWarmup.verb,
                        nonCooldownInterruptedWarmup.focusTarg);
                }

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

            LocalTargetInfo focus = cooldown.focusTarg;
            if (focus.IsValid
                && focus.HasThing
                && RimKataDualWeaponController.TryBeginFromVanillaCooldown(
                    ___pawn,
                    cooldown.verb,
                    focus.Thing,
                    Mathf.Max(1, cooldown.ticksLeft)))
            {
                __state = true;
                return false;
            }

            if (!cooldown.verb.Bursting
                && ___pawn?.stances?.curStance is Stance_Warmup interruptedWarmup)
            {
                RimKataDualWeaponController.CancelVanillaOpening(
                    ___pawn,
                    interruptedWarmup.verb,
                    interruptedWarmup.focusTarg);
            }

            return true;
        }

        public static void Postfix(
            Stance newStance,
            Pawn ___pawn,
            bool __state)
        {
            if (!__state
                || !(newStance is Stance_Cooldown cooldown)
                || !cooldown.focusTarg.HasThing)
            {
                return;
            }

            RimKataDualWeaponController.QueueDedicatedFollowupJob(
                ___pawn,
                cooldown.focusTarg.Thing);
            RimKataDualWeaponController.RefreshPendingDedicatedFollowupAim(
                ___pawn,
                cooldown.verb,
                cooldown.focusTarg);
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
