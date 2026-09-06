using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

internal static class Program
{
    private static readonly List<string> Failures = new List<string>();

    private static int Main()
    {
        Console.WriteLine("Radio live smoke - RadioMonster.fm + Rainwave only");
        RadioCatalog.Initialize();
        var stations = RadioCatalog.Snapshot();

        Probe("Exactly 10 RadioMonster + 6 Rainwave stations", () =>
            stations.Count == 16 &&
            stations.Count(s => s.Provider == "radiomonster") == 10 &&
            stations.Count(s => s.Provider == "rainwave") == 6 &&
            stations.All(s => s.Provider == "radiomonster" || s.Provider == "rainwave"));

        Probe("No stream or metadata URL escapes the two providers", () =>
            stations.All(station =>
                station.Streams.Count > 0 &&
                station.Streams.All(stream => IsAllowedUrl(stream.Url)) &&
                (string.IsNullOrWhiteSpace(station.MetadataUrl) || IsAllowedUrl(station.MetadataUrl))));

        Probe("RadioMonster quality order is 320k MP3 first", () =>
            stations.Where(s => s.Provider == "radiomonster").All(station =>
            {
                var ranked = StreamRanking.Rank(station.Streams);
                return ranked.Count == 3 &&
                       ranked[0].BitrateKbps == 320 &&
                       ranked[0].Codec == "mp3" &&
                       ranked[0].Url.EndsWith("/ultra", StringComparison.OrdinalIgnoreCase);
            }));

        Probe("RadioMonster Tophits live audio", () =>
        {
            var station = RadioCatalog.Find("radiomonster:tophits");
            var best = station == null ? null : StreamRanking.Rank(station.Streams).FirstOrDefault();
            return best != null && ProbeAudio(RadioNet.ResolveStreamVariant(station, best), 10000);
        });

        Probe("RadioMonster Tophits ICY metadata", () =>
        {
            var station = RadioCatalog.Find("radiomonster:tophits");
            var best = station == null ? null : StreamRanking.Rank(station.Streams).FirstOrDefault();
            if (best == null) return false;
            var title = RadioMetadata.ReadIcyStreamTitle(RadioNet.ResolveStreamVariant(station, best), 12000, 8);
            Console.WriteLine("  RadioMonster title: " + title);
            return RadioMetadata.IsTrackLike(title, station);
        });

        Probe("Rainwave station IDs and official playlists", () =>
        {
            var expected = new[] { "rainwave:1", "rainwave:2", "rainwave:3", "rainwave:4", "rainwave:5", "rainwave:6" };
            return expected.All(id =>
            {
                var station = RadioCatalog.Find(id);
                var best = station == null ? null : StreamRanking.Rank(station.Streams).FirstOrDefault();
                return best != null &&
                       best.Resolver == "rainwave" &&
                       best.Url.StartsWith("https://rainwave.cc/tune_in/", StringComparison.OrdinalIgnoreCase);
            });
        });

        Probe("Rainwave All playlist resolves to live audio", () =>
        {
            var station = RadioCatalog.Find("rainwave:5");
            var best = station == null ? null : StreamRanking.Rank(station.Streams).FirstOrDefault();
            if (best == null) return false;
            var resolved = RadioNet.ResolveStreamVariant(station, best);
            Console.WriteLine("  Rainwave resolved stream: " + resolved);
            return ProbeAudio(resolved, 10000);
        });

        Probe("Rainwave API current song + fallback alignment", () =>
        {
            TrackInfo track;
            var ok = RadioMetadata.TryParseRainwaveNowPlayingJson(
                RadioNet.DownloadText("https://rainwave.cc/api4/info?sid=5", 10000), out track);
            if (track != null) Console.WriteLine("  Rainwave API title: " + track.Display);
            return ok && track != null &&
                   !string.IsNullOrWhiteSpace(track.Display) &&
                   Math.Abs(track.PlaybackDelaySeconds - RadioMetadata.RainwaveScheduleFallbackDelaySeconds) < 0.001;
        });

        Probe("Rainwave schedule parser holds fallback by ten seconds", () =>
        {
            const string json = "{\"sched_current\":{\"songs\":[{\"title\":\"Synthetic Song\",\"artists\":[{\"name\":\"Synthetic Artist\"}]}]}}";
            TrackInfo track;
            return RadioMetadata.TryParseRainwaveNowPlayingJson(json, out track) &&
                   track != null &&
                   track.Display == "Synthetic Artist - Synthetic Song" &&
                   Math.Abs(track.PlaybackDelaySeconds - 10.0) < 0.001;
        });

        Advisory("Rainwave stream carries usable ICY metadata", () =>
        {
            var station = RadioCatalog.Find("rainwave:5");
            var best = station == null ? null : StreamRanking.Rank(station.Streams).FirstOrDefault();
            if (best == null) return false;
            var resolved = RadioNet.ResolveStreamVariant(station, best);
            var title = RadioMetadata.ReadIcyStreamTitle(resolved, 10000, 8);
            Console.WriteLine("  Rainwave stream title: " + title);
            return RadioMetadata.IsTrackLike(title, station);
        });

        Probe("Rainwave metadata path always returns stream-aligned or delayed data", () =>
        {
            var station = RadioCatalog.Find("rainwave:5");
            TrackInfo track;
            if (station == null || !RadioMetadata.TryReadTrack(station, out track) || track == null) return false;
            return track.PlaybackDelaySeconds == 0 ||
                   Math.Abs(track.PlaybackDelaySeconds - RadioMetadata.RainwaveScheduleFallbackDelaySeconds) < 0.001;
        });

        if (Failures.Count == 0)
        {
            Console.WriteLine("PASS: two-source Radio live smoke passed.");
            return 0;
        }

        Console.Error.WriteLine("FAIL: " + string.Join("; ", Failures));
        return 2;
    }

    private static bool IsAllowedUrl(string value)
    {
        Uri uri;
        if (!Uri.TryCreate(value, UriKind.Absolute, out uri)) return false;
        var host = uri.Host.ToLowerInvariant();
        return host == "rainwave.cc" || host.EndsWith(".rainwave.cc", StringComparison.Ordinal) ||
               host == "radiomonster.fm" || host.EndsWith(".radiomonster.fm", StringComparison.Ordinal);
    }

    private static bool ProbeAudio(string url, int timeoutMilliseconds)
    {
        try
        {
            var request = RadioNet.CreateRequest(url, timeoutMilliseconds, true);
            request.KeepAlive = false;
            using (var response = (HttpWebResponse)request.GetResponse())
            {
                if (response.StatusCode != HttpStatusCode.OK) return false;
                var contentType = (response.ContentType ?? string.Empty).ToLowerInvariant();
                return contentType.Length == 0 || contentType.Contains("audio") || contentType.Contains("mpeg") || contentType.Contains("aac") || contentType.Contains("mp3");
            }
        }
        catch
        {
            return false;
        }
    }

    private static void Probe(string name, Func<bool> probe)
    {
        try
        {
            var ok = probe();
            Console.WriteLine((ok ? "PASS" : "FAIL") + ": " + name);
            if (!ok) Failures.Add(name);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: " + name + " - " + ex.Message);
            Failures.Add(name + " (" + ex.Message + ")");
        }
    }

    private static void Advisory(string name, Func<bool> probe)
    {
        try
        {
            var ok = probe();
            Console.WriteLine((ok ? "PASS" : "WARN") + ": " + name + (ok ? "" : " (API fallback remains available)"));
        }
        catch (Exception ex)
        {
            Console.WriteLine("WARN: " + name + " - " + ex.Message + " (API fallback remains available)");
        }
    }
}
