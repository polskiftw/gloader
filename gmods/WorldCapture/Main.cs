#if GLOADER_CLIENT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.ID;
using Terraria.IO;

public static class Mod
{
    public static void Load()
    {
        WorldCaptureRuntime.Load();
    }
}

internal static class WorldCaptureRuntime
{
    internal const int SectionWidth = 200;
    internal const int SectionHeight = 150;

    private static readonly Queue<SectionCoord> Pending = new Queue<SectionCoord>();
    private static readonly HashSet<int> Queued = new HashSet<int>();

    private static WorldCaptureStore _store;
    private static bool[,] _captured;
    private static bool[,] _sessionSeen;
    private static bool[,] _dirty;
    private static int _sectionsX;
    private static int _sectionsY;
    private static int _capturedCount;
    private static int _sessionNewCount;
    private static int _scanFrame;
    private static int _dirtyFrame;
    private static string _worldKey;
    private static bool _ready;
    private static bool _disabled;
    private static string _errorText;

    internal static bool Ready => _ready && !_disabled;
    internal static int CapturedCount => _capturedCount;
    internal static int TotalSections => _sectionsX * _sectionsY;
    internal static int SessionNewCount => _sessionNewCount;
    internal static string WorldName => _store == null ? string.Empty : _store.Info.Name;
    internal static string CacheDirectory => _store == null ? string.Empty : _store.WorldDirectory;
    internal static string ErrorText => _errorText ?? string.Empty;

    internal static double CoveragePercent
    {
        get
        {
            int total = TotalSections;
            return total <= 0 ? 0.0 : _capturedCount * 100.0 / total;
        }
    }

    internal static void Load()
    {
        MessageBuffer.OnTileChangeReceived += OnTileChangeReceived;
        Netplay.OnDisconnect += OnDisconnect;

        Console.WriteLine("[World Capture] Enabled. Multiplayer world sections will be cached as Terraria native compressed tile blocks.");
        Console.WriteLine("[World Capture] Press F8 in a multiplayer world to toggle the coverage overlay.");
    }

    internal static void Tick()
    {
        try
        {
            if (Main.netMode != NetmodeID.MultiplayerClient || Main.gameMenu)
            {
                if (_ready)
                    EndSession();
                return;
            }

            if (_disabled)
                return;

            if (Main.maxTilesX <= 0 || Main.maxTilesY <= 0 || Main.sectionManager == null)
                return;

            var key = WorldCaptureStore.BuildCurrentWorldKey();
            if (!_ready || !string.Equals(key, _worldKey, StringComparison.Ordinal))
                BeginSession(key);

            _scanFrame++;
            if (_scanFrame >= 10)
            {
                _scanFrame = 0;
                ScanForLoadedSections();
            }

            // Received tile changes mark sections dirty immediately, but repeated
            // changes are deliberately coalesced. At most once per second we enqueue
            // dirty sections for a fresh full snapshot instead of recompressing a
            // 30,000-tile section for every pickaxe swing.
            _dirtyFrame++;
            if (_dirtyFrame >= 60)
            {
                _dirtyFrame = 0;
                QueueDirtySections();
            }

            CaptureOnePendingSection();
        }
        catch (Exception ex)
        {
            DisableAfterError(ex);
        }
    }

