using System.Numerics;
using Gelatin.Core.Authoring;
using Gelatin.Core.Imaging;
using Gelatin.Core.Models;
using Gelatin.Core.Physics;
using SkiaSharp;

namespace Gelatin.Tests;

public sealed class ImageAndAuthoringTests
{
    [Theory]
    [InlineData(SKEncodedImageFormat.Jpeg)]
    [InlineData(SKEncodedImageFormat.Webp)]
    public void JpegAndWebpNormalizeToPortablePng(SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(13, 7, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        bitmap.Erase(new SKColor(70, 130, 220, 255));
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(format, 90);
        Assert.NotNull(encoded);
        var png = ImageProcessor.NormalizeToPng(encoded.ToArray());
        Assert.Equal((13, 7), ImageProcessor.GetDimensions(png));
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png[..8]);
    }

    [Fact]
    public void CropResizeAndTrimProduceCorrectDimensions()
    {
        var png = TestAssets.Png(12, 10, (x, y) => x is >= 3 and <= 8 && y is >= 2 and <= 6 ? SKColors.Red : SKColors.Transparent);
        var bounds = ImageProcessor.FindTrimBounds(png, 0.01);
        Assert.Equal(new PixelRect(3, 2, 6, 5), bounds);
        var cropped = ImageProcessor.Crop(png, bounds!.Value);
        Assert.Equal((6, 5), ImageProcessor.GetDimensions(cropped));
        var resized = ImageProcessor.Resize(cropped, 30, 17);
        Assert.Equal((30, 17), ImageProcessor.GetDimensions(resized));
    }

    [Fact]
    public void CompletelyTransparentTrimReturnsNoBounds()
        => Assert.Null(ImageProcessor.FindTrimBounds(TestAssets.Png(8, 8, (_, _) => SKColors.Transparent), 0.01));

    [Fact]
    public void BackgroundRemovalPreservesExistingTransparencyAndFeathersContinuously()
    {
        var png = TestAssets.Png(5, 1, (x, _) => new SKColor((byte)(100 + x * 10), 100, 100, x == 0 ? (byte)40 : (byte)255));
        var hard = ImageProcessor.RemoveBackground(png, new SKColor(100, 100, 100), 0.055, 0);
        var soft = ImageProcessor.RemoveBackground(png, new SKColor(100, 100, 100), 0.055, 0.2);
        using var hardBitmap = ImageProcessor.Decode(hard);
        using var softBitmap = ImageProcessor.Decode(soft);
        Assert.Equal(0, hardBitmap.GetPixel(0, 0).Alpha);
        Assert.InRange(softBitmap.GetPixel(0, 0).Alpha, (byte)0, (byte)40);
        var distinct = Enumerable.Range(0, 5).Select(x => softBitmap.GetPixel(x, 0).Alpha).Distinct().Count();
        Assert.True(distinct >= 3);
    }

    [Fact]
    public void CropRemapsAndDiscardsNormalizedAuthoring()
    {
        var config = new GelConfig
        {
            Image = new ImageConfig { Width = 100, Height = 100 },
            Cores =
            [
                new CoreConfig { Id = 1, X = 0.5, Y = 0.5, RadiusX = 0.1, RadiusY = 0.2 },
                new CoreConfig { Id = 2, X = 0.05, Y = 0.05, RadiusX = 0.01, RadiusY = 0.01 }
            ],
            RigidityStrokes = [new RigidityStroke { Radius = 0.05, Strength = 1, Points = [[0.5, 0.5], [0.6, 0.5]] }]
        };
        ImageProcessor.RemapAuthoringForCrop(config, new PixelRect(25, 25, 50, 50), 100, 100);
        Assert.Single(config.Cores);
        Assert.Equal(0.5, config.Cores[0].X, 8);
        Assert.Equal(0.5, config.Cores[0].Y, 8);
        Assert.Equal(0.2, config.Cores[0].RadiusX, 8);
        Assert.Equal(0.5, config.RigidityStrokes[0].Points[0][0], 8);
    }

