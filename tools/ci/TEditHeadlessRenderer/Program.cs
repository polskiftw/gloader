using Microsoft.Xna.Framework;
using TEdit.Common;
using TEdit.Png;
using TEdit.Terraria;

namespace TEditHeadlessRenderer;

internal static class Program
{
    private const int Resolution = 8;

    private static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: TEditHeadlessRenderer <world.wld> <output.png>");
            return 2;
        }

        WorldConfiguration.Initialize();
        var (world, error) = World.LoadWorld(args[0]);
        if (error != null)
            throw new InvalidOperationException($"TEdit failed to load '{args[0]}'.", error);
        if (world == null)
            throw new InvalidOperationException($"TEdit returned a null world for '{args[0]}'.");
        if (world.TilesWide % Resolution != 0 || world.TilesHigh % Resolution != 0)
            throw new InvalidOperationException($"World dimensions {world.TilesWide}x{world.TilesHigh} are not divisible by the fixed {Resolution}-tile minimap resolution.");

        int outputWidth = world.TilesWide / Resolution;
        int outputHeight = world.TilesHigh / Resolution;
        var row = new byte[outputWidth * 4];

        using var stream = File.Create(args[1]);
        using var png = new StreamingPngWriter(stream, outputWidth, outputHeight);

        for (int py = 0; py < outputHeight; py++)
        {
            int worldY = py * Resolution;
            Color background = GetBackgroundColor(world, worldY);

            for (int px = 0; px < outputWidth; px++)
            {
                int worldX = px * Resolution;
                Color tileColor = GetTileColor(world.Tiles[worldX, worldY], background);
                if (tileColor.A < byte.MaxValue)
                    tileColor = AlphaBlend(background, tileColor);

                int offset = px * 4;
                row[offset] = tileColor.R;
                row[offset + 1] = tileColor.G;
                row[offset + 2] = tileColor.B;
                row[offset + 3] = tileColor.A;
            }

            png.WriteScanline(row);
        }

        png.Finish();
        Console.WriteLine($"TEdit headless render: {world.Title} {world.TilesWide}x{world.TilesHigh} -> {outputWidth}x{outputHeight} (1 px = {Resolution} tiles)");
        return 0;
    }

    // This is the CPU minimap path from the pinned TEdit RenderMiniMap/PixelMap logic,
    // kept deliberately independent of TEdit's WPF application project so CI does not
    // restore or build the full desktop UI merely to parse and color a .wld file.
    private static Color GetBackgroundColor(World world, int worldY)
    {
        string key;
        if (worldY < 80)
            key = "Space";
        else if (worldY > world.TilesHigh - 192)
            key = "Hell";
        else if (worldY > world.RockLevel)
            key = "Rock";
        else if (worldY > world.GroundLevel)
            key = "Earth";
        else
            key = "Sky";

        if (WorldConfiguration.GlobalColors.TryGetValue(key, out TEditColor zoneColor))
            return ToXnaColor(zoneColor);

        return new Color(128, 128, 128, 255);
    }

    private static Color GetTileColor(Tile tile, Color background)
    {
        Color c = Color.Transparent;

        if (tile.Wall > 0)
        {
            if (WorldConfiguration.WallProperties.Count > tile.Wall)
            {
                TEditColor wallColor = WorldConfiguration.WallProperties[tile.Wall].Color;
                c = wallColor.A != 0 ? AlphaBlend(c, wallColor) : background;
            }
            else
            {
                c = AlphaBlend(c, Color.Magenta);
            }

            byte brightness = 211;
            if (tile.InvisibleWall) brightness = 169;
            if (tile.FullBrightWall) brightness = 255;

            if (tile.WallColor > 0 && tile.TileColor == 0)
                ApplyPaint(ref c, tile.WallColor, brightness, WorldConfiguration.PaintProperties[tile.WallColor].Color);
            else
                ApplyBrightness(ref c, brightness);
        }
        else
        {
            c = background;
        }

        if (tile.IsActive)
        {
            if (WorldConfiguration.TileProperties.Count > tile.Type)
                c = AlphaBlend(c, WorldConfiguration.TileProperties[tile.Type].Color);
            else
                c = AlphaBlend(c, Color.Magenta);

            byte brightness = 211;
            if (tile.InvisibleBlock) brightness = 169;
            if (tile.FullBrightBlock) brightness = 255;

            if (tile.TileColor > 0 && tile.TileColor <= WorldConfiguration.PaintProperties.Count)
                ApplyPaint(ref c, tile.TileColor, brightness, WorldConfiguration.PaintProperties[tile.TileColor].Color);
            else
                ApplyBrightness(ref c, brightness);
        }

        if (tile.LiquidAmount > 0)
        {
            string liquidKey = tile.LiquidType switch
            {
                LiquidType.Lava => "Lava",
                LiquidType.Honey => "Honey",
                LiquidType.Shimmer => "Shimmer",
                _ => "Water"
            };
            c = AlphaBlend(c, WorldConfiguration.GlobalColors[liquidKey]);
        }

        if (tile.WireRed) c = AlphaBlend(c, WorldConfiguration.GlobalColors["Wire"]);
        if (tile.WireBlue) c = AlphaBlend(c, WorldConfiguration.GlobalColors["Wire1"]);
        if (tile.WireGreen) c = AlphaBlend(c, WorldConfiguration.GlobalColors["Wire2"]);
        if (tile.WireYellow) c = AlphaBlend(c, WorldConfiguration.GlobalColors["Wire3"]);

        return c;
    }

    private static void ApplyPaint(ref Color color, byte paintColor, byte brightness, TEditColor paintPropertyColor)
    {
        float brightnessFactor = brightness * (1f / 255f);
        switch (paintColor)
        {
            case 29:
                float light = color.B * 0.3f * brightnessFactor;
                color.R = (byte)(color.R * light);
                color.G = (byte)(color.G * light);
                color.B = (byte)(color.B * light);
                break;
            case 30:
                float half = 0.5f * brightnessFactor;
                color.R = (byte)((byte.MaxValue - color.R) * half);
                color.G = (byte)((byte.MaxValue - color.G) * half);
                color.B = (byte)((byte.MaxValue - color.B) * half);
                break;
            default:
                TEditColor paint = paintPropertyColor;
                paint.A = brightness;
                color = AlphaBlend(color, paint);
                break;
        }
    }

    private static void ApplyBrightness(ref Color color, byte brightness)
    {
        float factor = brightness * (1f / 255f);
        color.R = (byte)(color.R * factor);
        color.G = (byte)(color.G * factor);
        color.B = (byte)(color.B * factor);
    }

    private static Color ToXnaColor(TEditColor color) => new(color.R, color.G, color.B, color.A);

    private static Color AlphaBlend(Color background, TEditColor foreground) =>
        AlphaBlend(background.A, background.R, background.B, background.G,
            foreground.A, foreground.R, foreground.B, foreground.G);

    private static Color AlphaBlend(Color background, Color foreground) =>
        AlphaBlend(background.A, background.R, background.B, background.G,
            foreground.A, foreground.R, foreground.B, foreground.G);

    private static Color AlphaBlend(byte a1, byte r1, byte b1, byte g1, byte a2, byte r2, byte b2, byte g2)
    {
        byte a = (byte)((a2 / 255f) * a2 + (1f - a2 / 255f) * a1);
        byte r = (byte)((a2 / 255f) * r2 + (1f - a2 / 255f) * r1);
        byte g = (byte)((a2 / 255f) * g2 + (1f - a2 / 255f) * g1);
        byte b = (byte)((a2 / 255f) * b2 + (1f - a2 / 255f) * b1);
        return Color.FromNonPremultiplied(r, g, b, a);
    }
}