    private static void BeginSession(string worldKey)
    {
        EndSession();

        _worldKey = worldKey;
        _sectionsX = DivideRoundUp(Main.maxTilesX, SectionWidth);
        _sectionsY = DivideRoundUp(Main.maxTilesY, SectionHeight);
        _captured = new bool[_sectionsX, _sectionsY];
        _sessionSeen = new bool[_sectionsX, _sectionsY];
        _dirty = new bool[_sectionsX, _sectionsY];
        _capturedCount = 0;
        _sessionNewCount = 0;
        _scanFrame = 0;
        _dirtyFrame = 0;
        Pending.Clear();
        Queued.Clear();

        _store = new WorldCaptureStore(WorldCaptureWorldInfo.FromCurrent(), _sectionsX, _sectionsY);
        _store.LoadExistingCoverage(_captured, out _capturedCount);
        _store.WriteManifest(_capturedCount, TotalSections, _sessionNewCount);

        _ready = true;

        Console.WriteLine(
            "[World Capture] Tracking '" + _store.Info.Name + "' (" +
            Main.maxTilesX + "x" + Main.maxTilesY + " tiles, " +
            TotalSections + " sections). Existing coverage: " +
            CoveragePercent.ToString("0.00", CultureInfo.InvariantCulture) + "%.");
        Console.WriteLine("[World Capture] Cache: " + _store.WorldDirectory);

        // Do not wait for the normal ten-frame scan when joining. The initial spawn
        // sections may already be marked loaded by the time this mod sees the world.
        ScanForLoadedSections();
    }

    private static void EndSession()
    {
        if (_ready && _store != null)
        {
            try
            {
                _store.WriteManifest(_capturedCount, TotalSections, _sessionNewCount);
            }
            catch
            {
            }
        }

        _ready = false;
        _worldKey = null;
        _store = null;
        _captured = null;
        _sessionSeen = null;
        _dirty = null;
        _sectionsX = 0;
        _sectionsY = 0;
        _capturedCount = 0;
        _sessionNewCount = 0;
        _scanFrame = 0;
        _dirtyFrame = 0;
        Pending.Clear();
        Queued.Clear();
    }

    private static void OnDisconnect()
    {
        try
        {
            EndSession();
        }
        catch
        {
        }
    }

    private static void ScanForLoadedSections()
    {
        if (!_ready || Main.sectionManager == null)
            return;

        for (int sx = 0; sx < _sectionsX; sx++)
        {
            for (int sy = 0; sy < _sectionsY; sy++)
            {
                if (_sessionSeen[sx, sy])
                    continue;

                if (!Main.sectionManager.SectionLoaded(sx, sy))
                    continue;

                _sessionSeen[sx, sy] = true;

                // Refresh every section once per session even if it was already cached
                // on an earlier visit. This makes revisiting an area repair stale data
                // without changing the persistent coverage percentage.
                QueueSection(sx, sy);
            }
        }
    }

    private static void QueueDirtySections()
    {
        if (!_ready || _dirty == null || Main.sectionManager == null)
            return;

        for (int sx = 0; sx < _sectionsX; sx++)
        {
            for (int sy = 0; sy < _sectionsY; sy++)
            {
                if (!_dirty[sx, sy])
                    continue;

                if (!Main.sectionManager.SectionLoaded(sx, sy))
                    continue;

                QueueSection(sx, sy);
            }
        }
    }

    private static void QueueSection(int sx, int sy)
    {
        if (!_ready || sx < 0 || sy < 0 || sx >= _sectionsX || sy >= _sectionsY)
            return;

        if (Main.sectionManager == null || !Main.sectionManager.SectionLoaded(sx, sy))
            return;

        int key = sy * _sectionsX + sx;
        if (!Queued.Add(key))
            return;

        Pending.Enqueue(new SectionCoord(sx, sy));
    }

    private static void CaptureOnePendingSection()
    {
        if (!_ready || Pending.Count == 0)
            return;

        var section = Pending.Dequeue();
        Queued.Remove(section.Y * _sectionsX + section.X);

        if (Main.sectionManager == null || !Main.sectionManager.SectionLoaded(section.X, section.Y))
            return;

        if (_dirty != null)
            _dirty[section.X, section.Y] = false;

        int startX = section.X * SectionWidth;
        int startY = section.Y * SectionHeight;
        int width = Math.Min(SectionWidth, Main.maxTilesX - startX);
        int height = Math.Min(SectionHeight, Main.maxTilesY - startY);
        if (width <= 0 || height <= 0)
            return;

        byte[] payload;
        using (var stream = new MemoryStream(131072))
        {
            // Use Terraria's own network tile serializer. This intentionally keeps the
            // cache close to the game's native representation and makes a future world
            // reconstruction/exporter much easier than inventing a parallel tile schema.
            NetMessage.CompressTileBlock(startX, startY, (short)width, (short)height, stream);
            payload = stream.ToArray();
        }

        _store.WriteSection(section.X, section.Y, payload);

        bool newlyCaptured = !_captured[section.X, section.Y];
        if (newlyCaptured)
        {
            _captured[section.X, section.Y] = true;
            _capturedCount++;
            _sessionNewCount++;

            Console.WriteLine(
                "[World Capture] " + _capturedCount + "/" + TotalSections +
                " sections (" + CoveragePercent.ToString("0.00", CultureInfo.InvariantCulture) + "%).");

            _store.WriteManifest(_capturedCount, TotalSections, _sessionNewCount);
        }
    }

