$ErrorActionPreference = 'Stop'
$rkRoot = Split-Path -Parent $PSScriptRoot
$rkSource = Get-Content -LiteralPath (Join-Path $rkRoot 'Source/RimKataCombatMath.cs') -Raw -Encoding UTF8
$rkControllerSource = Get-Content -LiteralPath (Join-Path $rkRoot 'Source/RimKataDualWeaponController.cs') -Raw -Encoding UTF8
$rkFireSource = Get-Content -LiteralPath (Join-Path $rkRoot 'Source/RimKataFireUtility.cs') -Raw -Encoding UTF8

function Get-CSharpBlockAt([string] $source, [int] $start) {
    if ($start -lt 0) { throw 'Missing source block.' }
    $open = $source.IndexOf('{', $start)
    $depth = 0
    for ($index = $open; $index -lt $source.Length; $index++) {
        if ($source[$index] -eq '{') { $depth++ }
        if ($source[$index] -eq '}') {
            $depth--
            if ($depth -eq 0) { return $source.Substring($start, $index - $start + 1) }
        }
    }
    throw 'Unclosed source block.'
}

function Get-CSharpBlock([string] $source, [string] $marker) {
    return Get-CSharpBlockAt $source $source.IndexOf($marker, [StringComparison]::Ordinal)
}

function Convert-ToCompactSource([string] $source) {
    return [regex]::Replace($source, '\s+', ' ').Trim()
}

function Assert-SourceContains(
    [string] $source,
    [string] $expected,
    [string] $name) {
    if ($source.IndexOf($expected, [StringComparison]::Ordinal) -lt 0) {
        throw "Missing source contract: $name"
    }
}

function Assert-SourceExcludes(
    [string] $source,
    [string] $forbidden,
    [string] $name) {
    if ($source.IndexOf($forbidden, [StringComparison]::Ordinal) -ge 0) {
        throw "Forbidden source contract: $name"
    }
}

function Assert-SourceOrder(
    [string] $source,
    [string] $first,
    [string] $second,
    [string] $name) {
    $firstIndex = $source.IndexOf($first, [StringComparison]::Ordinal)
    $secondIndex = $source.IndexOf($second, [StringComparison]::Ordinal)
    if ($firstIndex -lt 0 -or $secondIndex -le $firstIndex) {
        throw "Invalid source order: $name"
    }
}

$rkWarmupMarker = 'public static int WarmupTicksForSingleShot('
$rkWarmupFirstIndex = $rkSource.IndexOf($rkWarmupMarker, [StringComparison]::Ordinal)
$rkWarmupSecondIndex = $rkSource.IndexOf(
    $rkWarmupMarker,
    $rkWarmupFirstIndex + $rkWarmupMarker.Length,
    [StringComparison]::Ordinal)
$rkWarmup = Get-CSharpBlockAt $rkSource $rkWarmupFirstIndex
$rkWarmupWithCount = Get-CSharpBlockAt $rkSource $rkWarmupSecondIndex
$rkCooldownMarker = 'public static int CooldownTicksForSingleShot('
$rkCooldownFirstIndex = $rkSource.IndexOf($rkCooldownMarker, [StringComparison]::Ordinal)
$rkCooldownSecondIndex = $rkSource.IndexOf(
    $rkCooldownMarker,
    $rkCooldownFirstIndex + $rkCooldownMarker.Length,
    [StringComparison]::Ordinal)
$rkCooldown = Get-CSharpBlockAt $rkSource $rkCooldownFirstIndex
$rkCooldownWithCount = Get-CSharpBlockAt $rkSource $rkCooldownSecondIndex
$rkAdjustedWarmup = Get-CSharpBlock $rkSource 'private static float AdjustedWarmupTicks(Verb verb)'
$rkRuntimeBurstCount = Get-CSharpBlock $rkSource 'private static int BurstCountForSingleShotTiming(Verb verb)'
$rkExplicitBurstCountIndex = $rkSource.IndexOf(
    'private static int BurstCountForSingleShotTiming(',
    $rkSource.IndexOf('private static int BurstCountForSingleShotTiming(', [StringComparison]::Ordinal) + 1,
    [StringComparison]::Ordinal)
$rkExplicitBurstCount = Get-CSharpBlockAt $rkSource $rkExplicitBurstCountIndex
$rkUsesConvertedTiming = Get-CSharpBlock $rkSource 'private static bool UsesConvertedSingleShotTiming(Verb verb)'

