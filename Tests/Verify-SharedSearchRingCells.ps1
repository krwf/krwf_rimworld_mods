$ErrorActionPreference = 'Stop'
$rkRoot = Split-Path -Parent $PSScriptRoot
$rkSharedSearchSource = Get-Content -LiteralPath (
    Join-Path $rkRoot 'Source/RimKataSharedTargetSearch.cs'
) -Raw -Encoding UTF8
$rkTargetingSource = Get-Content -LiteralPath (
    Join-Path $rkRoot 'Source/RimKataTargeting.cs'
) -Raw -Encoding UTF8
$rkDebugHudSource = Get-Content -LiteralPath (
    Join-Path $rkRoot 'Source/RimKataDebugHUD.cs'
) -Raw -Encoding UTF8

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

$rkCollectRingCells = Get-CSharpBlock $rkSharedSearchSource `
    'private static void CollectAutomaticTargetsInRingCells('
$rkCollectRingRow = Get-CSharpBlock $rkSharedSearchSource `
    'private static void CollectAutomaticTargetsInRingRow('
$rkCollectRingSegment = Get-CSharpBlock $rkSharedSearchSource `
    'private static void CollectAutomaticTargetsInRingRowSegment('
$rkCollectCell = Get-CSharpBlock $rkSharedSearchSource `
    'private static void CollectAutomaticTargetsInCell('
$rkValidateAttackTarget = Get-CSharpBlock $rkTargetingSource `
    'private static bool IsValidAttackTarget('
$rkBeginHudRecording = Get-CSharpBlock $rkDebugHudSource `
    'internal static bool TryBeginActualSearchCellRecording('
$rkRecordHudCell = Get-CSharpBlock $rkDebugHudSource `
    'internal static void RecordActualSearchCell('
$rkDrawSearchPulses = Get-CSharpBlock $rkDebugHudSource `
    'internal static void DrawSearchPulses('

$rkLegacyRingPath =
    '\bGenRadial\b|\bRadialPattern\b|\bNumCellsInRadius\b|' +
    '\bMaxRadialPatternRadius\b|\bAppendSearchRingCells\b|' +
    '\bRecordActualSearchRing\b|\bActualSearchRings\b'
$rkRingBlocks = $rkCollectRingCells + $rkCollectRingRow + $rkCollectRingSegment

if ($rkSharedSearchSource -match '\bGenRadial\b' -or
    $rkDebugHudSource -match $rkLegacyRingPath) {
    throw 'Shared search or DebugHUD regained a vanilla or duplicate ring enumerator.'
}

if ($rkCollectCell -match 'candidate\s+is\s+Projectile' -or
    [regex]::Matches(
        $rkCollectCell,
        'IsValidAutomaticAttackTarget\(').Count -ne 1 -or
    $rkValidateAttackTarget -notmatch 'target\s+is\s+IAttackTarget') {
    throw 'Ordinary ring candidates regained a redundant Projectile branch or lost the vanilla attack-target gate.'
}

if ($rkRingBlocks -match '\bMathf\.CeilToInt\b|\bDistanceToSquared\b|\.InBounds\(' -or
    $rkCollectRingCells -notmatch 'Mathf\.FloorToInt\(outerRadius\)' -or
    $rkCollectRingCells -notmatch 'for \(int absZ = 0; absZ <= maximumAbsZ; absZ\+\+\)' -or
    $rkCollectRingCells -notmatch 'while \(outerX >= 0' -or
    $rkCollectRingCells -notmatch 'while \(innerX >= 0' -or
    $rkCollectRingCells -notmatch 'if \(absZ > 0\)' -or
    [regex]::Matches(
        $rkCollectRingCells,
        'CollectAutomaticTargetsInRingRow\(').Count -ne 2) {
    throw 'Shared search no longer uses the exact center-outward scanline ring traversal.'
}

if ($rkRingBlocks -match '\byield\b|\bIEnumerable\s*<|\bnew\s+(List|HashSet)\s*<|\.(ToList|ToArray|Where|Select)\s*\(' -or
    $rkCollectRingRow -notmatch '\(uint\)z\s*>=\s*\(uint\)map\.Size\.z' -or
    [regex]::Matches(
        $rkCollectRingRow,
        'CollectAutomaticTargetsInRingRowSegment\(').Count -ne 3 -or
    [regex]::Matches(
        $rkCollectRingSegment,
        'CollectAutomaticTargetsInCell\(').Count -ne 1) {
    throw 'Ring traversal regained allocation, per-cell bounds work, or duplicate target visits.'
}

