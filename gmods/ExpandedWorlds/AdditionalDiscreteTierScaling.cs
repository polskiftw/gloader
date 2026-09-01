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
/// Pure continuations of additional Terraria 1.4.5 Small/Medium/Large sequences.
/// These are discrete source rules, not guesses from density or aesthetics.
/// </summary>
internal static class ExpandedWorldAdditionalTierMath
{
    public static int BoulderPetBaseCount(int oneBasedWorldTier)
    {
        if (oneBasedWorldTier < 1)
            throw new ArgumentOutOfRangeException(nameof(oneBasedWorldTier));
        // 2 / 4 / 6 in Terraria 1.4.5.8.
        return checked(2 * oneBasedWorldTier);
    }

    public static int GlowTulipCount(int oneBasedWorldTier)
    {
        if (oneBasedWorldTier < 1)
            throw new ArgumentOutOfRangeException(nameof(oneBasedWorldTier));
        // 2 / 4 / 6 in Terraria 1.4.5.8.
        return checked(2 * oneBasedWorldTier);
    }

    public static int SpikeCaveBaseCount(int oneBasedWorldTier)
    {
        if (oneBasedWorldTier < 1)
            throw new ArgumentOutOfRangeException(nameof(oneBasedWorldTier));
        // 3 / 5 / 7 in Terraria 1.4.5.8, before vanilla Next(2).
        return checked(2 * oneBasedWorldTier + 1);
    }

    public static int ChilletEggCount(int oneBasedWorldTier)
    {
        if (oneBasedWorldTier < 1)
            throw new ArgumentOutOfRangeException(nameof(oneBasedWorldTier));
        // 6 / 9 / 12 in Terraria 1.4.5.8.
        return checked(3 * (oneBasedWorldTier + 1));
    }
}

#if GLOADER_CLIENT
internal static class ExpandedWorldAdditionalTierPatchUtil
{
    internal static readonly MethodInfo GetWorldSizeMethod =
        AccessTools.Method(typeof(WorldGen), nameof(WorldGen.GetWorldSize), Type.EmptyTypes)
        ?? throw new MissingMethodException(typeof(WorldGen).FullName, nameof(WorldGen.GetWorldSize));

    internal static MethodBase ResolveUniqueWorldGenMethod(string name, int parameterCount)
    {
        var matches = typeof(WorldGen)
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(method => method.Name == name && method.GetParameters().Length == parameterCount)
            .Cast<MethodBase>()
            .ToList();

        if (matches.Count != 1)
        {
            throw new MissingMethodException(
                typeof(WorldGen).FullName,
                name + " with " + parameterCount + " parameters; found " + matches.Count);
        }

        return matches[0];
    }

