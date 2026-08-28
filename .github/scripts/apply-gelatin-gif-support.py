from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def read(rel):
    return (ROOT / rel).read_text(encoding="utf-8")


def write(rel, text):
    (ROOT / rel).write_text(text, encoding="utf-8", newline="\n")


def replace_once(rel, old, new):
    text = read(rel)
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{rel}: expected exactly one occurrence, found {count}: {old[:100]!r}")
    write(rel, text.replace(old, new, 1))


def replace_section(rel, start, end, replacement):
    text = read(rel)
    i = text.find(start)
    if i < 0:
        raise RuntimeError(f"{rel}: start marker not found: {start!r}")
    j = text.find(end, i)
    if j < 0:
        raise RuntimeError(f"{rel}: end marker not found: {end!r}")
    write(rel, text[:i] + replacement + text[j:])


# -----------------------------------------------------------------------------
# DocumentController: animated import, frame access, and atlas-aware commits.
# -----------------------------------------------------------------------------
rel = "tools/gelatin/src/Gelatin.App/DocumentController.cs"
replace_once(rel, 'private const string ToolVersion = "0.1.2";', 'private const string ToolVersion = "0.1.3";')
replace_once(
    rel,
    "    public event EventHandler? Changed;\n\n    public DocumentController()",
    "    public event EventHandler? Changed;\n\n"
    "    public bool IsAnimated => AnimatedImageProcessor.IsAnimated(_document.Config);\n"
    "    public int AnimationFrameCount => _document.Config.Animation?.Frames.Count ?? 1;\n"
    "    public byte[] GetFramePng(int frameIndex) => AnimatedImageProcessor.GetFramePng(_document, frameIndex);\n"
    "    public ImageStorageResult GetRecoveryStorage() => new(\n"
    "        (byte[])EnsureRecoverySource().Clone(),\n"
    "        _document.Config.Image.Width,\n"
    "        _document.Config.Image.Height,\n"
    "        _document.Config.Animation?.DeepClone());\n\n"
    "    public DocumentController()"
)
replace_once(
    rel,
    "    public void BeginCompoundEdit() => _history.Record(_document);",
    "    public void CommitStorage(ImageStorageResult storage, Action<GelConfig>? remap = null, ImageStorageResult? recoveryStorage = null)\n"
    "    {\n"
    "        ArgumentNullException.ThrowIfNull(storage);\n"
    "        var nextConfig = _document.Config.DeepClone();\n"
    "        remap?.Invoke(nextConfig);\n"
    "        nextConfig.SchemaVersion = storage.IsAnimated ? 2 : 1;\n"
    "        nextConfig.Animation = storage.Animation?.DeepClone();\n"
    "        nextConfig.Image.Width = storage.Width;\n"
    "        nextConfig.Image.Height = storage.Height;\n"
    "        nextConfig.Authoring.ToolVersion = ToolVersion;\n"
    "        GelValidator.Validate(nextConfig);\n\n"
    "        var storageDimensions = ImageProcessor.GetDimensions(storage.PngBytes);\n"
    "        if (storage.IsAnimated) AnimatedImageProcessor.ValidateAtlas(nextConfig, storageDimensions.Width, storageDimensions.Height);\n"
    "        else if (storageDimensions != (storage.Width, storage.Height))\n"
    "            throw new GelFormatException(\"The processed image dimensions do not match the logical image dimensions.\");\n\n"
    "        var recovery = recoveryStorage?.PngBytes is { } bytes ? (byte[])bytes.Clone() : (byte[])storage.PngBytes.Clone();\n"
    "        var recoveryDimensions = ImageProcessor.GetDimensions(recovery);\n"
    "        if (storage.IsAnimated) AnimatedImageProcessor.ValidateAtlas(nextConfig, recoveryDimensions.Width, recoveryDimensions.Height);\n"
    "        else if (recoveryDimensions != (storage.Width, storage.Height)) recovery = (byte[])storage.PngBytes.Clone();\n\n"
    "        _history.Record(_document);\n"
    "        _document = new GelDocument { Config = nextConfig, PngBytes = storage.PngBytes, RecoveryPngBytes = recovery };\n"
    "        IsDirty = true;\n"
    "        Notify();\n"
    "    }\n\n"
    "    public void BeginCompoundEdit() => _history.Record(_document);"
)
replace_section(
    rel,
    "    private static GelDocument CreateFromImage(byte[] bytes, string name)",
    "    private static GelDocument CreateWelcomeDocument()",
    "    private static GelDocument CreateFromImage(byte[] bytes, string name)\n"
    "    {\n"
    "        var storage = AnimatedImageProcessor.NormalizeInput(bytes);\n"
    "        return new GelDocument\n"
    "        {\n"
    "            PngBytes = storage.PngBytes,\n"
    "            Config = new GelConfig\n"
    "            {\n"
    "                SchemaVersion = storage.IsAnimated ? 2 : 1,\n"
    "                Animation = storage.Animation?.DeepClone(),\n"
    "                AssetName = string.IsNullOrWhiteSpace(name) ? \"Untitled Gel\" : name,\n"
    "                Image = new ImageConfig { Width = storage.Width, Height = storage.Height },\n"
    "                Cores = [new CoreConfig { Id = 1, Name = \"Core 1\", RadiusX = 0.24, RadiusY = 0.24 }]\n"
    "            }\n"
    "        };\n"
    "    }\n\n"
)

