using System.Numerics;
using Gelatin.Core.Authoring;
using Gelatin.Core.Models;

namespace Gelatin.Core.Physics;

public static class GelMeshBuilder
{
    public static GelMesh Build(GelDocument document, QualitySettings quality)
    {
        var aspect = document.Config.Image.Width / (double)document.Config.Image.Height;
        var (columns, rows) = quality.GridForAspect(aspect);
        var aspectHeight = (float)(document.Config.Image.Height / (double)document.Config.Image.Width);
        var vertices = new List<MeshVertex>(columns * rows);
        for (var y = 0; y < rows; y++)
        for (var x = 0; x < columns; x++)
        {
            var uv = new Vector2(x / (float)(columns - 1), y / (float)(rows - 1));
            var rest = new Vector2(uv.X - 0.5f, (uv.Y - 0.5f) * aspectHeight);
            var (coreInfluence, localSoftness) = CoreField(document.Config.Cores, uv);
            vertices.Add(new MeshVertex
            {
                Rest = rest,
                Previous = rest,
                Position = rest,
                Uv = uv,
                Rigidity = (float)InfluenceFields.Rigidity(document.Config.RigidityStrokes, uv),
                CoreInfluence = coreInfluence,
                LocalSoftnessMultiplier = localSoftness
            });
        }

        var triangles = new List<int>((columns - 1) * (rows - 1) * 6);
        var distances = new List<DistanceConstraint>();
        var areas = new List<AreaConstraint>();
        var structural = new HashSet<(int, int)>();

        void AddDistance(int a, int b, float compliance, bool maxStretch = false)
        {
            if (a > b) (a, b) = (b, a);
            if (!maxStretch && !structural.Add((a, b))) return;
            distances.Add(new DistanceConstraint
            {
                A = a,
                B = b,
                RestLength = Vector2.Distance(vertices[a].Rest, vertices[b].Rest),
                Compliance = compliance,
                MaxStretchOnly = maxStretch
            });
        }

        for (var y = 0; y < rows; y++)
        for (var x = 0; x < columns; x++)
        {
            var i = y * columns + x;
            if (x + 1 < columns) AddDistance(i, i + 1, 1);
            if (y + 1 < rows) AddDistance(i, i + columns, 1);
            if (x + 1 < columns && y + 1 < rows)
            {
                AddDistance(i, i + columns + 1, 2);
                AddDistance(i + 1, i + columns, 2);
                var a = i;
                var b = i + 1;
                var c = i + columns + 1;
                var d = i + columns;
                triangles.AddRange([a, b, c, a, c, d]);
                areas.Add(CreateArea(vertices, a, b, c));
                areas.Add(CreateArea(vertices, a, c, d));
            }
            if (x + 2 < columns) AddDistance(i, i + 2, 3);
            if (y + 2 < rows) AddDistance(i, i + columns * 2, 3);
        }

        foreach (var pair in structural.ToArray()) AddDistance(pair.Item1, pair.Item2, 0, true);

        var contours = AlphaContourExtractor.Extract(document.PngBytes, document.Config.Image.AlphaThreshold, quality.ContourSamples);
        var bindings = new List<ContourBinding>();
        for (var loop = 0; loop < contours.Count; loop++)
        for (var order = 0; order < contours[loop].Points.Count; order++)
            bindings.Add(Bind(contours[loop].Points[order], columns, rows, loop, order));

        var cores = new List<CoreBody>();
        foreach (var definition in document.Config.Cores)
        {
            var center = UvToLocal(new Vector2((float)definition.X, (float)definition.Y), aspectHeight);
            var body = new CoreBody
            {
                Config = CloneCore(definition),
                RestCenter = center,
                Center = center,
                PreviousCenter = center,
                InverseMass = (float)(1 / definition.Mass)
            };
            for (var i = 0; i < vertices.Count; i++)
            {
                var influence = (float)InfluenceFields.CoreInfluence(definition, vertices[i].Uv);
                if (influence > 0.005f) body.Attachments.Add(new CoreAttachment(i, vertices[i].Rest - center, influence));
            }
            cores.Add(body);
        }

        return new GelMesh
        {
            Columns = columns,
            Rows = rows,
            AspectHeight = aspectHeight,
            Vertices = vertices,
            TriangleIndices = triangles,
            Distances = distances,
            Areas = areas,
            Contour = bindings,
            Cores = cores
        };
    }

    private static (float Influence, float LocalSoftness) CoreField(IReadOnlyList<CoreConfig> cores, Vector2 uv)
    {
        if (cores.Count == 0) return (0, 1);
        double remaining = 1;
        double weightedSoftness = 0;
        double totalWeight = 0;
        foreach (var core in cores)
        {
            var influence = InfluenceFields.CoreInfluence(core, uv);
            remaining *= 1 - influence;
            weightedSoftness += influence * core.SoftnessMultiplier;
            totalWeight += influence;
        }

        var combined = Math.Clamp(1 - remaining, 0, 1);
        if (combined <= 1e-9 || totalWeight <= 1e-9) return ((float)combined, 1);
        var averageMultiplier = weightedSoftness / totalWeight;
        var blendedMultiplier = 1 + (averageMultiplier - 1) * combined;
        return ((float)combined, (float)Math.Clamp(blendedMultiplier, 0.1, 4));
    }

    private static AreaConstraint CreateArea(IReadOnlyList<MeshVertex> vertices, int a, int b, int c)
        => new() { A = a, B = b, C = c, RestArea = SignedArea(vertices[a].Rest, vertices[b].Rest, vertices[c].Rest) };

    private static float SignedArea(Vector2 a, Vector2 b, Vector2 c) => Cross(b - a, c - a) * 0.5f;

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

    private static Vector2 UvToLocal(Vector2 uv, float aspectHeight) => new(uv.X - 0.5f, (uv.Y - 0.5f) * aspectHeight);

    private static ContourBinding Bind(Vector2 uv, int columns, int rows, int loop, int order)
    {
        uv = Vector2.Clamp(uv, Vector2.Zero, Vector2.One);
        var gx = uv.X * (columns - 1);
        var gy = uv.Y * (rows - 1);
        var x = Math.Min((int)MathF.Floor(gx), columns - 2);
        var y = Math.Min((int)MathF.Floor(gy), rows - 2);
        var tx = gx - x;
        var ty = gy - y;
        var a = y * columns + x;
        var b = a + 1;
        var d = a + columns;
        var c = d + 1;
        return new ContourBinding(uv, a, b, c, d,
            new Vector4((1 - tx) * (1 - ty), tx * (1 - ty), tx * ty, (1 - tx) * ty), loop, order);
    }

    private static CoreConfig CloneCore(CoreConfig source) => new()
    {
        Id = source.Id, Name = source.Name, X = source.X, Y = source.Y,
        RadiusX = source.RadiusX, RadiusY = source.RadiusY, Mass = source.Mass,
        Coupling = source.Coupling, Damping = source.Damping,
        SoftnessMultiplier = source.SoftnessMultiplier, Falloff = source.Falloff
    };
}