    internal static IEnumerable<CodeInstruction> PatchGetWorldSizeSelection(
        IEnumerable<CodeInstruction> instructions,
        MethodBase original,
        int[] sourceValues,
        MethodInfo adjustMethod,
        string ruleName)
    {
        var code = instructions.ToList();
        int patched = 0;

        for (int callIndex = 0; callIndex < code.Count; callIndex++)
        {
            if (!Calls(code[callIndex], GetWorldSizeMethod))
                continue;

            int end = Math.Min(code.Count, callIndex + 96);
            var seen = new HashSet<int>();
            for (int i = callIndex + 1; i < end; i++)
            {
                for (int v = 0; v < sourceValues.Length; v++)
                {
                    if (IsIntConstant(code[i], sourceValues[v]))
                        seen.Add(sourceValues[v]);
                }

                if (seen.Count != sourceValues.Length || !IsLocalStore(code[i]))
                    continue;

                InsertAdjustmentBeforeStore(code, i, adjustMethod);
                patched++;
                callIndex = i + 1;
                break;
            }
        }

        if (patched != 1)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] " + ruleName + " source shape changed in " +
                (original?.DeclaringType?.FullName ?? "WorldGen") + "." +
                (original?.Name ?? "<unknown>") +
                ": expected one GetWorldSize selection containing [" +
                string.Join(",", sourceValues) + "], patched " + patched +
                ". Refusing to guess against this Terraria build.");
        }

        return code;
    }

    internal static IEnumerable<CodeInstruction> PatchInlineSelection(
        IEnumerable<CodeInstruction> instructions,
        MethodBase original,
        int[] sourceValues,
        MethodInfo adjustMethod,
        string ruleName)
    {
        var code = instructions.ToList();
        var seen = new HashSet<int>();
        int patched = 0;

        // GrowGlowTulips uses explicit maxTilesX comparisons instead of
        // GetWorldSize, then a 2/4/6 switch expression. Restrict the scan to the
        // early method prologue where that source selection lives.
        int end = Math.Min(code.Count, 120);
        for (int i = 0; i < end; i++)
        {
            for (int v = 0; v < sourceValues.Length; v++)
            {
                if (IsIntConstant(code[i], sourceValues[v]))
                    seen.Add(sourceValues[v]);
            }

            if (seen.Count != sourceValues.Length || !IsLocalStore(code[i]))
                continue;

            InsertAdjustmentBeforeStore(code, i, adjustMethod);
            patched++;
            break;
        }

        if (patched != 1)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] " + ruleName + " source shape changed in " +
                (original?.DeclaringType?.FullName ?? "WorldGen") + "." +
                (original?.Name ?? "<unknown>") +
                ": expected one early selection containing [" +
                string.Join(",", sourceValues) + "], patched " + patched +
                ". Refusing to guess against this Terraria build.");
        }

        return code;
    }

    internal static int ExpandedTierOrZero()
    {
        if (!ExpandedWorldState.GenerationArmed)
            return 0;

        switch (ExpandedWorldState.GenerationPreset)
        {
            case ExpandedWorldPreset.XL:
                return 4;
            case ExpandedWorldPreset.Huge:
                return 5;
            default:
                return 0;
        }
    }

    private static void InsertAdjustmentBeforeStore(
        List<CodeInstruction> code,
        int storeIndex,
        MethodInfo adjustMethod)
    {
        var adjust = new CodeInstruction(OpCodes.Call, adjustMethod);
        adjust.labels.AddRange(code[storeIndex].labels);
        code[storeIndex].labels.Clear();
        adjust.blocks.AddRange(code[storeIndex].blocks);
        code[storeIndex].blocks.Clear();
        code.Insert(storeIndex, adjust);
    }

    private static bool Calls(CodeInstruction instruction, MethodInfo method)
    {
        return (instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) &&
               Equals(instruction.operand, method);
    }

    private static bool IsLocalStore(CodeInstruction instruction)
    {
        return instruction.opcode == OpCodes.Stloc_0 ||
               instruction.opcode == OpCodes.Stloc_1 ||
               instruction.opcode == OpCodes.Stloc_2 ||
               instruction.opcode == OpCodes.Stloc_3 ||
               instruction.opcode == OpCodes.Stloc ||
               instruction.opcode == OpCodes.Stloc_S;
    }

    private static bool IsIntConstant(CodeInstruction instruction, int expected)
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

[HarmonyPatch]
internal static class ExpandedWorldBoulderPetTierPatch
{
    private static readonly MethodInfo AdjustMethod =
        AccessTools.Method(typeof(ExpandedWorldBoulderPetTierPatch), nameof(AdjustBaseCount))
        ?? throw new MissingMethodException(typeof(ExpandedWorldBoulderPetTierPatch).FullName, nameof(AdjustBaseCount));

    private static MethodBase TargetMethod() =>
        ExpandedWorldAdditionalTierPatchUtil.ResolveUniqueWorldGenMethod("placeTrap", 3);

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        return ExpandedWorldAdditionalTierPatchUtil.PatchGetWorldSizeSelection(
            instructions, __originalMethod, new[] { 2, 4, 6 }, AdjustMethod, "boulder-pet cap");
    }

    private static int AdjustBaseCount(int vanillaCount)
    {
        int tier = ExpandedWorldAdditionalTierPatchUtil.ExpandedTierOrZero();
        if (tier == 0)
            return vanillaCount;
        if (vanillaCount != 6)
            throw new InvalidOperationException("[Expanded Worlds] Expected vanilla Large boulder-pet cap 6, got " + vanillaCount + ".");
        return ExpandedWorldAdditionalTierMath.BoulderPetBaseCount(tier);
    }
}