# -----------------------------------------------------------------------------
# EditorCanvas: animate displayed frame and make alpha brush apply to all frames.
# -----------------------------------------------------------------------------
rel = "tools/gelatin/src/Gelatin.App/Controls/EditorCanvas.cs"
replace_once(rel, "using Avalonia;", "using System.Diagnostics;\nusing Avalonia;")
replace_once(rel, "using Avalonia.Media.Imaging;", "using Avalonia.Media.Imaging;\nusing Avalonia.Threading;")
replace_once(
    rel,
    "    private Bitmap? _bitmap;\n    private Bitmap? _preview;\n    private byte[]? _bitmapSource;",
    "    private Bitmap? _bitmap;\n"
    "    private Bitmap? _preview;\n"
    "    private GelDocument? _bitmapDocument;\n"
    "    private int _loadedFrameIndex = -1;\n"
    "    private readonly DispatcherTimer _animationTimer;\n"
    "    private readonly Stopwatch _animationClock = Stopwatch.StartNew();"
)
replace_once(
    rel,
    "    private AlphaBrushSession? _alphaBrush;",
    "    private AlphaBrushSession? _alphaBrush;\n    private AnimationAlphaBrushSession? _animatedAlphaBrush;"
)
replace_once(
    rel,
    "    public double Zoom => _zoom;\n    public Point Pan => _pan;",
    "    public double Zoom => _zoom;\n"
    "    public Point Pan => _pan;\n"
    "    public int CurrentFrameIndex => AnimatedImageProcessor.IsAnimated(_controller.Document.Config)\n"
    "        ? AnimatedImageProcessor.FrameIndexAtTime(_controller.Document.Config.Animation, _animationClock.Elapsed.TotalMilliseconds)\n"
    "        : 0;"
)
replace_once(
    rel,
    "        _controller.Changed += (_, _) => Reload();\n        Reload();",
    "        _controller.Changed += (_, _) => Reload();\n"
    "        _animationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };\n"
    "        _animationTimer.Tick += (_, _) => RefreshAnimationFrame();\n"
    "        _animationTimer.Start();\n"
    "        Reload();"
)
replace_once(
    rel,
    "        _shutdown = true;\n        CancelAlphaStroke();",
    "        _shutdown = true;\n        _animationTimer.Stop();\n        CancelAlphaStroke();"
)
replace_once(
    rel,
    "        if (_alphaPainting || _alphaBrush is not null) CancelAlphaStroke();",
    "        if (_alphaPainting || _alphaBrush is not null || _animatedAlphaBrush is not null) CancelAlphaStroke();"
)
replace_section(
    rel,
    "    private async void Reload()",
    "    private Rect ImageRect()",
    "    private void RefreshAnimationFrame()\n"
    "    {\n"
    "        if (_shutdown || _preview is not null || !AnimatedImageProcessor.IsAnimated(_controller.Document.Config)) return;\n"
    "        if (CurrentFrameIndex != _loadedFrameIndex) Reload();\n"
    "    }\n\n"
    "    private async void Reload()\n"
    "    {\n"
    "        var document = _controller.Document;\n"
    "        var changedDocument = !ReferenceEquals(_bitmapDocument, document);\n"
    "        if (changedDocument)\n"
    "        {\n"
    "            _bitmapDocument = document;\n"
    "            _loadedFrameIndex = -1;\n"
    "            _animationClock.Restart();\n"
    "        }\n"
    "        var frameIndex = CurrentFrameIndex;\n"
    "        if (!changedDocument && frameIndex == _loadedFrameIndex)\n"
    "        {\n"
    "            InvalidateVisual();\n"
    "            return;\n"
    "        }\n"
    "        var png = AnimatedImageProcessor.IsAnimated(document.Config)\n"
    "            ? AnimatedImageProcessor.GetFramePng(document, frameIndex)\n"
    "            : document.PngBytes;\n"
    "        _loadedFrameIndex = frameIndex;\n"
    "        _bitmapCancellation?.Cancel();\n"
    "        _previewCancellation?.Cancel();\n"
    "        _previewCancellation = null;\n"
    "        var cancellation = new CancellationTokenSource();\n"
    "        _bitmapCancellation = cancellation;\n"
    "        _preview?.Dispose();\n"
    "        _preview = null;\n"
    "        try\n"
    "        {\n"
    "            var bitmap = await Task.Run(() => DecodeBitmap(png, cancellation.Token), cancellation.Token);\n"
    "            if (!ReferenceEquals(_bitmapCancellation, cancellation) || _shutdown)\n"
    "            {\n"
    "                bitmap.Dispose();\n"
    "                return;\n"
    "            }\n"
    "            var previous = _bitmap;\n"
    "            _bitmap = bitmap;\n"
    "            previous?.Dispose();\n"
    "            InvalidateVisual();\n"
    "        }\n"
    "        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }\n"
    "        catch (Exception ex)\n"
    "        {\n"
    "            if (ReferenceEquals(_bitmapCancellation, cancellation)) ImageError?.Invoke($\"The processed image could not be displayed: {ex.Message}\");\n"
    "        }\n"
    "        finally\n"
    "        {\n"
    "            if (ReferenceEquals(_bitmapCancellation, cancellation)) _bitmapCancellation = null;\n"
    "            cancellation.Dispose();\n"
    "        }\n"
    "    }\n\n"
)
replace_section(
    rel,
    "    private void BeginAlphaStroke(PointerPressedEventArgs e, Point canvas)",
    "    private void CommitAlphaStroke()",
    "    private void BeginAlphaStroke(PointerPressedEventArgs e, Point canvas)\n"
    "    {\n"
    "        var pixel = CanvasToPixel(canvas);\n"
    "        if (pixel is null) return;\n"
    "        try\n"
    "        {\n"
    "            CancelAlphaStroke();\n"
    "            var mode = _mode == EditorMode.AlphaErase ? AlphaBrushMode.Erase : AlphaBrushMode.Restore;\n"
    "            _alphaBrushDocument = _controller.Document;\n"
    "            if (AnimatedImageProcessor.IsAnimated(_controller.Document.Config))\n"
    "            {\n"
    "                _animatedAlphaBrush = new AnimationAlphaBrushSession(_controller.Document.PngBytes, _controller.RecoveryPngBytes, _controller.Document.Config, mode, _alphaBrushSize);\n"
    "                _animatedAlphaBrush.ApplyPoint(pixel.Value);\n"
    "                SetPreview(_animatedAlphaBrush.EncodePreview(CurrentFrameIndex));\n"
    "            }\n"
    "            else\n"
    "            {\n"
    "                _alphaBrush = new AlphaBrushSession(_controller.Document.PngBytes, _controller.RecoveryPngBytes, mode, _alphaBrushSize);\n"
    "                _alphaBrush.ApplyPoint(pixel.Value);\n"
    "                SetPreview(_alphaBrush.Encode());\n"
    "            }\n"
    "            _lastAlphaPoint = pixel;\n"
    "            _alphaCursor = pixel;\n"
    "            _alphaPainting = true;\n"
    "            e.Pointer.Capture(this);\n"
    "        }\n"
    "        catch (Exception ex)\n"
    "        {\n"
    "            EditorError?.Invoke($\"Alpha brush could not start: {ex.Message}\");\n"
    "            CancelAlphaStroke();\n"
    "        }\n"
    "    }\n\n"
)
replace_section(
    rel,
    "    private void CommitAlphaStroke()",
    "    private void CancelAlphaStroke()",
    "    private void CommitAlphaStroke()\n"
    "    {\n"
    "        var brush = _alphaBrush;\n"
    "        var animatedBrush = _animatedAlphaBrush;\n"
    "        var sourceDocument = _alphaBrushDocument;\n"
    "        _alphaBrush = null;\n"
    "        _animatedAlphaBrush = null;\n"
    "        _alphaBrushDocument = null;\n"
    "        _alphaPainting = false;\n"
    "        _lastAlphaPoint = null;\n"
    "        if (brush is null && animatedBrush is null) return;\n"
    "        try\n"
    "        {\n"
    "            SetPreview(null);\n"
    "            if (!ReferenceEquals(sourceDocument, _controller.Document))\n"
    "            {\n"
    "                EditorError?.Invoke(\"The image changed while painting; the unfinished alpha stroke was discarded.\");\n"
    "                return;\n"
    "            }\n"
    "            if (animatedBrush is not null)\n"
    "            {\n"
    "                var result = animatedBrush.Encode();\n"
    "                var recovery = _controller.GetRecoveryStorage();\n"
    "                _controller.CommitStorage(result, recoveryStorage: recovery);\n"
    "            }\n"
    "            else\n"
    "            {\n"
    "                _controller.CommitImage(brush!.Encode());\n"
    "            }\n"
    "        }\n"
    "        catch (Exception ex)\n"
    "        {\n"
    "            EditorError?.Invoke($\"Alpha brush could not be committed: {ex.Message}\");\n"
    "        }\n"
    "        finally\n"
    "        {\n"
    "            brush?.Dispose();\n"
    "            animatedBrush?.Dispose();\n"
    "        }\n"
    "    }\n\n"
)
replace_section(
    rel,
    "    private void CancelAlphaStroke()",
    "    private void ClearPolygon()",
    "    private void CancelAlphaStroke()\n"
    "    {\n"
    "        _alphaPainting = false;\n"
    "        _lastAlphaPoint = null;\n"
    "        _alphaBrushDocument = null;\n"
    "        _alphaBrush?.Dispose();\n"
    "        _animatedAlphaBrush?.Dispose();\n"
    "        _alphaBrush = null;\n"
    "        _animatedAlphaBrush = null;\n"
    "        if (_preview is not null) SetPreview(null);\n"
    "    }\n\n"
)
replace_once(
    rel,
    "                    if (_lastAlphaPoint is PixelPoint last) _alphaBrush!.ApplySegment(last, current);\n                    else _alphaBrush!.ApplyPoint(current);\n                    _lastAlphaPoint = current;\n                    SetPreview(_alphaBrush.Encode());",
    "                    if (_animatedAlphaBrush is not null)\n"
    "                    {\n"
    "                        if (_lastAlphaPoint is PixelPoint last) _animatedAlphaBrush.ApplySegment(last, current);\n"
    "                        else _animatedAlphaBrush.ApplyPoint(current);\n"
    "                        SetPreview(_animatedAlphaBrush.EncodePreview(CurrentFrameIndex));\n"
    "                    }\n"
    "                    else\n"
    "                    {\n"
    "                        if (_lastAlphaPoint is PixelPoint last) _alphaBrush!.ApplySegment(last, current);\n"
    "                        else _alphaBrush!.ApplyPoint(current);\n"
    "                        SetPreview(_alphaBrush!.Encode());\n"
    "                    }\n"
    "                    _lastAlphaPoint = current;"
)

