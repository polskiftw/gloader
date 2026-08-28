using Avalonia.Media;

namespace Gelatin.App.Controls;

// Keeps the polygon renderer explicit about interface brush typing and the Avalonia 12
// StreamGeometry fill-rule API without changing the rest of the canvas rendering surface.
internal static class Brushes
{
    public static IBrush White => Avalonia.Media.Brushes.White;
    public static IBrush Black => Avalonia.Media.Brushes.Black;
    public static IBrush MediumPurple => Avalonia.Media.Brushes.MediumPurple;
}

internal sealed class StreamGeometry : Avalonia.Media.StreamGeometry
{
    public FillRule FillRule { get; set; } = FillRule.EvenOdd;

    public new StreamGeometryContext Open()
    {
        var context = base.Open();
        context.SetFillRule(FillRule);
        return context;
    }
}
