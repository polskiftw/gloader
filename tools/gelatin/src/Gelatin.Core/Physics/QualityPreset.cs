namespace Gelatin.Core.Physics;

public enum PhysicsQuality
{
    Sane,
    High,
    Overkill,
    Claire
}

public readonly record struct QualitySettings(int MeshTarget, int PhysicsHz, int SolverIterations, int SelfCollisionCadence, int ContourSamples)
{
    public static QualitySettings For(PhysicsQuality quality) => quality switch
    {
        PhysicsQuality.Sane => new(24, 240, 8, 2, 96),
        PhysicsQuality.High => new(32, 480, 12, 1, 144),
        PhysicsQuality.Overkill => new(48, 720, 16, 1, 224),
        PhysicsQuality.Claire => new(64, 960, 24, 1, 384),
        _ => throw new ArgumentOutOfRangeException(nameof(quality))
    };

    public (int Columns, int Rows) GridForAspect(double aspect)
    {
        aspect = Math.Clamp(aspect, 1d / 32, 32);
        var targetVertices = MeshTarget * MeshTarget;
        var columns = Math.Max(3, (int)Math.Round(Math.Sqrt(targetVertices * aspect)));
        var rows = Math.Max(3, (int)Math.Round(Math.Sqrt(targetVertices / aspect)));
        return (columns, rows);
    }
}
