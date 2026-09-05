$ErrorActionPreference = 'Stop'
$rkRoot = Split-Path -Parent $PSScriptRoot
$rkAttackSource = Get-Content -LiteralPath (Join-Path $rkRoot 'Source/JobDriver_RimKataAttack.cs') -Raw -Encoding UTF8
$rkControllerSource = Get-Content -LiteralPath (Join-Path $rkRoot 'Source/RimKataDualWeaponController.cs') -Raw -Encoding UTF8
$rkRangeSource = Get-Content -LiteralPath (Join-Path $rkRoot 'Source/RimKataRangeUtility.cs') -Raw -Encoding UTF8
$rkSecondarySource = Get-Content -LiteralPath (Join-Path $rkRoot 'Source/RimKataSecondaryWeapon.cs') -Raw -Encoding UTF8
$rkSharedSearchSource = Get-Content -LiteralPath (Join-Path $rkRoot 'Source/RimKataSharedTargetSearch.cs') -Raw -Encoding UTF8
$rkTargetingSource = Get-Content -LiteralPath (Join-Path $rkRoot 'Source/RimKataTargeting.cs') -Raw -Encoding UTF8
$rkVisualSource = Get-Content -LiteralPath (Join-Path $rkRoot 'Source/RimKataVisualPatches.cs') -Raw -Encoding UTF8
$rkInterceptionSource = Get-Content -LiteralPath (Join-Path $rkRoot 'Source/RimKataInterceptionTrajectory.cs') -Raw -Encoding UTF8

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

$rkPairPatch = Get-CSharpBlock $rkAttackSource 'public static class Patch_Pawn_TryGetAttackVerb_RimKataPairRange'
$rkSecondaryLookup = Get-CSharpBlock $rkSecondarySource 'internal static ThingWithComps SecondaryWeaponWithVerifiedAccess('
$rkKnownPair = Get-CSharpBlock $rkSecondarySource 'internal static Verb BestRangedCombatVerb('
$rkRangedValidity = Get-CSharpBlock $rkSecondarySource 'private static bool RangedVerbCanAttack('
$rkMultiSelect = Get-CSharpBlock $rkSecondarySource 'public static class RimKataMultiSelectAttackGizmoUtility'
$rkFactsLookup = Get-CSharpBlock $rkMultiSelect 'GetSelectedAttackGizmoFacts()'
$rkFactsBuild = Get-CSharpBlock $rkMultiSelect 'BuildSelectedAttackGizmoFacts(List<object> selected)'
$rkResolveCandidateCellRadius = Get-CSharpBlock $rkRangeSource 'public static float ResolveCandidateCellRadius('
$rkCandidateCellRadiusPadding = Get-CSharpBlock $rkRangeSource 'internal static float ApplyCandidateCellRadiusPadding('
$rkResolveLogicalCandidateRange = Get-CSharpBlock $rkRangeSource 'internal static float ResolveLogicalCandidateRange('
$rkBegin = Get-CSharpBlock $rkSharedSearchSource 'internal static bool Begin('
$rkAdvance = Get-CSharpBlock $rkSharedSearchSource 'internal static bool Advance('
$rkMaximumLogicalRing = Get-CSharpBlock $rkSharedSearchSource 'private static int MaximumLogicalRingFromCellRadius('
$rkCycleCellRadius = Get-CSharpBlock $rkSharedSearchSource 'private static float CandidateCellRadiusForCycle('
$rkVisualCellRadius = Get-CSharpBlock $rkVisualSource 'private static float AutomaticSearchVisualCellRadius('
$rkSquadPatch = Get-CSharpBlock $rkVisualSource 'public static class Patch_PawnAttackGizmoUtility_RimKataSquadRange'
$rkSquadPostfix = Get-CSharpBlock $rkSquadPatch 'public static void Postfix(ref Gizmo __result)'

if ([regex]::Matches($rkPairPatch, 'CanBeginGunKataAttack').Count -ne 1 -or
    $rkPairPatch -notmatch 'CanUseSecondarySlot\([\s\S]*?__instance,[\s\S]*?primary,[\s\S]*?true\)' -or
    $rkPairPatch -notmatch 'SecondaryWeaponWithVerifiedAccess\(__instance\)' -or
    [regex]::Matches($rkPairPatch, 'CanReachImmediate').Count -ne 1 -or
    $rkPairPatch -notmatch 'BestRangedCombatVerb\([\s\S]*?primary,[\s\S]*?secondary,[\s\S]*?\(bool\?\)false') {
    throw 'Pair-range Postfix regained duplicate eligibility, loadout, or adjacency work.'
}