[HarmonyPatch]
internal static class ExpandedWorldSpikeCaveTierPatch
{
    private static readonly MethodInfo AdjustMethod =
        AccessTools.Method(typeof(ExpandedWorldSpikeCaveTierPatch), nameof(AdjustBaseCount))
        ?? throw new MissingMethodException(typeof(ExpandedWorldSpikeCaveTierPatch).FullName, nameof(AdjustBaseCount));

    private static MethodBase TargetMethod() =>
        ExpandedWorldAdditionalTierPatchUtil.ResolveUniqueWorldGenMethod("AddSpikeCaves", 1);

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        return ExpandedWorldAdditionalTierPatchUtil.PatchGetWorldSizeSelection(
            instructions, __originalMethod, new[] { 3, 5, 7 }, AdjustMethod, "Spike Cave base count");
    }

    private static int AdjustBaseCount(int vanillaCount)
    {
        int tier = ExpandedWorldAdditionalTierPatchUtil.ExpandedTierOrZero();
        if (tier == 0)
            return vanillaCount;
        if (vanillaCount != 7)
            throw new InvalidOperationException("[Expanded Worlds] Expected vanilla Large Spike Cave base 7, got " + vanillaCount + ".");
        return ExpandedWorldAdditionalTierMath.SpikeCaveBaseCount(tier);
    }
}

[HarmonyPatch]
internal static class ExpandedWorldChilletEggTierPatch
{
    private static readonly MethodInfo AdjustMethod =
        AccessTools.Method(typeof(ExpandedWorldChilletEggTierPatch), nameof(AdjustCount))
        ?? throw new MissingMethodException(typeof(ExpandedWorldChilletEggTierPatch).FullName, nameof(AdjustCount));

    private static MethodBase TargetMethod() =>
        ExpandedWorldAdditionalTierPatchUtil.ResolveUniqueWorldGenMethod("PlaceChilletEggs", 0);

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        return ExpandedWorldAdditionalTierPatchUtil.PatchGetWorldSizeSelection(
            instructions, __originalMethod, new[] { 6, 9, 12 }, AdjustMethod, "Chillet Egg count");
    }

    private static int AdjustCount(int vanillaCount)
    {
        int tier = ExpandedWorldAdditionalTierPatchUtil.ExpandedTierOrZero();
        if (tier == 0)
            return vanillaCount;
        if (vanillaCount != 12)
            throw new InvalidOperationException("[Expanded Worlds] Expected vanilla Large Chillet Egg count 12, got " + vanillaCount + ".");
        return ExpandedWorldAdditionalTierMath.ChilletEggCount(tier);
    }
}

[HarmonyPatch]
internal static class ExpandedWorldGlowTulipTierPatch
{
    private static readonly MethodInfo AdjustMethod =
        AccessTools.Method(typeof(ExpandedWorldGlowTulipTierPatch), nameof(AdjustCount))
        ?? throw new MissingMethodException(typeof(ExpandedWorldGlowTulipTierPatch).FullName, nameof(AdjustCount));

    private static MethodBase TargetMethod() =>
        ExpandedWorldAdditionalTierPatchUtil.ResolveUniqueWorldGenMethod("GrowGlowTulips", 0);

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        return ExpandedWorldAdditionalTierPatchUtil.PatchInlineSelection(
            instructions, __originalMethod, new[] { 2, 4, 6 }, AdjustMethod, "Glow Tulip count");
    }

    private static int AdjustCount(int vanillaCount)
    {
        int tier = ExpandedWorldAdditionalTierPatchUtil.ExpandedTierOrZero();
        if (tier == 0)
            return vanillaCount;
        if (vanillaCount != 6)
            throw new InvalidOperationException("[Expanded Worlds] Expected vanilla Large Glow Tulip count 6, got " + vanillaCount + ".");
        return ExpandedWorldAdditionalTierMath.GlowTulipCount(tier);
    }
}
#endif
