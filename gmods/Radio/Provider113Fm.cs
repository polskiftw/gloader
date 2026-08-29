#if !GLOADER_SERVER
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

internal static class Radio113Fm
{
    private static readonly object CacheLock = new object();
    private static List<Station> _cached = new List<Station>();
    private static DateTime _cachedUtc = DateTime.MinValue;

    internal static List<Station> Discover(bool forceRefresh = false)
    {
        lock (CacheLock)
        {
            if (!forceRefresh && _cached.Count > 0 && DateTime.UtcNow - _cachedUtc < TimeSpan.FromMinutes(30))
                return _cached.ToList();
        }

        var found = new ConcurrentBag<Station>();
        var candidates = BuildCandidates().ToArray();
        Parallel.ForEach(
            candidates,
            new ParallelOptions { MaxDegreeOfParallelism = 28 },
            candidate =>
            {
                Station station;
                if (!TryProbe(candidate, out station) || station == null) return;
                found.Add(station);
            });

        // 113.FM currently mirrors some named channels across both StreamGuys and
        // CDNStream IDs. Present one logical station and retain every validated URL as
        // ranked failover instead of inflating the catalog with duplicate rows.
        var result = found
            .GroupBy(station => station.Name, StringComparer.OrdinalIgnoreCase)
            .Select(MergeNamedStation)
            .Where(station => station != null)
            .OrderBy(station => station.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        lock (CacheLock)
        {
            _cached = result;
            _cachedUtc = DateTime.UtcNow;
            return _cached.ToList();
        }
    }

    private static Station MergeNamedStation(IGrouping<string, Station> group)
    {
        var items = group == null ? new List<Station>() : group.ToList();
        if (items.Count == 0) return null;
        var first = items
            .OrderByDescending(station => StreamRanking.Rank(station.Streams).FirstOrDefault()?.BitrateKbps ?? 0)
            .ThenBy(station => station.Id, StringComparer.OrdinalIgnoreCase)
            .First();

        var stableName = first.Name.StartsWith("113.FM ", StringComparison.OrdinalIgnoreCase)
            ? first.Name.Substring("113.FM ".Length)
            : first.Name;
        first.Id = "113fm:" + RadioTaxonomy.Slug(stableName);
        first.Streams.Clear();
        foreach (var station in items)
        {
            first.AddTags(station.Tags.ToArray());
            first.AddDecades(station.Decades.ToArray());
            foreach (var stream in station.Streams)
            {
                if (!first.Streams.Any(existing => string.Equals(existing.Url, stream.Url, StringComparison.OrdinalIgnoreCase)))
                    first.Streams.Add(stream.Clone());
            }
        }
        return first;
    }

    private static IEnumerable<Candidate> BuildCandidates()
    {
        // The current free network uses two public families. The StreamGuys family
        // occupies the low numeric range while newer channels use CDNStream IDs.
        // Probe ranges rather than freezing a hand-curated snapshot so channels can
        // appear/disappear without requiring a Radio release.
        for (var id = 1000; id <= 1099; id++)
            yield return new Candidate("atunwa-" + id, "https://113fm-atunwadigital.streamguys1.com/" + id);

        for (var id = 1700; id <= 1899; id++)
        {
            // Most current channels answer on both CDN edges, but some are published
            // on only one. Probe edge2 first and fall back to edge1 for the same ID.
            yield return new Candidate("cdn-" + id, "https://113fm-edge2.cdnstream.com/" + id + "_128", "https://113fm-edge1.cdnstream.com/" + id + "_128");
        }
    }

    private static bool TryProbe(Candidate candidate, out Station station)
    {
        station = null;
        foreach (var url in candidate.Urls)
        {
            try
            {
                var request = RadioNet.CreateRequest(url, 2200, true);
                request.KeepAlive = false;
                request.ServicePoint.ConnectionLimit = Math.Max(request.ServicePoint.ConnectionLimit, 32);
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    if (response.StatusCode != HttpStatusCode.OK) continue;
                    var contentType = (response.ContentType ?? string.Empty).ToLowerInvariant();
                    if (contentType.Length > 0 && !contentType.Contains("audio") && !contentType.Contains("mpeg") && !contentType.Contains("mp3")) continue;

                    var rawName = RepairHeaderText((response.GetResponseHeader("icy-name") ?? string.Empty).Trim());
                    var rawGenre = RepairHeaderText((response.GetResponseHeader("icy-genre") ?? string.Empty).Trim());
                    var rawBitrate = (response.GetResponseHeader("icy-br") ?? string.Empty).Trim();
                    int bitrate;
                    if (!int.TryParse(rawBitrate.Split(',').FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out bitrate) || bitrate <= 0)
                        bitrate = 128;

                    var name = NormalizeStationName(rawName);
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    var result = RadioCatalog.One("113fm:probe-" + candidate.Key, name, "113fm", "113.FM", "https://113fmradio.com/", "Radio");
                    result.SourcePage = "113.FM public free stream discovery; ICY track metadata observed";
                    result.Streams.Add(RadioCatalog.Variant(url, "mp3", bitrate, "direct", bitrate + "k MP3"));
                    result.MetadataMode = MetadataMode.Icy;
                    if (!string.IsNullOrWhiteSpace(rawGenre))
                    {
                        foreach (var genre in rawGenre.Split(new[] { ',', ';', '/' }, StringSplitOptions.RemoveEmptyEntries))
                            result.AddTags(genre);
                    }
                    var decade = RadioTaxonomy.InferDecade(name + " " + rawGenre);
                    if (decade > 0) result.AddDecades(decade);

                    // The handoff requires 113.FM entries to pass a metadata probe, not
                    // merely answer HTTP. Read the stream's first few ICY blocks and
                    // require a track-like title distinct from station branding.
                    var title = TryReadIcyTitle(response, 4);
                    if (!RadioMetadata.IsTrackLike(title, result)) continue;

                    station = result;
                    return true;
                }
            }
            catch
            {
                // Probe failure means this numbered channel is not currently public on
                // this edge. Discovery intentionally continues through the whole range.
            }
        }
        return false;
    }

