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
    private const string UserAgent = "gloader-radio/2.0 (+https://github.com/polskiftw/gloader)";

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
        throw new NotSupportedException("Unknown Radio stream resolver: " + variant.Resolver);
    }

    internal static string ResolvePlaylist(string playlistUrl)
    {
        var text = DownloadText(playlistUrl, 7000);
        var urls = ExtractHttpUrls(text);
        if (urls.Count == 0) throw new InvalidDataException("Playlist did not contain a stream URL: " + playlistUrl);
        return urls[0];
    }

    private static string ResolveRainwave(string argument)
    {
        var sid = 5;
        int parsed;
        if (int.TryParse(argument, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) && parsed >= 1 && parsed <= 6)
            sid = parsed;
        return ResolvePlaylist("https://rainwave.cc/tune_in/" + sid.ToString(CultureInfo.InvariantCulture) + ".mp3.m3u");
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
}
#endif
