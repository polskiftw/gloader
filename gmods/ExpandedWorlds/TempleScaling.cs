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
/// 4,200-tile width tiers used by Expanded Worlds. Older audited source used a
/// local Rectangle[40] room buffer, which is only large enough for vanilla
/// Large's 20-31 room range.
///
/// This transpiler changes only that scratch-array length. It deliberately does
/// not require the current Terraria build to still use the literal 40: if
/// Re-Logic has independently increased the one constant-sized Rectangle[] room
/// buffer, that larger vanilla value is preserved. We only fail when the method
/// no longer has exactly one unambiguous constant-sized Rectangle[] allocation.
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

            if (!TryReadIntegerConstant(code[i - 1], out int vanillaCapacity) || vanillaCapacity <= 0)
                continue;

            // Keep the vanilla capacity on the stack and transform it at runtime
            // only when the expanded width actually requires more room records.
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
                ": expected exactly one constant-sized Rectangle[] room scratch allocation, found " +
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

    private static bool TryReadIntegerConstant(CodeInstruction instruction, out int value)
    {
        if (instruction.opcode == OpCodes.Ldc_I4 && instruction.operand is int)
        {
            value = (int)instruction.operand;
            return true;
        }

        if (instruction.opcode == OpCodes.Ldc_I4_S && instruction.operand is sbyte)
        {
            value = (sbyte)instruction.operand;
            return true;
        }

        if (instruction.opcode == OpCodes.Ldc_I4_M1) { value = -1; return true; }
        if (instruction.opcode == OpCodes.Ldc_I4_0) { value = 0; return true; }
        if (instruction.opcode == OpCodes.Ldc_I4_1) { value = 1; return true; }
        if (instruction.opcode == OpCodes.Ldc_I4_2) { value = 2; return true; }
        if (instruction.opcode == OpCodes.Ldc_I4_3) { value = 3; return true; }
        if (instruction.opcode == OpCodes.Ldc_I4_4) { value = 4; return true; }
        if (instruction.opcode == OpCodes.Ldc_I4_5) { value = 5; return true; }
        if (instruction.opcode == OpCodes.Ldc_I4_6) { value = 6; return true; }
        if (instruction.opcode == OpCodes.Ldc_I4_7) { value = 7; return true; }
        if (instruction.opcode == OpCodes.Ldc_I4_8) { value = 8; return true; }

        value = 0;
        return false;
    }
}
#endif
