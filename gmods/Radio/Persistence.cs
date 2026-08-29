#if !GLOADER_SERVER
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

internal sealed class RadioState
{
    public string SelectedStationId = "rainwave:5";
    public bool Playing = true;
    public bool SongNotifications = true;
    public float Volume = 1f;
    public readonly HashSet<string> Favorites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public readonly List<string> Recents = new List<string>();
}

internal static class RadioPersistence
{
    private const int RecentLimit = 20;
    internal static readonly List<string> CustomStationErrors = new List<string>();

    internal static RadioState LoadState(string modDirectory)
    {
        var state = new RadioState();
        var path = Path.Combine(modDirectory, "Radio.state.json");
        try
        {
            if (File.Exists(path)) ApplyStateJson(state, File.ReadAllText(path));
            else TryMigrateLegacyVgmRadio(modDirectory, state);
        }
        catch { }
        return state;
    }

    internal static void SaveState(string modDirectory, RadioState state)
    {
        if (string.IsNullOrWhiteSpace(modDirectory) || state == null) return;
        var root = new Dictionary<string, object>
        {
            { "version", 1 }, { "selectedStationId", state.SelectedStationId ?? string.Empty },
            { "playing", state.Playing }, { "songNotifications", state.SongNotifications },
            { "volume", state.Volume }, { "favorites", state.Favorites.OrderBy(v => v, StringComparer.OrdinalIgnoreCase).Cast<object>().ToList() },
            { "recents", state.Recents.Take(RecentLimit).Cast<object>().ToList() }
        };
        AtomicWrite(Path.Combine(modDirectory, "Radio.state.json"), MiniJson.Stringify(root));
    }

    internal static void TouchRecent(RadioState state, string id)
    {
        if (state == null || string.IsNullOrWhiteSpace(id)) return;
        state.Recents.RemoveAll(value => string.Equals(value, id, StringComparison.OrdinalIgnoreCase));
        state.Recents.Insert(0, id);
        while (state.Recents.Count > RecentLimit) state.Recents.RemoveAt(state.Recents.Count - 1);
    }

    internal static List<Station> LoadCustomStations(string modDirectory)
    {
        CustomStationErrors.Clear();
        var path = Path.Combine(modDirectory, "stations.json");
        if (!File.Exists(path))
        {
            AtomicWrite(path,
                "[\n" +
                "  {\"name\":\"Example custom station\",\"enabled\":false,\"url\":\"https://example.invalid/stream.mp3\",\"codec\":\"mp3\",\"bitrate\":128,\"tags\":[\"Custom\"],\"metadata\":\"icy\"}\n" +
                "]\n");
            return new List<Station>();
        }

        try { return ParseCustomStations(File.ReadAllText(path), CustomStationErrors); }
        catch (Exception ex)
        {
            CustomStationErrors.Add("stations.json: " + ex.Message);
            return new List<Station>();
        }
    }

    internal static List<Station> ParseCustomStations(string json, IList<string> errors)
    {
        var result = new List<Station>();
        var root = MiniJson.Parse(json) as List<object>;
        if (root == null) throw new InvalidDataException("Custom station file must contain a JSON array.");
        for (var index = 0; index < root.Count; index++)
        {
            try
            {
                var obj = root[index] as Dictionary<string, object>;
                if (obj == null) throw new InvalidDataException("entry is not an object");
                if (!JsonValue.Bool(obj, "enabled", true)) continue;
                var name = JsonValue.String(obj, "name").Trim();
                var url = JsonValue.String(obj, "url").Trim();
                Uri uri;
                if (name.Length == 0) throw new InvalidDataException("missing name");
                if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                    throw new InvalidDataException("url must be an http/https URL");
                var id = JsonValue.String(obj, "id");
                if (id.Length == 0) id = "custom:" + RadioTaxonomy.Slug(name) + "-" + RadioTaxonomy.StableHash(url);
                var station = RadioCatalog.One(id, name, "custom", "Custom", JsonValue.String(obj, "homepage"), "Custom");
                station.BuiltIn = false;
                station.Streams.Add(RadioCatalog.Variant(url, JsonValue.String(obj, "codec"), JsonValue.Int(obj, "bitrate"), JsonValue.String(obj, "resolver", "direct"), "Custom stream"));
                var tags = JsonValue.ChildArray(obj, "tags");
                if (tags != null) foreach (var tag in tags) station.AddTags(Convert.ToString(tag, CultureInfo.InvariantCulture));
                var metadata = JsonValue.String(obj, "metadata", "icy").ToLowerInvariant();
                station.MetadataMode = metadata == "none" ? MetadataMode.None : metadata == "web" ? MetadataMode.WebPage : MetadataMode.Icy;
                station.MetadataUrl = JsonValue.String(obj, "metadataUrl");
                result.Add(station);
            }
            catch (Exception ex)
            {
                errors?.Add("custom station #" + (index + 1) + ": " + ex.Message);
            }
        }
        return result;
    }

