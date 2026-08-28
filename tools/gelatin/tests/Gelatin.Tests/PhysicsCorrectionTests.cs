using System.Numerics;
using Gelatin.Core.Models;
using Gelatin.Core.Physics;
using SkiaSharp;

namespace Gelatin.Tests;

public sealed class PhysicsCorrectionTests
{
    private static readonly QualitySettings SmallQuality = new(12, 240, 8, 1, 72);
    private static readonly Chamber SmallChamber = new(-1, -1, 1, 1, 0.5f);

    [Fact]
    public void ResetToRestClearsMotionGrabContactsCoreRotationAndFixedStepBacklog()
    {
        var core = new CoreConfig { Id = 1, Name = "Reset core", X = 0.5, Y = 0.5, RadiusX = 0.35, RadiusY = 0.35, Mass = 3, Coupling = 0.7, Damping = 0.05, SoftnessMultiplier = 1, Falloff = 0.5 };
        var solver = CreateSolver(SmallQuality, new MaterialConfig(), core);
        var simulation = new FixedStepSimulation(solver, SmallQuality);
        solver.Reset(Vector2.Zero);
        var step = 1d / SmallQuality.PhysicsHz;
        Assert.Equal(0, simulation.Advance(step * 0.5));
        var held = solver.Mesh.Vertices[solver.Mesh.Vertices.Count / 2].Position;
        Assert.True(solver.BeginGrab(held, 1));
        solver.UpdateGrab(held + new Vector2(0.3f, -0.2f), 0.01f);
        solver.Smack(new Vector2(1, 0.4f), 2);
        solver.Hammer(solver.CenterOfMass() + new Vector2(-0.2f, -0.1f), 0.3f, 4);
        for (var i = 0; i < 8; i++) solver.Step((float)step);
        Assert.True(solver.KineticEnergy() > 0);

        simulation.ResetToRest();

        Assert.Empty(solver.ContactPoints);
        Assert.All(solver.Mesh.Vertices, vertex =>
        {
            Assert.True(Vector2.Distance(vertex.Position, vertex.Rest) < 1e-6f);
            Assert.True(vertex.Velocity.Length() < 1e-6f);
        });
        Assert.All(solver.Mesh.Cores, body =>
        {
            Assert.True(Vector2.Distance(body.Center, body.RestCenter) < 1e-6f);
            Assert.True(body.Velocity.Length() < 1e-6f);
            Assert.True(Math.Abs(body.Angle) < 1e-6f);
            Assert.True(Math.Abs(body.AngularVelocity) < 1e-6f);
        });
        Assert.Equal(0, simulation.Advance(step * 0.6));
        solver.Step((float)step);
        Assert.True(solver.KineticEnergy() < 1e-6f);
        Assert.True(solver.RestDeviation() < 1e-6f);
    }

    [Fact]
    public void CoreInfluenceChangesStructuralComplianceAndLocalSoftnessIsMonotonic()
    {
        var material = new MaterialConfig { Softness = 1, Damping = 0.2, AreaPreservation = 0, ShapeMemory = 0, BendResistance = 0, MaxStretch = 3 };
        var core = new CoreConfig { Id = 1, Name = "Support", X = 0.25, Y = 0.5, RadiusX = 0.28, RadiusY = 0.48, Mass = 3, Coupling = 0, Damping = 0.05, SoftnessMultiplier = 0.35, Falloff = 0.2 };
        var solver = CreateSolver(SmallQuality, material, core);
        solver.Reset(Vector2.Zero);
        StretchVertically(solver, 1.4f);
        solver.Step(1f / SmallQuality.PhysicsHz);

        var influenced = StructuralError(solver, 0.12f, 0.38f);
        var uninfluenced = StructuralError(solver, 0.68f, 0.94f);
        Assert.True(influenced < uninfluenced * 0.9f, $"influenced={influenced}, uninfluenced={uninfluenced}");
        Assert.Contains(solver.Mesh.Vertices, vertex => vertex.CoreInfluence > 0.8f && vertex.LocalSoftnessMultiplier < 0.6f);

        var firm = LocalSoftnessError(0.2);
        var normal = LocalSoftnessError(1.0);
        var soft = LocalSoftnessError(4.0);
        Assert.True(firm < normal && normal < soft, $"firm={firm}, normal={normal}, soft={soft}");
    }

