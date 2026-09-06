using System;
using System.Collections.Generic;
using System.Linq;

internal static class Program
{
    private static int _assertions;

    private static int Main()
    {
        try
        {
            RadioCatalog.Initialize();
            TestTwoSourceInvariant();
            TestRadioMonsterPolicy();
            TestRainwavePolicy();
            TestResolverPolicy();
            TestRemovedProviderStatePruning();
            Console.WriteLine("PASS: Radio two-source policy regressions (" + _assertions + " assertions).");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: " + ex);
            return 1;
        }
    }

    private static void TestTwoSourceInvariant()
    {
        var stations = RadioCatalog.Snapshot();
        Assert(stations.Count == 16, "catalog size is fixed at sixteen");
        Assert(stations.All(s => s.Provider == "radiomonster" || s.Provider == "rainwave"), "only RadioMonster and Rainwave providers exist");
        Assert(stations.All(s => s.Streams.Count > 0), "every station has at least one stream");
        Assert(stations.All(s => s.Streams.All(stream => AllowedConfiguredUrl(stream.Url))), "all configured stream URLs belong to the two providers");
        Assert(stations.All(s => string.IsNullOrWhiteSpace(s.MetadataUrl) || AllowedConfiguredUrl(s.MetadataUrl)), "all configured metadata URLs belong to the two providers");
    }

    private static void TestRadioMonsterPolicy()
    {
        var stations = RadioCatalog.Snapshot().Where(s => s.Provider == "radiomonster").ToList();
        Assert(stations.Count == 10, "RadioMonster exposes ten built-in channels");
        Assert(stations.Select(s => s.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 10, "RadioMonster IDs are unique");
        Assert(stations.All(s => s.MetadataMode == MetadataMode.Icy), "RadioMonster metadata is stream ICY only");
        Assert(stations.All(s => s.Streams.Count == 3), "RadioMonster has ultra/high/mobile variants");
        Assert(stations.All(s =>
        {
            var ranked = StreamRanking.Rank(s.Streams);
            return ranked.Count == 3 && ranked[0].BitrateKbps == 320 && ranked[0].Codec == "mp3" && ranked[0].Url.EndsWith("/ultra", StringComparison.OrdinalIgnoreCase);
        }), "RadioMonster always prefers official 320k MP3 Ultra");
    }

    private static void TestRainwavePolicy()
    {
        var stations = RadioCatalog.Snapshot().Where(s => s.Provider == "rainwave").ToList();
        Assert(stations.Count == 6, "Rainwave exposes six built-in stations");
        Assert(stations.Select(s => s.Id).OrderBy(x => x).SequenceEqual(new[] { "rainwave:1", "rainwave:2", "rainwave:3", "rainwave:4", "rainwave:5", "rainwave:6" }), "Rainwave station IDs are exactly 1 through 6");
        Assert(stations.All(s => s.MetadataMode == MetadataMode.Rainwave), "Rainwave stations use Rainwave metadata mode");
        Assert(stations.All(s => s.MetadataUrl.StartsWith("https://rainwave.cc/api4/info?sid=", StringComparison.OrdinalIgnoreCase)), "Rainwave fallback metadata uses official anonymous info API");
        Assert(stations.All(s => s.Streams.Count == 1 && s.Streams[0].Resolver == "rainwave"), "Rainwave playback uses only official tune-in playlist resolver");
        Assert(Math.Abs(RadioMetadata.RainwaveScheduleFallbackDelaySeconds - 10.0) < 0.001, "Rainwave schedule fallback alignment remains ten seconds");
    }

    private static void TestResolverPolicy()
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "direct", "playlist", "rainwave", string.Empty };
        var resolvers = RadioCatalog.Snapshot().SelectMany(s => s.Streams).Select(s => s.Resolver ?? string.Empty).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Assert(resolvers.All(allowed.Contains), "catalog does not use directory/page/browser resolvers");
        Assert(!resolvers.Contains("radio-browser-exact", StringComparer.OrdinalIgnoreCase), "Radio Browser resolver is absent");
        Assert(!resolvers.Contains("station-page", StringComparer.OrdinalIgnoreCase), "station-page scraper resolver is absent");
    }

    private static void TestRemovedProviderStatePruning()
    {
        var state = new RadioState { SelectedStationId = "laut:old" };
        state.Favorites.Add("113fm:old");
        state.Favorites.Add("rainwave:5");
        state.Recents.Add("radio-browser:old");
        state.Recents.Add("radiomonster:90s");
        RadioPersistence.PruneToCatalog(state, RadioCatalog.Snapshot());

        Assert(state.SelectedStationId == "rainwave:5", "removed selected provider falls back to Rainwave All");
        Assert(!state.Favorites.Contains("113fm:old") && state.Favorites.Contains("rainwave:5"), "removed-provider favorites are pruned without losing valid favorites");
        Assert(!state.Recents.Contains("radio-browser:old") && state.Recents.Contains("radiomonster:90s"), "removed-provider recents are pruned without losing valid recents");
    }

    private static bool AllowedConfiguredUrl(string value)
    {
        Uri uri;
        if (!Uri.TryCreate(value, UriKind.Absolute, out uri)) return false;
        var host = uri.Host.ToLowerInvariant();
        return host == "rainwave.cc" || host.EndsWith(".rainwave.cc", StringComparison.Ordinal) ||
               host == "radiomonster.fm" || host.EndsWith(".radiomonster.fm", StringComparison.Ordinal);
    }

    private static void Assert(bool condition, string message)
    {
        _assertions++;
        if (!condition) throw new InvalidOperationException("Assertion failed: " + message);
    }
}
