using System.Numerics;
using Gelatin.Core.Models;

namespace Gelatin.Core.Physics;

public sealed class GelSolver
{
    private readonly GelMesh _mesh;
    private readonly MaterialConfig _material;
    private readonly QualitySettings _quality;
    private readonly Vector2[] _contactNormals;
    private readonly bool[] _contacts;
    private int _substep;
    private int? _grabbedVertex;
    private Vector2 _grabTarget;
    private Vector2 _grabVelocity;
    private Vector2 _lastGrabTarget;
    private bool _hasGrabSample;

    public GelMesh Mesh => _mesh;
    public Chamber Chamber { get; private set; }
    public bool GravityEnabled { get; set; }
    public Vector2 Gravity { get; set; } = new(0, 2.8f);
    public IReadOnlyList<Vector2> ContactPoints => _contactPoints;
    private readonly List<Vector2> _contactPoints = [];

    public GelSolver(GelMesh mesh, MaterialConfig material, QualitySettings quality, Chamber chamber)
    {
        _mesh = mesh;
        _material = material;
        _quality = quality;
        Chamber = chamber;
        _contactNormals = new Vector2[mesh.Vertices.Count];
        _contacts = new bool[mesh.Vertices.Count];
        Reset(new Vector2(0.34f, 0.21f));
    }

    public void Reset(Vector2? initialVelocity = null)
    {
        var bodyWidth = Math.Min(Chamber.Width * 0.42f, Chamber.Height / Math.Max(_mesh.AspectHeight, 0.01f) * 0.58f);
        var center = new Vector2((Chamber.Left + Chamber.Right) * 0.5f, (Chamber.Top + Chamber.Bottom) * 0.5f);
        var velocity = initialVelocity ?? Vector2.Zero;
        foreach (var vertex in _mesh.Vertices)
        {
            vertex.Rest = center + vertex.Uv.UvToLocal(_mesh.AspectHeight) * bodyWidth;
            vertex.Position = vertex.Rest;
            vertex.Previous = vertex.Rest - velocity / _quality.PhysicsHz;
            vertex.Velocity = velocity;
        }
        foreach (var constraint in _mesh.Distances)
            constraint.RestLength = Vector2.Distance(_mesh.Vertices[constraint.A].Rest, _mesh.Vertices[constraint.B].Rest);
        foreach (var area in _mesh.Areas)
            area.RestArea = SignedArea(_mesh.Vertices[area.A].Rest, _mesh.Vertices[area.B].Rest, _mesh.Vertices[area.C].Rest);
        foreach (var core in _mesh.Cores)
        {
            var localCenter = new Vector2((float)core.Config.X, (float)core.Config.Y).UvToLocal(_mesh.AspectHeight);
            core.RestCenter = center + localCenter * bodyWidth;
            core.Center = core.RestCenter;
            core.PreviousCenter = core.Center - velocity / _quality.PhysicsHz;
            core.Velocity = velocity;
            core.Angle = core.PreviousAngle = core.AngularVelocity = 0;
            for (var i = 0; i < core.Attachments.Count; i++)
            {
                var attachment = core.Attachments[i];
                core.Attachments[i] = attachment with { RestOffset = (_mesh.Vertices[attachment.Vertex].Rest - core.RestCenter) };
            }
        }
        _grabbedVertex = null;
        _substep = 0;
    }

    public void ResizeChamber(Chamber chamber)
    {
        var old = Chamber;
        if (old.Width <= 0 || old.Height <= 0) { Chamber = chamber; Reset(); return; }
        var scale = Math.Min(chamber.Width / old.Width, chamber.Height / old.Height);
        var oldCenter = new Vector2((old.Left + old.Right) * 0.5f, (old.Top + old.Bottom) * 0.5f);
        var newCenter = new Vector2((chamber.Left + chamber.Right) * 0.5f, (chamber.Top + chamber.Bottom) * 0.5f);
        foreach (var vertex in _mesh.Vertices)
        {
            vertex.Position = newCenter + (vertex.Position - oldCenter) * scale;
            vertex.Previous = newCenter + (vertex.Previous - oldCenter) * scale;
            vertex.Rest = newCenter + (vertex.Rest - oldCenter) * scale;
        }
        foreach (var constraint in _mesh.Distances) constraint.RestLength *= scale;
        foreach (var area in _mesh.Areas) area.RestArea *= scale * scale;
        foreach (var core in _mesh.Cores)
        {
            core.Center = newCenter + (core.Center - oldCenter) * scale;
            core.PreviousCenter = newCenter + (core.PreviousCenter - oldCenter) * scale;
            core.RestCenter = newCenter + (core.RestCenter - oldCenter) * scale;
            for (var i = 0; i < core.Attachments.Count; i++) core.Attachments[i] = core.Attachments[i] with { RestOffset = core.Attachments[i].RestOffset * scale };
        }
        Chamber = chamber;
    }

