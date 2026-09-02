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
/// 1.4.5.8 Large boundary condition, then continue the discrete tier to 5/6.
///
/// This intentionally uses a postfix instead of the old GenerateWorld
/// transpiler. The clean retail source proves the assignment is in Reset(), not
/// GenerateWorld(), and Reset() also performs an earlier initialization write of
/// 2 to the same field. A postfix avoids guessing which field store is the final
/// size-derived one.
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

        // XL/Huge are physically wider than Large, so clean Terraria 1.4.5.8
        // must have selected Large's final multiplier before this continuation.
        if (vanillaMultiplier != 4)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] Expected Terraria 1.4.5.8 Reset() to leave the Large statue multiplier at 4, got " +
                vanillaMultiplier + ". Refusing to infer a changed source rule.");
        }

        switch (ExpandedWorldState.GenerationPreset)
        {
            case ExpandedWorldPreset.XL:
                GenVars.extraBastStatueCountMax = ExpandedWorldStatueTierMath.Multiplier(4);
                break;
            case ExpandedWorldPreset.Huge:
                GenVars.extraBastStatueCountMax = ExpandedWorldStatueTierMath.Multiplier(5);
                break;
        }
    }
}
#endif

/// <summary>
/// Pure source-derived discrete statue multiplier: tiers 1/2/3 are vanilla
/// Small/Medium/Large 2/3/4, therefore tiers 4/5 are XL/Huge 5/6.
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
