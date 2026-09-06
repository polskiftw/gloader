#if GLOADER_CLIENT
using AuthenticRaces.Core;
using Terraria.DataStructures;

namespace AuthenticRaces.Rendering
{
    /// <summary>
    /// Client-only adapter that rewrites Terraria's already-positioned player draw records.
    /// Renderers should preserve vanilla ordering/transforms and only replace or expand
    /// records owned by the selected race.
    /// </summary>
    internal abstract class RaceRenderer
    {
        public abstract void Rewrite(
            ref PlayerDrawSet drawInfo,
            Race race,
            RaceAppearanceData appearance);
    }

    internal sealed class PassthroughRaceRenderer : RaceRenderer
    {
        public static readonly PassthroughRaceRenderer Instance = new PassthroughRaceRenderer();

        private PassthroughRaceRenderer()
        {
        }

        public override void Rewrite(
            ref PlayerDrawSet drawInfo,
            Race race,
            RaceAppearanceData appearance)
        {
            // Human is deliberately a no-op proof renderer. Vanilla remains authoritative
            // until an actual race renderer explicitly substitutes one of its draw records.
        }
    }
}
#endif
