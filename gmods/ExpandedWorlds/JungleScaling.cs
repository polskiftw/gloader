#if GLOADER_CLIENT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Terraria;
using Terraria.GameContent.Biomes;
using Terraria.WorldBuilding;

/// <summary>
/// Axis-aware continuation of Terraria's JunglePass for worlds wider than Large.
///
/// Exact Terraria 1.4.5.8 retail source stores JunglePass._worldScale as double.
/// Keep that type end-to-end: using float here would both make reflection reads
/// fail on a boxed Double and make the ApplyPass transpiler emit an invalid call
/// signature at the double stfld use site.
///
/// Vanilla JunglePass carries one private _worldScale through several different
/// dimensional jobs because vanilla width and height grow together. Expanded
/// Worlds separates those jobs:
///
///   X displacement / horizontal margins -> width relative to Large
///   Y displacement                       -> height relative to Large
///   axis-neutral linear scalar           -> sqrt(area relative to Large)
///   round TileRunner body strength       -> sqrt(area relative to Large)
///
/// The sqrt(area) rule is the area-equivalent linear scale: it is symmetric in X
/// and Y, and when both axes grow by the same factor s it collapses exactly to s.
/// That makes it the mathematically neutral continuation for a vanilla linear
/// scalar whose source use does not identify one physical axis.
///
/// Repeated scalar counts use that same isotropic factor. Where vanilla nests two
/// scale-driven counts, their product therefore becomes
/// sqrt(horizontal*vertical)^2 = horizontal*vertical, i.e. exact area scaling.
///
/// The original pass still runs. We only replace the overloaded scale where its
/// dimensional meaning becomes ambiguous. Vanilla Small/Medium/Large are never
/// touched, and seed-specific JunglePass behavior remains authoritative.
/// </summary>
internal static class ExpandedWorldJungleScales
{
    internal static readonly FieldInfo WorldScaleField = RequireWorldScaleField();

    public static bool Active =>
        ExpandedWorldState.GenerationArmed && Main.maxTilesX > ExpandedWorldMath.LargeWidth;

    public static double ReadIsotropicScale(JunglePass instance)
    {
        object value = WorldScaleField.GetValue(instance);
        if (!(value is double))
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] JunglePass._worldScale did not return the audited Double value.");
        }

        return (double)value;
    }

    public static double LargeAnchorFromIsotropic(double isotropicScale)
    {
        double relative = ExpandedWorldMath.IsotropicLinearRelativeToLarge(Main.maxTilesX, Main.maxTilesY);
        if (relative <= 0d)
            throw new InvalidOperationException("[Expanded Worlds] Invalid Jungle isotropic scale ratio.");

        return isotropicScale / relative;
    }

    public static double Horizontal(JunglePass instance)
    {
        double large = LargeAnchorFromIsotropic(ReadIsotropicScale(instance));
        return ExpandedWorldMath.ScaleLargeLinearByWidth(large, Main.maxTilesX);
    }

    public static double Vertical(JunglePass instance)
    {
        double large = LargeAnchorFromIsotropic(ReadIsotropicScale(instance));
        return ExpandedWorldMath.ScaleLargeLinearByHeight(large, Main.maxTilesY);
    }

    public static double Scalar(JunglePass instance)
    {
        return ReadIsotropicScale(instance);
    }

    public static double ConvertVanillaOverallToIsotropic(double vanillaOverallScale)
    {
        if (!Active)
            return vanillaOverallScale;

        double widthRelative = ExpandedWorldMath.WidthRelativeToLarge(Main.maxTilesX);
        if (widthRelative <= 0d)
            return vanillaOverallScale;

        // The value Terraria just computed is its width-based continuation for
        // this expanded width. Divide that back to the Large boundary condition,
        // then extend the same Large value through sqrt(area ratio).
        double largeAnchor = vanillaOverallScale / widthRelative;
        return ExpandedWorldMath.ScaleLargeLinearIsotropically(
            largeAnchor,
            Main.maxTilesX,
            Main.maxTilesY);
    }

    public static double ConvertIsotropicToHorizontal(double isotropicScale)
    {
        if (!Active)
            return isotropicScale;

        double large = LargeAnchorFromIsotropic(isotropicScale);
        return ExpandedWorldMath.ScaleLargeLinearByWidth(large, Main.maxTilesX);
    }

    private static FieldInfo RequireWorldScaleField()
    {
        FieldInfo field = AccessTools.Field(typeof(JunglePass), "_worldScale");
        if (field == null)
            throw new MissingFieldException(typeof(JunglePass).FullName, "_worldScale");
        if (field.FieldType != typeof(double))
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] JunglePass._worldScale is no longer the audited Double field; refusing to patch it.");
        }

        return field;
    }
}

