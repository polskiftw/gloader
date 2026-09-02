using System;

#if GLOADER_CLIENT
using HarmonyLib;
using Terraria;
using Terraria.WorldBuilding;

/// <summary>
/// Current Terraria 1.4.5.8 computes the world-size statue multiplier near the
/// end of WorldGen.Reset():
///
///   Small  = 2 + 0
///   Medium = 2 + 1
///   Large  = 2 + 2
///
/// Expanded worlds satisfy the Large threshold and therefore leave vanilla
/// Reset() with 4. Let Terraria finish that calculation first, verify the clean
/// 1.4.5.8 Large boundary condition, then continue the source-backed width tier.
/// XL uses 5; Huge and THICC both use 6 because they share the same 16,800-tile
/// horizontal quantum. THICC's extra height is not used to invent a new
/// categorical statue term.
/// </summary>
[HarmonyPatch(typeof(WorldGen), nameof(WorldGen.Reset))]
internal static class ExpandedWorldStatueMultiplierPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        if (!ExpandedWorldState.GenerationArmed)
            return;

        int vanillaMultiplier = GenVars.extraBastStatueCountMax;

        if (vanillaMultiplier != 4)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] Expected Terraria 1.4.5.8 Reset() to leave the Large statue multiplier at 4, got " +
                vanillaMultiplier + ". Refusing to infer a changed source rule.");
        }

        switch (ExpandedWorldState.GenerationPreset)
        {
            case ExpandedWorldPreset.XL:
            case ExpandedWorldPreset.Huge:
            case ExpandedWorldPreset.Thicc:
                GenVars.extraBastStatueCountMax = ExpandedWorldStatueTierMath.Multiplier(
                    ExpandedWorldState.DiscreteTierFor(ExpandedWorldState.GenerationPreset));
                break;
        }
    }
}
#endif

/// <summary>
/// Pure source-derived discrete statue multiplier: tiers 1/2/3 are vanilla
/// Small/Medium/Large 2/3/4, therefore width tiers 4/5 are XL/Huge 5/6.
/// THICC intentionally reuses tier 5.
/// </summary>
internal static class ExpandedWorldStatueTierMath
{
    public static int Multiplier(int oneBasedWorldTier)
    {
        if (oneBasedWorldTier < 1)
            throw new ArgumentOutOfRangeException(nameof(oneBasedWorldTier));

        return checked(oneBasedWorldTier + 1);
    }
}