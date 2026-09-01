#if GLOADER_CLIENT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Terraria;
using Terraria.GameContent.Biomes;
using Terraria.IO;
using Terraria.WorldBuilding;

/// <summary>
/// Axis-aware continuation of Terraria's JunglePass for worlds wider than Large.
///
/// Vanilla JunglePass carries one private _worldScale through several different
/// dimensional jobs because vanilla width and height grow together. Expanded
/// Worlds separates those jobs:
///
///   X displacement / horizontal margins -> width relative to Large
///   Y displacement                       -> height relative to Large
///   round TileRunner body strength       -> sqrt(area relative to Large)
///   repeated macro/detail passes         -> Jungle area (width here, because
///                                           expanded presets retain Large height)
///
/// The original pass still runs. We only replace the overloaded scale where its
/// dimensional meaning becomes ambiguous. Vanilla Small/Medium/Large are never
/// touched, and seed-specific JunglePass behavior remains authoritative.
/// </summary>
internal static class ExpandedWorldJungleScales
{
    private static readonly FieldInfo WorldScaleField =
        AccessTools.Field(typeof(JunglePass), "_worldScale")
        ?? throw new MissingFieldException(typeof(JunglePass).FullName, "_worldScale");

    public static bool Active =>
        ExpandedWorldState.GenerationArmed && Main.maxTilesX > ExpandedWorldMath.LargeWidth;

    public static float ReadIsotropicScale(JunglePass instance)
    {
        return (float)WorldScaleField.GetValue(instance);
    }

    public static float LargeAnchorFromIsotropic(float isotropicScale)
    {
        double relative = ExpandedWorldMath.IsotropicLinearRelativeToLarge(Main.maxTilesX, Main.maxTilesY);
        if (relative <= 0d)
            throw new InvalidOperationException("[Expanded Worlds] Invalid Jungle isotropic scale ratio.");

        return (float)(isotropicScale / relative);
    }

    public static float Horizontal(JunglePass instance)
    {
        float large = LargeAnchorFromIsotropic(ReadIsotropicScale(instance));
        return (float)ExpandedWorldMath.ScaleLargeLinearByWidth(large, Main.maxTilesX);
    }

    public static float Vertical(JunglePass instance)
    {
        float large = LargeAnchorFromIsotropic(ReadIsotropicScale(instance));
        return (float)ExpandedWorldMath.ScaleLargeLinearByHeight(large, Main.maxTilesY);
    }

    public static float Isotropic(JunglePass instance)
    {
        return ReadIsotropicScale(instance);
    }

    // Macro/detail pass counts are proportional to the Jungle territory added by
    // the wider canvas. Since XL/Huge retain Large height, this is exactly the
    // width ratio to Large. Keep the source's Large anchor value so its existing
    // count constants remain meaningful.
    public static float Density(JunglePass instance)
    {
        return Horizontal(instance);
    }

    public static float ConvertVanillaOverallToIsotropic(float vanillaOverallScale)
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
        return (float)ExpandedWorldMath.ScaleLargeLinearIsotropically(
            largeAnchor,
            Main.maxTilesX,
            Main.maxTilesY);
    }

    public static float ConvertIsotropicToHorizontal(float isotropicScale)
    {
        if (!Active)
            return isotropicScale;

        float large = LargeAnchorFromIsotropic(isotropicScale);
        return (float)ExpandedWorldMath.ScaleLargeLinearByWidth(large, Main.maxTilesX);
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
    private static readonly FieldInfo WorldScaleField =
        AccessTools.Field(typeof(JunglePass), "_worldScale")
        ?? throw new MissingFieldException(typeof(JunglePass).FullName, "_worldScale");

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

        // 1) Terraria computes _worldScale from world size. Convert that one
        // float before it is stored. This makes round/local geometry isotropic.
        for (int i = 0; i < code.Count; i++)
        {
            if (code[i].opcode != OpCodes.Stfld || !Equals(code[i].operand, WorldScaleField))
                continue;

            code.Insert(i, new CodeInstruction(OpCodes.Call, ToIsotropicMethod));
            worldScaleStores++;
            i++;
        }

        if (worldScaleStores != 1)
        {
            throw Changed(__originalMethod,
                "expected exactly one JunglePass._worldScale assignment, found " + worldScaleStores);
        }

        // 2) ApplyPass copies _worldScale to a float local. Find that local from
        // ldfld -> stloc rather than assuming a compiler-specific slot number.
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

        // 3) The 25*scale expression is a horizontal clamp margin, not round
        // geometry. Convert only that local load back to horizontal scale.
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

/// <summary>
/// Vanilla multiplies both xRange and yRange by the same Jungle scale. For an
/// expanded aspect ratio, preserve the exact random-movement algorithm but feed
/// each axis its own physical continuation.
/// </summary>
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

        float horizontal = ExpandedWorldJungleScales.Horizontal(__instance);
        float vertical = ExpandedWorldJungleScales.Vertical(__instance);

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

/// <summary>
/// Gem-pass count follows the horizontally enlarged Jungle territory while the
/// placement envelope is axis-aware. The gem TileRunner itself remains vanilla.
/// </summary>
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

        float horizontal = ExpandedWorldJungleScales.Horizontal(__instance);
        float vertical = ExpandedWorldJungleScales.Vertical(__instance);

        for (int index = 0; index < 6d * horizontal; index++)
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

/// <summary>
/// Re-express JunglePass.GenerateFinishingTouches with the same vanilla random
/// operations, constants and TileRunner calls, but with explicit dimensional
/// scales. This method is private implementation detail in Terraria, so the patch
/// targets it by name and only replaces it for an armed expanded generation.
/// </summary>
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

        float horizontal = ExpandedWorldJungleScales.Horizontal(__instance);
        float vertical = ExpandedWorldJungleScales.Vertical(__instance);
        float isotropic = ExpandedWorldJungleScales.Isotropic(__instance);
        float density = ExpandedWorldJungleScales.Density(__instance);

        int walkX = oldX;
        int walkY = oldY;

        for (int index = 0; index <= 20d * density; index++)
        {
            progress.Set((float)((60d + index / (double)density) * 0.01d));
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

        for (int index = 0; index <= 10d * density; index++)
        {
            progress.Set((float)((80d + index / (double)density * 2d) * 0.01d));

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

            for (int detail = 0; detail < 8d * isotropic; detail++)
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

        for (int index = 0; index <= 300d * density; index++)
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