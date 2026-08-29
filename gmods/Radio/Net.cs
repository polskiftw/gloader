#if !GLOADER_SERVER
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

internal static class RadioNet
{
    private const string UserAgent = "gloader-radio/1.0 (+https://github.com/polskiftw/gloader)";

    internal static HttpWebRequest CreateRequest(string url, int timeoutMilliseconds, bool icy = false)
    {
        var request = (HttpWebRequest)WebRequest.Create(url);
        request.Method = "GET";
        request.UserAgent = UserAgent;
        request.Accept = "*/*";
        request.Timeout = timeoutMilliseconds;
        request.ReadWriteTimeout = timeoutMilliseconds;
        request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
        request.AllowAutoRedirect = true;
        request.MaximumAutomaticRedirections = 6;
        request.KeepAlive = !icy;
        if (icy) request.Headers["Icy-MetaData"] = "1";
        return request;
    }

    internal static string DownloadText(string url, int timeoutMilliseconds = 7000)
    {
        using (var response = (HttpWebResponse)CreateRequest(url, timeoutMilliseconds).GetResponse())
        using (var stream = response.GetResponseStream())
        using (var reader = new StreamReader(stream, Encoding.UTF8, true))
            return reader.ReadToEnd();
    }

    internal static string ResolveStreamVariant(Station station, StreamVariant variant)
    {
        if (variant == null) throw new InvalidDataException("Missing stream variant.");
        var resolver = (variant.Resolver ?? string.Empty).Trim().ToLowerInvariant();
        if (resolver.Length == 0 || resolver == "direct") return variant.Url;
        if (resolver == "playlist") return ResolvePlaylist(variant.Url);
        if (resolver == "rainwave") return ResolveRainwave(variant.ResolverArgument);
        if (resolver == "station-page") return ResolveStationPage(variant.Url, variant.ResolverArgument);
        if (resolver == "radio-browser-exact") return ResolveRadioBrowserExact(station, variant.ResolverArgument);
        throw new NotSupportedException("Unknown stream resolver: " + variant.Resolver);
    }

    internal static string ResolvePlaylist(string playlistUrl)
    {
        var text = DownloadText(playlistUrl, 7000);
        var urls = ExtractHttpUrls(text);
        var supported = urls.FirstOrDefault(url => IsLikelyAudioUrl(url));
        if (!string.IsNullOrEmpty(supported)) return supported;
        if (urls.Count > 0) return urls[0];
        throw new InvalidDataException("Playlist did not contain a stream URL: " + playlistUrl);
    }

    private static string ResolveRainwave(string argument)
    {
        var sid = 5;
        int.TryParse(argument, NumberStyles.Integer, CultureInfo.InvariantCulture, out sid);
        try { return ResolvePlaylist("https://rainwave.cc/tune_in/" + sid + ".mp3.m3u"); }
        catch
        {
            var mounts = new Dictionary<int, string> { { 1, "game" }, { 2, "ocremix" }, { 3, "covers" }, { 4, "chiptune" }, { 5, "all" }, { 6, "chill" } };
            string mount;
            if (!mounts.TryGetValue(sid, out mount)) mount = "all";
            return "https://gamestream.rainwave.cc/" + mount + ".mp3";
        }
    }

    private static string ResolveStationPage(string pageUrl, string providerHint)
    {
        var html = DownloadText(pageUrl, 8000);
        var candidates = ExtractPageUrls(pageUrl, html)
            .Where(url => IsLikelyAudioUrl(url) || url.IndexOf(".m3u", StringComparison.OrdinalIgnoreCase) >= 0 || url.IndexOf(".pls", StringComparison.OrdinalIgnoreCase) >= 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var ranked = candidates
            .OrderByDescending(url => PageCandidateScore(url, providerHint))
            .ToList();
        foreach (var candidate in ranked)
        {
            try
            {
                if (candidate.IndexOf(".m3u", StringComparison.OrdinalIgnoreCase) >= 0 || candidate.IndexOf(".pls", StringComparison.OrdinalIgnoreCase) >= 0)
                    return ResolvePlaylist(candidate);
                return candidate;
            }
            catch { }
        }
        throw new InvalidDataException("No compatible public stream was found on station page: " + pageUrl);
    }

    internal static List<string> ExtractPageUrls(string pageUrl, string html)
    {
        var results = ExtractHttpUrls(html);
        Uri baseUri;
        if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out baseUri)) return results;
        foreach (Match match in Regex.Matches(html ?? string.Empty, @"(?:href|src)\s*=\s*[""'](?<url>[^""']+)[""']", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            var raw = WebUtility.HtmlDecode(match.Groups["url"].Value.Trim());
            Uri resolved;
            if (!Uri.TryCreate(baseUri, raw, out resolved)) continue;
            if (resolved.Scheme != Uri.UriSchemeHttp && resolved.Scheme != Uri.UriSchemeHttps) continue;
            var value = resolved.AbsoluteUri;
            if (!results.Contains(value, StringComparer.OrdinalIgnoreCase)) results.Add(value);
        }
        return results;
    }