if ($rkCollectRingCells -notmatch 'Prefs\.DevMode[\s\S]*?RimKataDebugHUD\.SearchRangeEnabled[\s\S]*?TryBeginActualSearchCellRecording' -or
    [regex]::Matches(
        $rkCollectRingSegment,
        'if \(recordSearchCells\)').Count -ne 1 -or
    [regex]::Matches(
        $rkCollectRingSegment,
        'RecordActualSearchCell\(map, cell\)').Count -ne 1 -or
    $rkBeginHudRecording -notmatch 'actualSearchCellTick\s*!=\s*currentTick' -or
    $rkBeginHudRecording -notmatch 'IsHostileToPlayerFaction\(owner\)' -or
    $rkBeginHudRecording -notmatch 'map\.Disposed' -or
    $rkBeginHudRecording -notmatch 'owner\.Map\s*!=\s*map' -or
    $rkBeginHudRecording -notmatch 'currentTick\s*<\s*0' -or
    [regex]::Matches($rkBeginHudRecording, 'return false;').Count -ne 2 -or
    [regex]::Matches($rkBeginHudRecording, 'return true;').Count -ne 1 -or
    $rkBeginHudRecording -notmatch 'ActualSearchCells\.Clear\(\)' -or
    $rkBeginHudRecording -notmatch 'searchMeshTick\s*=\s*-1' -or
    $rkRecordHudCell -notmatch 'ActualSearchCells\.Add' -or
    $rkDrawSearchPulses -notmatch 'actualSearchCellTick\s*==\s*currentTick' -or
    $rkDrawSearchPulses -notmatch 'searchCell\.map\s*!=\s*map' -or
    $rkDrawSearchPulses -notmatch 'UniqueSearchCells\.Add\(searchCell\.cell\)') {
    throw 'DebugHUD no longer records only the cells visited by the live search loop.'
}

$rkHarness = @'
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace UnityEngine
{
    public static class Mathf
    {
        public static int Abs(int value) { return Math.Abs(value); }
        public static int FloorToInt(float value) { return (int)Math.Floor(value); }
        public static int Max(int left, int right) { return Math.Max(left, right); }
        public static int Min(int left, int right) { return Math.Min(left, right); }
    }
}

namespace Verse
{
    public struct IntVec3 : IEquatable<IntVec3>
    {
        public int x;
        public int y;
        public int z;

        public IntVec3(int x, int y, int z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public bool Equals(IntVec3 other)
        {
            return x == other.x && y == other.y && z == other.z;
        }

        public override bool Equals(object value)
        {
            return value is IntVec3 && Equals((IntVec3)value);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((x * 397) ^ y) * 397 ^ z;
            }
        }

        public override string ToString()
        {
            return "(" + x + "," + z + ")";
        }
    }

    public sealed class Map
    {
        public IntVec3 Size;

        public Map(int width, int height)
        {
            Size = new IntVec3(width, 0, height);
        }
    }

    public sealed class Pawn
    {
        public Map Map;

        public Pawn(Map map)
        {
            Map = map;
        }
    }

    public static class Prefs
    {
        public static bool DevMode;
    }
}

namespace KRWF.RimKata
{
    public sealed class RimKataPawnCombatState
    {
    }

    public static class RimKataDebugHUD
    {
        public static bool SearchRangeEnabled;
        public static int BeginCalls;
        public static readonly List<IntVec3> RecordedCells =
            new List<IntVec3>();

        public static bool TryBeginActualSearchCellRecording(Pawn owner, Map map)
        {
            BeginCalls++;
            return true;
        }

        public static void RecordActualSearchCell(Map map, IntVec3 cell)
        {
            RecordedCells.Add(cell);
        }
    }

    public static class RingTraversalHarness
    {
        private static readonly List<IntVec3> VisitedCells =
            new List<IntVec3>();
        private static int checks;

'@ + $rkCollectRingCells + "`r`n`r`n" +
    $rkCollectRingRow + "`r`n`r`n" +
    $rkCollectRingSegment + @'

        private static void CollectAutomaticTargetsInCell(
            Pawn pawn,
            RimKataPawnCombatState combatState,
            IntVec3 cell)
        {
            VisitedCells.Add(cell);
        }

        private static List<IntVec3> Enumerate(
            int width,
            int height,
            int centerX,
            int centerZ,
            float innerRadius,
            float outerRadius,
            bool devMode,
            bool searchRangeEnabled)
        {
            Map map = new Map(width, height);
            Pawn pawn = new Pawn(map);
            VisitedCells.Clear();
            Prefs.DevMode = devMode;
            RimKataDebugHUD.SearchRangeEnabled = searchRangeEnabled;
            RimKataDebugHUD.BeginCalls = 0;
            RimKataDebugHUD.RecordedCells.Clear();
            CollectAutomaticTargetsInRingCells(
                pawn,
                new RimKataPawnCombatState(),
                new IntVec3(centerX, 0, centerZ),
                innerRadius,
                outerRadius);
            return new List<IntVec3>(VisitedCells);
        }

