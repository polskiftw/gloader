using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Gelatin.Core.Authoring;
using Gelatin.Core.Imaging;
using Gelatin.Core.Models;
using ImagePixelRect = Gelatin.Core.Imaging.PixelRect;

namespace Gelatin.App.Controls;

public enum EditorMode
{
    Select,
    Core,
    Rigid,
    Erase,
    Crop,
    PolygonCutout,
    AlphaErase,
    AlphaRestore,
    Eyedropper
}

public sealed class EditorCanvas : Control
{
    private readonly DocumentController _controller;
    private Bitmap? _bitmap;
    private Bitmap? _preview;
    private GelDocument? _bitmapDocument;
    private int _loadedFrameIndex = -1;
    private readonly DispatcherTimer _animationTimer;
    private readonly Stopwatch _animationClock = Stopwatch.StartNew();
    private CancellationTokenSource? _bitmapCancellation;
    private CancellationTokenSource? _previewCancellation;
    private bool _shutdown;
    private Point _pan;
    private double _zoom = 1;
    private bool _panning;
    private Point _lastPointer;
    private Point _dragStart;
    private Point _cropStartPixel;
    private ImagePixelRect _cropOriginal;
    private CropDrag _cropDrag;
    private CoreDrag _coreDrag;
    private CoreConfig? _activeCore;
    private RigidityStroke? _activeStroke;
    private bool _compoundStarted;
    private EditorMode _mode;

    private readonly List<PixelPoint> _polygon = [];
    private bool _polygonClosed;
    private int? _selectedPolygonVertex;
    private int? _dragPolygonVertex;
    private PixelPoint? _polygonPointer;

    private AlphaBrushSession? _alphaBrush;
    private AnimationAlphaBrushSession? _animatedAlphaBrush;
    private GelDocument? _alphaBrushDocument;
    private PixelPoint? _lastAlphaPoint;
    private PixelPoint? _alphaCursor;
    private bool _alphaPainting;
    private double _alphaBrushSize = 24;

    public EditorMode Mode
    {
        get => _mode;
        set
        {
            if (_mode == value) return;
            if (IsAlphaMode(_mode)) CancelAlphaStroke();
            if (_mode == EditorMode.PolygonCutout && value != EditorMode.PolygonCutout) ClearPolygon();
            _mode = value;
            InvalidateVisual();
        }
    }

    public bool ShowOverlays { get; set; } = true;
    public bool ShowHeatmap { get; set; }
    public bool ShowRigidity { get; set; } = true;
    public double BrushRadius { get; set; } = 0.04;
    public double BrushStrength { get; set; } = 0.8;
    public int? SelectedCoreId { get; set; }
    public ImagePixelRect? CropRect { get; private set; }
    public IReadOnlyList<PixelPoint> PolygonPoints => _polygon;
    public bool PolygonClosed => _polygonClosed;
    public int? SelectedPolygonVertex => _selectedPolygonVertex;
    public double Zoom => _zoom;
    public Point Pan => _pan;
    public int CurrentFrameIndex => AnimatedImageProcessor.IsAnimated(_controller.Document.Config)
        ? AnimatedImageProcessor.FrameIndexAtTime(_controller.Document.Config.Animation, _animationClock.Elapsed.TotalMilliseconds)
        : 0;
    public double AlphaBrushSize
    {
        get => _alphaBrushSize;
        set
        {
            _alphaBrushSize = Math.Clamp(value, 1, 256);
            InvalidateVisual();
        }
    }

    public bool PolygonCanApply => _polygonClosed && PolygonGeometry.Validate(_polygon).IsValid;

    public event Action<int?>? CoreSelected;
    public event Action<ImagePixelRect?>? CropChanged;
    public event Action? PolygonChanged;
    public event Action<int, int>? PixelPicked;
    public event Action<string>? ImageError;
    public event Action<string>? EditorError;

    public EditorCanvas(DocumentController controller)
    {
        _controller = controller;
        ClipToBounds = true;
        Focusable = true;
        _controller.Changed += (_, _) => Reload();
        _animationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _animationTimer.Tick += (_, _) => RefreshAnimationFrame();
        _animationTimer.Start();
        Reload();
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerExited += (_, _) => { _alphaCursor = null; _polygonPointer = null; InvalidateVisual(); };
        PointerWheelChanged += OnWheel;
    }

    public void BeginPolygonCutout()
    {
        CancelAlphaStroke();
        _mode = EditorMode.PolygonCutout;
        ClearPolygon();
        Focus();
        InvalidateVisual();
    }

    public void AddPolygonVertex(PixelPoint point)
    {
        if (_polygonClosed) return;
        var width = _controller.Document.Config.Image.Width;
        var height = _controller.Document.Config.Image.Height;
        _polygon.Add(PolygonGeometry.Clamp(point, width, height));
        _selectedPolygonVertex = _polygon.Count - 1;
        RaisePolygonChanged();
    }

    public bool ClosePolygon()
    {
        if (_polygonClosed) return PolygonCanApply;
        var validation = PolygonGeometry.Validate(_polygon);
        if (!validation.IsValid)
        {
            EditorError?.Invoke(validation.Error!);
            return false;
        }
        _polygonClosed = true;
        _polygonPointer = null;
        _selectedPolygonVertex ??= 0;
        RaisePolygonChanged();
        return true;
    }

