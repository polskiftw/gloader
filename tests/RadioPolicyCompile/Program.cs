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
            TestAdvertisedNightridePolicy();
            TestDecadeSubcategory();
            TestAliasedSearch();
            TestBrowseRanking();
            TestSceneSatFallbackRanking();
            Console.WriteLine("PASS: Radio catalog/browser policy regressions (" + _assertions + " assertions).");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: " + ex);
            return 1;
        }
    }

    private static void TestAdvertisedNightridePolicy()
    {
        const string html = "<a href='/?station=nightride'>Nightride</a>" +
                            "<a href='/?station=chillsynth'>Chillsynth</a>" +
                            "<a href='/?station=rekt'>REKT</a>" +
                            "<a href='/?station=rektory'>REKTory</a>" +
                            "<a href='/?station=archives'>Archives</a>";
        var mounts = RadioCatalog.ParseAdvertisedNightrideMounts(html);
        Assert(mounts.SetEquals(new[] { "nightride", "chillsynth", "rekt", "rektory" }), "advertised Nightride mount parsing excludes Archives");
        Assert(!mounts.Contains("rektify"), "mystery Icecast mounts are not invented by advertised parser");
    }

    private static void TestDecadeSubcategory()
    {
        var station = RadioCatalog.One("test:80s-rap", "Old School Mix", "test", "Test", "https://example.invalid", "Hip-Hop").AddDecades(1980);
        Assert(RadioTaxonomy.Matches(station, "Hip-Hop", "1980s", 0, "", new HashSet<string>(), new List<string>()), "1980s subcategory checks station decade metadata");
        Assert(!RadioTaxonomy.Matches(station, "Hip-Hop", "1990s", 0, "", new HashSet<string>(), new List<string>()), "wrong decade subcategory is rejected");
    }

    private static void TestAliasedSearch()
    {
        var rap = RadioCatalog.One("test:rap", "Old School Mix", "test", "Test", "https://example.invalid", "Hip-Hop").AddDecades(1980);
        var rock = RadioCatalog.One("test:rock", "Eighties Rock", "test", "Test", "https://example.invalid", "Rock").AddDecades(1980);
        Assert(RadioTaxonomy.Matches(rap, "Everything", 0, "80s rap", new HashSet<string>(), new List<string>()), "80s rap free-text query maps rap to Hip-Hop");
        Assert(!RadioTaxonomy.Matches(rock, "Everything", 0, "80s rap", new HashSet<string>(), new List<string>()), "80s rap query does not accept unrelated 80s Rock");
        Assert(RadioTaxonomy.SearchScore(rap, "80s rap") > RadioTaxonomy.SearchScore(rock, "80s rap"), "matching decade/tag search ranks ahead of partial match");
    }

    private static void TestBrowseRanking()
    {
        var favorite = RadioCatalog.One("test:fav", "Favorite", "test", "Test", "https://example.invalid");
        var verifiedBuiltIn = RadioCatalog.One("test:verified", "Verified", "test", "Test", "https://example.invalid");
        verifiedBuiltIn.MetadataVerified = true;
        var live = RadioCatalog.One("test:live", "Live", "test", "Test", "https://example.invalid");
        live.BuiltIn = false;
        live.LiveDirectory = true;
        var favorites = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { favorite.Id };
        Assert(RadioTaxonomy.BrowseTier(favorite, favorites) > RadioTaxonomy.BrowseTier(verifiedBuiltIn, favorites), "favorite ranks first");
        Assert(RadioTaxonomy.BrowseTier(verifiedBuiltIn, favorites) > RadioTaxonomy.BrowseTier(live, favorites), "verified built-in ranks above unknown live directory result");
    }

    private static void TestSceneSatFallbackRanking()
    {
        var station = RadioCatalog.One("test:scene", "SceneSat", "scenesat", "SceneSat", "https://scenesat.com");
        station.Streams.Add(RadioCatalog.Variant("http://example.invalid/max", "mp3", 320, "direct", "320k"));
        station.Streams.Add(RadioCatalog.Variant("http://example.invalid/medium", "mp3", 128, "direct", "128k"));
        var ranked = StreamRanking.Rank(station.Streams);
        Assert(ranked.Count == 2 && ranked[0].BitrateKbps == 320 && ranked[1].BitrateKbps == 128, "compatible max-quality stream precedes fallback");
    }

    private static void Assert(bool condition, string message)
    {
        _assertions++;
        if (!condition) throw new InvalidOperationException("Assertion failed: " + message);
    }
}
