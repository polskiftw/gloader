#if GLOADER_CLIENT
using AuthenticRaces.Core;
using Terraria.DataStructures;

namespace AuthenticRaces.Rendering
{
    internal static class RaceRenderPipeline
    {
        public static void Rewrite(ref PlayerDrawSet drawInfo)
        {
            var player = drawInfo.drawPlayer;
            if (player == null || drawInfo.DrawDataCache == null)
                return;

            var race = RacePlayerState.GetRace(player);
            if (!RaceRendererRegistry.TryGet(race.UpstreamFullName, out var renderer))
                return;

            renderer.Rewrite(ref drawInfo, race, RaceAppearanceState.Get(player));
        }
    }
}
#endif