# -----------------------------------------------------------------------------
# MainWindow: GIF file picker, all-frame transforms, animation status/version.
# -----------------------------------------------------------------------------
rel = "tools/gelatin/src/Gelatin.App/MainWindow.cs"
text = read(rel).replace("Gelatin 0.1.2", "Gelatin 0.1.3")
write(rel, text)
replace_once(
    rel,
    "        _editor.PixelPicked += (x, y) => { _backgroundColor = RawRgbaTransforms.Sample(_controller.Document.PngBytes, x, y); UpdateBackgroundPreview(); };",
    "        _editor.PixelPicked += (x, y) => { _backgroundColor = RawRgbaTransforms.Sample(_controller.GetFramePng(_editor.CurrentFrameIndex), x, y); UpdateBackgroundPreview(); };"
)
replace_once(
    rel,
    'Patterns = ["*.gel", "*.png", "*.jpg", "*.jpeg", "*.webp"]',
    'Patterns = ["*.gel", "*.png", "*.jpg", "*.jpeg", "*.webp", "*.gif"]'
)
replace_once(
    rel,
    '        left.Children.Add(ActionButton("Open image / .gel", async () => await OpenPickerAsync(), wide: true));',
    '        left.Children.Add(ActionButton("Open image / .gel", async () => await OpenPickerAsync(), wide: true));\n'
    '        if (_controller.Document.Config.Animation is { } animation)\n'
    '        {\n'
    '            var repeat = animation.RepetitionCount < 0 ? "loops forever" : animation.RepetitionCount == 0 ? "plays once" : $"repeats {animation.RepetitionCount} time(s)";\n'
    '            var cycleMs = animation.Frames.Sum(frame => AnimatedImageProcessor.EffectiveDuration(frame.DurationMs));\n'
    '            left.Children.Add(new TextBlock { Text = $"Animated GIF: {animation.Frames.Count} frames • {cycleMs} ms/cycle • {repeat}", TextWrapping = TextWrapping.Wrap, Foreground = MutedBrush(), FontSize = 11 });\n'
    '        }'
)
replace_once(rel, 'right.Children.Add(ActionButton("Export processed PNG", async () => await ExportPngAsync(), wide: true));', 'right.Children.Add(ActionButton("Export embedded PNG / atlas", async () => await ExportPngAsync(), wide: true));')
replace_section(
    rel,
    "    private async Task ApplyCropAsync()",
    "    private async Task ApplyPolygonCutoutAsync()",
    "    private async Task ApplyCropAsync()\n"
    "    {\n"
    "        if (_editor.CropRect is not ImagePixelRect crop) return;\n"
    "        var document = _controller.Document;\n"
    "        var oldWidth = document.Config.Image.Width;\n"
    "        var oldHeight = document.Config.Image.Height;\n"
    "        try\n"
    "        {\n"
    "            _status.Text = \"Cropping image…\";\n"
    "            if (AnimatedImageProcessor.IsAnimated(document.Config))\n"
    "            {\n"
    "                var visible = await Task.Run(() => AnimatedImageProcessor.TransformAnimated(document.PngBytes, document.Config, frame => RawRgbaTransforms.Crop(frame, crop)));\n"
    "                var recovery = await Task.Run(() => AnimatedImageProcessor.TransformAnimated(document.RecoveryPngBytes ?? document.PngBytes, document.Config, frame => RawRgbaTransforms.Crop(frame, crop)));\n"
    "                if (!ReferenceEquals(document, _controller.Document)) return;\n"
    "                _controller.CommitStorage(visible, config => ImageProcessor.RemapAuthoringForCrop(config, crop, oldWidth, oldHeight), recovery);\n"
    "            }\n"
    "            else\n"
    "            {\n"
    "                var png = await Task.Run(() => RawRgbaTransforms.Crop(document.PngBytes, crop));\n"
    "                if (!ReferenceEquals(document, _controller.Document)) return;\n"
    "                _controller.CommitImage(png,\n"
    "                    config => ImageProcessor.RemapAuthoringForCrop(config, crop, oldWidth, oldHeight),\n"
    "                    recovery => RawRgbaTransforms.Crop(recovery, crop));\n"
    "            }\n"
    "            _editor.CancelCrop();\n"
    "            _editor.Mode = EditorMode.Select;\n"
    "        }\n"
    "        catch (Exception ex) { await Dialogs.ShowErrorAsync(this, \"Crop failed\", ex.Message); }\n"
    "    }\n\n"
)
replace_section(
    rel,
    "    private async Task ApplyPolygonCutoutAsync()",
    "    private async Task ResizeImageAsync(int width, int height)",
    "    private async Task ApplyPolygonCutoutAsync()\n"
    "    {\n"
    "        if (!_editor.PolygonClosed)\n"
    "        {\n"
    "            _status.Text = \"Close the polygon before applying the cutout.\";\n"
    "            return;\n"
    "        }\n"
    "        var polygon = _editor.GetPolygonSnapshot();\n"
    "        var validation = PolygonGeometry.Validate(polygon);\n"
    "        if (!validation.IsValid)\n"
    "        {\n"
    "            _status.Text = validation.Error!;\n"
    "            return;\n"
    "        }\n\n"
    "        var document = _controller.Document;\n"
    "        var oldWidth = document.Config.Image.Width;\n"
    "        var oldHeight = document.Config.Image.Height;\n"
    "        try\n"
    "        {\n"
    "            _status.Text = \"Applying polygon cutout and trimming transparent margins…\";\n"
    "            if (AnimatedImageProcessor.IsAnimated(document.Config))\n"
    "            {\n"
    "                var result = await Task.Run(() =>\n"
    "                {\n"
    "                    var masked = AnimatedImageProcessor.TransformAnimated(document.PngBytes, document.Config, frame => ImageAlphaEditing.ApplyPolygonCutout(frame, polygon));\n"
    "                    var maskedConfig = document.Config.DeepClone();\n"
    "                    maskedConfig.Animation = masked.Animation?.DeepClone();\n"
    "                    var bounds = AnimatedImageProcessor.FindUnionTrimBounds(masked.PngBytes, maskedConfig, 0);\n"
    "                    if (bounds is null) return (Bounds: (ImagePixelRect?)null, Visible: (ImageStorageResult?)null, Recovery: (ImageStorageResult?)null);\n"
    "                    var visible = AnimatedImageProcessor.TransformAnimated(masked.PngBytes, maskedConfig, frame => RawRgbaTransforms.Crop(frame, bounds.Value));\n"
    "                    var recovery = AnimatedImageProcessor.TransformAnimated(document.RecoveryPngBytes ?? document.PngBytes, document.Config, frame => RawRgbaTransforms.Crop(frame, bounds.Value));\n"
    "                    return (Bounds: (ImagePixelRect?)bounds.Value, Visible: (ImageStorageResult?)visible, Recovery: (ImageStorageResult?)recovery);\n"
    "                });\n"
    "                if (!ReferenceEquals(document, _controller.Document)) return;\n"
    "                if (result.Bounds is not ImagePixelRect bounds || result.Visible is null || result.Recovery is null)\n"
    "                {\n"
    "                    _status.Text = \"The polygon cutout would make every animation frame completely transparent; nothing was changed.\";\n"
    "                    return;\n"
    "                }\n"
    "                _controller.CommitStorage(result.Visible, config => ImageProcessor.RemapAuthoringForCrop(config, bounds, oldWidth, oldHeight), result.Recovery);\n"
    "            }\n"
    "            else\n"
    "            {\n"
    "                var result = await Task.Run(() =>\n"
    "                {\n"
    "                    var masked = ImageAlphaEditing.ApplyPolygonCutout(document.PngBytes, polygon);\n"
    "                    var bounds = RawRgbaTransforms.FindTrimBounds(masked, 0);\n"
    "                    return bounds is null ? (Bounds: (ImagePixelRect?)null, Png: (byte[]?)null) :\n"
    "                        (Bounds: (ImagePixelRect?)bounds.Value, Png: (byte[]?)RawRgbaTransforms.Crop(masked, bounds.Value));\n"
    "                });\n"
    "                if (!ReferenceEquals(document, _controller.Document)) return;\n"
    "                if (result.Bounds is not ImagePixelRect bounds || result.Png is null)\n"
    "                {\n"
    "                    _status.Text = \"The polygon cutout would make the image completely transparent; nothing was changed.\";\n"
    "                    return;\n"
    "                }\n"
    "                _controller.CommitImage(result.Png,\n"
    "                    config => ImageProcessor.RemapAuthoringForCrop(config, bounds, oldWidth, oldHeight),\n"
    "                    recovery => RawRgbaTransforms.Crop(recovery, bounds));\n"
    "            }\n"
    "            _editor.Mode = EditorMode.Select;\n"
    "            RefreshChrome();\n"
    "        }\n"
    "        catch (GelFormatException ex) { _status.Text = ex.Message; }\n"
    "        catch (Exception ex) { await Dialogs.ShowErrorAsync(this, \"Polygon cutout failed\", ex.Message); }\n"
    "    }\n\n"
)
replace_section(
    rel,
    "    private async Task ResizeImageAsync(int width, int height)",
    "    private async Task TrimTransparencyAsync()",
    "    private async Task ResizeImageAsync(int width, int height)\n"
    "    {\n"
    "        var document = _controller.Document;\n"
    "        try\n"
    "        {\n"
    "            _status.Text = $\"Resizing image to {width} × {height}…\";\n"
    "            if (AnimatedImageProcessor.IsAnimated(document.Config))\n"
    "            {\n"
    "                var visible = await Task.Run(() => AnimatedImageProcessor.TransformAnimated(document.PngBytes, document.Config, frame => RawRgbaTransforms.Resize(frame, width, height)));\n"
    "                var recovery = await Task.Run(() => AnimatedImageProcessor.TransformAnimated(document.RecoveryPngBytes ?? document.PngBytes, document.Config, frame => RawRgbaTransforms.Resize(frame, width, height)));\n"
    "                if (ReferenceEquals(document, _controller.Document)) _controller.CommitStorage(visible, recoveryStorage: recovery);\n"
    "            }\n"
    "            else\n"
    "            {\n"
    "                var png = await Task.Run(() => RawRgbaTransforms.Resize(document.PngBytes, width, height));\n"
    "                if (ReferenceEquals(document, _controller.Document))\n"
    "                    _controller.CommitImage(png, recoveryTransform: recovery => RawRgbaTransforms.Resize(recovery, width, height));\n"
    "            }\n"
    "        }\n"
    "        catch (Exception ex) { await Dialogs.ShowErrorAsync(this, \"Resize failed\", ex.Message); }\n"
    "    }\n\n"
)
replace_section(
    rel,
    "    private async Task TrimTransparencyAsync()",
    "    private async void UpdateBackgroundPreview()",
    "    private async Task TrimTransparencyAsync()\n"
    "    {\n"
    "        try\n"
    "        {\n"
    "            var document = _controller.Document;\n"
    "            var oldWidth = document.Config.Image.Width;\n"
    "            var oldHeight = document.Config.Image.Height;\n"
    "            var threshold = document.Config.Image.AlphaThreshold;\n"
    "            _status.Text = \"Finding transparent edges…\";\n"
    "            var bounds = await Task.Run(() => AnimatedImageProcessor.IsAnimated(document.Config)\n"
    "                ? AnimatedImageProcessor.FindUnionTrimBounds(document.PngBytes, document.Config, threshold)\n"
    "                : RawRgbaTransforms.FindTrimBounds(document.PngBytes, threshold));\n"
    "            if (!ReferenceEquals(document, _controller.Document)) return;\n"
    "            if (bounds is null)\n"
    "            {\n"
    "                await Dialogs.ShowErrorAsync(this, \"Nothing to trim\", \"The image is completely transparent at the current alpha threshold.\");\n"
    "                RefreshChrome();\n"
    "                return;\n"
    "            }\n"
    "            if (bounds.Value is { X: 0, Y: 0 } full && full.Width == oldWidth && full.Height == oldHeight)\n"
    "            {\n"
    "                RefreshChrome();\n"
    "                return;\n"
    "            }\n"
    "            var trim = bounds.Value;\n"
    "            if (AnimatedImageProcessor.IsAnimated(document.Config))\n"
    "            {\n"
    "                var visible = await Task.Run(() => AnimatedImageProcessor.TransformAnimated(document.PngBytes, document.Config, frame => RawRgbaTransforms.Crop(frame, trim)));\n"
    "                var recovery = await Task.Run(() => AnimatedImageProcessor.TransformAnimated(document.RecoveryPngBytes ?? document.PngBytes, document.Config, frame => RawRgbaTransforms.Crop(frame, trim)));\n"
    "                if (ReferenceEquals(document, _controller.Document))\n"
    "                    _controller.CommitStorage(visible, config => ImageProcessor.RemapAuthoringForCrop(config, trim, oldWidth, oldHeight), recovery);\n"
    "            }\n"
    "            else\n"
    "            {\n"
    "                var png = await Task.Run(() => RawRgbaTransforms.Crop(document.PngBytes, trim));\n"
    "                if (ReferenceEquals(document, _controller.Document))\n"
    "                    _controller.CommitImage(png, config => ImageProcessor.RemapAuthoringForCrop(config, trim, oldWidth, oldHeight), recovery => RawRgbaTransforms.Crop(recovery, trim));\n"
    "            }\n"
    "        }\n"
    "        catch (Exception ex) { await Dialogs.ShowErrorAsync(this, \"Trim failed\", ex.Message); }\n"
    "    }\n\n"
)
replace_section(
    rel,
    "    private async Task ApplyBackgroundAsync()",
    "    private Task<byte[]> GenerateBackgroundPreviewAsync(CancellationToken cancellationToken)",
    "    private async Task ApplyBackgroundAsync()\n"
    "    {\n"
    "        _backgroundPreviewCancellation?.Cancel();\n"
    "        var cancellation = new CancellationTokenSource();\n"
    "        _backgroundPreviewCancellation = cancellation;\n"
    "        var document = _controller.Document;\n"
    "        try\n"
    "        {\n"
    "            _status.Text = \"Applying background removal…\";\n"
    "            if (AnimatedImageProcessor.IsAnimated(document.Config))\n"
    "            {\n"
    "                var color = _backgroundColor;\n"
    "                var tolerance = _backgroundTolerance;\n"
    "                var feather = _backgroundFeather;\n"
    "                var visible = await Task.Run(() => AnimatedImageProcessor.TransformAnimated(document.PngBytes, document.Config, frame => RawRgbaTransforms.RemoveBackground(frame, color, tolerance, feather)), cancellation.Token);\n"
    "                if (!ReferenceEquals(document, _controller.Document)) return;\n"
    "                _controller.CommitStorage(visible, recoveryStorage: _controller.GetRecoveryStorage());\n"
    "            }\n"
    "            else\n"
    "            {\n"
    "                var preview = _backgroundPreview ?? await GenerateBackgroundPreviewAsync(cancellation.Token);\n"
    "                if (!ReferenceEquals(document, _controller.Document)) return;\n"
    "                _controller.CommitImage(preview);\n"
    "            }\n"
    "            _backgroundPreview = null;\n"
    "            _editor.SetPreview(null);\n"
    "            _editor.Mode = EditorMode.Select;\n"
    "        }\n"
    "        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }\n"
    "        catch (Exception ex) { await Dialogs.ShowErrorAsync(this, \"Background removal failed\", ex.Message); }\n"
    "        finally\n"
    "        {\n"
    "            if (ReferenceEquals(_backgroundPreviewCancellation, cancellation)) _backgroundPreviewCancellation = null;\n"
    "            cancellation.Dispose();\n"
    "        }\n"
    "    }\n\n"
)
replace_section(
    rel,
    "    private Task<byte[]> GenerateBackgroundPreviewAsync(CancellationToken cancellationToken)",
    "    private void CancelBackground()",
    "    private Task<byte[]> GenerateBackgroundPreviewAsync(CancellationToken cancellationToken)\n"
    "    {\n"
    "        var png = _controller.GetFramePng(_editor.CurrentFrameIndex);\n"
    "        var color = _backgroundColor;\n"
    "        var tolerance = _backgroundTolerance;\n"
    "        var feather = _backgroundFeather;\n"
    "        return Task.Run(() =>\n"
    "        {\n"
    "            cancellationToken.ThrowIfCancellationRequested();\n"
    "            var result = RawRgbaTransforms.RemoveBackground(png, color, tolerance, feather);\n"
    "            cancellationToken.ThrowIfCancellationRequested();\n"
    "            return result;\n"
    "        }, cancellationToken);\n"
    "    }\n\n"
)
replace_section(
    rel,
    "    private void RefreshChrome()",
    "    private void SetPanels(Control left, Control right)",
    "    private void RefreshChrome()\n"
    "    {\n"
    "        var dirty = _controller.IsDirty ? \" *\" : string.Empty;\n"
    "        Title = $\"Gelatin 0.1.3 — {_controller.Document.Config.AssetName}{dirty}\";\n"
    "        var config = _controller.Document.Config;\n"
    "        var animation = config.Animation is { } animated ? $\"   |   {animated.Frames.Count} animated frame(s)\" : string.Empty;\n"
    "        _status.Text = $\"{config.Image.Width} × {config.Image.Height} px{animation}   |   {config.Cores.Count} core(s)   |   {config.RigidityStrokes.Count} rigidity stroke(s)   |   {(_controller.IsDirty ? \"Unsaved changes\" : Path.GetFileName(_controller.CurrentPath))}\";\n"
    "    }\n\n"
)

