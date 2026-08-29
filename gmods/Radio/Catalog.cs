#if !GLOADER_SERVER
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;

internal static class RadioCatalog
{
    private static readonly object Sync = new object();
    private static readonly Dictionary<string, Station> StationsById = new Dictionary<string, Station>(StringComparer.OrdinalIgnoreCase);
    private static string _modDirectory;
    private static int _refreshing;

    internal static void Initialize(string modDirectory)
    {
        _modDirectory = modDirectory;
        lock (Sync)
        {
            StationsById.Clear();
            foreach (var station in BuiltInSeeds()) Upsert(station);
            foreach (var station in RadioPersistence.LoadCachedCatalog(modDirectory)) Upsert(station);
            foreach (var station in RadioPersistence.LoadCustomStations(modDirectory)) Upsert(station);
        }
        BeginRefresh();
    }

    internal static List<Station> Snapshot()
    {
        lock (Sync)
            return StationsById.Values.OrderBy(station => station.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    internal static Station Find(string id)
    {
        lock (Sync)
        {
            Station station;
            return !string.IsNullOrWhiteSpace(id) && StationsById.TryGetValue(id, out station) ? station : null;
        }
    }

    internal static void AddDirectoryResults(IEnumerable<Station> stations)
    {
        lock (Sync)
            foreach (var station in stations ?? Enumerable.Empty<Station>()) Upsert(station);
    }

    internal static void BeginRefresh()
    {
        if (Interlocked.Exchange(ref _refreshing, 1) != 0) return;
        new Thread(() =>
        {
            try
            {
                var refreshed = new List<Station>();
                TryAppend(refreshed, RefreshNightride);
                TryAppend(refreshed, Refresh181Fm);
                TryAppend(refreshed, RefreshRadcap);
                TryAppend(refreshed, Refresh113Fm);
                lock (Sync)
                    foreach (var station in refreshed) Upsert(station);
                if (refreshed.Count > 0) RadioPersistence.SaveCachedCatalog(_modDirectory, refreshed);
            }
            finally { Interlocked.Exchange(ref _refreshing, 0); }
        }) { IsBackground = true, Name = "gloader Radio catalog refresh" }.Start();
    }

    private static void TryAppend(List<Station> output, Func<List<Station>> loader)
    {
        try { output.AddRange(loader()); } catch { }
    }

    private static void Upsert(Station station)
    {
        if (station == null || string.IsNullOrWhiteSpace(station.Id) || string.IsNullOrWhiteSpace(station.Name)) return;
        StationsById[station.Id] = station;
    }

    private static IEnumerable<Station> BuiltInSeeds()
    {
        foreach (var station in RainwaveSeeds()) yield return station;
        yield return One("gtt:main", "Game That Tune Radio", "gtt", "Game That Tune", "https://gamethattune.com/", "Video Game Music", "Soundtracks")
            .WithStream("https://icecast.gttradio.com/mp3_320k", "mp3", 320, "direct")
            .WithMetadata(MetadataMode.Icy);

        yield return One("radiosega:main", "RadioSEGA", "radiosega", "RadioSEGA", "https://www.radiosega.net/", "Video Game Music", "Soundtracks")
            .WithStream("https://icecast.radiosega.net/live", "aac", 96, "direct", "HE-AAC")
            .WithStream("https://icecast.radiosega.net/rs-mpeg.mp3", "mp3", 64, "direct", "MP3 fallback")
            .WithMetadata(MetadataMode.Icy);

        yield return One("cvgm:main", "CVGM Radio", "cvgm", "CVGM", "https://radio.cvgm.net/", "Video Game Music", "Chiptune", "Demoscene")
            .WithStream("https://radio.cvgm.net/demovibes/streams/", "mp3", 192, "station-page", "192k MP3 relay resolver")
            .WithMetadata(MetadataMode.Icy);

        yield return One("scenesat:main", "SceneSat", "scenesat", "SceneSat", "https://scenesat.com/", "Demoscene", "Electronic", "Chiptune")
            .WithStream("https://sj-1.scenesat.com/scenesatmax", "mp3", 320, "direct", "320k MP3")
            .WithStream("http://Oscar.SceneSat.com:8000/scenesatmax", "mp3", 320, "direct", "320k MP3 fallback")
            .WithMetadata(MetadataMode.Icy);

        yield return One("slay:main", "SLAY Radio", "slay", "SLAY Radio", "https://www.slayradio.org/", "Chiptune", "Demoscene", "Electronic")
            .WithStream("http://relay4.slayradio.org:8000/", "mp3", 128, "direct", "128k MP3")
            .WithStream("http://relay1.slayradio.org:8000/", "mp3", 128, "direct", "128k MP3 fallback")
            .WithMetadata(MetadataMode.Icy);

        yield return One("gensokyo:main", "Gensokyo Radio", "gensokyo", "Gensokyo Radio", "https://gensokyoradio.net/", "Video Game Music", "Touhou", "Electronic")
            .WithStream("", "mp3", 0, "radio-browser-exact", "Public stream resolver", "Gensokyo Radio")
            .WithMetadata(MetadataMode.WebPage, "https://gensokyoradio.net/");

        foreach (var station in PulsRadioSeeds()) yield return station;
    }

    private static IEnumerable<Station> RainwaveSeeds()
    {
        var rows = new[]
        {
            new object[] { 5, "All", "All" }, new object[] { 1, "Game", "Game Music" },
            new object[] { 2, "OC ReMix", "OC ReMix" }, new object[] { 3, "Covers", "Covers" },
            new object[] { 4, "Chiptunes", "Chiptune" }, new object[] { 6, "Chill", "Chill" }
        };
        foreach (var row in rows)
        {
            var sid = (int)row[0];
            var name = (string)row[1];
            var tag = (string)row[2];
            yield return One("rainwave:" + sid, "Rainwave - " + name, "rainwave", "Rainwave", "https://rainwave.cc/", "Video Game Music", tag)
                .WithStream("https://rainwave.cc/tune_in/" + sid + ".mp3.m3u", "mp3", 192, "rainwave", "Rainwave MP3", sid.ToString(CultureInfo.InvariantCulture))
                .WithMetadata(MetadataMode.Rainwave, "https://rainwave.cc/api4/info?sid=" + sid);
        }
    }

    private static IEnumerable<Station> PulsRadioSeeds()
    {
        var rows = new[]
        {
            new[] { "dance", "PulsRadio Dance", "Dance", "" },
            new[] { "hits", "PulsRadio Hits", "Pop", "" },
            new[] { "club", "PulsRadio Club", "Dance", "" },
            new[] { "lounge", "PulsRadio Lounge", "Lounge", "" },
            new[] { "trance", "PulsRadio Trance", "Trance", "" },
            new[] { "2000", "PulsRadio 2000", "Dance", "2000" },
            new[] { "90s", "PulsRadio 90s", "Dance", "1990" },
            new[] { "80s", "PulsRadio 80s", "Oldies", "1980" }
        };
        foreach (var row in rows)
        {
            var station = One("pulsradio:" + row[0], row[1], "pulsradio", "PulsRadio", "https://www.pulsradio.com/en/" + row[0] + "/", row[2], "Electronic")
                .WithStream("", "mp3", 0, "radio-browser-exact", "Directory-backed public resolver", row[1])
                .WithMetadata(MetadataMode.Icy);
            int decade;
            if (int.TryParse(row[3], out decade)) station.AddDecades(decade);
            if (row[0] == "dance") station.Streams.Insert(0, Variant("https://www.pulsradio.com/pls/openstream/puls-adsl.m3u", "mp3", 192, "playlist", "Official MP3 playlist"));
            if (row[0] == "trance") station.Streams.Insert(0, Variant("https://www.pulsradio.com/pls/openstream/pulstrance-adsl.m3u", "mp3", 192, "playlist", "Official MP3 playlist"));
            if (row[0] == "80s") station.Streams.Insert(0, Variant("https://www.pulsradio.com/pls/openstream/pulsV80-adsl.m3u", "mp3", 192, "playlist", "Official MP3 playlist"));
            if (row[0] == "90s") station.Streams.Insert(0, Variant("https://www.pulsradio.com/pls/openstream/pulsV90-adsl.m3u", "mp3", 192, "playlist", "Official MP3 playlist"));
            yield return station;
        }
    }

    internal static List<Station> ParseIcecastCatalog(string json, string provider, string providerDisplay, string baseUrl, params string[] providerTags)
    {
        var result = new Dictionary<string, Station>(StringComparer.OrdinalIgnoreCase);
        var root = MiniJson.Parse(json) as Dictionary<string, object>;
        var ice = JsonValue.ChildObject(root, "icestats");
        if (ice == null) return result.Values.ToList();
        object sourceValue;
        if (!ice.TryGetValue("source", out sourceValue) || sourceValue == null) return result.Values.ToList();
        var sources = sourceValue as List<object> ?? new List<object> { sourceValue };
        foreach (var item in sources)
        {
            var source = item as Dictionary<string, object>;
            if (source == null) continue;
            var listen = JsonValue.String(source, "listenurl");
            if (listen.Length == 0) continue;
            var path = new Uri(listen).AbsolutePath.Trim('/');
            var dot = path.LastIndexOf('.');
            var stem = dot > 0 ? path.Substring(0, dot) : path;
            if (stem.Length == 0) continue;
            var id = provider + ":" + RadioTaxonomy.Slug(stem);
            Station station;
            if (!result.TryGetValue(id, out station))
            {
                var display = PrettyMount(stem);
                station = One(id, display, provider, providerDisplay, baseUrl, providerTags);
                station.SourcePage = baseUrl;
                result[id] = station;
            }
            var serverType = JsonValue.String(source, "server_type");
            var bitrate = JsonValue.Int(source, "bitrate");
            var codec = CodecFromContentType(serverType, listen);
            station.Streams.Add(Variant(listen, codec, bitrate, "direct", bitrate > 0 ? bitrate + "k " + codec.ToUpperInvariant() : codec.ToUpperInvariant()));
            var genre = JsonValue.String(source, "genre");
            if (genre.Length > 0 && !string.Equals(genre, "various", StringComparison.OrdinalIgnoreCase)) station.AddTags(genre);
        }
        foreach (var station in result.Values)
        {
            InferTags(station, station.Name + " " + station.Id);
            station.MetadataMode = MetadataMode.Icy;
        }
        return result.Values.ToList();
    }

    internal static List<Station> Parse181FmLinks(string html)
    {
        var result = new Dictionary<string, Station>(StringComparer.OrdinalIgnoreCase);
        var text = html ?? string.Empty;

        foreach (Match match in Regex.Matches(text, @"href\s*=\s*[""'](?<url>https?://listen\.181fm\.com/(?<slug>[^""']+?)_128k\.mp3[^""']*)[""'][^>]*>(?<name>.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
            Add181Station(result, match.Groups["url"].Value, match.Groups["slug"].Value, StripHtml(match.Groups["name"].Value));

        // The current 181.FM legacy page is deliberately simple and may expose stream
        // URLs as text rather than stable anchor markup. URL discovery is therefore the
        // canonical fallback, so the whole public catalog survives cosmetic HTML changes.
        foreach (Match match in Regex.Matches(text, @"(?<url>https?://listen\.181fm\.com/(?<slug>181-[a-z0-9-]+)_128k\.mp3(?:\?[^\s""'<>]*)?)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            Add181Station(result, match.Groups["url"].Value, match.Groups["slug"].Value, string.Empty);

        return result.Values.ToList();
    }

    private static void Add181Station(Dictionary<string, Station> result, string rawUrl, string rawSlug, string rawName)
    {
        var url = WebUtility.HtmlDecode(rawUrl ?? string.Empty).Trim();
        var slug = (rawSlug ?? string.Empty).Trim();
        if (url.Length == 0 || slug.Length == 0) return;
        var id = "181fm:" + RadioTaxonomy.Slug(slug);
        Station station;
        if (!result.TryGetValue(id, out station))
        {
            var name = (rawName ?? string.Empty).Trim();
            if (name.Length == 0) name = "181.FM " + PrettyMount(slug.StartsWith("181-", StringComparison.OrdinalIgnoreCase) ? slug.Substring(4) : slug);
            station = One(id, name, "181fm", "181.FM", "https://www.181.fm/", "Radio");
            result[id] = station;
            InferTags(station, name + " " + slug);
        }
        if (!station.Streams.Any(stream => string.Equals(stream.Url, url, StringComparison.OrdinalIgnoreCase)))
            station.Streams.Add(Variant(url, "mp3", 128, "direct", "128k MP3"));
        var aac = Regex.Replace(url, @"_128k\.mp3(?=\?|$)", "_64k.aac", RegexOptions.IgnoreCase);
        if (!string.Equals(aac, url, StringComparison.OrdinalIgnoreCase) && !station.Streams.Any(stream => string.Equals(stream.Url, aac, StringComparison.OrdinalIgnoreCase)))
            station.Streams.Add(Variant(aac, "aac", 64, "direct", "64k AAC fallback"));
    }

    internal static List<Station> ParseProviderStationLinks(string html, string provider, string providerDisplay, string baseUrl)
    {
        var result = new Dictionary<string, Station>(StringComparer.OrdinalIgnoreCase);
        var baseUri = new Uri(baseUrl);
        foreach (Match match in Regex.Matches(html ?? string.Empty, @"href\s*=\s*[""'](?<href>[^""'#]+)[""'][^>]*>(?<name>.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            var href = WebUtility.HtmlDecode(match.Groups["href"].Value.Trim());
            var name = StripHtml(match.Groups["name"].Value).Trim();
            if (name.Length < 3 || name.Length > 120) continue;
            Uri uri;
            if (!Uri.TryCreate(baseUri, href, out uri) || !string.Equals(uri.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase)) continue;
            var path = uri.AbsolutePath.ToLowerInvariant();
            var isCandidate = provider == "radcap"
                ? path.EndsWith(".html") && !path.EndsWith("index-db.html") && !path.EndsWith("index.html")
                : provider == "113fm" ? Is113FmCandidate(path, name) : (path.Contains("channel") || path.Contains("station"));
            if (!isCandidate || IsNavigationName(name)) continue;
            var slug = RadioTaxonomy.Slug(Path.GetFileNameWithoutExtension(uri.AbsolutePath));
            if (slug.Length < 2) slug = RadioTaxonomy.Slug(name);
            var id = provider + ":" + slug;
            if (result.ContainsKey(id)) continue;
            var station = One(id, name, provider, providerDisplay, baseUrl, "Radio");
            station.SourcePage = uri.AbsoluteUri;
            var codec = provider == "radcap" ? "aac" : provider == "113fm" ? "mp3" : string.Empty;
            var bitrate = provider == "radcap" ? 320 : provider == "113fm" ? 128 : 0;
            station.Streams.Add(Variant(uri.AbsoluteUri, codec, bitrate, "station-page", "Official station-page resolver", provider));
            station.Streams.Add(Variant("", "mp3", 0, "radio-browser-exact", "Radio Browser fallback", name));
            InferTags(station, name + " " + slug);
            result[id] = station;
        }
        return result.Values.ToList();
    }

    private static bool Is113FmCandidate(string path, string name)
    {
        var slug = (path ?? string.Empty).Trim('/').ToLowerInvariant();
        if (slug.Length < 2 || slug.Contains("/")) return false;
        foreach (var blocked in new[] { "browse", "about", "contact", "faq", "faqs", "chat", "login", "register", "profile", "news", "privacy", "terms", "search", "genres" })
            if (slug == blocked) return false;
        var lowerName = (name ?? string.Empty).ToLowerInvariant();
        if (lowerName.Contains("browse channels") || lowerName.Contains("sign in")) return false;
        return true;
    }

    private static List<Station> RefreshNightride()
    {
        return ParseIcecastCatalog(RadioNet.DownloadText("https://stream.nightride.fm/status-json.xsl", 8000), "nightride", "Nightride FM", "https://nightride.fm/", "Electronic", "Synthwave");
    }

    private static List<Station> Refresh181Fm() => Parse181FmLinks(RadioNet.DownloadText("https://www.181.fm/legacy.html", 10000));
    private static List<Station> RefreshRadcap() => ParseProviderStationLinks(RadioNet.DownloadText("https://radcap.ru/index-db.html", 12000), "radcap", "Radio Caprice", "https://radcap.ru/");

    private static List<Station> Refresh113Fm()
    {
        var official = ParseProviderStationLinks(RadioNet.DownloadText("https://113.fm/browse", 12000), "113fm", "113.FM", "https://113.fm/");
        if (official.Count >= 50) return official;

        // 113.FM's modern Browse page has alternated between server-rendered and
        // client-rendered markup. Keep the provider catalog complete when that happens
        // by supplementing it from healthy Radio Browser records that identify 113.FM.
        foreach (var directoryStation in RadioDirectories.SearchRadioBrowser("113.FM", 200))
        {
            var belongs = directoryStation.Name.IndexOf("113.FM", StringComparison.OrdinalIgnoreCase) >= 0 ||
                          directoryStation.HomePage.IndexOf("113.fm", StringComparison.OrdinalIgnoreCase) >= 0 ||
                          directoryStation.Streams.Any(stream => (stream.Url ?? string.Empty).IndexOf("113fm", StringComparison.OrdinalIgnoreCase) >= 0 || (stream.Url ?? string.Empty).IndexOf("113.fm", StringComparison.OrdinalIgnoreCase) >= 0);
            if (!belongs || directoryStation.Streams.Count == 0) continue;
            var uuid = directoryStation.Id.StartsWith("radiobrowser:", StringComparison.OrdinalIgnoreCase) ? directoryStation.Id.Substring("radiobrowser:".Length) : RadioTaxonomy.StableHash(directoryStation.Name);
            var id = "113fm:rb-" + uuid;
            if (official.Any(station => string.Equals(station.Id, id, StringComparison.OrdinalIgnoreCase) || string.Equals(station.Name, directoryStation.Name, StringComparison.OrdinalIgnoreCase))) continue;
            var station = One(id, directoryStation.Name, "113fm", "113.FM", string.IsNullOrWhiteSpace(directoryStation.HomePage) ? "https://113.fm/" : directoryStation.HomePage, directoryStation.Tags.ToArray());
            station.SourcePage = "Radio Browser provider fallback";
            station.Streams.AddRange(directoryStation.Streams.Select(stream => stream.Clone()));
            station.MetadataMode = MetadataMode.Icy;
            station.AddDecades(directoryStation.Decades.ToArray());
            official.Add(station);
        }
        return official;
    }

    internal static Station One(string id, string name, string provider, string providerDisplay, string homePage, params string[] tags)
    {
        var station = new Station { Id = id, Name = name, Provider = provider, ProviderDisplay = providerDisplay, HomePage = homePage, BuiltIn = true };
        station.AddTags(tags);
        return station;
    }

    internal static StreamVariant Variant(string url, string codec, int bitrate, string resolver, string label, string resolverArgument = "")
    {
        return new StreamVariant { Url = url ?? string.Empty, Codec = codec ?? string.Empty, BitrateKbps = bitrate, Resolver = resolver ?? string.Empty, Label = label ?? string.Empty, ResolverArgument = resolverArgument ?? string.Empty, PublicFree = true };
    }

    private static string CodecFromContentType(string type, string url)
    {
        var value = ((type ?? string.Empty) + " " + (url ?? string.Empty)).ToLowerInvariant();
        if (value.Contains("aac")) return "aac";
        if (value.Contains("mpeg") || value.Contains("mp3")) return "mp3";
        if (value.Contains("opus")) return "opus";
        if (value.Contains("ogg")) return "ogg";
        if (value.Contains("flac")) return "flac";
        return string.Empty;
    }

    private static string PrettyMount(string stem)
    {
        var special = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "nightride", "Nightride FM" }, { "chillsynth", "Chillsynth FM" }, { "darksynth", "Darksynth FM" },
            { "horrorsynth", "Horrorsynth FM" }, { "datawave", "Datawave FM" }, { "spacesynth", "Spacesynth FM" },
            { "ebsm", "EBSM" }, { "rekt", "REKT" }, { "rektify", "REKTify" }, { "rektory", "REKTory" }, { "d-notive", "D-Notive" }
        };
        string name;
        if (special.TryGetValue(stem, out name)) return name;
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(stem.Replace('-', ' ').Replace('_', ' '));
    }

    private static string StripHtml(string value)
    {
        return WebUtility.HtmlDecode(Regex.Replace(value ?? string.Empty, "<.*?>", string.Empty, RegexOptions.Singleline));
    }

    private static bool IsNavigationName(string name)
    {
        var value = (name ?? string.Empty).Trim().ToLowerInvariant();
        return value == "home" || value == "about" || value == "contact" || value == "news" || value == "login" || value == "register" || value.Contains("privacy") || value.Contains("terms") || value.Contains("browse channels");
    }

    private static void InferTags(Station station, string text)
    {
        var value = (text ?? string.Empty).ToLowerInvariant();
        var map = new[]
        {
            new[] { "synth", "Synthwave" }, new[] { "electro", "Electronic" }, new[] { "trance", "Trance" }, new[] { "dance", "Dance" },
            new[] { "rock", "Rock" }, new[] { "metal", "Rock" }, new[] { "pop", "Pop" }, new[] { "rap", "Hip-Hop" }, new[] { "hip hop", "Hip-Hop" },
            new[] { "jazz", "Jazz" }, new[] { "classical", "Classical" }, new[] { "ambient", "Ambient" }, new[] { "lounge", "Lounge" },
            new[] { "country", "Country" }, new[] { "oldies", "Oldies" }, new[] { "comedy", "Comedy" }, new[] { "christmas", "Holiday" },
            new[] { "chiptune", "Chiptune" }, new[] { "game", "Video Game Music" }, new[] { "soundtrack", "Soundtracks" }
        };
        foreach (var pair in map) if (value.Contains(pair[0])) station.AddTags(pair[1]);
        var decade = RadioTaxonomy.InferDecade(value);
        if (decade > 0) station.AddDecades(decade);
    }
}

internal static class StationExtensions
{
    internal static Station WithStream(this Station station, string url, string codec, int bitrate, string resolver, string label = "", string resolverArgument = "")
    {
        station.Streams.Add(RadioCatalog.Variant(url, codec, bitrate, resolver, label, resolverArgument));
        return station;
    }

    internal static Station WithMetadata(this Station station, MetadataMode mode, string url = "")
    {
        station.MetadataMode = mode;
        station.MetadataUrl = url ?? string.Empty;
        return station;
    }
}
#endif
