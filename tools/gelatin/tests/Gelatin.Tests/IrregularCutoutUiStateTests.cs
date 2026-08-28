using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Gelatin.App;
using Gelatin.App.Controls;
using Gelatin.Core.Imaging;

namespace Gelatin.Tests;

public sealed class IrregularCutoutUiStateTests
{
    [AvaloniaFact]
    public void OpenPolygonBackspaceAndEscapeRemainTransient()
    {
        var window = new MainWindow();
        window.Show();
        var originalTitle = window.Title;
        var editor = Assert.Single(window.GetLogicalDescendants().OfType<EditorCanvas>());
        editor.BeginPolygonCutout();
        editor.AddPolygonVertex(new PixelPoint(25, 25));
        editor.AddPolygonVertex(new PixelPoint(250, 25));
        editor.AddPolygonVertex(new PixelPoint(250, 180));

        Assert.True(editor.HandleEditorKey(Key.Back, KeyModifiers.None));
        Assert.Equal(2, editor.PolygonPoints.Count);
        Assert.True(editor.HandleEditorKey(Key.Escape, KeyModifiers.None));
        Assert.Empty(editor.PolygonPoints);
        Assert.Equal(originalTitle, window.Title);
        window.Close();
    }

    [AvaloniaFact]
    public void UnclosedPolygonCannotApplyAndViewStateDoesNotMutateSourceVertices()
    {
        var window = new MainWindow();
        window.Show();
        var editor = Assert.Single(window.GetLogicalDescendants().OfType<EditorCanvas>());
        editor.BeginPolygonCutout();
        editor.AddPolygonVertex(new PixelPoint(30, 30));
        editor.AddPolygonVertex(new PixelPoint(300, 40));
        editor.AddPolygonVertex(new PixelPoint(260, 210));
        var expected = editor.PolygonPoints.ToArray();
        Assert.False(editor.PolygonCanApply);

        typeof(EditorCanvas).GetField("_zoom", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(editor, 18d);
        typeof(EditorCanvas).GetField("_pan", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(editor, new Point(240, -115));
        editor.InvalidateVisual();

        Assert.Equal(18d, editor.Zoom);
        Assert.Equal(new Point(240, -115), editor.Pan);
        Assert.Equal(expected, editor.PolygonPoints);
        Assert.False(editor.PolygonCanApply);
        window.Close();
    }
}