    public void Step(float dt)
    {
        if (!(dt > 0) || !float.IsFinite(dt)) return;
        _substep++;
        Array.Clear(_contacts);
        Array.Clear(_contactNormals);
        _contactPoints.Clear();

        var damping = MathF.Exp(-(float)_material.Damping * dt * 8);
        foreach (var vertex in _mesh.Vertices)
        {
            vertex.Previous = vertex.Position;
            if (GravityEnabled) vertex.Velocity += Gravity * dt;
            vertex.Velocity *= damping * (1 + (0.985f - 1) * vertex.Rigidity);
            vertex.Position += vertex.Velocity * dt;
        }
        foreach (var core in _mesh.Cores)
        {
            core.PreviousCenter = core.Center;
            core.PreviousAngle = core.Angle;
            if (GravityEnabled) core.Velocity += Gravity * dt;
            var coreDamping = MathF.Exp(-(float)core.Config.Damping * dt * 8);
            core.Velocity *= coreDamping;
            core.AngularVelocity *= coreDamping;
            core.Center += core.Velocity * dt;
            core.Angle += core.AngularVelocity * dt;
        }
        if (_grabbedVertex is int grabbed)
        {
            _mesh.Vertices[grabbed].Position = Vector2.Lerp(_mesh.Vertices[grabbed].Position, _grabTarget, 0.92f);
        }

        ResetLambdas();
        for (var iteration = 0; iteration < _quality.SolverIterations; iteration++)
        {
            SolveDistances(dt);
            SolveAreas(dt);
            SolveShapeMemory(dt);
            SolveCores(dt);
            if (_grabbedVertex is int index) SolveGrab(index);
            SolveWalls();
            if (_material.SelfCollision && _substep % _quality.SelfCollisionCadence == 0 && iteration == _quality.SolverIterations - 1)
                SolveSelfCollision();
        }

        var inverseDt = 1 / dt;
        for (var i = 0; i < _mesh.Vertices.Count; i++)
        {
            var vertex = _mesh.Vertices[i];
            vertex.Velocity = (vertex.Position - vertex.Previous) * inverseDt;
            if (_contacts[i])
            {
                var normal = Vector2.Normalize(_contactNormals[i]);
                var normalSpeed = Vector2.Dot(vertex.Velocity, normal);
                if (normalSpeed < 0) vertex.Velocity -= normal * normalSpeed * (1 + Chamber.Restitution);
                vertex.Velocity -= (vertex.Velocity - normal * Vector2.Dot(vertex.Velocity, normal)) * Chamber.Friction;
                vertex.Previous = vertex.Position - vertex.Velocity * dt;
            }
        }
        foreach (var core in _mesh.Cores)
        {
            core.Velocity = (core.Center - core.PreviousCenter) * inverseDt;
            core.AngularVelocity = (core.Angle - core.PreviousAngle) * inverseDt;
        }
        if (_grabbedVertex is int held) _mesh.Vertices[held].Velocity = _grabVelocity;
        if (!IsFinite()) Reset();
    }

    public bool BeginGrab(Vector2 point, float maximumDistance = float.PositiveInfinity)
    {
        var best = -1;
        var bestDistance = maximumDistance * maximumDistance;
        for (var i = 0; i < _mesh.Vertices.Count; i++)
        {
            var distance = Vector2.DistanceSquared(point, _mesh.Vertices[i].Position);
            if (distance >= bestDistance) continue;
            best = i;
            bestDistance = distance;
        }
        if (best < 0) return false;
        _grabbedVertex = best;
        _grabTarget = _lastGrabTarget = point;
        _grabVelocity = Vector2.Zero;
        _hasGrabSample = false;
        return true;
    }

    public void UpdateGrab(Vector2 target, float elapsed)
    {
        _grabTarget = target;
        if (_hasGrabSample && elapsed > 0) _grabVelocity = Vector2.Lerp(_grabVelocity, (target - _lastGrabTarget) / elapsed, 0.55f);
        _lastGrabTarget = target;
        _hasGrabSample = true;
    }

