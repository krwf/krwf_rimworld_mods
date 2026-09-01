using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using Verse;

namespace KRWF.RimKata
{
    public struct RimKataAutomaticCastSuppressionState
    {
        internal bool pushed;
        internal Pawn previousPawn;
        internal int previousDepth;
    }

    public static class RimKataAutomaticCastSuppression
    {
        [ThreadStatic] private static Pawn activePawn;
        [ThreadStatic] private static int depth;

        public static bool ActiveFor(Pawn pawn)
        {
            return pawn != null && depth > 0 && activePawn == pawn;
        }

        public static RimKataAutomaticCastSuppressionState Push(Pawn pawn)
        {
            RimKataAutomaticCastSuppressionState state =
                new RimKataAutomaticCastSuppressionState
                {
                    pushed = pawn != null,
                    previousPawn = activePawn,
                    previousDepth = depth
                };
            if (pawn != null)
            {
                activePawn = pawn;
                depth++;
            }

            return state;
        }

        public static void Pop(RimKataAutomaticCastSuppressionState state)
        {
            if (!state.pushed)
            {
                return;
            }

            activePawn = state.previousPawn;
            depth = state.previousDepth;
        }
    }

    public struct RimKataVanillaSingleShotContextState
    {
        internal bool pushed;
        internal Verb previousVerb;
        internal int previousDepth;
    }

    public static class RimKataVanillaSingleShotContext
    {
        [ThreadStatic] private static Verb activeVerb;
        [ThreadStatic] private static int depth;

        public static bool ActiveFor(Verb verb)
        {
            return depth > 0 && verb != null && activeVerb == verb;
        }

        public static RimKataVanillaSingleShotContextState Push(Verb verb)
        {
            RimKataVanillaSingleShotContextState state =
                new RimKataVanillaSingleShotContextState
                {
                    pushed = verb != null,
                    previousVerb = activeVerb,
                    previousDepth = depth
                };
            if (verb != null)
            {
                activeVerb = verb;
                depth++;
            }

            return state;
        }

        public static void Pop(RimKataVanillaSingleShotContextState state)
        {
            if (!state.pushed)
            {
                return;
            }

            activeVerb = state.previousVerb;
            depth = state.previousDepth;
        }
    }

    public enum RimKataCloseDefensePrecheck
    {
        None,
        FirstDodgeSucceeded,
        FirstDodgeAndResponseFailed,
        ResponseSucceeded,
        ResponseSucceededWithAccidentalShot
    }

    public static class RimKataInterceptionShotRegistry
    {
        private sealed class Entry
        {
            public Pawn shooter;
            public Projectile targetProjectile;
        }

        private static readonly ConditionalWeakTable<Projectile, Entry> entries = new ConditionalWeakTable<Projectile, Entry>();

        public static void Register(
            Projectile shot,
            Pawn shooter,
            Projectile targetProjectile)
        {
            if (shot == null
                || shooter == null
                || targetProjectile == null)
            {
                return;
            }

            entries.Remove(shot);
            entries.Add(shot, new Entry{shooter = shooter, targetProjectile = targetProjectile});
        }

        public static bool TryResolve(Projectile shot)
        {
            if (shot == null
                || !entries.TryGetValue(shot, out Entry entry))
            {
                return false;
            }

            entries.Remove(shot);

            Projectile target = entry.targetProjectile;
            if (entry.shooter?.Map == null
                || target?.Map != entry.shooter.Map
                || !RimKataTargeting.IsInterceptionTargetActive(target))
            {
                return false;
            }

            return RimKataInterceptionUtility.Resolve(entry.shooter, target);
        }
    }

    public static class RimKataFireContext
    {
        private struct PendingCloseImpact
        {
            public Projectile projectile;
            public LocalTargetInfo usedTarget;
        }

        public struct ScopeState
        {
            private List<PendingCloseImpact> pendingImpacts;
            private Verb activeVerb;
            private Pawn shooter;
            private Thing closeTarget;
            private bool movingShot;
            private bool closeShot;
            private bool closeMeleeResolution;
            private bool closeMeleeHit;
            private bool interceptionShot;
            private Projectile interceptionTarget;
            private int originalBurstCount;
            private bool shotFired;
            private bool computingMovingFinalChance;
            private bool suppressCloseLaunch;
            private RimKataCloseDefensePrecheck closeDefensePrecheck;
            private RimKataDefenseUtility.CloseAttackResolutionState defenseState;

