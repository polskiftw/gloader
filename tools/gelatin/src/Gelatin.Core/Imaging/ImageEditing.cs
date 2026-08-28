using Gelatin.Core.Models;
using SkiaSharp;

namespace Gelatin.Core.Imaging;

public readonly record struct PixelPoint(double X, double Y);

public readonly record struct PolygonValidation(bool IsValid, string? Error)
{
    public static PolygonValidation Valid => new(true, null);
    public static PolygonValidation Invalid(string error) => new(false, error);
}

public static class PolygonGeometry
{
    private const double Epsilon = 1e-7;

    public static PolygonValidation Validate(IReadOnlyList<PixelPoint> points)
    {
        if (points.Count < 3) return PolygonValidation.Invalid("A polygon needs at least 3 vertices.");
        if (points.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y)))
            return PolygonValidation.Invalid("Polygon vertices must contain finite coordinates.");

        var distinct = new List<PixelPoint>();
        foreach (var point in points)
        {
            if (distinct.All(existing => DistanceSquared(existing, point) > Epsilon * Epsilon)) distinct.Add(point);
        }
        if (distinct.Count < 3) return PolygonValidation.Invalid("A polygon needs at least 3 distinct vertices.");

        // Prefer the actionable self-intersection diagnostic for bow-tie polygons. Their signed
        // shoelace area can cancel to zero even though the shape is not merely degenerate.
        for (var i = 0; i < points.Count; i++)
        {
            var a1 = points[i];
            var a2 = points[(i + 1) % points.Count];
            for (var j = i + 1; j < points.Count; j++)
            {
                if (AreAdjacentEdges(i, j, points.Count)) continue;
                var b1 = points[j];
                var b2 = points[(j + 1) % points.Count];
                if (SegmentsIntersect(a1, a2, b1, b2))
                    return PolygonValidation.Invalid("The polygon crosses itself. Move the vertices so edges do not intersect.");
            }
        }

        var twiceArea = 0d;
        for (var i = 0; i < points.Count; i++)
        {
            var next = points[(i + 1) % points.Count];
            twiceArea += points[i].X * next.Y - next.X * points[i].Y;
        }
        if (Math.Abs(twiceArea) <= Epsilon)
            return PolygonValidation.Invalid("The polygon has effectively zero area.");

        return PolygonValidation.Valid;
    }

    public static PixelPoint Clamp(PixelPoint point, int width, int height)
        => new(Math.Clamp(point.X, 0, Math.Max(0, width)), Math.Clamp(point.Y, 0, Math.Max(0, height)));

    public static PixelPoint Nudge(PixelPoint point, int dx, int dy, int width, int height)
        => Clamp(new PixelPoint(point.X + dx, point.Y + dy), width, height);

    public static PixelPoint ProjectToSegment(PixelPoint point, PixelPoint start, PixelPoint end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var lengthSquared = dx * dx + dy * dy;
        if (lengthSquared <= Epsilon * Epsilon) return start;
        var t = Math.Clamp(((point.X - start.X) * dx + (point.Y - start.Y) * dy) / lengthSquared, 0, 1);
        return new PixelPoint(start.X + dx * t, start.Y + dy * t);
    }

    public static List<PixelPoint> InsertOnEdge(IReadOnlyList<PixelPoint> points, int edgeIndex, PixelPoint point)
    {
        if (points.Count < 2) throw new ArgumentException("At least two vertices are required.", nameof(points));
        if (edgeIndex < 0 || edgeIndex >= points.Count) throw new ArgumentOutOfRangeException(nameof(edgeIndex));
        var projected = ProjectToSegment(point, points[edgeIndex], points[(edgeIndex + 1) % points.Count]);
        var result = points.ToList();
        result.Insert(edgeIndex + 1, projected);
        return result;
    }

    private static bool AreAdjacentEdges(int a, int b, int count)
        => a == b || Math.Abs(a - b) == 1 || (a == 0 && b == count - 1);

    private static bool SegmentsIntersect(PixelPoint a, PixelPoint b, PixelPoint c, PixelPoint d)
    {
        var o1 = Orientation(a, b, c);
        var o2 = Orientation(a, b, d);
        var o3 = Orientation(c, d, a);
        var o4 = Orientation(c, d, b);

        if (o1 * o2 < -Epsilon && o3 * o4 < -Epsilon) return true;
        if (Math.Abs(o1) <= Epsilon && OnSegment(a, b, c)) return true;
        if (Math.Abs(o2) <= Epsilon && OnSegment(a, b, d)) return true;
        if (Math.Abs(o3) <= Epsilon && OnSegment(c, d, a)) return true;
        if (Math.Abs(o4) <= Epsilon && OnSegment(c, d, b)) return true;
        return false;
    }

    private static double Orientation(PixelPoint a, PixelPoint b, PixelPoint c)
        => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

    private static bool OnSegment(PixelPoint a, PixelPoint b, PixelPoint p)
        => p.X >= Math.Min(a.X, b.X) - Epsilon && p.X <= Math.Max(a.X, b.X) + Epsilon &&
           p.Y >= Math.Min(a.Y, b.Y) - Epsilon && p.Y <= Math.Max(a.Y, b.Y) + Epsilon;

    private static double DistanceSquared(PixelPoint a, PixelPoint b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }
}

public enum AlphaBrushMode
{
    Erase,
    Restore
}

public sealed class AlphaBrushSession : IDisposable
{
    private readonly RgbaBuffer _working;
    private readonly RgbaBuffer? _recovery;
    private readonly AlphaBrushMode _mode;
    private readonly double _size;
    private bool _disposed;

