using System;

#if GLOADER_CLIENT
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Terraria;

/// <summary>
/// Generalizes worldgen rules that are explicitly keyed to Terraria's discrete
/// Small / Medium / Large category rather than to physical dimensions.
///
/// These are intentionally separate from GenerationMath's width/height/area
/// families. A discrete tier rule is only extended when the source itself gives
/// an unambiguous per-tier sequence. THICC shares Huge's horizontal/tier term;
/// its extra vertical canvas is handled only by physical height/area rules.
/// </summary>
[HarmonyPatch]
internal static class ExpandedWorldDiscreteTierGenerationPatch
{
    private static readonly MethodInfo GetWorldSizeMethod =
        AccessTools.Method(typeof(WorldGen), nameof(WorldGen.GetWorldSize), Type.EmptyTypes)
        ?? throw new MissingMethodException(typeof(WorldGen).FullName, nameof(WorldGen.GetWorldSize));

    private static readonly MethodInfo AdjustDirtiestBlockCountMethod =
        AccessTools.Method(
            typeof(ExpandedWorldDiscreteTierGenerationPatch),
            nameof(AdjustDirtiestBlockBaseCount))
        ?? throw new MissingMethodException(
            typeof(ExpandedWorldDiscreteTierGenerationPatch).FullName,
            nameof(AdjustDirtiestBlockBaseCount));

    private static MethodBase TargetMethod()
    {
        List<MethodBase> matches = EnumerateImplementationMethods(typeof(WorldGen))
            .Where(ContainsDirtiestBlockSwitchShape)
            .ToList();

        if (matches.Count != 1)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] Could not uniquely resolve the FinalCleanup Dirtiest Block delegate: " +
                "expected one WorldGen implementation method containing GetWorldSize + 3/6/9, found " +
                matches.Count + ". Refusing to guess against this Terraria build.");
        }

        return matches[0];
    }

    private static IEnumerable<MethodBase> EnumerateImplementationMethods(Type root)
    {
        const BindingFlags methodFlags =
            BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Static | BindingFlags.Instance |
            BindingFlags.DeclaredOnly;

        foreach (MethodInfo method in root.GetMethods(methodFlags))
            yield return method;

        const BindingFlags nestedFlags = BindingFlags.Public | BindingFlags.NonPublic;
        foreach (Type nested in root.GetNestedTypes(nestedFlags))
        {
            foreach (MethodBase method in EnumerateImplementationMethods(nested))
                yield return method;
        }
    }

    private static bool ContainsDirtiestBlockSwitchShape(MethodBase method)
    {
        MethodBody body;
        byte[] il;
        try
        {
            body = method.GetMethodBody();
            il = body?.GetILAsByteArray();
        }
        catch
        {
            return false;
        }

        if (il == null || il.Length == 0)
            return false;

        int callOffset = FindCallToGetWorldSize(il);
        if (callOffset < 0)
            return false;

        int end = Math.Min(il.Length, callOffset + 160);
        return ContainsIntConstant(il, callOffset, end, 3) &&
               ContainsIntConstant(il, callOffset, end, 6) &&
               ContainsIntConstant(il, callOffset, end, 9);
    }

    private static int FindCallToGetWorldSize(byte[] il)
    {
        int token = GetWorldSizeMethod.MetadataToken;
        for (int i = 0; i + 4 < il.Length; i++)
        {
            if (il[i] != 0x28 && il[i] != 0x6f)
                continue;

            if (BitConverter.ToInt32(il, i + 1) == token)
                return i;
        }

        return -1;
    }

    private static bool ContainsIntConstant(byte[] il, int start, int end, int value)
    {
        if (value >= 0 && value <= 8)
        {
            byte shortOpcode = (byte)(0x16 + value);
            for (int i = start; i < end; i++)
            {
                if (il[i] == shortOpcode)
                    return true;
            }
        }

        for (int i = start; i < end; i++)
        {
            if (il[i] == 0x1f && i + 1 < end && unchecked((sbyte)il[i + 1]) == value)
                return true;

            if (il[i] == 0x20 && i + 4 < end && BitConverter.ToInt32(il, i + 1) == value)
                return true;
        }

        return false;
    }

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions,
        MethodBase __originalMethod)
    {
        var code = instructions.ToList();
        int patched = 0;

        for (int callIndex = 0; callIndex < code.Count; callIndex++)
        {
            if (!Calls(code[callIndex], GetWorldSizeMethod))
                continue;

            int end = Math.Min(code.Count, callIndex + 64);
            bool saw6 = false;
            bool saw9 = false;
            bool saw3 = false;

            for (int i = callIndex + 1; i < end; i++)
            {
                saw6 |= IsIntConstant(code[i], 6);
                saw9 |= IsIntConstant(code[i], 9);
                saw3 |= IsIntConstant(code[i], 3);

                if (!IsLocalStore(code[i]) || !saw6 || !saw9 || !saw3)
                    continue;

                var adjust = new CodeInstruction(OpCodes.Call, AdjustDirtiestBlockCountMethod);
                adjust.labels.AddRange(code[i].labels);
                code[i].labels.Clear();
                adjust.blocks.AddRange(code[i].blocks);
                code[i].blocks.Clear();

                code.Insert(i, adjust);
                patched++;
                callIndex = i + 1;
                break;
            }
        }

        if (patched != 1)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] FinalCleanup discrete-tier source shape changed in " +
                (__originalMethod?.DeclaringType?.FullName ?? "WorldGen") + "." +
                (__originalMethod?.Name ?? "<generated>") +
                ": expected exactly one GetWorldSize switch with 3/6/9 Dirtiest Block counts, found " +
                patched + ". Refusing to guess against this Terraria build.");
        }

        return code;
    }

    private static int AdjustDirtiestBlockBaseCount(int vanillaCount)
    {
        if (!ExpandedWorldState.GenerationArmed)
            return vanillaCount;

        if (vanillaCount != 9)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] Expected vanilla Large Dirtiest Block base count 9, got " +
                vanillaCount + ".");
        }

        switch (ExpandedWorldState.GenerationPreset)
        {
            case ExpandedWorldPreset.XL:
            case ExpandedWorldPreset.Huge:
            case ExpandedWorldPreset.Thicc:
                return ExpandedWorldTierMath.DirtiestBlockBaseCount(
                    ExpandedWorldState.DiscreteTierFor(ExpandedWorldState.GenerationPreset));
            default:
                return vanillaCount;
        }
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
#endif

internal static class ExpandedWorldTierMath
{
    public static int DirtiestBlockBaseCount(int oneBasedWorldTier)
    {
        if (oneBasedWorldTier < 1)
            throw new ArgumentOutOfRangeException(nameof(oneBasedWorldTier));

        return checked(3 * oneBasedWorldTier);
    }

    public static int DirtiestBlockCount(int oneBasedWorldTier, bool celebrationMk10)
    {
        int count = DirtiestBlockBaseCount(oneBasedWorldTier);
        return celebrationMk10 ? checked(count * 5) : count;
    }
}