            internal static ScopeState Capture()
            {
                return new ScopeState
                {
                    pendingImpacts = pendingCloseImpacts,
                    activeVerb = ActiveVerb,
                    shooter = Shooter,
                    closeTarget = CloseTarget,
                    movingShot = MovingShot,
                    closeShot = CloseShot,
                    closeMeleeResolution = CloseMeleeResolution,
                    closeMeleeHit = CloseMeleeHit,
                    interceptionShot = InterceptionShot,
                    interceptionTarget = InterceptionTarget,
                    originalBurstCount = OriginalBurstCount,
                    shotFired = ShotFired,
                    computingMovingFinalChance = ComputingMovingFinalChance,
                    suppressCloseLaunch = SuppressCloseLaunch,
                    closeDefensePrecheck = CloseDefensePrecheck,
                    defenseState = RimKataDefenseUtility.PushCloseAttackResolution()
                };
            }

            internal void Restore()
            {
                pendingCloseImpacts = pendingImpacts;
                ActiveVerb = activeVerb;
                Shooter = shooter;
                CloseTarget = closeTarget;
                MovingShot = movingShot;
                CloseShot = closeShot;
                CloseMeleeResolution = closeMeleeResolution;
                CloseMeleeHit = closeMeleeHit;
                InterceptionShot = interceptionShot;
                InterceptionTarget = interceptionTarget;
                OriginalBurstCount = originalBurstCount;
                ShotFired = shotFired;
                ComputingMovingFinalChance = computingMovingFinalChance;
                SuppressCloseLaunch = suppressCloseLaunch;
                CloseDefensePrecheck = closeDefensePrecheck;
                RimKataDefenseUtility.PopCloseAttackResolution(defenseState);
            }
        }

        [ThreadStatic] private static List<PendingCloseImpact> pendingCloseImpacts;
        [ThreadStatic] public static Verb ActiveVerb;
        [ThreadStatic] public static Pawn Shooter;
        [ThreadStatic] public static Thing CloseTarget;
        [ThreadStatic] public static bool MovingShot;
        [ThreadStatic] public static bool CloseShot;
        [ThreadStatic] public static bool CloseMeleeResolution;
        [ThreadStatic] public static bool CloseMeleeHit;
        [ThreadStatic] public static bool InterceptionShot;
        [ThreadStatic] public static Projectile InterceptionTarget;
        [ThreadStatic] public static int OriginalBurstCount;
        [ThreadStatic] public static bool ShotFired;
        [ThreadStatic] public static bool ComputingMovingFinalChance;
        [ThreadStatic] public static bool SuppressCloseLaunch;
        [ThreadStatic] public static RimKataCloseDefensePrecheck CloseDefensePrecheck;

        public static ScopeState Begin(
            Verb verb,
            Pawn shooter,
            Thing closeTarget,
            bool movingShot,
            bool closeShot,
            bool interceptionShot,
            Projectile interceptionTarget,
            bool closeMeleeResolution,
            bool closeMeleeHit,
            RimKataCloseDefensePrecheck closeDefensePrecheck)
        {
            int nextOriginalBurstCount = ActiveVerb == verb
                ? OriginalBurstCount
                : Mathf.Max(1, verb?.BurstShotCount ?? 1);
            ScopeState previous = ScopeState.Capture();
            pendingCloseImpacts = null;
            OriginalBurstCount = Mathf.Max(1, nextOriginalBurstCount);
            ActiveVerb = verb;
            Shooter = shooter;
            CloseTarget = closeTarget;
            MovingShot = movingShot;
            CloseShot = closeShot;
            CloseMeleeResolution = closeMeleeResolution;
            CloseMeleeHit = closeMeleeHit;
            InterceptionShot = interceptionShot;
            InterceptionTarget = interceptionTarget;
            ShotFired = false;
            ComputingMovingFinalChance = false;
            SuppressCloseLaunch = false;
            CloseDefensePrecheck = closeDefensePrecheck;
            return previous;
        }

        public static void QueueCloseImpact(Projectile projectile, LocalTargetInfo usedTarget)
        {
            pendingCloseImpacts ??= new List<PendingCloseImpact>();
            pendingCloseImpacts.Add(new PendingCloseImpact
            {
                projectile = projectile,
                usedTarget = usedTarget
            });
        }

        public static void FlushPendingCloseImpacts()
        {
            while (pendingCloseImpacts != null && pendingCloseImpacts.Count > 0)
            {
                PendingCloseImpact pending = pendingCloseImpacts[0];
                pendingCloseImpacts.RemoveAt(0);
                RimKataProjectileUtility.ResolveCloseImpact(pending.projectile, pending.usedTarget);
            }
        }

