$ErrorActionPreference = 'Stop'
$rkRoot = Split-Path -Parent $PSScriptRoot
$rkCombatSource = Get-Content -LiteralPath (Join-Path $rkRoot 'Source/RimKataCombatState.cs') -Raw -Encoding UTF8
$rkControllerSource = Get-Content -LiteralPath (Join-Path $rkRoot 'Source/RimKataDualWeaponController.cs') -Raw -Encoding UTF8
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
$rkGunReadyUtility = Get-CSharpBlock $rkVisualSource 'internal static class RimKataGunReadyDrawUtility'
$rkCarryUtility = Get-CSharpBlock $rkVisualSource 'internal static class RimKataCarryDrawUtility'
$rkCarryPush = Get-CSharpBlock $rkCarryUtility 'public static int Push('
$rkCarryEnterScope = Get-CSharpBlock $rkCarryUtility 'private static int EnterScope('
$rkCarryEnsureNestedCapacity = Get-CSharpBlock $rkCarryUtility 'private static void EnsureNestedContextCapacity('
$rkCarryPop = Get-CSharpBlock $rkCarryUtility 'public static void Pop('
$rkCarryPushCatch = Get-CSharpBlock $rkCarryPush 'catch'
$rkTryDrawPair = Get-CSharpBlock $rkVisualSource 'public static bool TryDrawPair('
$rkDrawWeapon = Get-CSharpBlock $rkVisualSource 'private static void DrawWeapon('
$rkSymmetricIdleSecondaryLoc = Get-CSharpBlock $rkVisualSource 'private static Vector3 SymmetricIdleSecondaryLoc('
$rkResolveEquipmentPivot = Get-CSharpBlock $rkVisualSource 'internal static Vector3 ResolveEquipmentPivot('
$rkDeflectionPatch = Get-CSharpBlock $rkVisualSource 'public static class Patch_PawnRenderUtility_RimKataDeflection'
$rkGunReadyPatch = Get-CSharpBlock $rkVisualSource 'public static class Patch_PawnRenderUtility_RimKataGunReadyContext'
$rkCarryGunReadyPatch = Get-CSharpBlock $rkVisualSource 'public static class Patch_PawnRenderUtility_RimKataCarryGunReady'
$rkDrawGunReadyPatch = Get-CSharpBlock $rkVisualSource 'public static class Patch_PawnRenderUtility_RimKataDrawGunReady'
$rkCarryPatch = Get-CSharpBlock $rkVisualSource 'public static class Patch_PawnRenderUtility_RimKataCarryDrawContext'
$rkPush = Get-CSharpBlock $rkGunReadyUtility 'public static int Push('
$rkEnterScope = Get-CSharpBlock $rkGunReadyUtility 'private static int EnterScope('
$rkEnsureNestedCapacity = Get-CSharpBlock $rkGunReadyUtility 'private static void EnsureNestedContextCapacity('
$rkPop = Get-CSharpBlock $rkGunReadyUtility 'public static void Pop('
$rkPushCatch = Get-CSharpBlock $rkPush 'catch'
$rkCandidate = Get-CSharpBlock $rkVisualSource 'private static bool MayNeedGunReadyTarget('
$rkCombatIndicators = Get-CSharpBlock $rkVisualSource 'public static void DrawCombatIndicators('
$rkDodgeOffset = Get-CSharpBlock $rkVisualSource 'public static class Patch_PawnRenderer_RimKataDodgeOffset'
$rkDodgeOffsetPrefix = Get-CSharpBlock $rkDodgeOffset 'public static void Prefix('
$rkIndicatorCandidate = Get-CSharpBlock $rkControllerSource 'internal static bool MayNeedCombatIndicatorFrame('
$rkIndicatorFrame = Get-CSharpBlock $rkControllerSource 'GetCombatIndicatorFrameData('
$rkIndicatorWeaponFrame = Get-CSharpBlock $rkControllerSource 'private static RimKataCombatIndicatorWeaponFrame'
$rkIndicatorVisual = Get-CSharpBlock $rkControllerSource 'private static bool TryGetIndicatorVisualData('
$rkVanillaCooldownCandidate = Get-CSharpBlock $rkControllerSource 'private static bool TryGetPotentialVanillaRangedCooldown('
$rkFocusedTargetWithState = Get-CSharpBlock $rkControllerSource 'private static bool TryGetFocusedWeaponTarget('
$rkCloseTargetWithState = Get-CSharpBlock $rkControllerSource 'private static bool TryGetAttackGizmoCloseTarget('
$rkVisualDataWithState = Get-CSharpBlock $rkControllerSource 'private static bool TryGetVisualData('

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

if ($rkCandidate -notmatch 'bool statePresent' -or
    $rkCandidate -notmatch 'CurJobDef\s*==\s*RimKataDefOf\.RimKata_Attack' -or
    $rkCandidate -notmatch '\|\|\s*statePresent' -or
    $rkCandidate -match 'RimKataCombatStatePresenceCache|Drafted|Moving|Stance|GetComponent|GetState|\.Active|TryGetEnabledCombatVerb') {
    throw 'Gun-ready candidate gate no longer reuses only Job or supplied state presence.'
}

$rkRefConsumerPattern = 'ref readonly\s+RimKataGunReadyDrawContext\s+\w+\s*=\s*ref\s+RimKataGunReadyDrawUtility\.Current;'
$rkRefConsumerBlocks = @(
    $rkCarryPush,
    $rkTryDrawPair,
    $rkDeflectionPatch,
    $rkCarryGunReadyPatch,
    $rkDrawGunReadyPatch
)
if ($rkGunReadyUtility -notmatch 'public static ref readonly\s+RimKataGunReadyDrawContext\s+Current\s*=>\s*ref current;' -or
    [regex]::Matches($rkVisualSource, 'RimKataGunReadyDrawUtility\.Current').Count -ne 5 -or
    [regex]::Matches($rkVisualSource, $rkRefConsumerPattern).Count -ne 5 -or
    $rkVisualSource -match 'RimKataGunReadyDrawContext\s+\w+\s*=\s*RimKataGunReadyDrawUtility\.Current;') {
    throw 'Gun-ready Current or one of its five consumers regained a full context copy.'
}
foreach ($rkRefConsumerBlock in $rkRefConsumerBlocks) {
    if ([regex]::Matches($rkRefConsumerBlock, $rkRefConsumerPattern).Count -ne 1) {
        throw 'A gun-ready context consumer no longer holds exactly one readonly reference.'
    }
}

if ($rkGunReadyUtility -notmatch '\[ThreadStatic\]\s*private static int scopeDepth;' -or
    $rkGunReadyUtility -notmatch '\[ThreadStatic\]\s*private static RimKataGunReadyDrawContext\[\] nestedContexts;' -or
    $rkPush -match '\bRimKataGunReadyDrawContext\b|\bcurrent\s*=|\bvar\s+\w+\s*=\s*current\s*;|return\s+previous|\bnext\.' -or
    $rkPush -notmatch 'public static int Push\(Pawn pawn, PawnRenderFlags flags\)') {
    throw 'Top-level gun-ready scopes regained a full context token or local context copy.'
}

