$ErrorActionPreference = 'Stop'
$rkRoot = Split-Path -Parent $PSScriptRoot
$rkSource = Get-Content -LiteralPath (Join-Path $rkRoot 'Source/RimKataCombatMath.cs') -Raw -Encoding UTF8

function Get-CSharpBlock([string] $source, [string] $marker) {
    $start = $source.IndexOf($marker, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing source block: $marker" }
    $open = $source.IndexOf('{', $start)
    $depth = 0
    for ($index = $open; $index -lt $source.Length; $index++) {
        if ($source[$index] -eq '{') { $depth++ }
        if ($source[$index] -eq '}') {
            $depth--
            if ($depth -eq 0) { return $source.Substring($start, $index - $start + 1) }
        }
    }
    throw "Unclosed source block: $marker"
}

$rkClass = Get-CSharpBlock $rkSource 'public static class RimKataCombatMath'
$rkFieldsStart = $rkClass.IndexOf('{') + 1
$rkFieldsEnd = $rkClass.IndexOf('public static float ConfiguredChance(', [StringComparison]::Ordinal)
if ($rkFieldsEnd -lt $rkFieldsStart) { throw 'Could not locate probability delegate fields.' }
$rkFields = $rkClass.Substring($rkFieldsStart, $rkFieldsEnd - $rkFieldsStart)
$rkMarkers = @(
    'public static float AddConfiguredMeleeDodgeBonus(',
    'private static float ApplyConfiguredMeleeDodgeBonus(',
    'public static float AddConfiguredMeleeDodgeBonusToVanilla(',
    'public static float MeleeParryChance(',
    'internal static float CloseMeleeDodgeChanceVerified(',
    'private static float CloseMeleeDodgeChanceCore(',
    'private static Verb_MeleeAttack ResolveMeleeProbabilityVerb(',
    'private static float ReadMeleeProbability('
)
$rkMethods = @{}
foreach ($rkMarker in $rkMarkers) { $rkMethods[$rkMarker] = Get-CSharpBlock $rkSource $rkMarker }
$rkProduction = ($rkMarkers | ForEach-Object { $rkMethods[$_] }) -join [Environment]::NewLine
$rkResolve = $rkMethods['private static Verb_MeleeAttack ResolveMeleeProbabilityVerb(']
$rkRead = $rkMethods['private static float ReadMeleeProbability(']
if ($rkResolve -match 'TryGetMeleeVerb|ChooseMeleeVerb|Rand\.|new\s+Verb|TryStartCastOn|TryMeleeAttack') {
    throw 'Probability receiver lookup must not select/create/cast an attack or consume RNG.'
}
if ($rkRead -match '\.caster\s*=|\.currentTarget\s*=|\.Reset\(|TryStartCastOn|TryMeleeAttack|Rand\.') {
    throw 'Probability read scope must not mutate caster/target, cast, reset, or consume RNG.'
}
if ($rkProduction -match 'GetStatValue|MeleeLightingOffset|DarknessCombatUtility') {
    throw 'Delegated probability methods must not contain copied stat/lighting arithmetic.'
}
foreach ($rkName in @('NativeMeleeHitChance', 'NativeMeleeDodgeChance')) {
    if ($rkFields -notmatch ('(?s)static readonly Func<Verb_MeleeAttack,\s*LocalTargetInfo,\s*float>\s+' + $rkName + '\s*=\s*AccessTools.MethodDelegate')) {
        throw "Expected cached original-method delegate: $rkName"
    }
}
if ($rkFields -notmatch '(?s)\[ThreadStatic\]\s*private static Pawn verifiedMeleeDodgeTarget') {
    throw 'Expected thread-local verified dodge target context.'
}

# Extracted production methods run against explicit engine and Harmony fixtures.
# The two private native getter fixtures deliberately route through observable
# hooks; the dodge fixture mimics the existing Harmony bonus call. This does NOT
# validate the game assembly's formula, Harmony patching, or in-game behavior.
$rkHarness = @"
using System;
using System.Collections.Generic;
using System.Reflection;
namespace MeleeProbabilityDelegationChecks {
    public class Thing { }
    public sealed class Pawn : Thing {
        public VerbTracker verbTracker = new VerbTracker();
        public Pawn_MeleeVerbs meleeVerbs = new Pawn_MeleeVerbs();
        public object Job = new object(), Pose = new object();
        public bool eligible = true, mindNumbed;
        public int eligibilityChecks, dodgeMultiplierReads, responseMultiplierReads, serumMultiplierReads;
        public float hit = 0.2f, dodge = 0.2f;
    }
    public sealed class VerbTracker {
        public object directOwner;
        public List<Verb> AllVerbs = new List<Verb>();
    }
    public sealed class Pawn_MeleeVerbs {
        public Verb selected;
        public Thing selectedTarget;
        public int updateTick = 37, selectionCalls;
        public Verb TryGetMeleeVerb(Thing target) {
            selectionCalls++;
            throw new Exception("Probability lookup attempted attack selection.");
        }
    }
    public class Verb {
        public Thing caster;
        public bool surpriseAttack;
        public object currentTarget = new object();
        public Pawn CasterPawn { get { return caster as Pawn; } }
        public Thing Caster { get { return caster; } }
        public VerbTracker verbTracker;
        public bool IsMeleeAttack { get { return this is Verb_MeleeAttack; } }
        public static int created;
        public Verb() { created++; }
    }
    public class Verb_MeleeAttack : Verb {
        private float GetNonMissChance(LocalTargetInfo target) {
            Native.hitCalls++;
            return Native.hit(this, target);
        }
        private float GetDodgeChance(LocalTargetInfo target) {
            Native.dodgeCalls++;
            return Native.dodge(this, target);
        }
    }
    public struct LocalTargetInfo {
        public Thing Thing;
        public Pawn Pawn { get { return Thing as Pawn; } }
        public LocalTargetInfo(Thing thing) { Thing = thing; }
        public static implicit operator LocalTargetInfo(Thing thing) { return new LocalTargetInfo(thing); }
    }
    public static class AccessTools {
        public delegate ref F FieldRef<T, F>(T instance);
        private static ref bool Surprise(Verb verb) { return ref verb.surpriseAttack; }
        private static ref Verb Selected(Pawn_MeleeVerbs tracker) { return ref tracker.selected; }
        public static FieldRef<T, F> FieldRefAccess<T, F>(string field) {
            if (typeof(T) == typeof(Verb) && typeof(F) == typeof(bool) && field == "surpriseAttack") {
                FieldRef<Verb, bool> reader = Surprise;
                return (FieldRef<T, F>)(object)reader;
            }
            if (typeof(T) == typeof(Pawn_MeleeVerbs) && typeof(F) == typeof(Verb) && field == "curMeleeVerb") {
                FieldRef<Pawn_MeleeVerbs, Verb> reader = Selected;
                return (FieldRef<T, F>)(object)reader;
            }
            throw new Exception("Unexpected field reference fixture.");
        }
        public static MethodInfo Method(Type type, string name) {
            return type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }
        public static FieldInfo Field(Type type, string name) {
            return type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }
        public static T MethodDelegate<T>(MethodInfo method) {
            return (T)(object)method.CreateDelegate(typeof(T));
        }
    }
    public static class Mathf {
        public static float Clamp01(float value) { return Math.Max(0f, Math.Min(1f, value)); }
    }
    public static class Rand {
        public static int calls;
        public static bool Chance(float value) { calls++; return value > 0f; }
    }
    public sealed class RimKataSettings {
        public float dodgeMultiplier = 3f, responseMultiplier = 2f, serumMultiplier = 1.5f;
        public float GetMeleeDodgeBonusMultiplier(Pawn pawn) { pawn.dodgeMultiplierReads++; return dodgeMultiplier; }
        public float GetMeleeResponseBonusMultiplier(Pawn pawn) { pawn.responseMultiplierReads++; return responseMultiplier; }
        public float GetSerumResponseMultiplier(Pawn pawn) { pawn.serumMultiplierReads++; return serumMultiplier; }
    }
    public static class RimKataMod { public static RimKataSettings Settings = new RimKataSettings(); }
    public static class RimKataSerumUtility { public static bool IsMindNumbed(Pawn pawn) { return pawn.mindNumbed; } }
    public static class RimKataEligibility {
        public static bool CanRollMeleeDodge(Pawn pawn) {
            if (pawn == null) return false;
            pawn.eligibilityChecks++;
            return pawn.eligible;
        }
    }
    public static class Native {
        public static int hitCalls, dodgeCalls;
        public static Func<Verb_MeleeAttack, LocalTargetInfo, float> hit, dodge;
        public static void Reset() {
            hitCalls = dodgeCalls = 0;
            hit = (verb, target) => verb.surpriseAttack ? 1f : verb.CasterPawn.hit;
            dodge = (verb, target) => verb.surpriseAttack ? 0f
                : RimKataCombatMath.AddConfiguredMeleeDodgeBonusToVanilla(target.Pawn.dodge, target);
        }
    }
    public static class RimKataCombatMath {
        $rkFields
        $rkProduction
        public static Verb_MeleeAttack ProbeResolve(Pawn pawn) { return ResolveMeleeProbabilityVerb(pawn); }
        public static float ProbeRead(Verb_MeleeAttack verb, Pawn target, Func<Verb_MeleeAttack, LocalTargetInfo, float> calculator) {
            return ReadMeleeProbability(verb, target, calculator);
        }
        public static Pawn ProbeVerified { get { return verifiedMeleeDodgeTarget; } }
    }
    public static class Checks {
        private static int checks;
        private static void Check(bool condition, string name) {
            if (!condition) throw new Exception(name);
            checks++;
        }
        private static void Equal(float actual, float expected, string name) {
            Check(Math.Abs(actual - expected) < 0.00001f, name + ": got " + actual + ", expected " + expected);
        }
        private static Pawn NewPawn() {
            var pawn = new Pawn();
            pawn.verbTracker.directOwner = pawn;
            var ranged = new Verb { caster = pawn, verbTracker = pawn.verbTracker };
            var melee = new Verb_MeleeAttack { caster = pawn, verbTracker = pawn.verbTracker };
            pawn.verbTracker.AllVerbs.Add(ranged);
            pawn.verbTracker.AllVerbs.Add(melee);
            pawn.meleeVerbs.selected = ranged;
            pawn.meleeVerbs.selectedTarget = new Thing();
            return pawn;
        }
        private static Verb_MeleeAttack Body(Pawn pawn) { return (Verb_MeleeAttack)pawn.verbTracker.AllVerbs[1]; }
        public static int Run() {
            Native.Reset();
            Check(RimKataCombatMath.ProbeResolve(null) == null, "Null owner has no receiver");
            Equal(RimKataCombatMath.MeleeParryChance(null, NewPawn()), 0f, "Null defender returns zero");
            Equal(RimKataCombatMath.MeleeParryChance(NewPawn(), null), 0f, "Null attacker returns zero");
            Equal(RimKataCombatMath.CloseMeleeDodgeChanceVerified(null), 0f, "Null dodge target returns zero");
            var empty = NewPawn();
            empty.verbTracker.AllVerbs.Clear();
            Check(RimKataCombatMath.ProbeResolve(empty) == null, "Missing body melee receiver returns null");
            empty.verbTracker = null;
            Check(RimKataCombatMath.ProbeResolve(empty) == null, "Missing tracker returns null");

            var lookupOwner = NewPawn();
            var lookupOther = NewPawn();
            var cached = new Verb_MeleeAttack { caster = lookupOwner };
            lookupOwner.meleeVerbs.selected = cached;
            Check(ReferenceEquals(RimKataCombatMath.ProbeResolve(lookupOwner), cached), "Existing owned selected melee receiver takes precedence");
            lookupOwner.meleeVerbs.selected = Body(lookupOther);
            Check(ReferenceEquals(RimKataCombatMath.ProbeResolve(lookupOwner), Body(lookupOwner)), "Foreign selected receiver falls back to owned body instance");
            lookupOwner.verbTracker.AllVerbs.Insert(0, Body(lookupOther));
            Check(ReferenceEquals(RimKataCombatMath.ProbeResolve(lookupOwner), lookupOwner.verbTracker.AllVerbs[2]), "Body lookup skips foreign caster");
            lookupOwner.meleeVerbs.selected = cached;
            lookupOwner.verbTracker = null;
            Check(ReferenceEquals(RimKataCombatMath.ProbeResolve(lookupOwner), cached), "Cached owned receiver works without body tracker");

            var defender = NewPawn();
            var attacker = NewPawn();
            var body = Body(defender);
            var originalSelection = defender.meleeVerbs.selected;
            var originalSelectionTarget = defender.meleeVerbs.selectedTarget;
            var originalTarget = body.currentTarget;
            var originalJob = defender.Job;
            var originalPose = defender.Pose;
            var created = Verb.created;
            Check(ReferenceEquals(RimKataCombatMath.ProbeResolve(defender), body), "Lookup reuses existing body instance");
            defender.mindNumbed = true;
            body.surpriseAttack = true;
            Native.hit = (verb, target) => {
                Check(ReferenceEquals(verb, body) && ReferenceEquals(verb.CasterPawn, defender), "Parry reads defender-owned receiver");
                Check(ReferenceEquals(target.Pawn, attacker), "Parry passes attacker as target");
                Check(!verb.surpriseAttack, "Stale surprise flag is scoped off for parry query");
                return 0.2f;
            };
            Equal(RimKataCombatMath.MeleeParryChance(defender, attacker), 0.6f, "Parry applies response and serum multipliers once");
            Check(defender.responseMultiplierReads == 1 && defender.serumMultiplierReads == 1, "Parry multiplier read counts");
            Check(body.surpriseAttack, "Parry restores true surprise flag");
            Check(Native.hitCalls == 1, "Parry calls native probability once");
            Check(ReferenceEquals(body.caster, defender) && ReferenceEquals(body.currentTarget, originalTarget), "Probability preserves caster and current target");
            Check(ReferenceEquals(defender.meleeVerbs.selected, originalSelection)
                && ReferenceEquals(defender.meleeVerbs.selectedTarget, originalSelectionTarget)
                && defender.meleeVerbs.updateTick == 37 && defender.meleeVerbs.selectionCalls == 0,
                "Probability preserves attack selection cache");
            Check(ReferenceEquals(defender.Job, originalJob) && ReferenceEquals(defender.Pose, originalPose), "Probability preserves job and pose fixtures");
            Check(Verb.created == created && Rand.calls == 0, "Probability creates no verb and consumes no RNG");

            Native.Reset();
            Native.dodge = (verb, target) => {
                Check(ReferenceEquals(verb, body) && ReferenceEquals(target.Pawn, defender), "Dodge receiver and target are defender");
                Check(!verb.surpriseAttack, "Dodge query scopes off stale surprise flag");
                return RimKataCombatMath.AddConfiguredMeleeDodgeBonusToVanilla(0.2f, target);
            };
            defender.eligible = false;
            defender.eligibilityChecks = defender.dodgeMultiplierReads = 0;
            Equal(RimKataCombatMath.CloseMeleeDodgeChanceVerified(defender), 0.6f, "Verified dodge reuses caller eligibility");
            Check(defender.eligibilityChecks == 0 && defender.dodgeMultiplierReads == 1, "Verified dodge skips redundant eligibility without double bonus");
            Check(body.surpriseAttack && RimKataCombatMath.ProbeVerified == null, "Verified dodge restores surprise and context");
            defender.eligible = true;
            defender.eligibilityChecks = defender.dodgeMultiplierReads = 0;
            Equal(RimKataCombatMath.AddConfiguredMeleeDodgeBonusToVanilla(0.2f, defender), 0.6f, "Ordinary vanilla hook retains bonus behavior");
            Check(defender.eligibilityChecks == 1 && defender.dodgeMultiplierReads == 1, "Ordinary hook still checks eligibility");

            body.surpriseAttack = false;
            Equal(RimKataCombatMath.ProbeRead(body, attacker, (verb, target) => 0.4f), 0.4f, "Read returns native calculator value");
            Check(!body.surpriseAttack, "Read restores original false flag");
            body.surpriseAttack = true;
            bool failed = false;
            try { RimKataCombatMath.ProbeRead(body, attacker, (verb, target) => { throw new InvalidOperationException("fixture"); }); }
            catch (InvalidOperationException) { failed = true; }
            Check(failed && body.surpriseAttack, "Read restores true flag on exception");
            Equal(RimKataCombatMath.ProbeRead(body, attacker, (outer, target) => {
                Check(!outer.surpriseAttack, "Outer read sees scoped false flag");
                float nested = RimKataCombatMath.ProbeRead(body, defender, (inner, innerTarget) => {
                    Check(ReferenceEquals(inner, outer) && !inner.surpriseAttack, "Nested read reuses same receiver safely");
                    return 0.3f;
                });
                Check(!outer.surpriseAttack, "Nested read restores outer false scope");
                return nested;
            }), 0.3f, "Nested read returns inner value");
            Check(body.surpriseAttack, "Outer read restores preexisting true flag after nesting");

            Native.dodge = (verb, target) => { throw new InvalidOperationException("native fixture"); };
            failed = false;
            try { RimKataCombatMath.CloseMeleeDodgeChanceVerified(defender); }
            catch (InvalidOperationException) { failed = true; }
            Check(failed && body.surpriseAttack && RimKataCombatMath.ProbeVerified == null, "Native exception restores flag and verified context");

            var other = NewPawn();
            defender.eligible = false;
            other.eligible = false;
            defender.eligibilityChecks = defender.dodgeMultiplierReads = 0;
            other.eligibilityChecks = other.dodgeMultiplierReads = 0;
            Native.dodge = (verb, target) => {
                if (ReferenceEquals(target.Pawn, defender)) {
                    Check(ReferenceEquals(RimKataCombatMath.ProbeVerified, defender), "Outer verified target is current");
                    Equal(RimKataCombatMath.AddConfiguredMeleeDodgeBonusToVanilla(0.2f, other), 0.2f, "Verification does not apply to another target");
                    Equal(RimKataCombatMath.CloseMeleeDodgeChanceVerified(other), 0.6f, "Nested verified target gets one bonus");
                    Check(ReferenceEquals(RimKataCombatMath.ProbeVerified, defender), "Nested verified call restores outer target");
                } else {
                    Check(ReferenceEquals(RimKataCombatMath.ProbeVerified, other), "Nested verified target is current");
                }
                return RimKataCombatMath.AddConfiguredMeleeDodgeBonusToVanilla(0.2f, target);
            };
            Equal(RimKataCombatMath.CloseMeleeDodgeChanceVerified(defender), 0.6f, "Outer verified result retains one bonus after nesting");
            Check(defender.eligibilityChecks == 0 && defender.dodgeMultiplierReads == 1
                && other.eligibilityChecks == 1 && other.dodgeMultiplierReads == 1,
                "Nested context only skips eligibility for its exact target");
            Check(RimKataCombatMath.ProbeVerified == null && body.surpriseAttack, "Nested verified scopes fully restore");
            Check(Rand.calls == 0 && defender.meleeVerbs.selectionCalls == 0 && other.meleeVerbs.selectionCalls == 0,
                "All probability reads remain selection-free and RNG-free");
            return checks;
        }
    }
}
"@
Add-Type -TypeDefinition $rkHarness -Language CSharp
$rkPassed = [MeleeProbabilityDelegationChecks.Checks]::Run()
"PASS: $rkPassed assertions; extracted production delegation/scope/bonus methods with native-getter and Harmony stubs, plus source-boundary checks. Not a live game/Harmony validation."
