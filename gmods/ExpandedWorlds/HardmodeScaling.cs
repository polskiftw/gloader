#if GLOADER_CLIENT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Terraria;

/// <summary>
/// Terraria's Hardmode Hallow/evil runner historically computes its round
/// conversion thickness as:
///
///   Next(200, 250) * (Main.maxTilesX / 4200)
///
/// That is valid for vanilla because width and height grow together. Expanded
/// Worlds keeps Large height while adding horizontal acreage, so width is no
/// longer a valid proxy for an axis-neutral radius/diameter.
///
/// The stripe start X positions remain width-relative and GERunner still travels
/// through the real world height. Only the round linear thickness is generalized
/// to the area-equivalent linear scale sqrt(area / SmallArea). This collapses
/// exactly to vanilla when both axes scale together.
/// </summary>
[HarmonyPatch(typeof(WorldGen), nameof(WorldGen.GERunner))]
internal static class ExpandedWorldHardmodeRunnerScalePatch
{
    private static readonly MethodInfo AdjustStrengthMethod =
        AccessTools.Method(typeof(ExpandedWorldHardmodeRunnerScalePatch), nameof(AdjustStrength))
        ?? throw new MissingMethodException(
            typeof(ExpandedWorldHardmodeRunnerScalePatch).FullName,
            nameof(AdjustStrength));

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions,
        MethodBase __originalMethod)
    {
        var code = instructions.ToList();
        int replacementCount = 0;

        for (int i = 0; i < code.Count; i++)
        {
            int local = GetStoredLocalIndex(code[i]);
            if (local < 0)
                continue;

            int start = Math.Max(0, i - 24);
            bool saw200 = false;
            bool saw250 = false;
            bool saw4200 = false;
            bool sawMul = false;

            for (int j = start; j < i; j++)
            {
                saw200 |= IsIntConstant(code[j], 200);
                saw250 |= IsIntConstant(code[j], 250);
                saw4200 |= IsIntConstant(code[j], ExpandedWorldMath.SmallWidth);
                sawMul |= code[j].opcode == OpCodes.Mul;
            }

            if (!saw200 || !saw250 || !saw4200 || !sawMul)
                continue;

            code.Insert(i, new CodeInstruction(OpCodes.Call, AdjustStrengthMethod));
            replacementCount++;
            i++;
        }

        if (replacementCount != 1)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] GERunner source shape changed in " +
                (__originalMethod?.DeclaringType?.FullName ?? "WorldGen") + "." +
                (__originalMethod?.Name ?? "GERunner") +
                ": expected exactly one 200..250 * (width/4200) strength assignment, found " +
                replacementCount + ". Refusing to guess against this Terraria build.");
        }

        return code;
    }

    private static int AdjustStrength(int vanillaStrength)
    {
        if (!WorldGen.IsGeneratingHardMode ||
            Main.maxTilesX <= ExpandedWorldMath.LargeWidth ||
            Main.maxTilesY != ExpandedWorldMath.LargeHeight)
        {
            return vanillaStrength;
        }

        int widthTier = Main.maxTilesX / ExpandedWorldMath.SmallWidth;
        if (widthTier <= 0 || vanillaStrength % widthTier != 0)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] Could not recover GERunner's source random base from strength " +
                vanillaStrength + " at width tier " + widthTier + ".");
        }

        int randomBase = vanillaStrength / widthTier;
        return ExpandedWorldMath.HardmodeRunnerStrength(
            randomBase,
            Main.maxTilesX,
            Main.maxTilesY);
    }

    private static int GetStoredLocalIndex(CodeInstruction instruction)
    {
        if (instruction.opcode == OpCodes.Stloc_0) return 0;
        if (instruction.opcode == OpCodes.Stloc_1) return 1;
        if (instruction.opcode == OpCodes.Stloc_2) return 2;
        if (instruction.opcode == OpCodes.Stloc_3) return 3;
        if (instruction.opcode != OpCodes.Stloc && instruction.opcode != OpCodes.Stloc_S)
            return -1;
        return GetOperandLocalIndex(instruction.operand);
    }

    private static int GetOperandLocalIndex(object operand)
    {
        if (operand is LocalBuilder builder) return builder.LocalIndex;
        if (operand is byte b) return b;
        if (operand is sbyte sb) return sb;
        if (operand is short s) return s;
        if (operand is int i) return i;
        return -1;
    }

    private static bool IsIntConstant(CodeInstruction instruction, int expected)
    {
        if (instruction.opcode == OpCodes.Ldc_I4 && instruction.operand is int)
            return (int)instruction.operand == expected;
        if (instruction.opcode == OpCodes.Ldc_I4_S && instruction.operand is sbyte)
            return (sbyte)instruction.operand == expected;
        if (expected == 0 && instruction.opcode == OpCodes.Ldc_I4_0) return true;
        if (expected == 1 && instruction.opcode == OpCodes.Ldc_I4_1) return true;
        if (expected == 2 && instruction.opcode == OpCodes.Ldc_I4_2) return true;
        if (expected == 3 && instruction.opcode == OpCodes.Ldc_I4_3) return true;
        if (expected == 4 && instruction.opcode == OpCodes.Ldc_I4_4) return true;
        if (expected == 5 && instruction.opcode == OpCodes.Ldc_I4_5) return true;
        if (expected == 6 && instruction.opcode == OpCodes.Ldc_I4_6) return true;
        if (expected == 7 && instruction.opcode == OpCodes.Ldc_I4_7) return true;
        if (expected == 8 && instruction.opcode == OpCodes.Ldc_I4_8) return true;
        return false;
    }
}
#endif