$rkNestedSaveBlock = Get-CSharpBlock $rkEnterScope 'if (previousDepth > 0)'
$rkEnterSave = Get-Index $rkEnterScope 'nestedContexts[previousDepth - 1] = current;'
$rkEnterReset = Get-Index $rkEnterScope 'current = default(RimKataGunReadyDrawContext);'
$rkEnterDepthPublish = Get-Index $rkEnterScope 'scopeDepth = previousDepth + 1;'
$rkEnterScopedPublish = Get-Index $rkEnterScope 'current.scoped = true;'
if ([regex]::Matches($rkEnterScope, 'nestedContexts\[previousDepth - 1\]\s*=\s*current;').Count -ne 1 -or
    $rkNestedSaveBlock -notmatch 'EnsureNestedContextCapacity\(previousDepth\);' -or
    $rkNestedSaveBlock -notmatch 'nestedContexts\[previousDepth - 1\]\s*=\s*current;' -or
    $rkNestedSaveBlock -notmatch 'current\s*=\s*default\(RimKataGunReadyDrawContext\);' -or
    -not ($rkEnterSave -lt $rkEnterReset -and
          $rkEnterReset -lt $rkEnterDepthPublish -and
          $rkEnterDepthPublish -lt $rkEnterScopedPublish) -or
    $rkEnterScope -notmatch 'current\.portrait\s*=\s*portrait;' -or
    $rkEnterScope -notmatch 'current\.active\s*=\s*false;' -or
    $rkEnterScope -notmatch 'current\.gunReady\s*=\s*false;' -or
    $rkEnterScope -notmatch 'return scopeDepth;') {
    throw 'Gun-ready scopes no longer save a full context only for a nested scope.'
}

$rkPopNoOp = Get-CSharpBlock $rkPop 'if (scopeToken <= 0)'
$rkPopMismatch = Get-CSharpBlock $rkPop 'if (scopeDepth != scopeToken)'
$rkPopTopLevel = Get-CSharpBlock $rkPop 'if (previousDepth == 0)'
$rkPopRestore = Get-Index $rkPop 'current = nestedContexts[nestedIndex];'
$rkPopClearSlot = Get-Index $rkPop 'nestedContexts[nestedIndex] ='
$rkPopDepth = Get-Index $rkPop 'scopeDepth = previousDepth;'
if ($rkPopNoOp -notmatch '^if\s*\(scopeToken <= 0\)\s*\{\s*return;\s*\}$' -or
    $rkPopMismatch -notmatch 'current\s*=\s*default\(RimKataGunReadyDrawContext\);' -or
    $rkPopMismatch -notmatch 'scopeDepth\s*=\s*0;' -or
    $rkPopMismatch -notmatch 'Array\.Clear\(' -or
    $rkPopTopLevel -notmatch 'current\s*=\s*default\(RimKataGunReadyDrawContext\);' -or
    -not ($rkPopRestore -lt $rkPopClearSlot -and
          $rkPopClearSlot -lt $rkPopDepth) -or
    $rkPop -notmatch 'int nestedIndex\s*=\s*previousDepth - 1;' -or
    $rkPop -notmatch 'nestedContexts\[nestedIndex\]\s*=\s*default\(RimKataGunReadyDrawContext\);') {
    throw 'Gun-ready scope Pop no longer provides no-op, LIFO restore, and slot cleanup boundaries.'
}

$rkPatchPrefix = Get-CSharpBlock $rkGunReadyPatch 'public static void Prefix('
$rkPatchFinalizer = Get-CSharpBlock $rkGunReadyPatch 'public static Exception Finalizer('
$rkPatchStateZero = Get-Index $rkPatchPrefix '__state = 0;'
$rkPatchPush = Get-Index $rkPatchPrefix '__state = RimKataGunReadyDrawUtility.Push(pawn, flags);'
if ($rkGunReadyPatch -match 'RimKataGunReadyDrawContext\s+__state' -or
    $rkPatchPrefix -notmatch 'out int __state' -or
    $rkPatchFinalizer -notmatch 'int __state' -or
    $rkPatchFinalizer -notmatch 'RimKataGunReadyDrawUtility\.Pop\(__state\);' -or
    $rkPatchStateZero -ge $rkPatchPush -or
    $rkPushCatch -notmatch '^catch\s*\{\s*Pop\(scopeToken\);\s*throw;\s*\}$') {
    throw 'Gun-ready Harmony scope token no longer cleans up and rethrows Push failures safely.'
}

$rkEnterCall = Get-Index $rkPush 'int scopeToken = EnterScope(portrait);'
$rkTryStart = Get-Index $rkPush 'try'
$rkStatePresence = Get-Index $rkPush 'RimKataCombatStatePresenceCache.Contains('
$rkResponseProbe = Get-Index $rkPush 'TryGetResponseParticipantLoadout('
$rkCandidateCall = Get-Index $rkPush 'MayNeedGunReadyTarget(pawn, statePresent)'
$rkCandidateChecks = Get-Index $rkPush 'bool gunReadyCandidate = mayNeedGunReadyTarget'
$rkPawnConditionChecks = Get-Index $rkPush '&& !pawn.Dead'
$rkNeedsContext = Get-Index $rkPush 'if (!needsActiveContext)'
$rkSnapshotProbe = Get-Index $rkPush 'TryGetCachedResponseSnapshot('
$rkPawnPublish = Get-Index $rkPush 'current.pawn = pawn;'
$rkPrimaryPublish = Get-Index $rkPush 'current.primary = primary;'
$rkSecondaryPublish = Get-Index $rkPush 'current.secondary = secondary;'
$rkSnapshotPublish = Get-Index $rkPush 'out current.snapshot'
$rkSnapshotActivePublish = Get-Index $rkPush 'current.snapshotActive = snapshotActive;'
$rkActivePublish = Get-Index $rkPush 'current.active = true;'
$rkGunReadyGate = Get-Index $rkPush 'if (!gunReadyCandidate)'
$rkComponent = Get-Index $rkPush 'GetComponent<RimKataMapComponent>'
$rkTarget = Get-Index $rkPush 'TryGetGunReadyTarget'
$rkVerb = Get-Index $rkPush 'TryGetEnabledCombatVerb'
$rkAimPublish = Get-Index $rkPush 'current.aimAngle = aimAngle;'
$rkGunReadyPublish = Get-Index $rkPush 'current.gunReady = true;'
$rkDirectPublishPattern = 'current\.(pawn|primary|secondary|snapshotActive|active|aimAngle|gunReady)\s*='
if ([regex]::Matches($rkPush, $rkDirectPublishPattern).Count -ne 7 -or
    -not ($rkEnterCall -lt $rkTryStart -and
          $rkStatePresence -lt $rkResponseProbe -and
          $rkStatePresence -lt $rkCandidateCall -and
          $rkStatePresence -lt $rkSnapshotProbe -and
          $rkCandidateCall -lt $rkCandidateChecks -and
          $rkCandidateChecks -lt $rkPawnConditionChecks -and
          $rkPawnConditionChecks -lt $rkNeedsContext -and
          $rkNeedsContext -lt $rkSnapshotProbe -and
          $rkSnapshotProbe -lt $rkSnapshotPublish -and
          $rkSnapshotPublish -lt $rkPawnPublish -and
          $rkPawnPublish -lt $rkPrimaryPublish -and
          $rkPrimaryPublish -lt $rkSecondaryPublish -and
          $rkSecondaryPublish -lt $rkSnapshotActivePublish -and
          $rkSnapshotActivePublish -lt $rkActivePublish -and
          $rkActivePublish -lt $rkGunReadyGate -and
          $rkGunReadyGate -lt $rkComponent -and
          $rkComponent -lt $rkTarget -and
          $rkTarget -lt $rkVerb -and
          $rkVerb -lt $rkAimPublish -and
          $rkAimPublish -lt $rkGunReadyPublish)) {
    throw 'Gun-ready shared-state gates or direct context publications moved across a required boundary.'
}

