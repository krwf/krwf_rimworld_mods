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
$rkPairPostfix = Get-CSharpBlock $rkPairPatch 'public static void Postfix('
$rkPairRenderGate = Get-CSharpBlock $rkPairPostfix 'if (__0 == null'
$rkSecondaryLookup = Get-CSharpBlock $rkSecondarySource 'internal static ThingWithComps SecondaryWeaponWithVerifiedAccess('
$rkKnownPair = Get-CSharpBlock $rkSecondarySource 'internal static Verb BestRangedCombatVerb('
$rkRangedValidity = Get-CSharpBlock $rkSecondarySource 'private static bool RangedVerbCanAttack('
$rkSecondaryGizmoPatch = Get-CSharpBlock $rkSecondarySource 'public static class Patch_PawnEquipmentTracker_RimKataSecondaryGizmo'
$rkSecondaryGizmoIterator = Get-CSharpBlock $rkSecondaryGizmoPatch 'private static IEnumerable<Gizmo> MarkSecondaryWeaponGizmos('
$rkSecondaryGizmoEligibilityGate = Get-CSharpBlock $rkSecondaryGizmoIterator 'if (!RimKataEligibility.CanBeginGunKataAttack(pawn))'
$rkSecondaryGizmoBranch = Get-CSharpBlock $rkSecondaryGizmoIterator 'else if (weapon == secondary)'
$rkUndraftedSecondaryGate = Get-CSharpBlock $rkSecondaryGizmoBranch 'if (!pawn.Drafted && command.Disabled)'
$rkSecondaryRegistry = Get-CSharpBlock $rkSecondarySource 'public sealed class RimKataSecondaryWeaponRegistry'
$rkGetRegistered = Get-CSharpBlock $rkSecondaryRegistry 'public ThingWithComps GetRegistered(Pawn pawn)'
$rkSetRegistered = Get-CSharpBlock $rkSecondaryRegistry 'public void Set(Pawn pawn, ThingWithComps weapon)'
$rkRemoveRegistered = Get-CSharpBlock $rkSecondaryRegistry 'private void RemoveAt(int index)'
$rkTryCachedRegistered = Get-CSharpBlock $rkSecondaryRegistry 'private bool TryGetCachedRegisteredWeapon('
$rkCacheRegisteredWithTickMarker = @'
private void CacheRegisteredWeapon(
            Pawn pawn,
            ThingWithComps weapon,
            int currentTick)
'@
$rkCacheRegisteredWithTick = Get-CSharpBlock $rkSecondaryRegistry $rkCacheRegisteredWithTickMarker.Trim()
$rkInvalidateCachedRegistered = Get-CSharpBlock $rkSecondaryRegistry 'private void InvalidateCachedRegisteredWeapon(Pawn pawn)'
$rkPrepareRegisteredCache = Get-CSharpBlock $rkSecondaryRegistry 'private void PrepareRegisteredWeaponLookupCache(int currentTick)'
$rkResetRegisteredCache = Get-CSharpBlock $rkSecondaryRegistry 'private void ResetRegisteredWeaponLookupCache()'
$rkMeleeGizmoPatch = Get-CSharpBlock $rkSecondarySource 'public static class Patch_PawnAttackGizmoUtility_RimKataMeleeAttackGizmo'
$rkMeleeGizmoPrefix = Get-CSharpBlock $rkMeleeGizmoPatch 'public static bool Prefix('
$rkMeleeGizmoPostfix = Get-CSharpBlock $rkMeleeGizmoPatch 'public static void Postfix('
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