    private static int PageCandidateScore(string url, string providerHint)
    {
        var value = (url ?? string.Empty).ToLowerInvariant();
        var score = 0;
        if (value.Contains("320")) score += 1000;
        if (value.Contains("256")) score += 900;
        if (value.Contains("192")) score += 800;
        if (value.Contains("128")) score += 700;
        if (value.Contains(".mp3") || value.Contains("mpeg")) score += 500;
        if (value.Contains("aac")) score += 450;
        if (value.Contains(".ogg") || value.Contains("opus")) score -= 1000;
        if (string.Equals(providerHint, "radcap", StringComparison.OrdinalIgnoreCase))
        {
            if (value.Contains("/rc2/") || value.Contains("rc2")) score += 2500;
            if (value.Contains("/rc3/") || value.Contains("rc3")) score -= 500; // advertised reserve server, not the public first choice
        }
        if (string.Equals(providerHint, "113fm", StringComparison.OrdinalIgnoreCase) && value.Contains("listen.113fm.net")) score += 1200;
        return score;
    }

    internal static string ResolveRadioBrowserExact(Station station, string queryOverride)
    {
        var query = string.IsNullOrWhiteSpace(queryOverride) ? station.Name : queryOverride;
        var url = "https://all.api.radio-browser.info/json/stations/search?hidebroken=true&limit=20&order=bitrate&reverse=true&name=" + Uri.EscapeDataString(query);
        var root = MiniJson.Parse(DownloadText(url, 8000)) as List<object>;
        if (root == null) throw new InvalidDataException("Radio Browser returned no station array.");

        var candidates = new List<Tuple<int, string>>();
        foreach (var item in root)
        {
            var obj = item as Dictionary<string, object>;
            if (obj == null) continue;
            var resolved = JsonValue.String(obj, "url_resolved");
            if (string.IsNullOrWhiteSpace(resolved)) resolved = JsonValue.String(obj, "url");
            var codec = JsonValue.String(obj, "codec");
            if (!StreamRanking.IsCompatibleCodec(codec) || string.IsNullOrWhiteSpace(resolved)) continue;
            var score = JsonValue.Int(obj, "bitrate") + (JsonValue.Bool(obj, "lastcheckok") ? 10000 : 0);
            var homepage = JsonValue.String(obj, "homepage");
            if (!string.IsNullOrWhiteSpace(station.HomePage) && !string.IsNullOrWhiteSpace(homepage))
            {
                try
                {
                    if (string.Equals(new Uri(station.HomePage).Host.TrimStart('w','.'), new Uri(homepage).Host.TrimStart('w','.'), StringComparison.OrdinalIgnoreCase))
                        score += 20000;
                }
                catch { }
            }
            candidates.Add(Tuple.Create(score, resolved));
        }
        if (candidates.Count == 0) throw new InvalidDataException("No compatible Radio Browser stream matched " + query + ".");
        return candidates.OrderByDescending(pair => pair.Item1).First().Item2;
    }

    internal static List<string> ExtractHttpUrls(string text)
    {
        var results = new List<string>();
        if (string.IsNullOrEmpty(text)) return results;
        foreach (Match match in Regex.Matches(text, @"https?://[^\s""'<>\\]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            var value = WebUtility.HtmlDecode(match.Value).TrimEnd(')', ']', '}', ',', ';');
            if (!results.Contains(value, StringComparer.OrdinalIgnoreCase)) results.Add(value);
        }
        return results;
    }

    private static bool IsLikelyAudioUrl(string url)
    {
        var value = (url ?? string.Empty).ToLowerInvariant();
        return value.Contains(".mp3") || value.Contains(".aac") || value.Contains("audio") ||
               value.Contains(":8000") || value.Contains(":8002") || value.Contains(":8010") ||
               value.Contains(":8100") || value.Contains(":8300") || value.Contains("stream");
    }
}
#endif