    internal static List<Station> LoadCachedCatalog(string modDirectory)
    {
        var path = Path.Combine(modDirectory, "catalog-cache.json");
        try
        {
            if (!File.Exists(path)) return new List<Station>();
            var root = MiniJson.Parse(File.ReadAllText(path)) as Dictionary<string, object>;
            if (root == null) return new List<Station>();
            var updated = JsonValue.String(root, "updatedUtc");
            DateTime stamp;
            if (!DateTime.TryParse(updated, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out stamp) ||
                DateTime.UtcNow - stamp > TimeSpan.FromDays(14)) return new List<Station>();
            return DeserializeStations(JsonValue.ChildArray(root, "stations"));
        }
        catch { return new List<Station>(); }
    }

    internal static void SaveCachedCatalog(string modDirectory, IEnumerable<Station> stations)
    {
        try
        {
            var root = new Dictionary<string, object>
            {
                { "version", 1 }, { "updatedUtc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) },
                { "stations", stations.Where(station => station != null && station.BuiltIn).Select(SerializeStation).Cast<object>().ToList() }
            };
            AtomicWrite(Path.Combine(modDirectory, "catalog-cache.json"), MiniJson.Stringify(root));
        }
        catch { }
    }

    internal static string TryMigrateLegacyVgmRadio(string modDirectory, RadioState state)
    {
        try
        {
            var parent = Directory.GetParent(modDirectory)?.FullName;
            if (parent == null) return null;
            var path = Path.Combine(parent, "VGMRadio", "VGMRadio.ini");
            if (!File.Exists(path)) return null;
            var source = "rainwave";
            var station = "all";
            var show = true;
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                var equals = line.IndexOf('=');
                if (equals <= 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                var key = line.Substring(0, equals).Trim();
                var value = line.Substring(equals + 1).Trim();
                if (key.Equals("Source", StringComparison.OrdinalIgnoreCase) || key.Equals("Provider", StringComparison.OrdinalIgnoreCase)) source = value;
                if (key.Equals("Station", StringComparison.OrdinalIgnoreCase)) station = value;
                if (key.Equals("ShowNowPlaying", StringComparison.OrdinalIgnoreCase)) bool.TryParse(value, out show);
            }
            if (source.Replace(" ", string.Empty).StartsWith("gtt", StringComparison.OrdinalIgnoreCase)) state.SelectedStationId = "gtt:main";
            else
            {
                var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    { "game", 1 }, { "gamemusic", 1 }, { "ocremix", 2 }, { "ocr", 2 }, { "covers", 3 },
                    { "cover", 3 }, { "chiptunes", 4 }, { "chiptune", 4 }, { "chip", 4 }, { "all", 5 }, { "chill", 6 }
                };
                int sid;
                if (!map.TryGetValue(station.Replace("-", string.Empty).Replace(" ", string.Empty), out sid)) sid = 5;
                state.SelectedStationId = "rainwave:" + sid;
            }
            state.SongNotifications = show;
            SaveState(modDirectory, state);
            return state.SelectedStationId;
        }
        catch { return null; }
    }

