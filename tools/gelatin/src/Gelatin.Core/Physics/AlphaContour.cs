using System.Numerics;
using Gelatin.Core.Imaging;

namespace Gelatin.Core.Physics;

public sealed class AlphaContour
{
    public required IReadOnlyList<Vector2> Points { get; init; }
    public bool Closed { get; init; } = true;
}

public static class AlphaContourExtractor
{
    private readonly record struct GridPoint(int X, int Y);
    private readonly record struct Edge(GridPoint A, GridPoint B);

    public static IReadOnlyList<AlphaContour> Extract(ReadOnlySpan<byte> png, double threshold, int targetSamples = 128)
    {
        using var bitmap = ImageProcessor.Decode(png);
        var width = bitmap.Width;
        var height = bitmap.Height;
        var mask = new bool[width * height];
        var alpha = (byte)Math.Clamp(Math.Round(threshold * 255), 0, 255);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++) mask[y * width + x] = bitmap.GetPixel(x, y).Alpha > alpha;

        var edges = new List<Edge>();
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            if (!mask[y * width + x]) continue;
            if (y == 0 || !mask[(y - 1) * width + x]) edges.Add(new Edge(new GridPoint(x, y), new GridPoint(x + 1, y)));
            if (x == width - 1 || !mask[y * width + x + 1]) edges.Add(new Edge(new GridPoint(x + 1, y), new GridPoint(x + 1, y + 1)));
            if (y == height - 1 || !mask[(y + 1) * width + x]) edges.Add(new Edge(new GridPoint(x + 1, y + 1), new GridPoint(x, y + 1)));
            if (x == 0 || !mask[y * width + x - 1]) edges.Add(new Edge(new GridPoint(x, y + 1), new GridPoint(x, y)));
        }

        if (edges.Count == 0) return [];
        var byStart = edges.Select((edge, index) => (edge, index)).GroupBy(item => item.edge.A)
            .ToDictionary(group => group.Key, group => new Queue<int>(group.Select(item => item.index)));
        var used = new bool[edges.Count];
        var loops = new List<List<GridPoint>>();
        for (var startIndex = 0; startIndex < edges.Count; startIndex++)
        {
            if (used[startIndex]) continue;
            var loop = new List<GridPoint>();
            var currentIndex = startIndex;
            var guard = 0;
            while (!used[currentIndex] && guard++ <= edges.Count)
            {
                used[currentIndex] = true;
                var current = edges[currentIndex];
                loop.Add(current.A);
                if (current.B == edges[startIndex].A) break;
                if (!byStart.TryGetValue(current.B, out var nextCandidates)) break;
                while (nextCandidates.Count > 0 && used[nextCandidates.Peek()]) nextCandidates.Dequeue();
                if (nextCandidates.Count == 0) break;
                currentIndex = nextCandidates.Dequeue();
            }
            if (loop.Count >= 4) loops.Add(loop);
        }

        var totalPerimeter = loops.Sum(Perimeter);
        var result = new List<AlphaContour>();
        foreach (var loop in loops.OrderByDescending(Perimeter))
        {
            var share = totalPerimeter <= 0 ? targetSamples : Math.Max(12, (int)Math.Round(targetSamples * Perimeter(loop) / totalPerimeter));
            var normalized = loop.Select(point => new Vector2(point.X / (float)width, point.Y / (float)height)).ToList();
            var resampled = ResampleClosed(normalized, Math.Min(share, normalized.Count));
            if (resampled.Count >= 3) result.Add(new AlphaContour { Points = resampled });
        }
        return result;
    }

    private static double Perimeter(IReadOnlyList<GridPoint> loop)
    {
        double total = 0;
        for (var i = 0; i < loop.Count; i++)
        {
            var a = loop[i];
            var b = loop[(i + 1) % loop.Count];
            total += Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
        }
        return total;
    }

    private static List<Vector2> ResampleClosed(IReadOnlyList<Vector2> points, int count)
    {
        if (points.Count <= count) return [.. points];
        var cumulative = new double[points.Count + 1];
        for (var i = 0; i < points.Count; i++) cumulative[i + 1] = cumulative[i] + Vector2.Distance(points[i], points[(i + 1) % points.Count]);
        var total = cumulative[^1];
        var result = new List<Vector2>(count);
        var segment = 0;
        for (var sample = 0; sample < count; sample++)
        {
            var distance = total * sample / count;
            while (segment + 1 < cumulative.Length && cumulative[segment + 1] < distance) segment++;
            var a = points[segment % points.Count];
            var b = points[(segment + 1) % points.Count];
            var length = cumulative[segment + 1] - cumulative[segment];
            var t = length <= 1e-12 ? 0 : (distance - cumulative[segment]) / length;
            result.Add(Vector2.Lerp(a, b, (float)t));
        }
        return result;
    }
}
