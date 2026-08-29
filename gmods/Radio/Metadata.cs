#if !GLOADER_SERVER
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

internal static class RadioMetadata
{
    internal static bool TryReadTrack(Station station, out TrackInfo track)
    {
        track = null;
        if (station == null || station.MetadataMode == MetadataMode.None) return false;
        if (station.MetadataMode == MetadataMode.Rainwave) return TryRainwave(station.MetadataUrl, out track);
        if (station.MetadataMode == MetadataMode.LautFm) return TryLautFm(station.MetadataUrl, out track);
        if (station.MetadataMode == MetadataMode.WebPage && !string.IsNullOrWhiteSpace(station.MetadataUrl)) return TryWebPage(station, out track);

        foreach (var variant in StreamRanking.Rank(station.Streams))
        {
            try
            {
                var url = RadioNet.ResolveStreamVariant(station, variant);
                var title = ReadIcyStreamTitle(url, 6000, 4);
                if (IsTrackLike(title, station)) { track = TrackInfo.FromDisplay(title); return true; }
            }
            catch { }
        }
        return false;
    }

    internal static bool TryParseRainwaveNowPlayingJson(string json, out TrackInfo track)
    {
        track = null;
        try
        {
            var root = MiniJson.Parse(json) as Dictionary<string, object>;
            var current = JsonValue.ChildObject(root, "sched_current");
            if (current == null) return false;
            Dictionary<string, object> song = null;
            var songs = JsonValue.ChildArray(current, "songs");
            if (songs != null && songs.Count > 0) song = songs[0] as Dictionary<string, object>;
            if (song == null) song = JsonValue.ChildObject(current, "song_data");
            if (song == null) return false;
            var title = JsonValue.String(song, "title").Trim();
            if (title.Length == 0) return false;
            var artistNames = new List<string>();
            foreach (var artistItem in JsonValue.ChildArray(song, "artists") ?? new List<object>())
            {
                var artist = artistItem as Dictionary<string, object>;
                var name = artist == null ? Convert.ToString(artistItem, CultureInfo.InvariantCulture) : JsonValue.String(artist, "name");
                if (!string.IsNullOrWhiteSpace(name) && !artistNames.Contains(name, StringComparer.OrdinalIgnoreCase)) artistNames.Add(name.Trim());
            }
            track = new TrackInfo { Artist = string.Join(", ", artistNames), Title = title, Raw = (artistNames.Count == 0 ? title : string.Join(", ", artistNames) + " - " + title), ReceivedUtc = DateTime.UtcNow };
            return true;
        }
        catch { return false; }
    }

    internal static bool TryParseLautFmCurrentSong(string json, out TrackInfo track)
    {
        track = null;
        try
        {
            var root = MiniJson.Parse(json) as Dictionary<string, object>;
            if (root == null) return false;
            var title = JsonValue.String(root, "title").Trim();
            var artist = JsonValue.ChildObject(root, "artist");
            var artistName = JsonValue.String(artist, "name").Trim();
            if (title.Length == 0) return false;
            track = new TrackInfo { Artist = artistName, Title = title, Raw = artistName.Length == 0 ? title : artistName + " - " + title, ReceivedUtc = DateTime.UtcNow };
            return true;
        }
        catch { return false; }
    }

