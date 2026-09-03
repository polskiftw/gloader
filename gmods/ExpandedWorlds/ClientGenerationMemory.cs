#if GLOADER_CLIENT
using System;
using HarmonyLib;
using Terraria;

/// <summary>
/// Keep the client-only WorldMap backing array out of the world-generation peak.
///
/// Terraria 1.4.5.8 does not consume Main.Map while WorldGen.generatingWorld is
/// true. The map is needed later, when the generated world is actually loaded.
/// ExpandedWorldBackingStorage normally grows it from the clearWorld prefix so
/// expanded .wld loads and multiplayer joins have enough backing storage before
/// Main.Map.Load runs. During creation, however, allocating the expanded map at
/// that same point needlessly competes with clearWorld while it constructs one
/// Tile object for every world cell.
///
/// THICC's 16,800 x 4,800 MapTile canvas is roughly 323 MB by itself. Deferring
/// that client-only allocation until a non-generation clearWorld substantially
/// lowers the 32-bit host's peak without changing the generated world or save
/// format. The existing EnsureClientMapStorage call remains authoritative for
/// normal world loads and multiplayer joins.
/// </summary>
[HarmonyPatch(typeof(ExpandedWorldBackingStorage), "EnsureClientMapStorage")]
internal static class ExpandedWorldClientMapGenerationDeferralPatch
{
    private static bool _loggedThisGeneration;

    [HarmonyPrefix]
    private static bool Prefix()
    {
        if (!WorldGen.generatingWorld)
        {
            _loggedThisGeneration = false;
            return true;
        }

        if (!_loggedThisGeneration)
        {
            Console.WriteLine(
                "[Expanded Worlds] clearWorld: deferring client map backing during world generation at " +
                Main.maxTilesX + "x" + Main.maxTilesY +
                " to reduce x86 peak memory.");
            _loggedThisGeneration = true;
        }

        return false;
    }
}
#endif