/// <summary>
/// Convert JunglePass._worldScale to the isotropic area-equivalent linear scale
/// as soon as vanilla computes it. The direct ApplyPass uses of this value are
/// round TileRunner strength except the 25*scale horizontal beach margin; that
/// margin is converted back to the horizontal scale at its use site.
/// </summary>
[HarmonyPatch]
internal static class ExpandedWorldJungleApplyPassPatch
{
    private static readonly FieldInfo WorldScaleField = ExpandedWorldJungleScales.WorldScaleField;

    private static readonly MethodInfo ToIsotropicMethod =
        AccessTools.Method(typeof(ExpandedWorldJungleScales), nameof(ExpandedWorldJungleScales.ConvertVanillaOverallToIsotropic))
        ?? throw new MissingMethodException(typeof(ExpandedWorldJungleScales).FullName, nameof(ExpandedWorldJungleScales.ConvertVanillaOverallToIsotropic));

    private static readonly MethodInfo ToHorizontalMethod =
        AccessTools.Method(typeof(ExpandedWorldJungleScales), nameof(ExpandedWorldJungleScales.ConvertIsotropicToHorizontal))
        ?? throw new MissingMethodException(typeof(ExpandedWorldJungleScales).FullName, nameof(ExpandedWorldJungleScales.ConvertIsotropicToHorizontal));

    private static MethodBase TargetMethod()
    {
        MethodBase method = AccessTools.GetDeclaredMethods(typeof(JunglePass))
            .FirstOrDefault(candidate => candidate.Name == "ApplyPass");
        if (method == null)
            throw new MissingMethodException(typeof(JunglePass).FullName, "ApplyPass");
        return method;
    }

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions,
        MethodBase __originalMethod)
    {
        var code = instructions.ToList();
        int worldScaleStores = 0;
        int scaleLocal = -1;

        for (int i = 0; i < code.Count; i++)
        {
            if (code[i].opcode != OpCodes.Stfld || !Equals(code[i].operand, WorldScaleField))
                continue;

            // Exact 1.4.5.8 stack type here is Double; ToIsotropicMethod also
            // accepts/returns Double so the inserted call preserves valid IL.
            code.Insert(i, new CodeInstruction(OpCodes.Call, ToIsotropicMethod));
            worldScaleStores++;
            i++;
        }

        if (worldScaleStores != 1)
        {
            throw Changed(__originalMethod,
                "expected exactly one JunglePass._worldScale assignment, found " + worldScaleStores);
        }

        for (int i = 0; i < code.Count - 4 && scaleLocal < 0; i++)
        {
            if (code[i].opcode != OpCodes.Ldfld || !Equals(code[i].operand, WorldScaleField))
                continue;

            for (int j = i + 1; j <= i + 4 && j < code.Count; j++)
            {
                int candidate = GetStoredLocalIndex(code[j]);
                if (candidate >= 0)
                {
                    scaleLocal = candidate;
                    break;
                }
            }
        }

        if (scaleLocal < 0)
            throw Changed(__originalMethod, "could not identify the local copy of JunglePass._worldScale");

        int horizontalMargins = 0;
        for (int i = 0; i < code.Count; i++)
        {
            if (!LoadsLocal(code[i], scaleLocal))
                continue;

            if (!HasNumericConstantImmediatelyBefore(code, i, 25d, 6))
                continue;

            code.Insert(i + 1, new CodeInstruction(OpCodes.Call, ToHorizontalMethod));
            horizontalMargins++;
            i++;
        }

        if (horizontalMargins != 1)
        {
            throw Changed(__originalMethod,
                "expected exactly one 25*scale horizontal Jungle margin, found " + horizontalMargins);
        }

        return code;
    }

    private static InvalidOperationException Changed(MethodBase method, string detail)
    {
        return new InvalidOperationException(
            "[Expanded Worlds] JunglePass source shape changed in " +
            (method?.DeclaringType?.FullName ?? "JunglePass") + "." +
            (method?.Name ?? "ApplyPass") + ": " + detail +
            ". Refusing to guess against this Terraria build.");
    }

    private static bool HasNumericConstantImmediatelyBefore(
        IReadOnlyList<CodeInstruction> code,
        int loadIndex,
        double expected,
        int maxDistance)
    {
        int start = Math.Max(0, loadIndex - maxDistance);
        for (int i = loadIndex - 1; i >= start; i--)
        {
            if (IsNumericConstant(code[i], expected))
                return true;

            if (GetStoredLocalIndex(code[i]) >= 0)
                break;
        }

        return false;
    }

    private static bool IsNumericConstant(CodeInstruction instruction, double expected)
    {
        if (instruction.opcode == OpCodes.Ldc_R8 && instruction.operand is double)
            return Math.Abs((double)instruction.operand - expected) < 1e-9;
        if (instruction.opcode == OpCodes.Ldc_R4 && instruction.operand is float)
            return Math.Abs((double)(float)instruction.operand - expected) < 1e-6;
        if (instruction.opcode == OpCodes.Ldc_I4 && instruction.operand is int)
            return (int)instruction.operand == (int)expected;
        if (instruction.opcode == OpCodes.Ldc_I4_S && instruction.operand is sbyte)
            return (sbyte)instruction.operand == (int)expected;
        if ((int)expected == 0 && instruction.opcode == OpCodes.Ldc_I4_0) return true;
        if ((int)expected == 1 && instruction.opcode == OpCodes.Ldc_I4_1) return true;
        if ((int)expected == 2 && instruction.opcode == OpCodes.Ldc_I4_2) return true;
        if ((int)expected == 3 && instruction.opcode == OpCodes.Ldc_I4_3) return true;
        if ((int)expected == 4 && instruction.opcode == OpCodes.Ldc_I4_4) return true;
        if ((int)expected == 5 && instruction.opcode == OpCodes.Ldc_I4_5) return true;
        if ((int)expected == 6 && instruction.opcode == OpCodes.Ldc_I4_6) return true;
        if ((int)expected == 7 && instruction.opcode == OpCodes.Ldc_I4_7) return true;
        if ((int)expected == 8 && instruction.opcode == OpCodes.Ldc_I4_8) return true;
        return false;
    }

    private static bool LoadsLocal(CodeInstruction instruction, int localIndex)
    {
        if (localIndex == 0 && instruction.opcode == OpCodes.Ldloc_0) return true;
        if (localIndex == 1 && instruction.opcode == OpCodes.Ldloc_1) return true;
        if (localIndex == 2 && instruction.opcode == OpCodes.Ldloc_2) return true;
        if (localIndex == 3 && instruction.opcode == OpCodes.Ldloc_3) return true;
        if (instruction.opcode != OpCodes.Ldloc && instruction.opcode != OpCodes.Ldloc_S)
            return false;
        return GetOperandLocalIndex(instruction.operand) == localIndex;
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
}