if ($rkSecondaryLookup -notmatch 'TryGetRegisteredSecondaryWeapon' -or
    [regex]::Matches($rkSecondaryLookup, 'SecondaryWeapon\(pawn\)').Count -ne 1) {
    throw 'Verified secondary lookup no longer uses the registered-user cache with registry fallback.'
}

if ([regex]::Matches($rkKnownPair, 'RangedVerbCanAttack').Count -ne 2 -or
    $rkKnownPair -match 'CanUseSecondarySlot|PrimaryWeapon\(|SecondaryWeapon\(|CanReachImmediate') {
    throw 'Known pair resolver regained loadout or direct adjacency resolution.'
}

if ([regex]::Matches($rkRangedValidity, 'CanReachImmediate').Count -ne 1 -or
    $rkRangedValidity -notmatch 'targetAdjacent\.HasValue' -or
    $rkRangedValidity -notmatch 'targetAdjacent\s*=\s*adjacent') {
    throw 'Pair verb validity no longer shares its lazy adjacency result.'
}

if ($rkFactsLookup -notmatch 'SelectedObjectsListForReading' -or
    $rkFactsLookup -match 'SelectedPawns' -or
    $rkFactsLookup -notmatch 'Time\.frameCount' -or
    $rkFactsLookup -notmatch 'currentEvent\.rawType' -or
    $rkFactsLookup -notmatch 'ReferenceEquals\(factsSelection, selected\)' -or
    $rkFactsLookup -notmatch 'ReferenceEquals\(factsFirst, first\)' -or
    $rkFactsLookup -notmatch 'ReferenceEquals\(factsLast, last\)' -or
    [regex]::Matches($rkFactsLookup, 'BuildSelectedAttackGizmoFacts').Count -ne 1) {
    throw 'Selected attack-gizmo facts cache lost its allocation-free event/selection boundary.'
}

if ([regex]::Matches($rkFactsBuild, 'for \(').Count -ne 1 -or
    $rkFactsBuild -match 'SelectedPawns|CanDrawAutomaticSearchRange' -or
    $rkFactsBuild -notmatch 'CanBeginGunKataAttack' -or
    $rkFactsBuild -notmatch 'CanUseSecondarySlot\([\s\S]*?pawn,[\s\S]*?true\)' -or
    $rkFactsBuild -notmatch 'SecondaryWeaponWithVerifiedAccess') {
    throw 'Selected attack-gizmo facts are no longer computed in one access-sharing pass.'
}

if ([regex]::Matches($rkSquadPostfix, 'GetSelectedAttackGizmoFacts').Count -ne 1 -or
    $rkSquadPostfix -match 'HasSelectedPawnWithAutomaticSearchRange|HasSelectedPawnWithActiveRimKataAttack|ShouldUseUnifiedAttackGizmo' -or
    $rkSquadPostfix -notmatch 'selectedFacts\.HasAutomaticSearchRange' -or
    $rkSquadPostfix -notmatch 'selectedFacts\.HasCombatCapableUser' -or
    $rkSquadPostfix -notmatch 'selectedFacts\.UseUnifiedAttackGizmo') {
    throw 'Squad-range Postfix no longer reuses one selected-group fact snapshot.'
}

if ($rkRangeSource -notmatch 'CandidateCellRadiusPadding\s*=\s*0\.7f' -or
    $rkRangeSource -match '\bResolveCandidateRange\s*\(' -or
    [regex]::Matches($rkResolveCandidateCellRadius, 'ResolveEffectiveRange').Count -ne 1 -or
    [regex]::Matches($rkResolveCandidateCellRadius, 'ResolveLogicalCandidateRange').Count -ne 1 -or
    $rkResolveCandidateCellRadius -notmatch 'verb\?\.IsMeleeAttack\s*==\s*false' -or
    [regex]::Matches($rkResolveCandidateCellRadius, 'ApplyCandidateCellRadiusPadding').Count -ne 1) {
    throw 'Logical candidate range and actual candidate cell radius are no longer separated.'
}

