using Gelatin.App;
using Gelatin.Core.Imaging;
using Gelatin.Core.Models;
using SkiaSharp;

namespace Gelatin.Tests;

public sealed class IrregularCutoutTests
{
    [Fact]
    public void PolygonValidationRejectsTooFewAndDegenerateVertices()
    {
        Assert.False(PolygonGeometry.Validate([new(0, 0), new(1, 1)]).IsValid);
        Assert.False(PolygonGeometry.Validate([new(0, 0), new(0, 0), new(1, 1), new(2, 2)]).IsValid);
    }

    [Fact]
    public void PolygonValidationAcceptsSimpleAndConcavePolygons()
    {
        Assert.True(PolygonGeometry.Validate([new(0, 0), new(4, 0), new(0, 4)]).IsValid);
        Assert.True(PolygonGeometry.Validate([new(0, 0), new(4, 0), new(2, 2), new(4, 4), new(0, 4)]).IsValid);
    }

    [Fact]
    public void PolygonValidationRejectsBowTieButAllowsAdjacentEndpointSharing()
    {
        Assert.False(PolygonGeometry.Validate([new(0, 0), new(4, 4), new(0, 4), new(4, 0)]).IsValid);
        Assert.True(PolygonGeometry.Validate([new(0, 0), new(4, 0), new(4, 4), new(0, 4)]).IsValid);
    }

    [Fact]
    public void PolygonClampInsertAndNudgeUseSourcePixels()
    {
        Assert.Equal(new PixelPoint(0, 20), PolygonGeometry.Clamp(new PixelPoint(-4, 25), 20, 20));
        Assert.Equal(new PixelPoint(11, 9), PolygonGeometry.Nudge(new PixelPoint(10, 10), 1, -1, 20, 20));
        Assert.Equal(new PixelPoint(20, 0), PolygonGeometry.Nudge(new PixelPoint(15, 3), 10, -10, 20, 20));

        var polygon = new List<PixelPoint> { new(0, 0), new(10, 0), new(10, 10), new(0, 10) };
        var inserted = PolygonGeometry.InsertOnEdge(polygon, 0, new PixelPoint(4, 3));
        Assert.Equal(5, inserted.Count);
        Assert.Equal(new PixelPoint(4, 0), inserted[1]);
        Assert.Equal(polygon[1], inserted[2]);
    }

    [Fact]
    public void PolygonMaskPreservesInsideRgbaAndRgbUnderRemovedPixels()
    {
        var outside = new SKColor(11, 22, 33, 190);
        var inside = new SKColor(70, 80, 90, 123);
        var png = TestAssets.Png(8, 8, (x, y) => x == 3 && y == 2 ? inside : outside);
        var cut = ImageAlphaEditing.ApplyPolygonCutout(png, [new(1, 1), new(7, 1), new(1, 7)]);
        Assert.Equal((8, 8), ImageProcessor.GetDimensions(cut));
        using var bitmap = ImageProcessor.Decode(cut);

        Assert.Equal(inside, bitmap.GetPixel(3, 2));
        var removed = bitmap.GetPixel(7, 7);
        Assert.Equal((byte)0, removed.Alpha);
        Assert.Equal(outside.Red, removed.Red);
        Assert.Equal(outside.Green, removed.Green);
        Assert.Equal(outside.Blue, removed.Blue);
    }

    [Fact]
    public void PolygonMaskNeverIncreasesExistingAlphaAndAntialiasesAngledEdge()
    {
        var png = TestAssets.Png(6, 6, (_, _) => new SKColor(120, 60, 210, 80));
        var cut = ImageAlphaEditing.ApplyPolygonCutout(png, [new(0, 0), new(6, 0), new(0, 6)]);
        using var bitmap = ImageProcessor.Decode(cut);
        Assert.All(Enumerable.Range(0, 6).SelectMany(y => Enumerable.Range(0, 6).Select(x => bitmap.GetPixel(x, y).Alpha)),
            alpha => Assert.InRange(alpha, (byte)0, (byte)80));
        Assert.Contains(Enumerable.Range(0, 6).Select(i => bitmap.GetPixel(i, 5 - i).Alpha), alpha => alpha is > 0 and < 80);
    }

