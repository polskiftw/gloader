using System.Reflection;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Gelatin.App;
using Gelatin.App.Controls;
using Gelatin.Core.Imaging;
using SkiaSharp;

namespace Gelatin.Tests;

public sealed class IrregularCutoutCommitGuardTests
{
    [AvaloniaFact]
    public async Task CompletelyTransparentCutoutResultIsNotCommitted()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gelatin-empty-cutout-{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(path, TestAssets.Png(8, 8, (_, _) => SKColors.Transparent), TestContext.Current.CancellationToken);
        var window = new MainWindow();
        try
        {
            window.Show();
            var controller = (DocumentController)typeof(MainWindow).GetField("_controller", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            await controller.OpenAsync(path);
            var before = Convert.ToHexString(controller.Document.PngBytes);
            var editor = Assert.Single(window.GetLogicalDescendants().OfType<EditorCanvas>());
            editor.BeginPolygonCutout();
            editor.AddPolygonVertex(new PixelPoint(1, 1));
            editor.AddPolygonVertex(new PixelPoint(7, 1));
            editor.AddPolygonVertex(new PixelPoint(1, 7));
            Assert.True(editor.ClosePolygon());

            var apply = typeof(MainWindow).GetMethod("ApplyPolygonCutoutAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            await (Task)apply.Invoke(window, null)!;

            Assert.Equal(before, Convert.ToHexString(controller.Document.PngBytes));
            Assert.False(controller.CanUndo);
            Assert.True(editor.PolygonClosed);
        }
        finally
        {
            window.Close();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [AvaloniaFact]
    public void SelfCrossingPolygonCannotCloseAndReportsUsefulFeedback()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            var editor = Assert.Single(window.GetLogicalDescendants().OfType<EditorCanvas>());
            string? error = null;
            editor.EditorError += message => error = message;
            editor.BeginPolygonCutout();
            editor.AddPolygonVertex(new PixelPoint(20, 20));
            editor.AddPolygonVertex(new PixelPoint(240, 200));
            editor.AddPolygonVertex(new PixelPoint(20, 200));
            editor.AddPolygonVertex(new PixelPoint(240, 20));

            Assert.False(editor.ClosePolygon());
            Assert.False(editor.PolygonClosed);
            Assert.False(editor.PolygonCanApply);
            Assert.Contains("crosses itself", error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            window.Close();
        }
    }
}
