#if !GLOADER_SERVER
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

internal static class RadioTaxonomy
{
    internal static readonly string[] FrontCategories =
    {
        "Everything", "Favorites", "Recent", "Video Game Music", "Electronic", "Synthwave",
        "Rock", "Pop", "Hip-Hop", "Jazz", "Classical", "Ambient", "Lounge", "Dance",
        "Country", "Oldies", "Comedy", "Holiday"
    };

    private static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "vgm", "Video Game Music" }, { "game", "Video Game Music" }, { "games", "Video Game Music" },
        { "chiptune", "Chiptune" }, { "chipmusic", "Chiptune" },
        { "edm", "Electronic" }, { "electronica", "Electronic" }, { "electronic music", "Electronic" },
        { "synth", "Synthwave" }, { "retrowave", "Synthwave" }, { "outrun", "Synthwave" },
        { "hip hop", "Hip-Hop" }, { "hiphop", "Hip-Hop" }, { "rap", "Hip-Hop" },
        { "r&b", "R&B" }, { "rnb", "R&B" },
        { "club", "Dance" }, { "clubbing", "Dance" },
        { "chillout", "Lounge" }, { "chill-out", "Lounge" },
        { "soundtrack", "Soundtracks" }, { "ost", "Soundtracks" },
        { "christmas", "Holiday" }, { "xmas", "Holiday" }
    };

    internal static string NormalizeTag(string tag)
    {
        var value = CollapseSpaces((tag ?? string.Empty).Trim());
        if (value.Length == 0) return string.Empty;
        string alias;
        if (Aliases.TryGetValue(value, out alias)) return alias;
        if (value.Length <= 4 && value.All(c => char.IsUpper(c) || !char.IsLetter(c))) return value;
        return string.Join(" ", value.Split(' ').Select(part =>
            part.Length == 0 ? part : char.ToUpperInvariant(part[0]) + part.Substring(1).ToLowerInvariant()));
    }

    internal static string StableHash(string value)
    {
        unchecked
        {
            uint hash = 2166136261u;
            foreach (var ch in value ?? string.Empty)
            {
                hash ^= ch;
                hash *= 16777619u;
            }
            return hash.ToString("x8", CultureInfo.InvariantCulture);
        }
    }

    internal static string Slug(string value)
    {
        var builder = new StringBuilder();
        var dash = false;
        foreach (var c in (value ?? string.Empty).ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
                dash = false;
            }
            else if (!dash && builder.Length > 0)
            {
                builder.Append('-');
                dash = true;
            }
        }
        return builder.ToString().Trim('-');
    }

    internal static int InferDecade(string text)
    {
        var value = (text ?? string.Empty).ToLowerInvariant();
        for (var decade = 1920; decade <= 2020; decade += 10)
        {
            var two = (decade % 100).ToString("00");
            if (value.Contains(decade + "s") || value.Contains(decade + "'s") || value.Contains(two + "s") || value.Contains(two + "'s"))
                return decade;
        }
        return 0;
    }

    internal static bool Matches(Station station, string category, int decade, string query, ISet<string> favorites, IList<string> recents)
    {
        if (station == null) return false;
        if (string.Equals(category, "Favorites", StringComparison.OrdinalIgnoreCase) &&
            (favorites == null || !favorites.Contains(station.Id))) return false;
        if (string.Equals(category, "Recent", StringComparison.OrdinalIgnoreCase) &&
            (recents == null || !recents.Contains(station.Id))) return false;
        if (!string.IsNullOrWhiteSpace(category) &&
            !string.Equals(category, "Everything", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(category, "Favorites", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(category, "Recent", StringComparison.OrdinalIgnoreCase) &&
            !station.Tags.Any(tag => string.Equals(tag, category, StringComparison.OrdinalIgnoreCase))) return false;
        if (decade > 0 && !station.Decades.Contains(decade)) return false;
        if (string.IsNullOrWhiteSpace(query)) return true;

        var needle = query.Trim().ToLowerInvariant();
        var haystack = string.Join(" ", new[]
        {
            station.Name, station.ProviderDisplay, station.Provider,
            string.Join(" ", station.Tags), string.Join(" ", station.Decades)
        }).ToLowerInvariant();
        foreach (var token in needle.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            if (!haystack.Contains(token)) return false;
        return true;
    }

    private static string CollapseSpaces(string value)
    {
        return string.Join(" ", value.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
    }
}
#endif
