param(
    [string] $HarmonyPath = 'C:/Program Files (x86)/Steam/steamapps/workshop/content/294100/2009463077/Current/Assemblies/0Harmony.dll',
    [switch] $UseLegacySignature
)

$ErrorActionPreference = 'Stop'
$rkRoot = Split-Path -Parent $PSScriptRoot
$rkSource = Get-Content -LiteralPath (Join-Path $rkRoot 'Source/RimKataFireUtility.cs') -Raw -Encoding UTF8
$rkMarker = 'public static class Patch_Projectile_Impact_Context'
$rkStart = $rkSource.IndexOf($rkMarker, [StringComparison]::Ordinal)
if ($rkStart -lt 0) { throw "Missing source class: $rkMarker" }
$rkOpen = $rkSource.IndexOf('{', $rkStart)
$rkDepth = 0
$rkPatch = $null
for ($rkIndex = $rkOpen; $rkIndex -lt $rkSource.Length; $rkIndex++) {
    if ($rkSource[$rkIndex] -eq '{') { $rkDepth++ }
    if ($rkSource[$rkIndex] -eq '}') {
        $rkDepth--
        if ($rkDepth -eq 0) {
            $rkPatch = $rkSource.Substring($rkStart, $rkIndex - $rkStart + 1)
            break
        }
    }
}
if ($null -eq $rkPatch) { throw "Unclosed source class: $rkMarker" }
if ($UseLegacySignature) {
    $rkFixed = 'new[] { typeof(Projectile), typeof(Thing).MakeByRefType(), typeof(bool) }'
    if (-not $rkPatch.Contains($rkFixed)) { throw 'Cannot inject legacy signature: fixed registration not found.' }
    $rkPatch = $rkPatch.Replace($rkFixed, 'new[] { typeof(Projectile), typeof(Thing), typeof(bool) }')
}

