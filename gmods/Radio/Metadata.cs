#if !GLOADER_SERVER
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

internal static class RadioMetadata
{
    // Rainwave's anonymous info endpoint describes the schedule, which can advance
    // before a listener's buffered MP3 reaches the transition. This delay is used
    // only when stream-embedded metadata is unavailable.
    internal const double RainwaveScheduleFallbackDelaySeconds = 10.0;

    internal static bool TryReadTrack(Station station, out TrackInfo track)
    {
        track = null;
        if (station == null) return false;

        // Prefer metadata travelling with the audio stream. In particular, this keeps
        // Rainwave's title change tied to the stream rather than its ahead-of-playback schedule.
        if (TryReadIcyFromStation(station, out track))
        {
            track.PlaybackDelaySeconds = 0;
            return true;
        }

        if (station.MetadataMode == MetadataMode.Rainwave && !string.IsNullOrWhiteSpace(station.MetadataUrl))
        {
            try
            {
                if (TryParseRainwaveNowPlayingJson(RadioNet.DownloadText(station.MetadataUrl, 6000), out track) &&
                    track != null && IsTrackLike(track.Display, station))
                    return true;
            }
            catch { }
        }

        track = null;
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
                var name = artist == null
                    ? Convert.ToString(artistItem, CultureInfo.InvariantCulture)
                    : JsonValue.String(artist, "name");
                if (!string.IsNullOrWhiteSpace(name) && !artistNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                    artistNames.Add(name.Trim());
            }

            var artistText = string.Join(", ", artistNames);
            track = new TrackInfo
            {
                Artist = artistText,
                Title = title,
                Raw = artistText.Length == 0 ? title : artistText + " - " + title,
                ReceivedUtc = DateTime.UtcNow,
                PlaybackDelaySeconds = RainwaveScheduleFallbackDelaySeconds
            };
            return true;
        }
        catch
        {
            track = null;
            return false;
        }
    }

    internal static string ExtractIcyStreamTitle(string metadata)
    {
        if (string.IsNullOrEmpty(metadata)) return null;
        var match = Regex.Match(metadata, @"(?:^|;)\s*StreamTitle\s*=\s*'(?<title>.*?)'\s*;", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
        if (!match.Success)
            match = Regex.Match(metadata, @"StreamTitle\s*=\s*""(?<title>.*?)""", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
        return match.Success ? WebUtility.HtmlDecode(match.Groups["title"].Value).Trim() : null;
    }

    internal static bool IsTrackLike(string title, Station station)
    {
        var value = (title ?? string.Empty).Trim();
        if (value.Length < 2 || value.Length > 240) return false;
        if (station != null && string.Equals(value, station.Name, StringComparison.OrdinalIgnoreCase)) return false;
        var lower = value.ToLowerInvariant();
        if (lower.Contains("you are listening") || lower.Contains("you're listening") ||
            lower.Contains("station id") || lower.Contains("advertisement")) return false;
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

    private static bool TryReadIcyFromStation(Station station, out TrackInfo track)
    {
        track = null;
        foreach (var variant in StreamRanking.Rank(station.Streams))
        {
            try
            {
                var streamUrl = RadioNet.ResolveStreamVariant(station, variant);
                var title = ReadIcyStreamTitle(streamUrl, 6000, 4);
                if (!IsTrackLike(title, station)) continue;
                track = TrackInfo.FromDisplay(title);
                track.PlaybackDelaySeconds = 0;
                return true;
            }
            catch { }
        }
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
#endif
