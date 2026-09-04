$ErrorActionPreference = 'Stop'
$rkRoot = Split-Path -Parent $PSScriptRoot
$rkCombat = Get-Content -LiteralPath (Join-Path $rkRoot 'Source/RimKataCombatState.cs') -Raw -Encoding UTF8
$rkInactivity = Get-Content -LiteralPath (Join-Path $rkRoot 'Source/RimKataTemporaryInactivity.cs') -Raw -Encoding UTF8
$rkRange = Get-Content -LiteralPath (Join-Path $rkRoot 'Source/RimKataRangeUtility.cs') -Raw -Encoding UTF8
$rkController = Get-Content -LiteralPath (Join-Path $rkRoot 'Source/RimKataDualWeaponController.cs') -Raw -Encoding UTF8

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

$rkRequest = Get-CSharpBlock $rkCombat 'internal void RequestTemporaryInactivityUpdate('
$rkGetState = Get-CSharpBlock $rkCombat 'public RimKataPawnCombatState GetState('
$rkWeather = Get-CSharpBlock $rkCombat 'internal void RefreshWeatherRangeRevision('
$rkWeatherGetter = Get-CSharpBlock $rkCombat 'internal int WeatherRangeRevision'
$rkWeatherPatch = Get-CSharpBlock $rkRange 'public static class Patch_WeatherManager_RimKataRangeRevision'
$rkMapTick = Get-CSharpBlock $rkCombat 'public override void MapComponentTick()'
$rkFinalize = Get-CSharpBlock $rkCombat 'public override void FinalizeInit()'
$rkCleanup = Get-CSharpBlock $rkController 'public static void CancelOffenseForMentalState('
if ($rkMapTick.Contains('RefreshWeatherRangeRevision(') -or $rkMapTick.Contains('RimKataTemporaryInactivity.IsInactive(')) {
    throw 'Map tick still polls weather or the inactivity cache.'
}
if (-not $rkFinalize.Contains('RimKataTemporaryInactivity.IsInactive(state.pawn)')) { throw 'Missing load initialization.' }
if ($rkCleanup.Contains('StateFor(') -or $rkCleanup.Contains('temporaryInactive =')) { throw 'Cleanup re-queries or overwrites transition state.' }
$rkCalls = Get-ChildItem -LiteralPath (Join-Path $rkRoot 'Source') -Filter '*.cs' |
    Select-String -Pattern 'CancelOffenseForMentalState\('
if (@($rkCalls).Count -ne 2) { throw 'Expected one cleanup declaration and one map-tick call.' }