    public int Width => _working.Width;
    public int Height => _working.Height;

    public AlphaBrushSession(ReadOnlySpan<byte> png, ReadOnlySpan<byte> recoveryPng, AlphaBrushMode mode, double size)
    {
        if (!double.IsFinite(size) || size < 1 || size > 4096)
            throw new GelFormatException("Alpha brush size must be between 1 and 4096 source pixels.");
        _working = RawRgbaCodec.Decode(png);
        _mode = mode;
        _size = size;

        if (mode == AlphaBrushMode.Restore)
        {
            _recovery = RawRgbaCodec.Decode(recoveryPng);
            if (_recovery.Width != _working.Width || _recovery.Height != _working.Height)
                throw new GelFormatException("The alpha recovery source does not match the current image geometry.");
        }
    }

    public void ApplyPoint(PixelPoint point) => Stamp(ClampCenter(point));

    public void ApplySegment(PixelPoint start, PixelPoint end)
    {
        ThrowIfDisposed();
        start = ClampCenter(start);
        end = ClampCenter(end);
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        var step = Math.Max(0.25, _size * 0.25);
        var segments = Math.Max(1, (int)Math.Ceiling(distance / step));
        for (var i = 0; i <= segments; i++)
        {
            var t = i / (double)segments;
            Stamp(new PixelPoint(start.X + dx * t, start.Y + dy * t));
        }
    }

    public byte[] Encode()
    {
        ThrowIfDisposed();
        return RawRgbaCodec.Encode(Width, Height, _working.Pixels);
    }

    public void Dispose() => _disposed = true;

    private PixelPoint ClampCenter(PixelPoint point)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
            throw new GelFormatException("Alpha brush coordinates must be finite.");
        return new PixelPoint(Math.Clamp(point.X, 0, Width), Math.Clamp(point.Y, 0, Height));
    }

    private void Stamp(PixelPoint center)
    {
        ThrowIfDisposed();
        var radius = _size / 2d;
        var radiusSquared = radius * radius + 1e-9;
        var minX = Math.Max(0, (int)Math.Floor(center.X - radius));
        var minY = Math.Max(0, (int)Math.Floor(center.Y - radius));
        var maxX = Math.Min(Width - 1, (int)Math.Ceiling(center.X + radius));
        var maxY = Math.Min(Height - 1, (int)Math.Ceiling(center.Y + radius));

        for (var y = minY; y <= maxY; y++)
        for (var x = minX; x <= maxX; x++)
        {
            var dx = x + 0.5 - center.X;
            var dy = y + 0.5 - center.Y;
            if (dx * dx + dy * dy > radiusSquared) continue;
            var offset = (y * Width + x) * 4;
            if (_mode == AlphaBrushMode.Erase)
            {
                _working.Pixels[offset + 3] = 0;
            }
            else
            {
                _recovery!.Pixels.AsSpan(offset, 4).CopyTo(_working.Pixels.AsSpan(offset, 4));
            }
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}

public static class ImageAlphaEditing
{
    public static byte[] ApplyRectCutout(ReadOnlySpan<byte> png, PixelRect keep)
    {
        var source = RawRgbaCodec.Decode(png);
        if (keep.Width < 1 || keep.Height < 1 || keep.X < 0 || keep.Y < 0 || keep.Right > source.Width || keep.Bottom > source.Height)
            throw new GelFormatException("The crop rectangle must be inside the current image.");
        for (var y = 0; y < source.Height; y++)
        for (var x = 0; x < source.Width; x++)
        {
            if (x >= keep.X && x < keep.Right && y >= keep.Y && y < keep.Bottom) continue;
            source.Pixels[(y * source.Width + x) * 4 + 3] = 0;
        }
        return RawRgbaCodec.Encode(source.Width, source.Height, source.Pixels);
    }

    public static byte[] ApplyPolygonCutout(ReadOnlySpan<byte> png, IReadOnlyList<PixelPoint> polygon)
    {
        var validation = PolygonGeometry.Validate(polygon);
        if (!validation.IsValid) throw new GelFormatException(validation.Error!);

        var source = RawRgbaCodec.Decode(png);
        if (polygon.Any(point => point.X < 0 || point.Y < 0 || point.X > source.Width || point.Y > source.Height))
            throw new GelFormatException("Polygon vertices must stay inside the current image bounds.");

        using var mask = new SKBitmap(new SKImageInfo(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        mask.Erase(SKColors.Transparent);
        using (var canvas = new SKCanvas(mask))
        using (var path = new SKPath())
        using (var paint = new SKPaint { IsAntialias = true, Color = SKColors.White, Style = SKPaintStyle.Fill })
        {
            path.MoveTo((float)polygon[0].X, (float)polygon[0].Y);
            for (var i = 1; i < polygon.Count; i++) path.LineTo((float)polygon[i].X, (float)polygon[i].Y);
            path.Close();
            canvas.DrawPath(path, paint);
            canvas.Flush();
        }

        for (var y = 0; y < source.Height; y++)
        for (var x = 0; x < source.Width; x++)
        {
            var offset = (y * source.Width + x) * 4;
            var originalAlpha = source.Pixels[offset + 3];
            var coverage = mask.GetPixel(x, y).Alpha;
            source.Pixels[offset + 3] = (byte)Math.Min(originalAlpha, (originalAlpha * coverage + 127) / 255);
        }

        return RawRgbaCodec.Encode(source.Width, source.Height, source.Pixels);
    }
}
