using System.Diagnostics;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using Gelatin.Core.Authoring;
using Gelatin.Core.Imaging;
using Gelatin.Core.Physics;
using SkiaSharp;

namespace Gelatin.App.Controls;

public sealed class LabControl : Control
{
    private readonly DocumentController _controller;
    private readonly object _simulationLock = new();
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private FixedStepSimulation? _simulation;
    private SKImage? _texture;
    private long _lastTicks;
    private bool _advancing;
    private bool _dragging;
    private bool _shutdown;
    private Point _lastPointer;
    private long _lastPointerTicks;
    private PhysicsQuality _quality = PhysicsQuality.Sane;
    private CancellationTokenSource? _rebuildCancellation;

    public event Action<string>? SimulationError;

    public bool HammerMode { get; set; }
    public bool ShowMesh { get; set; }
    public bool ShowCores { get; set; }
    public bool ShowHeatmap { get; set; }
    public bool ShowRigidity { get; set; }
    public bool ShowContour { get; set; }
    public bool ShowVelocity { get; set; }

    public PhysicsQuality Quality
    {
        get => _quality;
        set { if (_quality == value) return; _quality = value; Rebuild(); }
    }

    public bool GravityEnabled
    {
        get { lock (_simulationLock) return _simulation?.Solver.GravityEnabled ?? false; }
        set { lock (_simulationLock) if (_simulation is not null) _simulation.Solver.GravityEnabled = value; }
    }

    public bool Paused
    {
        get { lock (_simulationLock) return _simulation?.Paused ?? false; }
        set { lock (_simulationLock) if (_simulation is not null) { _simulation.Paused = value; _simulation.ClearBacklog(); } }
    }

    public double Speed
    {
        get { lock (_simulationLock) return _simulation?.Speed ?? 1; }
        set { lock (_simulationLock) if (_simulation is not null) _simulation.Speed = Math.Clamp(value, 0.1, 1); }
    }

    public LabControl(DocumentController controller)
    {
        _controller = controller;
        Focusable = true;
        ClipToBounds = true;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += OnTick;
        _timer.Start();
        _controller.Changed += (_, _) => Rebuild();
        Rebuild();
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        KeyDown += OnKeyDown;
    }

    public void Reset()
    {
        lock (_simulationLock)
        {
            _simulation?.ResetToRest();
            _dragging = false;
        }
        InvalidateVisual();
    }

    public void Smack(Vector2 direction)
    {
        lock (_simulationLock) _simulation?.Solver.Smack(direction);
    }

    public void SetCleanView()
    {
        ShowMesh = ShowCores = ShowHeatmap = ShowRigidity = ShowContour = ShowVelocity = false;
        InvalidateVisual();
    }

    public void Shutdown()
    {
        _shutdown = true;
        _timer.Stop();
        _rebuildCancellation?.Cancel();
        lock (_simulationLock) _simulation = null;
        _texture?.Dispose();
        _texture = null;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        LabSnapshot? snapshot;
        lock (_simulationLock) snapshot = _simulation is null ? null : LabSnapshot.Capture(_simulation.Solver);
        if (snapshot is null || _texture is null)
        {
            context.FillRectangle(new SolidColorBrush(Color.Parse("#101116")), Bounds);
            return;
        }
        context.Custom(new LabDrawOperation(Bounds, snapshot, _texture, new Diagnostics(ShowMesh, ShowCores, ShowHeatmap, ShowRigidity, ShowContour, ShowVelocity)));
    }

