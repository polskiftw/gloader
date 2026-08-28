using System.Diagnostics.CodeAnalysis;
using Gelatin.Core.Runtime;

namespace Gelatin.Core.Models;

public static class GelValidator
{
    public const int MaxDimension = 32768;
    public const int MaxCores = 128;
    public const int MaxStrokes = 8192;
    public const int MaxPointsPerStroke = 8192;
    public const int MaxAnimationFrames = 512;
    public const int MaxAnimationFrameDurationMs = 600_000;

    public static void Validate(GelConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (config.SchemaVersion is not (1 or 2)) Fail("schemaVersion must be 1 or 2.");
        if (string.IsNullOrWhiteSpace(config.AssetName) || config.AssetName.Length > 256) Fail("assetName must contain 1 to 256 characters.");
        var image = config.Image ?? throw new GelFormatException("Invalid GEL configuration: image is required.");
        var material = config.Material ?? throw new GelFormatException("Invalid GEL configuration: material is required.");
        var appearance = config.Appearance ?? throw new GelFormatException("Invalid GEL configuration: appearance may not be null.");
        var motion = config.Motion ?? throw new GelFormatException("Invalid GEL configuration: motion may not be null.");
        var physics = config.Physics ?? throw new GelFormatException("Invalid GEL configuration: physics may not be null.");
        var bounceEffect = config.BounceEffect ?? throw new GelFormatException("Invalid GEL configuration: bounceEffect may not be null.");
        var cores = config.Cores ?? throw new GelFormatException("Invalid GEL configuration: cores is required.");
        var strokes = config.RigidityStrokes ?? throw new GelFormatException("Invalid GEL configuration: rigidityStrokes is required.");
        var authoring = config.Authoring ?? throw new GelFormatException("Invalid GEL configuration: authoring is required.");

        Range(image.Width, 1, MaxDimension, "image.width");
        Range(image.Height, 1, MaxDimension, "image.height");
        Range(image.AlphaThreshold, 0, 1, "image.alphaThreshold");
        ValidateAnimation(config, image);

        Range(material.Softness, 0, 1, "material.softness");
        Range(material.Damping, 0, 1, "material.damping");
        Range(material.AreaPreservation, 0, 1, "material.areaPreservation");
        Range(material.ShapeMemory, 0, 1, "material.shapeMemory");
        Range(material.BendResistance, 0, 1, "material.bendResistance");
        Range(material.MaxStretch, 1.05, 3, "material.maxStretch");
        Range(material.SelfCollisionThickness, 0.0001, 0.1, "material.selfCollisionThickness");

        Range(appearance.Opacity, 0, 1, "appearance.opacity");
        Range(motion.SpeedPixelsPerSecond, GelRuntimeSemantics.MinSpeedPixelsPerSecond, GelRuntimeSemantics.MaxSpeedPixelsPerSecond, "motion.speedPixelsPerSecond");
        Range(physics.Restitution, 0, 1, "physics.restitution");
        Range(physics.Friction, 0, 1, "physics.friction");
        if (!string.Equals(bounceEffect.Tint, GelRuntimeSemantics.TintOff, StringComparison.Ordinal) &&
            !string.Equals(bounceEffect.Tint, GelRuntimeSemantics.TintRandomNeon, StringComparison.Ordinal))
            Fail("bounceEffect.tint must be 'off' or 'random_neon'.");
        Range(bounceEffect.TintIntensity, 0, 1, "bounceEffect.tintIntensity");

        if (cores.Count > MaxCores) Fail($"cores may contain at most {MaxCores} entries.");
        var ids = new HashSet<int>();
        foreach (var core in cores)
        {
            if (core is null) Fail("cores may not contain null entries.");
            if (core.Id < 1 || !ids.Add(core.Id)) Fail("Every core id must be a unique positive integer.");
            if (core.Name is null) Fail($"Core {core.Id} name is required.");
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
            if (stroke is null) Fail("rigidityStrokes may not contain null entries.");
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

    private static void ValidateAnimation(GelConfig config, ImageConfig image)
    {
        if (config.SchemaVersion == 1)
        {
            if (config.Animation is not null) Fail("schemaVersion 1 assets may not contain animation metadata.");
            return;
        }

        var animation = config.Animation ?? throw new GelFormatException("Invalid GEL configuration: schemaVersion 2 requires animation metadata.");
        if (animation.RepetitionCount < -1 || animation.RepetitionCount > 1_000_000)
            Fail("animation.repetitionCount must be -1 (infinite) or between 0 and 1000000.");
        if (animation.Frames is null || animation.Frames.Count is < 2 or > MaxAnimationFrames)
            Fail($"animation.frames must contain 2 to {MaxAnimationFrames} frames.");

        foreach (var frame in animation.Frames)
        {
            if (frame is null) Fail("animation.frames may not contain null entries.");
            if (frame.X < 0 || frame.Y < 0) Fail("animation frame coordinates may not be negative.");
            if (frame.Width != image.Width || frame.Height != image.Height)
                Fail("every animation frame must match image.width and image.height.");
            if (frame.DurationMs < 0 || frame.DurationMs > MaxAnimationFrameDurationMs)
                Fail($"animation frame durationMs must be between 0 and {MaxAnimationFrameDurationMs}.");
            if ((long)frame.X + frame.Width > MaxDimension || (long)frame.Y + frame.Height > MaxDimension)
                Fail($"animation frame rectangles must fit inside a {MaxDimension} by {MaxDimension} atlas.");
        }
    }

    private static void Range(double value, double min, double max, string name)
    {
        if (!double.IsFinite(value) || value < min || value > max) Fail($"{name} must be between {min} and {max}.");
    }

    [DoesNotReturn]
    private static void Fail(string message) => throw new GelFormatException($"Invalid GEL configuration: {message}");
}
