using System.Numerics;
using System.Text.Json.Serialization;
using Gelatin.Core.Runtime;

namespace Gelatin.Core.Models;

public sealed class GelConfig
{
    [JsonRequired]
    public int SchemaVersion { get; set; } = 1;
    [JsonRequired]
    public string AssetName { get; set; } = "Untitled Gel";
    [JsonRequired]
    public ImageConfig Image { get; set; } = new();
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AnimationConfig? Animation { get; set; }
    [JsonRequired]
    public MaterialConfig Material { get; set; } = new();
    public AppearanceConfig Appearance { get; set; } = new();
    public MotionConfig Motion { get; set; } = new();
    public RuntimePhysicsConfig Physics { get; set; } = new();
    public BounceEffectConfig BounceEffect { get; set; } = new();
    [JsonRequired]
    public List<CoreConfig> Cores { get; set; } = [];
    [JsonRequired]
    public List<RigidityStroke> RigidityStrokes { get; set; } = [];
    [JsonRequired]
    public AuthoringConfig Authoring { get; set; } = new();

    public GelConfig DeepClone() => GelJson.Deserialize(GelJson.Serialize(this));
}

public sealed class ImageConfig
{
    [JsonRequired]
    public int Width { get; set; } = 1;
    [JsonRequired]
    public int Height { get; set; } = 1;
    [JsonRequired]
    public double AlphaThreshold { get; set; } = 0.0625;
}

public sealed class AnimationConfig
{
    // Mirrors SKCodec/GIF repetition semantics: -1 = infinite, 0 = play once,
    // N > 0 = repeat N additional times after the first pass.
    [JsonRequired]
    public int RepetitionCount { get; set; } = -1;
    [JsonRequired]
    public List<AnimationFrameConfig> Frames { get; set; } = [];

    public AnimationConfig DeepClone() => new()
    {
        RepetitionCount = RepetitionCount,
        Frames = Frames.Select(frame => frame.DeepClone()).ToList()
    };
}

public sealed class AnimationFrameConfig
{
    [JsonRequired]
    public int X { get; set; }
    [JsonRequired]
    public int Y { get; set; }
    [JsonRequired]
    public int Width { get; set; }
    [JsonRequired]
    public int Height { get; set; }
    // The exact decoded GIF delay in milliseconds. Zero is preserved in the file;
    // playback helpers clamp zero-delay frames to a safe 10 ms display interval.
    [JsonRequired]
    public int DurationMs { get; set; }

    public AnimationFrameConfig DeepClone() => new()
    {
        X = X,
        Y = Y,
        Width = Width,
        Height = Height,
        DurationMs = DurationMs
    };
}

public sealed class MaterialConfig
{
    [JsonRequired]
    public double Softness { get; set; } = 0.55;
    [JsonRequired]
    public double Damping { get; set; } = 0.18;
    [JsonRequired]
    public double AreaPreservation { get; set; } = 0.82;
    [JsonRequired]
    public double ShapeMemory { get; set; } = 0.58;
    [JsonRequired]
    public double BendResistance { get; set; } = 0.25;
    [JsonRequired]
    public double MaxStretch { get; set; } = 1.65;
    [JsonRequired]
    public bool SelfCollision { get; set; }
    [JsonRequired]
    public double SelfCollisionThickness { get; set; } = 0.008;
}

public sealed class AppearanceConfig
{
    public double Opacity { get; set; } = GelRuntimeSemantics.DefaultOpacity;
}

public sealed class MotionConfig
{
    public double SpeedPixelsPerSecond { get; set; } = GelRuntimeSemantics.DefaultSpeedPixelsPerSecond;
}

public sealed class RuntimePhysicsConfig
{
    public double Restitution { get; set; } = GelRuntimeSemantics.DefaultRestitution;
    public double Friction { get; set; } = GelRuntimeSemantics.DefaultFriction;
}

public sealed class BounceEffectConfig
{
    public string Tint { get; set; } = GelRuntimeSemantics.TintOff;
    public double TintIntensity { get; set; } = GelRuntimeSemantics.DefaultTintIntensity;
}

public sealed class CoreConfig
{
    [JsonRequired]
    public int Id { get; set; }
    [JsonRequired]
    public string Name { get; set; } = "Core";
    [JsonRequired]
    public double X { get; set; } = 0.5;
    [JsonRequired]
    public double Y { get; set; } = 0.5;
    [JsonRequired]
    public double RadiusX { get; set; } = 0.2;
    [JsonRequired]
    public double RadiusY { get; set; } = 0.2;
    [JsonRequired]
    public double Mass { get; set; } = 2.0;
    [JsonRequired]
    public double Coupling { get; set; } = 0.72;
    [JsonRequired]
    public double Damping { get; set; } = 0.12;
    [JsonRequired]
    public double SoftnessMultiplier { get; set; } = 1.0;
    [JsonRequired]
    public double Falloff { get; set; } = 0.65;
}

public sealed class RigidityStroke
{
    [JsonRequired]
    public double Radius { get; set; } = 0.04;
    [JsonRequired]
    public double Strength { get; set; } = 0.8;
    [JsonRequired]
    public List<double[]> Points { get; set; } = [];

    [JsonIgnore]
    public IEnumerable<Vector2> Vectors => Points.Select(point => new Vector2((float)point[0], (float)point[1]));
}

public sealed class AuthoringConfig
{
    [JsonRequired]
    public string Tool { get; set; } = "Gelatin";
    [JsonRequired]
    public string ToolVersion { get; set; } = "0.1.5";
}

public sealed class GelDocument
{
    public required GelConfig Config { get; init; }
    // Schema 1: a single processed PNG. Schema 2: the animation atlas PNG.
    public required byte[] PngBytes { get; init; }

    // Session-only recovery pixels for alpha Restore. For animated assets this is
    // an atlas with the same frame layout as PngBytes. GelFile deliberately ignores it.
    public byte[]? RecoveryPngBytes { get; init; }

    public GelDocument DeepClone() => new()
    {
        Config = Config.DeepClone(),
        PngBytes = (byte[])PngBytes.Clone(),
        RecoveryPngBytes = RecoveryPngBytes is null ? null : (byte[])RecoveryPngBytes.Clone()
    };
}

public sealed class GelFormatException : Exception
{
    public GelFormatException(string message) : base(message) { }
    public GelFormatException(string message, Exception innerException) : base(message, innerException) { }
}
