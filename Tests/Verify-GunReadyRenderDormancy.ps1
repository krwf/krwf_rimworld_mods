$ErrorActionPreference = 'Stop'
$rkRoot = Split-Path -Parent $PSScriptRoot
$rkCombatSource = Get-Content -LiteralPath (Join-Path $rkRoot 'Source/RimKataCombatState.cs') -Raw -Encoding UTF8
$rkVisualSource = Get-Content -LiteralPath (Join-Path $rkRoot 'Source/RimKataVisualPatches.cs') -Raw -Encoding UTF8

function Get-CSharpBlock([string] $source, [string] $marker) {
    $start = $source.IndexOf($marker, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing source block: $marker" }
    $open = $source.IndexOf('{', $start)
    $depth = 0
    for ($index = $open; $index -lt $source.Length; $index++) {
        if ($source[$index] -eq '{') { $depth++ }
        if ($source[$index] -eq '}') {
            $depth--
            if ($depth -eq 0) {
                return $source.Substring($start, $index - $start + 1)
            }
        }
    }
    throw "Unclosed source block: $marker"
}

function Get-Index([string] $source, [string] $marker) {
    $index = $source.IndexOf($marker, [StringComparison]::Ordinal)
    if ($index -lt 0) { throw "Missing source marker: $marker" }
    return $index
}

$rkCache = Get-CSharpBlock $rkCombatSource 'internal static class RimKataCombatStatePresenceCache'
$rkContains = Get-CSharpBlock $rkCache 'public static bool Contains('
$rkGetState = Get-CSharpBlock $rkCombatSource 'public RimKataPawnCombatState GetState('
$rkRebuild = Get-CSharpBlock $rkCombatSource 'private void RebuildStateIndex()'
$rkRemove = Get-CSharpBlock $rkCombatSource 'private void RemoveStateAt('
$rkMapRemoved = Get-CSharpBlock $rkCombatSource 'public override void MapRemoved()'
$rkVisualLoadout = Get-CSharpBlock $rkVisualSource 'private static bool TryGetVisualLoadout('
$rkResponseSnapshot = Get-CSharpBlock $rkVisualSource 'public static bool TryGetCachedResponseSnapshot('
$rkPush = Get-CSharpBlock $rkVisualSource 'public static RimKataGunReadyDrawContext Push('
$rkCandidate = Get-CSharpBlock $rkVisualSource 'private static bool MayNeedGunReadyTarget('

if ($rkCache -notmatch 'ConditionalWeakTable<Pawn, StateMarker>' -or
    $rkCache -notmatch 'public static bool Contains\(Pawn pawn, Map map\)' -or
    $rkCache -notmatch 'internal static void Mark\(Pawn pawn, Map map\)' -or
    $rkCache -notmatch 'internal static void Clear\(Pawn pawn, Map map\)') {
    throw 'Combat-state presence cache no longer has its weak-key marker boundary.'
}

if ($rkContains -match 'GetComponent|GetState|\.Active|RimKataEligibility|CombatVerb') {
    throw 'Presence-cache probe gained a deep combat or eligibility lookup.'
}

$rkCreateMark = Get-Index $rkGetState 'RimKataCombatStatePresenceCache.Mark(pawn, map);'
$rkCreatePublish = Get-Index $rkGetState 'statesByPawn[pawn] = state;'
if ($rkCreateMark -ge $rkCreatePublish) {
    throw 'New combat state is published before its conservative presence marker.'
}

$rkRebuildClear = Get-Index $rkRebuild 'RimKataCombatStatePresenceCache.Clear(indexedPawn, map);'
$rkRebuildDictionaryClear = Get-Index $rkRebuild 'statesByPawn.Clear();'
$rkRebuildMark = Get-Index $rkRebuild 'RimKataCombatStatePresenceCache.Mark(state.pawn, map);'
$rkRebuildPublish = Get-Index $rkRebuild 'statesByPawn[state.pawn] = state;'
if ($rkRebuildClear -ge $rkRebuildDictionaryClear -or
    $rkRebuildMark -ge $rkRebuildPublish) {
    throw 'State-index rebuild no longer synchronizes presence markers conservatively.'
}

if ($rkMapRemoved -notmatch 'RimKataCombatStatePresenceCache\.Clear\([\s\S]*?states\[i\]\?\.pawn,[\s\S]*?map\);') {
    throw 'Map removal no longer clears combat-state presence markers.'
}

$rkRemoveDictionary = Get-Index $rkRemove 'statesByPawn.Remove(state.pawn);'
$rkRemoveMarker = Get-Index $rkRemove 'RimKataCombatStatePresenceCache.Clear(state.pawn, map);'
if ($rkRemoveDictionary -ge $rkRemoveMarker) {
    throw 'Combat-state marker is cleared before the indexed state is removed.'
}

if ($rkCandidate -notmatch 'CurJobDef\s*==\s*RimKataDefOf\.RimKata_Attack' -or
    $rkCandidate -notmatch 'RimKataCombatStatePresenceCache\.Contains\(pawn, pawn\?\.Map\)' -or
    $rkCandidate -match 'Drafted|Moving|Stance|GetComponent|GetState|\.Active|TryGetEnabledCombatVerb') {
    throw 'Gun-ready candidate gate no longer uses only Job or state presence.'
}

$rkContextPublish = Get-Index $rkPush 'current = next;'
$rkDormancyGate = Get-Index $rkPush 'MayNeedGunReadyTarget(pawn)'
$rkComponent = Get-Index $rkPush 'GetComponent<RimKataMapComponent>'
$rkTarget = Get-Index $rkPush 'TryGetGunReadyTarget'
$rkVerb = Get-Index $rkPush 'TryGetEnabledCombatVerb'
if (-not ($rkContextPublish -lt $rkDormancyGate -and
          $rkDormancyGate -lt $rkComponent -and
          $rkComponent -lt $rkTarget -and
          $rkTarget -lt $rkVerb)) {
    throw 'Gun-ready dormancy gate moved across a required render boundary.'
}

if ([regex]::Matches($rkPush, 'TryGetGunReadyTarget').Count -ne 1) {
    throw 'Gun-ready target lookup is no longer a single guarded call.'
}

$rkPrimaryGate = Get-Index $rkPush 'pawn.equipment?.Primary == null'
$rkLoadoutRead = Get-Index $rkPush 'TryGetCachedWorldLoadout('
if ($rkPrimaryGate -ge $rkLoadoutRead) {
    throw 'Unarmed pawn gate moved behind the world-loadout lookup.'
}

if ($rkPush -notmatch 'if \(secondary == null\)[\s\S]*?TryGetResponseParticipantLoadout' -or
    $rkPush -notmatch 'TryGetCachedResponseSnapshot\([\s\S]*?responseParticipant') {
    throw 'Regular dual users no longer defer the response-participant probe.'
}

if ($rkResponseSnapshot -notmatch '!participantKnown[\s\S]*?IsParticipant\(pawn\)' -or
    $rkResponseSnapshot -match 'IsBodyVisualParticipant' -or
    $rkResponseSnapshot -notmatch 'TryGetActiveSnapshot\(pawn, out snapshot\)') {
    throw 'Weapon response snapshot probe regained body-only visual work.'
}

$rkFirstRegisteredRead = Get-Index $rkVisualLoadout 'TryGetRegisteredSecondaryWeapon('
$rkAccessDecision = Get-Index $rkVisualLoadout 'if (!hasAccess)'
$rkCachedFallback = $rkVisualLoadout.IndexOf(
    'if (!cached)',
    $rkAccessDecision,
    [StringComparison]::Ordinal)
if ($rkFirstRegisteredRead -ge $rkAccessDecision -or $rkCachedFallback -le $rkAccessDecision) {
    throw 'World render no longer shares its registered-user and secondary lookup.'
}

$rkHarness = @"
using System;
using System.Runtime.CompilerServices;
using Verse;

namespace Verse
{
    public sealed class Pawn { }
    public sealed class Map { }
}

namespace KRWF.RimKata
{
    $rkCache

    public static class GunReadyPresenceChecks
    {
        private static int checks;

        private static void Check(bool condition, string name)
        {
            if (!condition) throw new Exception("FAIL: " + name);
            checks++;
        }

        public static int Run()
        {
            var pawn = new Pawn();
            var oldMap = new Map();
            var newMap = new Map();
            Check(!RimKataCombatStatePresenceCache.Contains(pawn, oldMap),
                "new pawn has no state marker");
            RimKataCombatStatePresenceCache.Mark(pawn, oldMap);
            Check(RimKataCombatStatePresenceCache.Contains(pawn, oldMap),
                "old map marker is visible");
            RimKataCombatStatePresenceCache.Mark(pawn, newMap);
            Check(!RimKataCombatStatePresenceCache.Contains(pawn, oldMap)
                    && RimKataCombatStatePresenceCache.Contains(pawn, newMap),
                "new map replaces marker ownership");
            RimKataCombatStatePresenceCache.Clear(pawn, oldMap);
            Check(RimKataCombatStatePresenceCache.Contains(pawn, newMap),
                "old map cannot clear new map marker");
            RimKataCombatStatePresenceCache.Clear(pawn, newMap);
            Check(!RimKataCombatStatePresenceCache.Contains(pawn, newMap),
                "owning map clears marker");
            Check(!RimKataCombatStatePresenceCache.Contains(null, newMap)
                    && !RimKataCombatStatePresenceCache.Contains(pawn, null),
                "null cache probes are dormant");
            return checks;
        }
    }
}
"@

Add-Type -TypeDefinition $rkHarness -Language CSharp
$rkPassed = [KRWF.RimKata.GunReadyPresenceChecks]::Run()
"PASS: $rkPassed executable state-presence assertions + 13 gun-ready render dormancy source-boundary assertions; in-game profiler comparison remains required."
