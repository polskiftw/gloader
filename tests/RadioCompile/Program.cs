using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

internal static class Program
{
    private static int _assertions;

    private static int Main(string[] args)
    {
        try
        {
            if (args.Any(arg => string.Equals(arg, "--live", StringComparison.OrdinalIgnoreCase)))
                return RunLive();

            TestMiniJson();
            TestIcyMetadata();
            TestRainwaveMetadata();
            TestLautFmMetadata();
            TestStreamRanking();
            TestTaxonomy();
            TestCustomStations();
            TestPersistence();
            TestLegacyMigration();
            Test181Parser();
            TestIcecastCatalogParser();
            TestProviderLinkParser();
            TestRadioBrowserParser();
            TestGenerationAndBufferClear();

            Console.WriteLine("PASS: Radio unit/compile regressions (" + _assertions + " assertions).");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: " + ex);
            return 1;
        }
    }

    private static void TestMiniJson()
    {
        var value = MiniJson.Parse("{\"a\":1,\"b\":[true,\"x\"],\"n\":null}") as Dictionary<string, object>;
        Assert(value != null && JsonValue.Int(value, "a") == 1, "MiniJson object/int");
        Assert(JsonValue.ChildArray(value, "b").Count == 2, "MiniJson array");
        var roundTrip = MiniJson.Parse(MiniJson.Stringify(value)) as Dictionary<string, object>;
        Assert(roundTrip != null && JsonValue.Int(roundTrip, "a") == 1, "MiniJson round-trip");
    }

    private static void TestIcyMetadata()
    {
        Assert(RadioMetadata.ExtractIcyStreamTitle("StreamTitle='Artist - Song';StreamUrl='';") == "Artist - Song", "ICY single quote");
        Assert(RadioMetadata.ExtractIcyStreamTitle("StreamTitle=\"Other Artist - Other Song\"") == "Other Artist - Other Song", "ICY double quote");
        Assert(RadioMetadata.ExtractIcyStreamTitle("StreamUrl='x';") == null, "ICY missing title");
    }

    private static void TestRainwaveMetadata()
    {
        const string current = "{\"sched_current\":{\"type\":\"Election\",\"songs\":[{\"id\":123,\"title\":\"Dire, Dire Docks\",\"artists\":[{\"id\":1,\"name\":\"Koji Kondo\"}]}]}}";
        TrackInfo track;
        Assert(RadioMetadata.TryParseRainwaveNowPlayingJson(current, out track), "Rainwave current parse");
        Assert(track.Display == "Koji Kondo - Dire, Dire Docks", "Rainwave current display");
        const string legacy = "{\"sched_current\":{\"song_data\":{\"title\":\"Stickerbrush Symphony\",\"artists\":[{\"name\":\"David Wise\"}]}}}";
        Assert(RadioMetadata.TryParseRainwaveNowPlayingJson(legacy, out track), "Rainwave legacy parse");
        Assert(track.Display == "David Wise - Stickerbrush Symphony", "Rainwave legacy display");
    }

    private static void TestLautFmMetadata()
    {
        TrackInfo track;
        Assert(RadioMetadata.TryParseLautFmCurrentSong("{\"title\":\"Lucia\",\"artist\":{\"name\":\"Roosevelt\"}}", out track), "laut.fm parse");
        Assert(track.Display == "Roosevelt - Lucia", "laut.fm display");
    }

    private static void TestStreamRanking()
    {
        var streams = new List<StreamVariant>
        {
            new StreamVariant { Url = "opus", Codec = "opus", BitrateKbps = 320, PublicFree = true },
            new StreamVariant { Url = "mp3-128", Codec = "mp3", BitrateKbps = 128, PublicFree = true },
            new StreamVariant { Url = "aac-256", Codec = "aac", BitrateKbps = 256, PublicFree = true },
            new StreamVariant { Url = "paid", Codec = "mp3", BitrateKbps = 320, PublicFree = false },
            new StreamVariant { Url = "auth", Codec = "mp3", BitrateKbps = 320, PublicFree = true, RequiresAuthentication = true }
        };
        var ranked = StreamRanking.Rank(streams);
        Assert(ranked.Count == 2, "rank filters unsupported/paid/auth");
        Assert(ranked[0].Url == "aac-256", "AAC high-quality compatible first");
        Assert(ranked[1].Url == "mp3-128", "MP3 fallback second");
    }

    private static void TestTaxonomy()
    {
        Assert(RadioTaxonomy.NormalizeTag("hip hop") == "Hip-Hop", "taxonomy alias");
        Assert(RadioTaxonomy.NormalizeTag("EDM") == "Electronic", "taxonomy EDM alias");
        Assert(RadioTaxonomy.InferDecade("Best of the 80s") == 1980, "decade infer");
        var station = RadioCatalog.One("x", "Test 80s Rock", "p", "P", "https://example.com", "Rock").AddDecades(1980);
        Assert(RadioTaxonomy.Matches(station, "Rock", 1980, "test rock", new HashSet<string>(), new List<string>()), "taxonomy filtering");
    }

