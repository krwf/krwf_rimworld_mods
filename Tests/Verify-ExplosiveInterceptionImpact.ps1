$ErrorActionPreference = 'Stop'
$rkRoot = Split-Path -Parent $PSScriptRoot
$rkFire = Get-Content -LiteralPath (Join-Path $rkRoot 'Source/RimKataFireUtility.cs') -Raw -Encoding UTF8
$rkCombat = Get-Content -LiteralPath (Join-Path $rkRoot 'Source/RimKataCombatState.cs') -Raw -Encoding UTF8
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
$rkPatch = Get-CSharpBlock $rkFire 'public static class Patch_Projectile_Impact_Context'
$rkPrefix = Get-CSharpBlock $rkPatch 'public static bool Prefix('
$rkFinalizer = Get-CSharpBlock $rkPatch 'public static Exception Finalizer('
$rkResolve = Get-CSharpBlock $rkFire 'public static bool TryResolve('
$rkTake = Get-CSharpBlock $rkCombat 'internal bool TryTakeInterceptionTarget('
$rkContext = Get-CSharpBlock $rkFire 'public static class RimKataProjectileImpactContext'

# Production interception/impact entry and pair-consumption methods are compiled
# in memory. Engine impact/fuse and target resolution are deliberately tiny stubs:
# these checks verify dispatch, not actual explosion damage, visuals, or Harmony.
$rkHarness = @"
using System;
using System.Collections.Generic;
namespace ExplosiveInterceptionImpactChecks {
    public enum DestroyMode { Vanish }
    public struct Vector3 {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    }
    public class Thing { public Map Map; }
    public sealed class Pawn : Thing { }
    public sealed class ProjectileProperties { public float explosionRadius; }
    public sealed class ThingDef { public ProjectileProperties projectile = new ProjectileProperties(); }
    public sealed class Projectile : Thing {
        public bool Destroyed, Spawned = true, active = true;
        public bool critical, delayed;
        public int destroys, originals, explosions, scheduled;
        public Thing Launcher;
        public ThingDef def = new ThingDef();
        public void Destroy(DestroyMode mode) { destroys++; Destroyed = true; Spawned = false; Map = null; }
    }
    public sealed class Map {
        public readonly RimKataMapComponent component = new RimKataMapComponent();
        public T GetComponent<T>() where T : class { return component as T; }
    }
    public sealed class RimKataInterceptionShotLink { public Projectile shot, target; }
    public sealed class RimKataMapComponent {
        private readonly object statesLock = new object();
        private readonly Dictionary<Projectile, RimKataInterceptionShotLink> interceptionShotLinksByShot = new Dictionary<Projectile, RimKataInterceptionShotLink>();
        public int finished;
        public void Add(Projectile shot, Projectile target) { interceptionShotLinksByShot[shot] = new RimKataInterceptionShotLink { shot = shot, target = target }; }
        private void RemoveInterceptionShotLink(RimKataInterceptionShotLink link) { interceptionShotLinksByShot.Remove(link.shot); }
        public void NotifyRangedProjectileImpactFinished(Projectile projectile) { finished++; }
        $rkTake
    }
    public static class RimKataTargeting {
        public static bool IsInterceptionTargetActive(Projectile target) { return target.active; }
    }
    public static class RimKataInterceptionTrajectory {
        public static bool hasContact = true;
        public static int calls, placements;
        public static Vector3 contact = new Vector3(3f, 0f, 7f);
        public static Vector3 placedContact;
        public static Projectile placedShot;
        public static bool TryGetContact(Projectile shot, Projectile target, out Vector3 position) {
            calls++;
            position = contact;
            return hasContact;
        }
        public static void PlaceAtContact(Projectile shot, Vector3 position) {
            placements++;
            placedShot = shot;
            placedContact = position;
        }
    }
    public static class RimKataInterceptionUtility {
        public static bool succeeds = true;
        public static int calls, criticalOutcomes;
        public static Vector3 lastContact;
        public static bool placementPrecedesResolution;
        public static Action onResolve;
        public static bool Resolve(Pawn shooter, Projectile target, Vector3 contact) {
            calls++;
            lastContact = contact;
            placementPrecedesResolution = RimKataInterceptionTrajectory.placements == 1;
            if (succeeds && target.critical) criticalOutcomes++;
            if (onResolve != null) onResolve();
            return succeeds;
        }
    }
    public static class RimKataDefenseUtility {
        public static int depth;
        public static void EnterProjectileImpact() { depth++; }
        public static void ExitProjectileImpact() { depth--; }
    }
    public static class RimKataInterceptionShotRegistry { $rkResolve }
    $rkContext
    public static class Patch { $rkPrefix $rkFinalizer }
    public sealed class Fixture {
        public Map map = new Map();
        public Projectile shot = new Projectile(), target = new Projectile();
        public Thing originalHit;
        public Fixture(float radius = 1f) {
            shot.Map = target.Map = map;
            shot.Launcher = new Pawn { Map = map };
            shot.def.projectile.explosionRadius = radius;
            map.component.Add(shot, target);
            RimKataInterceptionUtility.calls = RimKataInterceptionUtility.criticalOutcomes = 0;
            RimKataInterceptionUtility.succeeds = true;
            RimKataInterceptionUtility.onResolve = null;
            RimKataInterceptionUtility.placementPrecedesResolution = false;
            RimKataInterceptionTrajectory.hasContact = true;
            RimKataInterceptionTrajectory.calls = RimKataInterceptionTrajectory.placements = 0;
            RimKataInterceptionTrajectory.placedShot = null;
        }
        public bool Impact(Thing hit, bool blocked = false) {
            bool allowed = Patch.Prefix(shot, ref hit, blocked);
            // Stand-in for the original method only records which branch runs.
            if (allowed) {
                originalHit = hit;
                shot.originals++;
                if (shot.def.projectile.explosionRadius > 0f) {
                    if (shot.delayed) shot.scheduled++; else shot.explosions++;
                }
            }
            Patch.Finalizer(null);
            return allowed;
        }
    }
    public static class Checks {
        private static int checks;
        private static void Check(bool condition, string name) {
            if (!condition) throw new Exception(name);
            checks++;
        }
        public static int Run() {
            var ordinary = new Fixture(0f);
            Check(!ordinary.Impact(ordinary.target) && ordinary.shot.destroys == 1 && ordinary.shot.originals == 0,
                "Successful ordinary interceptor keeps Vanish and skips original impact");
            var explosive = new Fixture();
            Check(explosive.Impact(explosive.target) && explosive.shot.destroys == 0 && explosive.shot.explosions == 1 && RimKataInterceptionUtility.calls == 1,
                "Successful explosive interceptor dispatches its own immediate impact exactly once");
            Check(RimKataInterceptionTrajectory.calls == 1
                    && RimKataInterceptionTrajectory.placements == 1
                    && RimKataInterceptionTrajectory.placedShot == explosive.shot
                    && RimKataInterceptionTrajectory.placedContact.x == RimKataInterceptionTrajectory.contact.x
                    && RimKataInterceptionTrajectory.placedContact.z == RimKataInterceptionTrajectory.contact.z
                    && RimKataInterceptionUtility.placementPrecedesResolution
                    && RimKataInterceptionUtility.lastContact.x == RimKataInterceptionTrajectory.contact.x
                    && RimKataInterceptionUtility.lastContact.z == RimKataInterceptionTrajectory.contact.z,
                "Actual contact is checked once, places the shot, then forwards the same point to target resolution");
            var critical = new Fixture(); critical.target.critical = true;
            Check(critical.Impact(critical.target) && critical.shot.explosions == 1 && RimKataInterceptionUtility.criticalOutcomes == 1,
                "Target critical outcome does not suppress interceptor impact");
            var delayed = new Fixture(); delayed.shot.delayed = true;
            Check(delayed.Impact(delayed.target) && delayed.shot.scheduled == 1 && delayed.shot.explosions == 0,
                "Delayed interceptor dispatches original fuse setup once instead of forcing an immediate explosion");
            // A target explosion/mod callback can invalidate the shot during Resolve.
            var destroyed = new Fixture();
            RimKataInterceptionUtility.onResolve = () => destroyed.shot.Destroy(DestroyMode.Vanish);
            Check(!destroyed.Impact(destroyed.target) && destroyed.shot.destroys == 1 && destroyed.shot.originals == 0,
                "Already destroyed shot neither double-destroys nor enters original impact");
            var despawned = new Fixture();
            RimKataInterceptionUtility.onResolve = () => despawned.shot.Spawned = false;
            Check(!despawned.Impact(despawned.target) && despawned.shot.originals == 0, "Despawned explosive shot skips original");
            var mapless = new Fixture();
            RimKataInterceptionUtility.onResolve = () => mapless.shot.Map = null;
            Check(!mapless.Impact(mapless.target) && mapless.shot.originals == 0, "Mapless explosive shot skips original");
            var blocked = new Fixture();
            Check(blocked.Impact(blocked.target, true) && blocked.originalHit == blocked.target
                    && RimKataInterceptionUtility.calls == 0 && RimKataInterceptionTrajectory.calls == 0
                    && RimKataInterceptionTrajectory.placements == 0,
                "Shield-blocked impact preserves original hit target without contact or target resolution");
            var missed = new Fixture();
            Thing obstacle = new Thing();
            Check(missed.Impact(obstacle) && missed.originalHit == obstacle
                    && RimKataInterceptionUtility.calls == 0 && RimKataInterceptionTrajectory.calls == 0
                    && RimKataInterceptionTrajectory.placements == 0,
                "Wrong hit target preserves original obstacle without contact or target resolution");
            var remote = new Fixture(); RimKataInterceptionTrajectory.hasContact = false;
            Check(remote.Impact(remote.target) && remote.originalHit == null
                    && remote.shot.explosions == 1 && RimKataInterceptionUtility.calls == 0
                    && RimKataInterceptionTrajectory.calls == 1 && RimKataInterceptionTrajectory.placements == 0,
                "Remote reference-only hit becomes a ground impact without resolving the distant target");
            remote.Impact(remote.target);
            Check(RimKataInterceptionTrajectory.calls == 1 && RimKataInterceptionUtility.calls == 0,
                "Failed spatial contact consumes the pair and is not retried during nested or later impact");
            var failed = new Fixture(); RimKataInterceptionUtility.succeeds = false;
            Check(failed.Impact(failed.target) && failed.originalHit == failed.target
                    && failed.shot.destroys == 0 && RimKataInterceptionUtility.calls == 1,
                "Failed target resolution retains original impact");
            var stale = new Fixture(); stale.target.active = false;
            Check(stale.Impact(stale.target) && stale.originalHit == null
                    && RimKataInterceptionUtility.calls == 0 && RimKataInterceptionTrajectory.calls == 0
                    && RimKataInterceptionTrajectory.placements == 0,
                "Inactive target follows original ground impact without remote damage or trajectory work");
            var invalidShooter = new Fixture(); invalidShooter.shot.Launcher = new Thing();
            Check(invalidShooter.Impact(invalidShooter.target) && invalidShooter.originalHit == null
                    && RimKataInterceptionUtility.calls == 0 && RimKataInterceptionTrajectory.calls == 0
                    && RimKataInterceptionTrajectory.placements == 0,
                "Invalid shooter clears the linked target before original impact");
            var otherMap = new Fixture(); otherMap.target.Map = new Map();
            Check(otherMap.Impact(otherMap.target) && otherMap.originalHit == null
                    && RimKataInterceptionUtility.calls == 0 && RimKataInterceptionTrajectory.calls == 0
                    && RimKataInterceptionTrajectory.placements == 0,
                "Target on another map cannot receive a reference-only hit");
            var nested = new Fixture();
            Thing nestedHit = nested.target;
            bool outer = Patch.Prefix(nested.shot, ref nestedHit, false);
            bool inner = Patch.Prefix(nested.shot, ref nestedHit, false);
            var error = new Exception("original failure");
            Exception returned = Patch.Finalizer(error);
            bool innerRestored = RimKataProjectileImpactContext.CurrentProjectile == nested.shot && nested.map.component.finished == 0;
            Patch.Finalizer(null);
            Check(outer && inner && RimKataInterceptionUtility.calls == 1 && returned == error && innerRestored
                    && RimKataProjectileImpactContext.CurrentProjectile == null && RimKataDefenseUtility.depth == 0 && nested.map.component.finished == 1,
                "Consumed pair prevents nested re-interception; finalizers preserve exception and outer impact context");
            var consumedMiss = new Fixture(); consumedMiss.Impact(null);
            consumedMiss.Impact(consumedMiss.target);
            Check(RimKataInterceptionUtility.calls == 0, "Miss consumes pairing, so subsequent impact cannot retroactively intercept");
            var later = new Fixture(); later.shot.delayed = true; later.Impact(later.target);
            later.shot.delayed = false; later.Impact(later.target);
            Check(later.shot.scheduled == 1 && later.shot.explosions == 1 && RimKataInterceptionUtility.calls == 1,
                "Later simulated fuse impact uses consumed-pair original path without repeating interception");
            return checks;
        }
    }
}
"@
Add-Type -TypeDefinition $rkHarness -Language CSharp
$rkPassed = [ExplosiveInterceptionImpactChecks.Checks]::Run()
"PASS: $rkPassed assertions; production interception/impact methods with engine stubs. Not an in-game explosion or Harmony test."
