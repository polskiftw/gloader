using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Gelatin.App;
using Gelatin.Core.Authoring;
using Gelatin.Core.Format;
using Gelatin.Core.Models;

namespace Gelatin.Tests;

public sealed class CorrectionRegressionTests
{
    [Fact]
    public void MiddleEraseSplitsStrokeAndLeavesRealGap()
    {
        var strokes = LongStroke();
        var beforeCenter = InfluenceFields.Rigidity(strokes, new Vector2(0.5f, 0.5f));

        InfluenceFields.Erase(strokes, new Vector2(0.5f, 0.5f), 0.08, 1);

        Assert.Equal(2, strokes.Count);
        Assert.All(strokes, stroke =>
        {
            Assert.Equal(0.04, stroke.Radius, 8);
            Assert.Equal(0.9, stroke.Strength, 8);
        });
        Assert.True(strokes[0].Points.Max(point => point[0]) < 0.5);
        Assert.True(strokes[1].Points.Min(point => point[0]) > 0.5);
        Assert.True(InfluenceFields.Rigidity(strokes, new Vector2(0.5f, 0.5f)) < beforeCenter * 0.05);
        Assert.True(InfluenceFields.Rigidity(strokes, new Vector2(0.25f, 0.5f)) > 0.5);
        Assert.True(InfluenceFields.Rigidity(strokes, new Vector2(0.75f, 0.5f)) > 0.5);
    }

    [Fact]
    public void EntireStrokeCanBeErasedCleanly()
    {
        var strokes = LongStroke();
        InfluenceFields.Erase(strokes, new Vector2(0.5f, 0.5f), 2, 1);
        Assert.Empty(strokes);
    }

    [Fact]
    public void SplitStrokeValidatesAndSurvivesSaveReopen()
    {
        var document = TestAssets.Document();
        document.Config.RigidityStrokes = LongStroke();
        InfluenceFields.Erase(document.Config.RigidityStrokes, new Vector2(0.5f, 0.5f), 0.08, 1);
        GelValidator.Validate(document.Config);

        var reopened = GelFile.Read(new MemoryStream(GelFile.WriteBytes(document)));

        Assert.Equal(2, reopened.Config.RigidityStrokes.Count);
        Assert.Equal(Convert.ToHexString(GelJson.Serialize(document.Config)), Convert.ToHexString(GelJson.Serialize(reopened.Config)));
    }

    [Fact]
    public void DragStyleEraseRemainsOneUndoableCompoundEdit()
    {
        var controller = new DocumentController();
        controller.Document.Config.RigidityStrokes = LongStroke();
        controller.BeginCompoundEdit();
        InfluenceFields.Erase(controller.Document.Config.RigidityStrokes, new Vector2(0.46f, 0.5f), 0.04, 1);
        controller.CompoundMutate(_ => { });
        InfluenceFields.Erase(controller.Document.Config.RigidityStrokes, new Vector2(0.54f, 0.5f), 0.04, 1);
        controller.CompoundMutate(_ => { });
        Assert.True(controller.Document.Config.RigidityStrokes.Count >= 2);

        controller.Undo();

        var restored = Assert.Single(controller.Document.Config.RigidityStrokes);
        Assert.Equal(5, restored.Points.Count);
    }

    [Fact]
    public void NullCoreNameIsFriendlyDomainError()
        => AssertMalformed(root => root["cores"]!.AsArray()[0]!.AsObject()["name"] = null, "name");

    [Fact]
    public void NullRequiredArraysAreFriendlyDomainErrors()
    {
        AssertMalformed(root => root["cores"] = null, "cores");
        AssertMalformed(root => root["rigidityStrokes"] = null, "rigidityStrokes");
    }

    [Fact]
    public void NullRequiredNestedObjectsAreFriendlyDomainErrors()
    {
        AssertMalformed(root => root["image"] = null, "image");
        AssertMalformed(root => root["material"] = null, "material");
        AssertMalformed(root => root["authoring"] = null, "authoring");
    }