    private static void TestCustomStations()
    {
        var errors = new List<string>();
        var json = "[" +
                   "{\"name\":\"Good\",\"url\":\"https://example.com/live.mp3\",\"codec\":\"mp3\",\"bitrate\":192,\"tags\":[\"rock\",\"80s\"]}," +
                   "{\"name\":\"Broken\",\"url\":\"not-a-url\"}," +
                   "{\"name\":\"Disabled\",\"enabled\":false,\"url\":\"https://example.com/x.mp3\"}" +
                   "]";
        var stations = RadioPersistence.ParseCustomStations(json, errors);
        Assert(stations.Count == 1 && stations[0].Name == "Good", "custom station valid/disabled handling");
        Assert(errors.Count == 1 && errors[0].Contains("#2"), "custom station per-record error");
    }

    private static void TestPersistence()
    {
        var root = TempDirectory();
        try
        {
            var state = new RadioState { SelectedStationId = "gtt:main", Playing = false, SongNotifications = false, Volume = 0.4f };
            state.Favorites.Add("gtt:main");
            RadioPersistence.TouchRecent(state, "gtt:main");
            RadioPersistence.SaveState(root, state);
            var loaded = RadioPersistence.LoadState(root);
            Assert(loaded.SelectedStationId == "gtt:main", "state selected station round-trip");
            Assert(!loaded.Playing && !loaded.SongNotifications, "state bool round-trip");
            Assert(Math.Abs(loaded.Volume - 0.4f) < 0.001f, "state volume round-trip");
            Assert(loaded.Favorites.Contains("gtt:main") && loaded.Recents.First() == "gtt:main", "favorites/recents round-trip");
        }
        finally { Directory.Delete(root, true); }
    }

    private static void TestLegacyMigration()
    {
        var parent = TempDirectory();
        try
        {
            var radio = Path.Combine(parent, "Radio");
            var legacy = Path.Combine(parent, "VGMRadio");
            Directory.CreateDirectory(radio);
            Directory.CreateDirectory(legacy);
            File.WriteAllText(Path.Combine(legacy, "VGMRadio.ini"), "Source=Rainwave\nStation=Chiptunes\nShowNowPlaying=false\n");
            var state = new RadioState();
            var migrated = RadioPersistence.TryMigrateLegacyVgmRadio(radio, state);
            Assert(migrated == "rainwave:4", "legacy Rainwave station migration");
            Assert(!state.SongNotifications, "legacy overlay preference migration");
            Assert(File.Exists(Path.Combine(radio, "Radio.state.json")), "legacy migration writes new state");
        }
        finally { Directory.Delete(parent, true); }
    }

    private static void Test181Parser()
    {
        var html = "<h3>80s</h3><a href=\"https://listen.181fm.com/181-awesome80s_128k.mp3\">Awesome 80's</a>" +
                   "<a href='https://listen.181fm.com/181-rock_128k.mp3'>Rock 181</a>";
        var stations = RadioCatalog.Parse181FmLinks(html);
        Assert(stations.Count == 2, "181 parser station count");
        Assert(stations.Any(s => s.Decades.Contains(1980)), "181 taxonomy inference");
        Assert(stations.All(s => s.Streams.Any(v => v.BitrateKbps == 128 && v.Codec == "mp3")), "181 MP3 stream extraction");
    }

    private static void TestIcecastCatalogParser()
    {
        var json = "{\"icestats\":{\"source\":[" +
                   "{\"listenurl\":\"https://stream.example/nightride.mp3\",\"server_type\":\"audio/mpeg\",\"bitrate\":320,\"genre\":\"Synthwave\"}," +
                   "{\"listenurl\":\"https://stream.example/nightride.ogg\",\"server_type\":\"application/ogg\",\"genre\":\"Synthwave\"}," +
                   "{\"listenurl\":\"https://stream.example/chillsynth.mp3\",\"server_type\":\"audio/mpeg\",\"bitrate\":320}" +
                   "]}}";
        var stations = RadioCatalog.ParseIcecastCatalog(json, "nightride", "Nightride FM", "https://nightride.fm", "Electronic", "Synthwave");
        Assert(stations.Count == 2, "Icecast variants group by mount stem");
        Assert(stations.First(s => s.Id.Contains("nightride")).Streams.Count == 2, "Icecast codec variants retained");
    }