[HarmonyPatch]
internal static class ExpandedWorldJungleRandomMovementPatch
{
    private static MethodBase TargetMethod()
    {
        MethodBase method = AccessTools.Method(typeof(JunglePass), "ApplyRandomMovement");
        if (method == null)
            throw new MissingMethodException(typeof(JunglePass).FullName, "ApplyRandomMovement");
        return method;
    }

    [HarmonyPrefix]
    private static bool Prefix(JunglePass __instance, ref int x, ref int y, int xRange, int yRange)
    {
        if (!ExpandedWorldJungleScales.Active)
            return true;

        double horizontal = ExpandedWorldJungleScales.Horizontal(__instance);
        double vertical = ExpandedWorldJungleScales.Vertical(__instance);

        x += WorldGen.genRand.Next(
            (int)(-xRange * horizontal),
            1 + (int)(xRange * horizontal));
        y += WorldGen.genRand.Next(
            (int)(-yRange * vertical),
            1 + (int)(yRange * vertical));
        y = Utils.Clamp(y, (int)Main.rockLayer, Main.maxTilesY);
        return false;
    }
}

[HarmonyPatch]
internal static class ExpandedWorldJungleGemPatch
{
    private static MethodBase TargetMethod()
    {
        MethodBase method = AccessTools.Method(typeof(JunglePass), "PlaceGemsAt");
        if (method == null)
            throw new MissingMethodException(typeof(JunglePass).FullName, "PlaceGemsAt");
        return method;
    }

    [HarmonyPrefix]
    private static bool Prefix(JunglePass __instance, int x, int y, ushort baseGem, int gemVariants)
    {
        if (!ExpandedWorldJungleScales.Active)
            return true;

        double horizontal = ExpandedWorldJungleScales.Horizontal(__instance);
        double vertical = ExpandedWorldJungleScales.Vertical(__instance);
        double scalar = ExpandedWorldJungleScales.Scalar(__instance);

        for (int index = 0; index < 6d * scalar; index++)
        {
            WorldGen.TileRunner(
                x + WorldGen.genRand.Next(-(int)(125d * horizontal), (int)(125d * horizontal)),
                y + WorldGen.genRand.Next(-(int)(125d * vertical), (int)(125d * vertical)),
                WorldGen.genRand.Next(3, 7),
                WorldGen.genRand.Next(3, 8),
                WorldGen.genRand.Next((int)baseGem, (int)baseGem + gemVariants),
                false,
                0f,
                0f,
                false,
                true,
                -1);
        }

        return false;
    }
}

