using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

internal static class Program
{
    private static int _assertions;

    private static int Main()
    {
        try
        {
            TestMiniJson();
            TestIcyMetadata();
            TestRainwaveMetadata();
            TestCatalog();
            TestStreamRanking();
            TestPersistence();
            TestLegacyMigration();
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
        const string current = "{\"sched_current\":{\"songs\":[{\"title\":\"Dire, Dire Docks\",\"artists\":[{\"name\":\"Koji Kondo\"}]}]}}";
        TrackInfo track;
        Assert(RadioMetadata.TryParseRainwaveNowPlayingJson(current, out track), "Rainwave current parse");
        Assert(track.Display == "Koji Kondo - Dire, Dire Docks", "Rainwave current display");
        Assert(Math.Abs(track.PlaybackDelaySeconds - 10.0) < 0.001, "Rainwave schedule fallback is delayed ten seconds");

        const string legacy = "{\"sched_current\":{\"song_data\":{\"title\":\"Stickerbrush Symphony\",\"artists\":[{\"name\":\"David Wise\"}]}}}";
        Assert(RadioMetadata.TryParseRainwaveNowPlayingJson(legacy, out track), "Rainwave song_data parse");
        Assert(track.Display == "David Wise - Stickerbrush Symphony", "Rainwave song_data display");
    }

    private static void TestCatalog()
    {
        RadioCatalog.Initialize();
        var stations = RadioCatalog.Snapshot();
        Assert(stations.Count == 16, "catalog has exactly sixteen stations");
        Assert(stations.Count(s => s.Provider == "radiomonster") == 10, "catalog has ten RadioMonster stations");
        Assert(stations.Count(s => s.Provider == "rainwave") == 6, "catalog has six Rainwave stations");
        Assert(stations.All(s => s.Provider == "radiomonster" || s.Provider == "rainwave"), "catalog has no third provider");
        Assert(RadioCatalog.Find("rainwave:5") != null, "Rainwave All exists");
        Assert(RadioCatalog.Find("radiomonster:80s") != null, "RadioMonster 80s exists");
    }

    private static void TestStreamRanking()
    {
        var streams = new List<StreamVariant>
        {
            new StreamVariant { Url = "mp3-320", Codec = "mp3", BitrateKbps = 320 },
            new StreamVariant { Url = "aac-128", Codec = "aac", BitrateKbps = 128 },
            new StreamVariant { Url = "aac-64", Codec = "aac", BitrateKbps = 64 },
            new StreamVariant { Url = "opus", Codec = "opus", BitrateKbps = 512 }
        };
        var ranked = StreamRanking.Rank(streams);
        Assert(ranked.Count == 3, "unsupported codecs are filtered");
        Assert(ranked[0].Url == "mp3-320", "320k MP3 ranks first");
        Assert(ranked[1].Url == "aac-128", "128k AAC ranks second");
        Assert(ranked[2].Url == "aac-64", "64k AAC remains fallback");
    }

    private static void TestPersistence()
    {
        RadioCatalog.Initialize();
        var root = TempDirectory();
        try
        {
            var state = new RadioState
            {
                SelectedStationId = "radiomonster:80s",
                Playing = false,
                SongNotifications = false,
                Volume = 0.4f
            };
            state.Favorites.Add("radiomonster:80s");
            state.Favorites.Add("old-provider:gone");
            RadioPersistence.TouchRecent(state, "radiomonster:80s");
            RadioPersistence.TouchRecent(state, "old-provider:gone");
            RadioPersistence.SaveState(root, state);

            var loaded = RadioPersistence.LoadState(root);
            RadioPersistence.PruneToCatalog(loaded, RadioCatalog.Snapshot());
            Assert(loaded.SelectedStationId == "radiomonster:80s", "selected station round-trip");
            Assert(!loaded.Playing && !loaded.SongNotifications, "state bool round-trip");
            Assert(Math.Abs(loaded.Volume - 0.4f) < 0.001f, "state volume round-trip");
            Assert(loaded.Favorites.Contains("radiomonster:80s"), "valid favorite survives");
            Assert(!loaded.Favorites.Contains("old-provider:gone"), "removed-provider favorite is pruned");
            Assert(!loaded.Recents.Contains("old-provider:gone"), "removed-provider recent is pruned");

            loaded.SelectedStationId = "old-provider:gone";
            RadioPersistence.PruneToCatalog(loaded, RadioCatalog.Snapshot());
            Assert(loaded.SelectedStationId == "rainwave:5", "invalid selected station falls back to Rainwave All");
        }
        finally
        {
            Directory.Delete(root, true);
        }
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
            var state = RadioPersistence.LoadState(radio);
            Assert(state.SelectedStationId == "rainwave:4", "legacy Rainwave station migration");
            Assert(!state.SongNotifications, "legacy overlay preference migration");
            Assert(File.Exists(Path.Combine(radio, "Radio.state.json")), "legacy migration writes state");
        }
        finally
        {
            Directory.Delete(parent, true);
        }
    }

    private static void TestGenerationAndBufferClear()
    {
        GeneralRadio.AudioBuffers.Enqueue(new byte[] { 1, 2, 3 });
        var before = GeneralRadio.AudioGeneration;
        GeneralRadio.ClearAudioBuffers();
        var after = System.Threading.Interlocked.Increment(ref GeneralRadio.AudioGeneration);
        Assert(GeneralRadio.AudioBuffers.IsEmpty, "station switch clears old PCM buffers");
        Assert(after != before, "worker generation changes");
    }

    private static string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "gloader-radio-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Assert(bool condition, string message)
    {
        _assertions++;
        if (!condition) throw new InvalidOperationException("Assertion failed: " + message);
    }
}