# The installed Harmony supplies its REAL AccessTools reflection implementation.
# Its MonoMod runtime detours cannot initialize under the installed pwsh/.NET 10
# (ReflectionHelper/SignatureHelper MethodAccessException), so registration and
# dispatch below are explicitly reflection-backed fixtures, NOT real detours.
# No game assembly, startup, map, or save is loaded. The COMPLETE production patch
# class is compiled unchanged (except the opt-in negative regression test).
# Run in a fresh pwsh process; -UseLegacySignature MUST fail at Apply registration.
Add-Type -Path $HarmonyPath
$rkHarness = @"
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
namespace ProjectileImpactRegistrationChecks {
    public enum HarmonyPatchType { All }
    public sealed class Patch { public string owner; public MethodInfo PatchMethod; }
    public sealed class Patches {
        public readonly List<Patch> Prefixes = new List<Patch>();
        public readonly List<Patch> Finalizers = new List<Patch>();
    }
    public sealed class HarmonyMethod {
        public readonly MethodInfo method;
        public HarmonyMethod(MethodInfo value) { method = value; }
    }
    public sealed class Harmony {
        private static readonly Dictionary<MethodBase, Patches> registered = new Dictionary<MethodBase, Patches>();
        public string Id;
        public Harmony(string id) { Id = id; }
        public static Patches GetPatchInfo(MethodBase method) {
            Patches result;
            return registered.TryGetValue(method, out result) ? result : null;
        }
        public void Patch(MethodBase target, HarmonyMethod prefix = null, HarmonyMethod finalizer = null) {
            var info = GetPatchInfo(target);
            if (info == null) registered[target] = info = new Patches();
            if (prefix != null) info.Prefixes.Add(new Patch { owner = Id, PatchMethod = prefix.method });
            if (finalizer != null) info.Finalizers.Add(new Patch { owner = Id, PatchMethod = finalizer.method });
        }
        public void Unpatch(MethodBase target, MethodInfo patch) {
            var info = GetPatchInfo(target);
            if (info == null) return;
            info.Prefixes.RemoveAll(p => p.PatchMethod == patch);
            info.Finalizers.RemoveAll(p => p.PatchMethod == patch);
        }
        public void Unpatch(MethodBase target, HarmonyPatchType type, string owner) {
            var info = GetPatchInfo(target);
            if (info == null) return;
            info.Prefixes.RemoveAll(p => p.owner == owner);
            info.Finalizers.RemoveAll(p => p.owner == owner);
        }
    }
    public static class FixtureDispatch {
        public static void Invoke(Projectile shot, Thing hit, bool shield) {
            var original = AccessTools.Method(shot.GetType(), "Impact", new[] { typeof(Thing), typeof(bool) });
            var info = Harmony.GetPatchInfo(original);
            if (info == null) throw new Exception("Missing registered fixture method");
            Exception error = null;
            try {
                bool runOriginal = true;
                foreach (var prefix in info.Prefixes) {
                    object[] args = { shot, hit, shield };
                    runOriginal &= (bool)prefix.PatchMethod.Invoke(null, args);
                    hit = (Thing)args[1];
                }
                if (runOriginal) original.Invoke(shot, new object[] { hit, shield });
            }
            catch (TargetInvocationException failure) { error = failure.InnerException; }
            finally {
                foreach (var finalizer in info.Finalizers)
                    error = (Exception)finalizer.PatchMethod.Invoke(null, new object[] { error });
            }
            if (error != null) throw error;
        }
    }
    public enum DestroyMode { Vanish }
    public class Thing { }
    public sealed class ProjectileProperties { public float explosionRadius; }
    public sealed class ThingDef { public ProjectileProperties projectile = new ProjectileProperties(); }
    public class Projectile : Thing {
        public bool Destroyed, Spawned = true, ThrowOnImpact;
        public object Map = new object();
        public ThingDef def = new ThingDef();
        public Thing Received;
        public bool ReceivedShield;
        public int OriginalCalls;
        public void Destroy(DestroyMode mode) { Destroyed = true; Spawned = false; Map = null; }
        [MethodImpl(MethodImplOptions.NoInlining)]
        protected virtual void Impact(Thing hit, bool blockedByShield) {
            OriginalCalls++;
            Received = hit;
            ReceivedShield = blockedByShield;
            if (ThrowOnImpact) throw new InvalidOperationException("fixture impact failure");
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Invoke(Thing hit, bool shield) { FixtureDispatch.Invoke(this, hit, shield); }
    }
    public sealed class OverriddenProjectile : Projectile {
        [MethodImpl(MethodImplOptions.NoInlining)]
        protected override void Impact(Thing hit, bool shield) { base.Impact(hit, shield); }
    }
    public sealed class InheritedProjectile : Projectile { }
    public abstract class AbstractProjectile : Projectile {
        protected abstract override void Impact(Thing hit, bool shield);
    }
    public static class Log {
        public static readonly List<string> Warnings = new List<string>();
        public static void Warning(string value) { Warnings.Add(value); }
    }
    public static class StringExtensions {
        public static bool NullOrEmpty(this string value) { return String.IsNullOrEmpty(value); }
    }
    public static class RimKataProjectileImpactContext {
        public static int Depth, Enters, Exits;
        public static void Enter(Projectile projectile) { Depth++; Enters++; }
        public static void Exit() { Depth--; Exits++; }
        public static void Reset() { Depth = Enters = Exits = 0; }
    }
    public static class RimKataInterceptionShotRegistry {
        public static Thing Replacement;
        public static bool Resolve;
        public static int Calls;
        public static bool TryResolve(Projectile shot, ref Thing hit, bool blockedByShield) {
            Calls++;
            if (Replacement != null) hit = Replacement;
            return Resolve;
        }
    }
    $rkPatch
    public static class Runner {
        private static int checks;
        private static void Check(bool condition, string label) {
            checks++;
            if (!condition) throw new Exception("FAIL: " + label);
        }
        private static MethodInfo Impact(Type type) {
            return type.GetMethod("Impact", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null, new[] { typeof(Thing), typeof(bool) }, null);
        }
        private static void CheckPatches(MethodInfo method, Harmony harmony, MethodInfo prefix, MethodInfo finalizer) {
            var info = Harmony.GetPatchInfo(method);
            Check(info != null, "patch info exists: " + method.DeclaringType.Name);
            Check(info.Prefixes.Count(p => p.owner == harmony.Id && p.PatchMethod == prefix) == 1,
                "one exact prefix: " + method.DeclaringType.Name);
            Check(info.Finalizers.Count(p => p.owner == harmony.Id && p.PatchMethod == finalizer) == 1,
                "one exact finalizer: " + method.DeclaringType.Name);
        }
        public static string Run() {
            var patchType = typeof(Patch_Projectile_Impact_Context);
            var prefix = AccessTools.Method(patchType, "Prefix", new[] {
                typeof(Projectile), typeof(Thing).MakeByRefType(), typeof(bool) });
            var finalizer = AccessTools.Method(patchType, "Finalizer", new[] { typeof(Exception) });
            Check(prefix != null && prefix.GetParameters()[1].ParameterType.IsByRef, "exact by-ref prefix resolves");
            Check(AccessTools.Method(patchType, "Prefix", new[] { typeof(Projectile), typeof(Thing), typeof(bool) }) == null,
                "legacy value signature cannot resolve prefix");
            Check(finalizer != null, "finalizer resolves");
            var baseImpact = Impact(typeof(Projectile));
            var overrideImpact = Impact(typeof(OverriddenProjectile));
            Check(baseImpact != null && !baseImpact.GetParameters()[0].ParameterType.IsByRef,
                "original Impact argument remains by value");
            var discovery = AccessTools.Method(patchType, "FindTargetMethods");
            var targets = ((IEnumerable<MethodBase>)discovery.Invoke(null, null)).ToArray();
            Check(targets.Length == 2, "only concrete declared base and override discovered");
            Check(targets.Contains(baseImpact) && targets.Contains(overrideImpact), "base and override present");
            Check(!targets.Contains(Impact(typeof(AbstractProjectile))), "abstract declaration excluded");
            bool nullRejected = false;
            try { Patch_Projectile_Impact_Context.Apply(null); }
            catch (ArgumentNullException) { nullRejected = true; }
            Check(nullRejected, "null Harmony rejected");
            var harmony = new Harmony("rimkata.tests.projectile-impact-registration");
            try {
                Patch_Projectile_Impact_Context.Apply(harmony);
                Check(Log.Warnings.Count == 0, "registration produced no skipped-patch warning");
                CheckPatches(baseImpact, harmony, prefix, finalizer);
                CheckPatches(overrideImpact, harmony, prefix, finalizer);
                Patch_Projectile_Impact_Context.Apply(harmony);
                CheckPatches(baseImpact, harmony, prefix, finalizer);
                CheckPatches(overrideImpact, harmony, prefix, finalizer);
                harmony.Unpatch(baseImpact, prefix);
                Patch_Projectile_Impact_Context.Apply(harmony);
                CheckPatches(baseImpact, harmony, prefix, finalizer);
                Check(Log.Warnings.Count == 0, "idempotent and partial repair registrations have no warnings");

                var original = new Thing();
                var replacement = new Thing();
                RimKataInterceptionShotRegistry.Replacement = replacement;
                RimKataProjectileImpactContext.Reset();
                var normal = new Projectile();
                normal.Invoke(original, true);
                Check(normal.OriginalCalls == 1 && ReferenceEquals(normal.Received, replacement),
                    "registered ref argument rewrite reaches fixture original");
                Check(normal.ReceivedShield, "shield argument preserved");
                Check(RimKataProjectileImpactContext.Depth == 0 && RimKataProjectileImpactContext.Enters == 1
                    && RimKataProjectileImpactContext.Exits == 1, "normal finalizer balances context");

                RimKataProjectileImpactContext.Reset();
                var nested = new OverriddenProjectile();
                nested.Invoke(original, false);
                Check(nested.OriginalCalls == 1 && ReferenceEquals(nested.Received, replacement), "override calls rewritten base");
                Check(RimKataProjectileImpactContext.Depth == 0 && RimKataProjectileImpactContext.Enters == 1
                    && RimKataProjectileImpactContext.Exits == 1, "override finalizer balances");

                RimKataProjectileImpactContext.Reset();
                var failing = new OverriddenProjectile { ThrowOnImpact = true };
                bool originalError = false;
                try { failing.Invoke(original, false); }
                catch (InvalidOperationException error) { originalError = error.Message == "fixture impact failure"; }
                Check(originalError, "original exception remains visible");
                Check(RimKataProjectileImpactContext.Depth == 0 && RimKataProjectileImpactContext.Enters == 1
                    && RimKataProjectileImpactContext.Exits == 1, "throwing override finalizer balances");

                RimKataProjectileImpactContext.Reset();
                RimKataInterceptionShotRegistry.Resolve = true;
                var absorbed = new Projectile();
                absorbed.Invoke(original, false);
                Check(absorbed.Destroyed && absorbed.OriginalCalls == 0, "successful nonexplosive interception skips original");
                Check(RimKataProjectileImpactContext.Depth == 0 && RimKataProjectileImpactContext.Exits == 1,
                    "skipped original still finalizes");
                var explosive = new Projectile();
                explosive.def.projectile.explosionRadius = 1f;
                explosive.Invoke(original, false);
                Check(!explosive.Destroyed && explosive.OriginalCalls == 1 && ReferenceEquals(explosive.Received, replacement),
                    "successful explosive interception preserves rewritten original impact");
                return "PASS: " + checks + " registration/dispatch assertions (real AccessTools; reflection-backed registry/engine fixtures, not runtime detours).";
            }
            finally {
                foreach (var target in targets) harmony.Unpatch(target, HarmonyPatchType.All, harmony.Id);
            }
        }
    }
}
"@
$rkReferences = @($HarmonyPath, 'mscorlib', 'System.Runtime', 'System.Collections', 'System.Linq')
Add-Type -TypeDefinition $rkHarness -ReferencedAssemblies $rkReferences
[ProjectileImpactRegistrationChecks.Runner]::Run()
