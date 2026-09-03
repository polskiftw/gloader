using System;

#if GLOADER
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
#endif

/// <summary>
/// Documented, evidence-backed continuations where the three vanilla terms are
/// not arithmetically unique in isolation, but the surrounding 1.4.5.8 size
/// system supplies a strong independent rule.
///
/// These are intentionally separate from the exact arithmetic continuations in
/// DungeonTierContinuity.cs so the distinction remains visible in code review.
/// </summary>
internal static class ExpandedWorldInferredTierMath
{
    /// <summary>
    /// Terraria 1.4.5.8: Small/Medium/Large = 8/14/18.
    ///
    /// Vertical network sections are 8/12/16. Medium and Large are exactly
    /// verticalSections + 2, while Small is the compact-world exception. The
    /// canonical expanded tiers continue vertical sections as 20/24/28.
    /// </summary>
    public static int EvilOrbHeartQuota(int oneBasedWorldTier)
    {
        if (oneBasedWorldTier < 1)
            throw new ArgumentOutOfRangeException(nameof(oneBasedWorldTier));

        if (oneBasedWorldTier == 1)
            return 8;

        return checked(4 * oneBasedWorldTier + 6);
    }

    /// <summary>
    /// Terraria 1.4.5.8: Small/Medium/Large = 2/6/8.
    ///
    /// Vertical network sections are 8/12/16. Medium and Large are exactly
    /// verticalSections / 2, while Small is the compact-world exception.
    /// </summary>
    public static int SpiderSpecializedRoomCount(int oneBasedWorldTier)
    {
        if (oneBasedWorldTier < 1)
            throw new ArgumentOutOfRangeException(nameof(oneBasedWorldTier));

        if (oneBasedWorldTier == 1)
            return 2;

        return checked(2 * oneBasedWorldTier + 2);
    }

    /// <summary>
    /// User-selected conservative continuation for the Lihzahrd painting cap.
    /// Vanilla Large consumes one Next(2) roll and produces 2 or 3. Expanded
    /// worlds preserve that exact RNG roll and shift only its result to 3 or 4.
    /// This is deliberately flat across XL/Huge/THICC rather than pretending a
    /// tier formula is known.
    /// </summary>
    public static int ExpandedLihzahrdPaintingMaxFromVanillaLarge(int vanillaLargeRandomizedMax)
    {
        if (vanillaLargeRandomizedMax != 2 && vanillaLargeRandomizedMax != 3)
        {
            throw new ArgumentOutOfRangeException(
                nameof(vanillaLargeRandomizedMax),
                vanillaLargeRandomizedMax,
                "Expected Terraria Large Lihzahrd painting max 2 or 3.");
        }

        return vanillaLargeRandomizedMax + 1;
    }
}

#if GLOADER
internal static class ExpandedWorldInferredTierPatchUtil
{
    internal static IEnumerable<CodeInstruction> InjectAfterUniqueLargeAssignment(
        IEnumerable<CodeInstruction> instructions,
        MethodBase original,
        string featureName,
        int expectedLargeValue,
        MethodInfo adjustMethod,
        int worldSizeCallOccurrence = 1,
        int scanWindow = 160)
    {
        var code = instructions.ToList();
        int start = FindNthWorldSizeCall(code, worldSizeCallOccurrence);
        if (start < 0)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] " + featureName + " no longer contains the audited WorldGen.GetWorldSize call.");
        }

        int end = Math.Min(code.Count - 1, start + scanWindow);
        var matches = new List<int>();

        for (int i = start + 1; i < end; i++)
        {
            if (ExpandedWorldDungeonTierPatchUtil.IsIntConstant(code[i], expectedLargeValue) &&
                ExpandedWorldDungeonTierPatchUtil.IsLocalStore(code[i + 1]))
            {
                matches.Add(i + 1);
            }
        }

        if (matches.Count != 1)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] " + featureName + " source shape changed in " +
                (original?.DeclaringType?.FullName ?? "<unknown>") + "." +
                (original?.Name ?? "<unknown>") + ": expected exactly one Large assignment of " +
                expectedLargeValue + ", found " + matches.Count + ". Refusing to guess.");
        }

        InsertCallBeforeStore(code, matches[0], adjustMethod);
        return code;
    }

    internal static IEnumerable<CodeInstruction> AdjustEveryStaticFieldStore(
        IEnumerable<CodeInstruction> instructions,
        MethodBase original,
        FieldInfo field,
        MethodInfo adjustMethod,
        int expectedStoreCount,
        string featureName)
    {
        var code = instructions.ToList();
        var stores = new List<int>();

        for (int i = 0; i < code.Count; i++)
        {
            if (code[i].opcode == OpCodes.Stsfld && Equals(code[i].operand, field))
                stores.Add(i);
        }

        if (stores.Count != expectedStoreCount)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] " + featureName + " source shape changed in " +
                (original?.DeclaringType?.FullName ?? "<unknown>") + "." +
                (original?.Name ?? "<unknown>") + ": expected " + expectedStoreCount +
                " stores, found " + stores.Count + ". Refusing to guess.");
        }

        for (int i = stores.Count - 1; i >= 0; i--)
            InsertCallBeforeStore(code, stores[i], adjustMethod);

        return code;
    }

    private static int FindNthWorldSizeCall(List<CodeInstruction> code, int occurrence)
    {
        int seen = 0;
        for (int i = 0; i < code.Count; i++)
        {
            CodeInstruction instruction = code[i];
            if ((instruction.opcode != OpCodes.Call && instruction.opcode != OpCodes.Callvirt) ||
                !Equals(instruction.operand, ExpandedWorldDungeonTierPatchUtil.GetWorldSizeMethod))
            {
                continue;
            }

            if (++seen == occurrence)
                return i;
        }

        return -1;
    }

    private static void InsertCallBeforeStore(List<CodeInstruction> code, int storeIndex, MethodInfo adjustMethod)
    {
        var call = new CodeInstruction(OpCodes.Call, adjustMethod);
        call.labels.AddRange(code[storeIndex].labels);
        code[storeIndex].labels.Clear();
        call.blocks.AddRange(code[storeIndex].blocks);
        code[storeIndex].blocks.Clear();
        code.Insert(storeIndex, call);
    }
}