# Compile the production inactivity file and selected production map methods in
# memory. Engine stubs exercise notifications and cache transitions, not gameplay.
$rkStubs = @"
namespace HarmonyLib {
    [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
    public sealed class HarmonyPatch : System.Attribute {
        public HarmonyPatch(System.Type type, string method) { }
        public HarmonyPatch(System.Type type, string method, System.Type[] args) { }
    }
}
namespace RimWorld {
    public sealed class MentalStateDef { }
    public sealed class WeatherDef { }
    public sealed class WeatherManager {
        public Verse.Map map;
        public float cap = -1f;
        public int reads;
        public float CurWeatherMaxRangeCap { get { reads++; return cap; } }
        public void TransitionTo(WeatherDef weather) { }
    }
}
namespace Verse {
    public class Thing { }
    public sealed class Game { }
    public static class Current { public static Game Game = new Game(); }
    public static class Find { public static TickManager TickManager = new TickManager(); }
    public sealed class TickManager { public int TicksGame; public void DoSingleTick() { } }
    public sealed class StunHandler {
        public bool Stunned;
        public Thing parent;
        public void StunFor(int ticks, Thing instigator, bool a, bool b, bool c) { }
    }
    public sealed class Stances { public StunHandler stunner = new StunHandler(); }
    public sealed class Pawn : Thing {
        private bool mental;
        public int mentalReads;
        public bool InMentalState { get { mentalReads++; return mental; } set { mental = value; } }
        public bool burning, Destroyed;
        public bool Spawned = true;
        public Map Map;
        public Stances stances = new Stances();
        public bool IsBurning() { return burning; }
        public T TryGetComp<T>() where T : class { return null; }
        public void SpawnSetup(Map map, bool respawningAfterLoad) { }
    }
    public class AttachableThing : Thing { public void AttachTo(Thing parent) { } }
    public sealed class Fire : AttachableThing { }
    public sealed class CompAttachBase { public System.Collections.Generic.List<AttachableThing> attachments; }
    public sealed class Map {
        public readonly KRWF.RimKata.RimKataMapComponent component;
        public readonly RimWorld.WeatherManager weatherManager;
        public int componentReads;
        public Map() {
            component = new KRWF.RimKata.RimKataMapComponent(this);
            weatherManager = new RimWorld.WeatherManager { map = this };
        }
        public T GetComponent<T>() where T : class { componentReads++; return component as T; }
    }
}
namespace Verse.AI {
    public sealed class MentalStateHandler { public bool TryStartMentalState() { return true; } }
}
namespace KRWF.RimKata {
    public sealed class RimKataPawnCombatState {
        public readonly Verse.Pawn pawn;
        internal bool temporaryInactive, temporaryInactivityCleanupPending;
        public RimKataPawnCombatState(Verse.Pawn pawn) { this.pawn = pawn; }
    }
    public sealed class RimKataMapComponent {
        private readonly Verse.Map map;
        private readonly object statesLock = new object();
        private readonly System.Collections.Generic.List<RimKataPawnCombatState> states = new System.Collections.Generic.List<RimKataPawnCombatState>();
        private readonly System.Collections.Generic.Dictionary<Verse.Pawn, RimKataPawnCombatState> statesByPawn = new System.Collections.Generic.Dictionary<Verse.Pawn, RimKataPawnCombatState>();
        private bool weatherRangeCapInitialized;
        private float observedWeatherMaxRangeCap;
        private int weatherRangeRevision;
        private int lastWeatherRangeCheckTick = int.MinValue;
        public RimKataMapComponent(Verse.Map map) { this.map = map; }
        public void Remove(Verse.Pawn pawn) { states.Remove(statesByPawn[pawn]); statesByPawn.Remove(pawn); }
        public void Drain(RimKataPawnCombatState state) { state.temporaryInactivityCleanupPending = false; }
        $rkRequest
        $rkGetState
        $rkWeather
        $rkWeatherGetter
    }
    $rkWeatherPatch
    public static class MapEventUpdateChecks {
        private static int checks;
        private static void Check(bool condition, string name) {
            if (!condition) throw new System.Exception(name);
            checks++;
        }
        private static Verse.Pawn Pawn(Verse.Map map) { return new Verse.Pawn { Map = map }; }
        public static int Run() {
            var map = new Verse.Map();
            var pawn = Pawn(map);
            var state = map.component.GetState(pawn, true);
            Check(!state.temporaryInactive && !state.temporaryInactivityCleanupPending, "Active seed");
            pawn.InMentalState = true;
            RimKataTemporaryInactivity.NotifyPotentiallyInactive(pawn);
            Check(state.temporaryInactive && state.temporaryInactivityCleanupPending, "Entry requests cleanup");
            map.component.Drain(state);
            int reads = map.componentReads, mentalReads = pawn.mentalReads;
            for (int i = 0; i < 100; i++) Check(RimKataTemporaryInactivity.IsInactive(pawn), "Cached inactive read");
            Check(map.componentReads == reads && pawn.mentalReads == mentalReads, "Cached reads neither notify nor resample");
            RimKataTemporaryInactivity.NotifyPotentiallyInactive(pawn);
            Check(map.componentReads == reads && !state.temporaryInactivityCleanupPending, "No duplicate entry request");
            pawn.stances.stunner.Stunned = true;
            RimKataTemporaryInactivity.NotifyPotentiallyInactive(pawn);
            pawn.InMentalState = false;
            RimKataTemporaryInactivity.TickInactivePawns();
            Check(state.temporaryInactive, "Overlapping cause stays inactive");
            pawn.stances.stunner.Stunned = false;
            RimKataTemporaryInactivity.TickInactivePawns();
            Check(!state.temporaryInactive && !state.temporaryInactivityCleanupPending, "Last cause recovery");
            reads = map.componentReads;
            RimKataTemporaryInactivity.TickInactivePawns();
            Check(map.componentReads == reads, "No repeated recovery notification");

            pawn.burning = true;
            RimKataTemporaryInactivity.NotifyPotentiallyInactive(pawn);
            pawn.burning = false;
            RimKataTemporaryInactivity.TickInactivePawns();
            Check(!state.temporaryInactive && state.temporaryInactivityCleanupPending, "Recovery preserves queued cleanup");
            map.component.Drain(state);
            Check(!state.temporaryInactive, "Cleanup does not reactivate inactivity");

            var initiallyInactive = Pawn(map);
            initiallyInactive.stances.stunner.Stunned = true;
            Check(RimKataTemporaryInactivity.IsInactive(initiallyInactive), "Inactive cache seed");
            var initialState = map.component.GetState(initiallyInactive, true);
            Check(initialState.temporaryInactive && initialState.temporaryInactivityCleanupPending, "State created after cache seed");
            map.component.Drain(initialState);
            map.component.Remove(initiallyInactive);
            var replacement = map.component.GetState(initiallyInactive, true);
            Check(replacement.temporaryInactive && replacement.temporaryInactivityCleanupPending, "Recreated combat state seed");
            initiallyInactive.Spawned = false;
            initiallyInactive.Map = null;
            RimKataTemporaryInactivity.TickInactivePawns();
            var otherMap = new Verse.Map();
            initiallyInactive.Spawned = true;
            initiallyInactive.Map = otherMap;
            var respawnState = otherMap.component.GetState(initiallyInactive, true);
            map.component.Drain(replacement);
            RimKataTemporaryInactivity.RefreshExisting(initiallyInactive);
            initiallyInactive.stances.stunner.Stunned = false;
            RimKataTemporaryInactivity.TickInactivePawns();
            Check(!respawnState.temporaryInactive, "Inactive-to-inactive respawn re-registers recovery");
            Check(replacement.temporaryInactive, "Notification only reaches current map");

            var unregistered = Pawn(map);
            unregistered.InMentalState = true;
            reads = map.componentReads;
            RimKataTemporaryInactivity.NotifyPotentiallyInactive(unregistered);
            Check(map.componentReads == reads && map.component.GetState(unregistered, false) == null, "Unknown pawn event does not create state");
            var lateState = map.component.GetState(unregistered, true);
            Check(lateState.temporaryInactive && lateState.temporaryInactivityCleanupPending, "Late registration sees missed entry event");
            map.component.Drain(lateState);
            unregistered.InMentalState = false;
            RimKataTemporaryInactivity.TickInactivePawns();
            Check(!lateState.temporaryInactive, "Late registration is recovery-tracked");

            var weatherMap = new Verse.Map();
            weatherMap.component.RefreshWeatherRangeRevision(true);
            Check(weatherMap.component.WeatherRangeRevision == 0 && weatherMap.weatherManager.reads == 1, "Weather initial sample and same-tick guard");
            Verse.Find.TickManager.TicksGame++;
            Check(weatherMap.weatherManager.reads == 1, "Time passage alone does not poll weather");
            Check(weatherMap.component.WeatherRangeRevision == 0 && weatherMap.weatherManager.reads == 2, "On-demand weather sample");
            weatherMap.weatherManager.cap = 12f;
            Patch_WeatherManager_RimKataRangeRevision.Postfix(weatherMap.weatherManager);
            Check(weatherMap.component.WeatherRangeRevision == 1, "Weather transition bypasses same-tick guard");
            Patch_WeatherManager_RimKataRangeRevision.Postfix(weatherMap.weatherManager);
            Check(weatherMap.component.WeatherRangeRevision == 1, "Equal weather cap keeps cache revision");
            Verse.Find.TickManager.TicksGame++;
            weatherMap.weatherManager.cap = 18f;
            Check(weatherMap.component.WeatherRangeRevision == 2, "On-demand fallback detects direct cap changes");
            return checks;
        }
    }
}
"@
Add-Type -TypeDefinition ($rkInactivity + [Environment]::NewLine + $rkStubs) -Language CSharp
$rkPassed = [KRWF.RimKata.MapEventUpdateChecks]::Run()
"PASS: $rkPassed assertions; production transition/cache methods with engine stubs, plus source-boundary checks."