        private static HashSet<IntVec3> BruteForce(
            int width,
            int height,
            int centerX,
            int centerZ,
            float innerRadius,
            float outerRadius)
        {
            float innerSquared = innerRadius < 0f
                ? -1f
                : innerRadius * innerRadius;
            float outerSquared = outerRadius * outerRadius;
            HashSet<IntVec3> expected = new HashSet<IntVec3>();
            for (int z = 0; z < height; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    long offsetX = x - centerX;
                    long offsetZ = z - centerZ;
                    float distanceSquared =
                        (float)(offsetX * offsetX + offsetZ * offsetZ);
                    if (distanceSquared > innerSquared
                        && distanceSquared <= outerSquared)
                    {
                        expected.Add(new IntVec3(x, 0, z));
                    }
                }
            }
            return expected;
        }

        private static void Check(bool condition, string message)
        {
            checks++;
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private static void CheckCase(
            int width,
            int height,
            int centerX,
            int centerZ,
            float innerRadius,
            float outerRadius)
        {
            List<IntVec3> actual = Enumerate(
                width,
                height,
                centerX,
                centerZ,
                innerRadius,
                outerRadius,
                true,
                true);
            HashSet<IntVec3> actualSet = new HashSet<IntVec3>(actual);
            HashSet<IntVec3> expected = BruteForce(
                width,
                height,
                centerX,
                centerZ,
                innerRadius,
                outerRadius);
            Check(actual.Count == actualSet.Count, "duplicate cells in ring");
            Check(actualSet.SetEquals(expected),
                "scanline cells differ from squared-distance reference");
            Check(RimKataDebugHUD.BeginCalls == 1,
                "enabled HUD gate did not open exactly once per ring");
            Check(RimKataDebugHUD.RecordedCells.Count == actual.Count,
                "HUD cell count differs from visited cell count");
            for (int i = 0; i < actual.Count; i++)
            {
                Check(RimKataDebugHUD.RecordedCells[i].Equals(actual[i]),
                    "HUD cell order differs from live visited-cell order");
            }
        }

        private static void CheckDisabledGate(bool devMode, bool searchRangeEnabled)
        {
            List<IntVec3> actual = Enumerate(
                41,
                43,
                20,
                21,
                4.7f,
                5.7f,
                devMode,
                searchRangeEnabled);
            Check(actual.Count > 0, "disabled HUD gate changed live traversal");
            Check(RimKataDebugHUD.BeginCalls == 0,
                "disabled HUD gate entered the recorder");
            Check(RimKataDebugHUD.RecordedCells.Count == 0,
                "disabled HUD gate recorded cells");
        }

        private static void CheckProgressiveUnion(
            int width,
            int height,
            int centerX,
            int centerZ,
            int maximumRing)
        {
            HashSet<IntVec3> union = new HashSet<IntVec3>();
            float innerRadius = -1f;
            for (int ring = 1; ring <= maximumRing; ring++)
            {
                float outerRadius = ring + 0.7f;
                List<IntVec3> cells = Enumerate(
                    width,
                    height,
                    centerX,
                    centerZ,
                    innerRadius,
                    outerRadius,
                    false,
                    false);
                for (int i = 0; i < cells.Count; i++)
                {
                    Check(union.Add(cells[i]),
                        "progressive rings overlap at " + cells[i]);
                }
                innerRadius = outerRadius;
            }

            HashSet<IntVec3> expected = BruteForce(
                width,
                height,
                centerX,
                centerZ,
                -1f,
                maximumRing + 0.7f);
            Check(union.SetEquals(expected),
                "progressive rings leave a gap in the final circle");
        }

        public static int Run()
        {
            checks = 0;
            CheckCase(61, 63, 30, 31, -1f, 1.7f);
            CheckCase(61, 63, 30, 31, 1.7f, 2.7f);
            CheckCase(31, 31, 15, 15, 4f, 5f);
            CheckCase(81, 83, 40, 41, 11.7f, 12.7f);
            CheckCase(81, 83, 40, 41, 24.7f, 25.35f);
            CheckCase(1, 1, 0, 0, -1f, 0.7f);
            CheckCase(1, 1, 0, 0, 0f, 0.7f);
            CheckCase(17, 19, 0, 0, -1f, 25.7f);
            CheckCase(17, 19, 16, 18, 12.7f, 25.7f);
            CheckCase(39, 41, 1, 39, 4.7f, 12.7f);
            CheckCase(205, 207, 102, 103, 79.7f, 100.7f);
            CheckProgressiveUnion(61, 63, 30, 31, 12);
            CheckProgressiveUnion(31, 33, 0, 16, 12);
            CheckDisabledGate(false, true);
            CheckDisabledGate(true, false);
            return checks;
        }
    }
}
'@

Add-Type -TypeDefinition $rkHarness -Language CSharp
$rkChecks = [KRWF.RimKata.RingTraversalHarness]::Run()
Write-Output (
    "PASS: {0} exact ring/HUD executable assertions + source-boundary assertions." -f `
        $rkChecks
)
