#if !GLOADER_SERVER
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

internal static class RadioDirectories
{
    internal static List<Station> SearchAll(string query)
    {
        var result = new List<Station>();
        if (string.IsNullOrWhiteSpace(query)) return result;
        try { result.AddRange(SearchLautFm(query)); } catch { }
        try { result.AddRange(SearchRadioBrowser(query)); } catch { }
        return result
            .GroupBy(station => station.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(40)
            .ToList();
    }

    internal static List<Station> SearchLautFm(string query)
    {
        var url = "https://api.laut.fm/search/stations?query=" + Uri.EscapeDataString(query) + "&limit=20";
        return ParseLautFmSearch(MiniJson.Parse(RadioNet.DownloadText(url, 9000)));
    }

    internal static List<Station> ParseLautFmSearch(object payload)
    {
        var result = new List<Station>();
        var stationObjects = new List<Dictionary<string, object>>();

        var rootObject = payload as Dictionary<string, object>;
        if (rootObject != null)
        {
            foreach (var groupItem in JsonValue.ChildArray(rootObject, "results") ?? new List<object>())
            {
                var group = groupItem as Dictionary<string, object>;
                if (group == null) continue;
                foreach (var itemValue in JsonValue.ChildArray(group, "items") ?? new List<object>())
                {
                    var item = itemValue as Dictionary<string, object>;
                    if (item == null) continue;
                    stationObjects.Add(JsonValue.ChildObject(item, "station") ?? item);
                }
            }
        }
        else
        {
            foreach (var itemValue in payload as List<object> ?? new List<object>())
            {
                var item = itemValue as Dictionary<string, object>;
                if (item != null) stationObjects.Add(JsonValue.ChildObject(item, "station") ?? item);
            }
        }

        foreach (var obj in stationObjects)
        {
            var name = JsonValue.String(obj, "name").Trim();
            if (name.Length == 0) continue;
            var display = JsonValue.String(obj, "display_name", name).Trim();
            var station = RadioCatalog.One("laut:" + RadioTaxonomy.Slug(name), display, "laut.fm", "laut.fm", "https://laut.fm/" + Uri.EscapeDataString(name), "Radio");
            station.BuiltIn = false;
            station.LiveDirectory = true;
            station.DirectorySource = "laut.fm live";
            station.Streams.Add(RadioCatalog.Variant("https://stream.laut.fm/" + Uri.EscapeDataString(name), "mp3", 128, "direct", "laut.fm MP3"));
            station.MetadataMode = MetadataMode.LautFm;
            station.MetadataUrl = "https://api.laut.fm/station/" + Uri.EscapeDataString(name) + "/current_song";
            var genres = JsonValue.ChildArray(obj, "genres");
            if (genres != null)
            {
                foreach (var genreItem in genres)
                {
                    var genreObj = genreItem as Dictionary<string, object>;
                    station.AddTags(genreObj == null ? Convert.ToString(genreItem, CultureInfo.InvariantCulture) : JsonValue.String(genreObj, "name"));
                }
            }
            var decade = RadioTaxonomy.InferDecade(display + " " + string.Join(" ", station.Tags));
            if (decade > 0) station.AddDecades(decade);
            result.Add(station);
        }

        return result
            .GroupBy(station => station.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    internal static List<Station> SearchRadioBrowser(string query, int limit = 30)
    {
        limit = Math.Max(1, Math.Min(250, limit));
        // 113.FM's current public directory records are not consistently punctuated
        // ("113.FM", "113FM", and "113.fm" all exist). A punctuation-free provider
        // query catches the complete family, after which callers still filter provider
        // identity/homepage/stream hosts before treating results as 113.FM stations.
        var effectiveQuery = string.Equals(query, "113.FM", StringComparison.OrdinalIgnoreCase) ? "113" : query;
        Exception last = null;
        foreach (var baseUrl in RadioBrowserServers())
        {
            try
            {
                var url = baseUrl + "/json/stations/search?hidebroken=true&limit=" + limit + "&order=bitrate&reverse=true&name=" + Uri.EscapeDataString(effectiveQuery);
                var root = MiniJson.Parse(RadioNet.DownloadText(url, 9000)) as List<object>;
                return ParseRadioBrowserResults(root);
            }
            catch (Exception ex) { last = ex; }
        }
        if (last != null) throw last;
        return new List<Station>();
    }

    internal static List<Station> ParseRadioBrowserResults(List<object> root)
    {
        var result = new List<Station>();
        foreach (var item in root ?? new List<object>())
        {
            var obj = item as Dictionary<string, object>;
            if (obj == null || !JsonValue.Bool(obj, "lastcheckok", true)) continue;
            var uuid = JsonValue.String(obj, "stationuuid");
            var name = JsonValue.String(obj, "name").Trim();
            var stream = JsonValue.String(obj, "url_resolved");
            if (stream.Length == 0) stream = JsonValue.String(obj, "url");
            var codec = JsonValue.String(obj, "codec");
            if (uuid.Length == 0 || name.Length == 0 || stream.Length == 0 || !StreamRanking.IsCompatibleCodec(codec)) continue;
            var station = RadioCatalog.One("radiobrowser:" + uuid, name, "radio-browser", "Radio Browser", JsonValue.String(obj, "homepage"), "Radio");
            station.BuiltIn = false;
            station.LiveDirectory = true;
            station.DirectorySource = "Radio Browser live";
            station.Streams.Add(RadioCatalog.Variant(stream, codec, JsonValue.Int(obj, "bitrate"), "direct", "Directory stream"));
            station.MetadataMode = MetadataMode.Icy;
            foreach (var tag in JsonValue.String(obj, "tags").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Take(8)) station.AddTags(tag);
            var decade = RadioTaxonomy.InferDecade(station.Name + " " + JsonValue.String(obj, "tags"));
            if (decade > 0) station.AddDecades(decade);
            result.Add(station);
        }
        return result;
    }

    internal static void CountRadioBrowserClick(Station station)
    {
        if (station == null || !station.Id.StartsWith("radiobrowser:", StringComparison.OrdinalIgnoreCase)) return;
        var uuid = station.Id.Substring("radiobrowser:".Length);
        System.Threading.ThreadPool.QueueUserWorkItem(_ =>
        {
            foreach (var baseUrl in RadioBrowserServers())
            {
                try { RadioNet.DownloadText(baseUrl + "/json/url/" + Uri.EscapeDataString(uuid), 4000); return; } catch { }
            }
        });
    }

    private static List<string> RadioBrowserServers()
    {
        var result = new List<string>();
        try
        {
            var root = MiniJson.Parse(RadioNet.DownloadText("https://all.api.radio-browser.info/json/servers", 5000)) as List<object>;
            foreach (var item in root ?? new List<object>())
            {
                var obj = item as Dictionary<string, object>;
                var name = JsonValue.String(obj, "name");
                if (name.Length > 0) result.Add("https://" + name.TrimEnd('/'));
            }
        }
        catch { }
        foreach (var fallback in new[] { "https://de1.api.radio-browser.info", "https://de2.api.radio-browser.info", "https://at1.api.radio-browser.info" })
            if (!result.Contains(fallback, StringComparer.OrdinalIgnoreCase)) result.Add(fallback);
        var offset = result.Count == 0 ? 0 : Math.Abs(Environment.TickCount) % result.Count;
        return result.Skip(offset).Concat(result.Take(offset)).ToList();
    }
}
#endif