        public static void End(Verb ownerVerb, ScopeState previous)
        {
            if (ownerVerb == null || ActiveVerb != ownerVerb)
            {
                return;
            }

            DiscardPendingCloseImpacts();
            previous.Restore();
        }

        private static void DiscardPendingCloseImpacts()
        {
            if (pendingCloseImpacts == null)
            {
                return;
            }

            for (int i = 0; i < pendingCloseImpacts.Count; i++)
            {
                Projectile projectile = pendingCloseImpacts[i].projectile;
                if (projectile != null && !projectile.Destroyed)
                {
                    projectile.Destroy();
                }
            }

            pendingCloseImpacts.Clear();
        }
    }

    public static class RimKataProjectileImpactContext
    {
        private struct ImpactScope
        {
            public Projectile previousProjectile;
            public Map impactMap;
        }

        [ThreadStatic] private static Stack<ImpactScope> projectileStack;
        [ThreadStatic] public static Projectile CurrentProjectile;

        public static void Enter(Projectile projectile)
        {
            projectileStack ??= new Stack<ImpactScope>();
            projectileStack.Push(new ImpactScope
            {
                previousProjectile = CurrentProjectile,
                impactMap = projectile?.Map
            });
            CurrentProjectile = projectile;
            RimKataDefenseUtility.EnterProjectileImpact();
        }

        public static void Exit()
        {
            RimKataDefenseUtility.ExitProjectileImpact();
            Projectile exitingProjectile = CurrentProjectile;
            ImpactScope scope = projectileStack != null
                && projectileStack.Count > 0
                    ? projectileStack.Pop()
                    : default(ImpactScope);
            CurrentProjectile = scope.previousProjectile;

            // Projectile.Impact can be entered once by an override and again
            // by its base implementation.  Keep the tracked result until the
            // outermost scope for that projectile has completed; the
            // projectile may already be despawned before damage is applied.
            if (exitingProjectile != null
                && exitingProjectile != CurrentProjectile)
            {
                scope.impactMap?.GetComponent<RimKataMapComponent>()?
                    .NotifyRangedProjectileImpactFinished(exitingProjectile);
            }
        }
    }

    public static class RimKataVerbUtility
    {
        private static readonly FieldInfo CurrentTargetField = AccessTools.Field(typeof(Verb), "currentTarget");
        private static readonly FieldInfo CurrentDestinationField = AccessTools.Field(typeof(Verb), "currentDestination");
        private static readonly FieldInfo SurpriseAttackField = AccessTools.Field(typeof(Verb), "surpriseAttack");
        private static readonly FieldInfo CanHitNonTargetPawnsField = AccessTools.Field(typeof(Verb), "canHitNonTargetPawnsNow");
        private static readonly FieldInfo PreventFriendlyFireField = AccessTools.Field(typeof(Verb), "preventFriendlyFire");
        private static readonly FieldInfo NonInterruptingSelfCastField = AccessTools.Field(typeof(Verb), "nonInterruptingSelfCast");

