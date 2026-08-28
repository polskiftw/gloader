from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def read(rel):
    return (ROOT / rel).read_text(encoding="utf-8")


def write(rel, text):
    path = ROOT / rel
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8", newline="\n")


def replace_once(rel, old, new):
    text = read(rel)
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{rel}: expected exactly one occurrence, found {count}: {old[:140]!r}")
    write(rel, text.replace(old, new, 1))


def insert_before(rel, marker, addition):
    text = read(rel)
    if addition.strip() in text:
        return
    index = text.find(marker)
    if index < 0:
        raise RuntimeError(f"{rel}: marker not found: {marker!r}")
    write(rel, text[:index] + addition + text[index:])


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
# Core: selected-frame transforms, playback offsets, and scoped alpha brushing.
# -----------------------------------------------------------------------------
rel = "tools/gelatin/src/Gelatin.Core/Imaging/AnimatedImageProcessor.cs"
insert_before(
    rel,
    "    public static PixelRect? FindUnionTrimBounds",
    '''    public static ImageStorageResult TransformFrame(ReadOnlySpan<byte> atlasPng, GelConfig config, int frameIndex, Func<byte[], byte[]> transform)\n    {\n        ArgumentNullException.ThrowIfNull(transform);\n        if (!IsAnimated(config)) throw new GelFormatException("The requested operation requires an animated GEL asset.");\n        var animation = config.Animation!;\n        if (frameIndex < 0 || frameIndex >= animation.Frames.Count) throw new ArgumentOutOfRangeException(nameof(frameIndex));\n        var frames = ExtractFrames(atlasPng, config);\n        frames[frameIndex] = transform(frames[frameIndex]);\n        return PackFrames(frames, animation.Frames.Select(frame => frame.DurationMs).ToArray(), animation.RepetitionCount);\n    }\n\n''')
insert_before(
    rel,
    "    public static int FrameIndexAtTime",
    '''    public static long FrameStartTimeMilliseconds(AnimationConfig? animation, int frameIndex)\n    {\n        if (animation is null || animation.Frames.Count == 0) return 0;\n        frameIndex = Math.Clamp(frameIndex, 0, animation.Frames.Count - 1);\n        long start = 0;\n        for (var index = 0; index < frameIndex; index++)\n            start = checked(start + EffectiveDuration(animation.Frames[index].DurationMs));\n        return start;\n    }\n\n''')
replace_once(
    rel,
    "    private readonly AnimationConfig _animation;\n    private bool _disposed;",
    "    private readonly AnimationConfig _animation;\n    private readonly int? _targetFrameIndex;\n    private bool _disposed;"
)
replace_once(
    rel,
    "    public AnimationAlphaBrushSession(ReadOnlySpan<byte> atlasPng, ReadOnlySpan<byte> recoveryAtlasPng, GelConfig config, AlphaBrushMode mode, double size)",
    "    public AnimationAlphaBrushSession(ReadOnlySpan<byte> atlasPng, ReadOnlySpan<byte> recoveryAtlasPng, GelConfig config, AlphaBrushMode mode, double size, int? targetFrameIndex = null)"
)
replace_once(
    rel,
    "        _animation = config.Animation!.DeepClone();\n        var current = AnimatedImageProcessor.ExtractFrames(atlasPng, config);",
    "        _animation = config.Animation!.DeepClone();\n        if (targetFrameIndex is int target && (target < 0 || target >= _animation.Frames.Count)) throw new ArgumentOutOfRangeException(nameof(targetFrameIndex));\n        _targetFrameIndex = targetFrameIndex;\n        var current = AnimatedImageProcessor.ExtractFrames(atlasPng, config);"
)
replace_once(
    rel,
    "    public void ApplyPoint(PixelPoint point)\n    {\n        ThrowIfDisposed();\n        foreach (var frame in _frames) frame.ApplyPoint(point);\n    }",
    "    public void ApplyPoint(PixelPoint point)\n    {\n        ThrowIfDisposed();\n        foreach (var frame in TargetFrames()) frame.ApplyPoint(point);\n    }"
)
replace_once(
    rel,
    "    public void ApplySegment(PixelPoint start, PixelPoint end)\n    {\n        ThrowIfDisposed();\n        foreach (var frame in _frames) frame.ApplySegment(start, end);\n    }",
    "    public void ApplySegment(PixelPoint start, PixelPoint end)\n    {\n        ThrowIfDisposed();\n        foreach (var frame in TargetFrames()) frame.ApplySegment(start, end);\n    }"
)
insert_before(
    rel,
    "    public void Dispose()",
    '''    private IEnumerable<AlphaBrushSession> TargetFrames()\n    {\n        if (_targetFrameIndex is int target)\n        {\n            yield return _frames[target];\n            yield break;\n        }\n        foreach (var frame in _frames) yield return frame;\n    }\n\n''')