    public void SelectPolygonVertex(int? index)
    {
        _selectedPolygonVertex = index is >= 0 && index < _polygon.Count ? index : null;
        RaisePolygonChanged();
    }

    public PixelPoint[] GetPolygonSnapshot() => _polygon.ToArray();

    public bool HandleEditorKey(Key key, KeyModifiers modifiers)
    {
        if (_mode != EditorMode.PolygonCutout) return false;
        var shift = modifiers.HasFlag(KeyModifiers.Shift);
        if ((modifiers & ~KeyModifiers.Shift) != KeyModifiers.None) return false;

        if (!_polygonClosed)
        {
            if (key == Key.Back)
            {
                if (_polygon.Count > 0)
                {
                    _polygon.RemoveAt(_polygon.Count - 1);
                    _selectedPolygonVertex = _polygon.Count == 0 ? null : _polygon.Count - 1;
                    RaisePolygonChanged();
                }
                return true;
            }
            if (key == Key.Escape)
            {
                ClearPolygon();
                return true;
            }
            if (key == Key.Enter)
            {
                ClosePolygon();
                return true;
            }
            return false;
        }

        if (key == Key.Escape)
        {
            ClearPolygon();
            return true;
        }
        if (key is Key.Delete or Key.Back)
        {
            if (_selectedPolygonVertex is int index)
            {
                if (_polygon.Count <= 3)
                    EditorError?.Invoke("A polygon needs at least 3 vertices; this vertex cannot be deleted.");
                else
                {
                    _polygon.RemoveAt(index);
                    _selectedPolygonVertex = Math.Min(index, _polygon.Count - 1);
                    RaisePolygonChanged();
                }
            }
            return true;
        }

        var dx = key == Key.Left ? -1 : key == Key.Right ? 1 : 0;
        var dy = key == Key.Up ? -1 : key == Key.Down ? 1 : 0;
        if ((dx != 0 || dy != 0) && _selectedPolygonVertex is int selected)
        {
            var step = shift ? 10 : 1;
            var width = _controller.Document.Config.Image.Width;
            var height = _controller.Document.Config.Image.Height;
            _polygon[selected] = PolygonGeometry.Nudge(_polygon[selected], dx * step, dy * step, width, height);
            RaisePolygonChanged();
            return true;
        }
        return false;
    }

    public void BeginAlphaBrush(AlphaBrushMode mode)
    {
        ClearPolygon();
        CancelAlphaStroke();
        _mode = mode == AlphaBrushMode.Erase ? EditorMode.AlphaErase : EditorMode.AlphaRestore;
        Focus();
        InvalidateVisual();
    }

    public void CancelTransientInteraction()
    {
        CancelAlphaStroke();
        ClearPolygon();
        _cropDrag = CropDrag.None;
        _coreDrag = CoreDrag.None;
        _activeCore = null;
        _activeStroke = null;
        _compoundStarted = false;
        _panning = false;
    }

    public async void SetPreview(byte[]? png)
    {
        _previewCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _previewCancellation = cancellation;
        if (png is null)
        {
            _preview?.Dispose();
            _preview = null;
            _previewCancellation = null;
            cancellation.Dispose();
            InvalidateVisual();
            return;
        }
        try
        {
            var bitmap = await Task.Run(() => DecodeBitmap(png, cancellation.Token), cancellation.Token);
            if (!ReferenceEquals(_previewCancellation, cancellation) || _shutdown)
            {
                bitmap.Dispose();
                return;
            }
            var previous = _preview;
            _preview = bitmap;
            previous?.Dispose();
            InvalidateVisual();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (ReferenceEquals(_previewCancellation, cancellation)) ImageError?.Invoke($"Image preview could not be decoded: {ex.Message}");
        }
        finally
        {
            if (ReferenceEquals(_previewCancellation, cancellation)) _previewCancellation = null;
            cancellation.Dispose();
        }
    }

    public void Shutdown()
    {
        _shutdown = true;
        _animationTimer.Stop();
        CancelAlphaStroke();
        _bitmapCancellation?.Cancel();
        _previewCancellation?.Cancel();
        _bitmap?.Dispose();
        _preview?.Dispose();
        _bitmap = _preview = null;
    }

    public void CancelCrop()
    {
        CropRect = null;
        CropChanged?.Invoke(null);
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(new SolidColorBrush(Color.Parse("#15151A")), Bounds);
        var imageRect = ImageRect();
        DrawCheckerboard(context, imageRect);
        var image = _preview ?? _bitmap;
        if (image is not null) context.DrawImage(image, imageRect);
        if (ShowOverlays)
        {
            if (ShowHeatmap) DrawHeatmap(context, imageRect);
            if (ShowRigidity) DrawRigidity(context, imageRect);
            DrawCores(context, imageRect);
        }
        DrawCrop(context, imageRect);
        DrawPolygon(context, imageRect);
        DrawAlphaCursor(context, imageRect);
    }

    private void RefreshAnimationFrame()
    {
        if (_shutdown || _preview is not null || !AnimatedImageProcessor.IsAnimated(_controller.Document.Config)) return;
        if (CurrentFrameIndex != _loadedFrameIndex) Reload();
    }