    [Fact]
    public void PolygonCutoutZeroAlphaTrimKeepsAntialiasedEdgePixels()
    {
        var png = TestAssets.Png(10, 10, (_, _) => SKColors.White);
        var cut = ImageAlphaEditing.ApplyPolygonCutout(png, [new(2, 2), new(8, 2), new(2, 8)]);
        var bounds = ImageProcessor.FindTrimBounds(cut, 0);
        Assert.NotNull(bounds);
        var cropped = ImageProcessor.Crop(cut, bounds!.Value);
        Assert.Equal((bounds.Value.Width, bounds.Value.Height), ImageProcessor.GetDimensions(cropped));
        using var bitmap = ImageProcessor.Decode(cut);
        Assert.True(bitmap.GetPixel(bounds.Value.X, bounds.Value.Y).Alpha > 0);
    }

    [Fact]
    public void TransparentSourceProducesNoCutoutTrimBounds()
    {
        var png = TestAssets.Png(8, 8, (_, _) => SKColors.Transparent);
        var cut = ImageAlphaEditing.ApplyPolygonCutout(png, [new(1, 1), new(7, 1), new(1, 7)]);
        Assert.Null(ImageProcessor.FindTrimBounds(cut, 0));
    }

    [Fact]
    public void OnePixelEraseTargetsOnePixelAndPreservesRgb()
    {
        var png = TestAssets.Png(5, 5, (x, y) => new SKColor((byte)(20 + x), (byte)(30 + y), 40, 170));
        using var brush = new AlphaBrushSession(png, png, AlphaBrushMode.Erase, 1);
        brush.ApplyPoint(new PixelPoint(2.5, 2.5));
        using var bitmap = ImageProcessor.Decode(brush.Encode());
        var erased = bitmap.GetPixel(2, 2);
        Assert.Equal((byte)0, erased.Alpha);
        Assert.Equal((byte)22, erased.Red);
        Assert.Equal((byte)32, erased.Green);
        Assert.Equal((byte)170, bitmap.GetPixel(1, 2).Alpha);
    }

    [Fact]
    public void AlphaEraseInterpolationFillsFastDragWithoutDottedGaps()
    {
        var png = TestAssets.Png(10, 3, (_, _) => SKColors.White);
        using var brush = new AlphaBrushSession(png, png, AlphaBrushMode.Erase, 1);
        brush.ApplySegment(new PixelPoint(0.5, 1.5), new PixelPoint(9.5, 1.5));
        using var bitmap = ImageProcessor.Decode(brush.Encode());
        for (var x = 0; x < 10; x++) Assert.Equal((byte)0, bitmap.GetPixel(x, 1).Alpha);
        for (var x = 0; x < 10; x++) Assert.Equal((byte)255, bitmap.GetPixel(x, 0).Alpha);
    }

    [Fact]
    public void RestoreCopiesExactRecoveryRgbaAndLeavesOutsideUntouched()
    {
        var recovery = TestAssets.Png(5, 5, (x, y) => new SKColor((byte)(50 + x), (byte)(80 + y), 120, (byte)(150 + x)));
        var current = TestAssets.Png(5, 5, (_, _) => new SKColor(1, 2, 3, 0));
        using var brush = new AlphaBrushSession(current, recovery, AlphaBrushMode.Restore, 1);
        brush.ApplyPoint(new PixelPoint(2.5, 3.5));
        using var restored = ImageProcessor.Decode(brush.Encode());
        using var source = ImageProcessor.Decode(recovery);
        Assert.Equal(source.GetPixel(2, 3), restored.GetPixel(2, 3));
        Assert.Equal(new SKColor(1, 2, 3, 0), restored.GetPixel(1, 3));
    }

    [Fact]
    public void AlphaStrokeCommitIsOneUndoRedoStep()
    {
        var controller = new DocumentController();
        var before = controller.Document.PngBytes;
        using (var brush = new AlphaBrushSession(before, controller.RecoveryPngBytes, AlphaBrushMode.Erase, 20))
        {
            brush.ApplySegment(new PixelPoint(80, 80), new PixelPoint(180, 80));
            controller.CommitImage(brush.Encode());
        }
        var after = controller.Document.PngBytes;
        Assert.NotEqual(Convert.ToHexString(before), Convert.ToHexString(after));

        controller.Undo();
        Assert.Equal(Convert.ToHexString(before), Convert.ToHexString(controller.Document.PngBytes));
        controller.Redo();
        Assert.Equal(Convert.ToHexString(after), Convert.ToHexString(controller.Document.PngBytes));
    }

