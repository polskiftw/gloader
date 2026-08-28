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
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static (int Width, int Height) GetDimensions(ReadOnlySpan<byte> encoded)
    {
        if (encoded.Length < PngSignature.Length || !encoded[..8].SequenceEqual(PngSignature))
            throw new GelFormatException("The embedded image is not a valid PNG payload.");
        try
        {
            using var bitmap = Decode(encoded);
            return (bitmap.Width, bitmap.Height);
        }
        catch (GelFormatException) { throw; }
        catch (Exception ex) when (ex is ArgumentException or OverflowException)
        {
            throw new GelFormatException("The embedded PNG could not be decoded.", ex);
        }
    }

    public static byte[] NormalizeToPng(ReadOnlySpan<byte> encoded)
    {
        using var bitmap = Decode(encoded);
        return EncodePng(bitmap);
    }

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
    {
        using var source = Decode(png);
        ValidateRect(rect, source.Width, source.Height);
        using var result = new SKBitmap(new SKImageInfo(rect.Width, rect.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.Transparent);
        canvas.DrawBitmap(source, new SKRect(rect.X, rect.Y, rect.Right, rect.Bottom), new SKRect(0, 0, rect.Width, rect.Height));
        return EncodePng(result);
    }

    public static byte[] Resize(ReadOnlySpan<byte> png, int width, int height)
    {
        if (width is < 1 or > GelValidator.MaxDimension || height is < 1 or > GelValidator.MaxDimension)
            throw new GelFormatException($"Resize dimensions must be between 1 and {GelValidator.MaxDimension} pixels.");
        using var source = Decode(png);
        using var result = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.Transparent);
#pragma warning disable CS0618 // SkiaSharp 3.119.4 keeps this overload for bitmap destination scaling.
        using var paint = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High };
#pragma warning restore CS0618
        canvas.DrawBitmap(source, new SKRect(0, 0, width, height), paint);
        return EncodePng(result);
    }

    public static PixelRect? FindTrimBounds(ReadOnlySpan<byte> png, double alphaThreshold)
    {
        using var bitmap = Decode(png);
        var threshold = (byte)Math.Clamp(Math.Round(alphaThreshold * 255), 0, 255);
        var minX = bitmap.Width;
        var minY = bitmap.Height;
        var maxX = -1;
        var maxY = -1;
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
        {
            if (bitmap.GetPixel(x, y).Alpha <= threshold) continue;
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
        }
        return maxX < minX ? null : new PixelRect(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    public static byte[] RemoveBackground(ReadOnlySpan<byte> png, SKColor background, double tolerance, double feather)
    {
        tolerance = Math.Clamp(tolerance, 0, 1);
        feather = Math.Clamp(feather, 0, 1);
        using var bitmap = Decode(png);
        var hard = tolerance * Math.Sqrt(3 * 255d * 255d);
        var soft = feather * Math.Sqrt(3 * 255d * 255d) * 0.35;
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
        {
            var color = bitmap.GetPixel(x, y);
            var dr = color.Red - background.Red;
            var dg = color.Green - background.Green;
            var db = color.Blue - background.Blue;
            var distance = Math.Sqrt(dr * dr + dg * dg + db * db);
            double keep;
            if (soft <= 0.00001) keep = distance <= hard ? 0 : 1;
            else keep = SmoothStep(hard - soft, hard + soft, distance);
            var alpha = (byte)Math.Clamp(Math.Round(color.Alpha * keep), 0, color.Alpha);
            bitmap.SetPixel(x, y, new SKColor(color.Red, color.Green, color.Blue, alpha));
        }
        return EncodePng(bitmap);
    }

    public static SKColor Sample(ReadOnlySpan<byte> png, int x, int y)
    {
        using var bitmap = Decode(png);
        x = Math.Clamp(x, 0, bitmap.Width - 1);
        y = Math.Clamp(y, 0, bitmap.Height - 1);
        return bitmap.GetPixel(x, y);
    }

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

    public static byte[] EncodePng(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100) ?? throw new GelFormatException("The processed image could not be encoded as PNG.");
        return data.ToArray();
    }

    private static double SmoothStep(double edge0, double edge1, double value)
    {
        var t = Math.Clamp((value - edge0) / Math.Max(edge1 - edge0, 1e-9), 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static void ValidateRect(PixelRect rect, int width, int height)
    {
        if (rect.Width < 1 || rect.Height < 1 || rect.X < 0 || rect.Y < 0 || rect.Right > width || rect.Bottom > height)
            throw new GelFormatException("The crop rectangle must be inside the current image.");
    }

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
