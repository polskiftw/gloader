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
        "Everything", "Favorites", "Recent", "Video Game Music", "Rock", "Metal",
        "Electronic", "Synthwave", "Hip-Hop", "R&B", "Jazz", "Blues", "Country", "Folk",
        "Classical", "Pop", "Ambient", "Lounge", "Dance", "Oldies", "World", "Reggae",
        "Comedy", "Holiday"
    };

    private static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "vgm", "Video Game Music" }, { "game", "Video Game Music" }, { "games", "Video Game Music" }, { "game music", "Video Game Music" },
        { "chiptune", "Chiptune" }, { "chiptunes", "Chiptune" }, { "chipmusic", "Chiptune" },
        { "edm", "Electronic" }, { "electronica", "Electronic" }, { "electronic music", "Electronic" },
        { "synth", "Synthwave" }, { "retrowave", "Synthwave" }, { "outrun", "Synthwave" },
        { "newwave", "New Wave" }, { "new-wave", "New Wave" },
        { "hip hop", "Hip-Hop" }, { "hiphop", "Hip-Hop" }, { "rap", "Hip-Hop" },
        { "r&b", "R&B" }, { "rnb", "R&B" }, { "rhythm and blues", "R&B" },
        { "hairband", "Hair Metal" }, { "hair band", "Hair Metal" }, { "glam metal", "Hair Metal" }, { "glam/hair", "Hair Metal" },
        { "club", "Dance" }, { "clubbing", "Dance" },
        { "chillout", "Ambient" }, { "chill-out", "Ambient" }, { "ambient & chill", "Ambient" },
        { "dnb", "Drum & Bass" }, { "drum and bass", "Drum & Bass" }, { "drum'n'bass", "Drum & Bass" },
        { "ebm", "Industrial / EBM" }, { "industrial ebm", "Industrial / EBM" },
        { "soundtrack", "Soundtracks" }, { "ost", "Soundtracks" },
        { "big band", "Big Band / Swing" }, { "swing", "Big Band / Swing" },
        { "blue grass", "Bluegrass" },
        { "ska", "Reggae" }, { "reggae / ska", "Reggae" },
        { "christmas", "Holiday" }, { "xmas", "Holiday" }, { "seasonal", "Holiday" }
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

    internal static string[] SubcategoriesFor(string category, int decade)
    {
        if (decade > 0 && (string.Equals(category, "Everything", StringComparison.OrdinalIgnoreCase) || string.Equals(category, "Oldies", StringComparison.OrdinalIgnoreCase)))
            return new[] { "Pop", "Rock", "New Wave", "Hair Metal", "R&B", "Hip-Hop", "Dance" };

        switch ((category ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "rock": return new[] { "Classic Rock", "Alternative", "Indie", "Punk", "Hard Rock", "Progressive" };
            case "metal": return new[] { "Heavy Metal", "Power Metal", "Thrash Metal", "Death Metal", "Black Metal", "Doom Metal" };
            case "electronic": return new[] { "Synthwave", "House", "Techno", "Trance", "Drum & Bass", "Dubstep" };
            case "synthwave": return new[] { "Chillsynth", "Darksynth", "Horrorsynth", "Spacesynth", "Datawave", "Industrial / EBM" };
            case "hip-hop": return new[] { "Old School", "R&B", "Soul", "1980s", "1990s", "2000s" };
            case "r&b": return new[] { "Soul", "Funk", "Motown", "Old School", "1980s", "1990s" };
            case "jazz": return new[] { "Traditional Jazz", "Smooth Jazz", "Bebop", "Fusion", "Big Band / Swing", "Vocal Jazz" };
            case "blues": return new[] { "Electric Blues", "Soul", "Funk", "Motown", "Disco", "Boogie" };
            case "country": return new[] { "Classic Country", "Modern Country", "Bluegrass", "Americana", "Folk", "Traditional" };
            case "folk": return new[] { "Americana", "Bluegrass", "Traditional", "World", "Celtic", "Acoustic" };
            case "classical": return new[] { "Baroque", "Romantic", "Opera", "Piano", "Chamber", "Contemporary Classical" };
            case "video game music": return new[] { "Chiptune", "Remixes", "Covers", "SEGA", "Touhou", "Demoscene" };
            case "pop": return new[] { "Hits", "Adult Contemporary", "Soft Pop", "Synthpop", "Dance", "Oldies" };
            case "dance": return new[] { "House", "Techno", "Trance", "Disco", "Club", "Electronic" };
            case "ambient": return new[] { "Chill", "Downtempo", "Drone", "Lounge", "Chillsynth", "New Age" };
            case "oldies": return new[] { "Pop", "Rock", "Soul", "R&B", "Country", "Big Band / Swing" };
            case "reggae": return new[] { "Ska", "Dub", "Roots Reggae", "Dancehall", "World", "Oldies" };
            case "holiday": return new[] { "Christmas", "Seasonal", "Oldies", "Jazz", "Classical", "Pop" };
            default: return new string[0];
        }
    }

    internal static bool Matches(Station station, string category, int decade, string query, ISet<string> favorites, IList<string> recents)
    {
        return Matches(station, category, string.Empty, decade, query, favorites, recents);
    }

    internal static bool Matches(Station station, string category, string subcategory, int decade, string query, ISet<string> favorites, IList<string> recents)
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
            !MatchesTagOrName(station, category)) return false;
        if (!string.IsNullOrWhiteSpace(subcategory))
        {
            var subcategoryDecade = InferDecade(subcategory);
            if (subcategoryDecade > 0)
            {
                if (!station.Decades.Contains(subcategoryDecade)) return false;
            }
            else if (!MatchesTagOrName(station, subcategory)) return false;
        }
        if (decade > 0 && !station.Decades.Contains(decade)) return false;
        if (string.IsNullOrWhiteSpace(query)) return true;

        var haystack = SearchHaystack(station);
        foreach (var rawToken in query.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var token = NormalizeSearchToken(rawToken);
            if (token.Length == 0) continue;
            if (!haystack.Contains(token)) return false;
        }
        return true;
    }

    internal static int SearchScore(Station station, string query)
    {
        if (station == null || string.IsNullOrWhiteSpace(query)) return 0;
        var needle = query.Trim().ToLowerInvariant();
        var name = (station.Name ?? string.Empty).ToLowerInvariant();
        var score = string.Equals(name, needle, StringComparison.OrdinalIgnoreCase) ? 2000 : name.Contains(needle) ? 900 : 0;
        foreach (var rawToken in needle.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var token = NormalizeSearchToken(rawToken);
            if (token.Length == 0) continue;
            if (name == token) score += 600;
            else if (name.Contains(token)) score += 300;
            if (station.Tags.Any(tag => string.Equals(tag, token, StringComparison.OrdinalIgnoreCase))) score += 500;
            else if (station.Tags.Any(tag => tag.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)) score += 250;
            var decade = InferDecade(token);
            if (decade > 0 && station.Decades.Contains(decade)) score += 500;
        }
        return score;
    }

    internal static int BrowseTier(Station station, ISet<string> favorites)
    {
        if (station == null) return int.MinValue;
        var score = favorites != null && favorites.Contains(station.Id) ? 10000 : 0;
        if (station.BuiltIn && station.MetadataVerified) score += 3000;
        else if (station.MetadataVerified) score += 2000;
        else if (station.BuiltIn) score += 1000;
        if (station.LiveDirectory) score -= 100;
        return score;
    }

    private static bool MatchesTagOrName(Station station, string value)
    {
        var needle = NormalizeTag(value);
        if (needle.Length == 0) return true;
        if (station.Tags.Any(tag => string.Equals(tag, needle, StringComparison.OrdinalIgnoreCase))) return true;
        if ((station.Name ?? string.Empty).IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        var lower = value.ToLowerInvariant();
        if (lower.EndsWith(" metal") && station.Tags.Any(tag => tag.IndexOf("Metal", StringComparison.OrdinalIgnoreCase) >= 0)) return true;
        if (string.Equals(lower, "metal", StringComparison.OrdinalIgnoreCase) && (station.Name ?? string.Empty).IndexOf("metal", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    private static string SearchHaystack(Station station)
    {
        return string.Join(" ", new[]
        {
            station.Name, station.ProviderDisplay, station.Provider,
            string.Join(" ", station.Tags), string.Join(" ", station.Decades.Select(decade => decade + "s"))
        }).ToLowerInvariant();
    }

    private static string NormalizeSearchToken(string token)
    {
        var value = (token ?? string.Empty).Trim().ToLowerInvariant();
        if (value.Length == 0) return string.Empty;
        if (value.EndsWith("'s") && value.Length >= 4 && char.IsDigit(value[0])) value = value.Substring(0, value.Length - 2) + "s";
        return value;
    }

    private static string CollapseSpaces(string value)
    {
        return string.Join(" ", value.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
    }
}
#endif