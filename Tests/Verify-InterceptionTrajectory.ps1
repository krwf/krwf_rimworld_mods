$ErrorActionPreference = 'Stop'
$rkRoot = Split-Path -Parent $PSScriptRoot
$rkTrajectory = Get-Content -LiteralPath (Join-Path $rkRoot 'Source/RimKataInterceptionTrajectory.cs') -Raw -Encoding UTF8
$rkFire = Get-Content -LiteralPath (Join-Path $rkRoot 'Source/RimKataFireUtility.cs') -Raw -Encoding UTF8
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
$rkLaunchGate = Get-CSharpBlock $rkFire 'if (RimKataFireContext.InterceptionShot)'

# Compile the complete production helper, without rewriting its methods. These
# minimal engine stubs reproduce vanilla's linear flight and ref-return fields.
# They do not simulate Harmony patch ordering, collision/cover, or game graphics.
$rkStubs = @'
namespace UnityEngine {
    public struct Vector3 {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public float sqrMagnitude { get { return x*x + y*y + z*z; } }
        public float magnitude { get { return (float)System.Math.Sqrt(sqrMagnitude); } }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x+b.x,a.y+b.y,a.z+b.z); }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z); }
        public static Vector3 operator *(Vector3 a, float b) { return new Vector3(a.x*b,a.y*b,a.z*b); }
        public static Vector3 operator /(Vector3 a, float b) { return new Vector3(a.x/b,a.y/b,a.z/b); }
        public static float Dot(Vector3 a, Vector3 b) { return a.x*b.x+a.y*b.y+a.z*b.z; }
    }
    public static class Mathf {
        public static float Clamp01(float value) { return System.Math.Max(0f,System.Math.Min(1f,value)); }
    }
}
namespace Verse {
    using UnityEngine;
    public sealed class Map { public int size = 1000; }
    public struct IntVec3 {
        public int x, z;
        public IntVec3(int x, int z) { this.x=x; this.z=z; }
        public float DistanceToSquared(IntVec3 other) { float dx=x-other.x, dz=z-other.z; return dx*dx+dz*dz; }
        public static bool operator ==(IntVec3 left, IntVec3 right) { return left.x == right.x && left.z == right.z; }
        public static bool operator !=(IntVec3 left, IntVec3 right) { return !(left == right); }
        public override bool Equals(object value) { return value is IntVec3 && this == (IntVec3)value; }
        public override int GetHashCode() { return x*397 ^ z; }
    }
    public static class VectorExtensions {
        public static Vector3 Yto0(this Vector3 value) { return new Vector3(value.x,0,value.z); }
        public static IntVec3 ToIntVec3(this Vector3 value) { return new IntVec3((int)System.Math.Floor(value.x),(int)System.Math.Floor(value.z)); }
        public static bool InBounds(this Vector3 value, Map map) { return map != null && value.x >= 0 && value.z >= 0 && value.x < map.size && value.z < map.size; }
    }
    public sealed class ProjectileProperties { public float SpeedTilesPerTick = 1; }
    public sealed class ThingDef { public ProjectileProperties projectile = new ProjectileProperties(); public float Altitude = 3; }
    public class Thing {
        public Map Map;
        public int tickDelta;
        public IntVec3 Position;
        public bool Spawned = true, Destroyed;
        public ThingDef def = new ThingDef();
    }
    public class Pawn : Thing { public Vector3 DrawPos; }
    public struct LocalTargetInfo {
        public Thing Thing;
        private IntVec3 cell;
        public LocalTargetInfo(Thing thing) { Thing=thing; cell=default(IntVec3); }
        public LocalTargetInfo(IntVec3 cell) { Thing=null; this.cell=cell; }
        public bool HasThing { get { return Thing != null; } }
        public IntVec3 Cell { get { return HasThing ? Thing.Position : cell; } }
    }
    public class Verb { public Thing EquipmentSource; public float range = 1000; }
    public class Verb_LaunchProjectile : Verb {
        public ThingDef projectileDef = new ThingDef();
        public int reads;
        public virtual ThingDef Projectile { get { reads++; return projectileDef; } }
    }
    public class Projectile : Thing {
        public Vector3 origin, destination;
        public int ticksToImpact, lifetime;
        public bool landed, overridePosition;
        public Vector3 customPosition;
        public LocalTargetInfo usedTarget, intendedTarget;
        public virtual Vector3 ExactPosition {
            get {
                if (overridePosition) return customPosition;
                float duration=(destination-origin).magnitude/def.projectile.SpeedTilesPerTick;
                if (duration <= 0f) duration=0.001f;
                float fraction=Mathf.Clamp01(1f-ticksToImpact/duration);
                Vector3 position=origin.Yto0()+(destination-origin).Yto0()*fraction;
                position.y=def.Altitude;
                return position;
            }
        }
    }
}
namespace HarmonyLib {
    public static class AccessTools {
        public delegate ref F FieldRef<T,F>(T instance);
        public static FieldRef<T,F> FieldRefAccess<T,F>(string name) {
            var field=typeof(T).GetField(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field == null || field.FieldType != typeof(F)) throw new System.InvalidOperationException("Missing fixture field " + name);
            var method=new System.Reflection.Emit.DynamicMethod("FixtureField_"+name, typeof(F).MakeByRefType(), new[] { typeof(T) }, true);
            var il=method.GetILGenerator();
            il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);
            il.Emit(System.Reflection.Emit.OpCodes.Ldflda,field);
            il.Emit(System.Reflection.Emit.OpCodes.Ret);
            return (FieldRef<T,F>)method.CreateDelegate(typeof(FieldRef<T,F>));
        }
    }
}
namespace KRWF.RimKata {
    public static class RimKataTargeting {
        public static bool IsInterceptionTargetActive(Verse.Projectile target) {
            return target != null && target.Spawned && !target.Destroyed && !target.landed && target.Map != null;
        }
    }
    public static class RimKataRangeUtility {
        public static float ResolveEffectiveRange(Verse.Pawn pawn, Verse.Thing equipment, Verse.Verb verb) { return verb.range; }
    }
    public static class RimKataFireContext {
        public static Verse.Pawn Shooter;
        public static Verse.Verb ActiveVerb;
        public static bool InterceptionShot;
        public static Verse.Projectile InterceptionTarget;
    }
    public static class RimKataInterceptionShotRegistry {
        public static int count;
        public static Verse.Projectile shot, target;
        public static void Register(Verse.Projectile registeredShot, Verse.Projectile registeredTarget) { count++; shot=registeredShot; target=registeredTarget; }
    }
}
'@

