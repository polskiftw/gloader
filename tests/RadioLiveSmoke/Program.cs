using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

internal static class Program
{
    private static readonly List<string> Failures = new List<string>();

    private static int Main()
    {
        Console.WriteLine("Radio live provider smoke - current public/free/compatible truth");

        Probe("Rainwave API + current song", () =>
        {
            TrackInfo track;
            return RadioMetadata.TryParseRainwaveNowPlayingJson(
                RadioNet.DownloadText("https://rainwave.cc/api4/info?sid=5", 10000), out track) &&
                track != null && !string.IsNullOrWhiteSpace(track.Display);
        });

        Probe("Nightride current Icecast catalog", () =>
        {
            var stations = RadioCatalog.ParseIcecastCatalog(
                RadioNet.DownloadText("https://stream.nightride.fm/status-json.xsl", 10000),
                "nightride", "Nightride FM", "https://nightride.fm/", "Electronic", "Synthwave");
            Console.WriteLine("  Nightride stations: " + stations.Count);
            return stations.Count >= 7 && stations.Any(s => StreamRanking.Rank(s.Streams).Count > 0);
        });

        Probe("181.FM full public legacy catalog + quality", () =>
        {
            var stations = RadioCatalog.Parse181FmLinks(RadioNet.DownloadText("https://www.181.fm/legacy.html", 10000));
            Console.WriteLine("  181.FM stations: " + stations.Count);
            return stations.Count >= 40 && stations.All(s =>
            {
                var ranked = StreamRanking.Rank(s.Streams);
                return ranked.Count > 0 && ranked[0].Codec == "mp3" && ranked[0].BitrateKbps >= 128;
            });
        });

        Probe("181.FM representative stream reachable", () => ProbeAudio("https://listen.181fm.com/181-awesome80s_128k.mp3", 10000));

        Probe("RADCAP current full database", () =>
        {
            var stations = RadioCatalog.ParseProviderStationLinks(
                RadioNet.DownloadText("https://radcap.ru/index-db.html", 12000),
                "radcap", "Radio Caprice", "https://radcap.ru/");
            Console.WriteLine("  RADCAP stations: " + stations.Count);
            return stations.Count >= 400 && stations.Any(s => StreamRanking.Rank(s.Streams).FirstOrDefault()?.BitrateKbps >= 320);
        });

        Probe("RADCAP 320k station-page resolver", () =>
        {
            var stations = RadioCatalog.ParseProviderStationLinks(
                RadioNet.DownloadText("https://radcap.ru/index-db.html", 12000),
                "radcap", "Radio Caprice", "https://radcap.ru/");
            var station = stations.FirstOrDefault(s => s.Name.IndexOf("ambient", StringComparison.OrdinalIgnoreCase) >= 0) ?? stations.FirstOrDefault();
            if (station == null) return false;
            var variant = StreamRanking.Rank(station.Streams).FirstOrDefault();
            if (variant == null || variant.BitrateKbps < 320) return false;
            var resolved = RadioNet.ResolveStreamVariant(station, variant);
            return !string.IsNullOrWhiteSpace(resolved) && resolved.StartsWith("http", StringComparison.OrdinalIgnoreCase);
        });

        Probe("113.FM direct public/free catalog", () =>
        {
            var stations = Radio113Fm.Discover(true);
            Console.WriteLine("  113.FM validated logical stations: " + stations.Count);
            foreach (var sample in stations.Take(5))
            {
                var stream = StreamRanking.Rank(sample.Streams).FirstOrDefault();
                Console.WriteLine("    " + sample.Name + " -> " + (stream == null ? "<no compatible stream>" : stream.Url));
            }

            // The scanner requires an identifiable station name plus actual track-like
            // ICY metadata. Requiring 60 independently validated logical stations catches
            // a materially incomplete pass while allowing individual channels to be down.
            return stations.Count >= 60 && stations.All(s =>
                s.Provider == "113fm" && StreamRanking.Rank(s.Streams).Any());
        });

        Probe("113.FM audio does not require fake track metadata", () =>
        {
            var station = Radio113Fm.Discover().FirstOrDefault(s => StreamRanking.Rank(s.Streams).Count > 0);
            if (station == null) return false;
            return ProbeAudio(StreamRanking.Rank(station.Streams)[0].Url, 8000);
        });

        Probe("SceneSat public compatible stream or directory fallback", () =>
        {
            // SceneSat's public listen menu was freshly verified during the implementation
            // survey to advertise a high-bandwidth/max-quality MP3 option. That web page
            // intermittently returns 404 to GitHub-hosted Azure runners, so CI verifies
            // the actual public audio paths rather than making page bot/routing behavior a
            // release gate. The runtime keeps all of these direct fallbacks too.
            if (ProbeAudio("http://Oscar.SceneSat.com:8000/scenesatmax", 5000) ||
                ProbeAudio("http://Salyut80.SceneSat.com:80/scenesatmax", 5000) ||
                ProbeAudio("https://sj-1.scenesat.com/scenesatmax", 5000) ||
                ProbeAudio("http://SC.SceneSat.com:8000", 5000))
                return true;

            // Azure cannot always route to the Icecast hosts. A current healthy compatible
            // Radio Browser record verifies the fallback the mod would use in that case.
            return RadioDirectories.SearchRadioBrowser("SceneSat", 30)
                .Any(s => StreamRanking.Rank(s.Streams).Count > 0);
        });

        Probe("RadioSEGA compatible public stream", () =>
            ProbeAudio("https://icecast.radiosega.net/live", 10000) ||
            ProbeAudio("https://icecast.radiosega.net/rs-mpeg.mp3", 10000));

        Probe("CVGM public stream-page resolver", () =>
        {
            var station = RadioCatalog.One("cvgm:smoke", "CVGM Radio", "cvgm", "CVGM", "https://radio.cvgm.net/", "Video Game Music");
            var variant = RadioCatalog.Variant("https://radio.cvgm.net/demovibes/streams/", "mp3", 192, "station-page", "192k MP3 relay resolver");
            var resolved = RadioNet.ResolveStreamVariant(station, variant);
            return !string.IsNullOrWhiteSpace(resolved) && resolved.StartsWith("http", StringComparison.OrdinalIgnoreCase);
        });

        Probe("SLAY Radio public MP3 relay", () =>
            ProbeAudio("http://relay4.slayradio.org:8000/", 7000) ||
            ProbeAudio("http://relay1.slayradio.org:8000/", 7000));

        Probe("PulsRadio current official playlists", () =>
        {
            var urls = new[]
            {
                "https://www.pulsradio.com/pls/openstream/puls-adsl.m3u",
                "https://www.pulsradio.com/pls/openstream/pulstrance-adsl.m3u",
                "https://www.pulsradio.com/pls/openstream/pulsV80-adsl.m3u",
                "https://www.pulsradio.com/pls/openstream/pulsV90-adsl.m3u"
            };
            return urls.All(url => RadioNet.ResolvePlaylist(url).StartsWith("http", StringComparison.OrdinalIgnoreCase));
        });

        Probe("Gensokyo Radio current public directory stream", () =>
            RadioDirectories.SearchRadioBrowser("Gensokyo Radio", 20)
                .Any(s => StreamRanking.Rank(s.Streams).Count > 0));

        Probe("laut.fm live discovery + current_song", () =>
        {
            var stations = RadioDirectories.SearchLautFm("rock");
            var station = stations.FirstOrDefault();
            if (station == null) return false;
            TrackInfo track;
            return RadioMetadata.TryParseLautFmCurrentSong(RadioNet.DownloadText(station.MetadataUrl, 8000), out track) &&
                   track != null && !string.IsNullOrWhiteSpace(track.Display);
        });

        Probe("Radio Browser healthy compatible discovery", () =>
            RadioDirectories.SearchRadioBrowser("jazz", 30).Any(s => StreamRanking.Rank(s.Streams).Count > 0));

        Probe("Game That Tune 320k + ICY", () =>
            !string.IsNullOrWhiteSpace(RadioMetadata.ReadIcyStreamTitle("https://icecast.gttradio.com/mp3_320k", 12000, 8)));

        if (Failures.Count == 0)
        {
            Console.WriteLine("PASS: all live Radio provider checks.");
            return 0;
        }

        Console.Error.WriteLine("FAIL: " + string.Join("; ", Failures));
        return 2;
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
}