    [Fact]
    public void PublishedQualityPresetsPreserveMaterialCharacterWithinReasonableTolerance()
    {
        var results = new List<QualityMetric>();
        foreach (var preset in Enum.GetValues<PhysicsQuality>())
        {
            var quality = QualitySettings.For(preset);
            var material = new MaterialConfig { Softness = 0.72, Damping = 0.24, AreaPreservation = 0.82, ShapeMemory = 0.58, BendResistance = 0.3, MaxStretch = 1.7 };
            var solver = CreateSolver(quality, material);
            solver.Reset(Vector2.Zero);
            var start = solver.CenterOfMass();
            solver.Smack(new Vector2(1, 0.2f), 1.1f);
            StepForSeconds(solver, quality, 0.04);
            var displacement = Vector2.Distance(start, solver.CenterOfMass());
            solver.Hammer(solver.CenterOfMass() + new Vector2(-0.18f, -0.08f), 0.25f, 3);
            StepForSeconds(solver, quality, 0.025);
            var deviation = solver.RestDeviation();
            var areaRatio = solver.CurrentArea() / solver.RestArea();

            var coreDefinition = new CoreConfig { Id = 1, Name = "Quality core", X = 0.5, Y = 0.5, RadiusX = 0.38, RadiusY = 0.38, Mass = 4, Coupling = 0.7, Damping = 0.05, SoftnessMultiplier = 1, Falloff = 0.4 };
            var coreSolver = CreateSolver(quality, material, coreDefinition);
            coreSolver.Reset(Vector2.Zero);
            coreSolver.Mesh.Cores[0].Velocity = Vector2.UnitX * 1.2f;
            StepForSeconds(coreSolver, quality, 0.02);
            var couplingMotion = coreSolver.CenterOfMass().X;
            results.Add(new QualityMetric(preset, displacement, deviation, areaRatio, couplingMotion));
        }

        AssertSpread(results.Select(result => result.Displacement), 1.4f, "COM displacement", results);
        AssertSpread(results.Select(result => Math.Max(result.RestDeviation, 1e-6f)), 2.2f, "rest deviation", results);
        Assert.True(results.Max(result => result.AreaRatio) - results.Min(result => result.AreaRatio) < 0.18f, string.Join("; ", results));
        AssertSpread(results.Select(result => Math.Abs(result.CouplingMotion)), 2.2f, "core coupling response", results);
    }

    [Fact]
    public void SlowMotionChangesOnlySimulatedTimeNotMaterialCharacter()
    {
        var states = new List<(double Speed, Vector2 Center, float Deviation, float Area)>();
        foreach (var speed in new[] { 1.0, 0.5, 0.25, 0.1 })
        {
            var solver = CreateSolver(SmallQuality, new MaterialConfig { Softness = 0.75, Damping = 0.3, AreaPreservation = 0.8, ShapeMemory = 0.55 });
            solver.Reset(Vector2.Zero);
            solver.Smack(new Vector2(1, 0.25f), 1.2f);
            var simulation = new FixedStepSimulation(solver, SmallQuality) { Speed = speed };
            var frames = (int)Math.Round(0.1 / ((1d / 120) * speed));
            for (var i = 0; i < frames; i++) simulation.Advance(1d / 120);
            states.Add((speed, solver.CenterOfMass(), solver.RestDeviation(), solver.CurrentArea() / solver.RestArea()));
        }

        var reference = states[0];
        foreach (var state in states.Skip(1))
        {
            Assert.True(Vector2.Distance(reference.Center, state.Center) < 0.004f, $"speed={state.Speed}");
            Assert.True(Math.Abs(reference.Deviation - state.Deviation) < 0.004f, $"speed={state.Speed}");
            Assert.True(Math.Abs(reference.Area - state.Area) < 0.01f, $"speed={state.Speed}");
        }
    }