/// <summary>
/// Promote the 8/14/18 evil-room quota using the cross-checked vertical-section
/// rule. Priority.Last is intentional: the exact-sequence Dual Dungeon patch
/// runs first, leaving the formerly-null 18 assignment available here.
/// </summary>
[HarmonyPatch]
internal static class ExpandedWorldInferredEvilOrbHeartQuotaPatch
{
    private const string TypeName = "Terraria.GameContent.Generation.Dungeon.Features.DungeonGlobalEarlyDualDungeonFeatures";
    private static readonly MethodInfo Adjust =
        ExpandedWorldDungeonTierPatchUtil.RequireOwnMethod(typeof(ExpandedWorldInferredEvilOrbHeartQuotaPatch), nameof(AdjustCount));

    private static MethodBase TargetMethod() =>
        ExpandedWorldDungeonTierPatchUtil.RequireMethod(TypeName, "EarlyDungeonFeatures");

    [HarmonyTranspiler]
    [HarmonyPriority(Priority.Last)]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original) =>
        ExpandedWorldInferredTierPatchUtil.InjectAfterUniqueLargeAssignment(
            instructions,
            original,
            "Early Dual Dungeon Shadow Orb / Crimson Heart quota",
            18,
            Adjust);

    private static int AdjustCount(int vanillaValue)
    {
        if (!ExpandedWorldGenerationContext.IsActive)
            return vanillaValue;

        if (vanillaValue != 18)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] Expected Terraria Large evil Orb/Heart quota 18, got " + vanillaValue + ".");
        }

        return ExpandedWorldInferredTierMath.EvilOrbHeartQuota(ExpandedWorldGenerationContext.ActiveTier);
    }
}

/// <summary>
/// Promote the 2/6/8 Spider specialized-room quota using the cross-checked
/// verticalSections/2 rule from Medium upward.
/// </summary>
[HarmonyPatch]
internal static class ExpandedWorldInferredSpiderRoomQuotaPatch
{
    private const string TypeName = "Terraria.GameContent.Generation.Dungeon.LayoutProviders.DualDungeonLayoutProvider";
    private static readonly MethodInfo Adjust =
        ExpandedWorldDungeonTierPatchUtil.RequireOwnMethod(typeof(ExpandedWorldInferredSpiderRoomQuotaPatch), nameof(AdjustCount));

    private static MethodBase TargetMethod() =>
        ExpandedWorldDungeonTierPatchUtil.RequireMethod(TypeName, "ConvertSpecializedRooms");

    [HarmonyTranspiler]
    [HarmonyPriority(Priority.Last)]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original) =>
        ExpandedWorldInferredTierPatchUtil.InjectAfterUniqueLargeAssignment(
            instructions,
            original,
            "Dual Dungeon Spider specialized-room quota",
            8,
            Adjust);

    private static int AdjustCount(int vanillaValue)
    {
        if (!ExpandedWorldGenerationContext.IsActive)
            return vanillaValue;

        if (vanillaValue != 8)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] Expected Terraria Large Spider-room quota 8, got " + vanillaValue + ".");
        }

        return ExpandedWorldInferredTierMath.SpiderSpecializedRoomCount(ExpandedWorldGenerationContext.ActiveTier);
    }
}

/// <summary>
/// Preserve Terraria Large's existing Next(2) roll for the Lihzahrd painting
/// cap, then shift the already-randomized 2/3 result to 3/4 on every expanded
/// tier. No additional RNG call is introduced.
/// </summary>
[HarmonyPatch]
internal static class ExpandedWorldLihzahrdPaintingCapPatch
{
    private const string TypeName = "Terraria.GameContent.Generation.Dungeon.Features.DungeonGlobalPaintings";
    private static readonly Type TargetType = ExpandedWorldDungeonTierPatchUtil.RequireType(TypeName);
    private static readonly FieldInfo PaintingMaxField =
        AccessTools.Field(TargetType, "lihzahrdPaintingsMax")
        ?? throw new MissingFieldException(TypeName, "lihzahrdPaintingsMax");
    private static readonly MethodInfo Adjust =
        ExpandedWorldDungeonTierPatchUtil.RequireOwnMethod(typeof(ExpandedWorldLihzahrdPaintingCapPatch), nameof(AdjustPaintingMax));

    private static MethodBase TargetMethod() =>
        ExpandedWorldDungeonTierPatchUtil.RequireMethod(TypeName, "Paintings");

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original) =>
        ExpandedWorldInferredTierPatchUtil.AdjustEveryStaticFieldStore(
            instructions,
            original,
            PaintingMaxField,
            Adjust,
            expectedStoreCount: 3,
            featureName: "Lihzahrd painting cap");

    private static int AdjustPaintingMax(int vanillaValue)
    {
        if (!ExpandedWorldGenerationContext.IsActive)
            return vanillaValue;

        return ExpandedWorldInferredTierMath.ExpandedLihzahrdPaintingMaxFromVanillaLarge(vanillaValue);
    }
}
#endif