    private async void Rebuild()
    {
        if (_shutdown) return;
        _rebuildCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _rebuildCancellation = cancellation;
        try
        {
            await Task.Delay(60, cancellation.Token);
            var document = _controller.Document.DeepClone();
            var quality = QualitySettings.For(_quality);
            var result = await Task.Run(() =>
            {
                cancellation.Token.ThrowIfCancellationRequested();
                var mesh = GelMeshBuilder.Build(document, quality);
                var solver = new GelSolver(mesh, document.Config.Material, quality, new Chamber(0.035f, 0.055f, 0.965f, 0.945f));
                using var bitmap = ImageProcessor.Decode(document.PngBytes);
                var texture = SKImage.FromBitmap(bitmap);
                try
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    return new LabBuildResult(new FixedStepSimulation(solver, quality), texture);
                }
                catch
                {
                    texture.Dispose();
                    throw;
                }
            }, cancellation.Token);
            if (!ReferenceEquals(_rebuildCancellation, cancellation) || _shutdown)
            {
                result.Texture.Dispose();
                return;
            }
            lock (_simulationLock) _simulation = result.Simulation;
            var previousTexture = _texture;
            _texture = result.Texture;
            previousTexture?.Dispose();
            _lastTicks = _clock.ElapsedTicks;
            InvalidateVisual();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (ReferenceEquals(_rebuildCancellation, cancellation) && !_shutdown)
            {
                lock (_simulationLock) _simulation = null;
                _texture?.Dispose();
                _texture = null;
                SimulationError?.Invoke($"Lab preview could not be rebuilt: {ex.Message}");
                InvalidateVisual();
            }
        }
        finally
        {
            if (ReferenceEquals(_rebuildCancellation, cancellation)) _rebuildCancellation = null;
            cancellation.Dispose();
        }
    }

    private async void OnTick(object? sender, EventArgs e)
    {
        if (_advancing || _shutdown) return;
        var now = _clock.ElapsedTicks;
        var elapsed = _lastTicks == 0 ? 1d / 60 : (now - _lastTicks) / (double)Stopwatch.Frequency;
        _lastTicks = now;
        _advancing = true;
        try
        {
            await Task.Run(() =>
            {
                lock (_simulationLock) _simulation?.Advance(elapsed);
            });
            InvalidateVisual();
        }
        catch (Exception ex)
        {
            lock (_simulationLock)
            {
                if (_simulation is not null) _simulation.Paused = true;
            }
            SimulationError?.Invoke($"Lab simulation paused after an error: {ex.Message}");
        }
        finally { _advancing = false; }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        Focus();
        var point = e.GetPosition(this);
        var world = ToWorld(point);
        lock (_simulationLock)
        {
            if (_simulation is null) return;
            if (HammerMode) _simulation.Solver.Hammer(world);
            else _dragging = _simulation.Solver.BeginGrab(world, 0.18f);
        }
        _lastPointer = point;
        _lastPointerTicks = _clock.ElapsedTicks;
        if (_dragging) e.Pointer.Capture(this);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging) return;
        var point = e.GetPosition(this);
        var now = _clock.ElapsedTicks;
        var elapsed = Math.Max(1d / 1000, (now - _lastPointerTicks) / (double)Stopwatch.Frequency);
        lock (_simulationLock) _simulation?.Solver.UpdateGrab(ToWorld(point), (float)elapsed);
        _lastPointer = point;
        _lastPointerTicks = now;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragging) lock (_simulationLock) _simulation?.Solver.EndGrab();
        _dragging = false;
        e.Pointer.Capture(null);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is TextBox or NumericUpDown) return;
        if (e.Key == Key.Space) { Paused = !Paused; e.Handled = true; }
        else if (e.Key == Key.R) { Reset(); e.Handled = true; }
        else if (e.Key == Key.H) { HammerMode = !HammerMode; e.Handled = true; }
        else if (e.Key == Key.M) { ShowMesh = !ShowMesh; InvalidateVisual(); e.Handled = true; }
    }

    private Vector2 ToWorld(Point point) => new((float)(point.X / Math.Max(1, Bounds.Width)), (float)(point.Y / Math.Max(1, Bounds.Height)));

    private sealed record LabSnapshot(Vector2[] Positions, Vector2[] Uvs, Vector2[] Velocities, float[] Rigidity, float[] CoreInfluence,
        ushort[] Indices, CoreView[] Cores, Vector2[][] Contours, Vector2[] Contacts)
    {
        public static LabSnapshot Capture(GelSolver solver)
        {
            var mesh = solver.Mesh;
            var bodyWidth = mesh.Vertices.Max(vertex => vertex.Rest.X) - mesh.Vertices.Min(vertex => vertex.Rest.X);
            var loops = mesh.Contour.GroupBy(item => item.Loop).Select(loop => loop.OrderBy(item => item.Order).Select(item => item.Position(mesh.Vertices)).ToArray()).ToArray();
            return new LabSnapshot(mesh.Vertices.Select(item => item.Position).ToArray(), mesh.Vertices.Select(item => item.Uv).ToArray(),
                mesh.Vertices.Select(item => item.Velocity).ToArray(), mesh.Vertices.Select(item => item.Rigidity).ToArray(),
                mesh.Vertices.Select(item => (float)InfluenceFields.CombinedCoreInfluence(mesh.Cores.Select(core => core.Config), item.Uv)).ToArray(),
                mesh.TriangleIndices.Select(index => checked((ushort)index)).ToArray(),
                mesh.Cores.Select(core => new CoreView(core.Center, core.Angle,
                    (float)core.Config.RadiusX * bodyWidth,
                    (float)core.Config.RadiusY * bodyWidth * mesh.AspectHeight)).ToArray(),
                loops, solver.ContactPoints.ToArray());
        }
    }

    private sealed record LabBuildResult(FixedStepSimulation Simulation, SKImage Texture);
    private readonly record struct CoreView(Vector2 Center, float Angle, float RadiusX, float RadiusY);
    private readonly record struct Diagnostics(bool Mesh, bool Cores, bool Heatmap, bool Rigidity, bool Contour, bool Velocity);

    private sealed class LabDrawOperation : ICustomDrawOperation
    {
        private readonly LabSnapshot _snapshot;
        private readonly SKImage _texture;
        private readonly Diagnostics _diagnostics;
        public Rect Bounds { get; }

        public LabDrawOperation(Rect bounds, LabSnapshot snapshot, SKImage texture, Diagnostics diagnostics)
        {
            Bounds = bounds;
            _snapshot = snapshot;
            _texture = texture;
            _diagnostics = diagnostics;
        }

        public void Render(ImmediateDrawingContext context)
        {
            var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (feature is null) return;
            using var lease = feature.Lease();
            var canvas = lease.SkCanvas;
            canvas.Save();
            canvas.ClipRect(ToSk(Bounds));
            using var chamberPaint = new SKPaint { Color = new SKColor(16, 17, 22), Style = SKPaintStyle.Fill };
            canvas.DrawRect(ToSk(Bounds), chamberPaint);
            using var wallPaint = new SKPaint { Color = new SKColor(92, 96, 115), Style = SKPaintStyle.Stroke, StrokeWidth = 2 };
            canvas.DrawRect(MapRect(new SKRect(0.035f, 0.055f, 0.965f, 0.945f)), wallPaint);

            var positions = _snapshot.Positions.Select(Map).ToArray();
            var tex = _snapshot.Uvs.Select(uv => new SKPoint(uv.X * _texture.Width, uv.Y * _texture.Height)).ToArray();
            using (var vertices = SKVertices.CreateCopy(SKVertexMode.Triangles, positions, tex, null, _snapshot.Indices))
            using (var shader = _texture.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp))
            using (var paint = new SKPaint { IsAntialias = true, Shader = shader })
                canvas.DrawVertices(vertices, SKBlendMode.Modulate, paint);

            if (_diagnostics.Heatmap) DrawField(canvas, positions, _snapshot.CoreInfluence, new SKColor(255, 118, 30));
            if (_diagnostics.Rigidity) DrawField(canvas, positions, _snapshot.Rigidity, new SKColor(40, 220, 235));
            if (_diagnostics.Mesh) DrawMesh(canvas, positions);
            if (_diagnostics.Cores) DrawCores(canvas);
            if (_diagnostics.Contour) DrawContours(canvas);
            if (_diagnostics.Velocity) DrawVelocities(canvas, positions);
            canvas.Restore();
        }

        private void DrawMesh(SKCanvas canvas, SKPoint[] positions)
        {
            using var paint = new SKPaint { Color = new SKColor(240, 240, 255, 125), StrokeWidth = 1, Style = SKPaintStyle.Stroke, IsAntialias = true };
            for (var i = 0; i < _snapshot.Indices.Length; i += 3)
            {
                using var path = new SKPath();
                path.MoveTo(positions[_snapshot.Indices[i]]);
                path.LineTo(positions[_snapshot.Indices[i + 1]]);
                path.LineTo(positions[_snapshot.Indices[i + 2]]);
                path.Close();
                canvas.DrawPath(path, paint);
            }
        }

        private void DrawField(SKCanvas canvas, SKPoint[] positions, float[] weights, SKColor color)
        {
            var colors = weights.Select(weight => new SKColor(color.Red, color.Green, color.Blue, (byte)(Math.Clamp(weight, 0, 1) * 145))).ToArray();
            using var vertices = SKVertices.CreateCopy(SKVertexMode.Triangles, positions, null, colors, _snapshot.Indices);
            using var paint = new SKPaint { IsAntialias = true };
            canvas.DrawVertices(vertices, SKBlendMode.SrcOver, paint);
        }

        private void DrawCores(SKCanvas canvas)
        {
            using var fill = new SKPaint { Color = new SKColor(160, 115, 255, 45), Style = SKPaintStyle.Fill, IsAntialias = true };
            using var stroke = new SKPaint { Color = new SKColor(205, 185, 255, 220), Style = SKPaintStyle.Stroke, StrokeWidth = 2, IsAntialias = true };
            using var centerPaint = new SKPaint { Color = new SKColor(245, 240, 255), Style = SKPaintStyle.Fill, IsAntialias = true };
            foreach (var core in _snapshot.Cores)
            {
                using var path = new SKPath();
                const int samples = 48;
                for (var sample = 0; sample <= samples; sample++)
                {
                    var radians = sample / (float)samples * MathF.Tau;
                    var local = new Vector2(MathF.Cos(radians) * core.RadiusX, MathF.Sin(radians) * core.RadiusY);
                    var cosine = MathF.Cos(core.Angle);
                    var sine = MathF.Sin(core.Angle);
                    var world = core.Center + new Vector2(cosine * local.X - sine * local.Y, sine * local.X + cosine * local.Y);
                    if (sample == 0) path.MoveTo(Map(world));
                    else path.LineTo(Map(world));
                }
                path.Close();
                canvas.DrawPath(path, fill);
                canvas.DrawPath(path, stroke);
                canvas.DrawCircle(Map(core.Center), 3.5f, centerPaint);
            }
        }

        private void DrawContours(SKCanvas canvas)
        {
            using var paint = new SKPaint { Color = new SKColor(255, 220, 80), Style = SKPaintStyle.Stroke, StrokeWidth = 2, IsAntialias = true };
            foreach (var contour in _snapshot.Contours)
            {
                if (contour.Length < 2) continue;
                using var path = new SKPath();
                path.MoveTo(Map(contour[0]));
                foreach (var point in contour.Skip(1)) path.LineTo(Map(point));
                path.Close();
                canvas.DrawPath(path, paint);
            }
            using var contact = new SKPaint { Color = SKColors.Red, Style = SKPaintStyle.Fill };
            foreach (var point in _snapshot.Contacts) canvas.DrawCircle(Map(point), 3, contact);
        }

        private void DrawVelocities(SKCanvas canvas, SKPoint[] positions)
        {
            using var paint = new SKPaint { Color = new SKColor(92, 255, 142, 190), StrokeWidth = 1.5f, IsAntialias = true };
            var stride = Math.Max(1, _snapshot.Positions.Length / 180);
            for (var i = 0; i < positions.Length; i += stride)
            {
                var end = new SKPoint(positions[i].X + _snapshot.Velocities[i].X * (float)Bounds.Width * 0.025f,
                    positions[i].Y + _snapshot.Velocities[i].Y * (float)Bounds.Height * 0.025f);
                canvas.DrawLine(positions[i], end, paint);
            }
        }

        private SKPoint Map(Vector2 point) => new((float)(Bounds.X + point.X * Bounds.Width), (float)(Bounds.Y + point.Y * Bounds.Height));
        private SKRect MapRect(SKRect rect)
        {
            var topLeft = Map(new Vector2(rect.Left, rect.Top));
            var bottomRight = Map(new Vector2(rect.Right, rect.Bottom));
            return new SKRect(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);
        }
        private static SKRect ToSk(Rect rect) => new((float)rect.X, (float)rect.Y, (float)rect.Right, (float)rect.Bottom);
        public bool HitTest(Point point) => Bounds.Contains(point);
        public bool Equals(ICustomDrawOperation? other) => false;
        public void Dispose() { }
    }
}
