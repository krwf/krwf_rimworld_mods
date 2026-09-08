// Temporary diagnostics. Delete this file, rebuild the normal DLL and restart
// the game to remove every probe. No gameplay source, settings or save field
// depends on this file. All times describe qualified JobTracker entry trees.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace KRWF.RimKata
{
    internal static class RimKataPerformanceProbe
    {
        internal const double FrameThresholdMs = 1.0;
        private const string Prefix = "[RimKata.DraftedFireProbe] ";
        private static readonly double TickToMs = 1000.0 / Stopwatch.Frequency;
        private static readonly FrameData frame = new FrameData();
        private static readonly FrameData peak = new FrameData();
        private static readonly Dictionary<MethodBase, Part> parts =
            new Dictionary<MethodBase, Part>();
        private static Game game;
        private static int generation;
        private static int windowFrames;
        private static int windowHighFrames;
        private static long windowTicks;
        private static long lastReport;
        [ThreadStatic] private static Entry[] stack;
        [ThreadStatic] private static int depth;
        [ThreadStatic] private static int detailDepth;
        [ThreadStatic] private static int logDepth;
        [ThreadStatic] private static int injuryDepth;
        [ThreadStatic] private static int deathDepth;
        [ThreadStatic] private static int modDeathDepth;
        [ThreadStatic] private static DamageWorker.DamageResult lastDamageResult;
        [ThreadStatic] private static DamageContext lastDamage;

        internal enum Part
        {
            Entry, Prepare, Cycle, NestedJob, Close, Normalize, Search,
            Select, Reserve, Slot, Fire, CloseHit, Continuity, Aim, BattleLog,
            Available, CanHit, FireContext, ShotPrepare, Warmup, Burst, Cast,
            Sound, Stance, AutoAttack, DirectHit, MeleeDamage, Damage, DamageLog,
            Clamor, Stagger, ExtraDamage, ProjectilePlace, ProjectileImpact, DefenseNotify,
            DamagePre, Injury, DamagePost, LogText, InjuryAdd, HealthState,
            Death, DeathEffects, DeathModify, DeathThoughts, DeathDespawn,
            DeathCorpse, DeathPlace, ModDeath,
            Count
        }

        internal struct Scope
        {
            internal int depth;
            internal int generation;
        }

        internal struct HealthScope
        {
            internal Scope scope;
            internal Pawn pawn;
            internal byte before;
            internal int beforeCount;
            internal string hediffDef;
        }

        private struct Entry
        {
            internal Part part;
            internal long start;
            internal long children;
            internal int pawnId;
            internal string job;
            internal MethodBase method;
            internal ShotContext shot;
            internal DamageContext damage;
            internal int deathPawnId;
        }

        private struct ShotContext
        {
            internal int sequence, tick, pawnId, targetId, weaponId;
            internal string weaponDef;
            internal byte flags;
        }

        private struct DamageContext
        {
            internal int sequence, recipientId, instigatorId;
            internal string recipientDef, damageDef;
            internal ThingCategory recipientCategory;
            internal float incomingAmount;
        }

        private struct HealthSample
        {
            internal long self;
            internal int tick, pawnId, damageId, shotId, beforeCount, afterCount;
            internal byte before, after;
            internal string hediffDef;
            internal bool failed;
            internal bool recorded;
        }

        private sealed class FrameData
        {
            internal readonly long[] self = new long[(int)Part.Count];
            internal readonly long[] inclusive = new long[(int)Part.Count];
            internal readonly int[] calls = new int[(int)Part.Count];
            internal readonly HealthSample[] health = new HealthSample[2];
            internal int number = -1, firstTick = -1, lastTick = -1;
            internal bool complete;
            internal long total, maxCall;
            internal int rootCalls, pawnId, errors;
            internal string job;
            internal int checks, rejected, removed, searchStarts, searchSteps;
            internal int shots, fired, closeShots, interceptionShots;
            internal int gc0, gc1, gc2;
            internal long detailSelf;
            internal MethodBase detailMethod;
            internal int detailPawnId, detailTick;
            internal ShotContext detailShot;
            internal int damageSequence;
            internal DamageContext detailDamage;
            internal int detailDeathPawnId;
            internal long modDeathTreeTicks, modDeathMaxTicks;
            internal int modDeathTreeCalls, modDeathPawnId, modDeathShotId, modDeathTick;
            internal MethodBase modDeathMethod;

            internal void Clear()
            {
                Array.Clear(self, 0, self.Length);
                Array.Clear(inclusive, 0, inclusive.Length);
                Array.Clear(calls, 0, calls.Length);
                Array.Clear(health, 0, health.Length);
                number = firstTick = lastTick = -1;
                complete = false;
                total = maxCall = 0;
                rootCalls = pawnId = errors = 0;
                job = null;
                checks = rejected = removed = searchStarts = searchSteps = 0;
                shots = fired = closeShots = interceptionShots = 0;
                gc0 = gc1 = gc2 = 0;
                detailSelf = 0;
                detailMethod = null;
                detailPawnId = detailTick = 0;
                detailShot = default;
                damageSequence = 0;
                detailDamage = default;
                detailDeathPawnId = 0;
                modDeathTreeTicks = modDeathMaxTicks = 0;
                modDeathTreeCalls = modDeathPawnId = modDeathShotId = modDeathTick = 0;
                modDeathMethod = null;
            }

            internal void CopyFrom(FrameData value)
            {
                Array.Copy(value.self, self, self.Length);
                Array.Copy(value.inclusive, inclusive, inclusive.Length);
                Array.Copy(value.calls, calls, calls.Length);
                Array.Copy(value.health, health, health.Length);
                number = value.number;
                firstTick = value.firstTick;
                lastTick = value.lastTick;
                total = value.total;
                maxCall = value.maxCall;
                rootCalls = value.rootCalls;
                pawnId = value.pawnId;
                job = value.job;
                errors = value.errors;
                checks = value.checks;
                rejected = value.rejected;
                removed = value.removed;
                searchStarts = value.searchStarts;
                searchSteps = value.searchSteps;
                shots = value.shots;
                fired = value.fired;
                closeShots = value.closeShots;
                interceptionShots = value.interceptionShots;
                gc0 = value.gc0;
                gc1 = value.gc1;
                gc2 = value.gc2;
                detailSelf = value.detailSelf;
                detailMethod = value.detailMethod;
                detailPawnId = value.detailPawnId;
                detailTick = value.detailTick;
                detailShot = value.detailShot;
                damageSequence = value.damageSequence;
                detailDamage = value.detailDamage;
                detailDeathPawnId = value.detailDeathPawnId;
                modDeathTreeTicks = value.modDeathTreeTicks;
                modDeathMaxTicks = value.modDeathMaxTicks;
                modDeathTreeCalls = value.modDeathTreeCalls;
                modDeathPawnId = value.modDeathPawnId;
                modDeathShotId = value.modDeathShotId;
                modDeathTick = value.modDeathTick;
                modDeathMethod = value.modDeathMethod;
            }
        }

        internal static void Reset()
        {
            generation++;
            depth = 0;
            detailDepth = 0;
            logDepth = 0;
            injuryDepth = 0;
            deathDepth = modDeathDepth = 0;
            ClearDamageResult();
            game = null;
            frame.Clear();
            peak.Clear();
            windowFrames = windowHighFrames = 0;
            windowTicks = lastReport = 0;
        }

        internal static void BeginFrame()
        {
            if (!ReferenceEquals(game, Current.Game))
            {
                Reset();
                game = Current.Game;
            }
            int number = Time.frameCount;
            if (frame.number == number)
            {
                return;
            }
            CompleteFrame();
            ClearDamageResult();
            frame.Clear();
            frame.number = number;
            frame.gc0 = GC.CollectionCount(0);
            frame.gc1 = GC.CollectionCount(1);
            frame.gc2 = GC.CollectionCount(2);
        }

        internal static Scope EnterRoot(Pawn pawn)
        {
            // Use maintained access only. Do not inspect genes, maps or weapons
            // for ordinary Pawns merely to decide whether to measure them.
            if (pawn == null
                || (!RimKataEligibilityCache.IsRegisteredUser(pawn)
                    && !(RimKataMod.Settings?.accessRestrictionsDisabled == true
                        && RimKataCombatStatePresenceCache.TryGetOwner(pawn, out _))))
            {
                return default;
            }
            if (depth == 0)
            {
                BeginFrame();
                if (game == null || frame.complete)
                {
                    return default;
                }
                int tick = Find.TickManager?.TicksGame ?? -1;
                if (frame.firstTick < 0)
                {
                    frame.firstTick = tick;
                }
                frame.lastTick = tick;
                frame.rootCalls++;
            }
            Scope scope = Push(Part.Entry);
            if (scope.depth == 1)
            {
                stack[0].pawnId = pawn.thingIDNumber;
                stack[0].job = pawn.CurJobDef?.defName;
                // Exclude probe-only context capture from this root's clock.
                stack[0].start = Stopwatch.GetTimestamp();
            }
            return scope;
        }

        internal static Scope EnterPart(MethodBase method)
        {
            if (depth == 0 || !parts.TryGetValue(method, out Part part)
                || (part >= Part.Available && detailDepth == 0)) return default;
            if ((part == Part.DamagePre || part == Part.Injury || part == Part.DamagePost)
                && stack[depth - 1].damage.sequence == 0) return default;
            if (part == Part.LogText && logDepth == 0) return default;
            if (part >= Part.DeathEffects && deathDepth == 0) return default;
            Scope scope = Push(part);
            if (scope.depth != 0) stack[depth - 1].method = method;
            return scope;
        }

        internal static Scope EnterFire(MethodBase method, Verb verb, LocalTargetInfo target,
            bool moving, bool close, bool interception)
        {
            if (depth == 0)
            {
                return default;
            }
            frame.shots++;
            if (close) frame.closeShots++;
            if (interception) frame.interceptionShots++;
            Scope scope = Push(Part.Fire);
            if (scope.depth != 0)
            {
                Thing weapon = verb?.EquipmentSource;
                stack[depth - 1].method = method;
                stack[depth - 1].shot = new ShotContext
                {
                    sequence = frame.shots,
                    tick = Find.TickManager?.TicksGame ?? -1,
                    pawnId = verb?.CasterPawn?.thingIDNumber ?? stack[depth - 1].pawnId,
                    targetId = target.Thing?.thingIDNumber ?? 0,
                    weaponId = weapon?.thingIDNumber ?? 0,
                    weaponDef = weapon?.def?.defName,
                    flags = (byte)((moving ? 1 : 0) | (close ? 2 : 0) | (interception ? 4 : 0))
                };
            }
            return scope;
        }

        internal static Scope EnterDamage(MethodBase method, Thing recipient, DamageInfo info)
        {
            if (depth == 0 || detailDepth == 0) return default;
            Scope scope = Push(Part.Damage);
            if (scope.depth != 0)
            {
                DamageContext damage = RecipientContext(recipient);
                damage.sequence = ++frame.damageSequence;
                damage.instigatorId = info.Instigator?.thingIDNumber ?? 0;
                damage.damageDef = info.Def?.defName;
                damage.incomingAmount = info.Amount;
                stack[depth - 1].method = method;
                stack[depth - 1].damage = damage;
            }
            return scope;
        }

        internal static Scope EnterDeath(MethodBase method, Pawn pawn)
        {
            if (depth == 0 || detailDepth == 0 || pawn == null) return default;
            Scope scope = Push(Part.Death);
            if (scope.depth != 0)
            {
                stack[depth - 1].method = method;
                stack[depth - 1].deathPawnId = pawn.thingIDNumber;
            }
            return scope;
        }

        internal static Scope EnterCorpsePlacement(MethodBase method, Thing thing)
        {
            if (depth == 0 || deathDepth == 0 || !(thing is Corpse corpse)
                || corpse.InnerPawn?.thingIDNumber != stack[depth - 1].deathPawnId) return default;
            Scope scope = Push(Part.DeathPlace);
            if (scope.depth != 0) stack[depth - 1].method = method;
            return scope;
        }

        internal static HealthScope EnterHealth(MethodBase method, Pawn pawn, Hediff hediff, Part part)
        {
            // Health hooks also run for unrelated Pawns. Read their state only
            // inside an already measured injury operation.
            if (depth == 0 || injuryDepth == 0 || pawn == null) return default;
            Scope scope = Push(part);
            if (scope.depth == 0) return default;
            stack[depth - 1].method = method;
            return new HealthScope
            {
                scope = scope, pawn = pawn, before = HealthFlags(pawn),
                beforeCount = pawn.health?.hediffSet?.hediffs?.Count ?? -1,
                hediffDef = hediff?.def?.defName
            };
        }

        private static byte HealthFlags(Pawn pawn)
            => (byte)((pawn.Downed ? 1 : 0) | (pawn.Dead ? 2 : 0));

        internal static void ExitHealth(HealthScope health, bool failed)
        {
            Scope scope = health.scope;
            if (scope.depth == 0 || scope.generation != generation || scope.depth != depth) return;
            long end = Stopwatch.GetTimestamp();
            Entry entry = stack[depth - 1];
            long self = Math.Max(0, end - entry.start - entry.children);
            int index = entry.part == Part.InjuryAdd ? 0 : 1;
            if (!frame.health[index].recorded || self > frame.health[index].self)
            {
                // Each stage retains its own worst call. Nested calls can observe
                // the same transition; these are observations, not event counts.
                frame.health[index] = new HealthSample
                {
                    self = self, tick = Find.TickManager?.TicksGame ?? -1,
                    pawnId = health.pawn.thingIDNumber,
                    damageId = entry.damage.sequence, shotId = entry.shot.sequence,
                    before = health.before, after = HealthFlags(health.pawn),
                    beforeCount = health.beforeCount,
                    afterCount = health.pawn.health?.hediffSet?.hediffs?.Count ?? -1,
                    hediffDef = health.hediffDef, failed = failed, recorded = true
                };
            }
            // Use the same endpoint for exclusive totals and the retained sample;
            // snapshot formatting/copying is not part of this health operation.
            Exit(scope, failed, end);
        }

        internal static Scope EnterDamageLog(MethodBase method, DamageWorker.DamageResult result)
        {
            if (depth == 0 || detailDepth == 0) return default;
            Scope scope = Push(Part.DamageLog);
            if (scope.depth != 0)
            {
                stack[depth - 1].method = method;
                // A result may be associated after another damage event. Never
                // assign that other event's packet merely because it was recent.
                stack[depth - 1].damage = result != null && ReferenceEquals(result, lastDamageResult)
                    ? lastDamage : RecipientContext(result?.hitThing);
            }
            return scope;
        }

        private static DamageContext RecipientContext(Thing recipient)
        {
            return new DamageContext
            {
                recipientId = recipient?.thingIDNumber ?? 0,
                recipientDef = recipient?.def?.defName,
                recipientCategory = recipient?.def?.category ?? default
            };
        }

        private static void ClearDamageResult()
        {
            lastDamageResult = null;
            lastDamage = default;
        }

        internal static void ExitDamage(Scope scope, DamageWorker.DamageResult result, bool failed)
        {
            if (scope.depth == 0 || scope.generation != generation || scope.depth != depth) return;
            if (failed || result == null)
            {
                ClearDamageResult();
            }
            else
            {
                // Outer Pawn damage completes after nested armor damage, restoring
                // the correct result/context pair for an immediate log association.
                lastDamageResult = result;
                lastDamage = stack[depth - 1].damage;
            }
            Exit(scope, failed);
        }

        private static bool OpensDetail(Part part)
            => part == Part.Fire || part == Part.CloseHit || part == Part.Aim;

        private static Scope Push(Part part)
        {
            if (stack == null) stack = new Entry[64];
            if (depth == stack.Length)
            {
                // An unmeasured deep child stays charged to its measured parent.
                return default;
            }
            Entry parent = depth > 0 ? stack[depth - 1] : default;
            stack[depth] = new Entry
            {
                part = part, start = Stopwatch.GetTimestamp(),
                pawnId = parent.pawnId, job = parent.job, shot = parent.shot,
                damage = parent.damage, deathPawnId = parent.deathPawnId
            };
            depth++;
            if (OpensDetail(part)) detailDepth++;
            if (part == Part.DamageLog) logDepth++;
            if (part == Part.Injury) injuryDepth++;
            if (part == Part.Death) deathDepth++;
            if (part == Part.ModDeath) modDeathDepth++;
            return new Scope { depth = depth, generation = generation };
        }

        internal static void Exit(Scope scope, bool failed, long end = 0)
        {
            if (scope.depth == 0 || scope.generation != generation
                || scope.depth != depth)
            {
                return;
            }
            if (end == 0) end = Stopwatch.GetTimestamp();
            Entry entry = stack[--depth];
            long elapsed = Math.Max(0, end - entry.start);
            int index = (int)entry.part;
            long self = Math.Max(0, elapsed - entry.children);
            frame.inclusive[index] += elapsed;
            frame.self[index] += self;
            frame.calls[index]++;
            // Keep one worst exclusive call, not the sum of several unrelated shots.
            // Its shot fields describe the enclosing shot, not necessarily the damage recipient.
            if (detailDepth > 0 && self > frame.detailSelf && entry.method != null)
            {
                frame.detailSelf = self;
                frame.detailMethod = entry.method;
                frame.detailPawnId = entry.pawnId;
                frame.detailTick = Find.TickManager?.TicksGame ?? -1;
                frame.detailShot = entry.shot;
                frame.detailDamage = entry.damage;
                frame.detailDeathPawnId = entry.deathPawnId;
            }
            if (entry.part == Part.ModDeath)
            {
                // Count an audited callback subtree once even when it calls
                // another measured RimKata helper or native death stage.
                if (modDeathDepth == 1)
                {
                    frame.modDeathTreeTicks += elapsed;
                    frame.modDeathTreeCalls++;
                    if (elapsed > frame.modDeathMaxTicks)
                    {
                        frame.modDeathMaxTicks = elapsed;
                        frame.modDeathMethod = entry.method;
                        frame.modDeathPawnId = entry.deathPawnId;
                        frame.modDeathShotId = entry.shot.sequence;
                        frame.modDeathTick = Find.TickManager?.TicksGame ?? -1;
                    }
                }
                modDeathDepth--;
            }
            if (OpensDetail(entry.part)) detailDepth--;
            if (entry.part == Part.DamageLog) logDepth--;
            if (entry.part == Part.Injury) injuryDepth--;
            if (entry.part == Part.Death) deathDepth--;
            if (depth > 0)
            {
                stack[depth - 1].children += elapsed;
            }
            else
            {
                ClearDamageResult();
                frame.total += elapsed;
                if (failed) frame.errors++;
                if (elapsed > frame.maxCall)
                {
                    frame.maxCall = elapsed;
                    frame.pawnId = entry.pawnId;
                    frame.job = entry.job;
                }
            }
            stack[depth] = default;
        }

        internal static void CountResult(MethodBase method, bool result)
        {
            if (depth == 0) return;
            switch (method.Name)
            {
                case "CanShootRegisteredCandidate":
                    frame.checks++;
                    if (!result) frame.rejected++;
                    break;
                case "RemoveAutomaticCandidateFromCycle":
                    if (result) frame.removed++;
                    break;
                case "Begin":
                    if (result) frame.searchStarts++;
                    break;
                case "Advance":
                    if (result) frame.searchSteps++;
                    break;
            }
        }

        internal static void CountFired(bool result)
        {
            if (depth > 0 && result) frame.fired++;
        }

        internal static void CompleteFrame()
        {
            if (depth != 0 || !ReferenceEquals(game, Current.Game)) return;
            if (!frame.complete && frame.number >= 0)
            {
                frame.complete = true;
                if (frame.rootCalls > 0)
                {
                    frame.gc0 = GC.CollectionCount(0) - frame.gc0;
                    frame.gc1 = GC.CollectionCount(1) - frame.gc1;
                    frame.gc2 = GC.CollectionCount(2) - frame.gc2;
                    windowFrames++;
                    windowTicks += frame.total;
                    if (frame.total * TickToMs >= FrameThresholdMs)
                    {
                        windowHighFrames++;
                        if (frame.total > peak.total) peak.CopyFrom(frame);
                    }
                }
            }
            if (windowHighFrames == 0) return;
            long now = Stopwatch.GetTimestamp();
            if (lastReport != 0 && now - lastReport < Stopwatch.Frequency) return;
            // The first crossing is immediate; subsequent crossings are coalesced
            // for one second, retaining the worst full frame and window counts.
            Report();
            lastReport = Stopwatch.GetTimestamp();
            peak.Clear();
            windowFrames = windowHighFrames = 0;
            windowTicks = 0;
        }

        private static string Ms(long ticks)
        {
            return (ticks * TickToMs).ToString("F3", CultureInfo.InvariantCulture);
        }

        private static void Report()
        {
            var text = new StringBuilder(3800);
            text.Append(Prefix).Append("version=5 frame=").Append(peak.number)
                .Append(" ticks=").Append(peak.firstTick).Append("..").Append(peak.lastTick)
                .Append(" frame_ms=").Append(Ms(peak.total))
                .Append(" root_calls=").Append(peak.rootCalls)
                .Append(" max_call_ms=").Append(Ms(peak.maxCall))
                .Append(" pawn_id=").Append(peak.pawnId).Append(" job=").Append(peak.job)
                .Append(" window_active_frames=").Append(windowFrames)
                .Append(" window_avg_ms=").Append(Ms(windowTicks / Math.Max(1, windowFrames)))
                .Append(" frames_ge_1ms=").Append(windowHighFrames)
                .Append(" self_ms{");
            bool separator = false;
            for (int i = 0; i < (int)Part.Count; i++)
            {
                if (peak.calls[i] == 0) continue;
                if (separator) text.Append(',');
                separator = true;
                text.Append((Part)i).Append('=').Append(Ms(peak.self[i]))
                    .Append('/').Append(peak.calls[i]);
            }
            text.Append("} inclusive_ms{Cycle=").Append(Ms(peak.inclusive[(int)Part.Cycle]))
                .Append(",Slot=").Append(Ms(peak.inclusive[(int)Part.Slot]))
                .Append(",Fire=").Append(Ms(peak.inclusive[(int)Part.Fire]))
                .Append(",CloseHit=").Append(Ms(peak.inclusive[(int)Part.CloseHit]))
                .Append(",Warmup=").Append(Ms(peak.inclusive[(int)Part.Warmup]))
                .Append(",Cast=").Append(Ms(peak.inclusive[(int)Part.Cast]))
                .Append(",Damage=").Append(Ms(peak.inclusive[(int)Part.Damage]))
                .Append(",DamagePre=").Append(Ms(peak.inclusive[(int)Part.DamagePre]))
                .Append(",Injury=").Append(Ms(peak.inclusive[(int)Part.Injury]))
                .Append(",InjuryAdd=").Append(Ms(peak.inclusive[(int)Part.InjuryAdd]))
                .Append(",HealthState=").Append(Ms(peak.inclusive[(int)Part.HealthState]))
                .Append(",Death=").Append(Ms(peak.inclusive[(int)Part.Death]))
                .Append(",DeathThoughts=").Append(Ms(peak.inclusive[(int)Part.DeathThoughts]))
                .Append(",DamagePost=").Append(Ms(peak.inclusive[(int)Part.DamagePost]))
                .Append(",LogText=").Append(Ms(peak.inclusive[(int)Part.LogText]))
                .Append(",Stance=").Append(Ms(peak.inclusive[(int)Part.Stance]))
                .Append(",BattleLog=").Append(Ms(peak.inclusive[(int)Part.BattleLog]))
                .Append("} checks=").Append(peak.checks)
                .Append(" rejected=").Append(peak.rejected).Append(" removed=").Append(peak.removed)
                .Append(" search_start/advance=").Append(peak.searchStarts).Append('/').Append(peak.searchSteps)
                .Append(" shot_attempt/fired/close/intercept=").Append(peak.shots).Append('/')
                .Append(peak.fired).Append('/').Append(peak.closeShots).Append('/').Append(peak.interceptionShots)
                .Append(" gc_frame=").Append(peak.gc0).Append('/').Append(peak.gc1).Append('/').Append(peak.gc2)
                .Append(" errors=").Append(peak.errors);
            if (peak.detailMethod != null)
            {
                text.Append(" detail_self_ms=").Append(Ms(peak.detailSelf))
                    .Append(" detail_method=").Append(peak.detailMethod.DeclaringType?.FullName)
                    .Append(':').Append(peak.detailMethod.Name)
                    .Append(" detail_root_pawn_id=").Append(peak.detailPawnId)
                    .Append(" detail_tick=").Append(peak.detailTick)
                    .Append(" detail_death_pawn_id=").Append(peak.detailDeathPawnId)
                    .Append(" active_shot_id=").Append(peak.detailShot.sequence)
                    .Append(" shot_tick=").Append(peak.detailShot.tick)
                    .Append(" shot_pawn_id=").Append(peak.detailShot.pawnId)
                    .Append(" shot_target_id=").Append(peak.detailShot.targetId)
                    .Append(" shot_weapon=").Append(peak.detailShot.weaponDef)
                    .Append('#').Append(peak.detailShot.weaponId)
                    .Append(" shot_moving/close/intercept=")
                    .Append((peak.detailShot.flags & 1) != 0 ? 1 : 0).Append('/')
                    .Append((peak.detailShot.flags & 2) != 0 ? 1 : 0).Append('/')
                    .Append((peak.detailShot.flags & 4) != 0 ? 1 : 0);
                text.Append(" damage_id=").Append(peak.detailDamage.sequence)
                    .Append(" actual_recipient_id=").Append(peak.detailDamage.recipientId)
                    .Append(" actual_recipient_def=").Append(peak.detailDamage.recipientDef)
                    .Append(" actual_recipient_category=").Append(peak.detailDamage.recipientCategory);
                if (peak.detailDamage.sequence != 0)
                {
                    text.Append(" damage_incoming_def=").Append(peak.detailDamage.damageDef)
                        .Append(" damage_incoming_amount=")
                        .Append(peak.detailDamage.incomingAmount.ToString("F3", CultureInfo.InvariantCulture))
                        .Append(" damage_instigator_id=").Append(peak.detailDamage.instigatorId);
                }
            }
            AppendHealthSample(text, "injury_add", peak.health[0]);
            AppendHealthSample(text, "health_state", peak.health[1]);
            text.Append(" mod_death_tree_ms=").Append(Ms(peak.modDeathTreeTicks))
                .Append(" mod_death_tree_calls=").Append(peak.modDeathTreeCalls);
            if (peak.modDeathMethod != null)
            {
                text.Append(" mod_death_max_tree_ms=").Append(Ms(peak.modDeathMaxTicks))
                    .Append(" mod_death_method=").Append(peak.modDeathMethod.DeclaringType?.FullName)
                    .Append(':').Append(peak.modDeathMethod.Name)
                    .Append(" mod_death_cause_pawn_id=").Append(peak.modDeathPawnId)
                    .Append(" mod_death_shot_id=").Append(peak.modDeathShotId)
                    .Append(" mod_death_tick=").Append(peak.modDeathTick);
            }
            Log.Message(text.ToString());
        }

        private static string HealthStateName(byte flags)
            => (flags & 2) != 0 ? "Dead" : (flags & 1) != 0 ? "Downed" : "Alive";

        private static void AppendHealthSample(StringBuilder text, string name, HealthSample sample)
        {
            if (!sample.recorded) return;
            text.Append(' ').Append(name).Append("_self_ms=").Append(Ms(sample.self))
                .Append(' ').Append(name).Append("_tick=").Append(sample.tick)
                .Append(' ').Append(name).Append("_pawn_id=").Append(sample.pawnId)
                .Append(' ').Append(name).Append("_damage_id=").Append(sample.damageId)
                .Append(' ').Append(name).Append("_shot_id=").Append(sample.shotId)
                .Append(' ').Append(name).Append("_hediff=").Append(sample.hediffDef)
                .Append(' ').Append(name).Append("_before=").Append(HealthStateName(sample.before))
                .Append(' ').Append(name).Append("_after=").Append(HealthStateName(sample.after))
                .Append(' ').Append(name).Append("_hediffs_before=").Append(sample.beforeCount)
                .Append(' ').Append(name).Append("_hediffs_after=").Append(sample.afterCount)
                .Append(' ').Append(name).Append("_failed=").Append(sample.failed ? 1 : 0);
        }

        internal static IEnumerable<MethodBase> TimingTargets()
        {
            if (parts.Count == 0)
            {
                Type controller = typeof(RimKataDualWeaponController);
                Add(controller, "PrepareWeaponCycleTick", 3, Part.Prepare);
                Add(controller, "TickPreparedWeaponCycles", 8, Part.Cycle);
                Add(typeof(JobDriver_RimKataAttack), "TickPreparedCombat", 5, Part.NestedJob);
                Add(controller, "ResolveCloseTarget", 7, Part.Close);
                Add(controller, "HandleCloseCombatTransition", 5, Part.Close);
                Add(controller, "SanitizeCycleForCloseCombat", 3, Part.Close);
                Add(controller, "NormalizeUnavailableCycleWork", 5, Part.Normalize);
                Add(typeof(RimKataSharedTargetSearch), "Begin", 3, Part.Search);
                Add(typeof(RimKataSharedTargetSearch), "Advance", 3, Part.Search);
                Add(typeof(RimKataSharedTargetSearch), "TrySelectCandidate", 10, Part.Select);
                Add(controller, "TryCacheSharedCandidate", 7, Part.Reserve);
                Add(controller, "TickWeaponCycle", 15, Part.Slot);
                Add(typeof(RimKataFireContext), "ResolvePendingCloseHits", 0, Part.CloseHit);
                Add(controller, "RefreshDualEngagementState", 3, Part.Continuity);
                Add(controller, "UpdateBodyAimStance", 2, Part.Aim);
                Add(typeof(Battle), "Add", 1, Part.BattleLog);
                // Deep native hooks only open clocks inside Fire, CloseHit or Aim.
                // Include inherited melee paths as well as projectile overrides.
                Add(typeof(Verb), "Available", 0, Part.Available);
                Add(typeof(Verb_LaunchProjectile), "Available", 0, Part.Available);
                Add(typeof(Verb), "CanHitTarget", 1, Part.CanHit);
                Add(typeof(RimKataFireContext), "Begin", 10, Part.FireContext);
                Add(typeof(RimKataFireContext), "End", 2, Part.FireContext);
                Add(typeof(RimKataDirectCloseShot), "TryPrepare", 3, Part.ShotPrepare);
                Add(typeof(Verb), "WarmupComplete", 0, Part.Warmup);
                Add(typeof(Verb_LaunchProjectile), "WarmupComplete", 0, Part.Warmup);
                Add(typeof(Verb), "TryCastNextBurstShot", 0, Part.Burst);
                Add(typeof(Verb_LaunchProjectile), "TryCastShot", 0, Part.Cast);
                Add(typeof(RimWorld.Verb_MeleeAttack), "TryCastShot", 0, Part.Cast);
                Add(typeof(Verse.Sound.SoundStarter), "PlayOneShot", 2, Part.Sound);
                Add(typeof(Pawn_StanceTracker), "SetStance", 1, Part.Stance);
                Add(typeof(Verse.AI.JobDriver_Wait), "CheckForAutoAttack", 0, Part.AutoAttack);
                Add(typeof(RimKataDirectCloseHit), "Resolve", 0, Part.DirectHit);
                Add(typeof(RimWorld.Verb_MeleeAttackDamage), "ApplyMeleeDamageToTarget", 1, Part.MeleeDamage);
                // TakeDamage/AssociateWithLog have typed patches below to capture
                // the actual recipient and match the returned result by identity.
                Add(typeof(Pawn), "PreApplyDamage", 2, Part.DamagePre);
                Add(typeof(DamageWorker_AddInjury), "Apply", 2, Part.Injury);
                Add(typeof(Pawn), "PostApplyDamage", 2, Part.DamagePost);
                Add(typeof(LogEntry), "ToGameStringFromPOV", 2, Part.LogText);
                Add(typeof(GenClamor), "DoClamor", new[]
                    { typeof(Thing), typeof(IntVec3), typeof(float), typeof(ClamorDef) }, Part.Clamor);
                Add(typeof(RimKataDirectCloseHit), "ApplyStagger", 1, Part.Stagger);
                Add(typeof(RimKataDirectCloseHit), "ApplyExtraDamages", 2, Part.ExtraDamage);
                Add(typeof(RimKataCloseProjectilePlacement), "TryPlace", 3, Part.ProjectilePlace);
                Add(typeof(RimKataProjectileUtility), "ResolveCloseImpact", 2, Part.ProjectileImpact);
                Add(typeof(RimKataProjectileUtility), "Impact", 3, Part.ProjectileImpact);
                Add(controller, "NotifyDefensiveCombatEvent", 2, Part.DefenseNotify);
                Add(typeof(Pawn), "DoKillSideEffects", 3, Part.DeathEffects);
                Add(typeof(Pawn), "PreDeathPawnModifications", 2, Part.DeathModify);
                Add(typeof(Pawn), "DropBeforeDying", 3, Part.DeathThoughts);
                Add(typeof(Thing), "DeSpawnOrDeselect", 1, Part.DeathDespawn);
                Add(typeof(Pawn), "MakeCorpse", 3, Part.DeathCorpse);
                // Audited helper roots, not a claim to cover every patch dispatch
                // or every possible contribution from this or another mod.
                Add(typeof(RimKataWeaponSlotUtility), "NotifyEquipmentChanged", 3, Part.ModDeath);
                Add(typeof(RimKataWeaponSlotUtility), "InvalidateCombatVerbCache", 1, Part.ModDeath);
                Add(typeof(RimKataMapComponent), "NotifyThingDespawned", 1, Part.ModDeath);
                Add(typeof(RimKataMapComponent), "NotifyThingSpawned", 1, Part.ModDeath);
                Add(typeof(RimKataEligibilityCache), "InvalidateRelations", 1, Part.ModDeath);
                Add(typeof(RimKataEligibilityCache), "InvalidateHediff", 2, Part.ModDeath);
                Add(typeof(RimKataEligibilityCache), "InvalidateGenes", 1, Part.ModDeath);
                Add(typeof(RimKataEligibilityCache), "InvalidateRole", 1, Part.ModDeath);
                Add(typeof(JobDriver_RimKataAttack), "ClearAimStance", 0, Part.ModDeath);
                Add(controller, "NotifyDedicatedCombatJobFinished", 1, Part.ModDeath);
                Add(typeof(RimKataSecondaryWeaponRegistry), "NotifySecondaryRecoveryJobFinished", 3, Part.ModDeath);
                Add(typeof(ThoughtWorker_MindNumbSerumWithdrawal), "CurrentStateInternal", 1, Part.ModDeath);
                Add(typeof(ThoughtWorker_MindNumbSerumEmotionRemoval), "CurrentStateInternal", 1, Part.ModDeath);
                Add(typeof(ThoughtWorker_MindNumbSerumDependencyOvercome), "CurrentStateInternal", 1, Part.ModDeath);
                Add(typeof(Thought_MindNumbSerumWithdrawal), "MoodOffset", 0, Part.ModDeath);
                Log.Message(Prefix + "enabled version=5; qualified ProcessJobTrackerTick trees only; "
                    + "frame threshold=1.000ms; self_ms entries are exclusive ms/calls; "
                    + "inclusive_ms entries overlap; window averages use active sampled frames; "
                    + "outside JobDriver work excluded, synchronous nested Job work included; "
                    + "deep scopes only inside Fire/CloseHit/Aim; detail_self_ms is the worst single exclusive call; "
                    + "active_shot_id is frame-local, zero means outside a shot; shot_target_id is the shot target, "
                    + "not an asserted damage recipient; nested same-category inclusive times overlap too; "
                    + "damage_id is frame-local; damage_incoming_def/amount are the original input before mitigation; "
                    + "actual_recipient describes the measured damage, not the shot target; "
                    + "damage_id=0 means no matched packet and recipient_id=0 means unknown; "
                    + "LogText is scoped to damage-log association; "
                    + "InjuryAdd/HealthState are scoped to Injury; each keeps its own worst exclusive call "
                    + "with Pawn state and hediff count before/after; Alive means not downed/dead; "
                    + "nested observations are not unique injury/death counts; "
                    + "Death stages and ModDeath helper clocks run only inside a measured Pawn.Kill; "
                    + "DeathPlace measures only the active victim's corpse, not dropped items; "
                    + "mod_death_tree_ms counts audited outermost RimKata callback subtrees once, "
                    + "including their descendants; it overlaps stage totals and is not all mod overhead; "
                    + "mod_death_cause_pawn_id is the death victim, not necessarily the helper's receiver; "
                    + "first crossing then worst frame per 1s; timing includes instrumentation overhead.");
            }
            return parts.Keys;
        }

        private static void Add(Type type, string name, int parameters, Part part)
        {
            MethodBase found = null;
            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(type))
            {
                if (method.Name != name || method.GetParameters().Length != parameters) continue;
                if (found != null)
                {
                    Log.Warning(Prefix + "skipped ambiguous target " + type.Name + "." + name);
                    return;
                }
                found = method;
            }
            if (found == null)
            {
                Log.Warning(Prefix + "missing target " + type.Name + "." + name);
                return;
            }
            parts.Add(found, part);
        }

        private static void Add(Type type, string name, Type[] parameters, Part part)
        {
            MethodInfo method = AccessTools.DeclaredMethod(type, name, parameters);
            if (method == null)
            {
                Log.Warning(Prefix + "missing exact target " + type.Name + "." + name);
                return;
            }
            parts.Add(method, part);
        }
    }

    [HarmonyPatch(typeof(RimKataDraftedFireController), nameof(RimKataDraftedFireController.ProcessJobTrackerTick))]
    internal static class RimKataProbeRootPatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(Pawn pawn, out RimKataPerformanceProbe.Scope __state)
            => __state = RimKataPerformanceProbe.EnterRoot(pawn);
        [HarmonyPriority(Priority.Last)]
        private static void Finalizer(RimKataPerformanceProbe.Scope __state, Exception __exception)
            => RimKataPerformanceProbe.Exit(__state, __exception != null);
    }

    [HarmonyPatch]
    internal static class RimKataProbePartsPatch
    {
        private static IEnumerable<MethodBase> TargetMethods() => RimKataPerformanceProbe.TimingTargets();
        private static void Prefix(MethodBase __originalMethod, out RimKataPerformanceProbe.Scope __state)
            => __state = RimKataPerformanceProbe.EnterPart(__originalMethod);
        private static void Finalizer(RimKataPerformanceProbe.Scope __state, Exception __exception)
            => RimKataPerformanceProbe.Exit(__state, __exception != null);
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
    internal static class RimKataProbeDamagePatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(MethodBase __originalMethod, Thing __instance, DamageInfo __0,
            out RimKataPerformanceProbe.Scope __state)
            => __state = RimKataPerformanceProbe.EnterDamage(__originalMethod, __instance, __0);
        [HarmonyPriority(Priority.Last)]
        private static void Finalizer(RimKataPerformanceProbe.Scope __state,
            DamageWorker.DamageResult __result, Exception __exception)
            => RimKataPerformanceProbe.ExitDamage(__state, __result, __exception != null);
    }

    [HarmonyPatch(typeof(DamageWorker.DamageResult), nameof(DamageWorker.DamageResult.AssociateWithLog))]
    internal static class RimKataProbeDamageLogPatch
    {
        private static void Prefix(MethodBase __originalMethod, DamageWorker.DamageResult __instance,
            out RimKataPerformanceProbe.Scope __state)
            => __state = RimKataPerformanceProbe.EnterDamageLog(__originalMethod, __instance);
        private static void Finalizer(RimKataPerformanceProbe.Scope __state, Exception __exception)
            => RimKataPerformanceProbe.Exit(__state, __exception != null);
    }

    [HarmonyPatch(typeof(HediffSet), nameof(HediffSet.AddDirect))]
    internal static class RimKataProbeInjuryAddPatch
    {
        private static void Prefix(MethodBase __originalMethod, Pawn ___pawn, Hediff __0,
            out RimKataPerformanceProbe.HealthScope __state)
            => __state = RimKataPerformanceProbe.EnterHealth(__originalMethod, ___pawn, __0,
                RimKataPerformanceProbe.Part.InjuryAdd);
        private static void Finalizer(RimKataPerformanceProbe.HealthScope __state, Exception __exception)
            => RimKataPerformanceProbe.ExitHealth(__state, __exception != null);
    }

    [HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.CheckForStateChange))]
    internal static class RimKataProbeHealthStatePatch
    {
        private static void Prefix(MethodBase __originalMethod, Pawn ___pawn, Hediff __1,
            out RimKataPerformanceProbe.HealthScope __state)
            => __state = RimKataPerformanceProbe.EnterHealth(__originalMethod, ___pawn, __1,
                RimKataPerformanceProbe.Part.HealthState);
        private static void Finalizer(RimKataPerformanceProbe.HealthScope __state, Exception __exception)
            => RimKataPerformanceProbe.ExitHealth(__state, __exception != null);
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    internal static class RimKataProbeDeathPatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(MethodBase __originalMethod, Pawn __instance,
            out RimKataPerformanceProbe.Scope __state)
            => __state = RimKataPerformanceProbe.EnterDeath(__originalMethod, __instance);
        [HarmonyPriority(Priority.Last)]
        private static void Finalizer(RimKataPerformanceProbe.Scope __state, Exception __exception)
            => RimKataPerformanceProbe.Exit(__state, __exception != null);
    }

    [HarmonyPatch]
    internal static class RimKataProbeCorpsePlacementPatch
    {
        private static MethodBase TargetMethod()
            => AccessTools.DeclaredMethod(typeof(GenPlace), nameof(GenPlace.TryPlaceThing), new[]
            {
                typeof(Thing), typeof(IntVec3), typeof(Map), typeof(ThingPlaceMode),
                typeof(Thing).MakeByRefType(), typeof(Action<Thing, int>),
                typeof(Predicate<IntVec3>), typeof(Rot4?), typeof(int)
            });
        private static void Prefix(MethodBase __originalMethod, Thing __0,
            out RimKataPerformanceProbe.Scope __state)
            => __state = RimKataPerformanceProbe.EnterCorpsePlacement(__originalMethod, __0);
        private static void Finalizer(RimKataPerformanceProbe.Scope __state, Exception __exception)
            => RimKataPerformanceProbe.Exit(__state, __exception != null);
    }

    [HarmonyPatch(typeof(RimKataVerbUtility), nameof(RimKataVerbUtility.FireSingleShot))]
    internal static class RimKataProbeFirePatch
    {
        private static void Prefix(MethodBase __originalMethod, Verb verb, LocalTargetInfo target,
            bool movingShot, bool closeShot, bool interceptionShot, out RimKataPerformanceProbe.Scope __state)
            => __state = RimKataPerformanceProbe.EnterFire(__originalMethod, verb, target,
                movingShot, closeShot, interceptionShot);
        private static void Postfix(bool __result) => RimKataPerformanceProbe.CountFired(__result);
        private static void Finalizer(RimKataPerformanceProbe.Scope __state, Exception __exception)
            => RimKataPerformanceProbe.Exit(__state, __exception != null);
    }

    [HarmonyPatch]
    internal static class RimKataProbeCountPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            Type type = typeof(RimKataSharedTargetSearch);
            foreach (string name in new[] { "CanShootRegisteredCandidate", "RemoveAutomaticCandidateFromCycle", "Begin", "Advance" })
            {
                MethodInfo method = AccessTools.DeclaredMethod(type, name);
                if (method != null) yield return method;
            }
        }
        private static void Postfix(MethodBase __originalMethod, bool __result)
            => RimKataPerformanceProbe.CountResult(__originalMethod, __result);
    }

    [HarmonyPatch(typeof(Root_Play), nameof(Root_Play.Update))]
    internal static class RimKataProbeFramePatch
    {
        private static void Prefix() => RimKataPerformanceProbe.BeginFrame();
        private static void Postfix() => RimKataPerformanceProbe.CompleteFrame();
    }

    [HarmonyPatch(typeof(Current), nameof(Current.Game), MethodType.Setter)]
    internal static class RimKataProbeGamePatch
    {
        private static void Prefix(Game __0)
        {
            if (!ReferenceEquals(Current.Game, __0)) RimKataPerformanceProbe.Reset();
        }
    }
}