$rkArmorIndex = $rkCooldownWithCount.IndexOf(
    'cooldownTicks *= settings.GetArmorCooldownFactor(pawn);',
    [StringComparison]::Ordinal)
$rkResponseIndex = $rkCooldownWithCount.IndexOf(
    'cooldownTicks *= settings.GetResponseCooldownFactor(pawn);',
    [StringComparison]::Ordinal)
$rkSpacingIndex = $rkCooldownWithCount.IndexOf(
    'float burstSpacingTicks',
    [StringComparison]::Ordinal)
$rkDivisionIndex = $rkCooldownWithCount.IndexOf(
    '(cooldownTicks + burstSpacingTicks) / originalBurstCount',
    [StringComparison]::Ordinal)
$rkCycleRoundingIndex = $rkCooldownWithCount.IndexOf(
    'int cycleTicks = Mathf.RoundToInt(',
    [StringComparison]::Ordinal)
if ($rkArmorIndex -lt 0 -or
    $rkResponseIndex -le $rkArmorIndex -or
    $rkSpacingIndex -le $rkResponseIndex -or
    $rkDivisionIndex -le $rkSpacingIndex -or
    $rkCycleRoundingIndex -le $rkDivisionIndex) {
    throw 'Cooldown modifiers, burst spacing, division, and cycle rounding are not ordered as required.'
}

$rkOpeningAttempt = Convert-ToCompactSource (Get-CSharpBlock $rkControllerSource 'public struct RimKataVanillaOpeningAttempt')
$rkPrepareOpening = Convert-ToCompactSource (Get-CSharpBlock $rkControllerSource 'public static RimKataVanillaOpeningAttempt PrepareVanillaOpening(')
$rkPrepareSingleShot = Convert-ToCompactSource (Get-CSharpBlock $rkControllerSource 'internal static void PrepareVanillaSingleShotTiming(')
$rkApplyWarmup = Convert-ToCompactSource (Get-CSharpBlock $rkControllerSource 'internal static void ApplyVanillaSingleShotWarmup(')
$rkShouldConvert = Convert-ToCompactSource (Get-CSharpBlock $rkControllerSource 'public static bool ShouldConvertVanillaOpeningToSingleShot(')
$rkQualifiedHunt = Convert-ToCompactSource (Get-CSharpBlock $rkControllerSource 'private static bool IsQualifiedHuntSingleShot(')
$rkTryStartPatch = Convert-ToCompactSource (Get-CSharpBlock $rkControllerSource 'public static class Patch_Verb_TryStartCastOn_RimKataOpening')
$rkVanillaContext = Convert-ToCompactSource (Get-CSharpBlock $rkFireSource 'public static class RimKataVanillaSingleShotContext')
$rkWarmupCompletePatch = Convert-ToCompactSource (Get-CSharpBlock $rkFireSource 'public static class Patch_Verb_WarmupComplete_RimKataOpeningSingleShot')
$rkExperiencePatch = Convert-ToCompactSource (Get-CSharpBlock $rkFireSource 'public static class Patch_VerbProperties_RimKataShootingExperience')

$rkSourceContracts = 0
Assert-SourceContains $rkOpeningAttempt 'public int originalBurstCount;' 'opening attempt keeps the runtime burst count'
$rkSourceContracts++
Assert-SourceContains $rkPrepareOpening 'pawn.jobs?.curDriver is JobDriver_Hunt' 'Hunt is excluded before vanilla opening takeover'
$rkSourceContracts++

Assert-SourceContains $rkQualifiedHunt 'pawn.jobs?.curDriver is not JobDriver_Hunt' 'Hunt driver is required'
Assert-SourceContains $rkQualifiedHunt 'job?.def != JobDefOf.Hunt' 'Hunt JobDef is required'
Assert-SourceContains $rkQualifiedHunt 'job.verbToUse != verb' 'the Hunt-selected verb is required'
Assert-SourceContains $rkQualifiedHunt 'Pawn prey = job?.targetA.Thing as Pawn;' 'Hunt targetA supplies the prey'
Assert-SourceContains $rkQualifiedHunt 'castTarget.Thing != prey' 'the cast target must match the Hunt prey'
Assert-SourceContains $rkQualifiedHunt 'verb.CasterPawn != pawn' 'the verb caster must match the hunter'
Assert-SourceContains $rkQualifiedHunt '!RimKataEligibility.CanBeginGunKataAttack(pawn)' 'RimKata attack eligibility is required'
Assert-SourceContains $rkQualifiedHunt '!RimKataEquipmentUtility.IsWeaponEnabled( firedWeapon.def)' 'the fired weapon Def must be enabled'
Assert-SourceContains $rkQualifiedHunt 'if (firedWeapon == primaryWeapon)' 'the primary slot is accepted directly'
Assert-SourceContains $rkQualifiedHunt 'RimKataWeaponSlotUtility.CanUseSecondarySlot( pawn, primaryWeapon, true)' 'secondary use requires verified slot access'
Assert-SourceContains $rkQualifiedHunt 'firedWeapon == RimKataWeaponSlotUtility.SecondaryWeapon(pawn)' 'the fired weapon must be the registered secondary'
$rkSourceContracts++

