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

$rkPatch = Get-CSharpBlock $rkSource 'public static class Patch_ColonistBar_RimKataDualWeaponIcons'

# Exercise the production patch class against synthetic IL and small UI/loadout
# stubs. This is not an in-game ColonistBar or rendering performance test.
$rkHarness = @"
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace HarmonyLib {
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public sealed class HarmonyPatch : Attribute {
        public HarmonyPatch(Type type, string methodName) { }
    }

    public struct ExceptionBlock { }

    public sealed class CodeInstruction {
        public OpCode opcode;
        public object operand;
        public readonly List<Label> labels = new List<Label>();
        public readonly List<ExceptionBlock> blocks = new List<ExceptionBlock>();
        public CodeInstruction(OpCode opcode) { this.opcode = opcode; }
        public CodeInstruction(OpCode opcode, object operand) {
            this.opcode = opcode;
            this.operand = operand;
        }
    }

    public static class CodeInstructionExtensions {
        public static bool Calls(this CodeInstruction instruction, MethodInfo method) {
            return instruction != null
                && method != null
                && (instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt)
                && Equals(instruction.operand, method);
        }
    }

    public static class AccessTools {
        private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Static | BindingFlags.Instance;
        public static MethodInfo Method(Type type, string name) {
            return type.GetMethod(name, All);
        }
        public static MethodInfo Method(Type type, string name, Type[] parameters) {
            return type.GetMethod(name, All, null, parameters, null);
        }
    }
}

namespace UnityEngine {
    public struct Vector2 {
        public float x;
        public float y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
    }
    public struct Rect {
        public float x;
        public float y;
        public float width;
        public float height;
        public Rect(float x, float y, float width, float height) {
            this.x = x; this.y = y; this.width = width; this.height = height;
        }
        public Vector2 center { get { return new Vector2(x + width / 2f, y + height / 2f); } }
    }
    public struct Matrix4x4 { public int marker; }
    public static class GUI { public static Matrix4x4 matrix; }
}

namespace RimWorld {
    public sealed class ColonistBar { public void ColonistBarOnGUI() { } }
}

namespace Verse {
    public struct Rot4 { }
    public class Thing { }
    public class ThingWithComps : Thing { }
    public sealed class Pawn_EquipmentTracker { public ThingWithComps Primary; }
    public sealed class Pawn { public Pawn_EquipmentTracker equipment = new Pawn_EquipmentTracker(); }

    public static class Log {
        public static readonly List<string> Warnings = new List<string>();
        public static void Warning(string message) { Warnings.Add(message); }
    }

    public static class UI {
        public static float LastAngle;
        public static void RotateAroundPivot(float angle, Vector2 pivot) {
            LastAngle = angle;
            GUI.matrix = new Matrix4x4 { marker = 45 };
        }
    }

    public static class Widgets {
        public static readonly List<Thing> DrawnThings = new List<Thing>();
        public static bool ThrowOnDraw;
        public static Rect LastRect;
        public static float LastAlpha;
        public static Rot4? LastRot;
        public static bool LastStackOfOne;
        public static float LastScale;
        public static bool LastGrayscale;
        public static void Reset() {
            DrawnThings.Clear();
            ThrowOnDraw = false;
            LastRect = default(Rect);
            LastAlpha = 0f;
            LastRot = null;
            LastStackOfOne = false;
            LastScale = 0f;
            LastGrayscale = false;
        }
        public static void ThingIcon(
            Rect rect,
            Thing thing,
            float alpha,
            Rot4? rot,
            bool stackOfOne,
            float scale,
            bool grayscale) {
            DrawnThings.Add(thing);
            LastRect = rect;
            LastAlpha = alpha;
            LastRot = rot;
            LastStackOfOne = stackOfOne;
            LastScale = scale;
            LastGrayscale = grayscale;
            if (ThrowOnDraw) throw new InvalidOperationException("draw failed");
        }
    }
}

namespace KRWF.RimKata {
    public static class RimKataVisualUtility {
        public static Pawn Owner;
        public static ThingWithComps Primary;
        public static ThingWithComps Secondary;
        public static bool HasLoadout = true;
        public static bool SecondaryUsable = true;

        public static Pawn FindPawnOwner(Thing thing) { return Owner; }
        public static bool TryGetUiLoadout(
            Pawn pawn,
            out ThingWithComps primary,
            out ThingWithComps secondary) {
            primary = Primary;
            secondary = Secondary;
            return HasLoadout;
        }
        public static bool IsSecondaryUsable(
            Pawn pawn,
            ThingWithComps primary,
            ThingWithComps secondary) {
            return SecondaryUsable;
        }
    }

    $rkPatch

