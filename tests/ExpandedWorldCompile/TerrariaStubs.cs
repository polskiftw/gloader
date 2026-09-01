using System;

namespace Microsoft.Xna.Framework
{
    public struct Color
    {
        public Color(int r, int g, int b) { }
        public static Color operator *(Color color, float scale) => color;
    }

    public struct Point
    {
        public int X;
        public int Y;
        public Point(int x, int y) { X = x; Y = y; }
    }

    public struct Rectangle
    {
        public int X;
        public int Y;
        public int Width;
        public int Height;

        public Rectangle(int x, int y, int width, int height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }
    }
}

namespace Terraria
{
    public static class Main
    {
        public static int maxTilesX;
        public static int maxTilesY;
        public static float rightWorld;
        public static float bottomWorld;
        public static int maxSectionsX;
        public static int maxSectionsY;
        public static double rockLayer;
        public static Tile[,] tile = new Tile[2, 2];
        public static object ActiveWorldFileData;
    }

    public sealed class Tile
    {
        public ushort type;
    }

    public static class Utils
    {
        public static T Clamp<T>(T value, T min, T max) where T : IComparable<T>
        {
            if (value.CompareTo(min) < 0) return min;
            if (value.CompareTo(max) > 0) return max;
            return value;
        }
    }

    public sealed class RandomStub
    {
        private readonly Random _random = new Random(1);
        public int Next(int maxValue) => _random.Next(maxValue);
        public int Next(int minValue, int maxValue) => _random.Next(minValue, maxValue);
    }

    public static class WorldGen
    {
        public static RandomStub genRand = new RandomStub();
        public static int GetWorldSize() => 2;
        public static void SetWorldSize(int size) { }
        public static void setWorldSize() { }
        public static void CreateNewWorld() { }
        public static void clearWorld() { }
        public static void GenerateWorld() { }
        public static void makeTemple(int x, int y) { }

        public static void TileRunner(
            int i,
            int j,
            double strength,
            int steps,
            int type,
            bool addTile,
            float speedX,
            float speedY,
            bool noYChange,
            bool overRide,
            int ignoreTileType)
        { }
    }
}

namespace Terraria.Audio
{
    public static class SoundEngine
    {
        public static void PlaySound(object sound) { }
    }
}

namespace Terraria.ID
{
    public static class SoundID
    {
        public static readonly object MenuTick = new object();
    }
}

namespace Terraria.UI
{
    public sealed class UIMouseEvent { }

    public delegate void UIElementEvent(UIMouseEvent evt, UIElement listeningElement);

    public class StyleDimension
    {
        public float Pixels;
        public float Percent;
        public void Set(float pixels, float percent)
        {
            Pixels = pixels;
            Percent = percent;
        }
    }

    public class UIElement
    {
        public readonly StyleDimension Width = new StyleDimension();
        public readonly StyleDimension Height = new StyleDimension();
        public readonly StyleDimension Top = new StyleDimension();
        public float HAlign;

        public event UIElementEvent OnLeftClick;
        public event UIElementEvent OnMouseOver;
        public event UIElementEvent OnMouseOut;

        public void Append(UIElement child) { }
        public void SetSnapPoint(string name, int id) { }

        protected void RaiseForStubOnly()
        {
            OnLeftClick?.Invoke(new UIMouseEvent(), this);
            OnMouseOver?.Invoke(new UIMouseEvent(), this);
            OnMouseOut?.Invoke(new UIMouseEvent(), this);
        }
    }
}

namespace Terraria.GameContent.UI.Elements
{
    using Microsoft.Xna.Framework;
    using Terraria.UI;

    public class UITextPanel<T> : UIElement
    {
        public Color BackgroundColor;
        public Color BorderColor;

        public UITextPanel(T text, float textScale = 1f, bool large = false) { }
        public void SetPadding(float pixels) { }
    }

    public class UIText : UIElement
    {
        public void SetText(string text) { }
    }
}

namespace Terraria.GameContent.UI.States
{
    public class UIWorldCreation : Terraria.UI.UIElement
    {
        public void AddWorldSizeOptions() { }
        public void ClickSizeOption() { }
        public void SetDefaultOptions() { }
        public void UpdateSliders() { }
        public void UpdatePreviewPlate() { }
    }
}

namespace Terraria.GameContent.Biomes
{
    public class JunglePass
    {
        private float _worldScale;
        private void ApplyPass() { }
        private void ApplyRandomMovement(ref int x, ref int y, int xRange, int yRange) { }
        private void PlaceGemsAt(int x, int y, ushort baseGem, int gemVariants) { }
        private void GenerateFinishingTouches(Terraria.WorldBuilding.GenerationProgress progress, int oldX, int oldY) { }
    }
}

namespace Terraria.GameContent.Biomes.Desert
{
    public class DesertDescription
    {
        public static DesertDescription CreateFromPlacement(Microsoft.Xna.Framework.Point origin) => new DesertDescription();
    }
}

namespace Terraria.IO
{
    public sealed class Placeholder { }
}

namespace Terraria.WorldBuilding
{
    public class GenerationProgress
    {
        public void Set(float value) { }
    }
}