if ([regex]::Matches($rkPush, 'RimKataCombatStatePresenceCache\.Contains').Count -ne 1) {
    throw 'Gun-ready render path no longer shares exactly one state-presence probe.'
}

if ($rkPush -notmatch 'bool\s+statePresent\s*=\s*RimKataCombatStatePresenceCache\.Contains\(\s*pawn,\s*pawn\.Map\s*\);' -or
    $rkPush -notmatch 'bool\s+mayNeedGunReadyTarget\s*=\s*rimKataUser\s*&&\s*MayNeedGunReadyTarget\(\s*pawn,\s*statePresent\s*\);' -or
    $rkPush -notmatch 'bool\s+gunReadyCandidate\s*=\s*mayNeedGunReadyTarget\s*&&\s*!pawn\.Dead\s*&&\s*!pawn\.Downed\s*&&\s*!pawn\.IsBurning\(\)\s*&&\s*primary != null\s*&&\s*pawn\.carryTracker\?\.CarriedThing == null\s*&&\s*\(flags & PawnRenderFlags\.NeverAimWeapon\) == 0\s*&&\s*!\(pawn\.stances\?\.curStance is Stance_Busy\);') {
    throw 'Dead, downed, burning, carrying, or busy pawns escaped the guarded gun-ready candidate boundary.'
}

if ([regex]::Matches($rkPush, 'TryGetGunReadyTarget').Count -ne 1) {
    throw 'Gun-ready target lookup is no longer a single guarded call.'
}

$rkPrimaryGate = Get-Index $rkPush 'pawn.equipment?.Primary == null'
$rkLoadoutRead = Get-Index $rkPush 'TryGetCachedWorldLoadout('
if ($rkEnterCall -ge $rkPrimaryGate -or $rkPrimaryGate -ge $rkLoadoutRead) {
    throw 'Unarmed pawn gate moved behind the world-loadout lookup.'
}

$rkResponseGate = Get-CSharpBlock $rkPush 'if (statePresent && (!rimKataUser || secondary == null))'
if ([regex]::Matches($rkPush, 'TryGetResponseParticipantLoadout').Count -ne 1 -or
    [regex]::Matches($rkPush, 'TryGetCachedResponseSnapshot').Count -ne 1 -or
    [regex]::Matches($rkPush, 'out\s+current\.snapshot').Count -ne 1 -or
    $rkPush -match 'RimKataVisualSnapshot\s+snapshot' -or
    $rkPush -match 'current\.snapshot\s*=\s*snapshot' -or
    $rkResponseGate -notmatch 'TryGetResponseParticipantLoadout' -or
    $rkPush -notmatch 'bool snapshotActive\s*=\s*statePresent\s*&&\s*\(secondary != null \|\| responseParticipant\)\s*&&\s*RimKataVisualUtility\.TryGetCachedResponseSnapshot\(\s*pawn,\s*responseParticipant,\s*out current\.snapshot\);') {
    throw 'Stateless renderers no longer defer response-participant and snapshot probes.'
}

if ($rkPush -notmatch 'bool\s+needsActiveContext\s*=\s*secondary != null\s*\|\|\s*responseParticipant\s*\|\|\s*gunReadyCandidate;\s*if \(!needsActiveContext\)\s*\{\s*return scopeToken;\s*\}' -or
    $rkNeedsContext -ge $rkPawnPublish) {
    throw 'Gun-ready render path regained an active context for an irrelevant armed pawn.'
}

$rkPortraitGate = Get-Index $rkPush 'if (portrait || pawn?.Spawned != true)'
if (-not ($rkEnterCall -lt $rkPortraitGate -and
          $rkPortraitGate -lt $rkPrimaryGate) -or
    $rkEnterScope -notmatch 'current\.scoped\s*=\s*true;' -or
    $rkEnterScope -notmatch 'current\.portrait\s*=\s*portrait;' -or
    $rkEnterScope -notmatch 'current\.active\s*=\s*false;' -or
    $rkEnterScope -notmatch 'current\.gunReady\s*=\s*false;') {
    throw 'Gun-ready scope no longer publishes an inactive context before early returns.'
}

$rkCarryRefConsumerPattern = 'ref readonly\s+RimKataCarryDrawContext\s+\w+\s*=\s*ref\s+RimKataCarryDrawUtility\.Current;'
$rkCarryRefConsumerBlocks = @(
    $rkTryDrawPair,
    $rkDrawWeapon,
    $rkSymmetricIdleSecondaryLoc,
    $rkResolveEquipmentPivot
)
if ($rkCarryUtility -notmatch 'public static ref readonly\s+RimKataCarryDrawContext\s+Current\s*=>\s*ref current;' -or
    [regex]::Matches($rkVisualSource, 'RimKataCarryDrawUtility\.Current').Count -ne 4 -or
    [regex]::Matches($rkVisualSource, $rkCarryRefConsumerPattern).Count -ne 4 -or
    $rkVisualSource -match 'RimKataCarryDrawContext\s+\w+\s*=\s*RimKataCarryDrawUtility\.Current;') {
    throw 'Carry Current or one of its four consumers regained a full context copy.'
}
foreach ($rkCarryRefConsumerBlock in $rkCarryRefConsumerBlocks) {
    if ([regex]::Matches($rkCarryRefConsumerBlock, $rkCarryRefConsumerPattern).Count -ne 1) {
        throw 'A carry context consumer no longer holds exactly one readonly reference.'
    }
}

if ($rkCarryUtility -notmatch '\[ThreadStatic\]\s*private static int scopeDepth;' -or
    $rkCarryUtility -notmatch '\[ThreadStatic\]\s*private static RimKataCarryDrawContext\[\] nestedContexts;' -or
    $rkCarryPush -match '\bRimKataCarryDrawContext\b|\bcurrent\s*=|\bvar\s+\w+\s*=\s*current\s*;|return\s+previous|\bnext\.' -or
    $rkCarryPush -notmatch 'public static int Push\(\s*ThingWithComps weapon,\s*Vector3 drawPos\)') {
    throw 'Top-level carry scopes regained a full context token or local context copy.'
}

