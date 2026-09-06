#if !GLOADER_SERVER
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

internal sealed class RadioState
{
    public string SelectedStationId = "rainwave:5";
    public bool Playing = true;
    public bool SongNotifications = true;
    public float Volume = 1f;
    public readonly HashSet<string> Favorites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public readonly List<string> Recents = new List<string>();
}

internal static class RadioPersistence
{
    private const int RecentLimit = 20;

    internal static RadioState LoadState(string modDirectory)
    {
        var state = new RadioState();
        var path = Path.Combine(modDirectory, "Radio.state.json");
        try
        {
            if (File.Exists(path)) ApplyStateJson(state, File.ReadAllText(path));
            else TryMigrateLegacyVgmRadio(modDirectory, state);
        }
        catch { }
        return state;
    }

    internal static void SaveState(string modDirectory, RadioState state)
    {
        if (string.IsNullOrWhiteSpace(modDirectory) || state == null) return;
        var root = new Dictionary<string, object>
        {
            { "version", 3 },
            { "selectedStationId", state.SelectedStationId ?? string.Empty },
            { "playing", state.Playing },
            { "songNotifications", state.SongNotifications },
            { "volume", state.Volume },
            { "favorites", state.Favorites.OrderBy(v => v, StringComparer.OrdinalIgnoreCase).Cast<object>().ToList() },
            { "recents", state.Recents.Take(RecentLimit).Cast<object>().ToList() }
        };
        AtomicWrite(Path.Combine(modDirectory, "Radio.state.json"), MiniJson.Stringify(root));
    }

    internal static void TouchRecent(RadioState state, string id)
    {
        if (state == null || string.IsNullOrWhiteSpace(id)) return;
        state.Recents.RemoveAll(value => string.Equals(value, id, StringComparison.OrdinalIgnoreCase));
        state.Recents.Insert(0, id);
        while (state.Recents.Count > RecentLimit) state.Recents.RemoveAt(state.Recents.Count - 1);
    }

    internal static void PruneToCatalog(RadioState state, IEnumerable<Station> catalog)
    {
        if (state == null) return;
        var stations = (catalog ?? Enumerable.Empty<Station>()).Where(station => station != null).ToList();
        var valid = new HashSet<string>(stations.Select(station => station.Id), StringComparer.OrdinalIgnoreCase);
        state.Favorites.RemoveWhere(id => !valid.Contains(id));
        state.Recents.RemoveAll(id => !valid.Contains(id));
        while (state.Recents.Count > RecentLimit) state.Recents.RemoveAt(state.Recents.Count - 1);

        if (!valid.Contains(state.SelectedStationId))
        {
            if (valid.Contains("rainwave:5")) state.SelectedStationId = "rainwave:5";
            else state.SelectedStationId = stations.Count == 0 ? string.Empty : stations[0].Id;
        }
    }

    private static void TryMigrateLegacyVgmRadio(string modDirectory, RadioState state)
    {
        try
        {
            var parent = Directory.GetParent(modDirectory)?.FullName;
            if (parent == null) return;
            var path = Path.Combine(parent, "VGMRadio", "VGMRadio.ini");
            if (!File.Exists(path)) return;

            var stationName = "all";
            var show = true;
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                var equals = line.IndexOf('=');
                if (equals <= 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                var key = line.Substring(0, equals).Trim();
                var value = line.Substring(equals + 1).Trim();
                if (key.Equals("Station", StringComparison.OrdinalIgnoreCase)) stationName = value;
                if (key.Equals("ShowNowPlaying", StringComparison.OrdinalIgnoreCase)) bool.TryParse(value, out show);
            }

            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                { "game", 1 }, { "gamemusic", 1 },
                { "ocremix", 2 }, { "ocr", 2 },
                { "covers", 3 }, { "cover", 3 },
                { "chiptunes", 4 }, { "chiptune", 4 }, { "chip", 4 },
                { "all", 5 }, { "chill", 6 }
            };
            int sid;
            if (!map.TryGetValue((stationName ?? string.Empty).Replace("-", string.Empty).Replace(" ", string.Empty), out sid)) sid = 5;
            state.SelectedStationId = "rainwave:" + sid.ToString(CultureInfo.InvariantCulture);
            state.SongNotifications = show;
            SaveState(modDirectory, state);
        }
        catch { }
    }

    private static void ApplyStateJson(RadioState state, string json)
    {
        var root = MiniJson.Parse(json) as Dictionary<string, object>;
        if (root == null) return;
        state.SelectedStationId = JsonValue.String(root, "selectedStationId", state.SelectedStationId);
        state.Playing = JsonValue.Bool(root, "playing", true);
        state.SongNotifications = JsonValue.Bool(root, "songNotifications", true);

        object volume;
        if (root.TryGetValue("volume", out volume))
        {
            try { state.Volume = Math.Max(0f, Math.Min(1f, Convert.ToSingle(volume, CultureInfo.InvariantCulture))); }
            catch { }
        }

        var favorites = JsonValue.ChildArray(root, "favorites");
        if (favorites != null)
            foreach (var value in favorites) state.Favorites.Add(Convert.ToString(value, CultureInfo.InvariantCulture));

        var recents = JsonValue.ChildArray(root, "recents");
        if (recents != null)
            foreach (var value in recents.Take(RecentLimit)) state.Recents.Add(Convert.ToString(value, CultureInfo.InvariantCulture));
    }

    private static void AtomicWrite(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var temp = path + ".tmp";
        File.WriteAllText(temp, text ?? string.Empty);
        if (!File.Exists(path))
        {
            File.Move(temp, path);
            return;
        }

        try
        {
            File.Replace(temp, path, null);
        }
        catch (IOException)
        {
            File.Delete(path);
            File.Move(temp, path);
        }
        catch (PlatformNotSupportedException)
        {
            File.Delete(path);
            File.Move(temp, path);
        }
    }
}
#endif