        public static bool FireSingleShot(
            Verb verb,
            LocalTargetInfo target,
            bool movingShot,
            bool closeShot,
            bool interceptionShot = false,
            bool closeMeleeResolution = false,
            bool closeMeleeHit = false,
            RimKataCloseDefensePrecheck closeDefensePrecheck = RimKataCloseDefensePrecheck.None,
            Projectile interceptionTarget = null)
        {
            Pawn pawn = verb?.CasterPawn;
            bool available = verb != null
                && (closeShot && !verb.IsMeleeAttack
                    ? RimKataEligibility.IsRangedVerbAvailableInCloseCombat(pawn, verb)
                    : verb.Available());
            if (pawn == null || !pawn.Spawned || !target.IsValid || !available)
            {
                return false;
            }

            if (!closeShot && !verb.CanHitTarget(target))
            {
                return false;
            }

            if (verb.state != VerbState.Idle)
            {
                return false;
            }

            object oldCurrentTarget = CurrentTargetField.GetValue(verb);
            object oldCurrentDestination = CurrentDestinationField.GetValue(verb);
            object oldSurpriseAttack = SurpriseAttackField.GetValue(verb);
            object oldCanHitNonTargetPawns = CanHitNonTargetPawnsField.GetValue(verb);
            object oldPreventFriendlyFire = PreventFriendlyFireField.GetValue(verb);
            object oldNonInterruptingSelfCast = NonInterruptingSelfCastField.GetValue(verb);

            LocalTargetInfo castTarget = target;
            if (closeShot
                && closeMeleeResolution
                && !closeMeleeHit
                && !(verb is Verb_LaunchProjectile)
                && target.HasThing)
            {
                IntVec3 missCell = RimKataProjectileUtility.FindCloseMissCell(pawn, target.Thing, pawn.Map);
                if (missCell.IsValid)
                {
                    castTarget = new LocalTargetInfo(missCell);
                }
            }

            pawn.rotationTracker.FaceCell(castTarget.Cell);
            verb.Reset();
            CurrentTargetField.SetValue(verb, castTarget);
            CurrentDestinationField.SetValue(verb, LocalTargetInfo.Invalid);
            SurpriseAttackField.SetValue(verb, false);
            CanHitNonTargetPawnsField.SetValue(verb, true);
            PreventFriendlyFireField.SetValue(verb, false);
            NonInterruptingSelfCastField.SetValue(verb, true);

            RimKataFireContext.ScopeState fireContext =
                RimKataFireContext.Begin(
                    verb,
                    pawn,
                    closeShot ? target.Thing : null,
                    movingShot,
                    closeShot,
                    interceptionShot,
                    interceptionTarget,
                    closeMeleeResolution,
                    closeMeleeHit,
                    closeDefensePrecheck);
            try
            {
                verb.WarmupComplete();
                RimKataFireContext.FlushPendingCloseImpacts();
                return RimKataFireContext.ShotFired;
            }
            catch (Exception exception)
            {
                Log.Error("[RimKata] Failed to fire a single shot: " + exception);
                return false;
            }
            finally
            {
                Stance_Busy ownedBusy = pawn.stances?.curStance as Stance_Busy;
                if (ownedBusy?.verb != verb)
                {
                    ownedBusy = null;
                }

                verb.Reset();
                CurrentTargetField.SetValue(verb, oldCurrentTarget);
                CurrentDestinationField.SetValue(verb, oldCurrentDestination);
                SurpriseAttackField.SetValue(verb, oldSurpriseAttack);
                CanHitNonTargetPawnsField.SetValue(verb, oldCanHitNonTargetPawns);
                PreventFriendlyFireField.SetValue(verb, oldPreventFriendlyFire);
                NonInterruptingSelfCastField.SetValue(verb, oldNonInterruptingSelfCast);

                RimKataFireContext.End(verb, fireContext);

                if (ownedBusy != null
                    && pawn.stances?.curStance == ownedBusy)
                {
                    RimKataAutomaticCastSuppressionState suppression =
                        RimKataAutomaticCastSuppression.Push(pawn);
                    try
                    {
                        pawn.stances.SetStance(new Stance_Mobile());
                    }
                    finally
                    {
                        RimKataAutomaticCastSuppression.Pop(suppression);
                    }
                }
            }
        }
    }

    public static class RimKataProjectileUtility
    {
        private static readonly FieldInfo DestinationField = AccessTools.Field(typeof(Projectile), "destination");
        private static readonly FieldInfo TicksToImpactField = AccessTools.Field(typeof(Projectile), "ticksToImpact");
        private static readonly FieldInfo LifetimeField = AccessTools.Field(typeof(Projectile), "lifetime");
        private static readonly FieldInfo LandedField = AccessTools.Field(typeof(Projectile), "landed");
        private static readonly Dictionary<Type, MethodInfo> ImpactMethods = new Dictionary<Type, MethodInfo>();
        private static readonly Dictionary<Type, MethodInfo> ExplodeMethods = new Dictionary<Type, MethodInfo>();

        public static void Impact(Projectile projectile, Thing hitThing, bool blockedByShield = false)
        {
            if (projectile == null || projectile.Destroyed)
            {
                return;
            }

            MethodInfo method = MethodFor(ImpactMethods, projectile.GetType(), "Impact", typeof(Thing), typeof(bool));
            method?.Invoke(projectile, new object[] { hitThing, blockedByShield });
        }

        public static void DetonateNow(Projectile projectile)
        {
            if (projectile == null || projectile.Destroyed)
            {
                return;
            }

            MethodInfo method = MethodFor(ExplodeMethods, projectile.GetType(), "Explode");
            if (method != null)
            {
                method.Invoke(projectile, null);
            }
            else
            {
                Impact(projectile, null);
            }
        }

