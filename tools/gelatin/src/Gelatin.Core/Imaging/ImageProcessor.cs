using Gelatin.Core.Models;
using SkiaSharp;

namespace Gelatin.Core.Imaging;

public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => checked(X + Width);
    public int Bottom => checked(Y + Height);
}

public static class ImageProcessor
{
    public static (int Width, int Height) GetDimensions(ReadOnlySpan<byte> encoded)
    {
        if (!RawRgbaCodec.IsPng(encoded))
            throw new GelFormatException("The embedded image is not a valid PNG payload.");
        var decoded = RawRgbaCodec.Decode(encoded);
        return (decoded.Width, decoded.Height);
    }

    public static byte[] NormalizeToPng(ReadOnlySpan<byte> encoded)
        => RawRgbaTransforms.NormalizeToPng(encoded);

    public static SKBitmap Decode(ReadOnlySpan<byte> encoded)
    {
        try
        {
            using var data = SKData.CreateCopy(encoded);
            using var codec = SKCodec.Create(data) ?? throw new GelFormatException("The image is unsupported or corrupt.");
            var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            var bitmap = new SKBitmap(info);
            var result = codec.GetPixels(info, bitmap.GetPixels());
            if (result != SKCodecResult.Success)
            {
                bitmap.Dispose();
                throw new GelFormatException($"The image decoder failed ({result}).");
            }
            return bitmap;
        }
        catch (GelFormatException) { throw; }
        catch (Exception ex)
        {
            throw new GelFormatException("The image is unsupported or corrupt.", ex);
        }
    }

    public static byte[] Crop(ReadOnlySpan<byte> png, PixelRect rect)
        => RawRgbaTransforms.Crop(png, rect);

    public static byte[] Crop(ReadOnlySpan<byte> png, PixelRect rect, CancellationToken cancellationToken)
        => RawRgbaTransforms.Crop(png, rect, cancellationToken);

    public static byte[] Resize(ReadOnlySpan<byte> png, int width, int height)
        => RawRgbaTransforms.Resize(png, width, height);

    public static byte[] Resize(ReadOnlySpan<byte> png, int width, int height, CancellationToken cancellationToken)
        => RawRgbaTransforms.Resize(png, width, height, cancellationToken);

    public static PixelRect? FindTrimBounds(ReadOnlySpan<byte> png, double alphaThreshold)
        => RawRgbaTransforms.FindTrimBounds(png, alphaThreshold);

    public static PixelRect? FindTrimBounds(ReadOnlySpan<byte> png, double alphaThreshold, CancellationToken cancellationToken)
        => RawRgbaTransforms.FindTrimBounds(png, alphaThreshold, cancellationToken);

    public static byte[] RemoveBackground(ReadOnlySpan<byte> png, SKColor background, double tolerance, double feather)
        => RawRgbaTransforms.RemoveBackground(png, background, tolerance, feather);

    public static byte[] RemoveBackground(ReadOnlySpan<byte> png, SKColor background, double tolerance, double feather, CancellationToken cancellationToken)
        => RawRgbaTransforms.RemoveBackground(png, background, tolerance, feather, cancellationToken);

    public static SKColor Sample(ReadOnlySpan<byte> png, int x, int y)
        => RawRgbaTransforms.Sample(png, x, y);

