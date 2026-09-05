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

$rkSource = Get-Content -LiteralPath (Join-Path $rkRoot 'Source/RimKataFireUtility.cs') -Raw -Encoding UTF8
if ($rkSource -notmatch 'AccessTools\.Method\(\s*typeof\(Verb\),\s*"CausesTimeSlowdown",\s*new\[\]\s*\{\s*typeof\(LocalTargetInfo\)\s*\}\s*\)') {
    throw 'Production no longer resolves Verb.CausesTimeSlowdown(LocalTargetInfo).'
}
if ($rkSource -notmatch 'MethodDelegate<CausesTimeSlowdownDelegate>\(\s*method,\s*null,\s*false,\s*null\s*\)') {
    throw 'Production no longer creates the cached open-instance delegate.'
}
if ($rkSource -notmatch 'TimeSlower\s+slower\s*=\s*tickManager\.slower' -or
    $rkSource -match 'FieldRef<TickManager,\s*TimeSlower>|ResolveTimeSlowerRef') {
    throw 'Production should access the public TickManager.slower field directly.'
}

# Harmony's installed MonoMod build is intended for the game's .NET Framework
# runtime, not pwsh/.NET. Verify the real method/delegate in an isolated legacy
# PowerShell process; no game or mod DLL is written or patched.
if (-not $IsolatedProcess) {
    $rkLegacyPowerShell = Join-Path $env:WINDIR 'System32/WindowsPowerShell/v1.0/powershell.exe'
    & $rkLegacyPowerShell -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath `
        -RimWorldDir $RimWorldDir -HarmonyPath $HarmonyPath -IsolatedProcess
    if ($LASTEXITCODE -ne 0) { throw "Native combat-time binding verification failed (exit $LASTEXITCODE)." }
    return
}
if ($PSVersionTable.PSEdition -ne 'Desktop') {
    throw 'The isolated verification requires Windows PowerShell/.NET Framework.'
}

$rkManaged = Join-Path $RimWorldDir 'RimWorldWin64_Data/Managed'
$rkGameAssembly = Join-Path $rkManaged 'Assembly-CSharp.dll'
$rkUnityAssembly = Join-Path $rkManaged 'UnityEngine.CoreModule.dll'
[Reflection.Assembly]::LoadFrom($rkUnityAssembly) | Out-Null
[Reflection.Assembly]::LoadFrom($rkGameAssembly) | Out-Null
[Reflection.Assembly]::LoadFrom($HarmonyPath) | Out-Null

$rkHarness = @"
using System;
using System.Reflection;
using HarmonyLib;
using Verse;

public static class CombatTimeNativeBindingChecks
{
    private delegate bool CausesTimeSlowdownDelegate(
        Verb verb,
        LocalTargetInfo target);

    private static readonly MethodInfo CausesTimeSlowdownMethod =
        AccessTools.Method(
            typeof(Verb),
            "CausesTimeSlowdown",
            new[] { typeof(LocalTargetInfo) });
    private static readonly CausesTimeSlowdownDelegate CausesTimeSlowdown =
        AccessTools.MethodDelegate<CausesTimeSlowdownDelegate>(
            CausesTimeSlowdownMethod,
            null,
            false,
            null);
    private static int checks;

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        checks++;
        Console.WriteLine("PASS: " + name);
    }

    public static int Run()
    {
        Check(CausesTimeSlowdownMethod != null
            && CausesTimeSlowdownMethod.IsPrivate
            && !CausesTimeSlowdownMethod.IsStatic
            && CausesTimeSlowdownMethod.ReturnType == typeof(bool),
            "real private instance CausesTimeSlowdown resolved");
        var parameters = CausesTimeSlowdownMethod.GetParameters();
        Check(parameters.Length == 1
            && parameters[0].ParameterType == typeof(LocalTargetInfo),
            "real CausesTimeSlowdown target signature matches");
        Check(CausesTimeSlowdown != null,
            "production open-instance Harmony delegate binds");

        FieldInfo slower = AccessTools.Field(typeof(TickManager), "slower");
        Check(slower != null && slower.IsPublic && !slower.IsStatic
            && slower.FieldType == typeof(TimeSlower),
            "real TickManager.slower is a public instance TimeSlower field");
        MethodInfo signal = AccessTools.Method(
            typeof(TimeSlower),
            "SignalForceNormalSpeed",
            Type.EmptyTypes);
        Check(signal != null && signal.IsPublic && !signal.IsStatic
            && signal.ReturnType == typeof(void),
            "real SignalForceNormalSpeed public API matches");

        Console.WriteLine(checks + "/" + checks + " native combat-time binding checks passed.");
        Console.WriteLine("Scope: real installed game DLL metadata and cached Harmony delegate binding only.");
        Console.WriteLine("NOT covered: live combat target predicate, time control, or gameplay.");
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
[CombatTimeNativeBindingChecks]::Run() | Out-Null
