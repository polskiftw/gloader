namespace Gelatin.App;

public sealed class ResizeAspectLink
{
    private double _aspect = 1;

    public ResizeAspectLink(double width, double height) => Capture(width, height);

    public double Aspect => _aspect;

    public void Capture(double width, double height)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0) return;
        _aspect = width / height;
    }

    public int HeightForWidth(double width)
        => Math.Max(1, (int)Math.Round(width / _aspect, MidpointRounding.AwayFromZero));

    public int WidthForHeight(double height)
        => Math.Max(1, (int)Math.Round(height * _aspect, MidpointRounding.AwayFromZero));
}
