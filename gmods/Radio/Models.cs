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
    None,
    Icy,
    Rainwave,
    LautFm,
    WebPage
}

internal sealed class TrackInfo
{
    public string Artist = string.Empty;
    public string Title = string.Empty;
    public string Raw = string.Empty;
    public DateTime ReceivedUtc = DateTime.UtcNow;

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
    public bool Lossless;
    public bool PublicFree = true;
    public bool RequiresAuthentication;
    public string Resolver = string.Empty;
    public string ResolverArgument = string.Empty;
    public string Label = string.Empty;

    public StreamVariant Clone()
    {
        return (StreamVariant)MemberwiseClone();
    }
}

internal sealed class Station
{
    public string Id = string.Empty;
    public string Name = string.Empty;
    public string Provider = string.Empty;
    public string ProviderDisplay = string.Empty;
    public string HomePage = string.Empty;
    public readonly List<string> Tags = new List<string>();
    public readonly List<int> Decades = new List<int>();
    public readonly List<StreamVariant> Streams = new List<StreamVariant>();
    public MetadataMode MetadataMode = MetadataMode.Icy;
    public string MetadataUrl = string.Empty;
    public bool BuiltIn = true;
    public bool LiveDirectory;
    public string DirectorySource = string.Empty;
    public bool MetadataVerified;
    public string SourcePage = string.Empty;

    public string CategorySummary
    {
        get
        {
            var values = Tags.Take(3).ToArray();
            return values.Length == 0 ? ProviderDisplay : string.Join(" / ", values);
        }
    }

    public Station AddTags(params string[] tags)
    {
        foreach (var tag in tags ?? new string[0])
        {
            var clean = RadioTaxonomy.NormalizeTag(tag);
            if (clean.Length > 0 && !Tags.Contains(clean, StringComparer.OrdinalIgnoreCase))
                Tags.Add(clean);
        }
        return this;
    }

    public Station AddDecades(params int[] decades)
    {
        foreach (var decade in decades ?? new int[0])
        {
            if (decade >= 1920 && decade <= 2030 && decade % 10 == 0 && !Decades.Contains(decade))
                Decades.Add(decade);
        }
        Decades.Sort();
        return this;
    }
}

internal static class StreamRanking
{
    internal static bool IsCompatibleCodec(string codec)
    {
        var value = (codec ?? string.Empty).Trim().ToLowerInvariant();
        if (value.Length == 0)
            return true; // Unknown direct MP3/AAC streams are allowed as a last resort.

        return value.Contains("mp3") ||
               value.Contains("mpeg") ||
               value.Contains("aac") ||
               value.Contains("wma") ||
               value.Contains("wave") ||
               value == "m4a";
    }

    internal static int Score(StreamVariant stream)
    {
        if (stream == null || !stream.PublicFree || stream.RequiresAuthentication)
            return int.MinValue;
        if (!IsCompatibleCodec(stream.Codec))
            return int.MinValue + 1;

        var codec = (stream.Codec ?? string.Empty).ToLowerInvariant();
        var codecScore = 1000;
        if (codec.Contains("aac")) codecScore = 5000;
        else if (codec.Contains("mp3") || codec.Contains("mpeg")) codecScore = 4500;
        else if (codec.Contains("wma")) codecScore = 3000;

        // Lossless is only rewarded when it is a format the Windows Media Foundation
        // path is actually expected to decode. Ogg/Opus/Vorbis/FLAC test mounts are
        // intentionally not treated as compatible merely because they sound better.
        if (stream.Lossless && IsCompatibleCodec(stream.Codec))
            codecScore += 3000;

        return codecScore + Math.Max(0, Math.Min(2000, stream.BitrateKbps));
    }

    internal static List<StreamVariant> Rank(IEnumerable<StreamVariant> streams)
    {
        return (streams ?? Enumerable.Empty<StreamVariant>())
            .Where(stream => Score(stream) > int.MinValue + 1)
            .OrderByDescending(Score)
            .ThenBy(stream => stream.Url ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
#endif
