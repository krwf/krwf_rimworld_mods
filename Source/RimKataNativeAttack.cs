using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Verse;
using Verse.AI;

namespace KRWF.RimKata
{
    // One reusable request per weapon cycle. The equipment/body VerbTracker owns
    // execution; the controller only supplies the ready target and consumes the result.
    internal sealed class RimKataNativeAttack
    {
        private sealed class VerbBinding
        {
            internal RimKataNativeAttack request;
            internal bool cancelAfterLoad;
        }

        private static readonly ConditionalWeakTable<Verb, VerbBinding> bindings =
            new ConditionalWeakTable<Verb, VerbBinding>();
        private static readonly AccessTools.FieldRef<Verb, LocalTargetInfo> currentTarget =
            AccessTools.FieldRefAccess<Verb, LocalTargetInfo>("currentTarget");
        private static readonly AccessTools.FieldRef<Verb, LocalTargetInfo> currentDestination =
            AccessTools.FieldRefAccess<Verb, LocalTargetInfo>("currentDestination");
        private static readonly AccessTools.FieldRef<Verb, bool> surpriseAttack =
            AccessTools.FieldRefAccess<Verb, bool>("surpriseAttack");
        private static readonly AccessTools.FieldRef<Verb, bool> canHitNonTargetPawns =
            AccessTools.FieldRefAccess<Verb, bool>("canHitNonTargetPawnsNow");
        private static readonly AccessTools.FieldRef<Verb, bool> preventFriendlyFire =
            AccessTools.FieldRefAccess<Verb, bool>("preventFriendlyFire");
        private static readonly AccessTools.FieldRef<Verb, bool> nonInterruptingSelfCast =
            AccessTools.FieldRefAccess<Verb, bool>("nonInterruptingSelfCast");

        internal Pawn pawn;
        internal RimKataPawnCombatState state;
        internal RimKataWeaponCycleState cycle;
        internal ThingWithComps weapon;
        internal Verb verb;
        internal Verb cycleVerb;
        internal Job job;
        internal Thing assignedTarget;
        internal Thing firedTarget;
        internal LocalTargetInfo target;
        internal bool playerForced;
        internal bool killIncappedTarget;
        internal bool closeCombatContext;
        internal bool allowAutomaticRangedFire;
        internal bool randomAttackEnabled;
        internal bool firedFromVanillaOpening;
        internal bool movingShot;
        internal bool closeShot;
        internal bool interceptionShot;
        internal bool closeMeleeResolution;
        internal bool closeMeleeHit;
        internal Projectile interceptionTarget;
        internal RimKataCloseDefensePrecheck closeDefensePrecheck;
        internal bool Pending { get; private set; }
        internal bool Executing { get; private set; }
        private bool cancelled;
        private Verb bindingVerb;
        private VerbBinding nativeBinding;
        private RimKataFireContext.ScopeState previousContext;
        private Stance_RimKataAim previousAim;

        internal static void Bind(Verb verb)
        {
            if (verb != null)
            {
                bindings.GetOrCreateValue(verb);
                RimKataPreparedWeaponData.Bind(verb);
            }
        }

        internal bool Queue()
        {
            if (Pending || verb == null || verb.state != VerbState.Idle
                || pawn?.Spawned != true || !target.IsValid)
            {
                return false;
            }
            if (bindingVerb != verb)
            {
                Bind(verb);
                bindingVerb = verb;
                nativeBinding = bindings.GetOrCreateValue(verb);
            }
            else if (!(verb.verbProps is RimKataPreparedVerbProperties properties)
                || properties.ConfigurationRevision != RimKataEquipmentUtility.WeaponConfigurationRevision)
            {
                RimKataPreparedWeaponData.Bind(verb);
            }
            if (nativeBinding.request != null)
            {
                return false;
            }

            // Initialize native cast state once. Nothing executes on this stack,
            // and no original target/flag snapshot has to be restored after firing.
            verb.Reset();
            currentTarget(verb) = target;
            currentDestination(verb) = LocalTargetInfo.Invalid;
            surpriseAttack(verb) = false;
            canHitNonTargetPawns(verb) = true;
            preventFriendlyFire(verb) = false;
            nonInterruptingSelfCast(verb) = true;
            cancelled = false;
            Pending = true;
            nativeBinding.request = this;
            return true;
        }

        internal static bool WaitingForNativeTick(Verb verb)
            => verb.verbProps is RimKataPreparedVerbProperties
                && bindings.TryGetValue(verb, out VerbBinding binding)
                && binding.request?.Pending == true && !binding.request.Executing;