# -----------------------------------------------------------------------------
# Image alpha editing: crop-as-mask for one animation frame.
# -----------------------------------------------------------------------------
rel = "tools/gelatin/src/Gelatin.Core/Imaging/ImageEditing.cs"
insert_before(
    rel,
    "    public static byte[] ApplyPolygonCutout",
    '''    public static byte[] ApplyRectCutout(ReadOnlySpan<byte> png, PixelRect keep)\n    {\n        var source = RawRgbaCodec.Decode(png);\n        if (keep.Width < 1 || keep.Height < 1 || keep.X < 0 || keep.Y < 0 || keep.Right > source.Width || keep.Bottom > source.Height)\n            throw new GelFormatException("The crop rectangle must be inside the current image.");\n        for (var y = 0; y < source.Height; y++)\n        for (var x = 0; x < source.Width; x++)\n        {\n            if (x >= keep.X && x < keep.Right && y >= keep.Y && y < keep.Bottom) continue;\n            source.Pixels[(y * source.Width + x) * 4 + 3] = 0;\n        }\n        return RawRgbaCodec.Encode(source.Width, source.Height, source.Pixels);\n    }\n\n''')

# -----------------------------------------------------------------------------
# Editor canvas: manual frame transport + current-frame alpha brush targeting.
# -----------------------------------------------------------------------------
rel = "tools/gelatin/src/Gelatin.App/Controls/EditorCanvas.cs"
replace_once(
    rel,
    "    private readonly Stopwatch _animationClock = Stopwatch.StartNew();\n    private CancellationTokenSource? _bitmapCancellation;",
    "    private readonly Stopwatch _animationClock = Stopwatch.StartNew();\n    private bool _animationPlaying = true;\n    private int _manualFrameIndex;\n    private double _animationBaseMs;\n    private CancellationTokenSource? _bitmapCancellation;"
)
replace_once(
    rel,
    '''    public double Zoom => _zoom;\n    public Point Pan => _pan;\n    public int CurrentFrameIndex => AnimatedImageProcessor.IsAnimated(_controller.Document.Config)\n        ? AnimatedImageProcessor.FrameIndexAtTime(_controller.Document.Config.Animation, _animationClock.Elapsed.TotalMilliseconds)\n        : 0;''',
    '''    public double Zoom => _zoom;\n    public Point Pan => _pan;\n    public bool EditCurrentAnimationFrameOnly { get; set; }\n    public bool AnimationPlaying => _animationPlaying;\n    public int CurrentFrameIndex\n    {\n        get\n        {\n            var config = _controller.Document.Config;\n            if (!AnimatedImageProcessor.IsAnimated(config) || config.Animation is not { Frames.Count: > 0 } animation) return 0;\n            if (!_animationPlaying) return Math.Clamp(_manualFrameIndex, 0, animation.Frames.Count - 1);\n            return AnimatedImageProcessor.FrameIndexAtTime(animation, _animationBaseMs + _animationClock.Elapsed.TotalMilliseconds);\n        }\n    }'''
)
replace_once(
    rel,
    "    public event Action<string>? ImageError;\n    public event Action<string>? EditorError;",
    "    public event Action<string>? ImageError;\n    public event Action<string>? EditorError;\n    public event Action<int>? AnimationFrameChanged;\n    public event Action? AnimationPlaybackChanged;"
)
insert_before(
    rel,
    "    public void BeginPolygonCutout()",
    '''    public void ResetAnimationPlayback()\n    {\n        _manualFrameIndex = 0;\n        _animationBaseMs = 0;\n        _animationPlaying = true;\n        _animationClock.Restart();\n        AnimationPlaybackChanged?.Invoke();\n        Reload();\n    }\n\n    public void SetAnimationPlaying(bool playing)\n    {\n        var animation = _controller.Document.Config.Animation;\n        if (!AnimatedImageProcessor.IsAnimated(_controller.Document.Config) || animation is null) return;\n        if (_animationPlaying == playing) return;\n        if (!playing)\n        {\n            _manualFrameIndex = CurrentFrameIndex;\n            _animationPlaying = false;\n            _animationBaseMs = 0;\n            _animationClock.Reset();\n        }\n        else\n        {\n            _manualFrameIndex = Math.Clamp(_manualFrameIndex, 0, animation.Frames.Count - 1);\n            _animationBaseMs = AnimatedImageProcessor.FrameStartTimeMilliseconds(animation, _manualFrameIndex);\n            _animationPlaying = true;\n            _animationClock.Restart();\n        }\n        AnimationPlaybackChanged?.Invoke();\n        Reload();\n    }\n\n    public void SetAnimationFrame(int frameIndex)\n    {\n        var animation = _controller.Document.Config.Animation;\n        if (!AnimatedImageProcessor.IsAnimated(_controller.Document.Config) || animation is null) return;\n        var wasPlaying = _animationPlaying;\n        _animationPlaying = false;\n        _animationBaseMs = 0;\n        _animationClock.Reset();\n        _manualFrameIndex = Math.Clamp(frameIndex, 0, animation.Frames.Count - 1);\n        if (wasPlaying) AnimationPlaybackChanged?.Invoke();\n        Reload();\n    }\n\n    public void StepAnimation(int delta)\n    {\n        var animation = _controller.Document.Config.Animation;\n        if (!AnimatedImageProcessor.IsAnimated(_controller.Document.Config) || animation is null || animation.Frames.Count == 0) return;\n        var current = CurrentFrameIndex;\n        var next = ((current + delta) % animation.Frames.Count + animation.Frames.Count) % animation.Frames.Count;\n        SetAnimationFrame(next);\n    }\n\n''')
