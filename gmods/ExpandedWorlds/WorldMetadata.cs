#if GLOADER_CLIENT
using System;
using System.Reflection;
using HarmonyLib;
using Terraria.IO;

/// <summary>
/// WorldFileData's vanilla full-seed prefix recognizes only the three exact
/// Small/Medium/Large dimension pairs. Expanded Worlds intentionally keeps XL,
/// Huge and THICC categorically Large, so their copied/displayed full seed must
/// use the vanilla Large prefix (3), not Unknown (0).
/// </summary>
[HarmonyPatch(typeof(WorldFileData), nameof(WorldFileData.GetFullSeedText))]
internal static class ExpandedWorldFullSeedPatch
{
    [HarmonyPostfix]
    private static void Postfix(WorldFileData __instance, ref string __result)
    {
        if (!IsExpandedLarge(__instance))
            return;

        if (string.IsNullOrEmpty(__result))
            throw new InvalidOperationException("[Expanded Worlds] WorldFileData.GetFullSeedText returned an empty value.");

        int firstDot = __result.IndexOf('.');
        if (firstDot <= 0)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] WorldFileData.GetFullSeedText format changed; refusing to guess the size prefix.");
        }

        string prefix = __result.Substring(0, firstDot);
        if (prefix == "3")
            return;

        if (prefix != "0")
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] Unexpected full-seed world-size prefix '" + prefix +
                "' for " + __instance.WorldSizeX + "x" + __instance.WorldSizeY + ".");
        }

        __result = "3" + __result.Substring(firstDot);
    }

    private static bool IsExpandedLarge(WorldFileData data)
    {
        return data != null &&
               ExpandedWorldMath.IsExpandedPresetDimensions(data.WorldSizeX, data.WorldSizeY);
    }
}

/// <summary>
/// Vanilla labels any nonstandard physical dimensions as "Unknown". This is only
/// presentation state; expose the three explicit Expanded Worlds presets by name
/// while leaving all gameplay/category logic at vanilla Large.
/// </summary>
[HarmonyPatch]
internal static class ExpandedWorldSizeNamePatch
{
    private static MethodBase TargetMethod()
    {
        MethodBase method = AccessTools.PropertyGetter(typeof(WorldFileData), nameof(WorldFileData.WorldSizeName));
        if (method == null)
            throw new MissingMethodException(typeof(WorldFileData).FullName, "get_WorldSizeName");
        return method;
    }

    [HarmonyPostfix]
    private static void Postfix(WorldFileData __instance, ref string __result)
    {
        if (__instance == null)
            return;

        int width = __instance.WorldSizeX;
        int height = __instance.WorldSizeY;

        if (width == ExpandedWorldMath.XLWidth && height == ExpandedWorldMath.XLHeight)
            __result = "XL";
        else if (width == ExpandedWorldMath.HugeWidth && height == ExpandedWorldMath.HugeHeight)
            __result = "Huge";
        else if (width == ExpandedWorldMath.ThiccWidth && height == ExpandedWorldMath.ThiccHeight)
            __result = "THICC";
    }
}
#endif