if ($rkCandidateCellRadiusPadding -notmatch 'effectiveWeaponRange\s*<=\s*0f' -or
    $rkCandidateCellRadiusPadding -notmatch 'logicalCandidateRange\s*<=\s*0f' -or
    $rkCandidateCellRadiusPadding -notmatch 'Mathf\.Min\([\s\S]*?effectiveWeaponRange,[\s\S]*?logicalCandidateRange\s*\+\s*CandidateCellRadiusPadding\)') {
    throw 'Candidate cell padding no longer preserves zero and effective-weapon caps.'
}

if ($rkVisualCellRadius -match '0\.7f|Padding|ResolveLogicalCandidateRange' -or
    [regex]::Matches($rkVisualCellRadius, 'ResolveEffectiveRange').Count -ne 1 -or
    [regex]::Matches($rkVisualCellRadius, 'ResolveCandidateCellRadius').Count -ne 1) {
    throw 'Automatic-search visual no longer shares the actual candidate cell radius.'
}

if ($rkSharedSearchSource -notmatch 'private const float CandidateCellRadiusPadding\s*=\s*[\r\n\s]*RimKataRangeUtility\.CandidateCellRadiusPadding;' -or
    $rkSharedSearchSource -notmatch 'CloseCombatRangedCandidateCellRadius\s*=\s*1\.7f' -or
    $rkBegin -notmatch 'MaximumLogicalRingFromCellRadius\([\s\S]*?maximumCellRadius\)' -or
    $rkAdvance -notmatch 'MaximumLogicalRingFromCellRadius\([\s\S]*?maximumCellRadius\)' -or
    $rkAdvance -notmatch 'Mathf\.Min\([\s\S]*?outerRing\s*\+\s*CandidateCellRadiusPadding,[\s\S]*?maximumCellRadius\)' -or
    $rkMaximumLogicalRing -notmatch 'candidateCellRadius\s*-\s*CandidateCellRadiusPadding' -or
    $rkCycleCellRadius -notmatch 'closeCombatContext[\s\S]*?UsesRangedCandidateLimit\(cycle\)[\s\S]*?CloseCombatRangedCandidateCellRadius' -or
    [regex]::Matches($rkSharedSearchSource, 'ResolveCandidateCellRadius\(').Count -ne 1 -or
    [regex]::Matches($rkTargetingSource, 'ResolveCandidateCellRadius\(').Count -ne 2 -or
    [regex]::Matches($rkControllerSource, 'ResolveCandidateCellRadius\(').Count -ne 3) {
    throw 'Shared search no longer keeps logical rings and candidate cell radii distinct.'
}

if ($rkSharedSearchSource -notmatch 'public float maximumCandidateCellRadius;' -or
    $rkSharedSearchSource -match 'public float effectiveMaximumRange;' -or
    $rkTargetingSource -notmatch 'MaximumAutomaticCandidateCellRadius\(' -or
    $rkTargetingSource -match 'MaximumAutomaticSearchRange\(' -or
    $rkControllerSource -match 'TargetWithinAutomaticSearchRange\(|LongestAutomaticRangeVerb\(') {
    throw 'An ambiguous automatic-search range name returned to a cell-radius path.'
}

if ($rkInterceptionSource -notmatch 'ResolveEffectiveRange\(' -or
    $rkInterceptionSource -match 'ResolveCandidateCellRadius\(|ApplyCandidateCellRadiusPadding\(') {
    throw 'Exact-range interception unexpectedly inherited automatic candidate padding.'
}

$rkHarness = @"
using System;
using UnityEngine;
using Verse;
using Verse.AI;

namespace UnityEngine
{
    public static class Mathf
    {
        public static float Min(float left, float right) { return Math.Min(left, right); }
        public static float Max(float left, float right) { return Math.Max(left, right); }
        public static int Max(int left, int right) { return Math.Max(left, right); }
        public static int CeilToInt(float value) { return (int)Math.Ceiling(value); }
    }
}

namespace Verse
{
    public class Thing { }

    public sealed class ThingWithComps : Thing
    {
        public Verb verb;
        public float range;
    }

    public sealed class Pawn
    {
        public int reachCalls;
        public bool adjacent;

        public bool CanReachImmediate(Thing target, PathEndMode mode)
        {
            reachCalls++;
            return adjacent;
        }
    }

    public sealed class Verb
    {
        public bool IsMeleeAttack;
        public bool apparelBlocked;
        public bool available = true;
        public bool closeAvailable = true;
        public bool canHit = true;

