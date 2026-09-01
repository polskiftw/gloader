#if GLOADER_CLIENT
using System;
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
/// an unambiguous per-tier sequence.
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
        MethodBase method = AccessTools.GetDeclaredMethods(typeof(WorldGen))
            .FirstOrDefault(candidate => candidate.Name == "GenerateWorld" && candidate.IsStatic);

        if (method == null)
            throw new MissingMethodException(typeof(WorldGen).FullName, "GenerateWorld");

        return method;
    }

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions,
        MethodBase __originalMethod)
    {
        var code = instructions.ToList();
        int patched = 0;

        // Audited 1.4.4+ source shape for The Dirtiest Block:
        //
        //   int target = 3;
        //   target = GetWorldSize() switch {
        //       1 => 6,
        //       2 => 9,
        //       _ => 3,
        //   };
        //   if (tenthAnniversaryWorldGen)
        //       target *= 5;
        //
        // We identify the switch-result local rather than relying on a compiler-
        // specific local number. The inserted call occurs after the switch result
        // is stored and before vanilla's Celebration x5 branch.
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

                int local = GetStoredLocalIndex(code[i]);
                if (local < 0 || !saw6 || !saw9 || !saw3)
                    continue;

                var replacement = new[]
                {
                    LoadLocal(local),
                    new CodeInstruction(OpCodes.Call, AdjustDirtiestBlockCountMethod),
                    StoreLocal(local),
                };

                code.InsertRange(i + 1, replacement);
                patched++;
                callIndex = i + replacement.Length;
                break;
            }
        }

        if (patched != 1)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] GenerateWorld discrete-tier source shape changed in " +
                (__originalMethod?.DeclaringType?.FullName ?? "WorldGen") + "." +
                (__originalMethod?.Name ?? "GenerateWorld") +
                ": expected exactly one GetWorldSize switch with 3/6/9 Dirtiest Block counts, found " +
                patched + ". Refusing to guess against this Terraria build.");
        }

        return code;
    }

    private static int AdjustDirtiestBlockBaseCount(int vanillaCount)
    {
        if (!ExpandedWorldState.GenerationArmed)
            return vanillaCount;

        // XL/Huge intentionally report Large categorically, so the source switch
        // must have produced Large's base 9 before we generalize it. Fail closed
        // if current Terraria changed that rule.
        if (vanillaCount != 9)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] Expected vanilla Large Dirtiest Block base count 9, got " +
                vanillaCount + ".");
        }

        switch (ExpandedWorldState.GenerationPreset)
        {
            case ExpandedWorldPreset.XL:
                return ExpandedWorldTierMath.DirtiestBlockBaseCount(4);
            case ExpandedWorldPreset.Huge:
                return ExpandedWorldTierMath.DirtiestBlockBaseCount(5);
            default:
                return vanillaCount;
        }
    }

    private static bool Calls(CodeInstruction instruction, MethodInfo method)
    {
        return (instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) &&
               Equals(instruction.operand, method);
    }

    private static CodeInstruction LoadLocal(int local)
    {
        switch (local)
        {
            case 0: return new CodeInstruction(OpCodes.Ldloc_0);
            case 1: return new CodeInstruction(OpCodes.Ldloc_1);
            case 2: return new CodeInstruction(OpCodes.Ldloc_2);
            case 3: return new CodeInstruction(OpCodes.Ldloc_3);
            default:
                if (local <= byte.MaxValue)
                    return new CodeInstruction(OpCodes.Ldloc_S, (byte)local);
                return new CodeInstruction(OpCodes.Ldloc, (short)local);
        }
    }

    private static CodeInstruction StoreLocal(int local)
    {
        switch (local)
        {
            case 0: return new CodeInstruction(OpCodes.Stloc_0);
            case 1: return new CodeInstruction(OpCodes.Stloc_1);
            case 2: return new CodeInstruction(OpCodes.Stloc_2);
            case 3: return new CodeInstruction(OpCodes.Stloc_3);
            default:
                if (local <= byte.MaxValue)
                    return new CodeInstruction(OpCodes.Stloc_S, (byte)local);
                return new CodeInstruction(OpCodes.Stloc, (short)local);
        }
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

    private static bool IsIntConstant(CodeInstruction instruction, int expected)
    {
        if (instruction.opcode == OpCodes.Ldc_I4 && instruction.operand is int)
            return (int)instruction.operand == expected;
        if (instruction.opcode == OpCodes.Ldc_I4_S && instruction.operand is sbyte)
            return (sbyte)instruction.operand == expected;
        if (expected == 0 && instruction.opcode == OpCodes.Ldc_I4_0) return true;
        if (expected == 1 && instruction.opcode == OpCodes.Ldc_I4_1) return true;
        if (expected == 2 && instruction.opcode == OpCodes.Ldc_I4_2) return true;
        if (expected == 3 && instruction.opcode == OpCodes.Ldc_I4_3) return true;
        if (expected == 4 && instruction.opcode == OpCodes.Ldc_I4_4) return true;
        if (expected == 5 && instruction.opcode == OpCodes.Ldc_I4_5) return true;
        if (expected == 6 && instruction.opcode == OpCodes.Ldc_I4_6) return true;
        if (expected == 7 && instruction.opcode == OpCodes.Ldc_I4_7) return true;
        if (expected == 8 && instruction.opcode == OpCodes.Ldc_I4_8) return true;
        if (expected == 9 && instruction.opcode == OpCodes.Ldc_I4_S && instruction.operand is sbyte)
            return (sbyte)instruction.operand == 9;
        return false;
    }
}
#endif

/// <summary>
/// Pure discrete-tier rules which can be compiled by the Terraria-independent
/// regression project.
/// </summary>
internal static class ExpandedWorldTierMath
{
    public static int DirtiestBlockBaseCount(int oneBasedWorldTier)
    {
        if (oneBasedWorldTier < 1)
            throw new ArgumentOutOfRangeException(nameof(oneBasedWorldTier));

        // Vanilla Small/Medium/Large are exactly 3/6/9.
        return checked(3 * oneBasedWorldTier);
    }

    public static int DirtiestBlockCount(int oneBasedWorldTier, bool celebrationMk10)
    {
        int count = DirtiestBlockBaseCount(oneBasedWorldTier);
        return celebrationMk10 ? checked(count * 5) : count;
    }
}
