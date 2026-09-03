// This file adapts TEdit's PixelMap color-composition algorithm and depth-zone
// background selection. TEdit is licensed under the Microsoft Public License;
// see LICENSE.TEdit.MS-PL.txt in this tool's source/package.

using System.ComponentModel;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using TEdit.Common;
using TEdit.Terraria;

namespace WorldFamilyRenderer;

internal static class TEditPaletteRenderer
{
    public static async Task<RenderedWorld> RenderAsync(
        string worldPath,
        WorldPreset preset,
        int maxWorldPixelWidth,
        string pngPath,
        IProgress<string> status,
        CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            World world = null;
            try
            {
                status?.Report($"{preset.Name}: loading generated world through TEdit...");
                var loadProgress = new Progress<ProgressChangedEventArgs>(e =>
                {
                    if (!string.IsNullOrWhiteSpace(e.UserState?.ToString()))
                        status?.Report($"{preset.Name}: {e.UserState}");
                });

                var loaded = World.LoadWorld(worldPath, headersOnly: false, progress: loadProgress);
                world = loaded.World;
                if (loaded.Error != null)
                    throw new InvalidDataException("TEdit could not load the generated world.", loaded.Error);
                if (world == null || world.Tiles == null)
                    throw new InvalidDataException("TEdit returned no tile data for the generated world.");

                cancellationToken.ThrowIfCancellationRequested();

                double pixelsPerTile = maxWorldPixelWidth / (double)WorldPreset.All.Max(item => item.Width);
                int outputWidth = Math.Max(1, (int)Math.Round(world.TilesWide * pixelsPerTile));
                int outputHeight = Math.Max(1, (int)Math.Round(world.TilesHigh * pixelsPerTile));

                status?.Report($"{preset.Name}: rendering {outputWidth:N0} x {outputHeight:N0} with the TEdit palette...");
                Directory.CreateDirectory(Path.GetDirectoryName(pngPath)!);

                using var bitmap = new Bitmap(outputWidth, outputHeight, PixelFormat.Format32bppArgb);
                var rect = new Rectangle(0, 0, outputWidth, outputHeight);
                BitmapData data = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    int stride = data.Stride;
                    var row = new byte[Math.Abs(stride)];

                    for (int py = 0; py < outputHeight; py++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int worldY = Math.Min(
                            world.TilesHigh - 1,
                            (int)Math.Floor((py + 0.5) / pixelsPerTile));
                        Rgba32 background = GetBackgroundColor(world, worldY);

                        Array.Clear(row);
                        for (int px = 0; px < outputWidth; px++)
                        {
                            int worldX = Math.Min(
                                world.TilesWide - 1,
                                (int)Math.Floor((px + 0.5) / pixelsPerTile));

                            Rgba32 color = GetTileColor(world.Tiles[worldX, worldY], background);
                            if (color.A < 255)
                                color = AlphaBlend(background, color);

                            int offset = px * 4;
                            row[offset] = color.B;
                            row[offset + 1] = color.G;
                            row[offset + 2] = color.R;
                            row[offset + 3] = 255;
                        }

                        IntPtr destination = IntPtr.Add(data.Scan0, py * stride);
                        Marshal.Copy(row, 0, destination, outputWidth * 4);

                        if (py % Math.Max(1, outputHeight / 20) == 0)
                        {
                            int percent = py * 100 / outputHeight;
                            status?.Report($"{preset.Name}: rendering TEdit colors... {percent}%");
                        }
                    }
                }
                finally
                {
                    bitmap.UnlockBits(data);
                }

                bitmap.Save(pngPath, ImageFormat.Png);
                status?.Report($"{preset.Name}: PNG complete.");
                return new RenderedWorld(preset, pngPath, outputWidth, outputHeight);
            }
            finally
            {
                if (world != null)
                    world.Tiles = null;

                // Expanded worlds can allocate multiple gigabytes of tile structs.
                // Render one world at a time and return that memory before the next size.
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    private static Rgba32 GetBackgroundColor(World world, int worldY)
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

        return WorldConfiguration.GlobalColors.TryGetValue(key, out TEditColor color)
            ? FromTEdit(color)
            : new Rgba32(128, 128, 128, 255);
    }

