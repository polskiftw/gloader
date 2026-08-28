using System.Numerics;
using Gelatin.Core.Models;
using Gelatin.Core.Physics;

namespace Gelatin.Tests;

public sealed class PhysicsTests
{
    private static readonly QualitySettings TestQuality = new(10, 240, 8, 1, 48);
    private static readonly Chamber TestChamber = new(-4, -4, 4, 4, 0.5f);

    [Fact]
    public void RestingMeshDoesNotGenerateEnergy()
    {
        var solver = CreateSolver(new MaterialConfig { Damping = 0.2 });
        solver.Reset(Vector2.Zero);
        var rest = solver.Mesh.Vertices.Select(vertex => vertex.Position).ToArray();
        Step(solver, 500);
        Assert.True(solver.IsFinite());
        Assert.True(solver.KineticEnergy() < 1e-5f);
        Assert.True(rest.Zip(solver.Mesh.Vertices).Max(pair => Vector2.Distance(pair.First, pair.Second.Position)) < 0.001f);
    }

    [Fact]
    public void ImpulseMovesBodyAndRemainsFinite()
    {
        var solver = CreateSolver();
        solver.Reset(Vector2.Zero);
        var before = solver.CenterOfMass();
        solver.Smack(Vector2.UnitX);
        Step(solver, 80);
        Assert.True(solver.CenterOfMass().X > before.X + 0.05f);
        Assert.True(solver.IsFinite());
    }

    [Fact]
    public void DampingDissipatesPostImpactEnergy()
    {
        var solver = CreateSolver(new MaterialConfig { Damping = 0.8, ShapeMemory = 0.5, AreaPreservation = 0.7 });
        solver.Reset(Vector2.Zero);
        solver.Smack(new Vector2(1, 0.3f), 1.2f);
        Step(solver, 20);
        var early = solver.KineticEnergy();
        Step(solver, 1200);
        Assert.True(solver.KineticEnergy() < early * 0.15f);
    }

    [Fact]
    public void AreaPreservationRetainsMoreAreaUnderSqueeze()
    {
        var low = CreateSolver(new MaterialConfig { AreaPreservation = 0, ShapeMemory = 0, Damping = 0.3, Softness = 0.9 });
        var high = CreateSolver(new MaterialConfig { AreaPreservation = 1, ShapeMemory = 0, Damping = 0.3, Softness = 0.9 });
        ControlledSqueeze(low, 100);
        ControlledSqueeze(high, 100);
        var lowRatio = low.CurrentArea() / low.RestArea();
        var highRatio = high.CurrentArea() / high.RestArea();
        Assert.True(highRatio > lowRatio + 0.03f, $"low={lowRatio}, high={highRatio}");
    }

    [Fact]
    public void ShapeMemoryReturnsCloserToRest()
    {
        var off = CreateSolver(new MaterialConfig { AreaPreservation = 0.2, ShapeMemory = 0, Damping = 0.45, Softness = 0.8 });
        var on = CreateSolver(new MaterialConfig { AreaPreservation = 0.2, ShapeMemory = 1, Damping = 0.45, Softness = 0.8 });
        Disturb(off);
        Disturb(on);
        Step(off, 500);
        Step(on, 500);
        Assert.True(on.RestDeviation() < off.RestDeviation() * 0.75f, $"off={off.RestDeviation()}, on={on.RestDeviation()}");
    }

    [Fact]
    public void HeavyCoreHasMoreMomentumLagThanLightCore()
    {
        var light = CreateSolver(core: new CoreConfig { Id = 1, Name = "Light", X = 0.5, Y = 0.5, RadiusX = 0.42, RadiusY = 0.42, Mass = 0.2, Coupling = 0.5, Damping = 0.02, SoftnessMultiplier = 1, Falloff = 0.5 });
        var heavy = CreateSolver(core: new CoreConfig { Id = 1, Name = "Heavy", X = 0.5, Y = 0.5, RadiusX = 0.42, RadiusY = 0.42, Mass = 18, Coupling = 0.5, Damping = 0.02, SoftnessMultiplier = 1, Falloff = 0.5 });
        light.Reset(Vector2.Zero);
        heavy.Reset(Vector2.Zero);
        light.Mesh.Cores[0].Velocity = heavy.Mesh.Cores[0].Velocity = Vector2.UnitX * 2;
        Step(light, 40);
        Step(heavy, 40);
        var lightLag = Math.Abs(light.Mesh.Cores[0].Velocity.X - AverageVelocity(light).X);
        var heavyLag = Math.Abs(heavy.Mesh.Cores[0].Velocity.X - AverageVelocity(heavy).X);
        Assert.True(heavyLag > lightLag, $"light={lightLag}, heavy={heavyLag}");
    }