replace_once(
    rel,
    '''        if (changedDocument)\n        {\n            _bitmapDocument = document;\n            _loadedFrameIndex = -1;\n            _animationClock.Restart();\n        }''',
    '''        if (changedDocument)\n        {\n            _bitmapDocument = document;\n            _loadedFrameIndex = -1;\n            var frameCount = document.Config.Animation?.Frames.Count ?? 1;\n            if (_animationPlaying)\n            {\n                _manualFrameIndex = 0;\n                _animationBaseMs = 0;\n                _animationClock.Restart();\n            }\n            else\n            {\n                _manualFrameIndex = Math.Clamp(_manualFrameIndex, 0, Math.Max(0, frameCount - 1));\n                _animationBaseMs = 0;\n                _animationClock.Reset();\n            }\n        }'''
)
replace_once(
    rel,
    "        _loadedFrameIndex = frameIndex;\n        _bitmapCancellation?.Cancel();",
    "        _loadedFrameIndex = frameIndex;\n        AnimationFrameChanged?.Invoke(frameIndex);\n        _bitmapCancellation?.Cancel();"
)
replace_once(
    rel,
    "                _animatedAlphaBrush = new AnimationAlphaBrushSession(_controller.Document.PngBytes, _controller.RecoveryPngBytes, _controller.Document.Config, mode, _alphaBrushSize);",
    "                var targetFrame = EditCurrentAnimationFrameOnly ? CurrentFrameIndex : (int?)null;\n                _animatedAlphaBrush = new AnimationAlphaBrushSession(_controller.Document.PngBytes, _controller.RecoveryPngBytes, _controller.Document.Config, mode, _alphaBrushSize, targetFrame);"
)

