#if GLOADER
using System;
using System.Runtime.CompilerServices;
using Terraria;

namespace AuthenticRaces.Core
{
    /// <summary>
    /// Per-Player race state without modifying Terraria.Player or depending on ModPlayer.
    /// Persistence/network serialization will sit on top of this state instead of being mixed into it.
    /// </summary>
    internal static class RacePlayerState
    {
        private static ConditionalWeakTable<Player, State> States =
            new ConditionalWeakTable<Player, State>();

        public static void ResetAll()
        {
            States = new ConditionalWeakTable<Player, State>();
        }

        public static Race GetRace(Player player)
        {
            if (player == null)
                throw new ArgumentNullException(nameof(player));

            var state = States.GetOrCreateValue(player);
            if (!RaceRegistry.TryGet(state.RaceId, out var race))
            {
                race = RaceRegistry.DefaultRace;
                state.RaceId = race.Id;
            }

            return race;
        }

        public static bool TrySetRace(Player player, int raceId)
        {
            if (!RaceRegistry.TryGet(raceId, out var race))
                return false;

            SetRace(player, race);
            return true;
        }

        public static bool TrySetRace(Player player, string raceName)
        {
            if (!RaceRegistry.TryGet(raceName, out var race))
                return false;

            SetRace(player, race);
            return true;
        }

        public static void SetRace(Player player, Race race)
        {
            if (player == null)
                throw new ArgumentNullException(nameof(player));
            if (race == null)
                throw new ArgumentNullException(nameof(race));

            var state = States.GetOrCreateValue(player);
            var previous = GetRace(player);
            if (previous.Id == race.Id)
                return;

            previous.PreRaceChange(player);
            state.RaceId = race.Id;
            race.PostRaceChange(player);
        }

        private sealed class State
        {
            // Human is deliberately registered first. If the registry is ever reordered,
            // GetRace still validates this value and falls back to DefaultRace.
            public int RaceId;
        }
    }
}
#endif