# -----------------------------------------------------------------------------
# LabControl: draw the timed atlas frame over the existing deforming mesh.
# -----------------------------------------------------------------------------
rel = "tools/gelatin/src/Gelatin.App/Controls/LabControl.cs"
replace_once(rel, "using Gelatin.Core.Imaging;", "using Gelatin.Core.Imaging;\nusing Gelatin.Core.Models;")
replace_once(
    rel,
    "    private SKImage? _texture;\n    private long _lastTicks;",
    "    private SKImage? _texture;\n    private AnimationConfig? _animation;\n    private double _animationElapsedMs;\n    private long _lastTicks;"
)
replace_once(
    rel,
    "            _simulation?.ResetToRest();\n            _dragging = false;",
    "            _simulation?.ResetToRest();\n            _animationElapsedMs = 0;\n            _dragging = false;"
)
replace_once(
    rel,
    "        LabSnapshot? snapshot;\n        lock (_simulationLock) snapshot = _simulation is null ? null : LabSnapshot.Capture(_simulation.Solver);",
    "        LabSnapshot? snapshot;\n"
    "        AnimationFrameConfig? frame = null;\n"
    "        lock (_simulationLock)\n"
    "        {\n"
    "            snapshot = _simulation is null ? null : LabSnapshot.Capture(_simulation.Solver);\n"
    "            if (_animation is { Frames.Count: > 0 }) frame = _animation.Frames[AnimatedImageProcessor.FrameIndexAtTime(_animation, _animationElapsedMs)];\n"
    "        }"
)
replace_once(
    rel,
    "        context.Custom(new LabDrawOperation(Bounds, snapshot, _texture, new Diagnostics(ShowMesh, ShowCores, ShowHeatmap, ShowRigidity, ShowContour, ShowVelocity)));",
    "        context.Custom(new LabDrawOperation(Bounds, snapshot, _texture, frame, new Diagnostics(ShowMesh, ShowCores, ShowHeatmap, ShowRigidity, ShowContour, ShowVelocity)));"
)
replace_once(
    rel,
    "                    return new LabBuildResult(new FixedStepSimulation(solver, quality), texture);",
    "                    return new LabBuildResult(new FixedStepSimulation(solver, quality), texture, document.Config.Animation?.DeepClone());"
)
replace_once(
    rel,
    "            _texture = result.Texture;\n            previousTexture?.Dispose();\n            _lastTicks = _clock.ElapsedTicks;",
    "            _texture = result.Texture;\n"
    "            _animation = result.Animation;\n"
    "            _animationElapsedMs = 0;\n"
    "            previousTexture?.Dispose();\n"
    "            _lastTicks = _clock.ElapsedTicks;"
)
replace_once(
    rel,
    "                lock (_simulationLock) _simulation?.Advance(elapsed);",
    "                lock (_simulationLock)\n"
    "                {\n"
    "                    if (_simulation is not null)\n"
    "                    {\n"
    "                        var paused = _simulation.Paused;\n"
    "                        var speed = _simulation.Speed;\n"
    "                        _simulation.Advance(elapsed);\n"
    "                        if (!paused) _animationElapsedMs += elapsed * 1000 * Math.Clamp(speed, 0.1, 1);\n"
    "                    }\n"
    "                }"
)
replace_once(
    rel,
    "    private sealed record LabBuildResult(FixedStepSimulation Simulation, SKImage Texture);",
    "    private sealed record LabBuildResult(FixedStepSimulation Simulation, SKImage Texture, AnimationConfig? Animation);"
)
replace_once(
    rel,
    "        private readonly SKImage _texture;\n        private readonly Diagnostics _diagnostics;",
    "        private readonly SKImage _texture;\n        private readonly AnimationFrameConfig? _frame;\n        private readonly Diagnostics _diagnostics;"
)
replace_once(
    rel,
    "        public LabDrawOperation(Rect bounds, LabSnapshot snapshot, SKImage texture, Diagnostics diagnostics)\n        {\n            Bounds = bounds;\n            _snapshot = snapshot;\n            _texture = texture;\n            _diagnostics = diagnostics;\n        }",
    "        public LabDrawOperation(Rect bounds, LabSnapshot snapshot, SKImage texture, AnimationFrameConfig? frame, Diagnostics diagnostics)\n"
    "        {\n"
    "            Bounds = bounds;\n"
    "            _snapshot = snapshot;\n"
    "            _texture = texture;\n"
    "            _frame = frame;\n"
    "            _diagnostics = diagnostics;\n"
    "        }"
)
replace_once(
    rel,
    "            var tex = _snapshot.Uvs.Select(uv => new SKPoint(uv.X * _texture.Width, uv.Y * _texture.Height)).ToArray();",
    "            var tex = _frame is null\n"
    "                ? _snapshot.Uvs.Select(uv => new SKPoint(uv.X * _texture.Width, uv.Y * _texture.Height)).ToArray()\n"
    "                : _snapshot.Uvs.Select(uv => new SKPoint(_frame.X + uv.X * _frame.Width, _frame.Y + uv.Y * _frame.Height)).ToArray();"
)