    private static string TryReadIcyTitle(HttpWebResponse response, int metadataBlocks)
    {
        if (response == null) return null;
        int interval;
        if (!int.TryParse(response.GetResponseHeader("icy-metaint"), NumberStyles.Integer, CultureInfo.InvariantCulture, out interval) || interval <= 0)
            return null;
        var stream = response.GetResponseStream();
        if (stream == null) return null;
        var skip = new byte[Math.Min(8192, Math.Max(1, interval))];
        for (var block = 0; block < Math.Max(1, metadataBlocks); block++)
        {
            var remaining = interval;
            while (remaining > 0)
            {
                var read = stream.Read(skip, 0, Math.Min(skip.Length, remaining));
                if (read <= 0) return null;
                remaining -= read;
            }
            var lengthByte = stream.ReadByte();
            if (lengthByte < 0) return null;
            var byteCount = lengthByte * 16;
            if (byteCount <= 0) continue;
            var metadata = new byte[byteCount];
            var offset = 0;
            while (offset < metadata.Length)
            {
                var read = stream.Read(metadata, offset, metadata.Length - offset);
                if (read <= 0) return null;
                offset += read;
            }
            var title = RadioMetadata.ExtractIcyStreamTitle(Encoding.UTF8.GetString(metadata).TrimEnd('\0'));
            if (!string.IsNullOrWhiteSpace(title)) return title.Trim();
        }
        return null;
    }

    private static string NormalizeStationName(string rawName)
    {
        var value = (rawName ?? string.Empty).Trim().TrimStart('.').Trim();
        foreach (var prefix in new[] { "113.FM", "113FM", "113.fm" })
        {
            if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            value = value.Substring(prefix.Length).TrimStart(' ', '-', ':', '.').Trim();
            break;
        }

        // A generic provider-only icy-name is not enough to identify a station and can
        // be returned by parking/default mounts. Ignore it instead of manufacturing a
        // fake channel name that would inflate the catalog.
        if (value.Length < 2 || value.Equals("Radio", StringComparison.OrdinalIgnoreCase)) return null;
        return "113.FM " + value;
    }

    private static string RepairHeaderText(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        try
        {
            // HttpWebResponse exposes HTTP header bytes as ISO-8859-1. Some Icecast
            // servers put UTF-8 names in those bytes, producing visible mojibake unless
            // we reinterpret them. ASCII remains unchanged by this round-trip.
            return Encoding.UTF8.GetString(Encoding.GetEncoding(28591).GetBytes(value));
        }
        catch
        {
            return value;
        }
    }

    private sealed class Candidate
    {
        internal readonly string Key;
        internal readonly string[] Urls;

        internal Candidate(string key, params string[] urls)
        {
            Key = key;
            Urls = urls ?? new string[0];
        }
    }
}
#endif