    public void EndGrab()
    {
        if (_grabbedVertex is int index) _mesh.Vertices[index].Velocity += _grabVelocity * 0.8f;
        _grabbedVertex = null;
    }

    public void Smack(Vector2 direction, float strength = 1.7f)
    {
        if (direction.LengthSquared() < 1e-8f) return;
        direction = Vector2.Normalize(direction);
        var center = CenterOfMass();
        foreach (var vertex in _mesh.Vertices)
        {
            var bias = 0.75f + 0.5f * MathF.Max(0, Vector2.Dot(Vector2.Normalize(vertex.Position - center + new Vector2(1e-4f)), -direction));
            vertex.Velocity += direction * strength * bias;
        }
        foreach (var core in _mesh.Cores) core.Velocity += direction * strength * 0.6f;
    }

    public void Hammer(Vector2 point, float radius = 0.25f, float strength = 3.2f)
    {
        var center = CenterOfMass();
        var inward = center - point;
        if (inward.LengthSquared() < 1e-8f) inward = Vector2.UnitY;
        inward = Vector2.Normalize(inward);
        foreach (var vertex in _mesh.Vertices)
        {
            var distance = Vector2.Distance(vertex.Position, point);
            var influence = Math.Clamp(1 - distance / Math.Max(radius, 1e-4f), 0, 1);
            influence *= influence;
            vertex.Velocity += inward * strength * influence;
        }
        foreach (var core in _mesh.Cores)
        {
            var influence = Math.Clamp(1 - Vector2.Distance(core.Center, point) / Math.Max(radius * 1.6f, 1e-4f), 0, 1);
            var impulse = inward * strength * influence * 0.65f;
            core.Velocity += impulse;
            var lever = point - core.Center;
            core.AngularVelocity += Cross(lever, impulse) * core.InverseMass * 4;
        }
    }

    public Vector2 CenterOfMass()
    {
        var sum = Vector2.Zero;
        foreach (var vertex in _mesh.Vertices) sum += vertex.Position;
        return sum / Math.Max(1, _mesh.Vertices.Count);
    }

    public float CurrentArea()
        => _mesh.Areas.Sum(area => MathF.Abs(SignedArea(_mesh.Vertices[area.A].Position, _mesh.Vertices[area.B].Position, _mesh.Vertices[area.C].Position)));

    public float RestArea() => _mesh.Areas.Sum(area => MathF.Abs(area.RestArea));

    public float RestDeviation()
    {
        var (center, angle) = BestFitTransform();
        var restCenter = RestCenter();
        var total = 0f;
        foreach (var vertex in _mesh.Vertices)
        {
            var target = center + Rotate(vertex.Rest - restCenter, angle);
            total += Vector2.DistanceSquared(vertex.Position, target);
        }
        return MathF.Sqrt(total / Math.Max(1, _mesh.Vertices.Count));
    }

    public float KineticEnergy() => _mesh.Vertices.Sum(vertex => 0.5f * vertex.Velocity.LengthSquared()) +
        _mesh.Cores.Sum(core => 0.5f * (float)core.Config.Mass * core.Velocity.LengthSquared());

    public bool IsFinite() => _mesh.Vertices.All(vertex => Finite(vertex.Position) && Finite(vertex.Velocity)) &&
        _mesh.Cores.All(core => Finite(core.Center) && Finite(core.Velocity) && float.IsFinite(core.Angle));

    private void ResetLambdas()
    {
        foreach (var constraint in _mesh.Distances) constraint.Lambda = 0;
        foreach (var constraint in _mesh.Areas) constraint.Lambda = 0;
    }

    private void SolveDistances(float dt)
    {
        var dtSquared = dt * dt;
        foreach (var constraint in _mesh.Distances)
        {
            var a = _mesh.Vertices[constraint.A];
            var b = _mesh.Vertices[constraint.B];
            var delta = b.Position - a.Position;
            var length = delta.Length();
            if (length < 1e-8f) continue;
            var target = constraint.RestLength;
            float compliance;
            if (constraint.MaxStretchOnly)
            {
                target *= (float)_material.MaxStretch;
                if (length <= target) continue;
                compliance = 1e-10f;
            }
            else
            {
                var localRigidity = (a.Rigidity + b.Rigidity) * 0.5f;
                var familyScale = constraint.Compliance switch { 2 => 1.8f, 3 => 10f / MathF.Max((float)_material.BendResistance * 9 + 1, 1), _ => 1f };
                var soft = 2e-8f + MathF.Pow((float)_material.Softness, 2.5f) * 3.5e-6f;
                compliance = soft * familyScale * (1 + (0.018f - 1) * localRigidity);
            }
            var c = length - target;
            var w = a.InverseMass + b.InverseMass;
            var alpha = compliance / dtSquared;
            var deltaLambda = (-c - alpha * constraint.Lambda) / Math.Max(w + alpha, 1e-8f);
            constraint.Lambda += deltaLambda;
            var correction = delta / length * deltaLambda;
            a.Position -= correction * a.InverseMass;
            b.Position += correction * b.InverseMass;
        }
    }