Assert-SourceOrder $rkTryStartPatch 'PrepareVanillaOpening(' 'PrepareVanillaSingleShotTiming(' 'opening classification precedes timing capture'
Assert-SourceOrder $rkTryStartPatch 'ApplyVanillaSingleShotWarmup(' 'CommitVanillaOpening(' 'the shortened warmup is visible before opening commit'
Assert-SourceContains $rkTryStartPatch 'if (__state.prepared)' 'only an actual opening is committed'
$rkSourceContracts++

Assert-SourceOrder $rkPrepareSingleShot 'int originalBurstCount = Mathf.Max(1, verb.BurstShotCount);' 'attempt.originalBurstCount = originalBurstCount;' 'runtime burst count is captured before the getter is context-forced to one'
Assert-SourceContains $rkApplyWarmup 'RimKataCombatMath.WarmupTicksForSingleShot( verb, attempt.originalBurstCount)' 'warmup uses the captured explicit burst count'
Assert-SourceContains $rkVanillaContext 'int nextOriginalBurstCount = ActiveFor(verb) ? originalBurstCount : Mathf.Max(1, verb?.BurstShotCount ?? 1);' 'nested contexts preserve the original runtime burst count'
Assert-SourceOrder $rkVanillaContext 'int nextOriginalBurstCount =' 'activeVerb = verb;' 'the original count is read before context activation'
$rkSourceContracts++

Assert-SourceContains $rkWarmupCompletePatch 'RimKataVanillaSingleShotContext.TryGetOriginalBurstCount( __instance, out int originalBurstCount)' 'WarmupComplete recovers the original burst count'
Assert-SourceContains $rkWarmupCompletePatch 'RimKataCombatMath.CooldownTicksForSingleShot( __instance, pawn, false, originalBurstCount)' 'WarmupComplete cooldown uses the explicit original count'
Assert-SourceOrder $rkWarmupCompletePatch 'public static void Postfix(' 'public static Exception Finalizer(' 'cooldown is corrected before context cleanup'
$rkSourceContracts++

Assert-SourceContains $rkExperiencePatch 'RimKataFireContext.ActiveVerb == ownerVerb' 'controller shots retain their XP correction path'
Assert-SourceContains $rkExperiencePatch 'RimKataVanillaSingleShotContext .TryGetOriginalBurstCount(ownerVerb, out burstCount)' 'vanilla opening and Hunt shots share XP correction'
Assert-SourceContains $rkExperiencePatch '(burstCount - 1) * ownerVerb.TicksBetweenBurstShots / 60f' 'XP restores original burst spacing'
Assert-SourceContains $rkExperiencePatch '__result = (__result + originalBurstSpacing) / burstCount;' 'XP is normalized per converted shot'
$rkSourceContracts++

foreach ($rkPureVanillaBlock in @(
    $rkPrepareSingleShot,
    $rkApplyWarmup,
    $rkShouldConvert,
    $rkQualifiedHunt,
    $rkWarmupCompletePatch,
    $rkExperiencePatch)) {
    Assert-SourceExcludes $rkPureVanillaBlock 'StateFor(' 'Hunt/single-shot timing must not create or query RimKata combat state'
    Assert-SourceExcludes $rkPureVanillaBlock 'RimKataSharedTargetSearch' 'Hunt/single-shot timing must not start shared target search'
    Assert-SourceExcludes $rkPureVanillaBlock 'RimKata_Attack' 'Hunt/single-shot timing must not replace the vanilla Job'
    Assert-SourceExcludes $rkPureVanillaBlock 'StartJob(' 'Hunt/single-shot timing must not start a replacement Job'
    Assert-SourceExcludes $rkPureVanillaBlock 'QueueDedicatedFollowupJob(' 'Hunt/single-shot timing must not queue a dedicated follow-up'
    if ([regex]::IsMatch(
        $rkPureVanillaBlock,
        '\b(?:verb|__instance)\.verbProps(?:\.[A-Za-z_][A-Za-z0-9_]*)?\s*=')) {
        throw 'Forbidden source contract: single-shot timing must not mutate VerbProperties/Defs.'
    }
}
$rkSourceContracts++