    public static class ColonistBarWeaponIconChecks {
        private static int checks;
        private static readonly MethodInfo ThingIconMethod = AccessTools.Method(
            typeof(Widgets),
            nameof(Widgets.ThingIcon),
            new[] {
                typeof(Rect), typeof(Thing), typeof(float), typeof(Rot4?),
                typeof(bool), typeof(float), typeof(bool)
            });
        private static readonly MethodInfo SecondaryHelperMethod = AccessTools.Method(
            typeof(Patch_ColonistBar_RimKataDualWeaponIcons),
            nameof(Patch_ColonistBar_RimKataDualWeaponIcons.DrawSecondaryWeaponIcon));

        private static void Check(bool condition, string name) {
            if (!condition) throw new Exception(name);
            checks++;
        }

        private static List<CodeInstruction> Transform(params CodeInstruction[] input) {
            var method = new DynamicMethod(
                "ColonistBarOverlayFixture",
                typeof(void),
                Type.EmptyTypes,
                typeof(ColonistBarWeaponIconChecks).Module,
                true);
            var output = new List<CodeInstruction>();
            foreach (CodeInstruction instruction in Patch_ColonistBar_RimKataDualWeaponIcons
                .Transpiler(input, method.GetILGenerator())) {
                output.Add(instruction);
            }
            return output;
        }

        private static int IndexOfCall(List<CodeInstruction> codes, MethodInfo method) {
            for (int i = 0; i < codes.Count; i++) {
                if (codes[i].opcode == OpCodes.Call && Equals(codes[i].operand, method)) return i;
            }
            return -1;
        }

        private static int CountCalls(List<CodeInstruction> codes, MethodInfo method) {
            int count = 0;
            for (int i = 0; i < codes.Count; i++) {
                if (codes[i].Calls(method)) count++;
            }
            return count;
        }

        private static void CheckLocalSequence(
            List<CodeInstruction> codes,
            int start,
            OpCode opcode,
            Type[] expectedTypes,
            string name) {
            Check(start >= 0 && start + expectedTypes.Length <= codes.Count, name + " bounds");
            for (int i = 0; i < expectedTypes.Length; i++) {
                LocalBuilder local = codes[start + i].operand as LocalBuilder;
                Check(codes[start + i].opcode == opcode && local != null
                    && local.LocalType == expectedTypes[i], name + " item " + i);
            }
        }

        private static void CheckFailSafe(params CodeInstruction[] input) {
            var before = input;
            var opcodes = new OpCode[before.Length];
            var operands = new object[before.Length];
            var labelCounts = new int[before.Length];
            var blockCounts = new int[before.Length];
            for (int i = 0; i < before.Length; i++) {
                opcodes[i] = before[i].opcode;
                operands[i] = before[i].operand;
                labelCounts[i] = before[i].labels.Count;
                blockCounts[i] = before[i].blocks.Count;
            }
            int warningCount = Log.Warnings.Count;
            List<CodeInstruction> output = Transform(input);
            Check(output.Count == before.Length, "Fail-safe changed instruction count");
            for (int i = 0; i < before.Length; i++) {
                Check(ReferenceEquals(output[i], before[i]), "Fail-safe changed original instruction");
                Check(output[i].opcode == opcodes[i] && Equals(output[i].operand, operands[i]),
                    "Fail-safe changed opcode or operand");
                Check(output[i].labels.Count == labelCounts[i]
                    && output[i].blocks.Count == blockCounts[i],
                    "Fail-safe changed control-flow metadata");
            }
            Check(Log.Warnings.Count == warningCount + 1, "Fail-safe warning count");
            Check(IndexOfCall(output, SecondaryHelperMethod) < 0, "Fail-safe inserted secondary helper");
        }