    public static void RemapAuthoringForCrop(GelConfig config, PixelRect crop, int oldWidth, int oldHeight)
    {
        var x0 = crop.X / (double)oldWidth;
        var y0 = crop.Y / (double)oldHeight;
        var sx = oldWidth / (double)crop.Width;
        var sy = oldHeight / (double)crop.Height;
        config.Cores.RemoveAll(core =>
            core.X + core.RadiusX < x0 || core.X - core.RadiusX > crop.Right / (double)oldWidth ||
            core.Y + core.RadiusY < y0 || core.Y - core.RadiusY > crop.Bottom / (double)oldHeight);
        foreach (var core in config.Cores)
        {
            core.X = Math.Clamp((core.X - x0) * sx, -1, 2);
            core.Y = Math.Clamp((core.Y - y0) * sy, -1, 2);
            core.RadiusX = Math.Clamp(core.RadiusX * sx, double.Epsilon, 2);
            core.RadiusY = Math.Clamp(core.RadiusY * sy, double.Epsilon, 2);
        }

        var remappedStrokes = new List<RigidityStroke>();
        foreach (var stroke in config.RigidityStrokes)
        {
            var clippedParts = ClipStroke(stroke.Points, x0 - stroke.Radius, y0 - stroke.Radius,
                crop.Right / (double)oldWidth + stroke.Radius, crop.Bottom / (double)oldHeight + stroke.Radius);
            foreach (var part in clippedParts)
            {
                if (remappedStrokes.Count >= GelValidator.MaxStrokes) break;
                var points = part.Select(point => new[]
                {
                    Math.Clamp((point[0] - x0) * sx, -1, 2),
                    Math.Clamp((point[1] - y0) * sy, -1, 2)
                }).ToList();
                if (points.Count == 0) continue;
                remappedStrokes.Add(new RigidityStroke
                {
                    Radius = Math.Clamp(stroke.Radius * Math.Sqrt(sx * sy), double.Epsilon, 1),
                    Strength = stroke.Strength,
                    Points = points
                });
            }
            if (remappedStrokes.Count >= GelValidator.MaxStrokes) break;
        }
        config.RigidityStrokes = remappedStrokes;
        config.Image.Width = crop.Width;
        config.Image.Height = crop.Height;
    }

    public static byte[] EncodePng(SKBitmap bitmap) => RawRgbaCodec.Encode(bitmap);

    private static List<List<double[]>> ClipStroke(IReadOnlyList<double[]> points, double left, double top, double right, double bottom)
    {
        var parts = new List<List<double[]>>();
        if (points.Count == 1)
        {
            var point = points[0];
            if (point[0] >= left && point[0] <= right && point[1] >= top && point[1] <= bottom)
                parts.Add([new[] { point[0], point[1] }]);
            return parts;
        }

        List<double[]>? current = null;
        for (var index = 1; index < points.Count; index++)
        {
            if (!ClipSegment(points[index - 1], points[index], left, top, right, bottom, out var start, out var end))
            {
                current = null;
                continue;
            }

            if (current is null || !SamePoint(current[^1], start))
            {
                current = [start];
                parts.Add(current);
            }
            if (!SamePoint(current[^1], end)) current.Add(end);
        }
        return parts;
    }

    private static bool ClipSegment(double[] a, double[] b, double left, double top, double right, double bottom,
        out double[] start, out double[] end)
    {
        var dx = b[0] - a[0];
        var dy = b[1] - a[1];
        var t0 = 0d;
        var t1 = 1d;
        if (!ClipTest(-dx, a[0] - left, ref t0, ref t1) ||
            !ClipTest(dx, right - a[0], ref t0, ref t1) ||
            !ClipTest(-dy, a[1] - top, ref t0, ref t1) ||
            !ClipTest(dy, bottom - a[1], ref t0, ref t1))
        {
            start = end = [];
            return false;
        }
        start = [a[0] + t0 * dx, a[1] + t0 * dy];
        end = [a[0] + t1 * dx, a[1] + t1 * dy];
        return true;
    }

    private static bool ClipTest(double p, double q, ref double t0, ref double t1)
    {
        if (Math.Abs(p) < 1e-12) return q >= 0;
        var ratio = q / p;
        if (p < 0)
        {
            if (ratio > t1) return false;
            if (ratio > t0) t0 = ratio;
        }
        else
        {
            if (ratio < t0) return false;
            if (ratio < t1) t1 = ratio;
        }
        return true;
    }

    private static bool SamePoint(double[] a, double[] b)
        => Math.Abs(a[0] - b[0]) < 1e-10 && Math.Abs(a[1] - b[1]) < 1e-10;
}