        internal static bool CanBeginNativeTick(Verb verb)
        {
            // Unmodified weapons never consult the request table or Pawn state.
            if (!(verb.verbProps is RimKataPreparedVerbProperties)
                || !bindings.TryGetValue(verb, out VerbBinding binding)
                || binding.request is not RimKataNativeAttack request
                || !request.Pending || request.Executing) return false;
            Pawn pawn = request.pawn;
            if (!pawn.Spawned || pawn.Dead || pawn.Downed || pawn.InMentalState
                || pawn.stances.stunner.Stunned || pawn.CurJob != request.job
                || request.cycle.weapon != request.weapon
                || request.cycle.plannedTarget != request.firedTarget
                || !RimKataDualWeaponController.NativeAttackStillAllowed(request)
                || verb.state != VerbState.Idle)
            {
                request.Cancel();
                return false;
            }
            Thing target = request.target.Thing;
            if (target != null && (!target.Spawned || target.Map != pawn.Map
                || (target is Pawn victim && (victim.Dead
                    || (victim.Downed && !(request.playerForced && request.killIncappedTarget))))))
            {
                request.Cancel();
                return false;
            }
            if ((request.closeShot && !pawn.CanReachImmediate(request.target, PathEndMode.Touch))
                || (request.interceptionShot
                    && !RimKataTargeting.IsInterceptionTargetActive(request.interceptionTarget)))
            {
                request.Cancel();
                return false;
            }
            if (request.closeShot)
            {
                request.Executing = true;
                try
                {
                    request.closeMeleeHit = RimKataCombatMath.RollCloseRangedNonMiss(pawn, verb, request.target);
                    request.closeDefensePrecheck = RimKataDefenseUtility.PrecheckCloseGunfire(
                        pawn, target, verb, request.closeMeleeHit);
                }
                catch
                {
                    request.Executing = false;
                    request.Cancel();
                    throw;
                }
                finally { request.Executing = false; }
                if (request.closeDefensePrecheck == RimKataCloseDefensePrecheck.ResponseSucceeded)
                {
                    request.Detach();
                    verb.Reset();
                    nonInterruptingSelfCast(verb) = false;
                    try { RimKataDualWeaponController.CompleteNativeAttack(request, true, request.cancelled); }
                    finally { request.ReleaseReferences(); }
                    return false;
                }
                if (request.closeDefensePrecheck == RimKataCloseDefensePrecheck.ResponseSucceededWithAccidentalShot)
                    request.closeMeleeHit = false;
                if (request.cancelled)
                {
                    request.Cancel();
                    return false;
                }
                if (!request.closeMeleeHit && !(verb is Verb_LaunchProjectile) && target != null)
                {
                    IntVec3 cell = RimKataProjectileUtility.FindCloseMissCell(pawn, target, pawn.Map);
                    if (cell.IsValid) currentTarget(verb) = new LocalTargetInfo(cell);
                }
            }
            return true;
        }

        internal static RimKataNativeAttack BeginNativeCast(Verb verb)
        {
            if (!bindings.TryGetValue(verb, out VerbBinding binding)
                || binding.request is not RimKataNativeAttack request
                || !request.Pending || request.Executing)
            {
                return null;
            }
            request.Executing = true;
            request.previousAim = request.pawn.stances?.curStance as Stance_RimKataAim;
            request.pawn.rotationTracker.FaceCell(verb.CurrentTarget.Cell);
            request.previousContext = RimKataFireContext.Begin(
                verb, request.pawn, request.closeShot ? request.target.Thing : null,
                request.movingShot, request.closeShot, request.interceptionShot,
                request.interceptionTarget, request.closeMeleeResolution,
                request.closeMeleeHit, request.closeDefensePrecheck);
            return request;
        }

        internal static bool OwnsActiveMelee(Verb verb)
            => bindings.TryGetValue(verb, out VerbBinding binding)
                && binding.request?.Executing == true;

        internal void FinishNativeCast(Exception exception)
        {
            bool acted = exception == null && RimKataFireContext.ShotFired;
            RimKataFireContext.End(verb, previousContext);
            Detach();
            // Native effecters may still read CurrentTarget after WarmupComplete.
            // Successful native casts already finish Idle; retain their own state.
            if (exception != null) verb.Reset();
            nonInterruptingSelfCast(verb) = false;

            if (pawn.stances?.curStance is Stance_Busy busy && busy.verb == verb)
            {
                if (previousAim != null)
                {
                    pawn.stances.curStance = previousAim;
                }
                else
                {
                    RimKataAutomaticCastSuppressionState suppression = RimKataAutomaticCastSuppression.Push(pawn);
                    try { pawn.stances.SetStance(new Stance_Mobile()); }
                    finally { RimKataAutomaticCastSuppression.Pop(suppression); }
                }
            }
            previousAim = null;
            try
            {
                RimKataDualWeaponController.CompleteNativeAttack(this, acted, cancelled);
            }
            finally
            {
                if (cancelled || cycle.boundVerb != verb)
                {
                    RimKataPreparedWeaponData.Restore(verb);
                    bindingVerb = null;
                    nativeBinding = null;
                }
                ReleaseReferences();
            }
        }

