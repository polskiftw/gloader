#if GLOADER_CLIENT
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Terraria;
using Terraria.WorldBuilding;

/// <summary>
/// Terraria's Floating Lake count is a discrete 1 / 2 / 3 Small/Medium/Large
/// rule rather than a continuously width-scaled WorldGenRange. For expanded
/// widths, generalize that exact sequence to floor(width / 2800):
///
///   4200 -> 1
///   6400 -> 2
///   8400 -> 3
///  12000 -> 4
///  16800 -> 6
///
/// We patch the assignment itself rather than placing extra lakes after worldgen.
/// This keeps vanilla's own placement loop, collision/spacing rules and seed
/// transformations in charge.
/// </summary>
[HarmonyPatch]
internal static class ExpandedWorldSkyLakeScalingPatch
{
    private static readonly FieldInfo SkyLakesField =
        AccessTools.Field(typeof(GenVars), "skyLakes")
        ?? throw new MissingFieldException(typeof(GenVars).FullName, "skyLakes");

    private static readonly MethodInfo AdjustMethod =
        AccessTools.Method(typeof(ExpandedWorldSkyLakeScalingPatch), nameof(AdjustAssignedSkyLakeCount))
        ?? throw new MissingMethodException(nameof(ExpandedWorldSkyLakeScalingPatch), nameof(AdjustAssignedSkyLakeCount));

    private static IEnumerable<MethodBase> TargetMethods()
    {
        bool found = false;

        foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(WorldGen)))
        {
            if (!method.IsStatic || !WritesSkyLakes(method))
                continue;

            found = true;
            yield return method;
        }

        if (!found)
        {
            throw new MissingMethodException(
                "Expanded Worlds could not find the vanilla WorldGen method that assigns GenVars.skyLakes. " +
                "The installed Terraria build changed and the Floating Lake scaling patch must be audited.");
        }
    }

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Stsfld && Equals(instruction.operand, SkyLakesField))
            {
                // Stack before stsfld: [..., proposedInt]
                // Transform only that proposed value, then let vanilla perform its normal store.
                yield return new CodeInstruction(OpCodes.Call, AdjustMethod);
            }

            yield return instruction;
        }
    }

    private static int AdjustAssignedSkyLakeCount(int vanillaValue)
    {
        if (!ExpandedWorldState.GenerationArmed || Main.maxTilesX <= ExpandedWorldMath.LargeWidth)
            return vanillaValue;

        // 3 is vanilla's normal Large-tier cap. A different value is treated as
        // deliberate vanilla/seed behavior and wins over Expanded Worlds.
        if (vanillaValue != 3)
            return vanillaValue;

        int target = ExpandedWorldMath.FloatingLakes(Main.maxTilesX);
        if (target != vanillaValue)
        {
            Console.WriteLine(
                "[Expanded Worlds] Floating Lakes: generalized vanilla " + vanillaValue +
                " -> " + target + " for width " + Main.maxTilesX + ".");
        }

        return target;
    }

    private static bool WritesSkyLakes(MethodInfo method)
    {
        MethodBody body;
        try
        {
            body = method.GetMethodBody();
        }
        catch
        {
            return false;
        }

        byte[] il = body?.GetILAsByteArray();
        if (il == null || il.Length < 5)
            return false;

        int token = SkyLakesField.MetadataToken;
        byte b0 = (byte)token;
        byte b1 = (byte)(token >> 8);
        byte b2 = (byte)(token >> 16);
        byte b3 = (byte)(token >> 24);

        // stsfld is the single-byte IL opcode 0x80 followed by a 4-byte metadata token.
        for (int i = 0; i <= il.Length - 5; i++)
        {
            if (il[i] == 0x80 &&
                il[i + 1] == b0 &&
                il[i + 2] == b1 &&
                il[i + 3] == b2 &&
                il[i + 4] == b3)
            {
                return true;
            }
        }

        return false;
    }
}
#endif
