using Gelatin.Core.Models;
using SkiaSharp;

namespace Gelatin.Core.Imaging;

public static class RawRgbaTransforms
{
    public static byte[] NormalizeToPng(ReadOnlySpan<byte> encoded)
    {
        var image = RawRgbaCodec.Decode(encoded);
        return RawRgbaCodec.Encode(image.Width, image.Height, image.Pixels);
    }

    public static byte[] Crop(ReadOnlySpan<byte> png, PixelRect rect)
    {
        var source = RawRgbaCodec.Decode(png);
        if (rect.Width < 1 || rect.Height < 1 || rect.X < 0 || rect.Y < 0 || rect.Right > source.Width || rect.Bottom > source.Height)
            throw new GelFormatException("The crop rectangle must be inside the current image.");
        var result = new byte[checked(rect.Width * rect.Height * 4)];
        var sourceStride = checked(source.Width * 4);
        var resultStride = checked(rect.Width * 4);
        for (var y = 0; y < rect.Height; y++)
            source.Pixels.AsSpan(checked((rect.Y + y) * sourceStride + rect.X * 4), resultStride).CopyTo(result.AsSpan(checked(y * resultStride), resultStride));
        return RawRgbaCodec.Encode(rect.Width, rect.Height, result);
    }

    public static byte[] Resize(ReadOnlySpan<byte> png, int width, int height)
    {
        if (width is < 1 or > GelValidator.MaxDimension || height is < 1 or > GelValidator.MaxDimension)
            throw new GelFormatException($"Resize dimensions must be between 1 and {GelValidator.MaxDimension} pixels.");
        var source = RawRgbaCodec.Decode(png);
        var result = new byte[checked(width * height * 4)];
        for (var y = 0; y < height; y++)
        {
            var sy = (y + 0.5) * source.Height / height - 0.5;
            var y0 = Math.Clamp((int)Math.Floor(sy), 0, source.Height - 1);
            var y1 = Math.Min(source.Height - 1, y0 + 1);
            var fy = Math.Clamp(sy - Math.Floor(sy), 0, 1);
            for (var x = 0; x < width; x++)
            {
                var sx = (x + 0.5) * source.Width / width - 0.5;
                var x0 = Math.Clamp((int)Math.Floor(sx), 0, source.Width - 1);
                var x1 = Math.Min(source.Width - 1, x0 + 1);
                var fx = Math.Clamp(sx - Math.Floor(sx), 0, 1);
                var destination = (y * width + x) * 4;
                var p00 = (y0 * source.Width + x0) * 4;
                var p10 = (y0 * source.Width + x1) * 4;
                var p01 = (y1 * source.Width + x0) * 4;
                var p11 = (y1 * source.Width + x1) * 4;
                for (var channel = 0; channel < 4; channel++)
                {
                    var top = source.Pixels[p00 + channel] + (source.Pixels[p10 + channel] - source.Pixels[p00 + channel]) * fx;
                    var bottom = source.Pixels[p01 + channel] + (source.Pixels[p11 + channel] - source.Pixels[p01 + channel]) * fx;
                    result[destination + channel] = (byte)Math.Clamp(Math.Round(top + (bottom - top) * fy), 0, 255);
                }
            }
        }
        return RawRgbaCodec.Encode(width, height, result);
    }

    public static PixelRect? FindTrimBounds(ReadOnlySpan<byte> png, double alphaThreshold)
    {
        var image = RawRgbaCodec.Decode(png);
        var threshold = (byte)Math.Clamp(Math.Round(alphaThreshold * 255), 0, 255);
        var minX = image.Width; var minY = image.Height; var maxX = -1; var maxY = -1;
        for (var y = 0; y < image.Height; y++)
        for (var x = 0; x < image.Width; x++)
        {
            if (image.Pixels[(y * image.Width + x) * 4 + 3] <= threshold) continue;
            minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
        }
        return maxX < minX ? null : new PixelRect(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    public static byte[] RemoveBackground(ReadOnlySpan<byte> png, SKColor background, double tolerance, double feather)
    {
        tolerance = Math.Clamp(tolerance, 0, 1); feather = Math.Clamp(feather, 0, 1);
        var image = RawRgbaCodec.Decode(png);
        var hard = tolerance * Math.Sqrt(3 * 255d * 255d);
        var soft = feather * Math.Sqrt(3 * 255d * 255d) * 0.35;
        for (var offset = 0; offset < image.Pixels.Length; offset += 4)
        {
            var dr = image.Pixels[offset] - background.Red;
            var dg = image.Pixels[offset + 1] - background.Green;
            var db = image.Pixels[offset + 2] - background.Blue;
            var distance = Math.Sqrt(dr * dr + dg * dg + db * db);
            var keep = soft <= 0.00001 ? (distance <= hard ? 0d : 1d) : SmoothStep(hard - soft, hard + soft, distance);
            var originalAlpha = image.Pixels[offset + 3];
            image.Pixels[offset + 3] = (byte)Math.Clamp(Math.Round(originalAlpha * keep), 0, originalAlpha);
        }
        return RawRgbaCodec.Encode(image.Width, image.Height, image.Pixels);
    }

    public static SKColor Sample(ReadOnlySpan<byte> png, int x, int y)
    {
        var image = RawRgbaCodec.Decode(png);
        x = Math.Clamp(x, 0, image.Width - 1); y = Math.Clamp(y, 0, image.Height - 1);
        var offset = (y * image.Width + x) * 4;
        return new SKColor(image.Pixels[offset], image.Pixels[offset + 1], image.Pixels[offset + 2], image.Pixels[offset + 3]);
    }

    private static double SmoothStep(double edge0, double edge1, double value)
    {
        var t = Math.Clamp((value - edge0) / Math.Max(edge1 - edge0, 1e-9), 0, 1);
        return t * t * (3 - 2 * t);
    }
}
