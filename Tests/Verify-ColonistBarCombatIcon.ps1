$ErrorActionPreference = 'Stop'
$rkRoot = Split-Path -Parent $PSScriptRoot
$rkSource = Get-Content -LiteralPath (Join-Path $rkRoot 'Source/RimKataColonistBarWeapons.cs') -Raw -Encoding UTF8

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

$rkPatch = Get-CSharpBlock $rkSource 'public static class Patch_ColonistBarColonistDrawer_RimKataAttackIcon'

# Exercise the production transpiler against the exact comparison shape used by
# the installed 1.6 DrawIcons method. This does not draw a live colonist bar.
$rkHarness = @"
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace HarmonyLib
{
    public struct ExceptionBlock
    {
    }

    public sealed class CodeInstruction
    {
        public OpCode opcode;
        public object operand;
        public readonly List<Label> labels = new List<Label>();
        public readonly List<ExceptionBlock> blocks = new List<ExceptionBlock>();

        public CodeInstruction(OpCode opcode)
        {
            this.opcode = opcode;
        }

        public CodeInstruction(OpCode opcode, object operand)
        {
            this.opcode = opcode;
            this.operand = operand;
        }
    }

    public static class AccessTools
    {
        private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Static | BindingFlags.Instance;

        public static FieldInfo Field(Type type, string name)
        {
            return type.GetField(name, All);
        }

        public static MethodInfo Method(Type type, string name)
        {
            return type.GetMethod(name, All);
        }
    }
}

namespace Verse
{
    public sealed class JobDef
    {
        public string defName;
    }

    public static class Log
    {
        public static readonly List<string> Warnings = new List<string>();
        public static void Warning(string message)
        {
            Warnings.Add(message);
        }
    }
}

namespace RimWorld
{
    public sealed class ColonistBarColonistDrawer
    {
    }

    public static class JobDefOf
    {
        public static readonly JobDef AttackStatic =
            new JobDef { defName = "AttackStatic" };
    }
}

namespace KRWF.RimKata
{
    public static class RimKataDefOf
    {
        public static readonly JobDef RimKata_Attack =
            new JobDef { defName = "RimKata_Attack" };
    }

    $rkPatch

    public static class ColonistBarCombatIconChecks
    {
        private static readonly FieldInfo AttackStaticField =
            AccessTools.Field(typeof(JobDefOf), "AttackStatic");
        private static readonly MethodInfo IsAttackJobMethod =
            AccessTools.Method(
                typeof(Patch_ColonistBarColonistDrawer_RimKataAttackIcon),
                "IsAttackJob");
        private static int checks;

        private static void Check(bool condition, string name)
        {
            if (!condition) throw new Exception("FAIL: " + name);
            checks++;
        }

        private static List<CodeInstruction> Transform(
            params CodeInstruction[] input)
        {
            return new List<CodeInstruction>(
                Patch_ColonistBarColonistDrawer_RimKataAttackIcon
                    .Transpiler(input));
        }

        private static void CheckValidTransform(OpCode sourceBranch, OpCode resultBranch)
        {
            object target = new object();
            var loadJob = new CodeInstruction(OpCodes.Ldloc_0);
            var comparison = new CodeInstruction(
                OpCodes.Ldsfld,
                AttackStaticField);
            comparison.labels.Add(default(Label));
            var branch = new CodeInstruction(sourceBranch, target);
            branch.blocks.Add(new ExceptionBlock());
            var tail = new CodeInstruction(OpCodes.Ret);
            var output = Transform(loadJob, comparison, branch, tail);

            Check(output.Count == 4,
                "valid transform preserves instruction count");
            Check(ReferenceEquals(output[0], loadJob)
                && ReferenceEquals(output[1], comparison)
                && ReferenceEquals(output[2], branch)
                && ReferenceEquals(output[3], tail),
                "valid transform preserves instruction objects");
            Check(comparison.opcode == OpCodes.Call
                && Equals(comparison.operand, IsAttackJobMethod),
                "AttackStatic load becomes IsAttackJob call");
            Check(branch.opcode == resultBranch
                && ReferenceEquals(branch.operand, target),
                "inequality branch becomes matching false branch");
            Check(comparison.labels.Count == 1 && branch.blocks.Count == 1,
                "valid transform preserves labels and exception blocks");
        }

        private static void CheckFailSafe(params CodeInstruction[] input)
        {
            var opcodes = new OpCode[input.Length];
            var operands = new object[input.Length];
            for (int i = 0; i < input.Length; i++)
            {
                opcodes[i] = input[i].opcode;
                operands[i] = input[i].operand;
            }
            int warningCount = Log.Warnings.Count;
            var output = Transform(input);
            Check(output.Count == input.Length,
                "fail-safe preserves instruction count");
            for (int i = 0; i < input.Length; i++)
            {
                Check(ReferenceEquals(output[i], input[i])
                    && output[i].opcode == opcodes[i]
                    && Equals(output[i].operand, operands[i]),
                    "fail-safe preserves instruction " + i);
            }
            Check(Log.Warnings.Count == warningCount + 1,
                "fail-safe emits one warning");
        }

        public static int Run()
        {
            CheckValidTransform(OpCodes.Bne_Un_S, OpCodes.Brfalse_S);
            CheckValidTransform(OpCodes.Bne_Un, OpCodes.Brfalse);
            Check(Patch_ColonistBarColonistDrawer_RimKataAttackIcon
                    .IsAttackJob(JobDefOf.AttackStatic),
                "vanilla ranged attack remains an attack icon job");
            Check(Patch_ColonistBarColonistDrawer_RimKataAttackIcon
                    .IsAttackJob(RimKataDefOf.RimKata_Attack),
                "RimKata attack gains the attack icon");
            Check(!Patch_ColonistBarColonistDrawer_RimKataAttackIcon
                    .IsAttackJob(new JobDef { defName = "Wait" })
                && !Patch_ColonistBarColonistDrawer_RimKataAttackIcon
                    .IsAttackJob(null),
                "unrelated and null jobs remain excluded");

            CheckFailSafe(
                new CodeInstruction(OpCodes.Nop),
                new CodeInstruction(OpCodes.Ret));
            CheckFailSafe(
                new CodeInstruction(OpCodes.Ldsfld, AttackStaticField),
                new CodeInstruction(OpCodes.Brtrue_S, new object()),
                new CodeInstruction(OpCodes.Ret));
            CheckFailSafe(
                new CodeInstruction(OpCodes.Ldsfld, AttackStaticField),
                new CodeInstruction(OpCodes.Bne_Un_S, new object()),
                new CodeInstruction(OpCodes.Ldsfld, AttackStaticField),
                new CodeInstruction(OpCodes.Bne_Un, new object()));
            return checks;
        }
    }
}
"@

Add-Type -TypeDefinition $rkHarness -Language CSharp
$rkPassed = [KRWF.RimKata.ColonistBarCombatIconChecks]::Run()
"PASS: $rkPassed assertions; production DrawIcons patch with synthetic IL stubs. Not an in-game colonist-bar test."