$rkCarryNestedSaveBlock = Get-CSharpBlock $rkCarryEnterScope 'if (previousDepth > 0)'
$rkCarryEnterSave = Get-Index $rkCarryEnterScope 'nestedContexts[previousDepth - 1] = current;'
$rkCarryEnterReset = Get-Index $rkCarryEnterScope 'current = default(RimKataCarryDrawContext);'
$rkCarryEnterDepthPublish = Get-Index $rkCarryEnterScope 'scopeDepth = previousDepth + 1;'
$rkCarryEnterInactivePublish = Get-Index $rkCarryEnterScope 'current.active = false;'
if ([regex]::Matches($rkCarryEnterScope, 'nestedContexts\[previousDepth - 1\]\s*=\s*current;').Count -ne 1 -or
    $rkCarryNestedSaveBlock -notmatch 'EnsureNestedContextCapacity\(previousDepth\);' -or
    $rkCarryNestedSaveBlock -notmatch 'nestedContexts\[previousDepth - 1\]\s*=\s*current;' -or
    $rkCarryNestedSaveBlock -notmatch 'current\s*=\s*default\(RimKataCarryDrawContext\);' -or
    -not ($rkCarryEnterSave -lt $rkCarryEnterReset -and
          $rkCarryEnterReset -lt $rkCarryEnterDepthPublish -and
          $rkCarryEnterDepthPublish -lt $rkCarryEnterInactivePublish) -or
    $rkCarryEnterScope -notmatch 'return scopeDepth;') {
    throw 'Carry scopes no longer save a full context only for a nested scope.'
}

$rkCarryPopNoOp = Get-CSharpBlock $rkCarryPop 'if (scopeToken <= 0)'
$rkCarryPopMismatch = Get-CSharpBlock $rkCarryPop 'if (scopeDepth != scopeToken)'
$rkCarryPopTopLevel = Get-CSharpBlock $rkCarryPop 'if (previousDepth == 0)'
$rkCarryPopRestore = Get-Index $rkCarryPop 'current = nestedContexts[nestedIndex];'
$rkCarryPopClearSlot = Get-Index $rkCarryPop 'nestedContexts[nestedIndex] ='
$rkCarryPopDepth = Get-Index $rkCarryPop 'scopeDepth = previousDepth;'
if ($rkCarryPopNoOp -notmatch '^if\s*\(scopeToken <= 0\)\s*\{\s*return;\s*\}$' -or
    $rkCarryPopMismatch -notmatch 'current\s*=\s*default\(RimKataCarryDrawContext\);' -or
    $rkCarryPopMismatch -notmatch 'scopeDepth\s*=\s*0;' -or
    $rkCarryPopMismatch -notmatch 'Array\.Clear\(' -or
    $rkCarryPopTopLevel -notmatch 'current\s*=\s*default\(RimKataCarryDrawContext\);' -or
    -not ($rkCarryPopRestore -lt $rkCarryPopClearSlot -and
          $rkCarryPopClearSlot -lt $rkCarryPopDepth) -or
    $rkCarryPop -notmatch 'int nestedIndex\s*=\s*previousDepth - 1;' -or
    $rkCarryPop -notmatch 'nestedContexts\[nestedIndex\]\s*=\s*default\(RimKataCarryDrawContext\);') {
    throw 'Carry scope Pop no longer provides no-op, LIFO restore, and slot cleanup boundaries.'
}

$rkCarryPatchPrefix = Get-CSharpBlock $rkCarryPatch 'public static void Prefix('
$rkCarryPatchFinalizer = Get-CSharpBlock $rkCarryPatch 'public static Exception Finalizer('
$rkCarryPatchStateZero = Get-Index $rkCarryPatchPrefix '__state = 0;'
$rkCarryPatchPush = Get-Index $rkCarryPatchPrefix '__state = RimKataCarryDrawUtility.Push(weapon, drawPos);'
if ($rkCarryPatch -match 'RimKataCarryDrawContext\s+__state' -or
    $rkCarryPatchPrefix -notmatch 'out int __state' -or
    $rkCarryPatchFinalizer -notmatch 'int __state' -or
    $rkCarryPatchFinalizer -notmatch 'RimKataCarryDrawUtility\.Pop\(__state\);' -or
    $rkCarryPatchStateZero -ge $rkCarryPatchPush -or
    $rkCarryPushCatch -notmatch '^catch\s*\{\s*Pop\(scopeToken\);\s*throw;\s*\}$') {
    throw 'Carry Harmony scope token no longer cleans up and rethrows Push failures safely.'
}

$rkCarryScopeTokenReturns = [regex]::Matches($rkCarryPush, 'return\s+scopeToken;').Count
if ($rkCarryScopeTokenReturns -lt 3 -or
    [regex]::Matches($rkCarryPush, '\breturn\b').Count -ne $rkCarryScopeTokenReturns) {
    throw 'A successful carry Push path no longer returns only its integer scope token.'
}

$rkCarryEnterCall = Get-Index $rkCarryPush 'int scopeToken = EnterScope();'
$rkCarryTryStart = Get-Index $rkCarryPush 'try'
$rkCarryGunReadyRead = Get-Index $rkCarryPush 'ref RimKataGunReadyDrawUtility.Current;'
$rkCarryScoped = Get-Index $rkCarryPush 'if (renderContext.scoped)'
$rkCarryFallback = Get-Index $rkCarryPush 'FindPawnOwner(weapon)'
$rkCarryScopedBlock = Get-CSharpBlock $rkCarryPush 'if (renderContext.scoped)'
$rkCarryScopedActiveBlock = Get-CSharpBlock $rkCarryScopedBlock 'if (renderContext.active'
$rkCarryScopedSnapshotBlock = Get-CSharpBlock $rkCarryScopedActiveBlock 'if (renderContext.snapshotActive)'
$rkCarryScopedPawnPublish = Get-Index $rkCarryScopedActiveBlock 'current.pawn = renderContext.pawn;'
$rkCarryScopedPrimaryPublish = Get-Index $rkCarryScopedActiveBlock 'current.primary = renderContext.primary;'
$rkCarryScopedSecondaryPublish = Get-Index $rkCarryScopedActiveBlock 'current.secondary = renderContext.secondary;'
$rkCarryScopedSnapshotPublish = Get-Index $rkCarryScopedActiveBlock 'current.snapshot = renderContext.snapshot;'
$rkCarryScopedSnapshotActivePublish = Get-Index $rkCarryScopedActiveBlock 'current.snapshotActive ='
$rkCarryScopedDrawPosPublish = Get-Index $rkCarryScopedActiveBlock 'current.drawPos = drawPos;'
$rkCarryScopedActivePublish = Get-Index $rkCarryScopedActiveBlock 'current.active = true;'
if (-not ($rkCarryEnterCall -lt $rkCarryTryStart -and
          $rkCarryTryStart -lt $rkCarryGunReadyRead -and
          $rkCarryGunReadyRead -lt $rkCarryScoped -and
          $rkCarryScoped -lt $rkCarryFallback) -or
    $rkCarryScopedBlock -match 'FindPawnOwner|TryGetCachedWorldLoadout|TryGetResponseParticipantLoadout|TryGetCachedActiveSnapshot' -or
    $rkCarryScopedBlock -notmatch 'return scopeToken;\s*\}$' -or
    $rkCarryScopedActiveBlock -notmatch 'renderContext\.active\s*&&\s*renderContext\.primary == weapon' -or
    $rkCarryScopedSnapshotBlock -notmatch 'current\.snapshot\s*=\s*renderContext\.snapshot;' -or
    -not ($rkCarryScopedPawnPublish -lt $rkCarryScopedPrimaryPublish -and
          $rkCarryScopedPrimaryPublish -lt $rkCarryScopedSecondaryPublish -and
          $rkCarryScopedSecondaryPublish -lt $rkCarryScopedSnapshotPublish -and
          $rkCarryScopedSnapshotPublish -lt $rkCarryScopedSnapshotActivePublish -and
          $rkCarryScopedSnapshotActivePublish -lt $rkCarryScopedDrawPosPublish -and
          $rkCarryScopedDrawPosPublish -lt $rkCarryScopedActivePublish)) {
    throw 'Scoped carry rendering no longer reuses GunReady data without entering owner or loadout fallback work.'
}