# -----------------------------------------------------------------------------
# Version/package/docs/schema.
# -----------------------------------------------------------------------------
rel = "tools/gelatin/src/Gelatin.App/Gelatin.App.csproj"
write(rel, read(rel).replace("<Version>0.1.2</Version>", "<Version>0.1.3</Version>"))

rel = "tools/gelatin/scripts/publish.ps1"
write(rel, read(rel).replace("gelatin-0.1.2-win-x64.zip", "gelatin-0.1.3-win-x64.zip"))

rel = ".github/workflows/gelatin.yml"
text = read(rel).replace("gelatin-0.1.2-win-x64.zip", "gelatin-0.1.3-win-x64.zip").replace("gelatin-0.1.2-win-x64", "gelatin-0.1.3-win-x64")
write(rel, text)

rel = "tools/gelatin/README.md"
text = read(rel)
text = text.replace("# Gelatin 0.1.2", "# Gelatin 0.1.3")
text = text.replace("gelatin-0.1.2-win-x64", "gelatin-0.1.3-win-x64")
text = text.replace("Open or drag/drop PNG, JPEG, WebP, and `.gel` files.", "Open or drag/drop PNG, JPEG, WebP, animated GIF, and `.gel` files. Animated GIF imports preserve per-frame delays and repetition semantics, decode into full RGBA frames, and are stored as a single PNG atlas plus timing metadata in GEL1 schema v2.")
text = text.replace("The canvas has a transparency checkerboard.", "Animated assets play automatically in the Asset and Gel workspaces with their preserved timing. Image edits apply to every frame, and transparent trimming uses the union of visible pixels across all frames so the asset never shifts between frames.\n\nThe canvas has a transparency checkerboard.")
text = text.replace("The Lab runs the same UI-independent XPBD solver used to interpret the saved material and core configuration. The PNG is texture-mapped over the live triangulated mesh; it is not a scaled rectangle animation.", "The Lab runs the same UI-independent XPBD solver used to interpret the saved material and core configuration. The PNG is texture-mapped over the live triangulated mesh; it is not a scaled rectangle animation. Animated assets select the correctly timed atlas frame while that same texture is deformed by the mesh, so animation and gel physics run together.")
old = "Gelatin 0.1.2 keeps the `GEL1` container and `schemaVersion: 1` unchanged and reads 0.1.0/0.1.1-compatible GEL1 files without migration. The recovery source is never serialized. The loader rejects incorrect magic, unsafe or impossible lengths, truncation, trailing bytes, invalid UTF-8/JSON, unsupported schema versions, invalid PNG data, and dimension mismatches. Saves are atomic. The complete JSON schema is in `gel.schema.json`."
new = "Gelatin 0.1.3 keeps the `GEL1` binary container unchanged. Static assets remain `schemaVersion: 1`; animated assets use `schemaVersion: 2`, where the embedded PNG is a texture atlas and JSON stores each logical frame rectangle, exact source delay in milliseconds, and repetition count (`-1` means infinite). Gelatin continues to read 0.1.0/0.1.1/0.1.2 static GEL1 files without migration. Gello therefore only needs PNG-atlas sampling and timing logic; it never needs a GIF decoder. The recovery source is never serialized. The loader rejects incorrect magic, unsafe or impossible lengths, truncation, trailing bytes, invalid UTF-8/JSON, unsupported schema versions, invalid PNG data, invalid animation metadata, atlas rectangles outside the PNG, and dimension mismatches. Saves are atomic. The complete JSON schema is in `gel.schema.json`."
if old not in text:
    raise RuntimeError("README GEL1 compatibility paragraph not found")