    internal static string ExtractIcyStreamTitle(string metadata)
    {
        if (string.IsNullOrEmpty(metadata)) return null;
        var match = Regex.Match(metadata, @"(?:^|;)\s*StreamTitle\s*=\s*'(?<title>.*?)'\s*;", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
        if (!match.Success) match = Regex.Match(metadata, @"StreamTitle\s*=\s*""(?<title>.*?)""", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
        return match.Success ? WebUtility.HtmlDecode(match.Groups["title"].Value).Trim() : null;
    }

    internal static bool IsTrackLike(string title, Station station)
    {
        var value = (title ?? string.Empty).Trim();
        if (value.Length < 2 || value.Length > 240) return false;
        var lower = value.ToLowerInvariant();
        if (station != null && string.Equals(value, station.Name, StringComparison.OrdinalIgnoreCase)) return false;
        if (lower.Contains("you are listening") || lower.Contains("you're listening") || lower.Contains("station id") || lower.Contains("advertisement")) return false;
        return true;
    }

    internal static string ReadIcyStreamTitle(string url, int timeoutMilliseconds, int metadataBlocks)
    {
        var request = RadioNet.CreateRequest(url, timeoutMilliseconds, true);
        using (var response = (HttpWebResponse)request.GetResponse())
        {
            int interval;
            if (!int.TryParse(response.GetResponseHeader("icy-metaint"), NumberStyles.Integer, CultureInfo.InvariantCulture, out interval) || interval <= 0)
                throw new InvalidDataException("Stream did not provide icy-metaint.");
            using (var stream = response.GetResponseStream())
            {
                for (var attempt = 0; attempt < metadataBlocks; attempt++)
                {
                    SkipExactly(stream, interval);
                    var length = stream.ReadByte();
                    if (length < 0) throw new EndOfStreamException();
                    if (length == 0) continue;
                    var buffer = new byte[length * 16];
                    ReadExactly(stream, buffer, 0, buffer.Length);
                    var title = ExtractIcyStreamTitle(Encoding.UTF8.GetString(buffer).TrimEnd('\0'));
                    if (!string.IsNullOrWhiteSpace(title)) return title.Trim();
                }
            }
        }
        throw new InvalidDataException("No ICY title was received.");
    }

    private static bool TryRainwave(string url, out TrackInfo track)
    {
        try { return TryParseRainwaveNowPlayingJson(RadioNet.DownloadText(url, 6000), out track); }
        catch { track = null; return false; }
    }

    private static bool TryLautFm(string url, out TrackInfo track)
    {
        try { return TryParseLautFmCurrentSong(RadioNet.DownloadText(url, 6000), out track); }
        catch { track = null; return false; }
    }

    private static bool TryWebPage(Station station, out TrackInfo track)
    {
        track = null;
        try
        {
            var html = RadioNet.DownloadText(station.MetadataUrl, 7000);
            var patterns = new[]
            {
                @"Now\s*Playing[\s\S]{0,500}?<[^>]+>(?<title>[^<>]{3,200})</",
                @"Playing\s*now\s*:[\s\S]{0,300}?(?<title>[A-Za-z0-9][^<\r\n]{2,200})"
            };
            foreach (var pattern in patterns)
            {
                var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (!match.Success) continue;
                var title = WebUtility.HtmlDecode(Regex.Replace(match.Groups["title"].Value, "<.*?>", string.Empty)).Trim();
                if (IsTrackLike(title, station)) { track = TrackInfo.FromDisplay(title); return true; }
            }
        }
        catch { }
        return false;
    }

    private static void SkipExactly(Stream stream, int count)
    {
        var buffer = new byte[Math.Min(8192, Math.Max(1, count))];
        while (count > 0)
        {
            var read = stream.Read(buffer, 0, Math.Min(buffer.Length, count));
            if (read <= 0) throw new EndOfStreamException();
            count -= read;
        }
    }

    private static void ReadExactly(Stream stream, byte[] buffer, int offset, int count)
    {
        while (count > 0)
        {
            var read = stream.Read(buffer, offset, count);
            if (read <= 0) throw new EndOfStreamException();
            offset += read;
            count -= read;
        }
    }
}

internal static class MetadataProbe
{
    internal static bool HasUsableTrackMetadata(Station station, out string firstTitle)
    {
        firstTitle = null;
        TrackInfo track;
        if (!RadioMetadata.TryReadTrack(station, out track) || track == null || string.IsNullOrWhiteSpace(track.Display)) return false;
        firstTitle = track.Display;
        return RadioMetadata.IsTrackLike(firstTitle, station);
    }
}
#endif