        public bool ApparelPreventsShooting() { return apparelBlocked; }
        public bool Available() { return available; }
        public bool CanHitTarget(Thing target) { return canHit; }
    }
}

namespace Verse.AI
{
    public enum PathEndMode { Touch }
}

namespace KRWF.RimKata
{
    public enum RimKataCandidateRangeMode
    {
        Short,
        Medium,
        Long,
        Unlimited,
        Custom
    }

    public sealed class RimKataSettings
    {
        public RimKataCandidateRangeMode candidateRangeMode;
        public float customCandidateRange;
    }

    public static class RimKataMod
    {
        public static RimKataSettings Settings;
    }

    public readonly struct RimKataRangeBands
    {
        public readonly float Touch;
        public readonly float Short;
        public readonly float Medium;
        public readonly float Long;

        public RimKataRangeBands(float touch, float shortRange, float medium, float longRange)
        {
            Touch = touch;
            Short = shortRange;
            Medium = medium;
            Long = longRange;
        }
    }

    public static class RimKataEligibility
    {
        public static bool IsRangedVerbAvailableInCloseCombat(Pawn pawn, Verb verb)
        {
            return verb.closeAvailable;
        }
    }

    public static class RimKataRangeUtility
    {
        internal const float CandidateCellRadiusPadding = 0.7f;
        public static bool RuntimeBandsAvailable = true;
        public static RimKataRangeBands CurrentBands =
            new RimKataRangeBands(3f, 12f, 25f, 40f);

        public static float ResolveEffectiveRange(Pawn pawn, ThingWithComps weapon, Verb verb)
        {
            return weapon == null ? 0f : weapon.range;
        }

        $rkResolveCandidateCellRadius

        $rkCandidateCellRadiusPadding

        $rkResolveLogicalCandidateRange
    }

    public static class RimKataSharedTargetSearch
    {
        private const float CandidateCellRadiusPadding =
            RimKataRangeUtility.CandidateCellRadiusPadding;

        $rkMaximumLogicalRing

        public static int LogicalRingFromCellRadius(float candidateCellRadius)
        {
            return MaximumLogicalRingFromCellRadius(candidateCellRadius);
        }
    }

    public static class RimKataWeaponSlotUtility
    {
        public static Verb CombatVerb(Pawn pawn, ThingWithComps weapon)
        {
            return weapon == null ? null : weapon.verb;
        }

        $rkKnownPair

        $rkRangedValidity
    }

    public static class RangeHotpathChecks
    {
        private static int checks;

        private static void Check(bool condition, string name)
        {
            if (!condition) throw new Exception("FAIL: " + name);
            checks++;
        }

        private static ThingWithComps Weapon(float range, Verb verb)
        {
            return new ThingWithComps { range = range, verb = verb };
        }