    private static void OnTileChangeReceived(int x, int y, int count, TileChangeType type)
    {
        try
        {
            if (!_ready || _disabled || _dirty == null ||
                Main.netMode != NetmodeID.MultiplayerClient || Main.sectionManager == null)
                return;

            // Vanilla supplies a count/size with received tile changes. Treat it as a
            // conservative radius here: over-refreshing an adjacent loaded section is
            // harmless, while under-refreshing would leave a stale cached snapshot.
            int radius = Math.Max(1, Math.Min(400, count));
            int minX = Math.Max(0, x - radius);
            int minY = Math.Max(0, y - radius);
            int maxX = Math.Min(Main.maxTilesX - 1, x + radius);
            int maxY = Math.Min(Main.maxTilesY - 1, y + radius);

            int minSectionX = minX / SectionWidth;
            int maxSectionX = maxX / SectionWidth;
            int minSectionY = minY / SectionHeight;
            int maxSectionY = maxY / SectionHeight;

            for (int sx = minSectionX; sx <= maxSectionX; sx++)
            {
                for (int sy = minSectionY; sy <= maxSectionY; sy++)
                {
                    if (sx >= 0 && sy >= 0 && sx < _sectionsX && sy < _sectionsY &&
                        Main.sectionManager.SectionLoaded(sx, sy))
                    {
                        _sessionSeen[sx, sy] = true;
                        _dirty[sx, sy] = true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            DisableAfterError(ex);
        }
    }

    private static void DisableAfterError(Exception ex)
    {
        if (_disabled)
            return;

        _disabled = true;
        _errorText = ex.GetType().Name + ": " + ex.Message;
        Console.Error.WriteLine("[World Capture] Disabled after an error: " + ex);
    }

    private static int DivideRoundUp(int value, int divisor)
    {
        return (value + divisor - 1) / divisor;
    }

    private readonly struct SectionCoord
    {
        internal readonly int X;
        internal readonly int Y;

        internal SectionCoord(int x, int y)
        {
            X = x;
            Y = y;
        }
    }
}

[HarmonyPatch(typeof(Main), "DrawInterface_33_MouseText")]
internal static class WorldCaptureDrawPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        WorldCaptureOverlay.TickHotkey();
        WorldCaptureRuntime.Tick();
        WorldCaptureOverlay.Draw();
    }
}

internal static class WorldCaptureOverlay
{
    private static KeyboardState _lastKeyboardState;
    private static bool _initializedKeyboard;
    private static bool _visible;

    internal static void TickHotkey()
    {
        var current = Keyboard.GetState();

        if (!_initializedKeyboard)
        {
            _lastKeyboardState = current;
            _initializedKeyboard = true;
            return;
        }

        if (!Main.gameMenu &&
            current.IsKeyDown(Keys.F8) &&
            _lastKeyboardState.IsKeyUp(Keys.F8))
        {
            _visible = !_visible;
        }

        _lastKeyboardState = current;
    }