text = text.replace(old, new)
write(rel, text)

schema = r'''{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://example.invalid/gelatin/gel-v1.schema.json",
  "title": "Gelatin GEL1 configuration",
  "type": "object",
  "additionalProperties": false,
  "required": ["schemaVersion", "assetName", "image", "material", "cores", "rigidityStrokes", "authoring"],
  "properties": {
    "schemaVersion": { "enum": [1, 2] },
    "assetName": { "type": "string", "minLength": 1, "maxLength": 256 },
    "image": {
      "type": "object",
      "additionalProperties": false,
      "required": ["width", "height", "alphaThreshold"],
      "properties": {
        "width": { "type": "integer", "minimum": 1, "maximum": 32768 },
        "height": { "type": "integer", "minimum": 1, "maximum": 32768 },
        "alphaThreshold": { "type": "number", "minimum": 0.0, "maximum": 1.0 }
      }
    },
    "animation": {
      "type": "object",
      "additionalProperties": false,
      "required": ["repetitionCount", "frames"],
      "properties": {
        "repetitionCount": { "type": "integer", "minimum": -1, "maximum": 1000000 },
        "frames": {
          "type": "array",
          "minItems": 2,
          "maxItems": 512,
          "items": {
            "type": "object",
            "additionalProperties": false,
            "required": ["x", "y", "width", "height", "durationMs"],
            "properties": {
              "x": { "type": "integer", "minimum": 0, "maximum": 32767 },
              "y": { "type": "integer", "minimum": 0, "maximum": 32767 },
              "width": { "type": "integer", "minimum": 1, "maximum": 32768 },
              "height": { "type": "integer", "minimum": 1, "maximum": 32768 },
              "durationMs": { "type": "integer", "minimum": 0, "maximum": 600000 }
            }
          }
        }
      }
    },
    "material": {
      "type": "object",
      "additionalProperties": false,
      "required": ["softness", "damping", "areaPreservation", "shapeMemory", "bendResistance", "maxStretch", "selfCollision", "selfCollisionThickness"],
      "properties": {
        "softness": { "type": "number", "minimum": 0.0, "maximum": 1.0 },
        "damping": { "type": "number", "minimum": 0.0, "maximum": 1.0 },
        "areaPreservation": { "type": "number", "minimum": 0.0, "maximum": 1.0 },
        "shapeMemory": { "type": "number", "minimum": 0.0, "maximum": 1.0 },
        "bendResistance": { "type": "number", "minimum": 0.0, "maximum": 1.0 },
        "maxStretch": { "type": "number", "minimum": 1.05, "maximum": 3.0 },
        "selfCollision": { "type": "boolean" },
        "selfCollisionThickness": { "type": "number", "minimum": 0.0001, "maximum": 0.1 }
      }
    },
    "cores": {
      "type": "array",
      "maxItems": 128,
      "items": {
        "type": "object",
        "additionalProperties": false,
        "required": ["id", "name", "x", "y", "radiusX", "radiusY", "mass", "coupling", "damping", "softnessMultiplier", "falloff"],
        "properties": {
          "id": { "type": "integer", "minimum": 1 },
          "name": { "type": "string", "maxLength": 128 },
          "x": { "type": "number", "minimum": -1.0, "maximum": 2.0 },
          "y": { "type": "number", "minimum": -1.0, "maximum": 2.0 },
          "radiusX": { "type": "number", "exclusiveMinimum": 0.0, "maximum": 2.0 },
          "radiusY": { "type": "number", "exclusiveMinimum": 0.0, "maximum": 2.0 },
          "mass": { "type": "number", "minimum": 0.1, "maximum": 20.0 },
          "coupling": { "type": "number", "minimum": 0.0, "maximum": 1.0 },
          "damping": { "type": "number", "minimum": 0.0, "maximum": 1.0 },
          "softnessMultiplier": { "type": "number", "minimum": 0.1, "maximum": 4.0 },
          "falloff": { "type": "number", "minimum": 0.0, "maximum": 1.0 }
        }
      }
    },
    "rigidityStrokes": {
      "type": "array",
      "maxItems": 8192,
      "items": {
        "type": "object",
        "additionalProperties": false,
        "required": ["radius", "strength", "points"],
        "properties": {
          "radius": { "type": "number", "exclusiveMinimum": 0.0, "maximum": 1.0 },
          "strength": { "type": "number", "minimum": 0.0, "maximum": 1.0 },
          "points": {
            "type": "array",
            "minItems": 1,
            "maxItems": 8192,
            "items": {
              "type": "array",
              "prefixItems": [
                { "type": "number", "minimum": -1.0, "maximum": 2.0 },
                { "type": "number", "minimum": -1.0, "maximum": 2.0 }
              ],
              "items": false,
              "minItems": 2,
              "maxItems": 2
            }
          }
        }
      }
    },
    "authoring": {
      "type": "object",
      "additionalProperties": false,
      "required": ["tool", "toolVersion"],
      "properties": {
        "tool": { "const": "Gelatin" },
        "toolVersion": { "type": "string", "minLength": 1, "maxLength": 64 }
      }
    }
  },
  "allOf": [
    {
      "if": { "properties": { "schemaVersion": { "const": 1 } }, "required": ["schemaVersion"] },
      "then": { "not": { "required": ["animation"] } }
    },
    {
      "if": { "properties": { "schemaVersion": { "const": 2 } }, "required": ["schemaVersion"] },
      "then": { "required": ["animation"] }
    }
  ]
}
'''
write("tools/gelatin/gel.schema.json", schema)

