#if GLOADER_CLIENT
using System;
using System.Collections.Generic;

namespace AuthenticRaces.Rendering
{
    internal static class RaceRendererRegistry
    {
        private static readonly Dictionary<string, RaceRenderer> Renderers =
            new Dictionary<string, RaceRenderer>(StringComparer.OrdinalIgnoreCase);

        public static void Initialize()
        {
            Renderers.Clear();
            Register("MrPlagueRaces/Human", PassthroughRaceRenderer.Instance);
        }

        public static void Register(string raceIdentity, RaceRenderer renderer, bool replace = false)
        {
            if (string.IsNullOrWhiteSpace(raceIdentity))
                throw new ArgumentException("Race renderer identity cannot be empty.", nameof(raceIdentity));
            if (renderer == null)
                throw new ArgumentNullException(nameof(renderer));

            if (!replace && Renderers.ContainsKey(raceIdentity))
                throw new InvalidOperationException("A renderer is already registered for '" + raceIdentity + "'.");

            Renderers[raceIdentity] = renderer;
        }

        public static bool TryGet(string raceIdentity, out RaceRenderer renderer)
        {
            if (string.IsNullOrWhiteSpace(raceIdentity))
            {
                renderer = null;
                return false;
            }

            return Renderers.TryGetValue(raceIdentity, out renderer);
        }
    }
}
#endif
