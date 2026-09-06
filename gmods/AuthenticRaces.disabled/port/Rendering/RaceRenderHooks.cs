#if GLOADER_CLIENT
using HarmonyLib;
using Terraria.DataStructures;

namespace AuthenticRaces.Rendering
{
    [HarmonyPatch(typeof(PlayerDrawLayers), nameof(PlayerDrawLayers.DrawPlayer_RenderAllLayers))]
    internal static class RaceRenderHooks
    {
        [HarmonyPrefix]
        private static void BeforeRender(ref PlayerDrawSet drawinfo)
        {
            RaceRenderPipeline.Rewrite(ref drawinfo);
        }
    }
}
#endif
