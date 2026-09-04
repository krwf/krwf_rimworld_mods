param(
    [string] $RimWorldDir,
    [string] $HarmonyPath,
    [switch] $IsolatedProcess
)

$ErrorActionPreference = 'Stop'
$rkRoot = Split-Path -Parent $PSScriptRoot
[xml] $rkProject = Get-Content -LiteralPath (Join-Path $rkRoot 'Source/RimKata.csproj') -Raw
if (-not $RimWorldDir) {
    $RimWorldDir = [string] $rkProject.Project.PropertyGroup.RimWorldDir
}
if (-not $HarmonyPath) {
    $HarmonyPath = [string] ($rkProject.Project.ItemGroup.Reference |
        Where-Object Include -eq '0Harmony').HintPath
}

# The installed Harmony's MonoMod detours do not support the installed pwsh/.NET
# runtime. Use a fresh .NET Framework process, never the game process. No game or
# mod DLL is written; all generated code and detours exist only in this process.
if (-not $IsolatedProcess) {
    $rkLegacyPowerShell = Join-Path $env:WINDIR 'System32/WindowsPowerShell/v1.0/powershell.exe'
    & $rkLegacyPowerShell -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath `
        -RimWorldDir $RimWorldDir -HarmonyPath $HarmonyPath -IsolatedProcess
    if ($LASTEXITCODE -ne 0) { throw "Native binding verification failed (exit $LASTEXITCODE)." }
    return
}
if ($PSVersionTable.PSEdition -ne 'Desktop') {
    throw 'The isolated verification requires Windows PowerShell/.NET Framework.'
}

$rkManaged = Join-Path $RimWorldDir 'RimWorldWin64_Data/Managed'
$rkGameAssembly = Join-Path $rkManaged 'Assembly-CSharp.dll'
$rkUnityAssembly = Join-Path $rkManaged 'UnityEngine.CoreModule.dll'
$rkSource = Get-Content -LiteralPath (Join-Path $rkRoot 'Source/RimKataCombatMath.cs') -Raw -Encoding UTF8
$rkDeclarations = @()
foreach ($rkField in @('NativeMeleeHitChance', 'NativeMeleeDodgeChance')) {
    $rkPattern = 'private static readonly Func<Verb_MeleeAttack, LocalTargetInfo, float>\s+' +
        [regex]::Escape($rkField) + '\s*=[\s\S]*?;'
    $rkMatch = [regex]::Match($rkSource, $rkPattern)
    if (-not $rkMatch.Success) { throw "Missing production declaration: $rkField" }
    $rkDeclarations += $rkMatch.Value
}
foreach ($rkField in @('MeleeSurpriseAttack', 'SelectedMeleeVerb')) {
    $rkPattern = 'private static readonly AccessTools\.FieldRef<\s*(\w+)\s*,\s*(\w+)\s*>\s+' +
        [regex]::Escape($rkField) + '\s*=\s*AccessTools\.FieldRefAccess<\s*\1\s*,\s*\2\s*>\("([^"\r\n]+)"\);'
    $rkMatch = [regex]::Match($rkSource, $rkPattern)
    if (-not $rkMatch.Success) { throw "Missing production declaration: $rkField" }
    # Windows PowerShell's legacy C# compiler rejects even referencing a ref-return
    # delegate type. Extract the exact generic types and field name from production
    # and invoke that same real generic AccessTools factory through reflection.
    $rkDeclarations += 'private static readonly Delegate ' + $rkField +
        ' = BindFieldRef(typeof(' + $rkMatch.Groups[1].Value + '), typeof(' +
        $rkMatch.Groups[2].Value + '), "' + $rkMatch.Groups[3].Value + '");'
}
$rkDelegateDeclarations = $rkDeclarations -join "`n"

[Reflection.Assembly]::LoadFrom($rkUnityAssembly) | Out-Null
[Reflection.Assembly]::LoadFrom($rkGameAssembly) | Out-Null
[Reflection.Assembly]::LoadFrom($HarmonyPath) | Out-Null
$rkHarness = @"
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using HarmonyLib;
using RimWorld;
using Verse;

public static class MeleeProbabilityNativeBindingChecks
{
    $rkDelegateDeclarations

    private static int checks;
    private static int hitPostfixCalls;
    private static int dodgePostfixCalls;

    private static Delegate BindFieldRef(Type owner, Type field, string name)
    {
        foreach (var method in typeof(AccessTools).GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            var parameters = method.GetParameters();
            if (method.Name == "FieldRefAccess" && method.IsGenericMethodDefinition &&
                method.GetGenericArguments().Length == 2 && parameters.Length == 1 &&
                parameters[0].ParameterType == typeof(string))
            {
                return (Delegate)method.MakeGenericMethod(owner, field).Invoke(null, new object[] { name });
            }
        }
        throw new MissingMethodException("AccessTools.FieldRefAccess<T, F>(string)");
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        checks++;
        Console.WriteLine("PASS: " + name);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void HitPostfix(ref float __result)
    {
        hitPostfixCalls++;
        __result = 0.375f;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void DodgePostfix(ref float __result)
    {
        dodgePostfixCalls++;
        __result = 0.625f;
    }

    public static int Run()
    {
        Console.WriteLine("Runtime: " + Environment.Version);
        Console.WriteLine("Game assembly: " + typeof(Verb_MeleeAttack).Assembly.Location);
        Console.WriteLine("Harmony assembly: " + typeof(Harmony).Assembly.Location);
        var hitMethod = AccessTools.Method(typeof(Verb_MeleeAttack), "GetNonMissChance");
        var dodgeMethod = AccessTools.Method(typeof(Verb_MeleeAttack), "GetDodgeChance");
        Check(hitMethod != null && hitMethod.IsPrivate && !hitMethod.IsStatic,
            "real private instance GetNonMissChance resolved");
        Check(dodgeMethod != null && dodgeMethod.IsPrivate && !dodgeMethod.IsStatic,
            "real private instance GetDodgeChance resolved");
        // The production FieldRef factories are invoked reflectively because the
        // legacy compiler rejects ref-return delegate types. No RimKata assembly
        // or Mod initialization is loaded; real Harmony creates the delegates.
        Check(MeleeSurpriseAttack != null &&
            MeleeSurpriseAttack.Method.ReturnType == typeof(bool).MakeByRefType() &&
            AccessTools.Field(typeof(Verb), "surpriseAttack").FieldType == typeof(bool),
            "production surpriseAttack FieldRef binds the real bool field");
        Check(SelectedMeleeVerb != null &&
            SelectedMeleeVerb.Method.ReturnType == typeof(Verb).MakeByRefType() &&
            AccessTools.Field(typeof(Pawn_MeleeVerbs), "curMeleeVerb").FieldType == typeof(Verb),
            "production curMeleeVerb FieldRef binds the real Verb field");

        // Bypass Pawn/Verb constructors, stat caches, map data and Unity runtime
        // initialization. Only the native surprise-attack early return is used.
        var verb = (Verb_MeleeAttack)FormatterServices.GetUninitializedObject(typeof(Verb_MeleeAttackDamage));
        var surprise = AccessTools.Field(typeof(Verb), "surpriseAttack");
        surprise.SetValue(verb, true);
        var target = default(LocalTargetInfo);
        Check(NativeMeleeHitChance(verb, target) == 1f,
            "production hit delegate invokes real native surprise branch");
        Check(NativeMeleeDodgeChance(verb, target) == 0f,
            "production dodge delegate invokes real native surprise branch");

        // Bind BEFORE patching: this specifically tests that cached open-instance
        // AccessTools delegates still see later Harmony detours. Probe postfixes
        // are not substitutes for RimKata's real bonus/eligibility calculation.
        var harmony = new Harmony("rimkata.tests.melee-probability-native-binding");
        try
        {
            harmony.Patch(hitMethod, postfix: new HarmonyMethod(
                typeof(MeleeProbabilityNativeBindingChecks), "HitPostfix"));
            harmony.Patch(dodgeMethod, postfix: new HarmonyMethod(
                typeof(MeleeProbabilityNativeBindingChecks), "DodgePostfix"));
            Check(NativeMeleeHitChance(verb, target) == 0.375f && hitPostfixCalls == 1,
                "cached production hit delegate observes Harmony postfix");
            Check(NativeMeleeDodgeChance(verb, target) == 0.625f && dodgePostfixCalls == 1,
                "cached production dodge delegate observes Harmony postfix");
            Check((bool)surprise.GetValue(verb), "native getter calls preserve verb surprise field");
        }
        finally
        {
            harmony.Unpatch(hitMethod, HarmonyPatchType.All, harmony.Id);
            harmony.Unpatch(dodgeMethod, HarmonyPatchType.All, harmony.Id);
        }
        Check(NativeMeleeHitChance(verb, target) == 1f && NativeMeleeDodgeChance(verb, target) == 0f,
            "cached delegates return native values after unpatch");
        Console.WriteLine(checks + "/" + checks + " native binding checks passed.");
        Console.WriteLine("Scope: real installed DLL method/FieldRef binding, native early returns and temporary real Harmony detours only.");
        Console.WriteLine("NOT covered: normal stat/lighting calculation, RimKata bonus patch execution, Unity/Mono gameplay.");
        return checks;
    }
}
"@
Add-Type -TypeDefinition $rkHarness -ReferencedAssemblies @(
    $rkGameAssembly,
    $rkUnityAssembly,
    $HarmonyPath,
    'System.Core'
) -Language CSharp
[MeleeProbabilityNativeBindingChecks]::Run() | Out-Null