$rkHarness = @"
using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace UnityEngine
{
    public static class Mathf
    {
        public static int Max(int left, int right) { return Math.Max(left, right); }
        public static float Max(float left, float right) { return Math.Max(left, right); }
        public static int RoundToInt(float value)
        {
            return (int)Math.Round(value, MidpointRounding.ToEven);
        }
    }
}

namespace RimWorld
{
    public static class StatDefOf
    {
        public static readonly object AimingDelayFactor = new object();
    }
}

namespace Verse
{
    public sealed class ThingDef
    {
        public bool enabled;
    }

    public sealed class EquipmentSource
    {
        public ThingDef def;
    }

    public sealed class Pawn
    {
        public float aimingDelayFactor = 1f;
        public bool hasEnabledArmor;

        public float GetStatValue(object stat)
        {
            return aimingDelayFactor;
        }
    }

    public sealed class VerbProperties
    {
        public float adjustedCooldownTicks;

        public float AdjustedCooldownTicks(Verb verb, Pawn pawn)
        {
            return adjustedCooldownTicks;
        }
    }

    public sealed class Verb
    {
        public Pawn CasterPawn;
        public VerbProperties verbProps;
        public EquipmentSource EquipmentSource;
        public bool IsMeleeAttack;
        public int BurstShotCount;
        public int TicksBetweenBurstShots;
        public float WarmupTime;
    }
}

namespace KRWF.RimKata
{
    public sealed class RimKataSettings
    {
        public bool singleShotConversionEnabled;
        public float armorCooldownFactor = 1f;
        public float responseCooldownFactor = 1f;

        public float GetArmorCooldownFactor(Pawn pawn)
        {
            return armorCooldownFactor;
        }

        public float GetResponseCooldownFactor(Pawn pawn)
        {
            return responseCooldownFactor;
        }
    }

    public static class RimKataMod
    {
        public static RimKataSettings Settings;
    }

    public static class RimKataEquipmentUtility
    {
        public static bool HasEnabledArmor(Pawn pawn)
        {
            return pawn.hasEnabledArmor;
        }

        public static bool IsWeaponEnabled(ThingDef def)
        {
            return def != null && def.enabled;
        }
    }

    public static class RimKataCombatMath
    {
        $rkWarmup

        $rkWarmupWithCount

        $rkCooldown

        $rkCooldownWithCount

        $rkAdjustedWarmup

        $rkRuntimeBurstCount

        $rkExplicitBurstCount

        $rkUsesConvertedTiming
    }

    public static class Checks
    {
        private static int checks;

        private static void Equal(int actual, int expected, string name)
        {
            if (actual != expected)
            {
                throw new Exception(
                    "FAIL: " + name + " (expected " + expected + ", got " + actual + ")");
            }

            checks++;
        }

        private static Verb NewVerb(Pawn pawn)
        {
            return new Verb
            {
                CasterPawn = pawn,
                verbProps = new VerbProperties { adjustedCooldownTicks = 90f },
                EquipmentSource = new EquipmentSource {
                    def = new ThingDef { enabled = true }
                },
                BurstShotCount = 3,
                TicksBetweenBurstShots = 10,
                WarmupTime = 2f
            };
        }

