#if !GLOADER_SERVER
using System;
using System.Collections.Generic;
using System.Linq;

internal enum RadioHealth
{
    Unknown,
    Online,
    Buffering,
    Reconnecting,
    Offline,
    MetadataUnavailable
}

internal enum MetadataMode
{
    Icy,
    Rainwave
}

internal sealed class TrackInfo
{
    public string Artist = string.Empty;
    public string Title = string.Empty;
    public string Raw = string.Empty;
    public DateTime ReceivedUtc = DateTime.UtcNow;

    // Metadata tied directly to an audio stream is published immediately.
    // Schedule-backed metadata can specify a small playback-alignment delay.
    public double PlaybackDelaySeconds;

    public string Display
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Artist) && !string.IsNullOrWhiteSpace(Title))
                return Artist.Trim() + " - " + Title.Trim();
            if (!string.IsNullOrWhiteSpace(Title))
                return Title.Trim();
            return (Raw ?? string.Empty).Trim();
        }
    }

    public static TrackInfo FromDisplay(string value)
    {
        var raw = (value ?? string.Empty).Trim();
        var result = new TrackInfo { Raw = raw, Title = raw, ReceivedUtc = DateTime.UtcNow };
        var split = raw.IndexOf(" - ", StringComparison.Ordinal);
        if (split > 0 && split + 3 < raw.Length)
        {
            result.Artist = raw.Substring(0, split).Trim();
            result.Title = raw.Substring(split + 3).Trim();
        }
        return result;
    }
}

internal sealed class StreamVariant
{
    public string Url = string.Empty;
    public string Codec = string.Empty;
    public int BitrateKbps;
    public string Resolver = string.Empty;
    public string ResolverArgument = string.Empty;
    public string Label = string.Empty;
}

internal sealed class Station
{
    public string Id = string.Empty;
    public string Name = string.Empty;
    public string Provider = string.Empty;
    public string ProviderDisplay = string.Empty;
    public string HomePage = string.Empty;
    public string Description = string.Empty;
    public readonly List<StreamVariant> Streams = new List<StreamVariant>();
    public MetadataMode MetadataMode = MetadataMode.Icy;
    public string MetadataUrl = string.Empty;
}

internal static class StreamRanking
{
    internal static bool IsCompatibleCodec(string codec)
    {
        var value = (codec ?? string.Empty).Trim().ToLowerInvariant();
        if (value.Length == 0) return true;
        return value.Contains("mp3") || value.Contains("mpeg") || value.Contains("aac") || value == "m4a";
    }

    internal static int Score(StreamVariant stream)
    {
        if (stream == null || !IsCompatibleCodec(stream.Codec)) return int.MinValue;
        var codec = (stream.Codec ?? string.Empty).ToLowerInvariant();
        var codecBonus = codec.Contains("aac") ? 400 : 0;
        var bitrateScore = stream.BitrateKbps > 0 ? Math.Min(1000, stream.BitrateKbps) * 20 : 1000;
        return bitrateScore + codecBonus;
    }

    internal static List<StreamVariant> Rank(IEnumerable<StreamVariant> streams)
    {
        return (streams ?? Enumerable.Empty<StreamVariant>())
            .Where(stream => Score(stream) != int.MinValue)
            .OrderByDescending(Score)
            .ThenBy(stream => stream.Url ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
#endif