# More animation regressions: decoded frames are distinct; all-frame transforms retain timing.
rel = "tools/gelatin/tests/Gelatin.Tests/AnimatedImageTests.cs"
text = read(rel)
needle = "\n    [Fact]\n    public void AnimatedGelRoundTripsThroughGel1Container()"
extra = '''
    [Fact]
    public void GifImportDecodesDistinctCompositedFrames()
    {
        var result = AnimatedImageProcessor.ImportGif(Convert.FromBase64String(TwoFrameGifBase64));
        var config = new GelConfig
        {
            SchemaVersion = 2,
            Image = new ImageConfig { Width = 2, Height = 2 },
            Animation = result.Animation,
            Cores = []
        };
        var first = RawRgbaCodec.Decode(AnimatedImageProcessor.GetFramePng(result.PngBytes, config, 0));
        var second = RawRgbaCodec.Decode(AnimatedImageProcessor.GetFramePng(result.PngBytes, config, 1));

        Assert.Equal(255, first.Pixels[0]);
        Assert.Equal(0, first.Pixels[1]);
        Assert.Equal(0, second.Pixels[0]);
        Assert.Equal(255, second.Pixels[1]);
    }

    [Fact]
    public void AnimatedTransformTouchesEveryFrameAndPreservesTiming()
    {
        var result = AnimatedImageProcessor.ImportGif(Convert.FromBase64String(TwoFrameGifBase64));
        var config = new GelConfig
        {
            SchemaVersion = 2,
            Image = new ImageConfig { Width = 2, Height = 2 },
            Animation = result.Animation,
            Cores = []
        };

        var resized = AnimatedImageProcessor.TransformAnimated(result.PngBytes, config, frame => RawRgbaTransforms.Resize(frame, 4, 3));

        Assert.Equal((4, 3), (resized.Width, resized.Height));
        Assert.Equal([50, 120], resized.Animation!.Frames.Select(frame => frame.DurationMs).ToArray());
        Assert.All(resized.Animation.Frames, frame => Assert.Equal((4, 3), (frame.Width, frame.Height)));
    }
'''
if needle not in text:
    raise RuntimeError("AnimatedImageTests insertion point not found")
write(rel, text.replace(needle, "\n" + extra + needle, 1))

print("Gelatin animated editor/Lab integration patch applied successfully.")