        public static int Run()
        {
            RimKataMod.Settings = new RimKataSettings {
                singleShotConversionEnabled = true,
                armorCooldownFactor = 0.5f,
                responseCooldownFactor = 0.8f
            };
            var pawn = new Pawn { aimingDelayFactor = 0.5f };
            var verb = NewVerb(pawn);

            Equal(RimKataCombatMath.WarmupTicksForSingleShot(verb), 20,
                "enabled ranged warmup divides adjusted ticks by burst count");
            Equal(RimKataCombatMath.CooldownTicksForSingleShot(verb, pawn, false), 37,
                "enabled ranged cooldown preserves two burst intervals");

            pawn.hasEnabledArmor = true;
            Equal(RimKataCombatMath.CooldownTicksForSingleShot(verb, pawn, true), 19,
                "armor and response modify cooldown before unmodified spacing is added");
            verb.EquipmentSource.def.enabled = false;
            Equal(RimKataCombatMath.CooldownTicksForSingleShot(verb, pawn, true), 22,
                "response factor requires an enabled weapon while armor still applies");

            pawn.hasEnabledArmor = false;
            verb.EquipmentSource.def.enabled = true;
            RimKataMod.Settings.singleShotConversionEnabled = false;
            Equal(RimKataCombatMath.WarmupTicksForSingleShot(verb), 60,
                "disabled conversion retains full ranged warmup");
            Equal(RimKataCombatMath.CooldownTicksForSingleShot(verb, pawn, false), 90,
                "disabled conversion retains full cooldown without burst spacing");
            Equal(RimKataCombatMath.CooldownTicksForSingleShot(verb, pawn, false, 5), 90,
                "explicit burst count cannot bypass disabled conversion");
            Equal(RimKataCombatMath.WarmupTicksForSingleShot(verb, 5), 60,
                "explicit burst count cannot bypass disabled warmup conversion");

            RimKataMod.Settings.singleShotConversionEnabled = true;
            verb.IsMeleeAttack = true;
            verb.BurstShotCount = 7;
            verb.TicksBetweenBurstShots = 40;
            Equal(RimKataCombatMath.WarmupTicksForSingleShot(verb), 60,
                "melee retains full warmup even with a burst-shaped definition");
            Equal(RimKataCombatMath.CooldownTicksForSingleShot(verb, pawn, false), 90,
                "melee retains full cooldown without burst spacing");
            Equal(RimKataCombatMath.CooldownTicksForSingleShot(verb, pawn, false, 5), 90,
                "explicit burst count cannot divide melee timing");
            Equal(RimKataCombatMath.WarmupTicksForSingleShot(verb, 5), 60,
                "explicit burst count cannot divide melee warmup");

            verb.IsMeleeAttack = false;
            verb.BurstShotCount = 1;
            verb.TicksBetweenBurstShots = 10;
            Equal(RimKataCombatMath.CooldownTicksForSingleShot(verb, pawn, false, 4), 30,
                "captured runtime burst count overload preserves three intervals");
            Equal(RimKataCombatMath.WarmupTicksForSingleShot(verb, 4), 15,
                "captured runtime burst count overload divides warmup");

            verb.BurstShotCount = 3;
            pawn.aimingDelayFactor = 1f;
            verb.WarmupTime = 1.02f;
            verb.verbProps.adjustedCooldownTicks = 89.2f;
            Equal(RimKataCombatMath.WarmupTicksForSingleShot(verb), 20,
                "fractional warmup rounds to the nearest divided tick");
            Equal(RimKataCombatMath.CooldownTicksForSingleShot(verb, pawn, false), 37,
                "cooldown allocates rounding so the combined cycle rounds once");
            Equal(
                RimKataCombatMath.WarmupTicksForSingleShot(verb)
                    + RimKataCombatMath.CooldownTicksForSingleShot(verb, pawn, false),
                57,
                "warmup and cooldown sum matches nearest whole single-shot cycle");

            pawn.aimingDelayFactor = 0.5f;
            verb.WarmupTime = 2f;
            verb.verbProps.adjustedCooldownTicks = 90f;
            verb.BurstShotCount = 0;
            Equal(RimKataCombatMath.WarmupTicksForSingleShot(verb), 60,
                "invalid runtime burst count clamps to one");
            Equal(RimKataCombatMath.CooldownTicksForSingleShot(verb, pawn, false), 90,
                "invalid runtime burst count adds no spacing");

            Equal(RimKataCombatMath.WarmupTicksForSingleShot(null), 0,
                "null verb warmup remains zero");
            Equal(RimKataCombatMath.CooldownTicksForSingleShot(null, pawn, false), 0,
                "null verb cooldown remains zero");
            Equal(RimKataCombatMath.CooldownTicksForSingleShot(verb, null, false), 0,
                "null pawn cooldown remains zero");

            RimKataMod.Settings = null;
            verb.BurstShotCount = 3;
            Equal(RimKataCombatMath.WarmupTicksForSingleShot(verb), 20,
                "missing settings retains the established default-enabled behavior");
            Equal(RimKataCombatMath.CooldownTicksForSingleShot(verb, pawn, false), 37,
                "missing settings retains default burst timing without modifiers");

            return checks;
        }
    }
}
"@

Add-Type -TypeDefinition $rkHarness -Language CSharp
$rkPassed = [KRWF.RimKata.Checks]::Run()
"PASS: $rkPassed executable single-shot timing assertions + $rkSourceContracts opening/Hunt integration contract groups."