        public static int Run()
        {
            var target = new Thing();
            var primaryVerb = new Verb();
            var secondaryVerb = new Verb();
            var primary = Weapon(12f, primaryVerb);
            var secondary = Weapon(24f, secondaryVerb);

            var pawn = new Pawn();
            Verb chosen = RimKataWeaponSlotUtility.BestRangedCombatVerb(
                pawn, target, primary, secondary, null);
            Check(chosen == primaryVerb && pawn.reachCalls == 1,
                "unknown adjacency is shared and primary wins");

            pawn = new Pawn();
            primaryVerb.apparelBlocked = true;
            chosen = RimKataWeaponSlotUtility.BestRangedCombatVerb(
                pawn, target, primary, secondary, null);
            Check(chosen == secondaryVerb && pawn.reachCalls == 1,
                "secondary fallback shares adjacency");

            pawn = new Pawn();
            secondaryVerb.apparelBlocked = true;
            chosen = RimKataWeaponSlotUtility.BestRangedCombatVerb(
                pawn, target, primary, secondary, null);
            Check(chosen == null && pawn.reachCalls == 0,
                "invalid verbs do not resolve adjacency");

            primaryVerb.apparelBlocked = false;
            secondaryVerb.apparelBlocked = false;
            pawn = new Pawn();
            chosen = RimKataWeaponSlotUtility.BestRangedCombatVerb(
                pawn, null, primary, secondary, null);
            Check(chosen == secondaryVerb && pawn.reachCalls == 0,
                "targetless selection preserves longer ranged verb");

            pawn = new Pawn();
            chosen = RimKataWeaponSlotUtility.BestRangedCombatVerb(
                pawn, target, primary, secondary, false);
            Check(chosen == primaryVerb && pawn.reachCalls == 0,
                "known non-adjacency bypasses reach query");

            pawn = new Pawn { adjacent = false };
            chosen = RimKataWeaponSlotUtility.BestRangedCombatVerb(
                pawn, target, primary, secondary, true);
            Check(chosen == primaryVerb && pawn.reachCalls == 0,
                "known adjacency bypasses reach query");

            RimKataMod.Settings = new RimKataSettings();
            var ranged = Weapon(30f, new Verb());
            Check(Math.Abs(RimKataRangeUtility.ResolveLogicalCandidateRange(
                    30f) - 12f) < 0.0001f,
                "short logical candidate range remains twelve");
            Check(Math.Abs(RimKataRangeUtility.ResolveCandidateCellRadius(
                    pawn, ranged, ranged.verb) - 12.7f) < 0.0001f,
                "short candidate radius includes cell padding");

            ranged.range = 12.4f;
            Check(Math.Abs(RimKataRangeUtility.ResolveCandidateCellRadius(
                    pawn, ranged, ranged.verb) - 12.4f) < 0.0001f,
                "candidate padding cannot exceed weapon range");

            ranged.range = 12f;
            Check(Math.Abs(RimKataRangeUtility.ResolveCandidateCellRadius(
                    pawn, ranged, ranged.verb) - 12f) < 0.0001f,
                "exact weapon boundary remains exact");

            ranged.range = 30f;
            ranged.verb.IsMeleeAttack = true;
            Check(Math.Abs(RimKataRangeUtility.ResolveCandidateCellRadius(
                    pawn, ranged, ranged.verb) - 12f) < 0.0001f,
                "ranged cell padding does not enter melee candidate calls");

            ranged.verb.IsMeleeAttack = false;
            RimKataMod.Settings.candidateRangeMode =
                RimKataCandidateRangeMode.Unlimited;
            Check(Math.Abs(RimKataRangeUtility.ResolveCandidateCellRadius(
                    pawn, ranged, ranged.verb) - 30f) < 0.0001f,
                "unlimited candidate radius remains weapon limited");

            RimKataMod.Settings.candidateRangeMode =
                RimKataCandidateRangeMode.Custom;
            RimKataMod.Settings.customCandidateRange = 15f;
            Check(Math.Abs(RimKataRangeUtility.ResolveCandidateCellRadius(
                    pawn, ranged, ranged.verb) - 15.7f) < 0.0001f,
                "custom candidate radius includes cell padding");

            Check(RimKataRangeUtility.ApplyCandidateCellRadiusPadding(
                    24f, 0f) == 0f,
                "zero candidate range remains disabled");
            Check(RimKataRangeUtility.ApplyCandidateCellRadiusPadding(
                    0f, 12f) == 0f,
                "zero effective range remains disabled");

            float paddedShort =
                RimKataRangeUtility.ApplyCandidateCellRadiusPadding(
                    30f,
                    12f);
            float diagonalCellSquared = 12f * 12f + 4f * 4f;
            Check(diagonalCellSquared > 12f * 12f
                    && diagonalCellSquared <= paddedShort * paddedShort,
                "cell offset 12,4 enters the padded short search radius");

            float outsideCellSquared = 13f * 13f;
            Check(outsideCellSquared > paddedShort * paddedShort,
                "cell offset 13,0 remains outside the padded short search radius");

            Check(RimKataSharedTargetSearch.LogicalRingFromCellRadius(12.7f) == 12,
                "short candidate cell radius maps back to logical ring twelve");
            Check(RimKataSharedTargetSearch.LogicalRingFromCellRadius(1.7f) == 1,
                "close candidate cell radius maps to one logical ring");
            Check(RimKataSharedTargetSearch.LogicalRingFromCellRadius(12.4f) == 12,
                "weapon-clipped cell radius keeps logical ring twelve");

            return checks;
        }
    }
}
"@

Add-Type -TypeDefinition $rkHarness -Language CSharp
$rkPassed = [KRWF.RimKata.RangeHotpathChecks]::Run()
"PASS: $rkPassed executable pair/candidate-range assertions + selected-gizmo cache and shared search/visual source-boundary assertions; in-game profiler, target acquisition, and ring-shape checks remain required."