$rkLaunchHarness = @"
namespace KRWF.RimKata {
    using Verse;
    public static class InterceptionLaunchGate {
        public static void Dispatch(Projectile __instance, LocalTargetInfo usedTarget, LocalTargetInfo intendedTarget) {
            $rkLaunchGate
        }
    }
}
"@

$rkChecks = @'
namespace KRWF.RimKata {
    using System;
    using UnityEngine;
    using Verse;
    public static class InterceptionTrajectoryChecks {
        private static int checks;
        private static Vector3 V(float x, float z=0, float y=0) { return new Vector3(x,y,z); }
        private static void Check(bool condition, string name) {
            checks++;
            if (!condition) throw new InvalidOperationException("FAIL #"+checks+": "+name);
        }
        private static bool Near(Vector3 left, Vector3 right, float tolerance=0.002f) { return (left-right).magnitude <= tolerance; }
        private static RimKataInterceptionTrajectory.Flight Flight(Vector3 from, Vector3 to, double duration, double remaining) {
            return new RimKataInterceptionTrajectory.Flight(from,to,duration,remaining);
        }
        private static Projectile Projectile(Map map, Vector3 from, Vector3 to, float speed, int remaining=-1) {
            int ticks=Math.Max(1,(int)Math.Ceiling((to-from).magnitude/speed));
            return new Projectile { Map=map, origin=from, destination=to, def=new ThingDef { projectile=new ProjectileProperties { SpeedTilesPerTick=speed } }, ticksToImpact=remaining < 0 ? ticks : remaining, lifetime=ticks };
        }
        private static Pawn Pawn(Map map, Vector3 position) { return new Pawn { Map=map, DrawPos=position, Position=position.ToIntVec3() }; }
        private static Verb_LaunchProjectile Verb(float speed=2, float range=1000) {
            return new Verb_LaunchProjectile { projectileDef=new ThingDef { projectile=new ProjectileProperties { SpeedTilesPerTick=speed } }, range=range };
        }
        private static void CheckSolution(Vector3 origin, float speed, RimKataInterceptionTrajectory.Flight flight, int delay, Vector3 expected, string name) {
            Vector3 point; int ticks;
            Check(RimKataInterceptionTrajectory.TryPredict(origin,speed,flight,delay,out point,out ticks),name+" exists");
            double time=(point-origin).magnitude/speed;
            Check(Near(point,expected) && Near(point,flight.PositionAfter(delay+time)),name+" physical crossing");
            Check(ticks == Math.Max(1,(int)Math.Ceiling(time)) && ticks < flight.remaining-delay,name+" vanilla ceil and earlier landing");
        }
        public static int Run() {
            checks=0;
            Vector3 point; int ticks;
            var stationary=Flight(V(10),V(10),100,100);
            CheckSolution(V(0),2,stationary,0,V(10),"stationary target");
            var headOn=Flight(V(20),V(0),20,20);
            CheckSolution(V(0),2,headOn,0,V(40f/3f),"head-on target");
            CheckSolution(V(0),2,headOn,10,V(20f/3f),"warmup delay moves target first");
            Check(RimKataInterceptionTrajectory.TryPredict(V(0),2,headOn,-5,out point,out ticks) && Near(point,V(40f/3f)),"negative warmup is normalized to immediate launch");
            var crossing=Flight(V(10,10),V(10,-90),100,100);
            float crossingTime=(float)((-20+Math.Sqrt(2800))/6);
            CheckSolution(V(0),2,crossing,0,V(10,10-crossingTime),"sideways target");
            CheckSolution(V(0),2,Flight(V(10),V(100),90,90),0,V(20),"slower receding target");
            Check(!RimKataInterceptionTrajectory.TryPredict(V(0),1,Flight(V(10),V(110),50,50),0,out point,out ticks),"faster receding target is unreachable");
            Check(!RimKataInterceptionTrajectory.TryPredict(V(0),1,Flight(V(10),V(100),90,90),0,out point,out ticks),"equal-speed receding target is unreachable");
            CheckSolution(V(0),1,Flight(V(10),V(0),10,10),0,V(5),"equal-speed approaching target");
            Check(!RimKataInterceptionTrajectory.TryPredict(V(0),1,Flight(V(100),V(99),1,1),0,out point,out ticks),"target lands before shot arrives");
            Check(!RimKataInterceptionTrajectory.TryPredict(V(0),2,headOn,20,out point,out ticks),"target lands during warmup");
            Check(!RimKataInterceptionTrajectory.TryPredict(V(0),1,Flight(V(1.1f),V(1.1f),2,2),0,out point,out ticks),"ceil arrival in landing tick is rejected");
            Check(!RimKataInterceptionTrajectory.TryPredict(V(0),1,Flight(V(2),V(2),2,2),0,out point,out ticks),"exact landing boundary is rejected");
            foreach (float invalid in new[] { 0f,-1f,float.NaN,float.PositiveInfinity }) {
                Check(!RimKataInterceptionTrajectory.TryPredict(V(0),invalid,headOn,0,out point,out ticks),"invalid shot speed rejected: "+invalid);
            }
            Check(!RimKataInterceptionTrajectory.TryPredict(V(0),1,Flight(V(10),V(0),double.NaN,10),0,out point,out ticks),"invalid duration rejected");
            var held=Flight(V(0.1f),V(9.3f),9.2,10);
            Check(Near(held.PositionAfter(0),V(0.1f)) && Near(held.PositionAfter(0.7),V(0.1f)),"fractional launch hold keeps target at origin");
            CheckSolution(V(0),1,held,0,V(0.1f),"crossing during fractional launch hold");
            CheckSolution(V(0),2,held,0,V(0.1f),"fast crossing during fractional launch hold");

            var map=new Map();
            var pawn=Pawn(map,V(0,20));
            var verb=Verb(2);
            var target=Projectile(map,V(20,20),V(0,20),1);
            Check(RimKataInterceptionTrajectory.CanIntercept(pawn,verb,target,0),"candidate checks nominal Verb projectile");
            Check(verb.reads == 1,"candidate reads projectile once");
            Check(!RimKataInterceptionTrajectory.CanIntercept(pawn,new Verb(),target,0),"non-projectile Verb rejected");
            Check(!RimKataInterceptionTrajectory.CanIntercept(pawn,verb,Projectile(new Map(),V(20,20),V(0,20),1),0),"cross-map target rejected");
            verb.range=12;
            Check(!RimKataInterceptionTrajectory.CanIntercept(pawn,verb,target,0),"predicted point outside full weapon range rejected");
            verb.range=1000;
            var departing=Projectile(map,V(10,20),V(100,20),1);
            var slowVerb=Verb(0.5f);
            Check(!RimKataInterceptionTrajectory.CanIntercept(pawn,slowVerb,departing,0),"nominal slow ammunition cannot catch departing target");
            var fastActual=Projectile(map,pawn.DrawPos,departing.ExactPosition.Yto0(),2);
            fastActual.usedTarget=fastActual.intendedTarget=new LocalTargetInfo(departing);
            Vector3 originalOrigin=fastActual.origin;
            Check(RimKataInterceptionTrajectory.TryRedirectHit(fastActual,departing,pawn,slowVerb),"actual launched ammunition speed used instead of Verb sample");
            Check(Near(fastActual.destination,V(20,20)) && fastActual.ticksToImpact == 10 && fastActual.lifetime == 10,"actual speed sets predicted destination and both saved tick fields");
            Check(Near(fastActual.origin,originalOrigin) && fastActual.usedTarget.Thing == departing && fastActual.intendedTarget.Thing == departing,"redirect preserves origin and target reference pair");
            var slowActual=Projectile(map,pawn.DrawPos,departing.ExactPosition.Yto0(),0.5f);
            slowActual.usedTarget=slowActual.intendedTarget=new LocalTargetInfo(departing);
            Vector3 oldDestination=slowActual.destination;
            int oldTicks=slowActual.ticksToImpact, oldLifetime=slowActual.lifetime;
            Check(!RimKataInterceptionTrajectory.TryRedirectHit(slowActual,departing,pawn,verb),"actual slow shot cannot use nominal fast speed");
            Check(Near(slowActual.destination,oldDestination) && slowActual.ticksToImpact == oldTicks && slowActual.lifetime == oldLifetime,"failed solve does not alter already launched flight");
            RimKataInterceptionTrajectory.ReleaseUnreachableHit(slowActual);
            Check(!slowActual.usedTarget.HasThing && slowActual.intendedTarget.Thing == departing,"unreachable hit releases only used target Thing");
            Check(slowActual.usedTarget.Cell.x == oldDestination.ToIntVec3().x && Near(slowActual.destination,oldDestination) && slowActual.ticksToImpact == oldTicks && !slowActual.Destroyed,"unreachable shot keeps original ground destination and flight");

            RimKataFireContext.Shooter=pawn; RimKataFireContext.ActiveVerb=verb;
            RimKataFireContext.InterceptionShot=true; RimKataFireContext.InterceptionTarget=departing;
            RimKataInterceptionShotRegistry.count=0;
            var classifiedHit=Projectile(map,pawn.DrawPos,departing.ExactPosition.Yto0(),2);
            classifiedHit.usedTarget=classifiedHit.intendedTarget=new LocalTargetInfo(departing);
            InterceptionLaunchGate.Dispatch(classifiedHit,classifiedHit.usedTarget,classifiedHit.intendedTarget);
            Check(RimKataInterceptionShotRegistry.count == 1 && RimKataInterceptionShotRegistry.shot == classifiedHit && RimKataInterceptionShotRegistry.target == departing && Near(classifiedHit.destination,V(20,20)),"production launch gate redirects and registers only solved hit");
            var classifiedMiss=Projectile(map,pawn.DrawPos,V(8,24),2);
            classifiedMiss.usedTarget=new LocalTargetInfo(new IntVec3(8,24));
            classifiedMiss.intendedTarget=new LocalTargetInfo(departing);
            Vector3 missDestination=classifiedMiss.destination; int missTicks=classifiedMiss.ticksToImpact;
            InterceptionLaunchGate.Dispatch(classifiedMiss,classifiedMiss.usedTarget,classifiedMiss.intendedTarget);
            Check(RimKataInterceptionShotRegistry.count == 1 && Near(classifiedMiss.destination,missDestination) && classifiedMiss.ticksToImpact == missTicks && classifiedMiss.lifetime == missTicks && !classifiedMiss.usedTarget.HasThing && classifiedMiss.intendedTarget.Thing == departing,"production launch gate leaves vanilla cell miss entirely unchanged");
            var cover=new Thing { Map=map };
            classifiedMiss.usedTarget=new LocalTargetInfo(cover);
            InterceptionLaunchGate.Dispatch(classifiedMiss,classifiedMiss.usedTarget,classifiedMiss.intendedTarget);
            Check(RimKataInterceptionShotRegistry.count == 1 && classifiedMiss.usedTarget.Thing == cover && Near(classifiedMiss.destination,missDestination),"production launch gate preserves cover hit rather than promotes it");
            var unreachableHit=Projectile(map,pawn.DrawPos,departing.ExactPosition.Yto0(),0.5f);
            unreachableHit.usedTarget=unreachableHit.intendedTarget=new LocalTargetInfo(departing);
            Vector3 unreachableDestination=unreachableHit.destination; int unreachableTicks=unreachableHit.ticksToImpact;
            InterceptionLaunchGate.Dispatch(unreachableHit,unreachableHit.usedTarget,unreachableHit.intendedTarget);
            Check(RimKataInterceptionShotRegistry.count == 1 && !unreachableHit.usedTarget.HasThing && unreachableHit.intendedTarget.Thing == departing && Near(unreachableHit.destination,unreachableDestination) && unreachableHit.ticksToImpact == unreachableTicks,"production launch gate sends unsolved hit to original ground flight without registration");
            RimKataFireContext.InterceptionShot=false;
            classifiedMiss.usedTarget=new LocalTargetInfo(departing);
            InterceptionLaunchGate.Dispatch(classifiedMiss,classifiedMiss.usedTarget,classifiedMiss.intendedTarget);
            Check(RimKataInterceptionShotRegistry.count == 1 && Near(classifiedMiss.destination,missDestination) && classifiedMiss.usedTarget.Thing == departing,"production launch gate ignores ordinary firing context");

            var custom=Projectile(map,V(20,20),V(0,20),1);
            custom.overridePosition=true; custom.customPosition=V(19,21);
            Check(!RimKataInterceptionTrajectory.CanIntercept(pawn,verb,custom,0),"nonlinear target sample rejected rather than coerced");
            var customShot=Projectile(map,pawn.DrawPos,V(20,20),2);
            customShot.overridePosition=true; customShot.customPosition=V(1,21);
            Check(!RimKataInterceptionTrajectory.TryRedirectHit(customShot,target,pawn,verb),"already displaced custom shot rejected");
            target.landed=true;
            Check(!RimKataInterceptionTrajectory.CanIntercept(pawn,verb,target,0),"landed target rejected");
            target.landed=false;
            target.tickDelta=3;
            Check(RimKataInterceptionTrajectory.CanIntercept(pawn,verb,target,0),"pending target ticks still permit feasible intercept");
            var pendingShot=Projectile(map,pawn.DrawPos,target.ExactPosition.Yto0(),2);
            Check(RimKataInterceptionTrajectory.TryRedirectHit(pendingShot,target,pawn,verb) && Near(pendingShot.destination,V(34f/3f,20)),"deferred target tickDelta included before prediction");
            target.tickDelta=20;
            Check(!RimKataInterceptionTrajectory.CanIntercept(pawn,verb,target,0),"pending landing is not treated as airborne");

            target=Projectile(map,V(20,20),V(0,20),1);
            var contactShot=Projectile(map,pawn.DrawPos,V(20,20),2);
            Check(RimKataInterceptionTrajectory.TryRedirectHit(contactShot,target,pawn,verb),"contact fixture redirected");
            Vector3 intercept=contactShot.destination;
            int arrival=contactShot.ticksToImpact;
            Check(!RimKataInterceptionTrajectory.TryGetContact(contactShot,target,out point),"pre-arrival reference match is not contact");
            contactShot.ticksToImpact=0; target.ticksToImpact-=arrival;
            Check(RimKataInterceptionTrajectory.TryGetContact(contactShot,target,out point) && Near(point.Yto0(),intercept),"actual crossing accepts ceil arrival discrepancy");
            target.ticksToImpact=20;
            Check(!RimKataInterceptionTrajectory.TryGetContact(contactShot,target,out point),"remote matching reference does not resolve interception");
            target.tickDelta=arrival;
            Check(RimKataInterceptionTrajectory.TryGetContact(contactShot,target,out point),"deferred target movement reconciles actual arrival");
            target.tickDelta=0; target.ticksToImpact=20-15; contactShot.ticksToImpact=arrival-15;
            Check(RimKataInterceptionTrajectory.TryGetContact(contactShot,target,out point),"negative remaining from offscreen interval samples original arrival");
            target.ticksToImpact=20; target.tickDelta=15;
            Check(RimKataInterceptionTrajectory.TryGetContact(contactShot,target,out point),"offscreen shot overshoot plus deferred target update combine correctly");
            target.tickDelta=0; target.ticksToImpact=20-arrival;
            contactShot.ticksToImpact=0;
            target.origin=V(20,22); target.destination=V(0,22);
            Check(!RimKataInterceptionTrajectory.TryGetContact(contactShot,target,out point),"parallel path two cells away is not actual crossing");
            target.origin=V(20,20); target.destination=V(0,20);
            contactShot.overridePosition=true; contactShot.customPosition=V(5,20);
            Check(!RimKataInterceptionTrajectory.TryGetContact(contactShot,target,out point),"impact before redirected destination is not arrival contact");
            contactShot.overridePosition=false;

            var earlyShot=Projectile(map,V(0,10),V(20,10),2,4);
            var earlyTarget=Projectile(map,V(11,0),V(11,20),2,5);
            earlyTarget.Position=new IntVec3(11,10);
            Check(RimKataInterceptionTrajectory.TryGetContact(earlyShot,earlyTarget,out point) && Near(point.Yto0(),V(11,10)),"early vanilla cell collision confirms simultaneous crossing before planned destination");
            earlyTarget.Position=new IntVec3(10,10);
            Check(!RimKataInterceptionTrajectory.TryGetContact(earlyShot,earlyTarget,out point),"early contact cannot skip beyond the cell vanilla collision already checked");
            earlyTarget.Position=new IntVec3(11,10);
            earlyTarget.ticksToImpact=2;
            Check(!RimKataInterceptionTrajectory.TryGetContact(earlyShot,earlyTarget,out point),"early reference match fails when target crossed several ticks earlier");
            var intervalShot=Projectile(map,V(0,10),V(100,10),2,35);
            intervalShot.tickDelta=15;
            var intervalTarget=Projectile(map,V(10,0),V(10,100),1,89);
            Check(!RimKataInterceptionTrajectory.TryGetContact(intervalShot,intervalTarget,out point),"intersecting long offscreen paths at different times are not contact");
            var expiredEarlyTarget=Projectile(map,V(30,0),V(30,10),1,1);
            expiredEarlyTarget.tickDelta=15;
            Check(!RimKataInterceptionTrajectory.TryGetContact(intervalShot,expiredEarlyTarget,out point),"early contact cannot hit the clamped ground point after deferred target landing");
            earlyTarget.ticksToImpact=5;
            Check(RimKataInterceptionTrajectory.TryGetContact(earlyShot,earlyTarget,out point),"early contact can be resolved again for placement fixture");
            Vector3 placement=point;
            Vector3 earlyOrigin=earlyShot.origin;
            RimKataInterceptionTrajectory.PlaceAtContact(earlyShot,placement);
            Check(Near(earlyShot.destination,placement.Yto0()) && earlyShot.ticksToImpact == 0 && earlyShot.lifetime == 0 && earlyShot.Position.x == placement.ToIntVec3().x && earlyShot.Position.z == placement.ToIntVec3().z,"confirmed contact aligns destination cell and impact timing for own explosion");
            Check(Near(earlyShot.origin,earlyOrigin) && Near(earlyShot.ExactPosition.Yto0(),placement.Yto0()) && !earlyShot.Destroyed,"contact placement preserves original shot and yields matching exact impact position");

            var historicalTarget=Projectile(map,V(20,20),V(0,20),1,10);
            var historicalShot=Projectile(map,pawn.DrawPos,historicalTarget.ExactPosition.Yto0(),2);
            Check(RimKataInterceptionTrajectory.TryRedirectHit(historicalShot,historicalTarget,pawn,verb),"historical contact fixture has interception before landing");
            int historicalArrival=historicalShot.ticksToImpact;
            historicalShot.ticksToImpact-=15;
            historicalTarget.tickDelta=15;
            Check(RimKataInterceptionTrajectory.TryGetContact(historicalShot,historicalTarget,out point),"still-spawned deferred target past nominal landing can confirm earlier contact");
            Check(!RimKataInterceptionTrajectory.CanIntercept(pawn,verb,historicalTarget,0),"historical contact exception does not reopen candidate after nominal landing");
            historicalTarget.ticksToImpact=1;
            Check(!RimKataInterceptionTrajectory.TryGetContact(historicalShot,historicalTarget,out point),"contact cannot be backdated after target had already landed");

            var savedShot=Projectile(map,pawn.DrawPos,V(20,20),2);
            var savedTarget=Projectile(map,V(20,20),V(0,20),1);
            Check(RimKataInterceptionTrajectory.TryRedirectHit(savedShot,savedTarget,pawn,verb),"save fixture redirected");
            var restored=Projectile(map,savedShot.origin,savedShot.destination,savedShot.def.projectile.SpeedTilesPerTick,savedShot.ticksToImpact);
            restored.lifetime=savedShot.lifetime;
            bool same=true;
            for (int elapsed=0; elapsed<=savedShot.lifetime; elapsed++) {
                savedShot.ticksToImpact=restored.ticksToImpact=savedShot.lifetime-elapsed;
                same &= Near(savedShot.ExactPosition,restored.ExactPosition);
            }
            Check(same,"restoring native origin destination lifetime remaining reconstructs identical flight");
            savedTarget.ticksToImpact-=savedShot.lifetime;
            Vector3 savedContact, restoredContact;
            Check(RimKataInterceptionTrajectory.TryGetContact(savedShot,savedTarget,out savedContact) && RimKataInterceptionTrajectory.TryGetContact(restored,savedTarget,out restoredContact) && Near(savedContact,restoredContact),"restored native fields preserve spatial-contact result");
            return checks;
        }
    }
}
'@

Add-Type -TypeDefinition ($rkTrajectory + "`n" + $rkStubs + "`n" + $rkLaunchHarness + "`n" + $rkChecks) -Language CSharp
$rkPassed = [KRWF.RimKata.InterceptionTrajectoryChecks]::Run()
"PASS: $rkPassed assertions; complete production trajectory helper and launch interception gate with linear engine stubs. Not an in-game collision, rendering, or serialization test."
