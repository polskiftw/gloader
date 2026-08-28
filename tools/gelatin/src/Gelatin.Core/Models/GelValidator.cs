using System.Diagnostics.CodeAnalysis;

namespace Gelatin.Core.Models;

public static class GelValidator
{
    public const int MaxDimension = 32768;
    public const int MaxCores = 128;
    public const int MaxStrokes = 8192;
    public const int MaxPointsPerStroke = 8192;

    public static void Validate(GelConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (config.SchemaVersion != 1) Fail("schemaVersion must be exactly 1.");
        if (string.IsNullOrWhiteSpace(config.AssetName) || config.AssetName.Length > 256) Fail("assetName must contain 1 to 256 characters.");
        var image = config.Image ?? throw new GelFormatException("Invalid GEL configuration: image is required.");
        var material = config.Material ?? throw new GelFormatException("Invalid GEL configuration: material is required.");
        var cores = config.Cores ?? throw new GelFormatException("Invalid GEL configuration: cores is required.");
        var strokes = config.RigidityStrokes ?? throw new GelFormatException("Invalid GEL configuration: rigidityStrokes is required.");
        var authoring = config.Authoring ?? throw new GelFormatException("Invalid GEL configuration: authoring is required.");

        Range(image.Width, 1, MaxDimension, "image.width");
        Range(image.Height, 1, MaxDimension, "image.height");
        Range(image.AlphaThreshold, 0, 1, "image.alphaThreshold");

        Range(material.Softness, 0, 1, "material.softness");
        Range(material.Damping, 0, 1, "material.damping");
        Range(material.AreaPreservation, 0, 1, "material.areaPreservation");
        Range(material.ShapeMemory, 0, 1, "material.shapeMemory");
        Range(material.BendResistance, 0, 1, "material.bendResistance");
        Range(material.MaxStretch, 1.05, 3, "material.maxStretch");
        Range(material.SelfCollisionThickness, 0.0001, 0.1, "material.selfCollisionThickness");

        if (cores.Count > MaxCores) Fail($"cores may contain at most {MaxCores} entries.");
        var ids = new HashSet<int>();
        foreach (var core in cores)
        {
            if (core.Id < 1 || !ids.Add(core.Id)) Fail("Every core id must be a unique positive integer.");
            if (core.Name.Length > 128) Fail($"Core {core.Id} name may not exceed 128 characters.");
            Range(core.X, -1, 2, $"core {core.Id} x");
            Range(core.Y, -1, 2, $"core {core.Id} y");
            Range(core.RadiusX, double.Epsilon, 2, $"core {core.Id} radiusX");
            Range(core.RadiusY, double.Epsilon, 2, $"core {core.Id} radiusY");
            Range(core.Mass, 0.1, 20, $"core {core.Id} mass");
            Range(core.Coupling, 0, 1, $"core {core.Id} coupling");
            Range(core.Damping, 0, 1, $"core {core.Id} damping");
            Range(core.SoftnessMultiplier, 0.1, 4, $"core {core.Id} softnessMultiplier");
            Range(core.Falloff, 0, 1, $"core {core.Id} falloff");
        }

        if (strokes.Count > MaxStrokes) Fail($"rigidityStrokes may contain at most {MaxStrokes} entries.");
        foreach (var stroke in strokes)
        {
            Range(stroke.Radius, double.Epsilon, 1, "rigidity stroke radius");
            Range(stroke.Strength, 0, 1, "rigidity stroke strength");
            if (stroke.Points is null || stroke.Points.Count is < 1 or > MaxPointsPerStroke)
                Fail($"Every rigidity stroke must contain 1 to {MaxPointsPerStroke} points.");
            foreach (var point in stroke.Points)
            {
                if (point is null || point.Length != 2) Fail("Every rigidity point must contain exactly two numbers.");
                Range(point[0], -1, 2, "rigidity point x");
                Range(point[1], -1, 2, "rigidity point y");
            }
        }

        if (authoring.Tool != "Gelatin") Fail("authoring.tool must be Gelatin.");
        if (string.IsNullOrWhiteSpace(authoring.ToolVersion) || authoring.ToolVersion.Length > 64)
            Fail("authoring.toolVersion must contain 1 to 64 characters.");
    }

    private static void Range(double value, double min, double max, string name)
    {
        if (!double.IsFinite(value) || value < min || value > max) Fail($"{name} must be between {min} and {max}.");
    }

    [DoesNotReturn]
    private static void Fail(string message) => throw new GelFormatException($"Invalid GEL configuration: {message}");
}
