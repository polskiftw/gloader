using System.Numerics;
using Gelatin.Core.Models;

namespace Gelatin.Core.Authoring;

public static class InfluenceFields
{
    public static double CoreInfluence(CoreConfig core, Vector2 uv)
    {
        var dx = (uv.X - core.X) / Math.Max(core.RadiusX, 1e-8);
        var dy = (uv.Y - core.Y) / Math.Max(core.RadiusY, 1e-8);
        var radius = Math.Sqrt(dx * dx + dy * dy);
        if (radius >= 1) return 0;
        var edgePower = 1.25 + core.Falloff * 6;
        var value = Math.Pow(1 - radius * radius, edgePower);
        return Math.Clamp(value, 0, 1);
    }

    public static double CombinedCoreInfluence(IEnumerable<CoreConfig> cores, Vector2 uv)
    {
        var remaining = 1d;
        foreach (var core in cores) remaining *= 1 - CoreInfluence(core, uv);
        return 1 - remaining;
    }

    public static double Rigidity(IEnumerable<RigidityStroke> strokes, Vector2 uv)
    {
        var remaining = 1d;
        foreach (var stroke in strokes)
        {
            if (stroke.Points.Count == 0) continue;
            var distance = stroke.Points.Count == 1
                ? Vector2.Distance(uv, ToVector(stroke.Points[0]))
                : MinimumPolylineDistance(stroke, uv);
            if (distance >= stroke.Radius) continue;
            var t = 1 - distance / Math.Max(stroke.Radius, 1e-8);
            var contribution = stroke.Strength * t * t * (3 - 2 * t);
            remaining *= 1 - Math.Clamp(contribution, 0, 1);
        }
        return 1 - remaining;
    }

    public static void Erase(List<RigidityStroke> strokes, Vector2 center, double radius, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        if (amount <= 0 || radius <= 0 || strokes.Count == 0) return;

        for (var strokeIndex = strokes.Count - 1; strokeIndex >= 0; strokeIndex--)
        {
            var stroke = strokes[strokeIndex];
            var eraseRadius = (float)Math.Max(1e-8, radius + stroke.Radius * 0.35);
            if (!IntersectsEraseBrush(stroke, center, eraseRadius)) continue;

            if (amount < 0.999)
            {
                stroke.Strength *= 1 - amount;
                if (stroke.Strength < 0.001) strokes.RemoveAt(strokeIndex);
                continue;
            }

            var fragments = ClipOutsideCircle(stroke.Points, center, eraseRadius)
                .SelectMany(SplitForPointLimit)
                .Where(fragment => fragment.Count > 0)
                .ToList();

            strokes.RemoveAt(strokeIndex);
            if (fragments.Count == 0) continue;

            var capacity = Math.Max(0, GelValidator.MaxStrokes - strokes.Count);
            if (fragments.Count > capacity)
            {
                fragments = fragments
                    .Select((points, index) => new { Points = points, Index = index, Length = PolylineLength(points) })
                    .OrderByDescending(item => item.Length)
                    .ThenBy(item => item.Index)
                    .Take(capacity)
                    .OrderBy(item => item.Index)
                    .Select(item => item.Points)
                    .ToList();
            }

            for (var fragmentIndex = fragments.Count - 1; fragmentIndex >= 0; fragmentIndex--)
            {
                strokes.Insert(strokeIndex, new RigidityStroke
                {
                    Radius = stroke.Radius,
                    Strength = stroke.Strength,
                    Points = fragments[fragmentIndex]
                });
            }
        }
    }

    private static bool IntersectsEraseBrush(RigidityStroke stroke, Vector2 center, float radius)
    {
        if (stroke.Points.Count == 0) return false;
        if (stroke.Points.Count == 1) return Vector2.Distance(ToVector(stroke.Points[0]), center) <= radius;
        return MinimumPolylineDistance(stroke, center) <= radius;
    }

