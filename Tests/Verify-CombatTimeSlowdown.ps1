$ErrorActionPreference = 'Stop'
$rkRoot = Split-Path -Parent $PSScriptRoot
$rkFireSource = Get-Content -LiteralPath (Join-Path $rkRoot 'Source/RimKataFireUtility.cs') -Raw -Encoding UTF8
$rkJobSource = Get-Content -LiteralPath (Join-Path $rkRoot 'Source/JobDriver_RimKataAttack.cs') -Raw -Encoding UTF8

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

$rkRequest = Get-CSharpBlock $rkFireSource 'internal static void RequestNormalSpeedForCombat('
$rkMaintain = Get-CSharpBlock $rkJobSource 'private void MaintainCombatNormalSpeedRequest('
$rkCombatTick = Get-CSharpBlock $rkJobSource 'private void CombatTick('

if ($rkFireSource -notmatch 'MethodDelegate<CausesTimeSlowdownDelegate>\(\s*method,\s*null,\s*false,\s*null\s*\)') {
    throw 'CausesTimeSlowdown is not using the cached open-instance production delegate.'
}
if ($rkFireSource -match 'FieldRef<TickManager,\s*TimeSlower>|ResolveTimeSlowerRef' -or
    $rkRequest -notmatch 'TimeSlower\s+slower\s*=\s*tickManager\.slower') {
    throw 'Public TickManager.slower should be read directly without a FieldRef binding.'
}
$rkSignalIndex = $rkRequest.IndexOf('slower.SignalForceNormalSpeed();', [StringComparison]::Ordinal)
$rkManagerRecordIndex = $rkRequest.IndexOf('lastNormalSpeedSignalManager = tickManager;', [StringComparison]::Ordinal)
$rkTickRecordIndex = $rkRequest.IndexOf('lastNormalSpeedSignalTick = tickManager.TicksGame;', [StringComparison]::Ordinal)
if ($rkSignalIndex -lt 0 -or $rkManagerRecordIndex -le $rkSignalIndex -or $rkTickRecordIndex -le $rkSignalIndex) {
    throw 'Same-tick suppression must be recorded only after a real TimeSlower signal.'
}
$rkActiveCheckIndex = $rkCombatTick.IndexOf('if (!RimKataDualWeaponController.IsDedicatedFollowupActive(pawn))', [StringComparison]::Ordinal)
$rkMaintainCallIndex = $rkCombatTick.IndexOf('MaintainCombatNormalSpeedRequest(assignedTarget);', [StringComparison]::Ordinal)
$rkComponentIndex = $rkCombatTick.IndexOf('RimKataMapComponent component', [StringComparison]::Ordinal)
if ($rkJobSource -notmatch 'NormalSpeedRefreshIntervalTicks\s*=\s*600' -or
    $rkMaintain -notmatch 'currentTick\s*<\s*nextNormalSpeedRequestTick' -or
    $rkMaintain -notmatch 'nextNormalSpeedRequestTick\s*=\s*currentTick\s*\+\s*NormalSpeedRefreshIntervalTicks' -or
    $rkActiveCheckIndex -lt 0 -or $rkMaintainCallIndex -le $rkActiveCheckIndex -or
    $rkComponentIndex -le $rkMaintainCallIndex) {
    throw 'RimKata attack jobs do not retain the bounded valid-target speed request.'
}

$rkHarness = @"
using System;

namespace CombatTimeSlowdownChecks
{
    public sealed class Verb
    {
        public bool slows;
    }

    public struct LocalTargetInfo
    {
    }

    public sealed class TimeSlower
    {
        public int signals;
        public void SignalForceNormalSpeed()
        {
            signals++;
        }
    }

    public sealed class TickManager
    {
        public int TicksGame;
        public TimeSlower slower;
    }

    public static class Find
    {
        public static TickManager TickManager;
    }

    public static class RimKataVerbUtility
    {
        private delegate bool CausesTimeSlowdownDelegate(
            Verb verb,
            LocalTargetInfo target);

        private static readonly CausesTimeSlowdownDelegate CausesTimeSlowdown =
            EvaluateTimeSlowdown;
        private static TickManager lastNormalSpeedSignalManager;
        private static int lastNormalSpeedSignalTick = -1;
        public static int predicateCalls;

        private static bool EvaluateTimeSlowdown(
            Verb verb,
            LocalTargetInfo target)
        {
            predicateCalls++;
            return verb.slows;
        }

        public static void Reset()
        {
            Find.TickManager = null;
            lastNormalSpeedSignalManager = null;
            lastNormalSpeedSignalTick = -1;
            predicateCalls = 0;
        }

        $rkRequest
    }

    public static class Checks
    {
        private static int checks;

        private static void Check(bool condition, string name)
        {
            if (!condition) throw new Exception("FAIL: " + name);
            checks++;
        }

        public static int Run()
        {
            RimKataVerbUtility.Reset();
            var target = new LocalTargetInfo();
            var manager = new TickManager {
                TicksGame = 100,
                slower = new TimeSlower()
            };
            Find.TickManager = manager;

            RimKataVerbUtility.RequestNormalSpeedForCombat(
                new Verb { slows = false }, target);
            Check(manager.slower.signals == 0 && RimKataVerbUtility.predicateCalls == 1,
                "nonqualifying request does not consume the tick");

            var qualifying = new Verb { slows = true };
            RimKataVerbUtility.RequestNormalSpeedForCombat(qualifying, target);
            Check(manager.slower.signals == 1 && RimKataVerbUtility.predicateCalls == 2,
                "qualifying request signals after a false request in the same tick");

            RimKataVerbUtility.RequestNormalSpeedForCombat(qualifying, target);
            Check(manager.slower.signals == 1 && RimKataVerbUtility.predicateCalls == 2,
                "same manager and tick are suppressed before another predicate call");

            manager.TicksGame++;
            RimKataVerbUtility.RequestNormalSpeedForCombat(qualifying, target);
            Check(manager.slower.signals == 2 && RimKataVerbUtility.predicateCalls == 3,
                "next tick can signal again");

            var replacementManager = new TickManager {
                TicksGame = manager.TicksGame,
                slower = new TimeSlower()
            };
            Find.TickManager = replacementManager;
            RimKataVerbUtility.RequestNormalSpeedForCombat(qualifying, target);
            Check(replacementManager.slower.signals == 1 && RimKataVerbUtility.predicateCalls == 4,
                "new game TickManager is not blocked by the previous game's tick value");

            var missingSlower = new TickManager { TicksGame = 200 };
            Find.TickManager = missingSlower;
            RimKataVerbUtility.RequestNormalSpeedForCombat(qualifying, target);
            Check(RimKataVerbUtility.predicateCalls == 5,
                "missing TimeSlower does not record a successful signal");
            missingSlower.slower = new TimeSlower();
            RimKataVerbUtility.RequestNormalSpeedForCombat(qualifying, target);
            Check(missingSlower.slower.signals == 1 && RimKataVerbUtility.predicateCalls == 6,
                "same-tick request retries after TimeSlower becomes available");

            return checks;
        }
    }
}
"@

Add-Type -TypeDefinition $rkHarness -Language CSharp
$rkPassed = [CombatTimeSlowdownChecks.Checks]::Run()
"PASS: $rkPassed executable TimeSlower assertions + 4 source-boundary groups; production request method with minimal fixtures, not an in-game speed test."
