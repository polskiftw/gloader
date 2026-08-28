using System.Numerics;
using System.Text.Json.Serialization;

namespace Gelatin.Core.Models;

public sealed class GelConfig
{
    [JsonRequired]
    public int SchemaVersion { get; set; } = 1;
    [JsonRequired]
    public string AssetName { get; set; } = "Untitled Gel";
    [JsonRequired]
    public ImageConfig Image { get; set; } = new();
    [JsonRequired]
    public MaterialConfig Material { get; set; } = new();
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
    public string ToolVersion { get; set; } = "0.1.0";
}

public sealed class GelDocument
{
    public required GelConfig Config { get; init; }
    public required byte[] PngBytes { get; init; }

    public GelDocument DeepClone() => new() { Config = Config.DeepClone(), PngBytes = (byte[])PngBytes.Clone() };
}

public sealed class GelFormatException : Exception
{
    public GelFormatException(string message) : base(message) { }
    public GelFormatException(string message, Exception innerException) : base(message, innerException) { }
}
