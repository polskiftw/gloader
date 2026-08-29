#if !GLOADER_SERVER
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
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

        var found = new ConcurrentDictionary<string, Station>(StringComparer.OrdinalIgnoreCase);
        var candidates = BuildCandidates().ToArray();
        Parallel.ForEach(
            candidates,
            new ParallelOptions { MaxDegreeOfParallelism = 28 },
            candidate =>
            {
                Station station;
                if (!TryProbe(candidate, out station) || station == null) return;
                found.TryAdd(station.Id, station);
            });

        var result = found.Values
            .OrderBy(station => station.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        lock (CacheLock)
        {
            _cached = result;
            _cachedUtc = DateTime.UtcNow;
            return _cached.ToList();
        }
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
                var request = RadioNet.CreateRequest(url, 1800, true);
                request.KeepAlive = false;
                request.ServicePoint.ConnectionLimit = Math.Max(request.ServicePoint.ConnectionLimit, 32);
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    if (response.StatusCode != HttpStatusCode.OK) continue;
                    var contentType = (response.ContentType ?? string.Empty).ToLowerInvariant();
                    if (contentType.Length > 0 && !contentType.Contains("audio") && !contentType.Contains("mpeg") && !contentType.Contains("mp3")) continue;

                    var rawName = (response.GetResponseHeader("icy-name") ?? string.Empty).Trim();
                    var rawGenre = (response.GetResponseHeader("icy-genre") ?? string.Empty).Trim();
                    var rawBitrate = (response.GetResponseHeader("icy-br") ?? string.Empty).Trim();
                    int bitrate;
                    if (!int.TryParse(rawBitrate.Split(',').FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out bitrate) || bitrate <= 0)
                        bitrate = 128;

                    var name = NormalizeStationName(rawName, candidate.Key);
                    var id = "113fm:direct-" + candidate.Key;
                    var result = RadioCatalog.One(id, name, "113fm", "113.FM", "https://113fmradio.com/", "Radio");
                    result.SourcePage = "113.FM public free stream discovery";
                    result.Streams.Add(RadioCatalog.Variant(url, "mp3", bitrate, "direct", bitrate + "k MP3"));
                    result.MetadataMode = MetadataMode.Icy;
                    if (!string.IsNullOrWhiteSpace(rawGenre))
                    {
                        foreach (var genre in rawGenre.Split(new[] { ',', ';', '/' }, StringSplitOptions.RemoveEmptyEntries))
                            result.AddTags(genre);
                    }
                    var decade = RadioTaxonomy.InferDecade(name + " " + rawGenre);
                    if (decade > 0) result.AddDecades(decade);
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

    private static string NormalizeStationName(string rawName, string key)
    {
        var value = (rawName ?? string.Empty).Trim();
        if (value.Length == 0 || value == "." || value.Equals("113.FM", StringComparison.OrdinalIgnoreCase) || value.Equals("113FM", StringComparison.OrdinalIgnoreCase))
            return "113.FM Channel " + key.Substring(key.IndexOf('-') + 1);
        if (value.StartsWith("113.fm", StringComparison.OrdinalIgnoreCase) || value.StartsWith("113FM", StringComparison.OrdinalIgnoreCase) || value.StartsWith("113.FM", StringComparison.OrdinalIgnoreCase))
            return value;
        return "113.FM " + value;
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