    internal static void Draw()
    {
        if (!_visible || Main.gameMenu || Main.netMode != NetmodeID.MultiplayerClient)
            return;

        var spriteBatch = Main.spriteBatch;
        if (spriteBatch == null)
            return;

        const float scale = 0.78f;
        float x = Main.screenWidth - 16f;
        float y = Main.screenHeight - 70f;

        if (!string.IsNullOrEmpty(WorldCaptureRuntime.ErrorText))
        {
            Utils.DrawBorderString(
                spriteBatch,
                "World capture disabled: " + WorldCaptureRuntime.ErrorText,
                new Vector2(x, y + 36f),
                Color.White,
                scale,
                1f,
                1f);
            return;
        }

        if (!WorldCaptureRuntime.Ready)
            return;

        string coverage = "World capture: " +
            WorldCaptureRuntime.CoveragePercent.ToString("0.00", CultureInfo.InvariantCulture) + "%";
        string sections = "Sections: " + WorldCaptureRuntime.CapturedCount + " / " + WorldCaptureRuntime.TotalSections;
        string session = "This session: +" + WorldCaptureRuntime.SessionNewCount;

        Utils.DrawBorderString(spriteBatch, coverage, new Vector2(x, y), Color.White, scale, 1f, 0f);
        Utils.DrawBorderString(spriteBatch, sections, new Vector2(x, y + 18f), Color.White, scale, 1f, 0f);
        Utils.DrawBorderString(spriteBatch, session, new Vector2(x, y + 36f), Color.White, scale, 1f, 0f);
    }
}

internal sealed class WorldCaptureStore
{
    internal const int FormatVersion = 1;

    internal readonly WorldCaptureWorldInfo Info;
    internal readonly string WorldDirectory;

    private readonly string _sectionsDirectory;
    private readonly string _manifestPath;
    private readonly int _sectionsX;
    private readonly int _sectionsY;

    internal WorldCaptureStore(WorldCaptureWorldInfo info, int sectionsX, int sectionsY)
    {
        Info = info;
        _sectionsX = sectionsX;
        _sectionsY = sectionsY;

        string root = Path.Combine(Main.SavePath, "gloader", "WorldCapture");
        WorldDirectory = Path.Combine(root, info.Key);
        _sectionsDirectory = Path.Combine(WorldDirectory, "sections");
        _manifestPath = Path.Combine(WorldDirectory, "manifest.txt");

        Directory.CreateDirectory(_sectionsDirectory);
    }

    internal static string BuildCurrentWorldKey()
    {
        var data = Main.ActiveWorldFileData;
        Guid uniqueId = data == null ? Guid.Empty : data.UniqueId;
        if (uniqueId != Guid.Empty)
            return uniqueId.ToString("N");

        return "id-" + unchecked((uint)Main.worldID).ToString("X8", CultureInfo.InvariantCulture) +
               "-" + Main.maxTilesX.ToString(CultureInfo.InvariantCulture) +
               "x" + Main.maxTilesY.ToString(CultureInfo.InvariantCulture);
    }

    internal void LoadExistingCoverage(bool[,] captured, out int capturedCount)
    {
        capturedCount = 0;

        for (int sx = 0; sx < _sectionsX; sx++)
        {
            for (int sy = 0; sy < _sectionsY; sy++)
            {
                string path = SectionPath(sx, sy);
                bool exists = false;
                try
                {
                    var file = new FileInfo(path);
                    exists = file.Exists && file.Length > 0;
                }
                catch
                {
                    exists = false;
                }

                captured[sx, sy] = exists;
                if (exists)
                    capturedCount++;
            }
        }
    }

    internal void WriteSection(int sectionX, int sectionY, byte[] payload)
    {
        if (payload == null || payload.Length == 0)
            throw new InvalidDataException("Terraria produced an empty tile-block payload.");

        string path = SectionPath(sectionX, sectionY);
        string temp = path + ".tmp";

        File.WriteAllBytes(temp, payload);
        ReplaceFile(temp, path);
    }

