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

        // Audited 1.4.5-era source shape for The Dirtiest Block:
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
        // We inject directly before the switch result is stored. At that point
        // the int result is already on the evaluation stack, so the helper can
        // transform it without reconstructing compiler locals by index.
        //
        // Any labels/exception blocks attached to the store are moved onto our
        // call. Switch branches may target the join/store instruction; leaving
        // labels there would allow a branch to skip the adjustment entirely.
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