        public static bool PrepareImmediateImpact(Projectile projectile, IntVec3 cell)
        {
            if (projectile == null
                || projectile.Destroyed
                || projectile.Map == null
                || !cell.InBounds(projectile.Map))
            {
                return false;
            }

            projectile.Position = cell;
            DestinationField.SetValue(projectile, cell.ToVector3Shifted());
            TicksToImpactField.SetValue(projectile, 0);
            LifetimeField.SetValue(projectile, 0);
            LandedField.SetValue(projectile, false);
            return true;
        }

        public static IntVec3 FindCloseMissCell(Pawn shooter, Thing target, Map map)
        {
            if (target == null || map == null)
            {
                return IntVec3.Invalid;
            }

            int start = Rand.Range(0, GenAdj.AdjacentCells.Length);
            for (int i = 0; i < GenAdj.AdjacentCells.Length; i++)
            {
                IntVec3 cell = target.Position + GenAdj.AdjacentCells[(start + i) % GenAdj.AdjacentCells.Length];
                if (cell.InBounds(map)
                    && cell != target.Position
                    && (shooter == null || cell != shooter.Position))
                {
                    return cell;
                }
            }

            return target.Position;
        }

        public static void ResolveCloseImpact(Projectile projectile, LocalTargetInfo usedTarget)
        {
            Thing target = RimKataFireContext.CloseTarget;
            if (projectile == null
                || projectile.Destroyed
                || target == null
                || !target.Spawned
                || target.Map != projectile.Map)
            {
                if (projectile != null && !projectile.Destroyed)
                {
                    projectile.Destroy();
                }

                return;
            }

            bool meleeResolution = RimKataFireContext.CloseMeleeResolution;
            bool meleeHit = !meleeResolution || RimKataFireContext.CloseMeleeHit;
            IntVec3 impactCell = meleeHit
                ? target.Position
                : FindCloseMissCell(RimKataFireContext.Shooter, target, projectile.Map);
            if (!PrepareImmediateImpact(projectile, impactCell))
            {
                projectile.Destroy();
                return;
            }

            Thing impactThing;
            if (meleeResolution)
            {
                impactThing = meleeHit && target.Spawned && target.Map == projectile.Map
                    ? target
                    : null;
            }
            else
            {
                impactThing = usedTarget.HasThing
                    && usedTarget.Thing.Spawned
                    && usedTarget.Thing.Map == projectile.Map
                        ? usedTarget.Thing
                        : null;
            }

            Impact(projectile, impactThing);
        }

        public static void SpawnDeflectedMiss(Projectile source, Pawn attacker, Pawn defender, Verb sourceVerb)
        {
            if (source == null || attacker?.Map == null || defender?.Map != attacker.Map)
            {
                return;
            }

            Map map = attacker.Map;
            IntVec3 missCell = FindMissCell(attacker.Position, defender.Position, map);
            if (!missCell.IsValid || missCell == defender.Position)
            {
                return;
            }

            Projectile redirected = ThingMaker.MakeThing(source.def) as Projectile;
            if (redirected == null)
            {
                return;
            }

            GenSpawn.Spawn(redirected, attacker.Position, map);
            redirected.damageDefOverride = source.damageDefOverride;
            if (source.extraDamages != null)
            {
                redirected.extraDamages = new List<ExtraDamage>(source.extraDamages);
            }

            ProjectileHitFlags flags = ProjectileHitFlags.NonTargetPawns | ProjectileHitFlags.NonTargetWorld;
            RimKataFireContext.SuppressCloseLaunch = true;
            try
            {
                redirected.Launch(attacker, attacker.DrawPos, missCell, defender, flags, false, sourceVerb?.EquipmentSource);
                redirected.stoppingPower = source.stoppingPower;
            }
            finally
            {
                RimKataFireContext.SuppressCloseLaunch = false;
            }
        }

        private static IntVec3 FindMissCell(IntVec3 attacker, IntVec3 defender, Map map)
        {
            Vector3 forward = (defender - attacker).ToVector3();
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.01f)
            {
                forward = Vector3.forward;
            }

            forward.Normalize();
            Vector3 side = new Vector3(-forward.z, 0f, forward.x) * (Rand.Bool ? 2f : -2f);
            for (int distance = 8; distance >= 2; distance--)
            {
                IntVec3 cell = (defender.ToVector3Shifted() + forward * distance + side).ToIntVec3();
                if (cell.InBounds(map))
                {
                    return cell;
                }
            }

            return IntVec3.Invalid;
        }

        private static MethodInfo MethodFor(Dictionary<Type, MethodInfo> cache, Type type, string name, params Type[] parameters)
        {
            if (!cache.TryGetValue(type, out MethodInfo method))
            {
                Type current = type;
                while (current != null && method == null)
                {
                    method = current.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, parameters, null);
                    current = current.BaseType;
                }

                cache[type] = method;
            }