    [Fact]
    public void NullArrayItemsAndNestedStrokePointsAreFriendlyDomainErrors()
    {
        AssertMalformed(root => root["cores"]!.AsArray()[0] = null, "cores");
        AssertMalformed(root => root["rigidityStrokes"]!.AsArray()[0] = null, "rigidityStrokes");
        AssertMalformed(root => root["rigidityStrokes"]!.AsArray()[0]!.AsObject()["points"] = null, "rigidity stroke");
    }

    [Fact]
    public void UnknownPropertiesRemainRejectedAsDomainErrors()
        => AssertMalformed(root => root["futureMysteryProperty"] = 42, "not valid JSON");

    [Fact]
    public void Gelatin011ReadsGelatin010CompatibleGel1WithoutMigration()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "assetName": "0.1.0 Fixture",
              "image": { "width": 4, "height": 3, "alphaThreshold": 0.125 },
              "material": {
                "softness": 0.67,
                "damping": 0.23,
                "areaPreservation": 0.88,
                "shapeMemory": 0.61,
                "bendResistance": 0.37,
                "maxStretch": 1.82,
                "selfCollision": true,
                "selfCollisionThickness": 0.009
              },
              "cores": [
                {
                  "id": 1,
                  "name": "Legacy core",
                  "x": 0.47,
                  "y": 0.52,
                  "radiusX": 0.31,
                  "radiusY": 0.22,
                  "mass": 4.2,
                  "coupling": 0.81,
                  "damping": 0.14,
                  "softnessMultiplier": 1.3,
                  "falloff": 0.72
                }
              ],
              "rigidityStrokes": [
                { "radius": 0.045, "strength": 0.87, "points": [[0.2, 0.3], [0.4, 0.39]] }
              ],
              "authoring": { "tool": "Gelatin", "toolVersion": "0.1.0" }
            }
            """;
        var png = TestAssets.Png(4, 3);
        var document = GelFile.Read(new MemoryStream(Container(Encoding.UTF8.GetBytes(json), png)));

        Assert.Equal(1, document.Config.SchemaVersion);
        Assert.Equal("0.1.0 Fixture", document.Config.AssetName);
        Assert.Equal("0.1.0", document.Config.Authoring.ToolVersion);
        Assert.Equal(4, document.Config.Image.Width);
        Assert.Equal(3, document.Config.Image.Height);
        Assert.Equal(0.67, document.Config.Material.Softness, 8);
        Assert.Equal(4.2, document.Config.Cores[0].Mass, 8);
        Assert.Equal(2, document.Config.RigidityStrokes[0].Points.Count);
        Assert.Equal(png, document.PngBytes);

        var rewritten = GelFile.Read(new MemoryStream(GelFile.WriteBytes(document)));
        Assert.Equal(1, rewritten.Config.SchemaVersion);
        Assert.Equal("0.1.0", rewritten.Config.Authoring.ToolVersion);
    }

    private static List<RigidityStroke> LongStroke() =>
    [
        new RigidityStroke
        {
            Radius = 0.04,
            Strength = 0.9,
            Points = [[0.1, 0.5], [0.3, 0.5], [0.5, 0.5], [0.7, 0.5], [0.9, 0.5]]
        }
    ];

    private static void AssertMalformed(Action<JsonObject> mutation, string expectedText)
    {
        var document = TestAssets.Document();
        var root = JsonNode.Parse(GelJson.Serialize(document.Config))!.AsObject();
        mutation(root);
        var bytes = Container(Encoding.UTF8.GetBytes(root.ToJsonString()), document.PngBytes);
        var error = Assert.Throws<GelFormatException>(() => GelFile.Read(new MemoryStream(bytes)));
        Assert.Contains(expectedText, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] Container(byte[] json, byte[] png)
    {
        var bytes = new byte[12 + json.Length + png.Length];
        "GEL1"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)json.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), (uint)png.Length);
        json.CopyTo(bytes, 12);
        png.CopyTo(bytes, 12 + json.Length);
        return bytes;
    }
}
