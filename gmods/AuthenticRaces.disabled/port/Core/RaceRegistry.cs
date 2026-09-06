#if GLOADER
using System;
using System.Collections.Generic;
using AuthenticRaces.Races;

namespace AuthenticRaces.Core
{
    /// <summary>
    /// Deterministic race registry replacing tModLoader's ModType registration path.
    /// Registration order defines stable in-process numeric IDs; string lookup also preserves upstream names.
    /// </summary>
    internal static class RaceRegistry
    {
        private static readonly List<Race> Races = new List<Race>();
        private static readonly Dictionary<string, Race> ByName =
            new Dictionary<string, Race>(StringComparer.OrdinalIgnoreCase);

        public static int Count => Races.Count;
        public static Race DefaultRace { get; private set; }

        public static void Initialize()
        {
            Races.Clear();
            ByName.Clear();

            DefaultRace = Register(new HumanRace());
        }

        public static Race Register(Race race)
        {
            if (race == null)
                throw new ArgumentNullException(nameof(race));
            if (string.IsNullOrWhiteSpace(race.Name))
                throw new InvalidOperationException("A race must have a non-empty name.");
            if (ByName.ContainsKey(race.Name) || ByName.ContainsKey(race.UpstreamFullName))
                throw new InvalidOperationException("Duplicate race registration: " + race.Name);

            race.Id = Races.Count;
            Races.Add(race);
            ByName.Add(race.Name, race);
            ByName.Add(race.UpstreamFullName, race);
            return race;
        }

        public static bool TryGet(int id, out Race race)
        {
            if (id >= 0 && id < Races.Count)
            {
                race = Races[id];
                return true;
            }

            race = null;
            return false;
        }

        public static bool TryGet(string name, out Race race)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                race = null;
                return false;
            }

            return ByName.TryGetValue(name, out race);
        }
    }
}
#endif