    [Fact]
    public void HigherCouplingTransmitsCoreMotionMoreStrongly()
    {
        var low = CreateSolver(core: Core(0.1));
        var high = CreateSolver(core: Core(1));
        low.Reset(Vector2.Zero);
        high.Reset(Vector2.Zero);
        low.Mesh.Cores[0].Velocity = high.Mesh.Cores[0].Velocity = Vector2.UnitX * 1.5f;
        var lowStart = low.CenterOfMass();
        var highStart = high.CenterOfMass();
        Step(low, 4);
        Step(high, 4);
        var lowMotion = low.CenterOfMass().X - lowStart.X;
        var highMotion = high.CenterOfMass().X - highStart.X;
        Assert.True(highMotion > lowMotion * 1.2f, $"low={lowMotion}, high={highMotion}");
    }

    [Fact]
    public void RigidWeightedVerticesDistortLess()
    {
        var solver = CreateSolver(new MaterialConfig { Softness = 1, ShapeMemory = 0, AreaPreservation = 0.1 });
        solver.Reset(Vector2.Zero);
        var left = solver.Mesh.Vertices.Where(vertex => vertex.Uv.X < 0.35f).ToArray();
        var right = solver.Mesh.Vertices.Where(vertex => vertex.Uv.X > 0.65f).ToArray();
        foreach (var vertex in left) vertex.Rigidity = 1;
        foreach (var vertex in right) vertex.Rigidity = 0;
        foreach (var vertex in left.Concat(right)) vertex.Position += new Vector2(0, (vertex.Uv.Y - 0.5f) * 0.35f);
        Step(solver, 12);
        var structural = solver.Mesh.Distances.Where(item => !item.MaxStretchOnly && item.Compliance == 1).ToArray();
        var rigidError = structural.Where(item => solver.Mesh.Vertices[item.A].Uv.X < 0.35f && solver.Mesh.Vertices[item.B].Uv.X < 0.35f)
            .Average(item => Math.Abs(Vector2.Distance(solver.Mesh.Vertices[item.A].Position, solver.Mesh.Vertices[item.B].Position) / item.RestLength - 1));
        var softError = structural.Where(item => solver.Mesh.Vertices[item.A].Uv.X > 0.65f && solver.Mesh.Vertices[item.B].Uv.X > 0.65f)
            .Average(item => Math.Abs(Vector2.Distance(solver.Mesh.Vertices[item.A].Position, solver.Mesh.Vertices[item.B].Position) / item.RestLength - 1));
        Assert.True(rigidError < softError, $"rigid={rigidError}, soft={softError}");
    }

    [Fact]
    public void MaxStretchBoundsExtremeImpulse()
    {
        var material = new MaterialConfig { Softness = 1, ShapeMemory = 0, AreaPreservation = 0, BendResistance = 0, MaxStretch = 1.12 };
        var solver = CreateSolver(material);
        solver.Reset(Vector2.Zero);
        solver.Mesh.Vertices[0].Velocity = new Vector2(-100, -80);
        Step(solver, 80);
        var structural = solver.Mesh.Distances.Where(item => !item.MaxStretchOnly && item.Compliance == 1).ToArray();
        var maximumRatio = structural.Max(item => Vector2.Distance(solver.Mesh.Vertices[item.A].Position, solver.Mesh.Vertices[item.B].Position) / item.RestLength);
        Assert.True(maximumRatio < 1.2f, $"ratio={maximumRatio}");
    }

