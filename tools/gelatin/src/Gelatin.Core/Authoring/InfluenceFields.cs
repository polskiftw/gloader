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
        for (var strokeIndex = strokes.Count - 1; strokeIndex >= 0; strokeIndex--)
        {
            var stroke = strokes[strokeIndex];
            var kept = stroke.Points.Where(point => Vector2.Distance(ToVector(point), center) > radius + stroke.Radius * 0.35).ToList();
            if (amount >= 0.999)
            {
                stroke.Points = kept;
                if (stroke.Points.Count == 0) strokes.RemoveAt(strokeIndex);
            }
            else if (kept.Count != stroke.Points.Count)
            {
                stroke.Strength *= 1 - amount;
                if (stroke.Strength < 0.001) strokes.RemoveAt(strokeIndex);
            }
        }
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

    private static Vector2 ToVector(double[] point) => new((float)point[0], (float)point[1]);
}
