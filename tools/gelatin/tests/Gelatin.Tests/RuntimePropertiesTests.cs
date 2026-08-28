using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Gelatin.Core.Format;
using Gelatin.Core.Models;
using Gelatin.Core.Physics;
using Gelatin.Core.Runtime;

namespace Gelatin.Tests;

public sealed class RuntimePropertiesTests
{
    private static readonly QualitySettings TestQuality = new(10, 240, 8, 1, 48);

    [Fact]
    public void LegacyGelJsonReceivesBackwardCompatibleRuntimeDefaults()
    {
        var root = JsonNode.Parse(Encoding.UTF8.GetString(GelJson.Serialize(TestAssets.Document().Config)))!.AsObject();
        root.Remove("appearance");
        root.Remove("motion");
        root.Remove("physics");
        root.Remove("bounceEffect");

        var legacy = GelJson.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString()));
        GelValidator.Validate(legacy);

        Assert.Equal(GelRuntimeSemantics.DefaultOpacity, legacy.Appearance.Opacity);
        Assert.Equal(GelRuntimeSemantics.DefaultSpeedPixelsPerSecond, legacy.Motion.SpeedPixelsPerSecond);
        Assert.Equal(GelRuntimeSemantics.DefaultRestitution, legacy.Physics.Restitution);
        Assert.Equal(GelRuntimeSemantics.DefaultFriction, legacy.Physics.Friction);
        Assert.Equal(GelRuntimeSemantics.TintOff, legacy.BounceEffect.Tint);
        Assert.Equal(GelRuntimeSemantics.DefaultTintIntensity, legacy.BounceEffect.TintIntensity);
    }

    [Fact]
    public void ExplicitRuntimePropertiesRoundTripExactlyThroughGel1()
    {
        var document = TestAssets.Document();
        document.Config.Appearance.Opacity = 0.37;
        document.Config.Motion.SpeedPixelsPerSecond = 777;
        document.Config.Physics.Restitution = 0.63;
        document.Config.Physics.Friction = 0.07;
        document.Config.BounceEffect.Tint = GelRuntimeSemantics.TintRandomNeon;
        document.Config.BounceEffect.TintIntensity = 0.42;

        var reopened = GelFile.Read(new MemoryStream(GelFile.WriteBytes(document)));

        Assert.Equal(0.37, reopened.Config.Appearance.Opacity);
        Assert.Equal(777, reopened.Config.Motion.SpeedPixelsPerSecond);
        Assert.Equal(0.63, reopened.Config.Physics.Restitution);
        Assert.Equal(0.07, reopened.Config.Physics.Friction);
        Assert.Equal(GelRuntimeSemantics.TintRandomNeon, reopened.Config.BounceEffect.Tint);
        Assert.Equal(0.42, reopened.Config.BounceEffect.TintIntensity);
    }

    [Theory]
    [InlineData(-0.001)]
    [InlineData(1.001)]
    public void OpacityOutsideUnitRangeIsRejected(double value)
    {
        var config = TestAssets.Document().Config;
        config.Appearance.Opacity = value;
        Assert.Throws<GelFormatException>(() => GelValidator.Validate(config));
    }

    [Theory]
    [InlineData(-0.001)]
    [InlineData(1.001)]
    public void TintIntensityOutsideUnitRangeIsRejected(double value)
    {
        var config = TestAssets.Document().Config;
        config.BounceEffect.TintIntensity = value;
        Assert.Throws<GelFormatException>(() => GelValidator.Validate(config));
    }

    [Fact]
    public void InvalidOrNonFiniteSpeedIsRejected()
    {
        foreach (var value in new[] { 0d, -1d, double.NaN, double.PositiveInfinity, GelRuntimeSemantics.MaxSpeedPixelsPerSecond + 1 })
        {
            var config = TestAssets.Document().Config;
            config.Motion.SpeedPixelsPerSecond = value;
            Assert.Throws<GelFormatException>(() => GelValidator.Validate(config));
        }
    }

    [Fact]
    public void UnknownTintModeIsRejected()
    {
        var config = TestAssets.Document().Config;
        config.BounceEffect.Tint = "rainbow_script";
        var error = Assert.Throws<GelFormatException>(() => GelValidator.Validate(config));
        Assert.Contains("off", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("random_neon", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OpacityMultipliesAlphaWithoutChangingSourceData()
    {
        const byte source = 200;
        Assert.Equal(source, GelRuntimeSemantics.ApplyOpacity(source, 1));
        Assert.Equal(100, GelRuntimeSemantics.ApplyOpacity(source, 0.5));
        Assert.Equal(0, GelRuntimeSemantics.ApplyOpacity(source, 0));
        Assert.Equal(200, source);
    }

    [Fact]
    public void TintIntensityUsesDocumentedPerChannelLerp()
    {
        var source = new RuntimeRgb(20, 100, 220);
        var tint = new RuntimeRgb(220, 40, 20);
        Assert.Equal(source, GelRuntimeSemantics.Blend(source, tint, 0));
        Assert.Equal(tint, GelRuntimeSemantics.Blend(source, tint, 1));
        Assert.Equal(new RuntimeRgb(70, 85, 170), GelRuntimeSemantics.Blend(source, tint, 0.25));
    }

    [Fact]
    public void BounceTintOffNeverChangesAndRandomNeonChangesOnlyOnBounceCalls()
    {
        var state = new BounceTintState();
        var random = new Random(12345);
        Assert.False(state.OnBounce(GelRuntimeSemantics.TintOff, random));
        Assert.Null(state.CurrentTint);

        Assert.True(state.OnBounce(GelRuntimeSemantics.TintRandomNeon, random));
        var firstIndex = state.CurrentIndex;
        var firstTint = state.CurrentTint;
        Assert.NotNull(firstTint);
        Assert.Contains(firstTint.Value, GelRuntimeSemantics.NeonPalette);

        Assert.Equal(firstIndex, state.CurrentIndex);
        Assert.Equal(firstTint, state.CurrentTint);

        Assert.True(state.OnBounce(GelRuntimeSemantics.TintRandomNeon, random));
        Assert.NotEqual(firstIndex, state.CurrentIndex);
        Assert.Contains(state.CurrentTint!.Value, GelRuntimeSemantics.NeonPalette);
    }

    [Fact]
    public void AuthoredPixelSpeedProducesExpectedDisplacementOverKnownDelta()
    {
        const double speed = 200;
        const double width = 1000;
        const double height = 500;
        const double elapsed = 0.5;
        var worldVelocity = GelRuntimeSemantics.InitialWorldVelocity(speed, width, height);
        var pixelDelta = new Vector2(
            worldVelocity.X * (float)width * (float)elapsed,
            worldVelocity.Y * (float)height * (float)elapsed);

        Assert.InRange(pixelDelta.Length(), 99.999f, 100.001f);
        Assert.InRange(GelRuntimeSemantics.PixelSpeed(worldVelocity, width, height), 199.999, 200.001);
    }

    [Fact]
    public void RuntimeSpeedFixedStepIsIndependentOfRenderChunking()
    {
        var a = CreateRuntimeSimulation(180, 1000, 1000);
        var b = CreateRuntimeSimulation(180, 1000, 1000);
        for (var i = 0; i < 60; i++) a.Advance(1d / 120);
        for (var i = 0; i < 15; i++) b.Advance(1d / 30);

        var deltaPixels = (a.Solver.CenterOfMass() - b.Solver.CenterOfMass()) * 1000;
        Assert.True(deltaPixels.Length() < 0.5f, $"deltaPixels={deltaPixels.Length()}");
    }

    [Fact]
    public void SolverReportsBounceWhenWallReflectionActuallyOccurs()
    {
        var quality = new QualitySettings(10, 240, 8, 1, 48);
        var document = TestAssets.Document(20, 14);
        document.Config.Material = new MaterialConfig { Damping = 0 };
        var mesh = GelMeshBuilder.Build(document, quality);
        var solver = new GelSolver(mesh, document.Config.Material, quality, new Chamber(-0.35f, -0.35f, 0.35f, 0.35f, 1f, 0f));
        solver.Reset(new Vector2(8f, 0f));

        Assert.True(solver.AverageVelocity().X > 0f);
        var reflected = false;
        for (var step = 0; step < 120; step++)
        {
            solver.Step(1f / quality.PhysicsHz);
            if (!solver.BouncedThisStep) continue;
            reflected = true;
            break;
        }

        Assert.True(reflected, "The test body must reach the wall and execute a real inward wall reflection.");
    }

    [Fact]
    public void AnimationClockUsesWallTimeNotSimulationSpeed()
    {
        const double elapsed = 0.125;
        var atSlowSimulation = GelRuntimeSemantics.AdvanceAnimationElapsedMilliseconds(500, elapsed, paused: false);
        var atFullSimulation = GelRuntimeSemantics.AdvanceAnimationElapsedMilliseconds(500, elapsed, paused: false);
        Assert.Equal(625, atSlowSimulation);
        Assert.Equal(atSlowSimulation, atFullSimulation);
        Assert.Equal(500, GelRuntimeSemantics.AdvanceAnimationElapsedMilliseconds(500, elapsed, paused: true));
    }

    private static FixedStepSimulation CreateRuntimeSimulation(double speed, double width, double height)
    {
        var document = TestAssets.Document(18, 12);
        document.Config.Cores = [];
        document.Config.Material.Damping = 0;
        var mesh = GelMeshBuilder.Build(document, TestQuality);
        var solver = new GelSolver(mesh, document.Config.Material, TestQuality, new Chamber(-10, -10, 10, 10, 0.82f, 0.015f));
        solver.Reset(GelRuntimeSemantics.InitialWorldVelocity(speed, width, height));
        return new FixedStepSimulation(solver, TestQuality)
        {
            RuntimeSpeedPixelsPerSecond = speed,
            ViewportWidthPixels = width,
            ViewportHeightPixels = height
        };
    }
}