    [Fact]
    public void RandomizedLongStressRunStaysFinite()
    {
        var solver = CreateSolver(new MaterialConfig { Softness = 0.95, Damping = 0.08, AreaPreservation = 0.75, ShapeMemory = 0.45, BendResistance = 0.2, MaxStretch = 1.4, SelfCollision = true, SelfCollisionThickness = 0.01 });
        var random = new Random(74219);
        solver.Reset(Vector2.Zero);
        for (var block = 0; block < 80; block++)
        {
            solver.Hammer(new Vector2((float)random.NextDouble(), (float)random.NextDouble()), 0.3f, 5);
            solver.Smack(new Vector2((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f), 2);
            Step(solver, 25);
            Assert.True(solver.IsFinite());
        }
    }

    [Fact]
    public void FixedStepIsIndependentOfRenderChunking()
    {
        var a = CreateSolver();
        var b = CreateSolver();
        a.Reset(Vector2.Zero);
        b.Reset(Vector2.Zero);
        a.Smack(new Vector2(1, 0.2f));
        b.Smack(new Vector2(1, 0.2f));
        var simA = new FixedStepSimulation(a, TestQuality);
        var simB = new FixedStepSimulation(b, TestQuality);
        for (var i = 0; i < 120; i++) simA.Advance(1d / 120);
        for (var i = 0; i < 30; i++) simB.Advance(1d / 30);
        Assert.True(Vector2.Distance(a.CenterOfMass(), b.CenterOfMass()) < 0.002f);
        Assert.True(Math.Abs(a.RestDeviation() - b.RestDeviation()) < 0.003f);
    }

    [Fact]
    public void CoreCanAcquireTemporaryRotationFromOffCenterImpact()
    {
        var definition = Core(0.8);
        definition.X = 0.62;
        definition.Y = 0.43;
        var solver = CreateSolver(core: definition);
        solver.Reset(Vector2.Zero);
        var point = solver.CenterOfMass() + new Vector2(-0.2f, -0.15f);
        solver.Hammer(point, 0.35f, 5);
        Step(solver, 60);
        Assert.True(Math.Abs(solver.Mesh.Cores[0].Angle) > 0.0001f || Math.Abs(solver.Mesh.Cores[0].AngularVelocity) > 0.0001f);
    }

    private static GelSolver CreateSolver(MaterialConfig? material = null, CoreConfig? core = null)
    {
        var document = TestAssets.Document(20, 14);
        document.Config.Material = material ?? new MaterialConfig();
        document.Config.Material.MaxStretch = Math.Clamp(document.Config.Material.MaxStretch, 1.05, 3);
        document.Config.Material.SelfCollisionThickness = Math.Clamp(document.Config.Material.SelfCollisionThickness, 0.0001, 0.1);
        document.Config.Cores = core is null ? [] : [core];
        var mesh = GelMeshBuilder.Build(document, TestQuality);
        return new GelSolver(mesh, document.Config.Material, TestQuality, TestChamber);
    }

    private static CoreConfig Core(double coupling) => new() { Id = 1, Name = "Core", X = 0.5, Y = 0.5, RadiusX = 0.42, RadiusY = 0.42, Mass = 3, Coupling = coupling, Damping = 0.05, SoftnessMultiplier = 1, Falloff = 0.5 };
    private static void Step(GelSolver solver, int steps) { for (var i = 0; i < steps; i++) solver.Step(1f / TestQuality.PhysicsHz); }
    private static Vector2 AverageVelocity(GelSolver solver) => solver.Mesh.Vertices.Aggregate(Vector2.Zero, (sum, vertex) => sum + vertex.Velocity) / solver.Mesh.Vertices.Count;

    private static void ControlledSqueeze(GelSolver solver, int steps)
    {
        solver.Reset(Vector2.Zero);
        var center = solver.CenterOfMass();
        for (var step = 0; step < steps; step++)
        {
            foreach (var vertex in solver.Mesh.Vertices)
            {
                if (vertex.Uv.X < 0.08f) vertex.Position = new Vector2(center.X - 0.11f, vertex.Position.Y);
                else if (vertex.Uv.X > 0.92f) vertex.Position = new Vector2(center.X + 0.11f, vertex.Position.Y);
            }
            solver.Step(1f / TestQuality.PhysicsHz);
        }
    }

    private static void Disturb(GelSolver solver)
    {
        solver.Reset(Vector2.Zero);
        var center = solver.CenterOfMass();
        foreach (var vertex in solver.Mesh.Vertices)
        {
            var offset = vertex.Position - center;
            vertex.Position = center + new Vector2(offset.X + offset.Y * 0.7f, offset.Y * (vertex.Uv.X < 0.5 ? 0.45f : 1.4f));
        }
    }
}
