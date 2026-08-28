using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Gelatin.App;
using Gelatin.App.Controls;
using Gelatin.Core.Imaging;

namespace Gelatin.Tests;

public sealed class IrregularCutoutUiTests
{
    [AvaloniaFact]
    public void AssetWorkspaceExposesSeparateCropPolygonAndAlphaRepairTools()
    {
        var window = new MainWindow();
        window.Show();
        var buttons = window.GetLogicalDescendants().OfType<Button>().ToArray();
        Assert.Contains(buttons, button => Equals(button.Content, "Draw crop rectangle"));
        Assert.Contains(buttons, button => Equals(button.Content, "Polygon cutout"));
        Assert.Contains(buttons, button => Equals(button.Content, "Apply Cutout"));
        Assert.Contains(buttons, button => Equals(button.Content, "Erase alpha"));
        Assert.Contains(buttons, button => Equals(button.Content, "Restore alpha"));

        var editor = Assert.Single(window.GetLogicalDescendants().OfType<EditorCanvas>());
        Click(buttons, "Polygon cutout");
        Assert.Equal(EditorMode.PolygonCutout, editor.Mode);
        Assert.Empty(editor.PolygonPoints);
        var apply = Assert.Single(window.GetLogicalDescendants().OfType<Button>(), button => Equals(button.Content, "Apply Cutout"));
        Assert.False(apply.IsEnabled);

        editor.AddPolygonVertex(new PixelPoint(20, 20));
        editor.AddPolygonVertex(new PixelPoint(300, 20));
        editor.AddPolygonVertex(new PixelPoint(300, 200));
        editor.AddPolygonVertex(new PixelPoint(20, 200));
        Assert.True(editor.HandleEditorKey(Key.Enter, KeyModifiers.None));
        Assert.True(editor.PolygonClosed);
        Assert.True(editor.PolygonCanApply);
        Assert.True(apply.IsEnabled);

        editor.SelectPolygonVertex(0);
        var before = editor.PolygonPoints[0];
        Assert.True(editor.HandleEditorKey(Key.Right, KeyModifiers.None));
        Assert.Equal(before.X + 1, editor.PolygonPoints[0].X);
        Assert.True(editor.HandleEditorKey(Key.Down, KeyModifiers.Shift));
        Assert.Equal(before.Y + 10, editor.PolygonPoints[0].Y);

        Assert.True(editor.HandleEditorKey(Key.Delete, KeyModifiers.None));
        Assert.Equal(3, editor.PolygonPoints.Count);
        Assert.True(editor.HandleEditorKey(Key.Delete, KeyModifiers.None));
        Assert.Equal(3, editor.PolygonPoints.Count);

        Assert.True(editor.HandleEditorKey(Key.Escape, KeyModifiers.None));
        Assert.Empty(editor.PolygonPoints);
        Assert.False(editor.PolygonClosed);
        window.Close();
    }

    [AvaloniaFact]
    public void PolygonHotkeysAreSuppressedInsideNumericInputAndWorkspaceSwitchClearsTransientState()
    {
        var window = new MainWindow();
        window.Show();
        var buttons = window.GetLogicalDescendants().OfType<Button>().ToArray();
        Click(buttons, "Polygon cutout");
        var editor = Assert.Single(window.GetLogicalDescendants().OfType<EditorCanvas>());
        editor.AddPolygonVertex(new PixelPoint(20, 20));
        editor.AddPolygonVertex(new PixelPoint(300, 20));
        editor.AddPolygonVertex(new PixelPoint(300, 200));
        editor.AddPolygonVertex(new PixelPoint(20, 200));
        Assert.True(editor.HandleEditorKey(Key.Enter, KeyModifiers.None));
        editor.SelectPolygonVertex(0);
        var before = editor.PolygonPoints[0];

        var numeric = window.GetLogicalDescendants().OfType<NumericUpDown>().First();
        numeric.Focus();
        RaiseKey(numeric, Key.Left, KeyModifiers.None);
        Assert.Equal(before, editor.PolygonPoints[0]);

        buttons = window.GetLogicalDescendants().OfType<Button>().ToArray();
        Click(buttons, "Gel");
        Assert.Empty(editor.PolygonPoints);
        window.Close();
    }

    [AvaloniaFact]
    public void AlphaRepairModesAndBrushRangeAreReachable()
    {
        var window = new MainWindow();
        window.Show();
        var editor = Assert.Single(window.GetLogicalDescendants().OfType<EditorCanvas>());
        var buttons = window.GetLogicalDescendants().OfType<Button>().ToArray();

        Click(buttons, "Erase alpha");
        Assert.Equal(EditorMode.AlphaErase, editor.Mode);
        Click(buttons, "Restore alpha");
        Assert.Equal(EditorMode.AlphaRestore, editor.Mode);

        editor.AlphaBrushSize = 0;
        Assert.Equal(1, editor.AlphaBrushSize);
        editor.AlphaBrushSize = 999;
        Assert.Equal(256, editor.AlphaBrushSize);
        window.Close();
    }

    private static void RaiseKey(Control source, Key key, KeyModifiers modifiers)
    {
        source.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = modifiers
        });
    }

    private static void Click(IEnumerable<Button> buttons, string label)
    {
        var button = Assert.Single(buttons, candidate => Equals(candidate.Content, label));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
}