    private void SolveAreas(float dt)
    {
        var preserve = (float)_material.AreaPreservation;
        var compliance = (1e-9f + MathF.Pow(1 - preserve, 3) * 2e-5f) / (dt * dt);
        foreach (var constraint in _mesh.Areas)
        {
            var a = _mesh.Vertices[constraint.A];
            var b = _mesh.Vertices[constraint.B];
            var c = _mesh.Vertices[constraint.C];
            var current = SignedArea(a.Position, b.Position, c.Position);
            var gradientA = Perpendicular(b.Position - c.Position) * 0.5f;
            var gradientB = Perpendicular(c.Position - a.Position) * 0.5f;
            var gradientC = Perpendicular(a.Position - b.Position) * 0.5f;
            var weighted = a.InverseMass * gradientA.LengthSquared() + b.InverseMass * gradientB.LengthSquared() + c.InverseMass * gradientC.LengthSquared();
            var deltaLambda = (-(current - constraint.RestArea) - compliance * constraint.Lambda) / Math.Max(weighted + compliance, 1e-10f);
            constraint.Lambda += deltaLambda;
            a.Position += gradientA * deltaLambda * a.InverseMass;
            b.Position += gradientB * deltaLambda * b.InverseMass;
            c.Position += gradientC * deltaLambda * c.InverseMass;
        }
    }

    private void SolveShapeMemory(float dt)
    {
        var (center, angle) = BestFitTransform();
        var restCenter = RestCenter();
        var baseStrength = 1 - MathF.Exp(-(float)_material.ShapeMemory * dt * 36);
        foreach (var vertex in _mesh.Vertices)
        {
            var target = center + Rotate(vertex.Rest - restCenter, angle);
            var strength = Math.Clamp(baseStrength * (1 + vertex.Rigidity * 8), 0, 0.82f);
            vertex.Position += (target - vertex.Position) * strength;
        }
    }

    private void SolveCores(float dt)
    {
        foreach (var core in _mesh.Cores)
        {
            foreach (var attachment in core.Attachments)
            {
                var vertex = _mesh.Vertices[attachment.Vertex];
                var offset = Rotate(attachment.RestOffset, core.Angle);
                var target = core.Center + offset;
                var difference = vertex.Position - target;
                var coupling = (float)core.Config.Coupling * attachment.Influence;
                if (coupling <= 1e-5f) continue;
                var compliance = (2e-8f + (float)_material.Softness * (float)core.Config.SoftnessMultiplier * 2e-6f) / Math.Max(coupling * coupling, 0.001f);
                var alpha = compliance / (dt * dt);
                var w = vertex.InverseMass + core.InverseMass + alpha;
                var correction = difference / Math.Max(w, 1e-8f) * coupling;
                vertex.Position -= correction * vertex.InverseMass;
                core.Center += correction * core.InverseMass;
                var torque = Cross(offset, correction) * core.InverseMass / Math.Max(offset.LengthSquared(), 0.001f);
                core.Angle += torque * 0.55f;
            }
        }
    }

    private void SolveGrab(int index)
    {
        var vertex = _mesh.Vertices[index];
        vertex.Position += (_grabTarget - vertex.Position) * 0.86f;
    }

    private void SolveWalls()
    {
        if (_mesh.Contour.Count == 0)
        {
            for (var i = 0; i < _mesh.Vertices.Count; i++) CorrectPoint(i, _mesh.Vertices[i].Position, [i], [1]);
            return;
        }
        foreach (var binding in _mesh.Contour)
        {
            var indices = new[] { binding.A, binding.B, binding.C, binding.D };
            var weights = new[] { binding.Weights.X, binding.Weights.Y, binding.Weights.Z, binding.Weights.W };
            CorrectPoint(-1, binding.Position(_mesh.Vertices), indices, weights);
        }
    }