            return method;
        }
    }


    [HarmonyPatch]
    public static class Patch_Verb_TryCastShot_RimKata
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            HashSet<MethodBase> methods = new HashSet<MethodBase>();
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic)
                {
                    continue;
                }

                IEnumerable<Type> types;
                try
                {
                    types = AccessTools.GetTypesFromAssembly(assembly);
                }
                catch
                {
                    continue;
                }

                foreach (Type type in types)
                {
                    if (typeof(Verb).IsAssignableFrom(type))
                    {
                        MethodInfo method = type.GetMethod(
                            "TryCastShot",
                            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                            null,
                            Type.EmptyTypes,
                            null);
                        if (method != null
                            && !method.IsAbstract
                            && method.ReturnType == typeof(bool)
                            && methods.Add(method))
                        {
                            yield return method;
                        }
                    }
                }
            }
        }

        public static void Postfix(Verb __instance, bool __result)
        {
            if (RimKataFireContext.ActiveVerb == __instance
                && (__result || __instance.IsMeleeAttack))
            {
                RimKataFireContext.ShotFired = true;
            }
        }
    }

    [HarmonyPatch(typeof(Verb), nameof(Verb.BurstShotCount), MethodType.Getter)]
    public static class Patch_Verb_BurstShotCount_RimKata
    {
        public static void Postfix(Verb __instance, ref int __result)
        {
            if (RimKataFireContext.ActiveVerb == __instance
                || RimKataVanillaSingleShotContext.ActiveFor(__instance))
            {
                __result = 1;
            }
        }
    }

    [HarmonyPatch]
    public static class Patch_Verb_WarmupComplete_RimKataOpeningSingleShot
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            HashSet<MethodBase> methods = new HashSet<MethodBase>();
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic)
                {
                    continue;
                }

                IEnumerable<Type> types;
                try
                {
                    types = AccessTools.GetTypesFromAssembly(assembly);
                }
                catch
                {
                    continue;
                }

                foreach (Type type in types)
                {
                    if (type == null
                        || !typeof(Verb).IsAssignableFrom(type))
                    {
                        continue;
                    }

                    MethodInfo method = type.GetMethod(
                        nameof(Verb.WarmupComplete),
                        BindingFlags.Instance
                            | BindingFlags.Public
                            | BindingFlags.NonPublic
                            | BindingFlags.DeclaredOnly,
                        null,
                        Type.EmptyTypes,
                        null);
                    if (method != null
                        && !method.IsAbstract
                        && !method.ContainsGenericParameters
                        && method.ReturnType == typeof(void)
                        && methods.Add(method))
                    {
                        yield return method;
                    }
                }
            }
        }

        public static void Prefix(
            Verb __instance,
            out RimKataVanillaSingleShotContextState __state)
        {
            __state = default(RimKataVanillaSingleShotContextState);
            if (RimKataDualWeaponController
                .ShouldConvertVanillaOpeningToSingleShot(__instance))
            {
                __state = RimKataVanillaSingleShotContext.Push(__instance);
            }
        }

        public static Exception Finalizer(
            Exception __exception,
            RimKataVanillaSingleShotContextState __state)
        {
            RimKataVanillaSingleShotContext.Pop(__state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(VerbProperties), nameof(VerbProperties.AdjustedFullCycleTime))]
    public static class Patch_VerbProperties_RimKataShootingExperience
    {
        public static void Postfix(Verb ownerVerb, ref float __result)
        {
            if (RimKataFireContext.ActiveVerb != ownerVerb
                || RimKataFireContext.OriginalBurstCount <= 1)
            {
                return;
            }

            int burstCount = RimKataFireContext.OriginalBurstCount;
            float originalBurstSpacing = (burstCount - 1) * ownerVerb.TicksBetweenBurstShots / 60f;
            __result = (__result + originalBurstSpacing) / burstCount;
        }
    }

    [HarmonyPatch(typeof(Verb), nameof(Verb.TryFindShootLineFromTo))]
    public static class Patch_Verb_CloseRimKataShootLine
    {
        public static bool Prefix(
            Verb __instance,
            IntVec3 root,
            LocalTargetInfo targ,
            ref ShootLine resultingLine,
            ref bool __result)
        {
            Thing target = RimKataFireContext.CloseTarget;
            if (!RimKataFireContext.CloseShot
                || RimKataFireContext.ActiveVerb != __instance
                || target == null
                || !target.Spawned)
            {
                return true;
            }

            resultingLine = new ShootLine(root, targ.Cell);
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(ShotReport), nameof(ShotReport.AimOnTargetChance_IgnoringPosture), MethodType.Getter)]
    public static class Patch_ShotReport_MovingAccuracy_RimKata
    {
        public static void Postfix(ShotReport __instance, ref float __result)
        {
            if (RimKataFireContext.ActiveVerb != null && RimKataFireContext.CloseMeleeResolution)
            {
                __result = RimKataFireContext.CloseMeleeHit ? 1f : 0f;
                return;
            }

            if (RimKataFireContext.ActiveVerb != null
                && RimKataFireContext.MovingShot
                && RimKataCombatMath.MovingAccuracyIsModified(RimKataFireContext.Shooter))
            {
                RimKataFireContext.ComputingMovingFinalChance = true;
                float coverChance;
                try
                {
                    coverChance = __instance.PassCoverChance;
                }
                finally
                {
                    RimKataFireContext.ComputingMovingFinalChance = false;
                }

                __result = RimKataCombatMath.MovingHitChance(RimKataFireContext.Shooter, __result * coverChance);
            }
        }
    }

    [HarmonyPatch(typeof(ShotReport), nameof(ShotReport.PassCoverChance), MethodType.Getter)]
    public static class Patch_ShotReport_MovingCoverRoll_RimKata
    {
        public static void Postfix(ref float __result)
        {
            if (RimKataFireContext.ActiveVerb != null && RimKataFireContext.CloseMeleeResolution)
            {
                __result = 1f;
                return;
            }

            if (RimKataFireContext.ActiveVerb != null
                && RimKataFireContext.MovingShot
                && RimKataCombatMath.MovingAccuracyIsModified(RimKataFireContext.Shooter)
                && !RimKataFireContext.ComputingMovingFinalChance)
            {
                __result = 1f;
            }
        }
    }

    [HarmonyPatch(typeof(VerbProperties), nameof(VerbProperties.ForcedMissRadius), MethodType.Getter)]
    public static class Patch_VerbProperties_CloseMeleeForcedMiss_RimKata
    {
        public static void Postfix(VerbProperties __instance, ref float __result)
        {
            if (RimKataFireContext.ActiveVerb?.verbProps == __instance && RimKataFireContext.CloseMeleeResolution)
            {
                __result = 0f;
            }
        }
    }

    [HarmonyPatch]
    public static class Patch_Projectile_Launch_CloseRimKata
    {
        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(Projectile),
                nameof(Projectile.Launch),
                new[]
                {
                    typeof(Thing), 
                    typeof(Vector3), 
                    typeof(LocalTargetInfo), 
                    typeof(LocalTargetInfo),
                    typeof(ProjectileHitFlags), 
                    typeof(bool), typeof(Thing), 
                    typeof(ThingDef)
                });
        }

        public static void Postfix(
            Projectile __instance,
            Thing launcher,
            Thing equipment,
            LocalTargetInfo usedTarget,
            LocalTargetInfo intendedTarget)
        {
            Map launchMap = __instance?.Map ?? launcher?.Map;
            RimKataMapComponent component =
                launchMap?.GetComponent<RimKataMapComponent>();
            component?.RegisterLaunchedExplosiveProjectile(__instance);

            Pawn usedPawn = usedTarget.Pawn;
            Pawn intendedPawn = intendedTarget.Pawn;
            if (usedPawn != null
                && usedPawn == intendedPawn
                && IsTrackableOrdinaryRangedProjectile(__instance)
                && RimKataEligibility.HasRimKataAccess(usedPawn)
                && launcher != usedPawn
                && (launcher?.Faction == null
                    || usedPawn.Faction == null
                    || launcher.Faction != usedPawn.Faction))
            {
                component?.RegisterLaunchedRangedProjectile(
                    __instance,
                    usedPawn);
            }

            if (launcher != RimKataFireContext.Shooter
                || equipment != RimKataFireContext.ActiveVerb?.EquipmentSource)
            {
                return;
            }

            if (RimKataFireContext.InterceptionShot
                && !__instance.Destroyed)
            {
                RimKataInterceptionShotRegistry.Register(
                    __instance,
                    RimKataFireContext.Shooter,
                    RimKataFireContext.InterceptionTarget);

                return;
            }

            if (!RimKataFireContext.CloseShot
                || RimKataFireContext.SuppressCloseLaunch
                || !IntendedForCloseTarget(intendedTarget)
                || __instance.Destroyed)
            {
                return;
            }

            RimKataFireContext.QueueCloseImpact(__instance, usedTarget);
        }

        private static bool IsTrackableOrdinaryRangedProjectile(
            Projectile projectile)
        {
            if (projectile == null
                || projectile.Destroyed
                || projectile.def?.projectile == null
                || projectile.def.projectile.explosionRadius > 0f)
            {
                return false;
            }

            DamageDef damageDef = projectile.damageDefOverride
                ?? projectile.def.projectile.damageDef;
            return damageDef?.isRanged == true && !damageDef.isExplosive;
        }

        private static bool IntendedForCloseTarget(LocalTargetInfo intendedTarget)
        {
            Thing closeTarget = RimKataFireContext.CloseTarget;
            if (closeTarget == null)
            {
                return false;
            }

            return intendedTarget.HasThing
                ? intendedTarget.Thing == closeTarget
                : intendedTarget.IsValid && intendedTarget.Cell == closeTarget.Position;
        }
    }

    public static class Patch_Projectile_Impact_Context
    {
        public static void Apply(Harmony harmony)
        {
            if (harmony == null)
            {
                throw new ArgumentNullException(nameof(harmony));
            }

            MethodInfo prefixMethod = AccessTools.Method(
                typeof(Patch_Projectile_Impact_Context),
                nameof(Prefix),
                new[] { typeof(Projectile) });
            MethodInfo finalizerMethod = AccessTools.Method(
                typeof(Patch_Projectile_Impact_Context),
                nameof(Finalizer),
                new[] { typeof(Exception) });
            if (prefixMethod == null || finalizerMethod == null)
            {
                throw new InvalidOperationException("Could not resolve Projectile.Impact context patch methods.");
            }

            foreach (MethodBase target in FindTargetMethods())
            {
                try
                {
                    Patches existing = Harmony.GetPatchInfo(target);
                    bool hasPrefix = HasPatch(existing?.Prefixes, harmony.Id, prefixMethod);
                    bool hasFinalizer = HasPatch(existing?.Finalizers, harmony.Id, finalizerMethod);
                    if (hasPrefix && hasFinalizer)
                    {
                        continue;
                    }

                    harmony.Patch(target, prefix: hasPrefix ? null : new HarmonyMethod(prefixMethod), finalizer: hasFinalizer ? null : new HarmonyMethod(finalizerMethod));
                }
                catch (Exception exception)
                {
                    Exception root = exception.GetBaseException();
                    string message = root.Message.NullOrEmpty()
                        ? "<no message>"
                        : root.Message.Replace('\r', ' ').Replace('\n', ' ');
                    Log.Warning("[RimKata] Skipped Projectile.Impact patch for " + (target.DeclaringType?.FullName ?? "<unknown>") + "::" + target.Name + ": " + root.GetType().Name + ": " + message);
                }
            }
        }

        private static IEnumerable<MethodBase> FindTargetMethods()
        {
            HashSet<MethodBase> methods = new HashSet<MethodBase>();
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic)
                {
                    continue;
                }

                IEnumerable<Type> types;
                try
                {
                    types = AccessTools.GetTypesFromAssembly(assembly);
                }
                catch
                {
                    continue;
                }

                foreach (Type type in types)
                {
                    if (type == null || !typeof(Projectile).IsAssignableFrom(type))
                    {
                        continue;
                    }

                    MethodInfo method;
                    try
                    {
                        method = type.GetMethod("Impact", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null,
                            new[] 
                            { 
                                typeof(Thing), 
                                typeof(bool) 
                            },
                            null);
                    }
                    catch
                    {
                        continue;
                    }

                    if (method != null && !method.IsAbstract && methods.Add(method))
                    {
                        yield return method;
                    }
                }
            }
        }

        private static bool HasPatch(
            IEnumerable<Patch> patches,
            string owner,
            MethodInfo patchMethod)
        {
            if (patches == null)
            {
                return false;
            }

            foreach (Patch patch in patches)
            {
                if (patch.owner == owner && patch.PatchMethod == patchMethod)
                {
                    return true;
                }
            }

            return false;
        }

        public static bool Prefix(Projectile __instance)
        {
            RimKataProjectileImpactContext.Enter(__instance);

            if (RimKataInterceptionShotRegistry.TryResolve(__instance))
            {
                if (!__instance.Destroyed)
                {
                    __instance.Destroy(DestroyMode.Vanish);
                }

                return false;
            }

            return true;
        }

        public static Exception Finalizer(Exception __exception)
        {
            RimKataProjectileImpactContext.Exit();
            return __exception;
        }
    }
}