# -----------------------------------------------------------------------------
# Main window: animation transport, edit scope, and scoped frame operations.
# -----------------------------------------------------------------------------
rel = "tools/gelatin/src/Gelatin.App/MainWindow.cs"
replace_once(
    rel,
    "    private Button? _polygonApplyButton;",
    "    private Button? _polygonApplyButton;\n    private Button? _animationPlayButton;\n    private NumericUpDown? _animationFrameNumber;\n    private bool _syncAnimationControls;"
)
replace_once(
    rel,
    "        _editor.PixelPicked += (x, y) => { _backgroundColor = RawRgbaTransforms.Sample(_controller.GetFramePng(_editor.CurrentFrameIndex), x, y); UpdateBackgroundPreview(); };",
    "        _editor.PixelPicked += (x, y) => { _backgroundColor = RawRgbaTransforms.Sample(_controller.GetFramePng(_editor.CurrentFrameIndex), x, y); UpdateBackgroundPreview(); };\n        _editor.AnimationFrameChanged += _ => RefreshAnimationControls();\n        _editor.AnimationPlaybackChanged += RefreshAnimationControls;"
)
replace_once(
    rel,
    "        var left = SectionStack(\"IMAGE PREP\");\n        left.Children.Add(ActionButton(\"Open image / .gel\", async () => await OpenPickerAsync(), wide: true));",
    "        var left = SectionStack(\"IMAGE PREP\");\n        _animationPlayButton = null;\n        _animationFrameNumber = null;\n        left.Children.Add(ActionButton(\"Open image / .gel\", async () => await OpenPickerAsync(), wide: true));"
)
replace_once(
    rel,
    '''            var repeat = animation.RepetitionCount < 0 ? "loops forever" : animation.RepetitionCount == 0 ? "plays once" : $"repeats {animation.RepetitionCount} time(s)";\n            var cycleMs = animation.Frames.Sum(frame => AnimatedImageProcessor.EffectiveDuration(frame.DurationMs));\n            left.Children.Add(new TextBlock { Text = $"Animated GIF: {animation.Frames.Count} frames • {cycleMs} ms/cycle • {repeat}", TextWrapping = TextWrapping.Wrap, Foreground = MutedBrush(), FontSize = 11 });''',
    '''            var repeat = animation.RepetitionCount < 0 ? "loops forever" : animation.RepetitionCount == 0 ? "plays once" : $"repeats {animation.RepetitionCount} time(s)";\n            var cycleMs = animation.Frames.Sum(frame => AnimatedImageProcessor.EffectiveDuration(frame.DurationMs));\n            left.Children.Add(new TextBlock { Text = $"Animated GIF: {animation.Frames.Count} frames • {cycleMs} ms/cycle • {repeat}", TextWrapping = TextWrapping.Wrap, Foreground = MutedBrush(), FontSize = 11 });\n            AddAnimationControls(left, animation);'''
)
insert_before(
    rel,
    "    private void BuildGelPanels()",
    '''    private void AddAnimationControls(StackPanel panel, AnimationConfig animation)\n    {\n        panel.Children.Add(Header("Animation frames"));\n        var transport = Row();\n        transport.Children.Add(ActionButton("◀ Prev", () => { ClearBackgroundPreview(); _editor.StepAnimation(-1); }));\n        _animationPlayButton = ActionButton(_editor.AnimationPlaying ? "Pause" : "Play", () =>\n        {\n            ClearBackgroundPreview();\n            _editor.SetAnimationPlaying(!_editor.AnimationPlaying);\n            RefreshAnimationControls();\n        });\n        transport.Children.Add(_animationPlayButton);\n        transport.Children.Add(ActionButton("Next ▶", () => { ClearBackgroundPreview(); _editor.StepAnimation(1); }));\n        panel.Children.Add(transport);\n\n        _animationFrameNumber = Number(_editor.CurrentFrameIndex + 1, 1, animation.Frames.Count, 1, "0");\n        _animationFrameNumber.ValueChanged += (_, _) =>\n        {\n            if (_syncAnimationControls) return;\n            ClearBackgroundPreview();\n            _editor.SetAnimationFrame((int)(_animationFrameNumber.Value ?? 1) - 1);\n        };\n        panel.Children.Add(Labeled("Frame", _animationFrameNumber));\n\n        var scope = new ComboBox\n        {\n            ItemsSource = new[] { "All frames", "Current frame" },\n            SelectedIndex = _editor.EditCurrentAnimationFrameOnly ? 1 : 0,\n            HorizontalAlignment = HorizontalAlignment.Stretch\n        };\n        scope.SelectionChanged += (_, _) =>\n        {\n            _editor.EditCurrentAnimationFrameOnly = scope.SelectedIndex == 1;\n            if (_editor.EditCurrentAnimationFrameOnly) _editor.SetAnimationPlaying(false);\n            RefreshAnimationControls();\n        };\n        panel.Children.Add(Labeled("Apply edits to", scope));\n        panel.Children.Add(new TextBlock\n        {\n            Text = "Current-frame crop/cutout masks only that frame and keeps the shared canvas aligned. Resize and Trim transparent edges remain animation-wide; Trim uses the union of every frame.",\n            TextWrapping = TextWrapping.Wrap,\n            Foreground = MutedBrush(),\n            FontSize = 11\n        });\n        RefreshAnimationControls();\n    }\n\n    private void RefreshAnimationControls()\n    {\n        if (_animationPlayButton is null || _animationFrameNumber is null) return;\n        var animation = _controller.Document.Config.Animation;\n        if (animation is null || animation.Frames.Count == 0) return;\n        _syncAnimationControls = true;\n        try\n        {\n            _animationPlayButton.Content = _editor.AnimationPlaying ? "Pause" : "Play";\n            _animationFrameNumber.Maximum = animation.Frames.Count;\n            _animationFrameNumber.Value = _editor.CurrentFrameIndex + 1;\n        }\n        finally { _syncAnimationControls = false; }\n    }\n\n''')