$rkCarryWorldLoadout = Get-Index $rkCarryPush 'TryGetCachedWorldLoadout('
$rkCarryResponseLoadout = Get-Index $rkCarryPush 'TryGetResponseParticipantLoadout('
$rkCarryParticipantPrimary = Get-Index $rkCarryPush 'primary = participantPrimary;'
$rkCarryReject = Get-Index $rkCarryPush 'if ((!rimKataUser && !responseParticipant)'
$rkCarrySecondary = Get-Index $rkCarryPush 'ThingWithComps secondary = rimKataUser'
$rkCarrySnapshotProbe = Get-Index $rkCarryPush 'TryGetCachedActiveSnapshot('
$rkCarrySnapshotPublish = Get-Index $rkCarryPush 'out current.snapshot'
$rkCarryPawnPublish = Get-Index $rkCarryPush 'current.pawn = pawn;'
$rkCarryPrimaryPublish = Get-Index $rkCarryPush 'current.primary = primary;'
$rkCarrySecondaryPublish = Get-Index $rkCarryPush 'current.secondary = secondary;'
$rkCarrySnapshotActivePublish = Get-Index $rkCarryPush 'current.snapshotActive = snapshotActive;'
$rkCarryDrawPosPublish = $rkCarryPush.IndexOf(
    'current.drawPos = drawPos;',
    $rkCarryPawnPublish,
    [StringComparison]::Ordinal)
$rkCarryActivePublish = $rkCarryPush.IndexOf(
    'current.active = true;',
    $rkCarryPawnPublish,
    [StringComparison]::Ordinal)
$rkCarryDirectPublishPattern = 'current\.(pawn|primary|secondary|snapshotActive|drawPos|active)\s*='
if ([regex]::Matches($rkCarryPush, $rkCarryDirectPublishPattern).Count -ne 12 -or
    [regex]::Matches($rkCarryPush, 'TryGetCachedActiveSnapshot').Count -ne 1 -or
    [regex]::Matches($rkCarryPush, 'out\s+current\.snapshot').Count -ne 1 -or
    [regex]::Matches($rkCarryPush, 'current\.snapshot\s*=\s*renderContext\.snapshot;').Count -ne 1 -or
    $rkCarryPush -match 'RimKataVisualSnapshot\s+\w+' -or
    -not ($rkCarryFallback -lt $rkCarryWorldLoadout -and
          $rkCarryWorldLoadout -lt $rkCarryResponseLoadout -and
          $rkCarryResponseLoadout -lt $rkCarryParticipantPrimary -and
          $rkCarryParticipantPrimary -lt $rkCarryReject -and
          $rkCarryReject -lt $rkCarrySecondary -and
          $rkCarrySecondary -lt $rkCarrySnapshotProbe -and
          $rkCarrySnapshotProbe -lt $rkCarrySnapshotPublish -and
          $rkCarrySnapshotPublish -lt $rkCarryPawnPublish -and
          $rkCarryPawnPublish -lt $rkCarryPrimaryPublish -and
          $rkCarryPrimaryPublish -lt $rkCarrySecondaryPublish -and
          $rkCarrySecondaryPublish -lt $rkCarrySnapshotActivePublish -and
          $rkCarrySnapshotActivePublish -lt $rkCarryDrawPosPublish -and
          $rkCarryDrawPosPublish -lt $rkCarryActivePublish) -or
    $rkCarryPush -notmatch 'bool snapshotActive\s*=\s*\(secondary != null \|\| responseParticipant\)\s*&&\s*RimKataVisualUtility\.TryGetCachedActiveSnapshot\(\s*pawn,\s*out current\.snapshot\);\s*current\.pawn\s*=\s*pawn;\s*current\.primary\s*=\s*primary;\s*current\.secondary\s*=\s*secondary;\s*current\.snapshotActive\s*=\s*snapshotActive;\s*current\.drawPos\s*=\s*drawPos;\s*current\.active\s*=\s*true;\s*return scopeToken;') {
    throw 'Unscoped carry rendering no longer preserves its guarded direct-publication boundary.'
}

$rkPairScoped = Get-Index $rkTryDrawPair 'if (renderContext.scoped)'
$rkPairFallback = Get-Index $rkTryDrawPair 'FindPawnOwner(equipment)'
$rkPairScopedBlock = Get-CSharpBlock $rkTryDrawPair 'if (renderContext.scoped)'
$rkPairInactiveBlock = Get-CSharpBlock $rkPairScopedBlock 'if (!renderContext.active)'
if ($rkCarryScoped -ge $rkCarryFallback -or
    $rkPairScoped -ge $rkPairFallback -or
    $rkCarryScopedBlock -notmatch 'return scopeToken;\s*\}$' -or
    $rkPairInactiveBlock -notmatch 'return false;\s*\}$') {
    throw 'Inactive gun-ready contexts no longer stop downstream owner/loadout fallback work.'
}

$rkScopeTokenReturns = [regex]::Matches($rkPush, 'return\s+scopeToken;').Count
if ($rkScopeTokenReturns -lt 5 -or
    [regex]::Matches($rkPush, '\breturn\b').Count -ne $rkScopeTokenReturns -or
    $rkPush -match 'return\s+(previous|current|next);' -or
    -not ($rkVerb -lt $rkAimPublish -and
          $rkAimPublish -lt $rkGunReadyPublish)) {
    throw 'Successful gun-ready rendering no longer publishes its target-facing context.'
}

