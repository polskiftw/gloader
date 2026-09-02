using System;

#if GLOADER
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Terraria;
#endif

/// <summary>
/// Source-derived scale for one feature's axis-neutral linear geometry.
///
/// Vanilla Small/Medium/Large grow width and height together closely enough that
/// WorldGen can use maxTilesX / 4200 as a general linear world scale. Expanded
/// Worlds deliberately stops growing height after Large. For one feature's
/// radius/lifetime/body size, continuing that width proxy would make the feature
/// grow vertically merely because more horizontal world exists.
/// </summary>
internal static class ExpandedWorldFeatureGeometryMath
{
    public static double AxisNeutralLinearScale(int width, int height)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));

        // Preserve Terraria's exact source scalar throughout the vanilla range.
        if (width <= ExpandedWorldMath.LargeWidth)
            return width / (double)ExpandedWorldMath.SmallWidth;

        // Large's source scale is exactly 2. Continue from there by the square
        // root of physical area growth. XL=sqrt(6), Huge=sqrt(8).
        return ExpandedWorldMath.IsotropicLinearScale(width, height);
    }
}

#if GLOADER
internal static class ExpandedWorldFeatureGeometryPatchUtil
{
    internal static readonly FieldInfo MaxTilesXField =
        AccessTools.Field(typeof(Main), nameof(Main.maxTilesX))
        ?? throw new MissingFieldException(typeof(Main).FullName, nameof(Main.maxTilesX));

    internal static readonly MethodInfo AdjustScaleMethod =
        AccessTools.Method(typeof(ExpandedWorldFeatureGeometryPatchUtil), nameof(CurrentLinearScale))
        ?? throw new MissingMethodException(
            typeof(ExpandedWorldFeatureGeometryPatchUtil).FullName,
            nameof(CurrentLinearScale));

    internal static double CurrentLinearScale()
    {
        return ExpandedWorldFeatureGeometryMath.AxisNeutralLinearScale(
            Main.maxTilesX,
            Main.maxTilesY);
    }

    internal static IEnumerable<CodeInstruction> ReplaceWidthScale(
        IEnumerable<CodeInstruction> instructions,
        MethodBase original,
        string featureName)
    {
        var code = instructions.ToList();
        int patched = 0;

        // Audited 1.4.5.8 source shape:
        //   (double)Main.maxTilesX / 4200.0
        // Replace only that complete scalar expression. Any seed multiplier
        // immediately after it stays untouched and therefore stays authoritative.
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

            var call = new CodeInstruction(OpCodes.Call, AdjustScaleMethod);
            call.labels.AddRange(code[i].labels);
            call.blocks.AddRange(code[i].blocks);
            code[i].labels.Clear();
            code[i].blocks.Clear();
            code[i] = call;

            // Keep instruction slots so any compiler-generated labels/EH markers
            // attached inside the original expression remain valid.
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
                "[Expanded Worlds] " + featureName + " linear-scale source shape changed in " +
                (original?.DeclaringType?.FullName ?? "WorldGen") + "." +
                (original?.Name ?? "<unknown>") +
                ": expected exactly one maxTilesX/4200.0 scalar, found " + patched +
                ". Refusing to guess against this Terraria build.");
        }

        return code;
    }

    private static bool IsDoubleConstant(CodeInstruction instruction, double expected)
    {
        return instruction.opcode == OpCodes.Ldc_R8 &&
               instruction.operand is double &&
               (double)instruction.operand == expected;
    }
}

/// <summary>
/// Neon moss biome count remains width-derived in the generation pass. This
/// patch changes only one generated biome's radius and lifetime scalar. For the
/// Worthy's downstream x1.5 multiplier remains vanilla-owned.
/// </summary>
[HarmonyPatch]
internal static class ExpandedWorldNeonMossGeometryPatch
{
    private static MethodBase TargetMethod()
    {
        MethodBase method = AccessTools.Method(
            typeof(WorldGen),
            "neonMossBiome",
            new[] { typeof(int), typeof(int), typeof(int) });
        if (method == null)
            throw new MissingMethodException(typeof(WorldGen).FullName, "neonMossBiome(int,int,int)");
        return method;
    }

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions,
        MethodBase __originalMethod)
    {
        return ExpandedWorldFeatureGeometryPatchUtil.ReplaceWidthScale(
            instructions,
            __originalMethod,
            "neonMossBiome");
    }
}

/// <summary>
/// Glowing Mushroom biome count remains width-derived in its generation pass.
/// ShroomPatch itself gets only the area-equivalent linear scale for one patch's
/// radius and lifetime. For the Worthy's downstream x1.5 multiplier (except in
/// Remix) remains vanilla-owned.
/// </summary>
[HarmonyPatch]
internal static class ExpandedWorldShroomPatchGeometryPatch
{
    private static MethodBase TargetMethod()
    {
        MethodBase method = AccessTools.Method(
            typeof(WorldGen),
            "ShroomPatch",
            new[] { typeof(int), typeof(int) });
        if (method == null)
            throw new MissingMethodException(typeof(WorldGen).FullName, "ShroomPatch(int,int)");
        return method;
    }

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions,
        MethodBase __originalMethod)
    {
        return ExpandedWorldFeatureGeometryPatchUtil.ReplaceWidthScale(
            instructions,
            __originalMethod,
            "ShroomPatch");
    }
}

/// <summary>
/// PlantAlch uses 15 * (maxTilesX/4200) as both the X and Y radius of its nearby
/// herb scan. Generation attempts remain width-derived, but one exclusion body's
/// two-dimensional radius must not double vertically just because Huge is wider.
///
/// PlantAlch is also called by the live WorldGen.UpdateWorld loop. In multiplayer
/// that loop is server-authoritative, so this patch intentionally compiles for
/// both GLOADER_CLIENT and GLOADER_SERVER. Vanilla dimensions still reproduce
/// the exact original scalar.
/// </summary>
[HarmonyPatch]
internal static class ExpandedWorldAlchemyHerbSpacingPatch
{
    private static MethodBase TargetMethod()
    {
        MethodBase method = AccessTools.Method(typeof(WorldGen), "PlantAlch", Type.EmptyTypes);
        if (method == null)
            throw new MissingMethodException(typeof(WorldGen).FullName, "PlantAlch");
        return method;
    }

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions,
        MethodBase __originalMethod)
    {
        return ExpandedWorldFeatureGeometryPatchUtil.ReplaceWidthScale(
            instructions,
            __originalMethod,
            "PlantAlch herb-spacing radius");
    }
}
#endif
