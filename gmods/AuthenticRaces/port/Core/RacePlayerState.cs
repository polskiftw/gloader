#if GLOADER
using System;
using System.Runtime.CompilerServices;
using Terraria;

namespace AuthenticRaces.Core
{
    /// <summary>
    /// Per-Player race state without modifying Terraria.Player or depending on ModPlayer.
    /// Persistence/network serialization sits on top of this state instead of being mixed into it.
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

            var state = GetState(player);
            if (!RaceRegistry.TryGet(state.RaceId, out var race))
            {
                race = RaceRegistry.DefaultRace;
                state.RaceId = race.Id;
            }

            return race;
        }

        public static string GetPersistedRaceName(Player player)
        {
            if (player == null)
                throw new ArgumentNullException(nameof(player));

            var state = GetState(player);
            return string.IsNullOrWhiteSpace(state.PersistedRaceName)
                ? GetRace(player).UpstreamFullName
                : state.PersistedRaceName;
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

            var state = GetState(player);
            var previous = GetRace(player);
            if (previous.Id == race.Id)
            {
                // A user explicitly choosing the current fallback race should replace any
                // unresolved saved identity rather than preserving it forever.
                state.PersistedRaceName = race.UpstreamFullName;
                return;
            }

            previous.PreRaceChange(player);
            state.RaceId = race.Id;
            state.PersistedRaceName = race.UpstreamFullName;
            race.PostRaceChange(player);
        }

        /// <summary>
        /// Restores serialized state without firing race-change behavior. Upstream LoadData
        /// assigns the saved race directly as well; loading a character is not a live race switch.
        /// Unknown identities are retained so an incomplete port does not destroy future race data.
        /// </summary>
        public static bool TryRestoreRace(Player player, string raceName)
        {
            if (player == null)
                throw new ArgumentNullException(nameof(player));

            var state = GetState(player);
            state.PersistedRaceName = raceName;

            if (!RaceRegistry.TryGet(raceName, out var race))
            {
                state.RaceId = RaceRegistry.DefaultRace.Id;
                return false;
            }

            state.RaceId = race.Id;
            state.PersistedRaceName = race.UpstreamFullName;
            return true;
        }

        public static void RestoreDefaultRace(Player player)
        {
            if (player == null)
                throw new ArgumentNullException(nameof(player));

            var state = GetState(player);
            state.RaceId = RaceRegistry.DefaultRace.Id;
            state.PersistedRaceName = RaceRegistry.DefaultRace.UpstreamFullName;
        }

        private static State GetState(Player player)
        {
            return States.GetValue(player, _ => new State());
        }

        private sealed class State
        {
            // Human is deliberately registered first. If the registry is ever reordered,
            // GetRace still validates this value and falls back to DefaultRace.
            public int RaceId;
            public string PersistedRaceName;
        }
    }
}
#endif