    private static void TestProviderLinkParser()
    {
        var radcap = "<a href='/hardbop.html'>Hard Bop</a><a href='/ambient.html'>Ambient</a><a href='/about.html'>About</a>";
        var stations = RadioCatalog.ParseProviderStationLinks(radcap, "radcap", "Radio Caprice", "https://radcap.ru/");
        Assert(stations.Count == 2, "RADCAP station-page parser");
        Assert(stations.All(s => s.Streams.Count == 2), "RADCAP primary resolver + fallback");
    }

    private static void TestRadioBrowserParser()
    {
        var root = MiniJson.Parse("[{\"stationuuid\":\"abc\",\"name\":\"Test\",\"url_resolved\":\"https://example.com/live.mp3\",\"homepage\":\"https://example.com\",\"tags\":\"rock,80s\",\"codec\":\"MP3\",\"bitrate\":192,\"lastcheckok\":true},{\"stationuuid\":\"bad\",\"name\":\"Bad Codec\",\"url_resolved\":\"https://example.com/x.ogg\",\"codec\":\"OGG\",\"lastcheckok\":true}]") as List<object>;
        var stations = RadioDirectories.ParseRadioBrowserResults(root);
        Assert(stations.Count == 1, "Radio Browser compatible-codec filter");
        Assert(stations[0].LiveDirectory && stations[0].Decades.Contains(1980), "Radio Browser live labeling/taxonomy");
    }

    private static void TestGenerationAndBufferClear()
    {
        GeneralRadio.AudioBuffers.Enqueue(new byte[] { 1, 2, 3 });
        var before = GeneralRadio.AudioGeneration;
        GeneralRadio.ClearAudioBuffers();
        var after = System.Threading.Interlocked.Increment(ref GeneralRadio.AudioGeneration);
        Assert(GeneralRadio.AudioBuffers.IsEmpty, "station switch clears old PCM buffers");
        Assert(after != before, "station switch generation invalidates old workers");
    }

    private static int RunLive()
    {
        Console.WriteLine("Radio live smoke tests - network/provider truth check");
        var failures = new List<string>();
        Live("Rainwave catalog/metadata", failures, () =>
        {
            TrackInfo track;
            return RadioMetadata.TryParseRainwaveNowPlayingJson(RadioNet.DownloadText("https://rainwave.cc/api4/info?sid=5", 10000), out track) && !string.IsNullOrWhiteSpace(track.Display);
        });
        Live("Nightride Icecast full catalog", failures, () => RadioCatalog.ParseIcecastCatalog(RadioNet.DownloadText("https://stream.nightride.fm/status-json.xsl", 10000), "nightride", "Nightride FM", "https://nightride.fm", "Electronic", "Synthwave").Count >= 7);
        Live("181.FM official catalog", failures, () => RadioCatalog.Parse181FmLinks(RadioNet.DownloadText("https://www.181.fm/links", 10000)).Count >= 40);
        Live("RADCAP official catalog", failures, () => RadioCatalog.ParseProviderStationLinks(RadioNet.DownloadText("https://radcap.ru/", 12000), "radcap", "Radio Caprice", "https://radcap.ru/").Count >= 400);
        Live("113.FM official catalog", failures, () => RadioCatalog.ParseProviderStationLinks(RadioNet.DownloadText("https://113.fm/browse", 12000), "113fm", "113.FM", "https://113.fm/").Count >= 50);
        Live("SceneSat max-quality playlist", failures, () => RadioNet.ResolvePlaylist("https://scenesat.com/listen/normal/max.m3u").StartsWith("http", StringComparison.OrdinalIgnoreCase));
        Live("Radio Browser live search", failures, () => RadioDirectories.SearchRadioBrowser("jazz").Count > 0);
        Live("laut.fm live search", failures, () => RadioDirectories.SearchLautFm("rock").Count > 0);
        Live("GTT stream accepts ICY metadata", failures, () => !string.IsNullOrWhiteSpace(RadioMetadata.ReadIcyStreamTitle("https://icecast.gttradio.com/mp3_320k", 12000, 8)));

        if (failures.Count == 0)
        {
            Console.WriteLine("PASS: all Radio live smoke tests.");
            return 0;
        }
        Console.Error.WriteLine("FAIL: " + string.Join("; ", failures));
        return 2;
    }

    private static void Live(string name, List<string> failures, Func<bool> probe)
    {
        try
        {
            var ok = probe();
            Console.WriteLine((ok ? "PASS" : "FAIL") + ": " + name);
            if (!ok) failures.Add(name);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: " + name + " - " + ex.Message);
            failures.Add(name + " (" + ex.Message + ")");
        }
    }

    private static string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "gloader-radio-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Assert(bool condition, string message)
    {
        _assertions++;
        if (!condition) throw new InvalidOperationException("Assertion failed: " + message);
    }
}