        public static int Run() {
            var start = new CodeInstruction(OpCodes.Nop);
            var originalThingIcon = new CodeInstruction(OpCodes.Call, ThingIconMethod);
            originalThingIcon.labels.Add(default(Label));
            var end = new CodeInstruction(OpCodes.Ret);
            List<CodeInstruction> output = Transform(start, originalThingIcon, end);
            int helperIndex = IndexOfCall(output, SecondaryHelperMethod);
            int vanillaIndex = IndexOfCall(output, ThingIconMethod);
            Check(helperIndex >= 0, "Missing secondary helper call");
            Check(vanillaIndex > helperIndex, "Secondary helper must precede vanilla draw");
            Check(ReferenceEquals(output[vanillaIndex], originalThingIcon), "Vanilla call instruction was replaced");
            Check(CountCalls(output, ThingIconMethod) == 1, "Vanilla ThingIcon call count");
            Check(CountCalls(output, SecondaryHelperMethod) == 1, "Secondary helper call count");
            Check(ReferenceEquals(output[0], start) && ReferenceEquals(output[output.Count - 1], end), "Sentinel order changed");
            Check(originalThingIcon.labels.Count == 0
                && output[helperIndex - 14].labels.Count == 1,
                "Branch label must move to the first injected instruction");
            CheckLocalSequence(
                output,
                helperIndex - 14,
                OpCodes.Stloc,
                new[] { typeof(bool), typeof(float), typeof(bool), typeof(Rot4?),
                    typeof(float), typeof(Thing), typeof(Rect) },
                "Argument stores");
            Type[] forwardArgumentTypes = {
                typeof(Rect), typeof(Thing), typeof(float), typeof(Rot4?),
                typeof(bool), typeof(float), typeof(bool)
            };
            CheckLocalSequence(output, helperIndex - 7, OpCodes.Ldloc, forwardArgumentTypes, "Helper loads");
            CheckLocalSequence(output, vanillaIndex - 7, OpCodes.Ldloc, forwardArgumentTypes, "Vanilla reloads");
            Check(Log.Warnings.Count == 0, "Normal transform emitted a warning");

            CheckFailSafe(new CodeInstruction(OpCodes.Nop), new CodeInstruction(OpCodes.Ret));
            CheckFailSafe(
                new CodeInstruction(OpCodes.Call, ThingIconMethod),
                new CodeInstruction(OpCodes.Call, ThingIconMethod),
                new CodeInstruction(OpCodes.Ret));
            var blockedCall = new CodeInstruction(OpCodes.Call, ThingIconMethod);
            blockedCall.blocks.Add(new ExceptionBlock());
            CheckFailSafe(blockedCall, new CodeInstruction(OpCodes.Ret));

            var pawn = new Pawn();
            var primary = new ThingWithComps();
            var secondary = new ThingWithComps();
            pawn.equipment.Primary = primary;
            RimKataVisualUtility.Owner = pawn;
            RimKataVisualUtility.Primary = primary;
            RimKataVisualUtility.Secondary = secondary;
            RimKataVisualUtility.HasLoadout = true;
            RimKataVisualUtility.SecondaryUsable = true;
            Widgets.Reset();
            GUI.matrix = new Matrix4x4 { marker = 17 };
            Patch_ColonistBar_RimKataDualWeaponIcons.DrawSecondaryWeaponIcon(
                new Rect(1f, 2f, 3f, 4f), primary, 0.5f, new Rot4(), true, 0.75f, true);
            Check(Widgets.DrawnThings.Count == 1 && ReferenceEquals(Widgets.DrawnThings[0], secondary),
                "Helper must draw only the secondary weapon");
            Check(Math.Abs(UI.LastAngle - 45f) < 0.001f, "Secondary angle");
            Check(Widgets.LastRect.x == 1f && Widgets.LastRect.y == 2f
                && Widgets.LastRect.width == 3f && Widgets.LastRect.height == 4f,
                "Secondary rect forwarding");
            Check(Widgets.LastAlpha == 0.5f && Widgets.LastRot.HasValue
                && Widgets.LastStackOfOne && Widgets.LastScale == 0.75f
                && Widgets.LastGrayscale,
                "Secondary draw argument forwarding");
            Check(GUI.matrix.marker == 17, "GUI matrix restore after successful draw");

            Widgets.Reset();
            RimKataVisualUtility.SecondaryUsable = false;
            Patch_ColonistBar_RimKataDualWeaponIcons.DrawSecondaryWeaponIcon(
                new Rect(), primary, 1f, null, true, 1f, false);
            Check(Widgets.DrawnThings.Count == 0, "Unusable secondary should not draw");

            Widgets.Reset();
            RimKataVisualUtility.SecondaryUsable = true;
            Widgets.ThrowOnDraw = true;
            GUI.matrix = new Matrix4x4 { marker = 23 };
            bool sameException = false;
            try {
                Patch_ColonistBar_RimKataDualWeaponIcons.DrawSecondaryWeaponIcon(
                    new Rect(), primary, 1f, null, true, 1f, false);
            }
            catch (InvalidOperationException error) {
                sameException = error.Message == "draw failed";
            }
            Check(sameException, "Draw exception propagation");
            Check(GUI.matrix.marker == 23, "GUI matrix restore after failed draw");
            return checks;
        }
    }
}
"@

Add-Type -TypeDefinition $rkHarness -Language CSharp
$rkPassed = [KRWF.RimKata.ColonistBarWeaponIconChecks]::Run()
"PASS: $rkPassed assertions; production ColonistBar patch with synthetic IL/UI stubs. Not an in-game GUI or performance test."