    [Fact]
    public void CutoutTrimCommitRemapsAuthoringAndRecoveryAsOneUndoStep()
    {
        var controller = new DocumentController();
        var before = controller.Document.DeepClone();
        var oldWidth = before.Config.Image.Width;
        var oldHeight = before.Config.Image.Height;
        var polygon = new[] { new PixelPoint(80, 50), new PixelPoint(400, 50), new PixelPoint(80, 250) };
        var masked = ImageAlphaEditing.ApplyPolygonCutout(before.PngBytes, polygon);
        var bounds = Assert.IsType<PixelRect>(ImageProcessor.FindTrimBounds(masked, 0));
        var final = ImageProcessor.Crop(masked, bounds);
        controller.CommitImage(final,
            config => ImageProcessor.RemapAuthoringForCrop(config, bounds, oldWidth, oldHeight),
            recovery => ImageProcessor.Crop(recovery, bounds));

        Assert.Equal(ImageProcessor.GetDimensions(controller.Document.PngBytes), ImageProcessor.GetDimensions(controller.RecoveryPngBytes));
        controller.Undo();
        Assert.Equal(oldWidth, controller.Document.Config.Image.Width);
        Assert.Equal(oldHeight, controller.Document.Config.Image.Height);
        Assert.Equal(Convert.ToHexString(before.PngBytes), Convert.ToHexString(controller.Document.PngBytes));
    }

