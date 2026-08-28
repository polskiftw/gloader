using System.Buffers.Binary;
using System.Text;
using Gelatin.App;
using Gelatin.Core.Format;
using Gelatin.Core.Imaging;
using Gelatin.Core.Models;
using SkiaSharp;

namespace Gelatin.Tests;

public sealed class Gelatin012CompatibilityTests
{
    [Fact]
    public void Gel1SchemaOneDoesNotPersistRecoverySource()
    {
        var document = TestAssets.Document(8, 6);
        document.Config.Authoring.ToolVersion = "0.1.2";
        document = new GelDocument
        {
            Config = document.Config,
            PngBytes = document.PngBytes,
            RecoveryPngBytes = TestAssets.Png(8, 6, (_, _) => SKColors.Lime)
        };

        var bytes = GelFile.WriteBytes(document);
        Assert.True(bytes.AsSpan(0, 4).SequenceEqual("GEL1"u8));
        var jsonLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4, 4)));
        var json = Encoding.UTF8.GetString(bytes, GelFile.HeaderSize, jsonLength);
        Assert.DoesNotContain("recovery", json, StringComparison.OrdinalIgnoreCase);

        var reopened = GelFile.Read(new MemoryStream(bytes));
        Assert.Equal(1, reopened.Config.SchemaVersion);
        Assert.Equal("0.1.2", reopened.Config.Authoring.ToolVersion);
        Assert.Null(reopened.RecoveryPngBytes);
    }

    [Fact]
    public void SchemaOneDocumentAuthoredBy011LoadsWithoutMigration()
    {
        var old = TestAssets.Document(9, 7);
        old.Config.Authoring.ToolVersion = "0.1.1";
        var bytes = GelFile.WriteBytes(old);

        var reopened = GelFile.Read(new MemoryStream(bytes));

        Assert.Equal(1, reopened.Config.SchemaVersion);
        Assert.Equal("0.1.1", reopened.Config.Authoring.ToolVersion);
        Assert.Equal((9, 7), ImageProcessor.GetDimensions(reopened.PngBytes));
        Assert.Equal(old.Config.Cores.Count, reopened.Config.Cores.Count);
        Assert.Equal(old.Config.RigidityStrokes.Count, reopened.Config.RigidityStrokes.Count);
    }

    [Fact]
    public async Task RectangularCropStillRemapsRecoveryAndUndoRedoTogether()
    {
        var png = TestAssets.Png(12, 10, (x, y) => new SKColor((byte)(20 + x), (byte)(40 + y), 90, 255));
        var sourcePath = Path.Combine(Path.GetTempPath(), $"gelatin-rectcrop-{Guid.NewGuid():N}.png");
        try
        {
            await File.WriteAllBytesAsync(sourcePath, png, TestContext.Current.CancellationToken);
            var controller = new DocumentController();
            await controller.OpenAsync(sourcePath);
            var original = controller.Document.DeepClone();
            var crop = new PixelRect(2, 2, 7, 5);
            var cropped = RawRgbaTransforms.Crop(controller.Document.PngBytes, crop);
            controller.CommitImage(cropped,
                config => ImageProcessor.RemapAuthoringForCrop(config, crop, original.Config.Image.Width, original.Config.Image.Height),
                recovery => RawRgbaTransforms.Crop(recovery, crop));

            Assert.Equal((7, 5), ImageProcessor.GetDimensions(controller.Document.PngBytes));
            Assert.Equal((7, 5), ImageProcessor.GetDimensions(controller.RecoveryPngBytes));
            controller.Undo();
            Assert.Equal((12, 10), ImageProcessor.GetDimensions(controller.Document.PngBytes));
            Assert.Equal((12, 10), ImageProcessor.GetDimensions(controller.RecoveryPngBytes));
            controller.Redo();
            Assert.Equal((7, 5), ImageProcessor.GetDimensions(controller.Document.PngBytes));
            Assert.Equal((7, 5), ImageProcessor.GetDimensions(controller.RecoveryPngBytes));
        }
        finally
        {
            if (File.Exists(sourcePath)) File.Delete(sourcePath);
        }
    }
}
