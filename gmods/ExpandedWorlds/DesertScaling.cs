#if GLOADER_CLIENT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Terraria;
using Terraria.GameContent.Biomes.Desert;

/// <summary>
/// Terraria's normal world sizes grow in both dimensions, so
/// DesertDescription.CreateFromPlacement derives one overall-size scalar from
/// maxTilesX / 4200 and uses it for both horizontal and vertical geometry.
///
/// Expanded Worlds intentionally changes the aspect ratio. Keep the source's
/// horizontal uses on maxTilesX / 4200, but replace only the three vertical uses
/// with maxTilesY / 1200:
///
///   normal depth:              170 * scale
///   Remix depth:               340 * scale
///   tenth-anniversary Y offset: 20 * scale
///
/// Everything else in CreateFromPlacement remains vanilla, including the exact
/// random draw, truncation, seed branches, surface scan and rectangle creation.
/// </summary>
[HarmonyPatch(typeof(DesertDescription), nameof(DesertDescription.CreateFromPlacement))]
internal static class ExpandedWorldDesertAxisScalingPatch
{
    private static readonly FieldInfo MaxTilesXField =
        AccessTools.Field(typeof(Main), nameof(Main.maxTilesX))
        ?? throw new MissingFieldException(typeof(Main).FullName, nameof(Main.maxTilesX));

    private static readonly MethodInfo AdjustVerticalScaleMethod =
        AccessTools.Method(typeof(ExpandedWorldDesertAxisScalingPatch), nameof(AdjustVerticalScale))
        ?? throw new MissingMethodException(
            typeof(ExpandedWorldDesertAxisScalingPatch).FullName,
            nameof(AdjustVerticalScale));

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions,
        MethodBase __originalMethod)
    {
        var code = instructions.ToList();
        int scaleLocal = FindVanillaOverallScaleLocal(code);
        int replacements = 0;

        for (int i = 0; i < code.Count; i++)
        {
            if (!LoadsLocal(code[i], scaleLocal) || !IsVerticalScaleUse(code, i))
                continue;

            // Stack at this point contains the source's width-derived double.
            // Transform only that value; leave every surrounding arithmetic and
            // random operation exactly as Terraria emitted it.
            code.Insert(i + 1, new CodeInstruction(OpCodes.Call, AdjustVerticalScaleMethod));
            replacements++;
            i++;
        }

        if (replacements != 3)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] Underground Desert source shape changed in " +
                (__originalMethod?.DeclaringType?.FullName ?? "DesertDescription") + "." +
                (__originalMethod?.Name ?? "CreateFromPlacement") +
                ": expected exactly 3 vertical overall-scale uses (170/340/20), found " +
                replacements + ". Refusing to guess against this Terraria build.");
        }

        return code;
    }

    private static double AdjustVerticalScale(double vanillaOverallScale)
    {
        if (!ExpandedWorldState.GenerationArmed || Main.maxTilesX <= ExpandedWorldMath.LargeWidth)
            return vanillaOverallScale;

        return ExpandedWorldMath.VerticalScale(Main.maxTilesY);
    }

    private static int FindVanillaOverallScaleLocal(IReadOnlyList<CodeInstruction> code)
    {
        // Source anchor:
        //   double num = (double)Main.maxTilesX / 4200.0;
        // Find the store following that exact physical-width calculation rather
        // than assuming a compiler-specific local number.
        for (int i = 0; i < code.Count; i++)
        {
            if (code[i].opcode != OpCodes.Ldsfld || !Equals(code[i].operand, MaxTilesXField))
                continue;

            bool saw4200 = false;
            bool sawDivision = false;

            for (int j = i + 1; j < code.Count && j <= i + 8; j++)
            {
                if (IsDoubleConstant(code[j], 4200d))
                    saw4200 = true;
                else if (code[j].opcode == OpCodes.Div)
                    sawDivision = true;

                int local = GetStoredLocalIndex(code[j]);
                if (local < 0)
                    continue;

                if (saw4200 && sawDivision)
                    return local;

                break;
            }
        }

        throw new InvalidOperationException(
            "[Expanded Worlds] Could not identify DesertDescription's maxTilesX / 4200 scale local. " +
            "The installed Terraria build changed; refusing to apply an inferred Desert patch.");
    }

    private static bool IsVerticalScaleUse(IReadOnlyList<CodeInstruction> code, int scaleLoadIndex)
    {
        // The three vertical source expressions are uniquely anchored by these
        // constants. Look only a short distance backward from the load of the
        // identified scale local so unrelated arithmetic cannot match.
        int start = Math.Max(0, scaleLoadIndex - 8);
        for (int i = scaleLoadIndex - 1; i >= start; i--)
        {
            if (IsDoubleConstant(code[i], 170d) ||
                IsDoubleConstant(code[i], 340d) ||
                IsDoubleConstant(code[i], 20d))
            {
                return true;
            }

            // A local store marks the beginning of another source assignment;
            // do not search through it for an old constant.
            if (GetStoredLocalIndex(code[i]) >= 0)
                break;
        }

        return false;
    }

    private static bool IsDoubleConstant(CodeInstruction instruction, double expected)
    {
        if (instruction.opcode != OpCodes.Ldc_R8 || !(instruction.operand is double))
            return false;

        return Math.Abs((double)instruction.operand - expected) < 1e-12;
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
        if (operand is LocalBuilder localBuilder)
            return localBuilder.LocalIndex;
        if (operand is byte byteIndex)
            return byteIndex;
        if (operand is sbyte signedByteIndex)
            return signedByteIndex;
        if (operand is short shortIndex)
            return shortIndex;
        if (operand is int intIndex)
            return intIndex;

        return -1;
    }
}
#endif