    [Fact]
    public async Task CropEraseRestoreKeepsRecoveryGeometryAligned()
    {
        var (controller, path) = await OpenPatternAsync(12, 10);
        try
        {
            var crop = new PixelRect(3, 2, 6, 5);
            controller.CommitImage(ImageProcessor.Crop(controller.Document.PngBytes, crop),
                recoveryTransform: recovery => ImageProcessor.Crop(recovery, crop));
            var expected = Pixel(controller.RecoveryPngBytes, 2, 2);
            controller.CommitImage(ErasePixel(controller, 2, 2));
            Assert.Equal((byte)0, Pixel(controller.Document.PngBytes, 2, 2).Alpha);
            controller.CommitImage(RestorePixel(controller, 2, 2));
            Assert.Equal(expected, Pixel(controller.Document.PngBytes, 2, 2));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ResizeEraseRestoreKeepsRecoveryGeometryAligned()
    {
        var (controller, path) = await OpenPatternAsync(7, 5);
        try
        {
            const int width = 14;
            const int height = 10;
            controller.CommitImage(ImageProcessor.Resize(controller.Document.PngBytes, width, height),
                recoveryTransform: recovery => ImageProcessor.Resize(recovery, width, height));
            var expected = Pixel(controller.RecoveryPngBytes, 9, 6);
            controller.CommitImage(ErasePixel(controller, 9, 6));
            controller.CommitImage(RestorePixel(controller, 9, 6));
            Assert.Equal(expected, Pixel(controller.Document.PngBytes, 9, 6));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task PolygonTrimCanRestoreMaskedPixelThatRemainsInsideTrimCanvas()
    {
        var (controller, path) = await OpenPatternAsync(10, 10);
        try
        {
            var polygon = new[] { new PixelPoint(1, 1), new PixelPoint(9, 1), new PixelPoint(1, 9) };
            var masked = ImageAlphaEditing.ApplyPolygonCutout(controller.Document.PngBytes, polygon);
            var bounds = Assert.IsType<PixelRect>(ImageProcessor.FindTrimBounds(masked, 0));
            controller.CommitImage(ImageProcessor.Crop(masked, bounds),
                recoveryTransform: recovery => ImageProcessor.Crop(recovery, bounds));

            var x = Math.Max(0, controller.Document.Config.Image.Width - 2);
            var y = Math.Max(0, controller.Document.Config.Image.Height - 2);
            Assert.Equal((byte)0, Pixel(controller.Document.PngBytes, x, y).Alpha);
            var expected = Pixel(controller.RecoveryPngBytes, x, y);
            controller.CommitImage(RestorePixel(controller, x, y));
            Assert.Equal(expected, Pixel(controller.Document.PngBytes, x, y));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task BackgroundRemovalUndoRedoThenRestoreUsesSynchronizedRecovery()
    {
        var png = TestAssets.Png(6, 4, (x, _) => x == 0 ? new SKColor(200, 10, 10, 255) : new SKColor(10, 10, 200, 255));
        var path = await WriteTempPngAsync(png);
        var controller = new DocumentController();
        try
        {
            await controller.OpenAsync(path);
            var removed = ImageProcessor.RemoveBackground(controller.Document.PngBytes, new SKColor(200, 10, 10), 0, 0);
            controller.CommitImage(removed);
            Assert.Equal((byte)0, Pixel(controller.Document.PngBytes, 0, 1).Alpha);
            controller.Undo();
            controller.Redo();
            controller.CommitImage(RestorePixel(controller, 0, 1));
            Assert.Equal(new SKColor(200, 10, 10, 255), Pixel(controller.Document.PngBytes, 0, 1));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task SaveReopenMakesProcessedPngTheNewRecoveryBaseline()
    {
        var png = TestAssets.Png(6, 4, (x, _) => x == 0 ? new SKColor(200, 10, 10, 255) : new SKColor(10, 10, 200, 255));
        var sourcePath = await WriteTempPngAsync(png);
        var gelPath = Path.Combine(Path.GetTempPath(), $"gelatin-recovery-{Guid.NewGuid():N}.gel");
        try
        {
            var controller = new DocumentController();
            await controller.OpenAsync(sourcePath);
            controller.CommitImage(ImageProcessor.RemoveBackground(controller.Document.PngBytes, new SKColor(200, 10, 10), 0, 0));
            await controller.SaveAsync(gelPath);

            var reopened = new DocumentController();
            await reopened.OpenAsync(gelPath);
            Assert.Equal(Convert.ToHexString(reopened.Document.PngBytes), Convert.ToHexString(reopened.RecoveryPngBytes));
            reopened.CommitImage(RestorePixel(reopened, 0, 1));
            Assert.Equal((byte)0, Pixel(reopened.Document.PngBytes, 0, 1).Alpha);
            Assert.Equal(1, reopened.Document.Config.SchemaVersion);
        }
        finally
        {
            File.Delete(sourcePath);
            if (File.Exists(gelPath)) File.Delete(gelPath);
        }
    }

    private static byte[] ErasePixel(DocumentController controller, int x, int y)
    {
        using var brush = new AlphaBrushSession(controller.Document.PngBytes, controller.RecoveryPngBytes, AlphaBrushMode.Erase, 1);
        brush.ApplyPoint(new PixelPoint(x + 0.5, y + 0.5));
        return brush.Encode();
    }

    private static byte[] RestorePixel(DocumentController controller, int x, int y)
    {
        using var brush = new AlphaBrushSession(controller.Document.PngBytes, controller.RecoveryPngBytes, AlphaBrushMode.Restore, 1);
        brush.ApplyPoint(new PixelPoint(x + 0.5, y + 0.5));
        return brush.Encode();
    }

    private static SKColor Pixel(byte[] png, int x, int y)
    {
        using var bitmap = ImageProcessor.Decode(png);
        return bitmap.GetPixel(x, y);
    }

    private static async Task<(DocumentController Controller, string Path)> OpenPatternAsync(int width, int height)
    {
        var png = TestAssets.Png(width, height, (x, y) => new SKColor((byte)(20 + x * 10), (byte)(30 + y * 10), (byte)(90 + x), 255));
        var path = await WriteTempPngAsync(png);
        var controller = new DocumentController();
        await controller.OpenAsync(path);
        return (controller, path);
    }

    private static async Task<string> WriteTempPngAsync(byte[] png)
    {
        var path = Path.Combine(Path.GetTempPath(), $"gelatin-source-{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(path, png, TestContext.Current.CancellationToken);
        return path;
    }
}