if ($rkResponseSnapshot -notmatch '!participantKnown[\s\S]*?IsParticipant\(pawn\)' -or
    $rkResponseSnapshot -match 'IsBodyVisualParticipant' -or
    $rkResponseSnapshot -notmatch 'TryGetActiveSnapshot\(pawn, out snapshot\)') {
    throw 'Weapon response snapshot probe regained body-only visual work.'
}

$rkIndicatorSelected = Get-Index $rkCombatIndicators '!Find.Selector.IsSelected(pawn)'
$rkIndicatorDormancy = Get-Index $rkCombatIndicators 'MayNeedCombatIndicatorFrame(pawn)'
$rkIndicatorLoadout = Get-Index $rkCombatIndicators 'TryGetUiLoadout('
if (-not ($rkIndicatorSelected -lt $rkIndicatorDormancy -and
          $rkIndicatorDormancy -lt $rkIndicatorLoadout)) {
    throw 'Combat-indicator dormancy gate moved behind UI loadout resolution.'
}

if ($rkIndicatorCandidate -notmatch 'RimKataCombatStatePresenceCache\.Contains\([\s\S]*?pawn,[\s\S]*?pawn\.Map\)' -or
    $rkIndicatorCandidate -notmatch 'TryGetPotentialVanillaRangedCooldown\(' -or
    $rkIndicatorCandidate -match 'StateFor|GetComponent|GetState|RimKataEligibility|CombatVerb|TryGetUiLoadout') {
    throw 'Combat-indicator candidate gate regained a deep state, eligibility, or loadout lookup.'
}

$rkDodgePhaseGate = Get-Index $rkDodgeOffsetPrefix 'phase == DrawPhase.EnsureInitialized'
$rkDodgePresenceGate = Get-Index $rkDodgeOffsetPrefix 'RimKataCombatStatePresenceCache.Contains('
$rkDodgeAimRead = Get-Index $rkDodgeOffsetPrefix 'Stance_RimKataAim movingAim'
$rkDodgeSnapshotRead = Get-Index $rkDodgeOffsetPrefix 'TryGetCachedActiveSnapshot('
if (-not ($rkDodgePhaseGate -lt $rkDodgePresenceGate -and
          $rkDodgePresenceGate -lt $rkDodgeAimRead -and
          $rkDodgeAimRead -lt $rkDodgeSnapshotRead) -or
    [regex]::Matches($rkDodgeOffsetPrefix, 'RimKataCombatStatePresenceCache\.Contains').Count -ne 1 -or
    $rkDodgeOffsetPrefix -notmatch 'if\s*\(phase\s*==\s*DrawPhase\.EnsureInitialized\)\s*\{\s*return;\s*\}' -or
    $rkDodgeOffsetPrefix -notmatch 'if\s*\(\s*!RimKataCombatStatePresenceCache\.Contains\(\s*___pawn,\s*___pawn\?\.Map\s*\)\s*\)\s*\{\s*return;\s*\}' -or
    $rkDodgeOffsetPrefix -match 'GetComponent|GetState|StateFor|RimKataEligibility|CombatVerb|TryGetEnabledCombatVerb') {
    throw 'Dodge-offset render gate no longer rejects stateless pawns before aim and snapshot reads.'
}

if ($rkVanillaCooldownCandidate -notmatch 'Stance_Cooldown' -or
    $rkVanillaCooldownCandidate -notmatch '\|\|\s*verb\.IsMeleeAttack' -or
    $rkVanillaCooldownCandidate -notmatch 'cooldown\.ticksLeft\s*<=\s*0' -or
    $rkVanillaCooldownCandidate -notmatch '!cooldown\.focusTarg\.IsValid' -or
    $rkVanillaCooldownCandidate -notmatch 'drawAimPie\s*!=\s*true' -or
    $rkVanillaCooldownCandidate -notmatch 'cooldownWeapon\s*==\s*null' -or
    $rkVanillaCooldownCandidate -notmatch 'weapon\s*!=\s*null\s*&&\s*cooldownWeapon\s*!=\s*weapon') {
    throw 'Combat-indicator dormancy gate no longer preserves the drawable vanilla ranged cooldown fallback.'
}

if ([regex]::Matches($rkIndicatorFrame, 'StateFor\(pawn, false\)').Count -ne 1 -or
    [regex]::Matches($rkIndicatorFrame, 'GetCombatIndicatorWeaponFrame\(').Count -ne 2 -or
    [regex]::Matches($rkIndicatorFrame, 'TryGetPotentialVanillaRangedCooldown\(').Count -ne 1 -or
    $rkIndicatorFrame -match 'CombatVerb\(|ShouldPauseFireForDodge|IsVisualLocked' -or
    $rkIndicatorFrame -notmatch 'movingFireEnabled\s*==\s*false[\s\S]*?state\?\.DodgeVisualLocked\s*==\s*true') {
    throw 'Combat-indicator frame no longer shares one state lookup across both weapon slots.'
}

if ($rkIndicatorWeaponFrame -match 'StateFor\(|CombatVerb\(|TryGetPotentialVanillaRangedCooldown\(' -or
    $rkIndicatorWeaponFrame -notmatch 'TryGetFocusedWeaponTarget\([\s\S]*?state' -or
    $rkIndicatorWeaponFrame -notmatch 'TryGetIndicatorVisualData\([\s\S]*?state') {
    throw 'Combat-indicator weapon frame stopped consuming the shared state snapshot.'
}

if ([regex]::Matches($rkCombatIndicators, 'GetCombatIndicatorFrameData\(').Count -ne 1 -or
    $rkCombatIndicators -match 'TryGetFocusedWeaponTarget|TryGetAttackGizmoCloseTarget|TryGetIndicatorVisualData|CombatVerb|ShouldPauseFireForDodge') {
    throw 'Combat-indicator renderer regained per-consumer state or verb lookups.'
}

if ([regex]::Matches($rkIndicatorVisual, 'CombatVerb\(').Count -ne 1 -or
    $rkIndicatorVisual -match 'StateFor\(' -or
    $rkIndicatorVisual -notmatch '!internalIndicator\s*&&\s*!potentialVanillaCooldown' -or
    (Get-Index $rkIndicatorVisual '!internalIndicator && !potentialVanillaCooldown') -ge
        (Get-Index $rkIndicatorVisual 'CombatVerb(pawn, weapon)') -or
    $rkIndicatorVisual -notmatch 'potentialVanillaCooldown\s*=\s*vanillaCooldown\s*!=\s*null[\s\S]*?vanillaCooldown\.verb\?\.EquipmentSource\s*==\s*weapon' -or
    $rkIndicatorVisual -notmatch 'vanillaCooldown\.verb\s*!=\s*verb' -or
    $rkIndicatorVisual -notmatch 'data\.target\s*=\s*vanillaCooldown\.focusTarg' -or
    $rkIndicatorVisual -notmatch 'data\.warming\s*=\s*false' -or
    $rkIndicatorVisual -notmatch 'data\.warmupTicksRemaining\s*=\s*0' -or
    $rkIndicatorVisual -notmatch 'data\.warmupTotalTicks\s*=\s*0' -or
    $rkIndicatorVisual -notmatch 'data\.cooldownTicksRemaining\s*=\s*Mathf\.Max\([\s\S]*?data\.cooldownTicksRemaining,[\s\S]*?vanillaCooldown\.ticksLeft\)' -or
    $rkIndicatorVisual -notmatch 'claimsVanillaRangedCooldown\s*=\s*true') {
    throw 'Combat-indicator visual data no longer resolves its verb once and only when an indicator may be visible.'
}

