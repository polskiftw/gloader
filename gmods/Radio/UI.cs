#if !GLOADER_SERVER
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using HarmonyLib;

internal static class RadioUi
{
    internal static bool IsOpen;

    private static string _category = "Everything";
    private static string _subcategory = string.Empty;
    private static int _decade;
    private static string _query = string.Empty;
    private static bool _searchFocused;
    private static int _page;
    private static volatile bool _directorySearching;
    private static string _directoryStatus = "";
    private static string _notificationText = "";
    private static bool _notificationPending;
    private static double _notificationStart;
    private static double _notificationEnd;
    private static Type _ingameOptionsType;
    private static MethodInfo _fontDraw;
    private static object _font;
    private static Type _vector2Type;
    private static Type _colorType;
    private static object _white;
    private static object _zero;
    private static object _one;

    internal static void TryInstall(Harmony harmony)
    {
        try
        {
            _ingameOptionsType = AccessTools.TypeByName("Terraria.IngameOptions");
            var draw = _ingameOptionsType?.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(method => method.Name == "Draw" && method.GetParameters().Length == 2);
            if (draw != null)
            {
                harmony.Patch(draw,
                    prefix: new HarmonyMethod(AccessTools.Method(typeof(RadioUi), nameof(IngameOptionsPrefix))),
                    postfix: new HarmonyMethod(AccessTools.Method(typeof(RadioUi), nameof(IngameOptionsPostfix))));
            }

            var close = _ingameOptionsType == null ? null : AccessTools.Method(_ingameOptionsType, "Close", Type.EmptyTypes);
            if (close != null)
            {
                harmony.Patch(close,
                    postfix: new HarmonyMethod(AccessTools.Method(typeof(RadioUi), nameof(IngameOptionsClosePostfix))));
            }

            var overlayTarget = AccessTools.Method(GeneralRadio.MainType, "DrawInterface_33_MouseText", Type.EmptyTypes);
            if (overlayTarget != null)
                harmony.Patch(overlayTarget, postfix: new HarmonyMethod(AccessTools.Method(typeof(RadioUi), nameof(DrawGameplayOverlay))));
        }
        catch { }
    }

    internal static void NotifySongChange(string display)
    {
        lock (GeneralRadio.StateLock)
        {
            if (GeneralRadio.State == null || !GeneralRadio.State.SongNotifications) return;
            _notificationText = display ?? string.Empty;
            _notificationPending = true;
            _notificationStart = 0;
            _notificationEnd = 0;
        }
    }

    private static bool IngameOptionsPrefix(object[] __args)
    {
        if (!IsOpen) return true;
        try
        {
            var spriteBatch = __args != null && __args.Length > 1 ? __args[1] : null;
            DrawBrowser(spriteBatch);
        }
        catch { IsOpen = false; }
        return false;
    }

    private static void IngameOptionsClosePostfix()
    {
        IsOpen = false;
        _searchFocused = false;
    }

    private static void IngameOptionsPostfix(object[] __args)
    {
        if (IsOpen) return;
        try
        {
            var spriteBatch = __args != null && __args.Length > 1 ? __args[1] : null;
            if (spriteBatch == null) return;
            var width = MainInt("screenWidth", 1280);
            var height = MainInt("screenHeight", 720);
            var x = width / 2 + 222;
            var y = height / 2 - 235;
            DrawButton(spriteBatch, x, y, 108, 28, "Radio", false, () =>
            {
                IsOpen = true;
                _page = 0;
                _searchFocused = false;
            });
        }
        catch { }
    }

