using Gelatin.Core.Imaging;
using Gelatin.Core.Models;
using SkiaSharp;

namespace Gelatin.Tests;

internal static class TestAssets
{
    public static byte[] Png(int width = 16, int height = 10, Func<int, int, SKColor>? pixel = null)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++) bitmap.SetPixel(x, y, pixel?.Invoke(x, y) ?? new SKColor((byte)(x * 13), (byte)(y * 19), 170, (byte)(30 + x * 10)));
        return ImageProcessor.EncodePng(bitmap);
    }

    public static GelDocument Document(int width = 16, int height = 10, bool opaque = true)
    {
        var png = Png(width, height, (_, _) => opaque ? new SKColor(180, 70, 210, 255) : SKColors.Transparent);
        return new GelDocument
        {
            PngBytes = png,
            Config = new GelConfig
            {
                AssetName = "Round Trip Gel",
                Image = new ImageConfig { Width = width, Height = height, AlphaThreshold = 0.125 },
                Material = new MaterialConfig
                {
                    Softness = 0.67, Damping = 0.23, AreaPreservation = 0.88, ShapeMemory = 0.61,
                    BendResistance = 0.37, MaxStretch = 1.82, SelfCollision = true, SelfCollisionThickness = 0.009
                },
                Cores =
                [
                    new CoreConfig { Id = 1, Name = "Heavy middle", X = 0.47, Y = 0.52, RadiusX = 0.31, RadiusY = 0.22, Mass = 4.2, Coupling = 0.81, Damping = 0.14, SoftnessMultiplier = 1.3, Falloff = 0.72 },
                    new CoreConfig { Id = 2, Name = "Edge", X = 0.8, Y = 0.31, RadiusX = 0.12, RadiusY = 0.18, Mass = 0.9, Coupling = 0.42, Damping = 0.3, SoftnessMultiplier = 0.8, Falloff = 0.2 }
                ],
                RigidityStrokes =
                [
                    new RigidityStroke { Radius = 0.045, Strength = 0.87, Points = [[0.2, 0.3], [0.29, 0.33], [0.4, 0.39]] }
                ]
            }
        };
    }
}
