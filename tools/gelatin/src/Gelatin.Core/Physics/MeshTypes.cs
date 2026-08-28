using System.Numerics;

namespace Gelatin.Core.Physics;

public sealed class MeshVertex
{
    public Vector2 Rest;
    public Vector2 Previous;
    public Vector2 Position;
    public Vector2 Velocity;
    public float InverseMass = 1;
    public float Rigidity;
    public float CoreInfluence;
    public float LocalSoftnessMultiplier = 1;
    public Vector2 Uv;
}

public sealed class DistanceConstraint
{
    public int A;
    public int B;
    public float RestLength;
    public float Compliance;
    public float Lambda;
    public bool MaxStretchOnly;
}

public sealed class AreaConstraint
{
    public int A;
    public int B;
    public int C;
    public float RestArea;
    public float Compliance;
    public float Lambda;
}

public readonly record struct ContourBinding(Vector2 Uv, int A, int B, int C, int D, Vector4 Weights, int Loop, int Order)
{
    public Vector2 Position(IReadOnlyList<MeshVertex> vertices)
        => vertices[A].Position * Weights.X + vertices[B].Position * Weights.Y + vertices[C].Position * Weights.Z + vertices[D].Position * Weights.W;
}

public sealed class CoreBody
{
    public required Gelatin.Core.Models.CoreConfig Config { get; init; }
    public Vector2 RestCenter;
    public Vector2 Center;
    public Vector2 PreviousCenter;
    public Vector2 Velocity;
    public float Angle;
    public float PreviousAngle;
    public float AngularVelocity;
    public float InverseMass;
    public List<CoreAttachment> Attachments { get; } = [];
}

public record struct CoreAttachment(int Vertex, Vector2 RestOffset, float Influence)
{
    public Vector2 Lambda { get; set; }
}

public readonly record struct Chamber(float Left, float Top, float Right, float Bottom, float Restitution = 0.82f, float Friction = 0.015f)
{
    public float Width => Right - Left;
    public float Height => Bottom - Top;
}

public sealed class GelMesh
{
    public required int Columns { get; init; }
    public required int Rows { get; init; }
    public required float AspectHeight { get; init; }
    public required List<MeshVertex> Vertices { get; init; }
    public required List<int> TriangleIndices { get; init; }
    public required List<DistanceConstraint> Distances { get; init; }
    public required List<AreaConstraint> Areas { get; init; }
    public required List<ContourBinding> Contour { get; init; }
    public required List<CoreBody> Cores { get; init; }
}