if ($rkFocusedTargetWithState -match 'StateFor\(|CombatVerb\(' -or
    $rkCloseTargetWithState -match 'StateFor\(|CombatVerb\(' -or
    $rkVisualDataWithState -match 'StateFor\(|CombatVerb\(') {
    throw 'Combat-indicator state readers regained their own state or verb lookup.'
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
            checks = 0;
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

    public struct RimKataGunReadyDrawContext
    {
        public bool scoped;
        public bool portrait;
        public bool active;
        public bool gunReady;
        public int marker;
        public object heldReference;
    }

    public static class GunReadyScopeChecks
    {
        [ThreadStatic] private static RimKataGunReadyDrawContext current;
        [ThreadStatic] private static int scopeDepth;
        [ThreadStatic] private static RimKataGunReadyDrawContext[] nestedContexts;

        $rkEnterScope

        $rkEnsureNestedCapacity

        $rkPop

        private static int checks;

        private static void Check(bool condition, string name)
        {
            if (!condition) throw new Exception("FAIL: " + name);
            checks++;
        }

        private static void ResetScope()
        {
            current = default(RimKataGunReadyDrawContext);
            scopeDepth = 0;
            nestedContexts = null;
        }

        private static void Publish(int marker, object heldReference)
        {
            current.marker = marker;
            current.heldReference = heldReference;
            current.active = true;
            current.gunReady = true;
        }

        private static bool NestedSlotsAreClear()
        {
            if (nestedContexts == null)
            {
                return true;
            }

            for (int index = 0; index < nestedContexts.Length; index++)
            {
                if (nestedContexts[index].scoped
                    || nestedContexts[index].portrait
                    || nestedContexts[index].active
                    || nestedContexts[index].gunReady
                    || nestedContexts[index].marker != 0
                    || nestedContexts[index].heldReference != null)
                {
                    return false;
                }
            }

            return true;
        }

        private static void ThrowInsideScope(bool portrait)
        {
            int scopeToken = EnterScope(portrait);
            try
            {
                Publish(999, new object());
                throw new InvalidOperationException("scope failure");
            }
            $rkPushCatch
        }

        public static int Run()
        {
            checks = 0;
            ResetScope();

            int topToken = EnterScope(false);
            Check(topToken == 1 && scopeDepth == 1,
                "top-level scope uses an integer depth token");
            Check(current.scoped && !current.portrait
                    && !current.active && !current.gunReady,
                "top-level scope publishes only its inactive header");
            Check(nestedContexts == null,
                "top-level scope does not allocate or save a context");

            object outerReference = new object();
            Publish(11, outerReference);
            int nestedToken = EnterScope(true);
            Check(nestedToken == 2 && scopeDepth == 2,
                "nested scope increments the depth token");
            Check(current.scoped && current.portrait
                    && !current.active && !current.gunReady
                    && current.marker == 0 && current.heldReference == null,
                "nested scope starts from a cleared context");
            Check(nestedContexts != null
                    && nestedContexts[0].marker == 11
                    && object.ReferenceEquals(
                        nestedContexts[0].heldReference,
                        outerReference),
                "nested scope saves the complete outer context");

            object nestedReference = new object();
            Publish(22, nestedReference);
            Pop(0);
            Check(scopeDepth == 2 && current.marker == 22
                    && object.ReferenceEquals(current.heldReference, nestedReference)
                    && nestedContexts[0].marker == 11,
                "default token is a strict no-op");

            Pop(nestedToken);
            Check(scopeDepth == 1 && current.marker == 11
                    && object.ReferenceEquals(current.heldReference, outerReference),
                "nested Pop restores the immediately preceding context");
            Check(nestedContexts[0].marker == 0
                    && nestedContexts[0].heldReference == null
                    && !nestedContexts[0].scoped,
                "nested Pop clears its saved slot");
            Pop(topToken);
            Check(scopeDepth == 0 && !current.scoped
                    && current.marker == 0 && current.heldReference == null,
                "top-level Pop clears the current context");

            bool topLevelRethrew = false;
            try
            {
                ThrowInsideScope(false);
            }
            catch (InvalidOperationException)
            {
                topLevelRethrew = true;
            }
            Check(topLevelRethrew && scopeDepth == 0 && !current.scoped
                    && current.marker == 0 && current.heldReference == null,
                "top-level Push failure pops its scope and rethrows");

            topToken = EnterScope(false);
            outerReference = new object();
            Publish(31, outerReference);
            bool nestedRethrew = false;
            try
            {
                ThrowInsideScope(true);
            }
            catch (InvalidOperationException)
            {
                nestedRethrew = true;
            }
            Check(nestedRethrew && scopeDepth == 1
                    && current.marker == 31
                    && object.ReferenceEquals(current.heldReference, outerReference),
                "nested Push failure restores its outer scope and rethrows");
            Check(nestedContexts[0].marker == 0
                    && nestedContexts[0].heldReference == null,
                "nested Push failure clears its saved slot");
            Pop(topToken);

            ResetScope();
            object firstReference = new object();
            object secondReference = new object();
            object thirdReference = new object();
            int firstToken = EnterScope(false);
            Publish(41, firstReference);
            int secondToken = EnterScope(true);
            Publish(42, secondReference);
            int thirdToken = EnterScope(false);
            Publish(43, thirdReference);
            int fourthToken = EnterScope(true);
            Publish(44, new object());
            Check(nestedContexts != null && nestedContexts.Length >= 3,
                "deep nesting grows the saved-context array");
            Pop(fourthToken);
            Check(scopeDepth == 3 && current.marker == 43
                    && object.ReferenceEquals(current.heldReference, thirdReference),
                "fourth scope restores the third scope");
            Pop(thirdToken);
            Check(scopeDepth == 2 && current.marker == 42
                    && object.ReferenceEquals(current.heldReference, secondReference),
                "third scope restores the second scope");
            Pop(secondToken);
            Check(scopeDepth == 1 && current.marker == 41
                    && object.ReferenceEquals(current.heldReference, firstReference),
                "second scope restores the first scope");
            Pop(firstToken);
            Check(scopeDepth == 0 && !current.scoped && NestedSlotsAreClear(),
                "deep LIFO unwind clears every saved slot");

            ResetScope();
            topToken = EnterScope(false);
            Publish(51, new object());
            nestedToken = EnterScope(true);
            Publish(52, new object());
            Pop(topToken);
            Check(scopeDepth == 0 && !current.scoped
                    && current.marker == 0 && current.heldReference == null
                    && NestedSlotsAreClear(),
                "mismatched Pop fails closed and clears all scope state");

            return checks;
        }
    }

    public struct RimKataCarryDrawContext
    {
        public bool active;
        public int marker;
        public object heldReference;
    }

    public static class CarryScopeChecks
    {
        [ThreadStatic] private static RimKataCarryDrawContext current;
        [ThreadStatic] private static int scopeDepth;
        [ThreadStatic] private static RimKataCarryDrawContext[] nestedContexts;

        $rkCarryEnterScope

        $rkCarryEnsureNestedCapacity

        $rkCarryPop

        private static int checks;

        private static void Check(bool condition, string name)
        {
            if (!condition) throw new Exception("FAIL: " + name);
            checks++;
        }

        private static void ResetScope()
        {
            current = default(RimKataCarryDrawContext);
            scopeDepth = 0;
            nestedContexts = null;
        }

        private static void Publish(int marker, object heldReference)
        {
            current.marker = marker;
            current.heldReference = heldReference;
            current.active = true;
        }

        private static bool NestedSlotsAreClear()
        {
            if (nestedContexts == null)
            {
                return true;
            }

            for (int index = 0; index < nestedContexts.Length; index++)
            {
                if (nestedContexts[index].active
                    || nestedContexts[index].marker != 0
                    || nestedContexts[index].heldReference != null)
                {
                    return false;
                }
            }

            return true;
        }

        private static void ThrowInsideScope()
        {
            int scopeToken = EnterScope();
            try
            {
                Publish(999, new object());
                throw new InvalidOperationException("carry scope failure");
            }
            $rkCarryPushCatch
        }

        public static int Run()
        {
            checks = 0;
            ResetScope();

            int topToken = EnterScope();
            Check(topToken == 1 && scopeDepth == 1,
                "top-level carry scope uses an integer depth token");
            Check(!current.active && current.marker == 0
                    && current.heldReference == null,
                "top-level carry scope begins inactive");
            Check(nestedContexts == null,
                "top-level carry scope does not allocate or save a context");

            object outerReference = new object();
            Publish(11, outerReference);
            int nestedToken = EnterScope();
            Check(nestedToken == 2 && scopeDepth == 2,
                "nested carry scope increments the depth token");
            Check(!current.active && current.marker == 0
                    && current.heldReference == null,
                "nested carry scope starts from a cleared context");
            Check(nestedContexts != null
                    && nestedContexts[0].active
                    && nestedContexts[0].marker == 11
                    && object.ReferenceEquals(
                        nestedContexts[0].heldReference,
                        outerReference),
                "nested carry scope saves the complete outer context");

            object nestedReference = new object();
            Publish(22, nestedReference);
            Pop(0);
            Check(scopeDepth == 2 && current.active && current.marker == 22
                    && object.ReferenceEquals(current.heldReference, nestedReference)
                    && nestedContexts[0].marker == 11,
                "default carry token is a strict no-op");

            Pop(nestedToken);
            Check(scopeDepth == 1 && current.active && current.marker == 11
                    && object.ReferenceEquals(current.heldReference, outerReference),
                "nested carry Pop restores the immediately preceding context");
            Check(!nestedContexts[0].active
                    && nestedContexts[0].marker == 0
                    && nestedContexts[0].heldReference == null,
                "nested carry Pop clears its saved slot");
            Pop(topToken);
            Check(scopeDepth == 0 && !current.active
                    && current.marker == 0 && current.heldReference == null,
                "top-level carry Pop clears the current context");

            bool topLevelRethrew = false;
            try
            {
                ThrowInsideScope();
            }
            catch (InvalidOperationException)
            {
                topLevelRethrew = true;
            }
            Check(topLevelRethrew && scopeDepth == 0 && !current.active
                    && current.marker == 0 && current.heldReference == null,
                "top-level carry Push failure pops its scope and rethrows");

            topToken = EnterScope();
            outerReference = new object();
            Publish(31, outerReference);
            bool nestedRethrew = false;
            try
            {
                ThrowInsideScope();
            }
            catch (InvalidOperationException)
            {
                nestedRethrew = true;
            }
            Check(nestedRethrew && scopeDepth == 1 && current.active
                    && current.marker == 31
                    && object.ReferenceEquals(current.heldReference, outerReference),
                "nested carry Push failure restores its outer scope and rethrows");
            Check(!nestedContexts[0].active
                    && nestedContexts[0].marker == 0
                    && nestedContexts[0].heldReference == null,
                "nested carry Push failure clears its saved slot");
            Pop(topToken);

            ResetScope();
            object firstReference = new object();
            object secondReference = new object();
            object thirdReference = new object();
            int firstToken = EnterScope();
            Publish(41, firstReference);
            int secondToken = EnterScope();
            Publish(42, secondReference);
            int thirdToken = EnterScope();
            Publish(43, thirdReference);
            int fourthToken = EnterScope();
            Publish(44, new object());
            Check(nestedContexts != null && nestedContexts.Length >= 3,
                "deep carry nesting grows the saved-context array");
            Pop(fourthToken);
            Check(scopeDepth == 3 && current.marker == 43
                    && object.ReferenceEquals(current.heldReference, thirdReference),
                "fourth carry scope restores the third scope");
            Pop(thirdToken);
            Check(scopeDepth == 2 && current.marker == 42
                    && object.ReferenceEquals(current.heldReference, secondReference),
                "third carry scope restores the second scope");
            Pop(secondToken);
            Check(scopeDepth == 1 && current.marker == 41
                    && object.ReferenceEquals(current.heldReference, firstReference),
                "second carry scope restores the first scope");
            Pop(firstToken);
            Check(scopeDepth == 0 && !current.active && NestedSlotsAreClear(),
                "deep carry LIFO unwind clears every saved slot");

            ResetScope();
            topToken = EnterScope();
            Publish(51, new object());
            nestedToken = EnterScope();
            Publish(52, new object());
            Pop(topToken);
            Check(scopeDepth == 0 && !current.active
                    && current.marker == 0 && current.heldReference == null
                    && NestedSlotsAreClear(),
                "mismatched carry Pop fails closed and clears all scope state");

            return checks;
        }
    }
}
"@

Add-Type -TypeDefinition $rkHarness -Language CSharp
$rkPresencePassed = [KRWF.RimKata.GunReadyPresenceChecks]::Run()
$rkScopePassed = [KRWF.RimKata.GunReadyScopeChecks]::Run()
$rkCarryScopePassed = [KRWF.RimKata.CarryScopeChecks]::Run()
"PASS: $rkPresencePassed executable state-presence assertions + $rkScopePassed executable gun-ready scope assertions + $rkCarryScopePassed executable carry scope assertions + gun-ready, carry, combat-indicator, and dodge-offset render dormancy source-boundary assertions; in-game profiler comparison remains required."