$rkPairRenderGateIndex = Get-Index $rkPairPostfix 'if (__0 == null'
$rkPairEligibilityIndex = Get-Index $rkPairPostfix 'RimKataEligibility.CanBeginGunKataAttack'
if ([regex]::Matches($rkPairPostfix, '__0\s*==\s*null').Count -ne 1 -or
    [regex]::Matches($rkPairPostfix, 'RimKataGunReadyDrawUtility\.IsDrawingEquipmentFor').Count -ne 1 -or
    $rkPairRenderGate -notmatch '__0\s*==\s*null\s*&&\s*RimKataGunReadyDrawUtility\.IsDrawingEquipmentFor\(__instance\)' -or
    $rkPairRenderGate -match '\|\|' -or
    [regex]::Matches($rkPairRenderGate, 'return;').Count -ne 1 -or
    $rkPairRenderGateIndex -ge $rkPairEligibilityIndex) {
    throw 'Pair-range render dormancy no longer skips only targetless equipment-render calls.'
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

if ($rkSecondarySource -match 'CreateVerbTargetCommand' -or
    [regex]::Matches($rkSecondaryGizmoIterator, 'CanBeginGunKataAttack').Count -ne 1 -or
    [regex]::Matches($rkSecondaryGizmoIterator, 'SecondaryWeaponWithVerifiedAccess\(pawn\)').Count -ne 1 -or
    $rkSecondaryGizmoIterator -notmatch 'ThingWithComps secondary\s*=\s*RimKataWeaponSlotUtility\.SecondaryWeaponWithVerifiedAccess\(pawn\)' -or
    $rkSecondaryGizmoIterator -match 'IsSecondaryWeapon\(|IsRegisteredUser|SecondaryWeapon\(pawn\)' -or
    [regex]::Matches($rkSecondaryGizmoIterator, '"IsNotDrafted"\s*\.Translate').Count -ne 1 -or
    [regex]::Matches($rkSecondaryGizmoIterator, 'command\.Disabled\s*=\s*false').Count -ne 1 -or
    [regex]::Matches($rkSecondaryGizmoIterator, 'command\.disabledReason\s*=\s*null').Count -ne 1) {
    throw 'Undrafted secondary-gizmo unlock regained a per-Verb patch or duplicate eligibility/loadout work.'
}

$rkCachedReadIndex = Get-Index $rkGetRegistered 'TryGetCachedRegisteredWeapon('
$rkRawReadIndex = Get-Index $rkGetRegistered 'pawns.IndexOf(pawn)'
$rkCacheWriteIndex = Get-Index $rkGetRegistered 'CacheRegisteredWeapon(pawn, registeredWeapon, currentTick)'
if ($rkSecondaryRegistry -notmatch 'Dictionary<Pawn,\s*ThingWithComps>\s*sameTickRegisteredWeapons' -or
    $rkCachedReadIndex -ge $rkRawReadIndex -or
    $rkRawReadIndex -ge $rkCacheWriteIndex -or
    $rkTryCachedRegistered -notmatch 'PrepareRegisteredWeaponLookupCache\(currentTick\)' -or
    $rkSetRegistered -notmatch 'CacheRegisteredWeapon\(pawn, weapon\)' -or
    $rkRemoveRegistered -notmatch 'InvalidateCachedRegisteredWeapon\(pawn\)' -or
    $rkInvalidateCachedRegistered -notmatch 'sameTickRegisteredWeapons\.Remove\(pawn\)' -or
    $rkPrepareRegisteredCache -notmatch 'sameTickRegisteredWeapons\.Clear\(\)' -or
    $rkPrepareRegisteredCache -notmatch 'registeredWeaponLookupTick\s*=\s*currentTick' -or
    $rkResetRegisteredCache -notmatch 'sameTickRegisteredWeapons\.Clear\(\)') {
    throw 'Secondary registry no longer reuses one lookup per Pawn/tick or refreshes mutations.'
}

$rkUnifiedIndex = Get-Index $rkSecondaryGizmoIterator 'ShouldUseUnifiedAttackGizmo()'
$rkNullSecondaryIndex = Get-Index $rkSecondaryGizmoIterator 'if (secondary == null)'
$rkSecondaryBranchIndex = Get-Index $rkSecondaryGizmoIterator 'else if (weapon == secondary)'
$rkEligibilityIndex = Get-Index $rkSecondaryGizmoIterator 'if (!RimKataEligibility.CanBeginGunKataAttack(pawn))'
$rkPrimaryLookupIndex = Get-Index $rkSecondaryGizmoIterator 'ThingWithComps primary ='
$rkUnifiedBlock = Get-CSharpBlock $rkSecondaryGizmoIterator 'if (RimKataMultiSelectAttackGizmoUtility'
$rkNullSecondaryBlock = Get-CSharpBlock $rkSecondaryGizmoIterator 'if (secondary == null)'
$rkGateIndex = Get-Index $rkSecondaryGizmoBranch 'if (!pawn.Drafted && command.Disabled)'
$rkReasonGuardIndex = Get-Index $rkSecondaryGizmoBranch 'if (notDraftedReason == null)'
$rkTranslateIndex = Get-Index $rkSecondaryGizmoBranch '"IsNotDrafted"'
$rkReasonMatchIndex = Get-Index $rkSecondaryGizmoBranch 'if (command.disabledReason == notDraftedReason)'
$rkEnableIndex = Get-Index $rkSecondaryGizmoBranch 'command.Disabled = false'
$rkReasonClearIndex = Get-Index $rkSecondaryGizmoBranch 'command.disabledReason = null'
$rkLabelIndex = Get-Index $rkSecondaryGizmoBranch 'command.defaultLabel = SecondaryGizmoLabel'
$rkAddIndex = Get-Index $rkSecondaryGizmoBranch 'secondaryCommands.Add(command)'
if ($rkUnifiedIndex -ge $rkNullSecondaryIndex -or
    $rkNullSecondaryIndex -ge $rkSecondaryBranchIndex -or
    $rkEligibilityIndex -ge $rkPrimaryLookupIndex -or
    $rkSecondaryGizmoEligibilityGate -notmatch 'yield return gizmo' -or
    $rkSecondaryGizmoEligibilityGate -notmatch 'yield break' -or
    $rkSecondaryGizmoIterator -notmatch 'string notDraftedReason\s*=\s*null' -or
    $rkUnifiedBlock -notmatch 'commandWeapon\s*==\s*primary' -or
    $rkUnifiedBlock -notmatch 'commandWeapon\s*==\s*secondary' -or
    $rkUnifiedBlock -notmatch 'continue' -or
    $rkUnifiedBlock -notmatch 'yield break' -or
    $rkNullSecondaryBlock -notmatch 'yield return gizmo' -or
    $rkNullSecondaryBlock -notmatch 'yield break' -or
    $rkGateIndex -ge $rkReasonGuardIndex -or
    $rkReasonGuardIndex -ge $rkTranslateIndex -or
    $rkTranslateIndex -ge $rkReasonMatchIndex -or
    $rkReasonMatchIndex -ge $rkEnableIndex -or
    $rkEnableIndex -ge $rkReasonClearIndex -or
    $rkReasonClearIndex -ge $rkLabelIndex -or
    $rkLabelIndex -ge $rkAddIndex) {
    throw 'Undrafted secondary-gizmo unlock no longer preserves its exact reason, ordering, or grouping boundary.'
}

if ([regex]::Matches($rkMeleeGizmoPrefix, 'CanBeginGunKataAttack').Count -ne 1 -or
    [regex]::Matches($rkMeleeGizmoPostfix, 'CanBeginGunKataAttack').Count -ne 1 -or
    $rkMeleeGizmoPrefix -notmatch 'out bool\? __state' -or
    $rkMeleeGizmoPrefix -notmatch '__state\s*=\s*null' -or
    $rkMeleeGizmoPostfix -notmatch 'bool\? __state' -or
    $rkMeleeGizmoPostfix -notmatch '__state[\s\S]*?\?\?[\s\S]*?CanBeginGunKataAttack') {
    throw 'Melee-gizmo patch no longer reuses eligibility with a skipped-Prefix fallback.'
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
using System.Collections.Generic;
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
    public sealed class TickManager
    {
        public int TicksGame;
    }

    public static class Find
    {
        public static TickManager TickManager;
    }

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
        public bool Drafted;
        public string LabelShort = "Pawn";

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

    public sealed class Command_VerbTarget
    {
        public bool Disabled;
        public string disabledReason;
    }

    public static class TranslationExtensions
    {
        public static int calls;
        public static string result = "Pawn is not drafted";

        public static string Translate(this string key, params object[] args)
        {
            calls++;
            return result;
        }
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
        public bool targetRushEnabled = true;
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
        public static bool canBegin;
        public static int beginCalls;

        public static bool CanBeginGunKataAttack(Pawn pawn)
        {
            beginCalls++;
            return canBegin;
        }

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

    public static class RimKataGunReadyDrawUtility
    {
        public static bool Scoped;
        public static Pawn ScopePawn;
        public static int Calls;

        public static bool IsDrawingEquipmentFor(Pawn pawn)
        {
            Calls++;
            return pawn != null
                && Scoped
                && object.ReferenceEquals(ScopePawn, pawn);
        }
    }

    public static class PairRangeRenderGateHarness
    {
        public static int DeepCalls;

        public static void Run(Pawn __instance, Thing __0)
        {
            $rkPairRenderGate

            DeepCalls++;
        }
    }

    public static class UndraftedSecondaryGizmoHarness
    {
        public static void Apply(
            Pawn pawn,
            ThingWithComps weapon,
            ThingWithComps secondary,
            Command_VerbTarget command,
            ref string notDraftedReason)
        {
            if (weapon == secondary)
            {
                $rkUndraftedSecondaryGate
            }
        }
    }

    public sealed class SecondaryRegistryLookupHarness
    {
        private readonly List<Pawn> pawns = new List<Pawn>();
        private readonly List<ThingWithComps> weapons =
            new List<ThingWithComps>();
        private readonly Dictionary<Pawn, ThingWithComps>
            sameTickRegisteredWeapons =
                new Dictionary<Pawn, ThingWithComps>();
        private int registeredWeaponLookupTick = int.MinValue;

        $rkGetRegistered

        $rkTryCachedRegistered

        $rkCacheRegisteredWithTick

        $rkInvalidateCachedRegistered

        $rkPrepareRegisteredCache

        $rkResetRegisteredCache

        public void AddWithoutCache(Pawn pawn, ThingWithComps weapon)
        {
            pawns.Add(pawn);
            weapons.Add(weapon);
        }

        public void ReplaceFirstWithoutCache(
            Pawn pawn,
            ThingWithComps weapon)
        {
            weapons[pawns.IndexOf(pawn)] = weapon;
        }

        public void RefreshSameTick(Pawn pawn, ThingWithComps weapon)
        {
            CacheRegisteredWeapon(
                pawn,
                weapon,
                Find.TickManager == null
                    ? int.MinValue
                    : Find.TickManager.TicksGame);
        }

        public void RemoveFirstAndInvalidate(Pawn pawn)
        {
            int index = pawns.IndexOf(pawn);
            pawns.RemoveAt(index);
            weapons.RemoveAt(index);
            InvalidateCachedRegisteredWeapon(pawn);
        }

        public void ResetForLoad()
        {
            ResetRegisteredWeaponLookupCache();
        }
    }

    $rkMeleeGizmoPatch

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

        private static bool EvaluateMeleeGizmo(
            Pawn pawn,
            bool vanillaResult,
            out bool originalRan)
        {
            bool result = false;
            bool? state;
            originalRan = Patch_PawnAttackGizmoUtility_RimKataMeleeAttackGizmo.Prefix(
                pawn,
                ref result,
                out state);
            if (originalRan)
            {
                result = vanillaResult;
            }

            Patch_PawnAttackGizmoUtility_RimKataMeleeAttackGizmo.Postfix(
                pawn,
                ref result,
                state);
            return result;
        }

        public static int Run()
        {
            var renderPawn = new Pawn();
            var otherPawn = new Pawn();
            RimKataGunReadyDrawUtility.Scoped = true;
            RimKataGunReadyDrawUtility.ScopePawn = renderPawn;
            RimKataGunReadyDrawUtility.Calls = 0;
            PairRangeRenderGateHarness.DeepCalls = 0;
            PairRangeRenderGateHarness.Run(renderPawn, null);
            Check(PairRangeRenderGateHarness.DeepCalls == 0
                    && RimKataGunReadyDrawUtility.Calls == 1,
                "targetless equipment-render lookup stops before pair-range work");

            PairRangeRenderGateHarness.Run(otherPawn, null);
            Check(PairRangeRenderGateHarness.DeepCalls == 1
                    && RimKataGunReadyDrawUtility.Calls == 2,
                "targetless lookup for another Pawn remains active");

            RimKataGunReadyDrawUtility.Calls = 0;
            PairRangeRenderGateHarness.DeepCalls = 0;
            PairRangeRenderGateHarness.Run(renderPawn, new Thing());
            Check(PairRangeRenderGateHarness.DeepCalls == 1
                    && RimKataGunReadyDrawUtility.Calls == 0,
                "actual attack target bypasses the render-scope probe");

            RimKataGunReadyDrawUtility.Scoped = false;
            RimKataGunReadyDrawUtility.Calls = 0;
            PairRangeRenderGateHarness.DeepCalls = 0;
            PairRangeRenderGateHarness.Run(renderPawn, null);
            Check(PairRangeRenderGateHarness.DeepCalls == 1
                    && RimKataGunReadyDrawUtility.Calls == 1,
                "non-render targetless lookup remains active");

            Find.TickManager = new TickManager { TicksGame = 100 };
            var registry = new SecondaryRegistryLookupHarness();
            var registryPawn = new Pawn();
            var firstRegistered = new ThingWithComps();
            var secondRegistered = new ThingWithComps();
            registry.AddWithoutCache(registryPawn, firstRegistered);
            Check(registry.GetRegistered(registryPawn) == firstRegistered,
                "first registry read resolves the stored weapon");

            registry.ReplaceFirstWithoutCache(
                registryPawn,
                secondRegistered);
            Check(registry.GetRegistered(registryPawn) == firstRegistered,
                "same-tick registry read reuses the first result");

            Find.TickManager.TicksGame++;
            Check(registry.GetRegistered(registryPawn) == secondRegistered,
                "next tick refreshes the registry result");

            registry.RefreshSameTick(registryPawn, firstRegistered);
            Check(registry.GetRegistered(registryPawn) == firstRegistered,
                "same-tick Set refresh exposes the new weapon immediately");

            registry.RefreshSameTick(registryPawn, null);
            Check(registry.GetRegistered(registryPawn) == null,
                "same-tick Clear refresh exposes removal immediately");

            var newlyRegisteredPawn = new Pawn();
            Check(registry.GetRegistered(newlyRegisteredPawn) == null,
                "unregistered result is cached explicitly");
            registry.AddWithoutCache(newlyRegisteredPawn, secondRegistered);
            Check(registry.GetRegistered(newlyRegisteredPawn) == null,
                "same-tick negative lookup remains cached");
            registry.RefreshSameTick(newlyRegisteredPawn, secondRegistered);
            Check(registry.GetRegistered(newlyRegisteredPawn) == secondRegistered,
                "same-tick registration refresh replaces a negative result");

            registry.AddWithoutCache(registryPawn, secondRegistered);
            registry.RemoveFirstAndInvalidate(registryPawn);
            Check(registry.GetRegistered(registryPawn) == secondRegistered,
                "removal invalidation preserves a remaining duplicate entry");

            registry.ReplaceFirstWithoutCache(
                newlyRegisteredPawn,
                firstRegistered);
            registry.ResetForLoad();
            Check(registry.GetRegistered(newlyRegisteredPawn) == firstRegistered,
                "load reset invalidates same-tick lookup state");

            RimKataMod.Settings = new RimKataSettings { targetRushEnabled = true };
            RimKataEligibility.canBegin = true;
            RimKataEligibility.beginCalls = 0;
            bool originalRan;
            bool meleeGizmo = EvaluateMeleeGizmo(
                new Pawn { Drafted = true },
                false,
                out originalRan);
            Check(meleeGizmo && !originalRan && RimKataEligibility.beginCalls == 1,
                "drafted eligible rush-on gizmo reuses one eligibility result");

            RimKataMod.Settings.targetRushEnabled = false;
            RimKataEligibility.beginCalls = 0;
            meleeGizmo = EvaluateMeleeGizmo(
                new Pawn { Drafted = true },
                false,
                out originalRan);
            Check(meleeGizmo && originalRan && RimKataEligibility.beginCalls == 1,
                "rush-off preserves vanilla execution before RimKata override");

            RimKataMod.Settings.targetRushEnabled = true;
            RimKataEligibility.canBegin = false;
            RimKataEligibility.beginCalls = 0;
            meleeGizmo = EvaluateMeleeGizmo(
                new Pawn { Drafted = true },
                false,
                out originalRan);
            Check(!meleeGizmo && originalRan && RimKataEligibility.beginCalls == 1,
                "drafted ineligible pawn preserves the vanilla result");

            var gizmoPawn = new Pawn { Drafted = false };
            var gizmoPrimary = new ThingWithComps();
            var gizmoSecondary = new ThingWithComps();
            var secondaryCommand = new Command_VerbTarget
            {
                Disabled = true,
                disabledReason = TranslationExtensions.result
            };
            string notDraftedReason = null;
            TranslationExtensions.calls = 0;
            UndraftedSecondaryGizmoHarness.Apply(
                gizmoPawn,
                gizmoSecondary,
                gizmoSecondary,
                secondaryCommand,
                ref notDraftedReason);
            Check(!secondaryCommand.Disabled
                    && secondaryCommand.disabledReason == null
                    && TranslationExtensions.calls == 1,
                "exact undrafted secondary reason is enabled once");

            var secondSecondaryCommand = new Command_VerbTarget
            {
                Disabled = true,
                disabledReason = TranslationExtensions.result
            };
            UndraftedSecondaryGizmoHarness.Apply(
                gizmoPawn,
                gizmoSecondary,
                gizmoSecondary,
                secondSecondaryCommand,
                ref notDraftedReason);
            Check(!secondSecondaryCommand.Disabled
                    && TranslationExtensions.calls == 1,
                "multiple secondary verbs share one translated reason");

            var otherReasonCommand = new Command_VerbTarget
            {
                Disabled = true,
                disabledReason = "Other reason"
            };
            notDraftedReason = null;
            TranslationExtensions.calls = 0;
            UndraftedSecondaryGizmoHarness.Apply(
                gizmoPawn,
                gizmoSecondary,
                gizmoSecondary,
                otherReasonCommand,
                ref notDraftedReason);
            Check(otherReasonCommand.Disabled
                    && otherReasonCommand.disabledReason == "Other reason"
                    && TranslationExtensions.calls == 1,
                "another disabled reason remains blocked");

            var draftedCommand = new Command_VerbTarget
            {
                Disabled = true,
                disabledReason = TranslationExtensions.result
            };
            notDraftedReason = null;
            TranslationExtensions.calls = 0;
            UndraftedSecondaryGizmoHarness.Apply(
                new Pawn { Drafted = true },
                gizmoSecondary,
                gizmoSecondary,
                draftedCommand,
                ref notDraftedReason);
            Check(draftedCommand.Disabled
                    && TranslationExtensions.calls == 0,
                "drafted secondary command remains unchanged");

            var primaryCommand = new Command_VerbTarget
            {
                Disabled = true,
                disabledReason = TranslationExtensions.result
            };
            notDraftedReason = null;
            TranslationExtensions.calls = 0;
            UndraftedSecondaryGizmoHarness.Apply(
                gizmoPawn,
                gizmoPrimary,
                gizmoSecondary,
                primaryCommand,
                ref notDraftedReason);
            Check(primaryCommand.Disabled
                    && TranslationExtensions.calls == 0,
                "primary command remains blocked without translation");

            var enabledCommand = new Command_VerbTarget
            {
                Disabled = false,
                disabledReason = null
            };
            notDraftedReason = null;
            TranslationExtensions.calls = 0;
            UndraftedSecondaryGizmoHarness.Apply(
                gizmoPawn,
                gizmoSecondary,
                gizmoSecondary,
                enabledCommand,
                ref notDraftedReason);
            Check(!enabledCommand.Disabled
                    && enabledCommand.disabledReason == null
                    && TranslationExtensions.calls == 0,
                "already enabled secondary command avoids translation");

            RimKataEligibility.canBegin = true;
            RimKataEligibility.beginCalls = 0;
            meleeGizmo = EvaluateMeleeGizmo(
                new Pawn { Drafted = false },
                true,
                out originalRan);
            Check(!meleeGizmo && !originalRan && RimKataEligibility.beginCalls == 1,
                "rush-on undrafted eligible pawn preserves the original skip");

            RimKataMod.Settings.targetRushEnabled = false;
            RimKataEligibility.beginCalls = 0;
            meleeGizmo = EvaluateMeleeGizmo(
                new Pawn { Drafted = false },
                true,
                out originalRan);
            Check(meleeGizmo && originalRan && RimKataEligibility.beginCalls == 0,
                "rush-off undrafted pawn preserves the vanilla result without eligibility");

            RimKataEligibility.beginCalls = 0;
            meleeGizmo = false;
            Patch_PawnAttackGizmoUtility_RimKataMeleeAttackGizmo.Postfix(
                new Pawn { Drafted = true },
                ref meleeGizmo,
                null);
            Check(meleeGizmo && RimKataEligibility.beginCalls == 1,
                "skipped RimKata Prefix retains the Postfix eligibility fallback");

            RimKataEligibility.canBegin = false;
            RimKataEligibility.beginCalls = 0;
            meleeGizmo = true;
            Patch_PawnAttackGizmoUtility_RimKataMeleeAttackGizmo.Postfix(
                new Pawn { Drafted = true },
                ref meleeGizmo,
                null);
            Check(meleeGizmo && RimKataEligibility.beginCalls == 1,
                "skipped Prefix fallback preserves an ineligible external result");

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
