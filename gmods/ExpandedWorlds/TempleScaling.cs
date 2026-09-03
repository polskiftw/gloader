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
/// Validates Jungle Temple room scratch storage without changing current
/// Terraria 1.4.5 generation behavior.
///
/// Terraria 1.4.5.8 sizes both Temple room arrays dynamically from the rolled
/// room count (numRooms + 10), including Drunk/For-the-Worthy/Remix seed
/// multipliers. That layout needs no Expanded Worlds capacity patch and must be
/// left untouched.
///
/// Older audited Terraria builds used one constant-sized Rectangle[] room
/// buffer. Keep the source-derived resize as a legacy compatibility fallback,
/// but only when the IL still exposes that exact unambiguous fixed allocation.
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
        var candidates = typeof(WorldGen)
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(method => method.Name == "makeTemple")
            .Where(IsSupportedMakeTempleSignature)
            .Cast<MethodBase>()
            .ToList();

        if (candidates.Count != 1)
        {
            throw new MissingMethodException(
                typeof(WorldGen).FullName,
                "makeTemple(int,int[,GenerationProgress]) - expected exactly one audited overload, found " +
                candidates.Count);
        }

        return candidates[0];
    }

    private static bool IsSupportedMakeTempleSignature(MethodInfo method)
    {
        ParameterInfo[] parameters = method.GetParameters();
        if (parameters.Length != 2 && parameters.Length != 3)
            return false;
        if (parameters[0].ParameterType != typeof(int) || parameters[1].ParameterType != typeof(int))
            return false;

        if (parameters.Length == 3)
        {
            return string.Equals(
                parameters[2].ParameterType.FullName,
                "Terraria.WorldBuilding.GenerationProgress",
                StringComparison.Ordinal);
        }

        return true;
    }

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions,
        MethodBase __originalMethod)
    {
        var code = instructions.ToList();
        int rectangleAllocations = 0;
        int legacyFixedAllocations = 0;
        int modernDynamicAllocations = 0;

        for (int i = 1; i < code.Count; i++)
        {
            if (code[i].opcode != OpCodes.Newarr || !Equals(code[i].operand, typeof(Rectangle)))
                continue;

            rectangleAllocations++;

            if (TryReadIntegerConstant(code[i - 1], out int vanillaCapacity) && vanillaCapacity > 0)
            {
                // Legacy layout: preserve vanilla's constant on the stack and
                // enlarge it only for an armed Expanded Worlds generation.
                code.Insert(i, new CodeInstruction(OpCodes.Call, CapacityMethod));
                legacyFixedAllocations++;
                i++;
                continue;
            }

            if (IsAuditedModernDynamicAllocation(code, i))
            {
                // Terraria 1.4.5.8: roomRects = new Rectangle[numRooms + 10].
                // Dynamic storage already follows every seed multiplier and must
                // not be rewritten.
                modernDynamicAllocations++;
                continue;
            }

            throw new InvalidOperationException(
                "[Expanded Worlds] makeTemple Rectangle[] allocation no longer matches either the " +
                "audited legacy fixed-capacity shape or Terraria 1.4.5.8's numRooms+10 dynamic shape. " +
                "Refusing to guess against this Terraria build.");
        }

        if (rectangleAllocations != 1 || legacyFixedAllocations + modernDynamicAllocations != 1)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] makeTemple source shape changed in " +
                (__originalMethod?.DeclaringType?.FullName ?? "WorldGen") + "." +
                (__originalMethod?.Name ?? "makeTemple") +
                ": expected exactly one audited Rectangle[] room allocation, found " +
                rectangleAllocations + ". Refusing to guess against this Terraria build.");
        }

        if (modernDynamicAllocations == 1)
        {
            Console.WriteLine(
                "[Expanded Worlds] Terraria 1.4.5-style Temple room storage is dynamic; " +
                "no Temple scratch-capacity resize is required.");
        }

        return code;
    }

    private static bool IsAuditedModernDynamicAllocation(List<CodeInstruction> code, int newarrIndex)
    {
        // C# `new Rectangle[numRooms + 10]` compiles as:
        //   ldloc.* numRooms
        //   ldc.i4.s 10
        //   add
        //   newarr Rectangle
        // We intentionally do not care which local contains numRooms, but do
        // require the +10 slack and arithmetic shape that the 1.4.5.8 source
        // audit enforces independently.
        if (newarrIndex < 3 || code[newarrIndex - 1].opcode != OpCodes.Add)
            return false;

        return TryReadIntegerConstant(code[newarrIndex - 2], out int slack) && slack == 10;
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