replace_section(
    rel,
    "    private async Task ApplyCropAsync()",
    "    private async Task ApplyPolygonCutoutAsync()",
    '''    private async Task ApplyCropAsync()\n    {\n        if (_editor.CropRect is not ImagePixelRect crop) return;\n        var document = _controller.Document;\n        var oldWidth = document.Config.Image.Width;\n        var oldHeight = document.Config.Image.Height;\n        try\n        {\n            if (AnimatedImageProcessor.IsAnimated(document.Config) && _editor.EditCurrentAnimationFrameOnly)\n            {\n                var frameIndex = _editor.CurrentFrameIndex;\n                _status.Text = $"Masking frame {frameIndex + 1} outside crop…";\n                var visible = await Task.Run(() => AnimatedImageProcessor.TransformFrame(document.PngBytes, document.Config, frameIndex, frame => ImageAlphaEditing.ApplyRectCutout(frame, crop)));\n                if (!ReferenceEquals(document, _controller.Document)) return;\n                _controller.CommitStorage(visible, recoveryStorage: _controller.GetRecoveryStorage());\n                _status.Text = $"Frame {frameIndex + 1}: pixels outside the crop are transparent; animation canvas unchanged.";\n            }\n            else\n            {\n                _status.Text = "Cropping image…";\n                if (AnimatedImageProcessor.IsAnimated(document.Config))\n                {\n                    var visible = await Task.Run(() => AnimatedImageProcessor.TransformAnimated(document.PngBytes, document.Config, frame => RawRgbaTransforms.Crop(frame, crop)));\n                    var recovery = await Task.Run(() => AnimatedImageProcessor.TransformAnimated(document.RecoveryPngBytes ?? document.PngBytes, document.Config, frame => RawRgbaTransforms.Crop(frame, crop)));\n                    if (!ReferenceEquals(document, _controller.Document)) return;\n                    _controller.CommitStorage(visible, config => ImageProcessor.RemapAuthoringForCrop(config, crop, oldWidth, oldHeight), recovery);\n                }\n                else\n                {\n                    var png = await Task.Run(() => RawRgbaTransforms.Crop(document.PngBytes, crop));\n                    if (!ReferenceEquals(document, _controller.Document)) return;\n                    _controller.CommitImage(png,\n                        config => ImageProcessor.RemapAuthoringForCrop(config, crop, oldWidth, oldHeight),\n                        recovery => RawRgbaTransforms.Crop(recovery, crop));\n                }\n            }\n            _editor.CancelCrop();\n            _editor.Mode = EditorMode.Select;\n        }\n        catch (Exception ex) { await Dialogs.ShowErrorAsync(this, "Crop failed", ex.Message); }\n    }\n\n''')