[HarmonyPatch]
internal static class ExpandedWorldJungleFinishingTouchesPatch
{
    private static MethodBase TargetMethod()
    {
        MethodBase method = AccessTools.Method(typeof(JunglePass), "GenerateFinishingTouches");
        if (method == null)
            throw new MissingMethodException(typeof(JunglePass).FullName, "GenerateFinishingTouches");
        return method;
    }

    [HarmonyPrefix]
    private static bool Prefix(JunglePass __instance, GenerationProgress progress, int oldX, int oldY)
    {
        if (!ExpandedWorldJungleScales.Active)
            return true;

        double horizontal = ExpandedWorldJungleScales.Horizontal(__instance);
        double vertical = ExpandedWorldJungleScales.Vertical(__instance);
        double scalar = ExpandedWorldJungleScales.Scalar(__instance);

        int walkX = oldX;
        int walkY = oldY;

        for (int index = 0; index <= 20d * scalar; index++)
        {
            progress.Set((float)((60d + index / scalar) * 0.01d));
            walkX += WorldGen.genRand.Next((int)(-5d * horizontal), (int)(6d * horizontal));
            walkY += WorldGen.genRand.Next((int)(-5d * vertical), (int)(6d * vertical));
            WorldGen.TileRunner(
                walkX,
                walkY,
                WorldGen.genRand.Next(40, 100),
                WorldGen.genRand.Next(300, 500),
                59,
                false,
                0f,
                0f,
                false,
                true,
                -1);
        }

        for (int index = 0; index <= 10d * scalar; index++)
        {
            progress.Set((float)((80d + index / scalar * 2d) * 0.01d));

            int i = oldX + WorldGen.genRand.Next(
                (int)(-600d * horizontal),
                (int)(600d * horizontal));
            int j = oldY + WorldGen.genRand.Next(
                (int)(-200d * vertical),
                (int)(200d * vertical));

            while (i < 1 || i >= Main.maxTilesX - 1 ||
                   j < 1 || j >= Main.maxTilesY - 1 ||
                   Main.tile[i, j].type != 59)
            {
                i = oldX + WorldGen.genRand.Next(
                    (int)(-600d * horizontal),
                    (int)(600d * horizontal));
                j = oldY + WorldGen.genRand.Next(
                    (int)(-200d * vertical),
                    (int)(200d * vertical));
            }

            // Vanilla multiplies both this outer count and the nested detail
            // count by one linear world scale. Using the isotropic factor for
            // each preserves that algebra exactly: scalar^2 = area factor.
            for (int detail = 0; detail < 8d * scalar; detail++)
            {
                i += WorldGen.genRand.Next(-30, 31);
                j += WorldGen.genRand.Next(-30, 31);
                int type = WorldGen.genRand.Next(7) == 0 ? -2 : -1;
                WorldGen.TileRunner(
                    i,
                    j,
                    WorldGen.genRand.Next(10, 20),
                    WorldGen.genRand.Next(30, 70),
                    type,
                    false,
                    0f,
                    0f,
                    false,
                    true,
                    -1);
            }
        }

        for (int index = 0; index <= 300d * scalar; index++)
        {
            int i = oldX + WorldGen.genRand.Next(
                (int)(-600d * horizontal),
                (int)(600d * horizontal));
            int j = oldY + WorldGen.genRand.Next(
                (int)(-200d * vertical),
                (int)(200d * vertical));

            while (i < 1 || i >= Main.maxTilesX - 1 ||
                   j < 1 || j >= Main.maxTilesY - 1 ||
                   Main.tile[i, j].type != 59)
            {
                i = oldX + WorldGen.genRand.Next(
                    (int)(-600d * horizontal),
                    (int)(600d * horizontal));
                j = oldY + WorldGen.genRand.Next(
                    (int)(-200d * vertical),
                    (int)(200d * vertical));
            }

            WorldGen.TileRunner(
                i,
                j,
                WorldGen.genRand.Next(4, 10),
                WorldGen.genRand.Next(5, 30),
                1,
                false,
                0f,
                0f,
                false,
                true,
                -1);

            if (WorldGen.genRand.Next(4) == 0)
            {
                int type = WorldGen.genRand.Next(63, 69);
                WorldGen.TileRunner(
                    i + WorldGen.genRand.Next(-1, 2),
                    j + WorldGen.genRand.Next(-1, 2),
                    WorldGen.genRand.Next(3, 7),
                    WorldGen.genRand.Next(4, 8),
                    type,
                    false,
                    0f,
                    0f,
                    false,
                    true,
                    -1);
            }
        }

        return false;
    }
}
#endif