    [Fact]
    public void SelfCollisionMateriallyReducesFoldPenetrations()
    {
        var off = FoldedSolver(false);
        var on = FoldedSolver(true);
        var initial = on.SelfCollisionPenetrationCount();
        Assert.True(initial > 0, "The deterministic fold must begin penetrated.");
        for (var i = 0; i < 5; i++)
        {
            off.Step(1f / SmallQuality.PhysicsHz);
            on.Step(1f / SmallQuality.PhysicsHz);
        }
        var offCount = off.SelfCollisionPenetrationCount();
        var onCount = on.SelfCollisionPenetrationCount();
        Assert.True(onCount < offCount, $"initial={initial}, off={offCount}, on={onCount}");
        Assert.True(on.IsFinite());
    }

    [Fact]
    public void SelfCollisionAlsoSeparatesDifferentAlphaContourLoops()
    {
        var baseDocument = TestAssets.Document(40, 20);
        var document = new GelDocument
        {
            PngBytes = TestAssets.Png(40, 20, (x, y) =>
                (x is >= 3 and <= 15 || x is >= 24 and <= 36) && y is >= 5 and <= 14 ? SKColors.White : SKColors.Transparent),
            Config = baseDocument.Config
        };
        document.Config.Cores = [];
        document.Config.RigidityStrokes = [];
        document.Config.Material = new MaterialConfig { Softness = 1, Damping = 0.2, AreaPreservation = 0, ShapeMemory = 0, BendResistance = 0, MaxStretch = 3, SelfCollision = true, SelfCollisionThickness = 0.04 };
        var solver = CreateSolver(SmallQuality, document.Config.Material, document: document);
        solver.Reset(Vector2.Zero);
        Assert.True(solver.Mesh.Contour.Select(binding => binding.Loop).Distinct().Count() >= 2);
        var center = solver.CenterOfMass();
        foreach (var vertex in solver.Mesh.Vertices)
        {
            vertex.Position = new Vector2(center.X + (vertex.Position.X - center.X) * 0.12f, vertex.Position.Y);
            vertex.Previous = vertex.Position;
            vertex.Velocity = Vector2.Zero;
        }
        var before = solver.SelfCollisionPenetrationCount(crossLoopOnly: true);
        Assert.True(before > 0, "Compressed islands must enter cross-loop collision thickness.");
        for (var i = 0; i < 5; i++) solver.Step(1f / SmallQuality.PhysicsHz);
        var after = solver.SelfCollisionPenetrationCount(crossLoopOnly: true);
        Assert.True(after < before, $"before={before}, after={after}");
        Assert.True(solver.IsFinite());
    }

    private static GelSolver FoldedSolver(bool selfCollision)
    {
        var material = new MaterialConfig { Softness = 1, Damping = 0.2, AreaPreservation = 0, ShapeMemory = 0, BendResistance = 0, MaxStretch = 3, SelfCollision = selfCollision, SelfCollisionThickness = 0.04 };
        var solver = CreateSolver(SmallQuality, material);
        solver.Reset(Vector2.Zero);
        var center = solver.CenterOfMass();
        var width = solver.Mesh.Vertices.Max(vertex => vertex.Rest.X) - solver.Mesh.Vertices.Min(vertex => vertex.Rest.X);
        foreach (var vertex in solver.Mesh.Vertices)
        {
            var folded = vertex.Uv.X <= 0.5f ? vertex.Uv.X : 1 - vertex.Uv.X;
            vertex.Position = new Vector2(center.X + (folded - 0.25f) * width, vertex.Position.Y + (vertex.Uv.X <= 0.5f ? -0.015f : 0.015f));
            vertex.Previous = vertex.Position;
            vertex.Velocity = Vector2.Zero;
        }
        return solver;
    }