replace_section(
    rel,
    "    private async Task ApplyPolygonCutoutAsync()",
    "    private async Task ResizeImageAsync(int width, int height)",
    '''    private async Task ApplyPolygonCutoutAsync()\n    {\n        if (!_editor.PolygonClosed)\n        {\n            _status.Text = "Close the polygon before applying the cutout.";\n            return;\n        }\n        var polygon = _editor.GetPolygonSnapshot();\n        var validation = PolygonGeometry.Validate(polygon);\n        if (!validation.IsValid)\n        {\n            _status.Text = validation.Error!;\n            return;\n        }\n\n        var document = _controller.Document;\n        var oldWidth = document.Config.Image.Width;\n        var oldHeight = document.Config.Image.Height;\n        try\n        {\n            if (AnimatedImageProcessor.IsAnimated(document.Config) && _editor.EditCurrentAnimationFrameOnly)\n            {\n                var frameIndex = _editor.CurrentFrameIndex;\n                _status.Text = $"Applying polygon cutout to frame {frameIndex + 1}…";\n                var visible = await Task.Run(() => AnimatedImageProcessor.TransformFrame(document.PngBytes, document.Config, frameIndex, frame => ImageAlphaEditing.ApplyPolygonCutout(frame, polygon)));\n                if (!ReferenceEquals(document, _controller.Document)) return;\n                _controller.CommitStorage(visible, recoveryStorage: _controller.GetRecoveryStorage());\n                _status.Text = $"Frame {frameIndex + 1}: polygon mask applied; animation canvas unchanged.";\n            }\n            else if (AnimatedImageProcessor.IsAnimated(document.Config))\n            {\n                _status.Text = "Applying polygon cutout and trimming transparent margins…";\n                var result = await Task.Run(() =>\n                {\n                    var masked = AnimatedImageProcessor.TransformAnimated(document.PngBytes, document.Config, frame => ImageAlphaEditing.ApplyPolygonCutout(frame, polygon));\n                    var maskedConfig = document.Config.DeepClone();\n                    maskedConfig.Animation = masked.Animation?.DeepClone();\n                    var bounds = AnimatedImageProcessor.FindUnionTrimBounds(masked.PngBytes, maskedConfig, 0);\n                    if (bounds is null) return (Bounds: (ImagePixelRect?)null, Visible: (ImageStorageResult?)null, Recovery: (ImageStorageResult?)null);\n                    var visible = AnimatedImageProcessor.TransformAnimated(masked.PngBytes, maskedConfig, frame => RawRgbaTransforms.Crop(frame, bounds.Value));\n                    var recovery = AnimatedImageProcessor.TransformAnimated(document.RecoveryPngBytes ?? document.PngBytes, document.Config, frame => RawRgbaTransforms.Crop(frame, bounds.Value));\n                    return (Bounds: (ImagePixelRect?)bounds.Value, Visible: (ImageStorageResult?)visible, Recovery: (ImageStorageResult?)recovery);\n                });\n                if (!ReferenceEquals(document, _controller.Document)) return;\n                if (result.Bounds is not ImagePixelRect bounds || result.Visible is null || result.Recovery is null)\n                {\n                    _status.Text = "The polygon cutout would make every animation frame completely transparent; nothing was changed.";\n                    return;\n                }\n                _controller.CommitStorage(result.Visible, config => ImageProcessor.RemapAuthoringForCrop(config, bounds, oldWidth, oldHeight), result.Recovery);\n            }\n            else\n            {\n                _status.Text = "Applying polygon cutout and trimming transparent margins…";\n                var result = await Task.Run(() =>\n                {\n                    var masked = ImageAlphaEditing.ApplyPolygonCutout(document.PngBytes, polygon);\n                    var bounds = RawRgbaTransforms.FindTrimBounds(masked, 0);\n                    return bounds is null ? (Bounds: (ImagePixelRect?)null, Png: (byte[]?)null) :\n                        (Bounds: (ImagePixelRect?)bounds.Value, Png: (byte[]?)RawRgbaTransforms.Crop(masked, bounds.Value));\n                });\n                if (!ReferenceEquals(document, _controller.Document)) return;\n                if (result.Bounds is not ImagePixelRect bounds || result.Png is null)\n                {\n                    _status.Text = "The polygon cutout would make the image completely transparent; nothing was changed.";\n                    return;\n                }\n                _controller.CommitImage(result.Png,\n                    config => ImageProcessor.RemapAuthoringForCrop(config, bounds, oldWidth, oldHeight),\n                    recovery => RawRgbaTransforms.Crop(recovery, bounds));\n            }\n            _editor.Mode = EditorMode.Select;\n            RefreshChrome();\n        }\n        catch (GelFormatException ex) { _status.Text = ex.Message; }\n        catch (Exception ex) { await Dialogs.ShowErrorAsync(this, "Polygon cutout failed", ex.Message); }\n    }\n\n''')

replace_section(
    rel,
    "    private async Task ApplyBackgroundAsync()",
    "    private Task<byte[]> GenerateBackgroundPreviewAsync",
    '''    private async Task ApplyBackgroundAsync()\n    {\n        _backgroundPreviewCancellation?.Cancel();\n        var cancellation = new CancellationTokenSource();\n        _backgroundPreviewCancellation = cancellation;\n        var document = _controller.Document;\n        try\n        {\n            _status.Text = "Applying background removal…";\n            if (AnimatedImageProcessor.IsAnimated(document.Config))\n            {\n                var color = _backgroundColor;\n                var tolerance = _backgroundTolerance;\n                var feather = _backgroundFeather;\n                ImageStorageResult visible;\n                if (_editor.EditCurrentAnimationFrameOnly)\n                {\n                    var frameIndex = _editor.CurrentFrameIndex;\n                    visible = await Task.Run(() => AnimatedImageProcessor.TransformFrame(document.PngBytes, document.Config, frameIndex, frame => RawRgbaTransforms.RemoveBackground(frame, color, tolerance, feather)), cancellation.Token);\n                }\n                else\n                {\n                    visible = await Task.Run(() => AnimatedImageProcessor.TransformAnimated(document.PngBytes, document.Config, frame => RawRgbaTransforms.RemoveBackground(frame, color, tolerance, feather)), cancellation.Token);\n                }\n                if (!ReferenceEquals(document, _controller.Document)) return;\n                _controller.CommitStorage(visible, recoveryStorage: _controller.GetRecoveryStorage());\n            }\n            else\n            {\n                var preview = _backgroundPreview ?? await GenerateBackgroundPreviewAsync(cancellation.Token);\n                if (!ReferenceEquals(document, _controller.Document)) return;\n                _controller.CommitImage(preview);\n            }\n            _backgroundPreview = null;\n            _editor.SetPreview(null);\n            _editor.Mode = EditorMode.Select;\n        }\n        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }\n        catch (Exception ex) { await Dialogs.ShowErrorAsync(this, "Background removal failed", ex.Message); }\n        finally\n        {\n            if (ReferenceEquals(_backgroundPreviewCancellation, cancellation)) _backgroundPreviewCancellation = null;\n            cancellation.Dispose();\n        }\n    }\n\n''')