    private static void DrawBrowser(object spriteBatch)
    {
        if (spriteBatch == null) return;
        var screenWidth = MainInt("screenWidth", 1280);
        var screenHeight = MainInt("screenHeight", 720);
        DrawRect(spriteBatch, 0, 0, screenWidth, screenHeight, 18, 22, 31, 235);

        var left = Math.Max(24, screenWidth / 2 - 585);
        var top = Math.Max(20, screenHeight / 2 - 330);
        var width = Math.Min(1170, screenWidth - left * 2);
        var height = Math.Min(660, screenHeight - top * 2);
        DrawRect(spriteBatch, left, top, width, height, 31, 38, 54, 245);
        DrawText(spriteBatch, "RADIO", left + 20, top + 14, 255, 255, 255, 255, 1.15f);
        DrawText(spriteBatch, "internet radio  •  no world items  •  provider-neutral", left + 112, top + 18, 185, 195, 215, 255, 0.85f);
        DrawButton(spriteBatch, left + width - 90, top + 12, 70, 28, "Back", false, () => { IsOpen = false; _searchFocused = false; });

        var categoryX = left + 16;
        var categoryY = top + 58;
        var categoryWidth = 145;
        var categoryGap = 6;
        var categoryRows = (RadioTaxonomy.FrontCategories.Length + 1) / 2;
        var categoryPanelWidth = categoryWidth * 2 + categoryGap;
        DrawText(spriteBatch, "BROWSE", categoryX + 8, categoryY - 24, 170, 185, 210, 255, 0.8f);
        for (var i = 0; i < RadioTaxonomy.FrontCategories.Length; i++)
        {
            var value = RadioTaxonomy.FrontCategories[i];
            var column = i / categoryRows;
            var row = i % categoryRows;
            var selected = string.Equals(_category, value, StringComparison.OrdinalIgnoreCase);
            DrawButton(spriteBatch, categoryX + column * (categoryWidth + categoryGap), categoryY + row * 23, categoryWidth, 21, value, selected, () =>
            {
                _category = value;
                _subcategory = string.Empty;
                _page = 0;
            });
        }

        var contentX = categoryX + categoryPanelWidth + 18;
        var contentWidth = left + width - contentX - 16;
        DrawSearch(spriteBatch, contentX, top + 54, contentWidth);
        DrawDecades(spriteBatch, contentX, top + 90, contentWidth);
        DrawSubcategories(spriteBatch, contentX, top + 120, contentWidth);
        DrawStationRows(spriteBatch, contentX, top + 154, contentWidth, height - 248);
        DrawNowPlayingStrip(spriteBatch, contentX, top + height - 80, contentWidth, 62);
    }

    private static void DrawSearch(object spriteBatch, int x, int y, int width)
    {
        if (_searchFocused)
        {
            try
            {
                var getInputText = GeneralRadio.MainType?.GetMethod("GetInputText", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(string) }, null);
                if (getInputText != null) _query = Convert.ToString(getInputText.Invoke(null, new object[] { _query })) ?? _query;
            }
            catch { }
        }