    private static void ApplyStateJson(RadioState state, string json)
    {
        var root = MiniJson.Parse(json) as Dictionary<string, object>;
        if (root == null) return;
        state.SelectedStationId = JsonValue.String(root, "selectedStationId", state.SelectedStationId);
        state.Playing = JsonValue.Bool(root, "playing", true);
        state.SongNotifications = JsonValue.Bool(root, "songNotifications", true);
        object volume;
        if (root.TryGetValue("volume", out volume))
        {
            try { state.Volume = Math.Max(0f, Math.Min(1f, Convert.ToSingle(volume, CultureInfo.InvariantCulture))); } catch { }
        }
        var favorites = JsonValue.ChildArray(root, "favorites");
        if (favorites != null) foreach (var value in favorites) state.Favorites.Add(Convert.ToString(value, CultureInfo.InvariantCulture));
        var recents = JsonValue.ChildArray(root, "recents");
        if (recents != null) foreach (var value in recents.Take(RecentLimit)) state.Recents.Add(Convert.ToString(value, CultureInfo.InvariantCulture));
    }

    private static Dictionary<string, object> SerializeStation(Station station)
    {
        return new Dictionary<string, object>
        {
            { "id", station.Id }, { "name", station.Name }, { "provider", station.Provider }, { "providerDisplay", station.ProviderDisplay },
            { "homePage", station.HomePage }, { "tags", station.Tags.Cast<object>().ToList() }, { "decades", station.Decades.Cast<object>().ToList() },
            { "metadataMode", station.MetadataMode.ToString() }, { "metadataUrl", station.MetadataUrl }, { "sourcePage", station.SourcePage },
            { "streams", station.Streams.Select(stream => (object)new Dictionary<string, object>
                {
                    { "url", stream.Url }, { "codec", stream.Codec }, { "bitrate", stream.BitrateKbps }, { "lossless", stream.Lossless },
                    { "publicFree", stream.PublicFree }, { "requiresAuth", stream.RequiresAuthentication }, { "resolver", stream.Resolver },
                    { "resolverArgument", stream.ResolverArgument }, { "label", stream.Label }
                }).ToList() }
        };
    }

    private static List<Station> DeserializeStations(List<object> list)
    {
        var result = new List<Station>();
        foreach (var item in list ?? new List<object>())
        {
            var obj = item as Dictionary<string, object>;
            if (obj == null) continue;
            var station = RadioCatalog.One(JsonValue.String(obj, "id"), JsonValue.String(obj, "name"), JsonValue.String(obj, "provider"), JsonValue.String(obj, "providerDisplay"), JsonValue.String(obj, "homePage"));
            var tags = JsonValue.ChildArray(obj, "tags");
            if (tags != null) foreach (var tag in tags) station.AddTags(Convert.ToString(tag, CultureInfo.InvariantCulture));
            var decades = JsonValue.ChildArray(obj, "decades");
            if (decades != null) foreach (var decade in decades) { try { station.AddDecades(Convert.ToInt32(decade, CultureInfo.InvariantCulture)); } catch { } }
            MetadataMode mode;
            if (Enum.TryParse(JsonValue.String(obj, "metadataMode"), true, out mode)) station.MetadataMode = mode;
            station.MetadataUrl = JsonValue.String(obj, "metadataUrl");
            station.SourcePage = JsonValue.String(obj, "sourcePage");
            foreach (var streamItem in JsonValue.ChildArray(obj, "streams") ?? new List<object>())
            {
                var stream = streamItem as Dictionary<string, object>;
                if (stream == null) continue;
                station.Streams.Add(new StreamVariant
                {
                    Url = JsonValue.String(stream, "url"), Codec = JsonValue.String(stream, "codec"), BitrateKbps = JsonValue.Int(stream, "bitrate"),
                    Lossless = JsonValue.Bool(stream, "lossless"), PublicFree = JsonValue.Bool(stream, "publicFree", true),
                    RequiresAuthentication = JsonValue.Bool(stream, "requiresAuth"), Resolver = JsonValue.String(stream, "resolver"),
                    ResolverArgument = JsonValue.String(stream, "resolverArgument"), Label = JsonValue.String(stream, "label")
                });
            }
            if (station.Id.Length > 0 && station.Streams.Count > 0) result.Add(station);
        }
        return result;
    }

    private static void AtomicWrite(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var temp = path + ".tmp";
        File.WriteAllText(temp, text ?? string.Empty);
        if (!File.Exists(path))
        {
            File.Move(temp, path);
            return;
        }
        try
        {
            File.Replace(temp, path, null);
        }
        catch (IOException)
        {
            File.Delete(path);
            File.Move(temp, path);
        }
        catch (PlatformNotSupportedException)
        {
            File.Delete(path);
            File.Move(temp, path);
        }
    }
}
#endif