    private static Rgba32 GetTileColor(Tile tile, Rgba32 background)
    {
        Rgba32 color = new(0, 0, 0, 0);

        if (tile.Wall > 0)
        {
            if (WorldConfiguration.WallProperties.Count > tile.Wall)
            {
                TEditColor wallColor = WorldConfiguration.WallProperties[tile.Wall].Color;
                color = wallColor.A != 0 ? AlphaBlend(color, FromTEdit(wallColor)) : background;
            }
            else
            {
                color = AlphaBlend(color, new Rgba32(255, 0, 255, 255));
            }

            byte brightness = 211;
            if (tile.InvisibleWall) brightness = 169;
            if (tile.FullBrightWall) brightness = 255;

            if (tile.WallColor > 0 && tile.TileColor == 0 && tile.WallColor < WorldConfiguration.PaintProperties.Count)
                ApplyPaint(ref color, tile.WallColor, brightness, WorldConfiguration.PaintProperties[tile.WallColor].Color);
            else
                ApplyBrightness(ref color, brightness);
        }
        else
        {
            color = background;
        }

        if (tile.IsActive)
        {
            if (WorldConfiguration.TileProperties.Count > tile.Type)
                color = AlphaBlend(color, FromTEdit(WorldConfiguration.TileProperties[tile.Type].Color));
            else
                color = AlphaBlend(color, new Rgba32(255, 0, 255, 255));

            byte brightness = 211;
            if (tile.InvisibleBlock) brightness = 169;
            if (tile.FullBrightBlock) brightness = 255;

            if (tile.TileColor > 0 && tile.TileColor < WorldConfiguration.PaintProperties.Count)
                ApplyPaint(ref color, tile.TileColor, brightness, WorldConfiguration.PaintProperties[tile.TileColor].Color);
            else
                ApplyBrightness(ref color, brightness);
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
            color = AlphaBlend(color, FromTEdit(WorldConfiguration.GlobalColors[liquidKey]));
        }

        if (tile.WireRed)
            color = AlphaBlend(color, FromTEdit(WorldConfiguration.GlobalColors["Wire"]));
        if (tile.WireBlue)
            color = AlphaBlend(color, FromTEdit(WorldConfiguration.GlobalColors["Wire1"]));
        if (tile.WireGreen)
            color = AlphaBlend(color, FromTEdit(WorldConfiguration.GlobalColors["Wire2"]));
        if (tile.WireYellow)
            color = AlphaBlend(color, FromTEdit(WorldConfiguration.GlobalColors["Wire3"]));

        return color;
    }

    private static void ApplyPaint(ref Rgba32 color, byte paintColor, byte brightness, TEditColor paintPropertyColor)
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
                Rgba32 paint = FromTEdit(paintPropertyColor);
                paint.A = brightness;
                color = AlphaBlend(color, paint);
                break;
        }
    }

    private static void ApplyBrightness(ref Rgba32 color, byte brightness)
    {
        float factor = brightness * (1f / 255f);
        color.R = (byte)(color.R * factor);
        color.G = (byte)(color.G * factor);
        color.B = (byte)(color.B * factor);
    }

    private static Rgba32 AlphaBlend(Rgba32 background, Rgba32 foreground)
    {
        float alpha = foreground.A / 255f;
        byte a = (byte)(alpha * foreground.A + (1f - alpha) * background.A);
        byte r = (byte)(alpha * foreground.R + (1f - alpha) * background.R);
        byte g = (byte)(alpha * foreground.G + (1f - alpha) * background.G);
        byte b = (byte)(alpha * foreground.B + (1f - alpha) * background.B);
        return new Rgba32(r, g, b, a);
    }

    private static Rgba32 FromTEdit(TEditColor color) => new(color.R, color.G, color.B, color.A);

    private struct Rgba32
    {
        public byte R;
        public byte G;
        public byte B;
        public byte A;

        public Rgba32(byte r, byte g, byte b, byte a)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }
    }
}
