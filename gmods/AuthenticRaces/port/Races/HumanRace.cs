#if GLOADER
using AuthenticRaces.Core;

namespace AuthenticRaces.Races
{
    /// <summary>
    /// The deliberately boring proof race. No tModLoader config, assets, or abilities yet.
    /// If this race can register and receive lifecycle calls, the port spine is alive.
    /// </summary>
    internal sealed class HumanRace : Race
    {
        public override string Name => "Human";
        public override string DisplayName => "Human";
        public override string Description =>
            "Surprisingly durable and resilient, Humans can adapt to any situation.";
    }
}
#endif