        DrawButton(spriteBatch, x, y, Math.Max(180, width - 260), 28, "Search: " + (string.IsNullOrEmpty(_query) ? "(click and type)" : _query), _searchFocused, () => _searchFocused = !_searchFocused);
        DrawButton(spriteBatch, x + width - 250, y, 112, 28, "Search live", false, BeginDirectorySearch);
        DrawButton(spriteBatch, x + width - 132, y, 60, 28, "Clear", false, () => { _query = ""; _page = 0; _searchFocused = false; });
        DrawButton(spriteBatch, x + width - 66, y, 66, 28, GeneralRadio.State.SongNotifications ? "Popup ✓" : "Popup ✕", GeneralRadio.State.SongNotifications, GeneralRadio.ToggleNotifications);
        if (!string.IsNullOrWhiteSpace(_directoryStatus)) DrawText(spriteBatch, _directoryStatus, x, y + 30, 155, 170, 190, 255, 0.7f);
    }

    private static void DrawDecades(object spriteBatch, int x, int y, int width)
    {
        var labels = new[] { 0, 1940, 1950, 1960, 1970, 1980, 1990, 2000, 2010, 2020 };
        var buttonWidth = Math.Max(42, Math.Min(68, (width - (labels.Length - 1) * 4) / labels.Length));
        for (var i = 0; i < labels.Length; i++)
        {
            var decade = labels[i];
            var label = decade == 0 ? "Any" : (decade >= 2000 ? decade.ToString(CultureInfo.InvariantCulture).Substring(2) + "s" : (decade % 100).ToString("00") + "s");
            DrawButton(spriteBatch, x + i * (buttonWidth + 4), y, buttonWidth, 24, label, _decade == decade, () =>
            {
                _decade = decade;
                _subcategory = string.Empty;
                _page = 0;
            });
        }
    }

    private static void DrawSubcategories(object spriteBatch, int x, int y, int width)
    {
        var options = RadioTaxonomy.SubcategoriesFor(_category, _decade);
        if (options.Length == 0)
        {
            _subcategory = string.Empty;
            DrawText(spriteBatch, "FILTER  All", x, y + 6, 150, 170, 195, 255, 0.68f);
            return;
        }

        if (!string.IsNullOrWhiteSpace(_subcategory) && !options.Contains(_subcategory, StringComparer.OrdinalIgnoreCase))
            _subcategory = string.Empty;

        var count = options.Length + 1;
        var gap = 4;
        var buttonWidth = Math.Max(58, Math.Min(108, (width - (count - 1) * gap) / count));
        DrawButton(spriteBatch, x, y, buttonWidth, 24, "All", string.IsNullOrWhiteSpace(_subcategory), () => { _subcategory = string.Empty; _page = 0; });
        for (var i = 0; i < options.Length; i++)
        {
            var value = options[i];
            DrawButton(spriteBatch, x + (i + 1) * (buttonWidth + gap), y, buttonWidth, 24, value, string.Equals(_subcategory, value, StringComparison.OrdinalIgnoreCase), () =>
            {
                _subcategory = value;
                _page = 0;
            });
        }
    }

    private static void DrawStationRows(object spriteBatch, int x, int y, int width, int availableHeight)
    {
        var all = GeneralRadio.Stations();
        List<string> recents;
        HashSet<string> favorites;
        lock (GeneralRadio.StateLock)
        {
            recents = GeneralRadio.State.Recents.ToList();
            favorites = new HashSet<string>(GeneralRadio.State.Favorites, StringComparer.OrdinalIgnoreCase);
        }
        var filtered = all.Where(station => RadioTaxonomy.Matches(station, _category, _subcategory, _decade, _query, favorites, recents)).ToList();
        if (string.Equals(_category, "Recent", StringComparison.OrdinalIgnoreCase))
        {
            filtered = filtered.OrderBy(station => recents.IndexOf(station.Id)).ToList();
        }
        else
        {
            filtered = filtered
                .OrderByDescending(station => RadioTaxonomy.BrowseTier(station, favorites))
                .ThenByDescending(station => RadioTaxonomy.SearchScore(station, _query))
                .ThenBy(station => station.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        var rowsPerPage = Math.Max(5, Math.Min(10, availableHeight / 38));
        var maxPage = Math.Max(0, (filtered.Count - 1) / rowsPerPage);
        if (_page > maxPage) _page = maxPage;
        if (_page < 0) _page = 0;
        var rows = filtered.Skip(_page * rowsPerPage).Take(rowsPerPage).ToList();

        var filterText = filtered.Count + " stations";
        if (!string.IsNullOrWhiteSpace(_subcategory)) filterText += "  •  " + _subcategory;
        DrawText(spriteBatch, filterText, x, y - 20, 170, 185, 205, 255, 0.75f);
        DrawText(spriteBatch, "Built-in catalogs and live-directory results are labeled separately.", x + 190, y - 20, 145, 160, 180, 255, 0.70f);
        for (var i = 0; i < rows.Count; i++)
        {
            var station = rows[i];
            var rowY = y + i * 38;
            var selected = GeneralRadio.SelectedStation != null && string.Equals(GeneralRadio.SelectedStation.Id, station.Id, StringComparison.OrdinalIgnoreCase);
            DrawRect(spriteBatch, x, rowY, width, 34, selected ? 54 : 39, selected ? 78 : 47, selected ? 105 : 63, 230);
            DrawText(spriteBatch, station.Name, x + 10, rowY + 4, 235, 240, 248, 255, 0.84f);
            var source = station.LiveDirectory ? station.DirectorySource : station.ProviderDisplay;
            var meta = station.CategorySummary + "  •  " + source;
            DrawText(spriteBatch, meta, x + 10, rowY + 19, station.LiveDirectory ? 210 : 150, station.LiveDirectory ? 185 : 175, station.LiveDirectory ? 120 : 205, 255, 0.66f);
            DrawButton(spriteBatch, x + width - 110, rowY + 4, 52, 26, "Play", selected, () => GeneralRadio.SelectStation(station));
            DrawButton(spriteBatch, x + width - 54, rowY + 4, 46, 26, GeneralRadio.IsFavorite(station) ? "★" : "☆", GeneralRadio.IsFavorite(station), () => GeneralRadio.ToggleFavorite(station));
            if (Clicked(x, rowY, width - 118, 34)) { ConsumeClick(); GeneralRadio.SelectStation(station); }
        }

        var navY = y + rowsPerPage * 38 + 4;
        DrawButton(spriteBatch, x, navY, 74, 24, "< Prev", false, () => { if (_page > 0) _page--; });
        DrawText(spriteBatch, "Page " + (_page + 1) + " / " + (maxPage + 1), x + 84, navY + 5, 170, 185, 205, 255, 0.72f);
        DrawButton(spriteBatch, x + 180, navY, 74, 24, "Next >", false, () => { if (_page < maxPage) _page++; });
    }

    private static void DrawNowPlayingStrip(object spriteBatch, int x, int y, int width, int height)
    {
        DrawRect(spriteBatch, x, y, width, height, 20, 26, 38, 245);
        var station = GeneralRadio.SelectedStation;
        var track = GeneralRadio.CurrentTrack;
        var stationName = station == null ? "No station" : station.Name;
        var trackText = track == null ? (GeneralRadio.Health == RadioHealth.MetadataUnavailable ? "Track metadata unavailable" : "Waiting for track metadata…") : track.Display;
        DrawText(spriteBatch, stationName, x + 12, y + 7, 245, 245, 250, 255, 0.84f);
        DrawText(spriteBatch, trackText, x + 12, y + 27, 185, 205, 230, 255, 0.76f);
        DrawText(spriteBatch, GeneralRadio.Health + "  •  " + GeneralRadio.StatusDetail + (string.IsNullOrWhiteSpace(GeneralRadio.ActiveStreamLabel) ? "" : "  •  " + GeneralRadio.ActiveStreamLabel), x + 12, y + 44, 145, 165, 190, 255, 0.64f);

        var controlsX = x + width - 250;
        DrawButton(spriteBatch, controlsX, y + 8, 72, 28, GeneralRadio.State.Playing ? "Pause" : "Play", GeneralRadio.State.Playing, GeneralRadio.TogglePlaying);
        DrawButton(spriteBatch, controlsX + 78, y + 8, 34, 28, "-", false, () => GeneralRadio.SetVolume(GeneralRadio.State.Volume - 0.1f));
        DrawText(spriteBatch, Math.Round(GeneralRadio.State.Volume * 100) + "%", controlsX + 116, y + 16, 220, 225, 235, 255, 0.72f);
        DrawButton(spriteBatch, controlsX + 166, y + 8, 34, 28, "+", false, () => GeneralRadio.SetVolume(GeneralRadio.State.Volume + 0.1f));
    }

    private static void BeginDirectorySearch()
    {
        if (_directorySearching || string.IsNullOrWhiteSpace(_query)) return;
        _directorySearching = true;
        _directoryStatus = "Searching laut.fm + Radio Browser…";
        var query = _query;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                var results = RadioDirectories.SearchAll(query);
                RadioCatalog.AddDirectoryResults(results);
                _directoryStatus = results.Count == 0 ? "No live-directory matches." : "Added " + results.Count + " live-directory matches.";
            }
            catch (Exception ex) { _directoryStatus = "Live search failed: " + ex.Message; }
            finally { _directorySearching = false; }
        });
    }

    private static void DrawGameplayOverlay()
    {
        if (IsOpen || GeneralRadio.State == null || !GeneralRadio.State.SongNotifications) return;
        try
        {
            var spriteBatch = MainObject("spriteBatch");
            if (spriteBatch == null) return;
            var now = GeneralRadio.Clock.Elapsed.TotalSeconds;
            string text;
            lock (GeneralRadio.StateLock)
            {
                if (_notificationPending && !string.IsNullOrWhiteSpace(_notificationText))
                {
                    _notificationPending = false;
                    _notificationStart = now;
                    _notificationEnd = now + GeneralRadio.NotificationSeconds;
                }
                text = _notificationText;
            }
            if (string.IsNullOrWhiteSpace(text) || now < _notificationStart || now >= _notificationEnd) return;
            var alpha = 1.0;
            if (now - _notificationStart < 0.25) alpha = (now - _notificationStart) / 0.25;
            else if (_notificationEnd - now < 0.5) alpha = (_notificationEnd - now) / 0.5;
            alpha = Math.Max(0, Math.Min(1, alpha));
            var height = MainInt("screenHeight", 720);
            DrawRect(spriteBatch, 14, height - 94, 620, 46, 15, 20, 30, (int)(210 * alpha));
            DrawText(spriteBatch, GeneralRadio.SelectedStation == null ? "Radio" : GeneralRadio.SelectedStation.Name, 24, height - 88, 180, 195, 220, (int)(255 * alpha), 0.68f);
            DrawText(spriteBatch, text, 24, height - 70, 245, 245, 250, (int)(255 * alpha), 0.84f);
        }
        catch { }
    }

    private static void DrawButton(object spriteBatch, int x, int y, int width, int height, string label, bool selected, Action action)
    {
        var hovered = MouseIn(x, y, width, height);
        var baseValue = selected ? 72 : hovered ? 61 : 45;
        DrawRect(spriteBatch, x, y, width, height, baseValue, selected ? 95 : baseValue + 8, selected ? 124 : baseValue + 18, 235);
        DrawText(spriteBatch, label, x + 7, y + Math.Max(3, (height - 16) / 2), 235, 240, 248, 255, height <= 22 ? 0.64f : 0.72f);
        if (Clicked(x, y, width, height)) { ConsumeClick(); action?.Invoke(); }
    }

    private static bool MouseIn(int x, int y, int width, int height)
    {
        var mx = MainInt("mouseX", -1);
        var my = MainInt("mouseY", -1);
        return mx >= x && mx < x + width && my >= y && my < y + height;
    }

    private static bool Clicked(int x, int y, int width, int height)
    {
        return MouseIn(x, y, width, height) && MainBool("mouseLeft") && MainBool("mouseLeftRelease");
    }

    private static void ConsumeClick()
    {
        try { AccessTools.Field(GeneralRadio.MainType, "mouseLeftRelease")?.SetValue(null, false); } catch { }
    }

    private static void DrawText(object spriteBatch, string text, float x, float y, int r, int g, int b, int a, float scale)
    {
        if (spriteBatch == null || string.IsNullOrEmpty(text) || !EnsureTextResources()) return;
        try
        {
            var position = Activator.CreateInstance(_vector2Type, new object[] { x, y });
            var scaleVector = Activator.CreateInstance(_vector2Type, new object[] { scale, scale });
            var color = CreateColor(r, g, b, a);
            _fontDraw.Invoke(null, new[] { spriteBatch, _font, text, position, color, (object)0f, _zero, scaleVector, (object)(-1f), (object)2f });
        }
        catch { }
    }

    private static bool EnsureTextResources()
    {
        if (_fontDraw != null && _font != null) return true;
        try
        {
            var fontAssetsType = AccessTools.TypeByName("Terraria.GameContent.FontAssets");
            var chatManagerType = AccessTools.TypeByName("Terraria.UI.Chat.ChatManager");
            _vector2Type = AccessTools.TypeByName("Microsoft.Xna.Framework.Vector2");
            _colorType = AccessTools.TypeByName("Microsoft.Xna.Framework.Color");
            if (fontAssetsType == null || chatManagerType == null || _vector2Type == null || _colorType == null) return false;
            var member = (MemberInfo)fontAssetsType.GetField("MouseText", BindingFlags.Static | BindingFlags.Public) ?? fontAssetsType.GetProperty("MouseText", BindingFlags.Static | BindingFlags.Public);
            var asset = member is FieldInfo ? ((FieldInfo)member).GetValue(null) : ((PropertyInfo)member).GetValue(null, null);
            _font = asset?.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public)?.GetValue(asset, null);
            _zero = StaticMember(_vector2Type, "Zero");
            _one = StaticMember(_vector2Type, "One");
            _white = StaticMember(_colorType, "White");
            _fontDraw = chatManagerType.GetMethods(BindingFlags.Static | BindingFlags.Public).FirstOrDefault(method =>
            {
                var p = method.GetParameters();
                return method.Name == "DrawColorCodedStringWithShadow" && p.Length == 10 && p[2].ParameterType == typeof(string) && p[5].ParameterType == typeof(float);
            });
            return _fontDraw != null && _font != null;
        }
        catch { return false; }
    }

    private static void DrawRect(object spriteBatch, int x, int y, int width, int height, int r, int g, int b, int a)
    {
        if (spriteBatch == null || width <= 0 || height <= 0) return;
        try
        {
            var textureAssetsType = AccessTools.TypeByName("Terraria.GameContent.TextureAssets");
            var rectangleType = AccessTools.TypeByName("Microsoft.Xna.Framework.Rectangle");
            if (textureAssetsType == null || rectangleType == null || !EnsureTextResources()) return;
            var member = (MemberInfo)textureAssetsType.GetField("MagicPixel", BindingFlags.Static | BindingFlags.Public) ?? textureAssetsType.GetProperty("MagicPixel", BindingFlags.Static | BindingFlags.Public);
            var asset = member is FieldInfo ? ((FieldInfo)member).GetValue(null) : ((PropertyInfo)member).GetValue(null, null);
            var texture = asset?.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public)?.GetValue(asset, null);
            if (texture == null) return;
            var rectangle = Activator.CreateInstance(rectangleType, new object[] { x, y, width, height });
            var color = CreateColor(r, g, b, a);
            var draw = spriteBatch.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault(method =>
            {
                if (method.Name != "Draw") return false;
                var p = method.GetParameters();
                return p.Length == 3 && p[1].ParameterType == rectangleType && p[2].ParameterType == _colorType;
            });
            draw?.Invoke(spriteBatch, new[] { texture, rectangle, color });
        }
        catch { }
    }

    private static object CreateColor(int r, int g, int b, int a)
    {
        if (_colorType == null) return _white;
        r = Math.Max(0, Math.Min(255, r)); g = Math.Max(0, Math.Min(255, g)); b = Math.Max(0, Math.Min(255, b)); a = Math.Max(0, Math.Min(255, a));
        var ints = _colorType.GetConstructor(new[] { typeof(int), typeof(int), typeof(int), typeof(int) });
        if (ints != null) return ints.Invoke(new object[] { r, g, b, a });
        var bytes = _colorType.GetConstructor(new[] { typeof(byte), typeof(byte), typeof(byte), typeof(byte) });
        return bytes != null ? bytes.Invoke(new object[] { (byte)r, (byte)g, (byte)b, (byte)a }) : _white;
    }

    private static object StaticMember(Type type, string name)
    {
        var field = type?.GetField(name, BindingFlags.Static | BindingFlags.Public);
        if (field != null) return field.GetValue(null);
        return type?.GetProperty(name, BindingFlags.Static | BindingFlags.Public)?.GetValue(null, null);
    }

    private static object MainObject(string field)
    {
        try { return AccessTools.Field(GeneralRadio.MainType, field)?.GetValue(null); } catch { return null; }
    }

    private static int MainInt(string field, int fallback)
    {
        try { var value = MainObject(field); return value == null ? fallback : Convert.ToInt32(value, CultureInfo.InvariantCulture); } catch { return fallback; }
    }

    private static bool MainBool(string field)
    {
        try { var value = MainObject(field); return value != null && Convert.ToBoolean(value, CultureInfo.InvariantCulture); } catch { return false; }
    }
}
#endif