        internal void Cancel()
        {
            if (!Pending) return;
            cancelled = true;
            // Damage may reset a cycle while its native attack is still unwinding.
            // Let that cast finish; never reset a live native call from its callback.
            if (Executing) return;
            Verb pendingVerb = verb;
            Detach();
            pendingVerb.Reset();
            nonInterruptingSelfCast(pendingVerb) = false;
            ReleaseReferences();
        }

        internal void ForgetBinding()
        {
            Cancel();
            if (Executing) return;
            bindingVerb = null;
            nativeBinding = null;
        }

        internal static void NotifyReset(Verb verb)
        {
            if (bindings.TryGetValue(verb, out VerbBinding binding)
                && binding.request is RimKataNativeAttack request && !request.Executing)
            {
                request.Detach();
                nonInterruptingSelfCast(verb) = false;
                request.ReleaseReferences();
            }
        }

        internal static void NotifyEquipmentLost(Verb verb)
        {
            if (bindings.TryGetValue(verb, out VerbBinding binding)
                && binding.request is RimKataNativeAttack request)
            {
                request.Cancel();
                if (request.Executing) return;
            }
            RimKataPreparedWeaponData.Restore(verb);
        }

        internal static void ExposeData(Verb verb)
        {
            bool queued = WaitingForNativeTick(verb);
            Scribe_Values.Look(ref queued, "rimKataQueuedCast");
            if (Scribe.mode == LoadSaveMode.LoadingVars && queued)
                bindings.GetOrCreateValue(verb).cancelAfterLoad = true;
            if (Scribe.mode == LoadSaveMode.PostLoadInit
                && bindings.TryGetValue(verb, out VerbBinding binding)
                && binding.cancelAfterLoad)
            {
                binding.cancelAfterLoad = false;
                verb.Reset();
            }
        }

        private void Detach()
        {
            if (verb != null && bindings.TryGetValue(verb, out VerbBinding binding)
                && binding.request == this) binding.request = null;
            Pending = false;
            Executing = false;
        }

        private void ReleaseReferences()
        {
            pawn = null;
            state = null;
            cycle = null;
            weapon = null;
            verb = null;
            cycleVerb = null;
            job = null;
            assignedTarget = null;
            firedTarget = null;
            target = LocalTargetInfo.Invalid;
            interceptionTarget = null;
            previousContext = default;
        }

        internal void ClearCompletedReferences() => ReleaseReferences();
    }

    [HarmonyPatch(typeof(Verb), nameof(Verb.VerbTick))]
    internal static class Patch_VerbTick_RimKataNativeAttack
    {
        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            MethodInfo warmup = AccessTools.Method(typeof(Verb), nameof(Verb.WarmupComplete));
            MethodInfo ready = AccessTools.Method(typeof(RimKataNativeAttack), nameof(RimKataNativeAttack.CanBeginNativeTick));
            Label regular = generator.DefineLabel();
            // Keep the virtual native call inside native VerbTick. Waiting stays
            // Idle until native initialization (beam paths are not ready earlier).
            // Ordinary weapons branch directly to their original IL: no RimKata
            // method, eligibility check or request-table lookup on their ticks.
            yield return new CodeInstruction(OpCodes.Ldarg_0);
            yield return new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(Verb), nameof(Verb.verbProps)));
            yield return new CodeInstruction(OpCodes.Isinst, typeof(RimKataPreparedVerbProperties));
            yield return new CodeInstruction(OpCodes.Brfalse, regular);
            yield return new CodeInstruction(OpCodes.Ldarg_0);
            yield return new CodeInstruction(OpCodes.Call, ready);
            yield return new CodeInstruction(OpCodes.Brfalse, regular);
            yield return new CodeInstruction(OpCodes.Ldarg_0);
            yield return new CodeInstruction(OpCodes.Callvirt, warmup);
            CodeInstruction originalEntry = new CodeInstruction(OpCodes.Nop);
            originalEntry.labels.Add(regular);
            yield return originalEntry;
            foreach (CodeInstruction instruction in instructions) yield return instruction;
        }
    }

    [HarmonyPatch(typeof(Verb), nameof(Verb.Reset))]
    internal static class Patch_VerbReset_RimKataNativeAttack
    {
        private static void Prefix(Verb __instance) => RimKataNativeAttack.NotifyReset(__instance);
    }

    [HarmonyPatch(typeof(Verb), nameof(Verb.ExposeData))]
    internal static class Patch_VerbExposeData_RimKataNativeAttack
    {
        private static void Postfix(Verb __instance)
        {
            // Cast context is transient. On load, the saved weapon-cycle plan retries
            // instead of letting a pending native burst fire without its Touch context.
            RimKataNativeAttack.ExposeData(__instance);
        }
    }

    [HarmonyPatch(typeof(Verb), nameof(Verb.Notify_EquipmentLost))]
    internal static class Patch_VerbEquipmentLost_RimKataNativeAttack
    {
        private static void Prefix(Verb __instance) => RimKataNativeAttack.NotifyEquipmentLost(__instance);
    }
}