    [Fact]
    public void CropClipsCrossingStrokesAndKeepsRemappedAuthoringValid()
    {
        var config = new GelConfig
        {
            Image = new ImageConfig { Width = 1000, Height = 1000 },
            Cores = [new CoreConfig { Id = 1, X = 0.2, Y = 0.5, RadiusX = 0.25, RadiusY = 0.25 }],
            RigidityStrokes =
            [
                new RigidityStroke { Radius = 0.04, Strength = 0.8, Points = [[-0.5, 0.5], [1.5, 0.5]] },
                new RigidityStroke { Radius = 0.03, Strength = 0.7, Points = [[1.5, 1.5], [1.8, 1.8]] }
            ]
        };

        ImageProcessor.RemapAuthoringForCrop(config, new PixelRect(450, 450, 100, 100), 1000, 1000);

        Assert.Single(config.Cores);
        Assert.Single(config.RigidityStrokes);
        Assert.All(config.RigidityStrokes[0].Points, point =>
        {
            Assert.InRange(point[0], -1, 2);
            Assert.InRange(point[1], -1, 2);
        });
        GelValidator.Validate(config);
    }

    [Fact]
    public void CoreInfluenceFadesAndOverlapsCombine()
    {
        var core = new CoreConfig { Id = 1, X = 0.5, Y = 0.5, RadiusX = 0.3, RadiusY = 0.2, Falloff = 0.5 };
        var center = InfluenceFields.CoreInfluence(core, new Vector2(0.5f, 0.5f));
        var middle = InfluenceFields.CoreInfluence(core, new Vector2(0.65f, 0.5f));
        var edge = InfluenceFields.CoreInfluence(core, new Vector2(0.8f, 0.5f));
        Assert.True(center > middle && middle > edge);
        Assert.True(InfluenceFields.CombinedCoreInfluence([core, core], new Vector2(0.65f, 0.5f)) > middle);
    }

    [Fact]
    public void RigidityIsGradientAndEraseRemovesPoints()
    {
        var strokes = new List<RigidityStroke> { new() { Radius = 0.2, Strength = 1, Points = [[0.5, 0.5], [0.7, 0.5]] } };
        Assert.True(InfluenceFields.Rigidity(strokes, new Vector2(0.5f, 0.5f)) > InfluenceFields.Rigidity(strokes, new Vector2(0.5f, 0.65f)));
        InfluenceFields.Erase(strokes, new Vector2(0.5f, 0.5f), 0.06, 1);
        Assert.Single(strokes);
        Assert.Single(strokes[0].Points);
    }

    [Fact]
    public void AlphaContourWrapsOpaqueShapeWithoutTransparentCorners()
    {
        var png = TestAssets.Png(20, 20, (x, y) => x is >= 5 and <= 14 && y is >= 6 and <= 13 ? SKColors.White : SKColors.Transparent);
        var contours = AlphaContourExtractor.Extract(png, 0.1, 64);
        Assert.NotEmpty(contours);
        var points = contours.SelectMany(contour => contour.Points).ToArray();
        Assert.All(points, point => Assert.True(point.X is >= 0.2f and <= 0.8f && point.Y is >= 0.25f and <= 0.75f));
        Assert.DoesNotContain(points, point => Vector2.Distance(point, Vector2.Zero) < 0.1f);
    }

    [Fact]
    public void HistoryIsBoundedAndSupportsUndoRedo()
    {
        var history = new DocumentHistory(2, 1024 * 1024);
        var current = TestAssets.Document();
        history.Record(current);
        current.Config.AssetName = "B";
        history.Record(current);
        current.Config.AssetName = "C";
        var b = history.Undo(current);
        Assert.Equal("B", b.Config.AssetName);
        var original = history.Undo(b);
        Assert.Equal("Round Trip Gel", original.Config.AssetName);
        Assert.Equal("B", history.Redo(original).Config.AssetName);
    }

    [Fact]
    public void QualityPresetsMatchThePublishedContract()
    {
        Assert.Equal(new QualitySettings(24, 240, 8, 2, 96), QualitySettings.For(PhysicsQuality.Sane));
        Assert.Equal(new QualitySettings(32, 480, 12, 1, 144), QualitySettings.For(PhysicsQuality.High));
        Assert.Equal(new QualitySettings(48, 720, 16, 1, 224), QualitySettings.For(PhysicsQuality.Overkill));
        Assert.Equal(new QualitySettings(64, 960, 24, 1, 384), QualitySettings.For(PhysicsQuality.Claire));
        var wide = QualitySettings.For(PhysicsQuality.Claire).GridForAspect(16);
        Assert.True(wide.Columns >= 3 && wide.Rows >= 3 && wide.Columns * wide.Rows > 3000);
    }
}