replace_once(
    rel,
    '''    private void CancelBackground()\n    {\n        _backgroundPreviewCancellation?.Cancel();\n        _backgroundPreviewCancellation = null;\n        _backgroundPreview = null;\n        _editor.SetPreview(null);\n        _editor.Mode = EditorMode.Select;\n    }''',
    '''    private void ClearBackgroundPreview()\n    {\n        _backgroundPreviewCancellation?.Cancel();\n        _backgroundPreviewCancellation = null;\n        _backgroundPreview = null;\n        _editor.SetPreview(null);\n    }\n\n    private void CancelBackground()\n    {\n        ClearBackgroundPreview();\n        _editor.Mode = EditorMode.Select;\n    }'''
)
replace_once(
    rel,
    "        try { await _controller.OpenAsync(path); ShowWorkspace(Workspace.Asset); }",
    "        try\n        {\n            await _controller.OpenAsync(path);\n            _editor.EditCurrentAnimationFrameOnly = false;\n            _editor.ResetAnimationPlayback();\n            ShowWorkspace(Workspace.Asset);\n        }"
)
replace_once(
    rel,
    "        _status.Text = $\"{config.Image.Width} × {config.Image.Height} px{animation}   |   {config.Cores.Count} core(s)   |   {config.RigidityStrokes.Count} rigidity stroke(s)   |   {(_controller.IsDirty ? \"Unsaved changes\" : Path.GetFileName(_controller.CurrentPath))}\";",
    "        _status.Text = $\"{config.Image.Width} × {config.Image.Height} px{animation}   |   {config.Cores.Count} core(s)   |   {config.RigidityStrokes.Count} rigidity stroke(s)   |   {(_controller.IsDirty ? \"Unsaved changes\" : Path.GetFileName(_controller.CurrentPath))}\";\n        RefreshAnimationControls();"
)

# -----------------------------------------------------------------------------
# Regression tests.
# -----------------------------------------------------------------------------
rel = "tools/gelatin/tests/Gelatin.Tests/AnimatedImageTests.cs"
insert_before(
    rel,
    "    [Fact]\n    public void AnimatedGelRoundTripsThroughGel1Container()",
    '''    [Fact]\n    public void SelectedFrameTransformTouchesOnlyThatFrameAndKeepsTiming()\n    {\n        var imported = AnimatedImageProcessor.ImportGif(Convert.FromBase64String(TwoFrameGifBase64));\n        var config = new GelConfig\n        {\n            SchemaVersion = 2,\n            Image = new ImageConfig { Width = 2, Height = 2 },\n            Animation = imported.Animation,\n            Cores = []\n        };\n\n        var edited = AnimatedImageProcessor.TransformFrame(imported.PngBytes, config, 1, frame =>\n            ImageAlphaEditing.ApplyRectCutout(frame, new PixelRect(0, 0, 1, 2)));\n        var editedConfig = config.DeepClone();\n        editedConfig.Animation = edited.Animation;\n        var first = RawRgbaCodec.Decode(AnimatedImageProcessor.GetFramePng(edited.PngBytes, editedConfig, 0));\n        var second = RawRgbaCodec.Decode(AnimatedImageProcessor.GetFramePng(edited.PngBytes, editedConfig, 1));\n\n        Assert.Equal(255, first.Pixels[3]);\n        Assert.Equal(255, first.Pixels[7]);\n        Assert.Equal(255, second.Pixels[3]);\n        Assert.Equal(0, second.Pixels[7]);\n        Assert.Equal([50, 120], edited.Animation!.Frames.Select(frame => frame.DurationMs).ToArray());\n        Assert.All(edited.Animation.Frames, frame => Assert.Equal((2, 2), (frame.Width, frame.Height)));\n    }\n\n    [Fact]\n    public void SelectedFrameAlphaBrushDoesNotTouchOtherFrames()\n    {\n        var imported = AnimatedImageProcessor.ImportGif(Convert.FromBase64String(TwoFrameGifBase64));\n        var config = new GelConfig\n        {\n            SchemaVersion = 2,\n            Image = new ImageConfig { Width = 2, Height = 2 },\n            Animation = imported.Animation,\n            Cores = []\n        };\n\n        using var brush = new AnimationAlphaBrushSession(imported.PngBytes, imported.PngBytes, config, AlphaBrushMode.Erase, 1, 1);\n        brush.ApplyPoint(new PixelPoint(0.5, 0.5));\n        var edited = brush.Encode();\n        var editedConfig = config.DeepClone();\n        editedConfig.Animation = edited.Animation;\n        var first = RawRgbaCodec.Decode(AnimatedImageProcessor.GetFramePng(edited.PngBytes, editedConfig, 0));\n        var second = RawRgbaCodec.Decode(AnimatedImageProcessor.GetFramePng(edited.PngBytes, editedConfig, 1));\n\n        Assert.Equal(255, first.Pixels[3]);\n        Assert.Equal(0, second.Pixels[3]);\n    }\n\n    [Fact]\n    public void FrameStartTimeUsesPreservedPerFrameDurations()\n    {\n        var animation = new AnimationConfig\n        {\n            Frames =\n            [\n                new AnimationFrameConfig { Width = 1, Height = 1, DurationMs = 50 },\n                new AnimationFrameConfig { Width = 1, Height = 1, DurationMs = 120 },\n                new AnimationFrameConfig { Width = 1, Height = 1, DurationMs = 30 }\n            ]\n        };\n\n        Assert.Equal(0, AnimatedImageProcessor.FrameStartTimeMilliseconds(animation, 0));\n        Assert.Equal(50, AnimatedImageProcessor.FrameStartTimeMilliseconds(animation, 1));\n        Assert.Equal(170, AnimatedImageProcessor.FrameStartTimeMilliseconds(animation, 2));\n    }\n\n''')