    private async void Reload()
    {
        var document = _controller.Document;
        var changedDocument = !ReferenceEquals(_bitmapDocument, document);
        if (changedDocument)
        {
            _bitmapDocument = document;
            _loadedFrameIndex = -1;
            _animationClock.Restart();
        }
        var frameIndex = CurrentFrameIndex;
        if (!changedDocument && frameIndex == _loadedFrameIndex)
        {
            InvalidateVisual();
            return;
        }
        var png = AnimatedImageProcessor.IsAnimated(document.Config)
            ? AnimatedImageProcessor.GetFramePng(document, frameIndex)
            : document.PngBytes;
        _loadedFrameIndex = frameIndex;
        _bitmapCancellation?.Cancel();
        _previewCancellation?.Cancel();
        _previewCancellation = null;
        var cancellation = new CancellationTokenSource();
        _bitmapCancellation = cancellation;
        _preview?.Dispose();
        _preview = null;
        try
        {
            var bitmap = await Task.Run(() => DecodeBitmap(png, cancellation.Token), cancellation.Token);
            if (!ReferenceEquals(_bitmapCancellation, cancellation) || _shutdown)
            {
                bitmap.Dispose();
                return;
            }
            var previous = _bitmap;
            _bitmap = bitmap;
            previous?.Dispose();
            InvalidateVisual();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (ReferenceEquals(_bitmapCancellation, cancellation)) ImageError?.Invoke($"The processed image could not be displayed: {ex.Message}");
        }
        finally
        {
            if (ReferenceEquals(_bitmapCancellation, cancellation)) _bitmapCancellation = null;
            cancellation.Dispose();
        }
    }