    private static float LocalSoftnessError(double multiplier)
    {
        var material = new MaterialConfig { Softness = 1, Damping = 0.2, AreaPreservation = 0, ShapeMemory = 0, BendResistance = 0, MaxStretch = 3 };
        var core = new CoreConfig { Id = 1, Name = "Local", X = 0.5, Y = 0.5, RadiusX = 0.55, RadiusY = 0.55, Mass = 3, Coupling = 0, Damping = 0.05, SoftnessMultiplier = multiplier, Falloff = 0 };
        var solver = CreateSolver(SmallQuality, material, core);
        solver.Reset(Vector2.Zero);
        StretchVertically(solver, 1.4f);
        solver.Step(1f / SmallQuality.PhysicsHz);
        return StructuralError(solver, 0.38f, 0.62f);
    }

    private static void StretchVertically(GelSolver solver, float scale)
    {
        var center = solver.CenterOfMass();
        foreach (var vertex in solver.Mesh.Vertices)
        {
            vertex.Position = new Vector2(vertex.Position.X, center.Y + (vertex.Position.Y - center.Y) * scale);
            vertex.Previous = vertex.Position;
            vertex.Velocity = Vector2.Zero;
        }
    }

    private static float StructuralError(GelSolver solver, float minX, float maxX)
    {
        var constraints = solver.Mesh.Distances.Where(item =>
        {
            if (item.MaxStretchOnly || item.Compliance != 1) return false;
            var midpoint = (solver.Mesh.Vertices[item.A].Uv.X + solver.Mesh.Vertices[item.B].Uv.X) * 0.5f;
            return midpoint >= minX && midpoint <= maxX;
        }).ToArray();
        Assert.NotEmpty(constraints);
        return constraints.Average(item => Math.Abs(Vector2.Distance(solver.Mesh.Vertices[item.A].Position, solver.Mesh.Vertices[item.B].Position) / item.RestLength - 1));
    }

    private static GelSolver CreateSolver(QualitySettings quality, MaterialConfig? material = null, CoreConfig? core = null, GelDocument? document = null)
    {
        document ??= TestAssets.Document(20, 14);
        document.Config.Material = material ?? new MaterialConfig();
        document.Config.Material.MaxStretch = Math.Clamp(document.Config.Material.MaxStretch, 1.05, 3);
        document.Config.Material.SelfCollisionThickness = Math.Clamp(document.Config.Material.SelfCollisionThickness, 0.0001, 0.1);
        if (core is not null) document.Config.Cores = [core];
        else if (document.Config.Cores.Count > 0) document.Config.Cores = [];
        var mesh = GelMeshBuilder.Build(document, quality);
        return new GelSolver(mesh, document.Config.Material, quality, SmallChamber);
    }

    private static void StepForSeconds(GelSolver solver, QualitySettings quality, double seconds)
    {
        var count = Math.Max(1, (int)Math.Round(seconds * quality.PhysicsHz));
        for (var i = 0; i < count; i++) solver.Step(1f / quality.PhysicsHz);
    }

    private static void AssertSpread(IEnumerable<float> values, float maximumRatio, string name, IReadOnlyList<QualityMetric> results)
    {
        var array = values.Select(Math.Abs).ToArray();
        var min = array.Min();
        var max = array.Max();
        Assert.True(min > 1e-7f && max / min <= maximumRatio, $"{name}: ratio={max / Math.Max(min, 1e-7f)}; {string.Join("; ", results)}");
    }

    private readonly record struct QualityMetric(PhysicsQuality Quality, float Displacement, float RestDeviation, float AreaRatio, float CouplingMotion);
}
