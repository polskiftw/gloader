#if GLOADER
using System;
using System.Reflection;
using HarmonyLib;
using Terraria;

namespace AuthenticRaces.Core
{
    /// <summary>
    /// Direct Terraria hook bridge. Keep this file boring: it translates verified vanilla
    /// lifecycle points into Race calls and contains no race-specific behavior.
    /// </summary>
    [HarmonyPatch]
    internal static class ResetEffectsHook
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Player), "ResetEffects", Type.EmptyTypes)
                ?? throw new MissingMethodException(typeof(Player).FullName, "ResetEffects()");
        }

        [HarmonyPostfix]
        private static void Postfix(Player __instance)
        {
            RacePlayerState.GetRace(__instance).ResetEffects(__instance);
        }
    }

    [HarmonyPatch]
    internal static class PostUpdateHook
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Player), "Update", new[] { typeof(int) })
                ?? throw new MissingMethodException(typeof(Player).FullName, "Update(Int32)");
        }

        [HarmonyPostfix]
        private static void Postfix(Player __instance)
        {
            RacePlayerState.GetRace(__instance).PostUpdate(__instance);
        }
    }
}
#endif
