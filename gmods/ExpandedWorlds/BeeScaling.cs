using System;

#if GLOADER_CLIENT
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Terraria;
#endif

/// <summary>
/// Aspect-ratio continuation for the Drunk-world Hive tunnel body.
///
/// Terraria 1.4.5.8 computes one tunnel's linear multiplier as:
///   ((maxTilesX / 4200.0) + 1.0) / 2.0
/// and applies that same multiplier to both tunnel radius and lifetime. That is
/// a valid general world-scale proxy while vanilla width and height grow
/// together. Expanded Worlds intentionally stops growing height after Large, so
/// continuing the width-only scalar would make each Hive grow vertically merely
/// because more horizontal world exists.
/// </summary>
internal static class ExpandedWorldBeeGeometryMath
{
    public static double DrunkHiveTunnelScale(int width, int height)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));

        // Preserve the exact Terraria source result for every vanilla width.
        if (width <= ExpandedWorldMath.LargeWidth)
        {
            double sourceWidthScale = width / (double)ExpandedWorldMath.SmallWidth;
            return (sourceWidthScale + 1d) / 2d;
        }

        // Large's exact source multiplier is 1.5. Continue one feature's
        // axis-neutral linear geometry by sqrt(physical area relative to Large).
        const double vanillaLargeScale = 1.5d;
        return vanillaLargeScale * Math.Sqrt(
            ExpandedWorldMath.AreaRelativeToLarge(width, height));
    }

    /// <summary>
    /// Value fed into Terraria's untouched `(value + 1) / 2` expression so the
    /// final local multiplier becomes DrunkHiveTunnelScale. Keeping the original
    /// arithmetic downstream means the transpiler only replaces the one bad
    /// width proxy instead of reconstructing Hive logic.
    /// </summary>
    public static double DrunkHiveSourceWidthProxy(int width, int height)
    {
        return 2d * DrunkHiveTunnelScale(width, height) - 1d;
    }
}

#if GLOADER_CLIENT
/// <summary>
/// Patches only HiveBiome.CreateHiveTunnel's `maxTilesX / 4200.0` source proxy.
/// The Drunk branch, RNG, larva creation, Hive count, honey geometry, Remix
/// branch, and all seed-combination control flow remain Terraria-owned.
/// </summary>
[HarmonyPatch]
internal static class ExpandedWorldDrunkHiveGeometryPatch
{
    private const string HiveBiomeTypeName = "Terraria.GameContent.Biomes.HiveBiome";

    private static readonly FieldInfo MaxTilesXField =
        AccessTools.Field(typeof(Main), nameof(Main.maxTilesX))
        ?? throw new MissingFieldException(typeof(Main).FullName, nameof(Main.maxTilesX));

    private static readonly MethodInfo AdjustProxyMethod =
        AccessTools.Method(typeof(ExpandedWorldDrunkHiveGeometryPatch), nameof(CurrentSourceWidthProxy))
        ?? throw new MissingMethodException(
            typeof(ExpandedWorldDrunkHiveGeometryPatch).FullName,
            nameof(CurrentSourceWidthProxy));

    private static MethodBase TargetMethod()
    {
        Type hiveBiome = AccessTools.TypeByName(HiveBiomeTypeName)
            ?? throw new TypeLoadException("[Expanded Worlds] Could not find " + HiveBiomeTypeName + ".");

        var matches = hiveBiome
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(method => method.Name == "CreateHiveTunnel")
            .Where(method =>
            {
                ParameterInfo[] parameters = method.GetParameters();
                return parameters.Length == 3 &&
                       parameters[0].ParameterType == typeof(int) &&
                       parameters[1].ParameterType == typeof(int);
            })
            .Cast<MethodBase>()
            .ToList();

        if (matches.Count != 1)
        {
            throw new MissingMethodException(
                HiveBiomeTypeName,
                "CreateHiveTunnel(int,int,*) - expected one audited overload, found " + matches.Count);
        }

        return matches[0];
    }

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions,
        MethodBase __originalMethod)
    {
        var code = instructions.ToList();
        int patched = 0;

        // Exact 1.4.5.8 decompile inside the Drunk branch:
        //   double num3 = (double)Main.maxTilesX / 4200.0;
        //   num3 = (num3 + 1.0) / 2.0;
        //   num *= num3;
        //   num2 *= num3;
        //
        // Replace only the first expression. Terraria's +1,/2 and subsequent
        // seed behavior remain verbatim.
        for (int i = 0; i + 3 < code.Count; i++)
        {
            if (code[i].opcode != OpCodes.Ldsfld ||
                !Equals(code[i].operand, MaxTilesXField) ||
                code[i + 1].opcode != OpCodes.Conv_R8 ||
                !IsDoubleConstant(code[i + 2], 4200d) ||
                code[i + 3].opcode != OpCodes.Div)
            {
                continue;
            }

            var call = new CodeInstruction(OpCodes.Call, AdjustProxyMethod);
            call.labels.AddRange(code[i].labels);
            call.blocks.AddRange(code[i].blocks);
            code[i].labels.Clear();
            code[i].blocks.Clear();
            code[i] = call;

            for (int j = i + 1; j <= i + 3; j++)
            {
                code[j].opcode = OpCodes.Nop;
                code[j].operand = null;
            }

            patched++;
            i += 3;
        }

        if (patched != 1)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] Drunk Hive tunnel source shape changed in " +
                (__originalMethod?.DeclaringType?.FullName ?? HiveBiomeTypeName) + "." +
                (__originalMethod?.Name ?? "CreateHiveTunnel") +
                ": expected exactly one maxTilesX/4200.0 source proxy, found " + patched +
                ". Refusing to guess against this Terraria build.");
        }

        return code;
    }

    private static double CurrentSourceWidthProxy()
    {
        if (!ExpandedWorldState.GenerationArmed || Main.maxTilesX <= ExpandedWorldMath.LargeWidth)
            return Main.maxTilesX / (double)ExpandedWorldMath.SmallWidth;

        return ExpandedWorldBeeGeometryMath.DrunkHiveSourceWidthProxy(
            Main.maxTilesX,
            Main.maxTilesY);
    }

    private static bool IsDoubleConstant(CodeInstruction instruction, double expected)
    {
        return instruction.opcode == OpCodes.Ldc_R8 &&
               instruction.operand is double &&
               (double)instruction.operand == expected;
    }
}
#endif