    private static List<List<double[]>> ClipOutsideCircle(IReadOnlyList<double[]> points, Vector2 center, float radius)
    {
        var fragments = new List<List<double[]>>();
        if (points.Count == 0) return fragments;
        if (points.Count == 1)
        {
            if (Vector2.Distance(ToVector(points[0]), center) > radius) fragments.Add([ClonePoint(points[0])]);
            return fragments;
        }

        List<double[]>? current = null;
        var radiusSquared = radius * radius;
        for (var segmentIndex = 1; segmentIndex < points.Count; segmentIndex++)
        {
            var a = ToVector(points[segmentIndex - 1]);
            var b = ToVector(points[segmentIndex]);
            var cuts = new List<float> { 0, 1 };
            cuts.AddRange(SegmentCircleIntersections(a, b, center, radius));
            cuts.Sort();

            var uniqueCuts = new List<float>(cuts.Count);
            foreach (var cut in cuts)
            {
                var clamped = Math.Clamp(cut, 0, 1);
                if (uniqueCuts.Count == 0 || Math.Abs(clamped - uniqueCuts[^1]) > 1e-6f) uniqueCuts.Add(clamped);
            }

            for (var interval = 1; interval < uniqueCuts.Count; interval++)
            {
                var startT = uniqueCuts[interval - 1];
                var endT = uniqueCuts[interval];
                if (endT - startT <= 1e-6f) continue;
                var midpoint = Vector2.Lerp(a, b, (startT + endT) * 0.5f);
                var outside = Vector2.DistanceSquared(midpoint, center) >= radiusSquared - 1e-9f;
                if (!outside)
                {
                    current = null;
                    continue;
                }

                var start = Vector2.Lerp(a, b, startT);
                var end = Vector2.Lerp(a, b, endT);
                if (current is null)
                {
                    current = [];
                    fragments.Add(current);
                    AddPoint(current, start);
                }
                else
                {
                    AddPoint(current, start);
                }
                AddPoint(current, end);
            }
        }

        return fragments;
    }

    private static IEnumerable<List<double[]>> SplitForPointLimit(List<double[]> points)
    {
        if (points.Count <= GelValidator.MaxPointsPerStroke)
        {
            yield return points;
            yield break;
        }

        var start = 0;
        while (start < points.Count)
        {
            var count = Math.Min(GelValidator.MaxPointsPerStroke, points.Count - start);
            var fragment = points.GetRange(start, count);
            if (fragment.Count > 0) yield return fragment;
            if (start + count >= points.Count) yield break;
            start += count - 1;
        }
    }

    private static IEnumerable<float> SegmentCircleIntersections(Vector2 a, Vector2 b, Vector2 center, float radius)
    {
        var delta = b - a;
        var relative = a - center;
        var qa = Vector2.Dot(delta, delta);
        if (qa < 1e-12f) yield break;
        var qb = 2 * Vector2.Dot(relative, delta);
        var qc = Vector2.Dot(relative, relative) - radius * radius;
        var discriminant = qb * qb - 4 * qa * qc;
        if (discriminant <= 1e-12f) yield break;
        var root = MathF.Sqrt(discriminant);
        var first = (-qb - root) / (2 * qa);
        var second = (-qb + root) / (2 * qa);
        if (first > 1e-6f && first < 1 - 1e-6f) yield return first;
        if (second > 1e-6f && second < 1 - 1e-6f && Math.Abs(second - first) > 1e-6f) yield return second;
    }

    private static void AddPoint(List<double[]> points, Vector2 point)
    {
        if (points.Count > 0 && Vector2.DistanceSquared(ToVector(points[^1]), point) < 1e-12f) return;
        points.Add([(double)point.X, (double)point.Y]);
    }

    private static double PolylineLength(IReadOnlyList<double[]> points)
    {
        double length = 0;
        for (var i = 1; i < points.Count; i++) length += Vector2.Distance(ToVector(points[i - 1]), ToVector(points[i]));
        return length;
    }

    private static double MinimumPolylineDistance(RigidityStroke stroke, Vector2 point)
    {
        var minimum = double.PositiveInfinity;
        for (var i = 1; i < stroke.Points.Count; i++)
        {
            var a = ToVector(stroke.Points[i - 1]);
            var b = ToVector(stroke.Points[i]);
            var ab = b - a;
            var t = ab.LengthSquared() < 1e-12f ? 0 : Math.Clamp(Vector2.Dot(point - a, ab) / ab.LengthSquared(), 0, 1);
            minimum = Math.Min(minimum, Vector2.Distance(point, a + ab * t));
        }
        return minimum;
    }

    private static double[] ClonePoint(double[] point) => [(double)point[0], (double)point[1]];
    private static Vector2 ToVector(double[] point) => new((float)point[0], (float)point[1]);
}
