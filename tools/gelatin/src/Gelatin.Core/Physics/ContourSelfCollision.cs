using System.Numerics;

namespace Gelatin.Core.Physics;

internal static class ContourSelfCollision
{
    public static void Solve(GelMesh mesh, float thickness)
    {
        if (thickness <= 0 || mesh.Contour.Count < 2) return;
        var topology = BuildTopology(mesh, thickness);
        foreach (var point in topology.Points)
        {
            var pointPosition = point.Binding.Position(mesh.Vertices);
            if (!topology.Grid.TryGetValue(Cell(pointPosition, topology.CellSize), out var candidates)) continue;
            foreach (var segmentIndex in candidates)
            {
                var segment = topology.Segments[segmentIndex];
                if (Adjacent(point, segment, topology.LoopSizes)) continue;

                var a = segment.A.Position(mesh.Vertices);
                var b = segment.B.Position(mesh.Vertices);
                var nearest = Nearest(pointPosition, a, b);
                if (nearest.Distance >= thickness) continue;

                var normal = CollisionNormal(point, segment, pointPosition, a, b, nearest);
                var correction = normal * (thickness - nearest.Distance) * 0.5f;
                Distribute(mesh, point.Binding, correction);
                Distribute(mesh, segment.A, -correction * (1 - nearest.T) * 0.5f);
                Distribute(mesh, segment.B, -correction * nearest.T * 0.5f);
            }
        }
    }

    public static int CountPenetrations(GelMesh mesh, float thickness, bool crossLoopOnly)
    {
        if (thickness <= 0 || mesh.Contour.Count < 2) return 0;
        var topology = BuildTopology(mesh, thickness);
        var count = 0;
        foreach (var point in topology.Points)
        {
            var position = point.Binding.Position(mesh.Vertices);
            foreach (var segment in topology.Segments)
            {
                if (crossLoopOnly && point.Loop == segment.Loop) continue;
                if (Adjacent(point, segment, topology.LoopSizes)) continue;
                if (Nearest(position, segment.A.Position(mesh.Vertices), segment.B.Position(mesh.Vertices)).Distance < thickness) count++;
            }
        }
        return count;
    }

    private static Topology BuildTopology(GelMesh mesh, float thickness)
    {
        var loops = mesh.Contour
            .GroupBy(binding => binding.Loop)
            .ToDictionary(group => group.Key, group => group.OrderBy(binding => binding.Order).ToArray());
        var loopSizes = loops.ToDictionary(pair => pair.Key, pair => pair.Value.Length);
        var points = loops
            .SelectMany(pair => pair.Value.Select(binding => new CollisionPoint(pair.Key, binding.Order, binding)))
            .ToArray();
        var segments = new List<CollisionSegment>();
        foreach (var pair in loops)
        {
            var loop = pair.Value;
            if (loop.Length < 2) continue;
            for (var i = 0; i < loop.Length; i++)
                segments.Add(new CollisionSegment(pair.Key, loop[i], loop[(i + 1) % loop.Length]));
        }

        var minX = mesh.Vertices.Min(vertex => vertex.Position.X);
        var maxX = mesh.Vertices.Max(vertex => vertex.Position.X);
        var minY = mesh.Vertices.Min(vertex => vertex.Position.Y);
        var maxY = mesh.Vertices.Max(vertex => vertex.Position.Y);
        var extent = MathF.Max(maxX - minX, maxY - minY);
        var cellSize = MathF.Max(MathF.Max(thickness * 2, extent / 64), 1e-4f);
        var grid = new Dictionary<(int X, int Y), List<int>>();
        for (var segmentIndex = 0; segmentIndex < segments.Count; segmentIndex++)
        {
            var segment = segments[segmentIndex];
            var a = segment.A.Position(mesh.Vertices);
            var b = segment.B.Position(mesh.Vertices);
            var min = Cell(new Vector2(MathF.Min(a.X, b.X) - thickness, MathF.Min(a.Y, b.Y) - thickness), cellSize);
            var max = Cell(new Vector2(MathF.Max(a.X, b.X) + thickness, MathF.Max(a.Y, b.Y) + thickness), cellSize);
            for (var y = min.Y; y <= max.Y; y++)
            for (var x = min.X; x <= max.X; x++)
            {
                var key = (x, y);
                if (!grid.TryGetValue(key, out var list)) grid[key] = list = [];
                list.Add(segmentIndex);
            }
        }
        return new Topology(points, segments.ToArray(), loopSizes, grid, cellSize);
    }

    private static bool Adjacent(CollisionPoint point, CollisionSegment segment, IReadOnlyDictionary<int, int> loopSizes)
    {
        if (point.Loop != segment.Loop) return false;
        var length = loopSizes[point.Loop];
        if (length <= 5) return true;
        return CircularDistance(point.Order, segment.A.Order, length) <= 2 ||
               CircularDistance(point.Order, segment.B.Order, length) <= 2;
    }

    private static int CircularDistance(int a, int b, int length)
    {
        var distance = Math.Abs(a - b);
        return Math.Min(distance, length - distance);
    }

    private static NearestPoint Nearest(Vector2 point, Vector2 a, Vector2 b)
    {
        var edge = b - a;
        var t = edge.LengthSquared() < 1e-12f ? 0 : Math.Clamp(Vector2.Dot(point - a, edge) / edge.LengthSquared(), 0, 1);
        var nearest = a + edge * t;
        return new NearestPoint(t, Vector2.Distance(point, nearest), nearest);
    }

    private static Vector2 CollisionNormal(CollisionPoint point, CollisionSegment segment, Vector2 pointPosition,
        Vector2 a, Vector2 b, NearestPoint nearest)
    {
        var delta = pointPosition - nearest.Position;
        if (nearest.Distance > 1e-6f) return delta / nearest.Distance;

        var edge = b - a;
        var normal = edge.LengthSquared() > 1e-12f ? Vector2.Normalize(new Vector2(edge.Y, -edge.X)) : Vector2.UnitX;
        var restNearest = Vector2.Lerp(segment.A.Uv, segment.B.Uv, nearest.T);
        var restDelta = point.Binding.Uv - restNearest;
        if (Vector2.Dot(normal, restDelta) < 0) normal = -normal;
        if (restDelta.LengthSquared() < 1e-10f && (point.Loop > segment.Loop || point.Order > segment.A.Order)) normal = -normal;
        return normal;
    }

    private static void Distribute(GelMesh mesh, ContourBinding binding, Vector2 correction)
    {
        mesh.Vertices[binding.A].Position += correction * binding.Weights.X;
        mesh.Vertices[binding.B].Position += correction * binding.Weights.Y;
        mesh.Vertices[binding.C].Position += correction * binding.Weights.Z;
        mesh.Vertices[binding.D].Position += correction * binding.Weights.W;
    }

    private static (int X, int Y) Cell(Vector2 point, float size)
        => ((int)MathF.Floor(point.X / size), (int)MathF.Floor(point.Y / size));

    private readonly record struct CollisionPoint(int Loop, int Order, ContourBinding Binding);
    private readonly record struct CollisionSegment(int Loop, ContourBinding A, ContourBinding B);
    private readonly record struct NearestPoint(float T, float Distance, Vector2 Position);
    private sealed record Topology(CollisionPoint[] Points, CollisionSegment[] Segments, Dictionary<int, int> LoopSizes,
        Dictionary<(int X, int Y), List<int>> Grid, float CellSize);
}
