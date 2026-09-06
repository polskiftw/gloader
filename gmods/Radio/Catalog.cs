#if !GLOADER_SERVER
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

internal static class RadioCatalog
{
    private static readonly object Sync = new object();
    private static readonly List<Station> Stations = new List<Station>();
    private static readonly Dictionary<string, Station> StationsById = new Dictionary<string, Station>(StringComparer.OrdinalIgnoreCase);

    internal static void Initialize()
    {
        lock (Sync)
        {
            Stations.Clear();
            StationsById.Clear();
            foreach (var station in RadioMonsterStations().Concat(RainwaveStations()))
            {
                Stations.Add(station);
                StationsById[station.Id] = station;
            }
        }
    }

    internal static List<Station> Snapshot()
    {
        lock (Sync) return Stations.ToList();
    }

    internal static Station Find(string id)
    {
        lock (Sync)
        {
            Station station;
            return !string.IsNullOrWhiteSpace(id) && StationsById.TryGetValue(id, out station) ? station : null;
        }
    }

    private static IEnumerable<Station> RadioMonsterStations()
    {
        var rows = new[]
        {
            new[] { "tophits", "Tophits", "Current chart hits" },
            new[] { "dance", "Dance", "Dance & club" },
            new[] { "evergreens", "Evergreens", "Evergreens" },
            new[] { "rock", "Rock", "Rock" },
            new[] { "rnb", "R&B", "R&B" },
            new[] { "schlager", "Schlager", "Schlager" },
            new[] { "deutsch", "Deutsch", "German hits" },
            new[] { "80s", "80's", "1980s" },
            new[] { "90s", "90's", "1990s" },
            new[] { "2000s", "2000's", "2000s" }
        };

        foreach (var row in rows)
        {
            var slug = row[0];
            var station = Create("radiomonster:" + slug, row[1], "radiomonster", "RadioMonster.fm", "https://www.radiomonster.fm/", row[2]);
            station.MetadataMode = MetadataMode.Icy;
            station.Streams.Add(Variant("https://" + slug + ".radiomonster.fm/ultra", "mp3", 320, "direct", "Ultra · 320k MP3"));
            station.Streams.Add(Variant("https://" + slug + ".radiomonster.fm/high", "aac", 128, "direct", "High · 128k AAC"));
            station.Streams.Add(Variant("https://" + slug + ".radiomonster.fm/mobile", "aac", 64, "direct", "Mobile · 64k AAC+"));
            yield return station;
        }
    }

    private static IEnumerable<Station> RainwaveStations()
    {
        var rows = new[]
        {
            new object[] { 1, "Game", "Game music" },
            new object[] { 2, "OC ReMix", "OC ReMix" },
            new object[] { 3, "Covers", "Game music covers" },
            new object[] { 4, "Chiptunes", "Chiptunes" },
            new object[] { 5, "All", "All Rainwave channels" },
            new object[] { 6, "Chill", "Chill game music" }
        };

        foreach (var row in rows)
        {
            var sid = (int)row[0];
            var station = Create(
                "rainwave:" + sid.ToString(CultureInfo.InvariantCulture),
                (string)row[1],
                "rainwave",
                "Rainwave",
                "https://rainwave.cc/",
                (string)row[2]);
            station.MetadataMode = MetadataMode.Rainwave;
            station.MetadataUrl = "https://rainwave.cc/api4/info?sid=" + sid.ToString(CultureInfo.InvariantCulture);
            station.Streams.Add(Variant(
                "https://rainwave.cc/tune_in/" + sid.ToString(CultureInfo.InvariantCulture) + ".mp3.m3u",
                "mp3",
                192,
                "rainwave",
                "Rainwave MP3",
                sid.ToString(CultureInfo.InvariantCulture)));
            yield return station;
        }
    }

    internal static Station Create(string id, string name, string provider, string providerDisplay, string homePage, string description)
    {
        return new Station
        {
            Id = id ?? string.Empty,
            Name = name ?? string.Empty,
            Provider = provider ?? string.Empty,
            ProviderDisplay = providerDisplay ?? string.Empty,
            HomePage = homePage ?? string.Empty,
            Description = description ?? string.Empty
        };
    }

    internal static StreamVariant Variant(string url, string codec, int bitrate, string resolver, string label, string resolverArgument = "")
    {
        return new StreamVariant
        {
            Url = url ?? string.Empty,
            Codec = codec ?? string.Empty,
            BitrateKbps = bitrate,
            Resolver = resolver ?? string.Empty,
            ResolverArgument = resolverArgument ?? string.Empty,
            Label = label ?? string.Empty
        };
    }
}
#endif
