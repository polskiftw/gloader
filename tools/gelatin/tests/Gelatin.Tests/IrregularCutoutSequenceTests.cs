using Gelatin.App;
using Gelatin.Core.Imaging;
using SkiaSharp;

namespace Gelatin.Tests;

public sealed class IrregularCutoutSequenceTests
{
    [Fact]
    public async Task CropBackgroundRemovalUndoRedoRestoreKeepsRecoveryAligned()
    {
        var source = TestAssets.Png(12, 10, (x, y) =>
            x < 5 ? new SKColor(210, 30, 30, 255) : new SKColor((byte)(20 + x), (byte)(70 + y), 210, 255));
        var path = await WriteTempPngAsync(source);
        try
        {
            var controller = new DocumentController();
            await controller.OpenAsync(path);
            var originalWidth = controller.Document.Config.Image.Width;
            var originalHeight = controller.Document.Config.Image.Height;
            var crop = new PixelRect(2, 1, 8, 7);
            controller.CommitImage(
                RawRgbaTransforms.Crop(controller.Document.PngBytes, crop),
                config => ImageProcessor.RemapAuthoringForCrop(config, crop, originalWidth, originalHeight),
                recovery => RawRgbaTransforms.Crop(recovery, crop));

            var expected = RawRgbaTransforms.Sample(controller.RecoveryPngBytes, 1, 2);
            Assert.Equal(new SKColor(210, 30, 30, 255), expected);
            controller.CommitImage(RawRgbaTransforms.RemoveBackground(controller.Document.PngBytes, new SKColor(210, 30, 30), 0, 0));
            Assert.Equal((byte)0, RawRgbaTransforms.Sample(controller.Document.PngBytes, 1, 2).Alpha);

            controller.Undo();
            Assert.Equal((byte)255, RawRgbaTransforms.Sample(controller.Document.PngBytes, 1, 2).Alpha);
            Assert.Equal(expected, RawRgbaTransforms.Sample(controller.RecoveryPngBytes, 1, 2));
            controller.Redo();
            Assert.Equal((byte)0, RawRgbaTransforms.Sample(controller.Document.PngBytes, 1, 2).Alpha);
            Assert.Equal(expected, RawRgbaTransforms.Sample(controller.RecoveryPngBytes, 1, 2));

            using var restore = new AlphaBrushSession(controller.Document.PngBytes, controller.RecoveryPngBytes, AlphaBrushMode.Restore, 1);
            restore.ApplyPoint(new PixelPoint(1.5, 2.5));
            controller.CommitImage(restore.Encode());
            Assert.Equal(expected, RawRgbaTransforms.Sample(controller.Document.PngBytes, 1, 2));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void EraseStrokeNeverIncreasesAlphaAndPreservesEveryRgbChannel()
    {
        var source = TestAssets.Png(13, 9, (x, y) => new SKColor((byte)(20 + x), (byte)(40 + y), (byte)(80 + x + y), (byte)(70 + x * 8)));
        var before = RawRgbaCodec.Decode(source);
        using var erase = new AlphaBrushSession(source, source, AlphaBrushMode.Erase, 3);
        erase.ApplySegment(new PixelPoint(1.5, 4.5), new PixelPoint(11.5, 4.5));
        var edited = erase.Encode();
        var after = RawRgbaCodec.Decode(edited);

        for (var offset = 0; offset < before.Pixels.Length; offset += 4)
        {
            Assert.Equal(before.Pixels[offset], after.Pixels[offset]);
            Assert.Equal(before.Pixels[offset + 1], after.Pixels[offset + 1]);
            Assert.Equal(before.Pixels[offset + 2], after.Pixels[offset + 2]);
            Assert.True(after.Pixels[offset + 3] <= before.Pixels[offset + 3]);
        }
    }

    [Fact]
    public async Task MixedGeometryAndAlphaSequenceNeverDesynchronizesDimensionsOrIndexes()
    {
        var source = TestAssets.Png(18, 14, (x, y) => new SKColor((byte)(20 + x * 3), (byte)(40 + y * 4), 160, 255));
        var path = await WriteTempPngAsync(source);
        try
        {
            var controller = new DocumentController();
            await controller.OpenAsync(path);
            var crop = new PixelRect(3, 2, 11, 9);
            controller.CommitImage(RawRgbaTransforms.Crop(controller.Document.PngBytes, crop),
                recoveryTransform: recovery => RawRgbaTransforms.Crop(recovery, crop));
            controller.CommitImage(RawRgbaTransforms.Resize(controller.Document.PngBytes, 22, 18),
                recoveryTransform: recovery => RawRgbaTransforms.Resize(recovery, 22, 18));

            var polygon = new[] { new PixelPoint(1, 1), new PixelPoint(21, 1), new PixelPoint(15, 17), new PixelPoint(2, 16) };
            var masked = ImageAlphaEditing.ApplyPolygonCutout(controller.Document.PngBytes, polygon);
            var bounds = Assert.IsType<PixelRect>(RawRgbaTransforms.FindTrimBounds(masked, 0));
            controller.CommitImage(RawRgbaTransforms.Crop(masked, bounds),
                recoveryTransform: recovery => RawRgbaTransforms.Crop(recovery, bounds));

            var dimensions = ImageProcessor.GetDimensions(controller.Document.PngBytes);
            Assert.Equal(dimensions, ImageProcessor.GetDimensions(controller.RecoveryPngBytes));
            var x = Math.Clamp(dimensions.Width / 2, 0, dimensions.Width - 1);
            var y = Math.Clamp(dimensions.Height / 2, 0, dimensions.Height - 1);
            using (var erase = new AlphaBrushSession(controller.Document.PngBytes, controller.RecoveryPngBytes, AlphaBrushMode.Erase, 1))
            {
                erase.ApplyPoint(new PixelPoint(x + 0.5, y + 0.5));
                controller.CommitImage(erase.Encode());
            }
            using (var restore = new AlphaBrushSession(controller.Document.PngBytes, controller.RecoveryPngBytes, AlphaBrushMode.Restore, 1))
            {
                restore.ApplyPoint(new PixelPoint(x + 0.5, y + 0.5));
                controller.CommitImage(restore.Encode());
            }

            controller.Undo();
            Assert.Equal(ImageProcessor.GetDimensions(controller.Document.PngBytes), ImageProcessor.GetDimensions(controller.RecoveryPngBytes));
            controller.Redo();
            Assert.Equal(ImageProcessor.GetDimensions(controller.Document.PngBytes), ImageProcessor.GetDimensions(controller.RecoveryPngBytes));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static async Task<string> WriteTempPngAsync(byte[] png)
    {
        var path = Path.Combine(Path.GetTempPath(), $"gelatin-sequence-{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(path, png, TestContext.Current.CancellationToken);
        return path;
    }
}