    private void CorrectPoint(int directIndex, Vector2 point, IReadOnlyList<int> indices, IReadOnlyList<float> weights)
    {
        var correction = Vector2.Zero;
        var normal = Vector2.Zero;
        if (point.X < Chamber.Left) { correction.X += Chamber.Left - point.X; normal += Vector2.UnitX; }
        else if (point.X > Chamber.Right) { correction.X += Chamber.Right - point.X; normal -= Vector2.UnitX; }
        if (point.Y < Chamber.Top) { correction.Y += Chamber.Top - point.Y; normal += Vector2.UnitY; }
        else if (point.Y > Chamber.Bottom) { correction.Y += Chamber.Bottom - point.Y; normal -= Vector2.UnitY; }
        if (correction == Vector2.Zero) return;
        var denominator = 0f;
        for (var i = 0; i < indices.Count; i++) denominator += weights[i] * weights[i] * _mesh.Vertices[indices[i]].InverseMass;
        if (denominator < 1e-8f) return;
        for (var i = 0; i < indices.Count; i++)
        {
            var index = indices[i];
            var vertex = _mesh.Vertices[index];
            vertex.Position += correction * (weights[i] * vertex.InverseMass / denominator);
            _contacts[index] = true;
            _contactNormals[index] += normal * weights[i];
        }
        _contactPoints.Add(point + correction);
    }

    private void SolveSelfCollision()
    {
        var thickness = (float)_material.SelfCollisionThickness * Math.Max(Chamber.Width, Chamber.Height);
        if (thickness <= 0) return;
        var grouped = _mesh.Contour.GroupBy(binding => binding.Loop);
        foreach (var loop in grouped)
        {
            var points = loop.OrderBy(binding => binding.Order).ToArray();
            for (var i = 0; i < points.Length; i++)
            {
                var point = points[i].Position(_mesh.Vertices);
                for (var j = i + 3; j < points.Length - (i == 0 ? 1 : 0); j++)
                {
                    var a = points[j].Position(_mesh.Vertices);
                    var b = points[(j + 1) % points.Length].Position(_mesh.Vertices);
                    var ab = b - a;
                    var t = ab.LengthSquared() < 1e-10f ? 0 : Math.Clamp(Vector2.Dot(point - a, ab) / ab.LengthSquared(), 0, 1);
                    var nearest = a + ab * t;
                    var delta = point - nearest;
                    var distance = delta.Length();
                    if (distance >= thickness || distance < 1e-6f) continue;
                    var correction = delta / distance * (thickness - distance) * 0.35f;
                    Distribute(points[i], correction);
                    Distribute(points[j], -correction * (1 - t) * 0.5f);
                    Distribute(points[(j + 1) % points.Length], -correction * t * 0.5f);
                }
            }
        }
    }

    private void Distribute(ContourBinding binding, Vector2 correction)
    {
        _mesh.Vertices[binding.A].Position += correction * binding.Weights.X;
        _mesh.Vertices[binding.B].Position += correction * binding.Weights.Y;
        _mesh.Vertices[binding.C].Position += correction * binding.Weights.Z;
        _mesh.Vertices[binding.D].Position += correction * binding.Weights.W;
    }

    private (Vector2 Center, float Angle) BestFitTransform()
    {
        var center = CenterOfMass();
        var restCenter = RestCenter();
        double numerator = 0;
        double denominator = 0;
        foreach (var vertex in _mesh.Vertices)
        {
            var p = vertex.Rest - restCenter;
            var q = vertex.Position - center;
            numerator += Cross(p, q);
            denominator += Vector2.Dot(p, q);
        }
        return (center, (float)Math.Atan2(numerator, denominator));
    }

    private Vector2 RestCenter()
    {
        var sum = Vector2.Zero;
        foreach (var vertex in _mesh.Vertices) sum += vertex.Rest;
        return sum / Math.Max(1, _mesh.Vertices.Count);
    }

    private static Vector2 Rotate(Vector2 value, float radians)
    {
        var c = MathF.Cos(radians);
        var s = MathF.Sin(radians);
        return new Vector2(c * value.X - s * value.Y, s * value.X + c * value.Y);
    }

    private static float SignedArea(Vector2 a, Vector2 b, Vector2 c) => Cross(b - a, c - a) * 0.5f;
    private static Vector2 Perpendicular(Vector2 value) => new(value.Y, -value.X);
    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
    private static bool Finite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);
}

internal static class PhysicsVectorExtensions
{
    public static Vector2 UvToLocal(this Vector2 uv, float aspectHeight) => new(uv.X - 0.5f, (uv.Y - 0.5f) * aspectHeight);
}