    internal void WriteManifest(int capturedSections, int totalSections, int sessionNewSections)
    {
        double percent = totalSections <= 0 ? 0.0 : capturedSections * 100.0 / totalSections;
        string temp = _manifestPath + ".tmp";

        using (var writer = new StreamWriter(temp, false))
        {
            writer.WriteLine("format=GLoaderWorldCapture");
            writer.WriteLine("format_version=" + FormatVersion.ToString(CultureInfo.InvariantCulture));
            writer.WriteLine("cache_encoding=Terraria.NetMessage.CompressTileBlock");
            writer.WriteLine("terraria_version=" + SafeLine(Main.versionNumber2));
            writer.WriteLine("world_name=" + SafeLine(Info.Name));
            writer.WriteLine("world_uuid=" + (Info.UniqueId == Guid.Empty ? "unknown" : Info.UniqueId.ToString("D")));
            writer.WriteLine("world_id=" + Info.WorldId.ToString(CultureInfo.InvariantCulture));
            writer.WriteLine("world_generator_version=" + Info.WorldGeneratorVersion.ToString(CultureInfo.InvariantCulture));
            writer.WriteLine("game_mode=" + Info.GameMode.ToString(CultureInfo.InvariantCulture));
            writer.WriteLine("width_tiles=" + Info.Width.ToString(CultureInfo.InvariantCulture));
            writer.WriteLine("height_tiles=" + Info.Height.ToString(CultureInfo.InvariantCulture));
            writer.WriteLine("section_width=" + WorldCaptureRuntime.SectionWidth.ToString(CultureInfo.InvariantCulture));
            writer.WriteLine("section_height=" + WorldCaptureRuntime.SectionHeight.ToString(CultureInfo.InvariantCulture));
            writer.WriteLine("sections_x=" + _sectionsX.ToString(CultureInfo.InvariantCulture));
            writer.WriteLine("sections_y=" + _sectionsY.ToString(CultureInfo.InvariantCulture));
            writer.WriteLine("captured_sections=" + capturedSections.ToString(CultureInfo.InvariantCulture));
            writer.WriteLine("total_sections=" + totalSections.ToString(CultureInfo.InvariantCulture));
            writer.WriteLine("coverage_percent=" + percent.ToString("0.0000", CultureInfo.InvariantCulture));
            writer.WriteLine("new_sections_this_session=" + sessionNewSections.ToString(CultureInfo.InvariantCulture));
            writer.WriteLine("seed=not_provided_by_remote_server");
            writer.WriteLine("last_seen_utc=" + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        }

        ReplaceFile(temp, _manifestPath);
    }

    private string SectionPath(int sectionX, int sectionY)
    {
        return Path.Combine(
            _sectionsDirectory,
            "x" + sectionX.ToString("D2", CultureInfo.InvariantCulture) +
            "_y" + sectionY.ToString("D2", CultureInfo.InvariantCulture) + ".bin");
    }

    private static void ReplaceFile(string temp, string destination)
    {
        if (!File.Exists(destination))
        {
            File.Move(temp, destination);
            return;
        }

        try
        {
            File.Replace(temp, destination, null);
        }
        catch
        {
            // File.Replace can be unavailable on a few filesystems. Falling back to a
            // same-directory overwrite still leaves the temporary source intact until
            // the new bytes have been copied successfully.
            File.Copy(temp, destination, true);
            File.Delete(temp);
        }
    }

    private static string SafeLine(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value.Replace('\r', ' ').Replace('\n', ' ');
    }
}

internal sealed class WorldCaptureWorldInfo
{
    internal string Key;
    internal string Name;
    internal Guid UniqueId;
    internal int WorldId;
    internal ulong WorldGeneratorVersion;
    internal int GameMode;
    internal int Width;
    internal int Height;

    internal static WorldCaptureWorldInfo FromCurrent()
    {
        WorldFileData data = Main.ActiveWorldFileData;

        return new WorldCaptureWorldInfo
        {
            Key = WorldCaptureStore.BuildCurrentWorldKey(),
            Name = Main.worldName ?? string.Empty,
            UniqueId = data == null ? Guid.Empty : data.UniqueId,
            WorldId = Main.worldID,
            WorldGeneratorVersion = data == null ? 0UL : data.WorldGeneratorVersion,
            GameMode = Main.GameMode,
            Width = Main.maxTilesX,
            Height = Main.maxTilesY
        };
    }
}
#endif