    private Rect ImageRect()
    {
        var size = _bitmap?.Size ?? new Size(Math.Max(1, _controller.Document.Config.Image.Width), Math.Max(1, _controller.Document.Config.Image.Height));
        var scale = Math.Min(Math.Max(1, Bounds.Width - 40) / size.Width, Math.Max(1, Bounds.Height - 40) / size.Height) * _zoom;
        var width = size.Width * scale;
        var height = size.Height * scale;
        return new Rect((Bounds.Width - width) / 2 + _pan.X, (Bounds.Height - height) / 2 + _pan.Y, width, height);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        Focus();
        var point = e.GetPosition(this);
        var properties = e.GetCurrentPoint(this).Properties;
        if (properties.IsMiddleButtonPressed || properties.IsRightButtonPressed)
        {
            _panning = true;
            _lastPointer = point;
            e.Pointer.Capture(this);
            return;
        }

        if (!properties.IsLeftButtonPressed) return;
        if (_mode == EditorMode.PolygonCutout)
        {
            HandlePolygonPress(e, point);
            return;
        }
        if (IsAlphaMode(_mode))
        {
            BeginAlphaStroke(e, point);
            return;
        }

        var uv = CanvasToUv(point);
        if (uv is null) return;
        _dragStart = point;
        _compoundStarted = false;
        switch (_mode)
        {
            case EditorMode.Eyedropper:
                PixelPicked?.Invoke((int)Math.Clamp(uv.Value.X * _controller.Document.Config.Image.Width, 0, _controller.Document.Config.Image.Width - 1),
                    (int)Math.Clamp(uv.Value.Y * _controller.Document.Config.Image.Height, 0, _controller.Document.Config.Image.Height - 1));
                break;
            case EditorMode.Crop:
                var imageWidth = _controller.Document.Config.Image.Width;
                var imageHeight = _controller.Document.Config.Image.Height;
                _cropDrag = HitCrop(point);
                _cropStartPixel = new Point(uv.Value.X * imageWidth, uv.Value.Y * imageHeight);
                _cropOriginal = CropRect ?? new ImagePixelRect(
                    Math.Clamp((int)_cropStartPixel.X, 0, imageWidth - 1),
                    Math.Clamp((int)_cropStartPixel.Y, 0, imageHeight - 1), 1, 1);
                if (_cropDrag == CropDrag.Draw)
                {
                    CropRect = _cropOriginal;
                    CropChanged?.Invoke(CropRect);
                }
                e.Pointer.Capture(this);
                break;
            case EditorMode.Core:
                if (_controller.Document.Config.Cores.Count >= GelValidator.MaxCores)
                {
                    EditorError?.Invoke($"A GEL asset may contain at most {GelValidator.MaxCores} cores.");
                    break;
                }
                BeginCompoundEdit();
                var usedIds = _controller.Document.Config.Cores.Select(core => core.Id).ToHashSet();
                var nextId = 1;
                while (usedIds.Contains(nextId)) nextId++;
                _activeCore = new CoreConfig { Id = nextId, Name = $"Core {nextId}", X = uv.Value.X, Y = uv.Value.Y, RadiusX = 0.001, RadiusY = 0.001 };
                _controller.Document.Config.Cores.Add(_activeCore);
                _controller.CompoundMutate(_ => { });
                SelectedCoreId = nextId;
                CoreSelected?.Invoke(nextId);
                _coreDrag = CoreDrag.Create;
                e.Pointer.Capture(this);
                break;
            case EditorMode.Rigid:
                if (_controller.Document.Config.RigidityStrokes.Count >= GelValidator.MaxStrokes)
                {
                    EditorError?.Invoke($"A GEL asset may contain at most {GelValidator.MaxStrokes} rigidity strokes.");
                    break;
                }
                BeginCompoundEdit();
                _activeStroke = new RigidityStroke { Radius = BrushRadius, Strength = BrushStrength, Points = [[uv.Value.X, uv.Value.Y]] };
                _controller.Document.Config.RigidityStrokes.Add(_activeStroke);
                _controller.CompoundMutate(_ => { });
                e.Pointer.Capture(this);
                break;
            case EditorMode.Erase:
                BeginCompoundEdit();
                InfluenceFields.Erase(_controller.Document.Config.RigidityStrokes, new System.Numerics.Vector2((float)uv.Value.X, (float)uv.Value.Y), BrushRadius, 1);
                _controller.CompoundMutate(_ => { });
                e.Pointer.Capture(this);
                break;
            default:
                PickOrBeginCoreDrag(point, uv.Value);
                e.Pointer.Capture(this);
                break;
        }
        InvalidateVisual();
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var point = e.GetPosition(this);
        if (_panning)
        {
            _pan += point - _lastPointer;
            _lastPointer = point;
            InvalidateVisual();
            return;
        }

        if (_mode == EditorMode.PolygonCutout)
        {
            _polygonPointer = CanvasToPixelClamped(point);
            if (_dragPolygonVertex is int vertex && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                _polygon[vertex] = _polygonPointer.Value;
                _selectedPolygonVertex = vertex;
                RaisePolygonChanged();
            }
            else InvalidateVisual();
            return;
        }

        if (IsAlphaMode(_mode))
        {
            _alphaCursor = CanvasToPixel(point);
            if (_alphaPainting && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && _alphaCursor is PixelPoint current)
            {
                try
                {
                    if (_animatedAlphaBrush is not null)
                    {
                        if (_lastAlphaPoint is PixelPoint last) _animatedAlphaBrush.ApplySegment(last, current);
                        else _animatedAlphaBrush.ApplyPoint(current);
                        SetPreview(_animatedAlphaBrush.EncodePreview(CurrentFrameIndex));
                    }
                    else
                    {
                        if (_lastAlphaPoint is PixelPoint last) _alphaBrush!.ApplySegment(last, current);
                        else _alphaBrush!.ApplyPoint(current);
                        SetPreview(_alphaBrush!.Encode());
                    }
                    _lastAlphaPoint = current;
                }
                catch (Exception ex)
                {
                    EditorError?.Invoke($"Alpha brush failed: {ex.Message}");
                    CancelAlphaStroke();
                }
            }
            InvalidateVisual();
            return;
        }

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var uv = _mode == EditorMode.Crop ? CanvasToUvClamped(point) : CanvasToUv(point);
        if (uv is null) return;
        if (_mode == EditorMode.Crop)
        {
            UpdateCrop(uv.Value);
            CropChanged?.Invoke(CropRect);
        }
        else if (_activeCore is not null)
        {
            BeginCompoundEdit();
            UpdateCoreDrag(uv.Value);
            _controller.CompoundMutate(_ => { });
        }
        else if (_activeStroke is not null)
        {
            var previous = _activeStroke.Points[^1];
            if (_activeStroke.Points.Count < GelValidator.MaxPointsPerStroke &&
                Math.Sqrt(Math.Pow(previous[0] - uv.Value.X, 2) + Math.Pow(previous[1] - uv.Value.Y, 2)) > BrushRadius * 0.12)
                _activeStroke.Points.Add([uv.Value.X, uv.Value.Y]);
            _controller.CompoundMutate(_ => { });
        }
        else if (_mode == EditorMode.Erase)
        {
            InfluenceFields.Erase(_controller.Document.Config.RigidityStrokes, new System.Numerics.Vector2((float)uv.Value.X, (float)uv.Value.Y), BrushRadius, 1);
            _controller.CompoundMutate(_ => { });
        }
        InvalidateVisual();
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_alphaPainting)
        {
            CommitAlphaStroke();
            e.Pointer.Capture(null);
            return;
        }
        _panning = false;
        _dragPolygonVertex = null;
        _activeCore = null;
        _activeStroke = null;
        _compoundStarted = false;
        _cropDrag = CropDrag.None;
        _coreDrag = CoreDrag.None;
        e.Pointer.Capture(null);
    }

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        var before = e.GetPosition(this);
        var old = _zoom;
        _zoom = Math.Clamp(_zoom * Math.Pow(1.12, e.Delta.Y), 0.08, 32);
        var ratio = _zoom / old;
        _pan = new Point(before.X - Bounds.Width / 2 - (before.X - Bounds.Width / 2 - _pan.X) * ratio,
            before.Y - Bounds.Height / 2 - (before.Y - Bounds.Height / 2 - _pan.Y) * ratio);
        InvalidateVisual();
    }

    private void HandlePolygonPress(PointerPressedEventArgs e, Point canvas)
    {
        var pixel = CanvasToPixel(canvas);
        if (pixel is null) return;
        _polygonPointer = pixel;

        if (!_polygonClosed)
        {
            if (_polygon.Count >= 3 && HitPolygonVertex(canvas) == 0)
            {
                ClosePolygon();
                return;
            }
            if (e.ClickCount >= 2 && _polygon.Count >= 3)
            {
                ClosePolygon();
                return;
            }
            AddPolygonVertex(pixel.Value);
            return;
        }

        var vertex = HitPolygonVertex(canvas);
        if (vertex is int hit)
        {
            _selectedPolygonVertex = hit;
            _dragPolygonVertex = hit;
            e.Pointer.Capture(this);
            RaisePolygonChanged();
            return;
        }

        var edge = HitPolygonEdge(canvas);
        if (edge is int edgeIndex)
        {
            var inserted = PolygonGeometry.InsertOnEdge(_polygon, edgeIndex, pixel.Value);
            _polygon.Clear();
            _polygon.AddRange(inserted);
            _selectedPolygonVertex = edgeIndex + 1;
            _dragPolygonVertex = edgeIndex + 1;
            e.Pointer.Capture(this);
            RaisePolygonChanged();
            return;
        }

        _selectedPolygonVertex = null;
        RaisePolygonChanged();
    }

    private void BeginAlphaStroke(PointerPressedEventArgs e, Point canvas)
    {
        var pixel = CanvasToPixel(canvas);
        if (pixel is null) return;
        try
        {
            CancelAlphaStroke();
            var mode = _mode == EditorMode.AlphaErase ? AlphaBrushMode.Erase : AlphaBrushMode.Restore;
            _alphaBrushDocument = _controller.Document;
            if (AnimatedImageProcessor.IsAnimated(_controller.Document.Config))
            {
                _animatedAlphaBrush = new AnimationAlphaBrushSession(_controller.Document.PngBytes, _controller.RecoveryPngBytes, _controller.Document.Config, mode, _alphaBrushSize);
                _animatedAlphaBrush.ApplyPoint(pixel.Value);
                SetPreview(_animatedAlphaBrush.EncodePreview(CurrentFrameIndex));
            }
            else
            {
                _alphaBrush = new AlphaBrushSession(_controller.Document.PngBytes, _controller.RecoveryPngBytes, mode, _alphaBrushSize);
                _alphaBrush.ApplyPoint(pixel.Value);
                SetPreview(_alphaBrush.Encode());
            }
            _lastAlphaPoint = pixel;
            _alphaCursor = pixel;
            _alphaPainting = true;
            e.Pointer.Capture(this);
        }
        catch (Exception ex)
        {
            EditorError?.Invoke($"Alpha brush could not start: {ex.Message}");
            CancelAlphaStroke();
        }
    }

    private void CommitAlphaStroke()
    {
        var brush = _alphaBrush;
        var animatedBrush = _animatedAlphaBrush;
        var sourceDocument = _alphaBrushDocument;
        _alphaBrush = null;
        _animatedAlphaBrush = null;
        _alphaBrushDocument = null;
        _alphaPainting = false;
        _lastAlphaPoint = null;
        if (brush is null && animatedBrush is null) return;
        try
        {
            SetPreview(null);
            if (!ReferenceEquals(sourceDocument, _controller.Document))
            {
                EditorError?.Invoke("The image changed while painting; the unfinished alpha stroke was discarded.");
                return;
            }
            if (animatedBrush is not null)
            {
                var result = animatedBrush.Encode();
                var recovery = _controller.GetRecoveryStorage();
                _controller.CommitStorage(result, recoveryStorage: recovery);
            }
            else
            {
                _controller.CommitImage(brush!.Encode());
            }
        }
        catch (Exception ex)
        {
            EditorError?.Invoke($"Alpha brush could not be committed: {ex.Message}");
        }
        finally
        {
            brush?.Dispose();
            animatedBrush?.Dispose();
        }
    }

    private void CancelAlphaStroke()
    {
        _alphaPainting = false;
        _lastAlphaPoint = null;
        _alphaBrushDocument = null;
        _alphaBrush?.Dispose();
        _animatedAlphaBrush?.Dispose();
        _alphaBrush = null;
        _animatedAlphaBrush = null;
        if (_preview is not null) SetPreview(null);
    }

    private void ClearPolygon()
    {
        _polygon.Clear();
        _polygonClosed = false;
        _selectedPolygonVertex = null;
        _dragPolygonVertex = null;
        _polygonPointer = null;
        RaisePolygonChanged();
    }

    private void RaisePolygonChanged()
    {
        PolygonChanged?.Invoke();
        InvalidateVisual();
    }

    private int? HitPolygonVertex(Point canvas)
    {
        if (_polygon.Count == 0) return null;
        var imageRect = ImageRect();
        for (var i = 0; i < _polygon.Count; i++)
            if (Distance(canvas, PixelToCanvas(_polygon[i], imageRect)) <= 11) return i;
        return null;
    }

    private int? HitPolygonEdge(Point canvas)
    {
        if (!_polygonClosed || _polygon.Count < 3) return null;
        var imageRect = ImageRect();
        var best = double.MaxValue;
        int? bestIndex = null;
        for (var i = 0; i < _polygon.Count; i++)
        {
            var a = PixelToCanvas(_polygon[i], imageRect);
            var b = PixelToCanvas(_polygon[(i + 1) % _polygon.Count], imageRect);
            var distance = DistanceToSegment(canvas, a, b);
            if (distance < best)
            {
                best = distance;
                bestIndex = i;
            }
        }
        return best <= 9 ? bestIndex : null;
    }

    private void PickOrBeginCoreDrag(Point canvas, Point uv)
    {
        var core = _controller.Document.Config.Cores.FirstOrDefault(candidate =>
            Math.Pow((uv.X - candidate.X) / candidate.RadiusX, 2) + Math.Pow((uv.Y - candidate.Y) / candidate.RadiusY, 2) <= 1);
        SelectedCoreId = core?.Id;
        CoreSelected?.Invoke(core?.Id);
        if (core is null) return;
        _activeCore = core;
        var rect = ImageRect();
        var coreCanvas = UvToCanvas(new Point(core.X, core.Y), rect);
        var handleX = UvToCanvas(new Point(core.X + core.RadiusX, core.Y), rect);
        var handleY = UvToCanvas(new Point(core.X, core.Y + core.RadiusY), rect);
        _coreDrag = Distance(canvas, handleX) < 12 ? CoreDrag.ResizeX : Distance(canvas, handleY) < 12 ? CoreDrag.ResizeY : CoreDrag.Move;
    }

    private void BeginCompoundEdit()
    {
        if (_compoundStarted) return;
        _controller.BeginCompoundEdit();
        _compoundStarted = true;
    }

    private void UpdateCoreDrag(Point uv)
    {
        if (_activeCore is null) return;
        if (_coreDrag == CoreDrag.Create)
        {
            var start = CanvasToUv(_dragStart)!.Value;
            _activeCore.X = (start.X + uv.X) / 2;
            _activeCore.Y = (start.Y + uv.Y) / 2;
            _activeCore.RadiusX = Math.Max(0.001, Math.Abs(uv.X - start.X) / 2);
            _activeCore.RadiusY = Math.Max(0.001, Math.Abs(uv.Y - start.Y) / 2);
        }
        else if (_coreDrag == CoreDrag.Move)
        {
            _activeCore.X = Math.Clamp(uv.X, -1, 2);
            _activeCore.Y = Math.Clamp(uv.Y, -1, 2);
        }
        else if (_coreDrag == CoreDrag.ResizeX) _activeCore.RadiusX = Math.Clamp(Math.Abs(uv.X - _activeCore.X), 0.001, 2);
        else if (_coreDrag == CoreDrag.ResizeY) _activeCore.RadiusY = Math.Clamp(Math.Abs(uv.Y - _activeCore.Y), 0.001, 2);
    }

    private CropDrag HitCrop(Point canvas)
    {
        if (CropRect is not ImagePixelRect crop) return CropDrag.Draw;
        var rect = PixelToCanvas(crop, ImageRect(), _controller.Document.Config.Image.Width, _controller.Document.Config.Image.Height);
        const double threshold = 11;
        var nearLeft = Math.Abs(canvas.X - rect.Left) <= threshold;
        var nearRight = Math.Abs(canvas.X - rect.Right) <= threshold;
        var nearTop = Math.Abs(canvas.Y - rect.Top) <= threshold;
        var nearBottom = Math.Abs(canvas.Y - rect.Bottom) <= threshold;
        var withinX = canvas.X >= rect.Left - threshold && canvas.X <= rect.Right + threshold;
        var withinY = canvas.Y >= rect.Top - threshold && canvas.Y <= rect.Bottom + threshold;

        if (nearLeft && nearTop) return CropDrag.TopLeft;
        if (nearRight && nearTop) return CropDrag.TopRight;
        if (nearLeft && nearBottom) return CropDrag.BottomLeft;
        if (nearRight && nearBottom) return CropDrag.BottomRight;
        if (nearLeft && withinY) return CropDrag.Left;
        if (nearRight && withinY) return CropDrag.Right;
        if (nearTop && withinX) return CropDrag.Top;
        if (nearBottom && withinX) return CropDrag.Bottom;
        return rect.Contains(canvas) ? CropDrag.Move : CropDrag.Draw;
    }

    private void UpdateCrop(Point uv)
    {
        var imageWidth = _controller.Document.Config.Image.Width;
        var imageHeight = _controller.Document.Config.Image.Height;
        var px = Math.Clamp((int)Math.Round(uv.X * imageWidth), 0, imageWidth);
        var py = Math.Clamp((int)Math.Round(uv.Y * imageHeight), 0, imageHeight);
        var left = _cropOriginal.X;
        var top = _cropOriginal.Y;
        var right = _cropOriginal.X + _cropOriginal.Width;
        var bottom = _cropOriginal.Y + _cropOriginal.Height;

        if (_cropDrag == CropDrag.Draw)
        {
            var startX = Math.Clamp((int)Math.Floor(_cropStartPixel.X), 0, imageWidth - 1);
            var startY = Math.Clamp((int)Math.Floor(_cropStartPixel.Y), 0, imageHeight - 1);
            left = Math.Min(startX, Math.Min(px, imageWidth - 1));
            top = Math.Min(startY, Math.Min(py, imageHeight - 1));
            right = Math.Max(startX + 1, px);
            bottom = Math.Max(startY + 1, py);
        }
        else if (_cropDrag == CropDrag.Move)
        {
            var dx = (int)Math.Round(px - _cropStartPixel.X);
            var dy = (int)Math.Round(py - _cropStartPixel.Y);
            left = Math.Clamp(_cropOriginal.X + dx, 0, imageWidth - _cropOriginal.Width);
            top = Math.Clamp(_cropOriginal.Y + dy, 0, imageHeight - _cropOriginal.Height);
            right = left + _cropOriginal.Width;
            bottom = top + _cropOriginal.Height;
        }
        else
        {
            if (_cropDrag is CropDrag.Left or CropDrag.TopLeft or CropDrag.BottomLeft) left = Math.Clamp(px, 0, right - 1);
            if (_cropDrag is CropDrag.Right or CropDrag.TopRight or CropDrag.BottomRight) right = Math.Clamp(px, left + 1, imageWidth);
            if (_cropDrag is CropDrag.Top or CropDrag.TopLeft or CropDrag.TopRight) top = Math.Clamp(py, 0, bottom - 1);
            if (_cropDrag is CropDrag.Bottom or CropDrag.BottomLeft or CropDrag.BottomRight) bottom = Math.Clamp(py, top + 1, imageHeight);
        }

        CropRect = new ImagePixelRect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }

    private void DrawCrop(DrawingContext context, Rect imageRect)
    {
        if (CropRect is not ImagePixelRect crop) return;
        var rect = PixelToCanvas(crop, imageRect, _controller.Document.Config.Image.Width, _controller.Document.Config.Image.Height);
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)), new Pen(Brushes.White, 2), rect);
        context.DrawLine(new Pen(Brushes.White, 1), new Point(rect.X + rect.Width / 3, rect.Y), new Point(rect.X + rect.Width / 3, rect.Bottom));
        context.DrawLine(new Pen(Brushes.White, 1), new Point(rect.X + rect.Width * 2 / 3, rect.Y), new Point(rect.X + rect.Width * 2 / 3, rect.Bottom));
        context.DrawLine(new Pen(Brushes.White, 1), new Point(rect.X, rect.Y + rect.Height / 3), new Point(rect.Right, rect.Y + rect.Height / 3));
        context.DrawLine(new Pen(Brushes.White, 1), new Point(rect.X, rect.Y + rect.Height * 2 / 3), new Point(rect.Right, rect.Y + rect.Height * 2 / 3));
        foreach (var handle in CropHandles(rect)) context.FillRectangle(Brushes.White, new Rect(handle.X - 4, handle.Y - 4, 8, 8));
    }

    private void DrawPolygon(DrawingContext context, Rect imageRect)
    {
        if (_mode != EditorMode.PolygonCutout || _polygon.Count == 0) return;
        var points = _polygon.Select(point => PixelToCanvas(point, imageRect)).ToArray();
        var linePen = new Pen(new SolidColorBrush(Color.Parse("#FFE58A")), 2);
        for (var i = 1; i < points.Length; i++) context.DrawLine(linePen, points[i - 1], points[i]);
        if (_polygonClosed && points.Length >= 3)
        {
            context.DrawLine(linePen, points[^1], points[0]);
            DrawOutsidePolygonPreview(context, imageRect, points);
        }
        else if (_polygonPointer is PixelPoint prospective)
        {
            context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(190, 255, 229, 138)), 1), points[^1], PixelToCanvas(prospective, imageRect));
        }

        for (var i = 0; i < points.Length; i++)
        {
            var selected = i == _selectedPolygonVertex;
            var first = i == 0;
            var radius = selected ? 7 : first ? 6 : 5;
            var fill = selected ? Brushes.White : first ? new SolidColorBrush(Color.Parse("#FF9CF0")) : new SolidColorBrush(Color.Parse("#FFE58A"));
            context.DrawEllipse(fill, new Pen(Brushes.Black, 1), points[i], radius, radius);
        }
    }

    private static void DrawOutsidePolygonPreview(DrawingContext context, Rect imageRect, IReadOnlyList<Point> polygon)
    {
        var geometry = new StreamGeometry { FillRule = FillRule.EvenOdd };
        using (var path = geometry.Open())
        {
            path.BeginFigure(imageRect.TopLeft, true);
            path.LineTo(imageRect.TopRight);
            path.LineTo(imageRect.BottomRight);
            path.LineTo(imageRect.BottomLeft);
            path.EndFigure(true);
            path.BeginFigure(polygon[0], true);
            for (var i = 1; i < polygon.Count; i++) path.LineTo(polygon[i]);
            path.EndFigure(true);
        }
        context.DrawGeometry(new SolidColorBrush(Color.FromArgb(92, 20, 16, 28)), null, geometry);
    }

    private void DrawAlphaCursor(DrawingContext context, Rect imageRect)
    {
        if (!IsAlphaMode(_mode) || _alphaCursor is not PixelPoint cursor) return;
        var center = PixelToCanvas(cursor, imageRect);
        var sourceScale = imageRect.Width / Math.Max(1, _controller.Document.Config.Image.Width);
        var radius = Math.Max(0.5, _alphaBrushSize * sourceScale / 2);
        context.DrawEllipse(null, new Pen(Brushes.White, 1.5), center, radius, radius);
        context.DrawEllipse(null, new Pen(Brushes.Black, 0.75), center, radius + 1.5, radius + 1.5);
    }

    private void DrawCores(DrawingContext context, Rect imageRect)
    {
        foreach (var core in _controller.Document.Config.Cores)
        {
            var center = UvToCanvas(new Point(core.X, core.Y), imageRect);
            var rect = new Rect(center.X - core.RadiusX * imageRect.Width, center.Y - core.RadiusY * imageRect.Height,
                core.RadiusX * imageRect.Width * 2, core.RadiusY * imageRect.Height * 2);
            var selected = core.Id == SelectedCoreId;
            context.DrawEllipse(new SolidColorBrush(Color.FromArgb(selected ? (byte)55 : (byte)30, 156, 118, 255)), new Pen(selected ? Brushes.White : Brushes.MediumPurple, selected ? 2 : 1), rect);
            if (selected)
            {
                context.DrawEllipse(Brushes.White, null, new Point(rect.Right, center.Y), 5, 5);
                context.DrawEllipse(Brushes.White, null, new Point(center.X, rect.Bottom), 5, 5);
            }
        }
    }

    private void DrawHeatmap(DrawingContext context, Rect imageRect)
    {
        const int cells = 36;
        var cores = _controller.Document.Config.Cores;
        for (var y = 0; y < cells; y++)
        for (var x = 0; x < cells; x++)
        {
            var influence = InfluenceFields.CombinedCoreInfluence(cores, new System.Numerics.Vector2((x + 0.5f) / cells, (y + 0.5f) / cells));
            if (influence < 0.015) continue;
            context.FillRectangle(new SolidColorBrush(Color.FromArgb((byte)(influence * 105), 255, (byte)(190 - influence * 120), 40)),
                new Rect(imageRect.X + x * imageRect.Width / cells, imageRect.Y + y * imageRect.Height / cells, imageRect.Width / cells + 1, imageRect.Height / cells + 1));
        }
    }

    private void DrawRigidity(DrawingContext context, Rect imageRect)
    {
        foreach (var stroke in _controller.Document.Config.RigidityStrokes)
        {
            var brush = new SolidColorBrush(Color.FromArgb((byte)(35 + stroke.Strength * 145), 45, 220, 235));
            var radius = stroke.Radius * Math.Sqrt(imageRect.Width * imageRect.Height);
            foreach (var point in stroke.Points)
            {
                var canvas = UvToCanvas(new Point(point[0], point[1]), imageRect);
                context.DrawEllipse(brush, null, canvas, radius, radius);
            }
        }
    }

    private static void DrawCheckerboard(DrawingContext context, Rect rect)
    {
        const double size = 14;
        context.FillRectangle(new SolidColorBrush(Color.Parse("#35353A")), rect);
        var light = new SolidColorBrush(Color.Parse("#45454B"));
        for (var y = rect.Y; y < rect.Bottom; y += size)
        for (var x = rect.X; x < rect.Right; x += size)
            if ((((int)((x - rect.X) / size)) + ((int)((y - rect.Y) / size))) % 2 == 0)
                context.FillRectangle(light, new Rect(x, y, Math.Min(size, rect.Right - x), Math.Min(size, rect.Bottom - y)));
    }

    private Point? CanvasToUv(Point point)
    {
        var rect = ImageRect();
        if (!rect.Contains(point)) return null;
        return new Point((point.X - rect.X) / rect.Width, (point.Y - rect.Y) / rect.Height);
    }

    private Point CanvasToUvClamped(Point point)
    {
        var rect = ImageRect();
        return new Point(Math.Clamp((point.X - rect.X) / rect.Width, 0, 1), Math.Clamp((point.Y - rect.Y) / rect.Height, 0, 1));
    }

    private PixelPoint? CanvasToPixel(Point point)
    {
        var uv = CanvasToUv(point);
        if (uv is null) return null;
        return new PixelPoint(uv.Value.X * _controller.Document.Config.Image.Width, uv.Value.Y * _controller.Document.Config.Image.Height);
    }

    private PixelPoint CanvasToPixelClamped(Point point)
    {
        var uv = CanvasToUvClamped(point);
        return new PixelPoint(uv.X * _controller.Document.Config.Image.Width, uv.Y * _controller.Document.Config.Image.Height);
    }

    private Point PixelToCanvas(PixelPoint pixel, Rect rect) => new(
        rect.X + pixel.X / Math.Max(1, _controller.Document.Config.Image.Width) * rect.Width,
        rect.Y + pixel.Y / Math.Max(1, _controller.Document.Config.Image.Height) * rect.Height);

    private static Point[] CropHandles(Rect rect) =>
    [
        rect.TopLeft, new Point(rect.Center.X, rect.Top), rect.TopRight,
        new Point(rect.Left, rect.Center.Y), new Point(rect.Right, rect.Center.Y),
        rect.BottomLeft, new Point(rect.Center.X, rect.Bottom), rect.BottomRight
    ];

    private static Bitmap DecodeBitmap(byte[] png, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new MemoryStream(png, writable: false);
        var bitmap = new Bitmap(stream);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    private static bool IsAlphaMode(EditorMode mode) => mode is EditorMode.AlphaErase or EditorMode.AlphaRestore;
    private static Point UvToCanvas(Point uv, Rect rect) => new(rect.X + uv.X * rect.Width, rect.Y + uv.Y * rect.Height);
    private static Rect PixelToCanvas(ImagePixelRect crop, Rect rect, int imageWidth, int imageHeight) => new(
        rect.X + crop.X / (double)imageWidth * rect.Width,
        rect.Y + crop.Y / (double)imageHeight * rect.Height,
        crop.Width / (double)imageWidth * rect.Width,
        crop.Height / (double)imageHeight * rect.Height);
    private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    private static double DistanceToSegment(Point point, Point a, Point b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var lengthSquared = dx * dx + dy * dy;
        if (lengthSquared <= 1e-9) return Distance(point, a);
        var t = Math.Clamp(((point.X - a.X) * dx + (point.Y - a.Y) * dy) / lengthSquared, 0, 1);
        return Distance(point, new Point(a.X + dx * t, a.Y + dy * t));
    }

    private enum CoreDrag { None, Create, Move, ResizeX, ResizeY }
    private enum CropDrag { None, Draw, Move, Left, Right, Top, Bottom, TopLeft, TopRight, BottomLeft, BottomRight }
}
