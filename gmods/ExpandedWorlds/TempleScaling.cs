#if GLOADER_CLIENT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Terraria;

/// <summary>
/// makeTemple's room-count formula already continues cleanly through the exact
/// 4,200-tile width tiers used by Expanded Worlds. The historical/current method
/// shape, however, allocates a local Rectangle[40], which is only large enough
/// for vanilla Large's 20-31 room range.
///
/// This transpiler changes only that scratch-array length. The room-count roll,
/// room geometry, RNG sequence, placement, Temple count and special-seed logic
/// remain vanilla.
/// </summary>
[HarmonyPatch]
internal static class ExpandedWorldTempleScratchCapacityPatch
{
    private static readonly MethodInfo CapacityMethod =
        AccessTools.Method(typeof(ExpandedWorldTempleScratchCapacityPatch), nameof(CapacityFromVanilla))
        ?? throw new MissingMethodException(
            typeof(ExpandedWorldTempleScratchCapacityPatch).FullName,
            nameof(CapacityFromVanilla));

    private static MethodBase TargetMethod()
    {
        MethodBase method = AccessTools.Method(
            typeof(WorldGen),
            "makeTemple",
            new[] { typeof(int), typeof(int) });

        if (method == null)
            throw new MissingMethodException(typeof(WorldGen).FullName, "makeTemple(int,int)");

        return method;
    }

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions,
        MethodBase __originalMethod)
    {
        var code = instructions.ToList();
        int replacements = 0;

        for (int i = 1; i < code.Count; i++)
        {
            if (code[i].opcode != OpCodes.Newarr || !Equals(code[i].operand, typeof(Rectangle)))
                continue;

            if (!LoadsIntegerConstant(code[i - 1], 40))
                continue;

            // Preserve vanilla's literal 40 on the stack, then transform that
            // allocation length at runtime only when an expanded generation is
            // armed. Non-expanded worlds receive the original 40 unchanged.
            code.Insert(i, new CodeInstruction(OpCodes.Call, CapacityMethod));
            replacements++;
            i++;
        }

        if (replacements != 1)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] makeTemple source shape changed in " +
                (__originalMethod?.DeclaringType?.FullName ?? "WorldGen") + "." +
                (__originalMethod?.Name ?? "makeTemple") +
                ": expected exactly one Rectangle[40] room scratch allocation, found " +
                replacements + ". Refusing to guess against this Terraria build.");
        }

        return code;
    }

    private static int CapacityFromVanilla(int vanillaCapacity)
    {
        if (!ExpandedWorldState.GenerationArmed || Main.maxTilesX <= ExpandedWorldMath.LargeWidth)
            return vanillaCapacity;

        return Math.Max(
            vanillaCapacity,
            ExpandedWorldCapacityMath.JungleTempleRoomScratchCapacity(Main.maxTilesX));
    }

    private static bool LoadsIntegerConstant(CodeInstruction instruction, int expected)
    {
        if (instruction.opcode == OpCodes.Ldc_I4 && instruction.operand is int)
            return (int)instruction.operand == expected;
        if (instruction.opcode == OpCodes.Ldc_I4_S && instruction.operand is sbyte)
            return (sbyte)instruction.operand == expected;

        if (expected == -1 && instruction.opcode == OpCodes.Ldc_I4_M1) return true;
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