write(
    "tools/gelatin/tests/Gelatin.Tests/FrameEditorUiTests.cs",
    '''using Avalonia.Headless.XUnit;\nusing Gelatin.App;\nusing Gelatin.App.Controls;\n\nnamespace Gelatin.Tests;\n\npublic sealed class FrameEditorUiTests\n{\n    private const string TwoFrameGifBase64 = "R0lGODlhAgACAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQIBQAAACwAAAAAAgACAAAIBgABCAQQEAAh+QQIDAAAACwAAAAAAgACAIEA/wAAAAAAAAAAAAAIBgABCAQQEAA7";\n\n    [AvaloniaFact]\n    public async Task ManualFrameNavigationPausesAndWraps()\n    {\n        var path = Path.Combine(Path.GetTempPath(), $"gelatin-frame-{Guid.NewGuid():N}.gif");\n        await File.WriteAllBytesAsync(path, Convert.FromBase64String(TwoFrameGifBase64));\n        try\n        {\n            var controller = new DocumentController();\n            await controller.OpenAsync(path);\n            var editor = new EditorCanvas(controller);\n            try\n            {\n                Assert.True(editor.AnimationPlaying);\n                editor.SetAnimationFrame(1);\n                Assert.False(editor.AnimationPlaying);\n                Assert.Equal(1, editor.CurrentFrameIndex);\n\n                editor.StepAnimation(1);\n                Assert.Equal(0, editor.CurrentFrameIndex);\n                editor.StepAnimation(-1);\n                Assert.Equal(1, editor.CurrentFrameIndex);\n\n                editor.SetAnimationPlaying(true);\n                Assert.True(editor.AnimationPlaying);\n            }\n            finally { editor.Shutdown(); }\n        }\n        finally\n        {\n            if (File.Exists(path)) File.Delete(path);\n        }\n    }\n}\n''')

# README behavior docs, keeping the package version unchanged until the feature gate is green.
rel = "tools/gelatin/README.md"
replace_once(
    rel,
    "Animated assets play automatically in the Asset and Gel workspaces with their preserved timing. Image edits apply to every frame, and transparent trimming uses the union of visible pixels across all frames so the asset never shifts between frames.",
    "Animated assets play automatically in the Asset and Gel workspaces with their preserved timing. In Asset, animation transport controls let you pause, step backward/forward, or jump directly to a frame. **Apply edits to** switches between the compatibility default **All frames** and **Current frame** for crop masking, polygon cutout, background removal, and alpha erase/restore. Current-frame crop/cutout keeps the shared canvas dimensions and turns pixels outside the selected region transparent only on that frame. Resize and transparent trim remain animation-wide; trim uses the union of visible pixels across all frames so the asset never shifts between frames."
)

print("Gelatin frame-edit implementation patch applied.")
