from __future__ import annotations

from pathlib import Path
import re
import textwrap

ROOT = Path(__file__).resolve().parents[2]


def path(rel: str) -> Path:
    return ROOT / rel


def read(rel: str) -> str:
    return path(rel).read_text(encoding="utf-8")


def write(rel: str, content: str) -> None:
    target = path(rel)
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(textwrap.dedent(content).lstrip("\n"), encoding="utf-8", newline="\n")


def replace_once(rel: str, old: str, new: str) -> None:
    text = read(rel)
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{rel}: expected exactly one match, found {count}: {old[:120]!r}")
    path(rel).write_text(text.replace(old, new, 1), encoding="utf-8", newline="\n")


def replace_between(rel: str, start: str, end: str, replacement: str) -> None:
    text = read(rel)
    start_index = text.find(start)
    if start_index < 0:
        raise RuntimeError(f"{rel}: start marker not found: {start!r}")
    end_index = text.find(end, start_index)
    if end_index < 0:
        raise RuntimeError(f"{rel}: end marker not found: {end!r}")
    path(rel).write_text(text[:start_index] + textwrap.dedent(replacement).lstrip("\n") + text[end_index:], encoding="utf-8", newline="\n")


# -----------------------------------------------------------------------------
# One product version for both assemblies and all runtime-visible version text.
# -----------------------------------------------------------------------------
write("tools/gelatin/Directory.Build.props", r'''
<Project>
  <PropertyGroup>
    <Version>0.1.6</Version>
  </PropertyGroup>
</Project>
''')

for rel in (
    "tools/gelatin/src/Gelatin.App/Gelatin.App.csproj",
    "tools/gelatin/src/Gelatin.Core/Gelatin.Core.csproj",
):
    text = read(rel)
    text, count = re.subn(r"\n\s*<Version>[^<]+</Version>", "", text, count=1)
    if count != 1:
        raise RuntimeError(f"{rel}: project Version element was not found exactly once")
    path(rel).write_text(text, encoding="utf-8", newline="\n")

write("tools/gelatin/src/Gelatin.Core/GelatinProduct.cs", r'''
using System.Reflection;

namespace Gelatin.Core;

public static class GelatinProduct
{
    public static string Version { get; } = ResolveVersion();

    private static string ResolveVersion()
    {
        var informational = typeof(GelatinProduct).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (string.IsNullOrWhiteSpace(informational))
            return typeof(GelatinProduct).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        var metadata = informational.IndexOf('+');
        return metadata >= 0 ? informational[..metadata] : informational;
    }
}
''')

# -----------------------------------------------------------------------------
# Model cleanup: current tool version from assembly metadata and a cheap runtime
# clone that deliberately omits editor-only recovery pixels.
# -----------------------------------------------------------------------------
rel = "tools/gelatin/src/Gelatin.Core/Models/GelConfig.cs"
text = read(rel)
text = text.replace("using System.Text.Json.Serialization;\n", "using System.Text.Json.Serialization;\nusing Gelatin.Core;\n", 1)
text = text.replace('public string ToolVersion { get; set; } = "0.1.5";', 'public string ToolVersion { get; set; } = GelatinProduct.Version;', 1)
text = text.replace("// Schema 1: a single processed PNG. Schema 2: the animation atlas PNG.", "// schemaVersion selects image storage representation: 1 = one PNG, 2 = an animation atlas.", 1)
old = '''    public GelDocument DeepClone() => new()\n    {\n        Config = Config.DeepClone(),\n        PngBytes = (byte[])PngBytes.Clone(),\n        RecoveryPngBytes = RecoveryPngBytes is null ? null : (byte[])RecoveryPngBytes.Clone()\n    };'''
new = '''    public GelDocument DeepClone() => new()\n    {\n        Config = Config.DeepClone(),\n        PngBytes = (byte[])PngBytes.Clone(),\n        RecoveryPngBytes = RecoveryPngBytes is null ? null : (byte[])RecoveryPngBytes.Clone()\n    };\n\n    public GelDocument DeepCloneWithoutRecovery() => new()\n    {\n        Config = Config.DeepClone(),\n        PngBytes = (byte[])PngBytes.Clone()\n    };'''
if old not in text:
    raise RuntimeError("GelConfig.cs: DeepClone block not found")
text = text.replace(old, new, 1)
path(rel).write_text(text, encoding="utf-8", newline="\n")

# -----------------------------------------------------------------------------
# Undo/redo now carries state identity so the controller can know when undo has
# returned to the exact state that was last saved. Redo gets the same memory cap.
# -----------------------------------------------------------------------------
write("tools/gelatin/src/Gelatin.Core/Authoring/DocumentHistory.cs", r'''
using Gelatin.Core.Models;

namespace Gelatin.Core.Authoring;

public readonly record struct DocumentHistoryEntry(GelDocument Document, long StateId);

public sealed class DocumentHistory
{
    private readonly LinkedList<DocumentHistoryEntry> _undo = [];
    private readonly LinkedList<DocumentHistoryEntry> _redo = [];
    private readonly int _maximumEntries;
    private readonly long _maximumBytes;
    private long _undoBytes;
    private long _redoBytes;

    public DocumentHistory(int maximumEntries = 30, long maximumBytes = 512L * 1024 * 1024)
    {
        _maximumEntries = Math.Max(1, maximumEntries);
        _maximumBytes = Math.Max(1024 * 1024, maximumBytes);
    }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Record(GelDocument current) => Record(current, 0);

    public void Record(GelDocument current, long stateId)
    {
        AddUndo(CloneEntry(current, stateId));
        ClearRedo();
    }

    public GelDocument Undo(GelDocument current) => Undo(current, 0).Document;

    public DocumentHistoryEntry Undo(GelDocument current, long stateId)
    {
        if (_undo.Last is null) return CloneEntry(current, stateId);
        AddRedo(CloneEntry(current, stateId));
        var result = _undo.Last.Value;
        _undoBytes -= Estimate(result.Document);
        _undo.RemoveLast();
        return CloneEntry(result.Document, result.StateId);
    }

    public GelDocument Redo(GelDocument current) => Redo(current, 0).Document;

    public DocumentHistoryEntry Redo(GelDocument current, long stateId)
    {
        if (_redo.Last is null) return CloneEntry(current, stateId);
        AddUndo(CloneEntry(current, stateId));
        var result = _redo.Last.Value;
        _redoBytes -= Estimate(result.Document);
        _redo.RemoveLast();
        return CloneEntry(result.Document, result.StateId);
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _undoBytes = 0;
        _redoBytes = 0;
    }

    private void AddUndo(DocumentHistoryEntry entry)
    {
        _undo.AddLast(entry);
        _undoBytes += Estimate(entry.Document);
        Trim(_undo, ref _undoBytes);
    }

    private void AddRedo(DocumentHistoryEntry entry)
    {
        _redo.AddLast(entry);
        _redoBytes += Estimate(entry.Document);
        Trim(_redo, ref _redoBytes);
    }

    private void ClearRedo()
    {
        _redo.Clear();
        _redoBytes = 0;
    }

    private void Trim(LinkedList<DocumentHistoryEntry> list, ref long bytes)
    {
        while (list.Count > _maximumEntries || (bytes > _maximumBytes && list.Count > 1))
        {
            var first = list.First!.Value;
            bytes -= Estimate(first.Document);
            list.RemoveFirst();
        }
    }

    private static DocumentHistoryEntry CloneEntry(GelDocument document, long stateId)
        => new(document.DeepClone(), stateId);

    private static long Estimate(GelDocument document)
        => document.PngBytes.LongLength + (document.RecoveryPngBytes?.LongLength ?? 0) +
           document.Config.Cores.Count * 256L +
           document.Config.RigidityStrokes.Sum(stroke => 64L + stroke.Points.Count * 24L);
}
''')

# -----------------------------------------------------------------------------
# Controller: state-identity dirty tracking, serialized saves, change categories,
# current-version stamping, and runtime clones that do not duplicate recovery.
# -----------------------------------------------------------------------------
write("tools/gelatin/src/Gelatin.App/DocumentController.cs", r'''
using Gelatin.Core;
using Gelatin.Core.Authoring;
using Gelatin.Core.Format;
using Gelatin.Core.Imaging;
using Gelatin.Core.Models;
using SkiaSharp;

namespace Gelatin.App;

public enum DocumentChangeKind
{
    Metadata,
    RenderOnly,
    Simulation,
    Full
}

public sealed class DocumentController
{
    private GelDocument _document;
    private readonly DocumentHistory _history = new();
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private long _nextStateId = 1;
    private long _stateId;
    private long _savedStateId;

    public GelDocument Document => _document;
    public byte[] RecoveryPngBytes => EnsureRecoverySource();
    public string? CurrentPath { get; private set; }
    public bool IsDirty => _stateId != _savedStateId;
    public bool CanUndo => _history.CanUndo;
    public bool CanRedo => _history.CanRedo;
    public event EventHandler? Changed;
    public event Action<DocumentChangeKind>? DetailedChanged;

    public bool IsAnimated => AnimatedImageProcessor.IsAnimated(_document.Config);
    public int AnimationFrameCount => _document.Config.Animation?.Frames.Count ?? 1;
    public byte[] GetFramePng(int frameIndex) => AnimatedImageProcessor.GetFramePng(_document, frameIndex);
    public ImageStorageResult GetRecoveryStorage() => new(
        (byte[])EnsureRecoverySource().Clone(),
        _document.Config.Image.Width,
        _document.Config.Image.Height,
        _document.Config.Animation?.DeepClone());

    public DocumentController()
    {
        _document = EstablishRecoveryBaseline(CreateWelcomeDocument());
        _stateId = NextStateId();
        _savedStateId = _stateId;
    }

    public async Task OpenAsync(string path)
    {
        var isGel = Path.GetExtension(path).Equals(".gel", StringComparison.OrdinalIgnoreCase);
        var document = await Task.Run(() => isGel
            ? GelFile.Read(path)
            : CreateFromImage(File.ReadAllBytes(path), Path.GetFileNameWithoutExtension(path)));
        _document = EstablishRecoveryBaseline(document);
        CurrentPath = isGel ? path : null;
        _stateId = NextStateId();
        _savedStateId = isGel ? _stateId : -1;
        _history.Clear();
        Notify(DocumentChangeKind.Full);
    }

    public async Task SaveAsync(string path)
    {
        if (!string.Equals(_document.Config.Authoring.ToolVersion, GelatinProduct.Version, StringComparison.Ordinal))
        {
            _history.Record(_document, _stateId);
            StampVersion();
            _stateId = NextStateId();
        }

        var snapshot = _document.DeepCloneWithoutRecovery();
        var snapshotStateId = _stateId;
        await _saveGate.WaitAsync();
        try
        {
            await Task.Run(() => GelFile.WriteAtomic(path, snapshot));
        }
        finally
        {
            _saveGate.Release();
        }

        CurrentPath = path;
        _savedStateId = snapshotStateId;
        Notify(DocumentChangeKind.Metadata);
    }

    public void ExportPng(string path) => File.WriteAllBytes(path, _document.PngBytes);
    public void ExportJson(string path) => File.WriteAllBytes(path, GelJson.Serialize(_document.Config, true));

    public void Mutate(Action<GelConfig> mutation, DocumentChangeKind kind = DocumentChangeKind.Simulation)
    {
        _history.Record(_document, _stateId);
        mutation(_document.Config);
        StampVersion();
        _stateId = NextStateId();
        Notify(kind);
    }

    public void CommitImage(byte[] png, Action<GelConfig>? remap = null, Func<byte[], byte[]>? recoveryTransform = null)
    {
        ArgumentNullException.ThrowIfNull(png);
        var dimensions = ImageProcessor.GetDimensions(png);
        var currentRecovery = EnsureRecoverySource();
        var nextRecovery = recoveryTransform is null
            ? (byte[])currentRecovery.Clone()
            : recoveryTransform((byte[])currentRecovery.Clone());

        var recoveryDimensions = ImageProcessor.GetDimensions(nextRecovery);
        if (recoveryDimensions != dimensions)
        {
            // Never permit stale recovery geometry to survive an edit. Resetting loses hidden pixels but is safe.
            nextRecovery = (byte[])png.Clone();
        }

        var nextConfig = _document.Config.DeepClone();
        remap?.Invoke(nextConfig);
        nextConfig.Image.Width = dimensions.Width;
        nextConfig.Image.Height = dimensions.Height;
        nextConfig.Authoring.ToolVersion = GelatinProduct.Version;

        _history.Record(_document, _stateId);
        _document = new GelDocument { Config = nextConfig, PngBytes = png, RecoveryPngBytes = nextRecovery };
        _stateId = NextStateId();
        Notify(DocumentChangeKind.Full);
    }

    public void CommitStorage(ImageStorageResult storage, Action<GelConfig>? remap = null, ImageStorageResult? recoveryStorage = null)
    {
        ArgumentNullException.ThrowIfNull(storage);
        var nextConfig = _document.Config.DeepClone();
        remap?.Invoke(nextConfig);
        nextConfig.SchemaVersion = storage.IsAnimated ? 2 : 1;
        nextConfig.Animation = storage.Animation?.DeepClone();
        nextConfig.Image.Width = storage.Width;
        nextConfig.Image.Height = storage.Height;
        nextConfig.Authoring.ToolVersion = GelatinProduct.Version;
        GelValidator.Validate(nextConfig);

        var storageDimensions = ImageProcessor.GetDimensions(storage.PngBytes);
        if (storage.IsAnimated) AnimatedImageProcessor.ValidateAtlas(nextConfig, storageDimensions.Width, storageDimensions.Height);
        else if (storageDimensions != (storage.Width, storage.Height))
            throw new GelFormatException("The processed image dimensions do not match the logical image dimensions.");

        var recovery = recoveryStorage?.PngBytes is { } bytes ? (byte[])bytes.Clone() : (byte[])storage.PngBytes.Clone();
        var recoveryDimensions = ImageProcessor.GetDimensions(recovery);
        if (storage.IsAnimated) AnimatedImageProcessor.ValidateAtlas(nextConfig, recoveryDimensions.Width, recoveryDimensions.Height);
        else if (recoveryDimensions != (storage.Width, storage.Height)) recovery = (byte[])storage.PngBytes.Clone();

        _history.Record(_document, _stateId);
        _document = new GelDocument { Config = nextConfig, PngBytes = storage.PngBytes, RecoveryPngBytes = recovery };
        _stateId = NextStateId();
        Notify(DocumentChangeKind.Full);
    }

    public void BeginCompoundEdit() => _history.Record(_document, _stateId);

    public void CompoundMutate(Action<GelConfig> mutation, DocumentChangeKind kind = DocumentChangeKind.Simulation)
    {
        mutation(_document.Config);
        StampVersion();
        _stateId = NextStateId();
        Notify(kind);
    }

    public void Undo()
    {
        if (!CanUndo) return;
        var result = _history.Undo(_document, _stateId);
        _document = result.Document;
        _stateId = result.StateId;
        EnsureRecoverySource();
        Notify(DocumentChangeKind.Full);
    }

    public void Redo()
    {
        if (!CanRedo) return;
        var result = _history.Redo(_document, _stateId);
        _document = result.Document;
        _stateId = result.StateId;
        EnsureRecoverySource();
        Notify(DocumentChangeKind.Full);
    }

    private long NextStateId() => _nextStateId++;
    private void StampVersion() => _document.Config.Authoring.ToolVersion = GelatinProduct.Version;

    private void Notify(DocumentChangeKind kind)
    {
        Changed?.Invoke(this, EventArgs.Empty);
        DetailedChanged?.Invoke(kind);
    }

    private byte[] EnsureRecoverySource()
    {
        var recovery = _document.RecoveryPngBytes;
        if (recovery is not null)
        {
            try
            {
                if (ImageProcessor.GetDimensions(recovery) == ImageProcessor.GetDimensions(_document.PngBytes)) return recovery;
            }
            catch (GelFormatException)
            {
                // Fall through to a safe current-image baseline.
            }
        }

        recovery = (byte[])_document.PngBytes.Clone();
        _document = new GelDocument
        {
            Config = _document.Config,
            PngBytes = _document.PngBytes,
            RecoveryPngBytes = recovery
        };
        return recovery;
    }

    private static GelDocument EstablishRecoveryBaseline(GelDocument document) => new()
    {
        Config = document.Config,
        PngBytes = document.PngBytes,
        RecoveryPngBytes = (byte[])document.PngBytes.Clone()
    };

    private static GelDocument CreateFromImage(byte[] bytes, string name)
    {
        var storage = AnimatedImageProcessor.NormalizeInput(bytes);
        return new GelDocument
        {
            PngBytes = storage.PngBytes,
            Config = new GelConfig
            {
                SchemaVersion = storage.IsAnimated ? 2 : 1,
                Animation = storage.Animation?.DeepClone(),
                AssetName = string.IsNullOrWhiteSpace(name) ? "Untitled Gel" : name,
                Image = new ImageConfig { Width = storage.Width, Height = storage.Height },
                Cores = [new CoreConfig { Id = 1, Name = "Core 1", RadiusX = 0.24, RadiusY = 0.24 }]
            }
        };
    }

    private static GelDocument CreateWelcomeDocument()
    {
        const int width = 480;
        const int height = 300;
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        using var path = new SKPath();
        path.AddRoundRect(new SKRoundRect(new SKRect(28, 28, width - 28, height - 28), 76, 76));
        using var paint = new SKPaint { IsAntialias = true };
        paint.Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(width, height),
            [new SKColor(116, 69, 220), new SKColor(234, 79, 177), new SKColor(255, 166, 72)], null, SKShaderTileMode.Clamp);
        canvas.DrawPath(path, paint);
        using var shine = new SKPaint { IsAntialias = true, Color = new SKColor(255, 255, 255, 65) };
        canvas.DrawOval(new SKRect(75, 56, 280, 125), shine);
        return new GelDocument
        {
            PngBytes = ImageProcessor.EncodePng(bitmap),
            Config = new GelConfig
            {
                AssetName = "New Gel",
                Image = new ImageConfig { Width = width, Height = height },
                Cores = [new CoreConfig { Id = 1, Name = "Core 1", RadiusX = 0.28, RadiusY = 0.3, Mass = 2.4 }]
            }
        };
    }
}
''')

# -----------------------------------------------------------------------------
# One alpha-safe RGBA transform implementation. ImageProcessor becomes the thin
# compatibility/authoring facade; RawRgbaCodec owns PNG writing.
# -----------------------------------------------------------------------------
write("tools/gelatin/src/Gelatin.Core/Imaging/ImageProcessor.cs", r'''
using Gelatin.Core.Models;
using SkiaSharp;

namespace Gelatin.Core.Imaging;

public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => checked(X + Width);
    public int Bottom => checked(Y + Height);
}

public static class ImageProcessor
{
    public static (int Width, int Height) GetDimensions(ReadOnlySpan<byte> encoded)
    {
        if (!RawRgbaCodec.IsPng(encoded))
            throw new GelFormatException("The embedded image is not a valid PNG payload.");
        var decoded = RawRgbaCodec.Decode(encoded);
        return (decoded.Width, decoded.Height);
    }

    public static byte[] NormalizeToPng(ReadOnlySpan<byte> encoded)
        => RawRgbaTransforms.NormalizeToPng(encoded);

    public static SKBitmap Decode(ReadOnlySpan<byte> encoded)
    {
        try
        {
            using var data = SKData.CreateCopy(encoded);
            using var codec = SKCodec.Create(data) ?? throw new GelFormatException("The image is unsupported or corrupt.");
            var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            var bitmap = new SKBitmap(info);
            var result = codec.GetPixels(info, bitmap.GetPixels());
            if (result != SKCodecResult.Success)
            {
                bitmap.Dispose();
                throw new GelFormatException($"The image decoder failed ({result}).");
            }
            return bitmap;
        }
        catch (GelFormatException) { throw; }
        catch (Exception ex)
        {
            throw new GelFormatException("The image is unsupported or corrupt.", ex);
        }
    }

    public static byte[] Crop(ReadOnlySpan<byte> png, PixelRect rect, CancellationToken cancellationToken = default)
        => RawRgbaTransforms.Crop(png, rect, cancellationToken);

    public static byte[] Resize(ReadOnlySpan<byte> png, int width, int height, CancellationToken cancellationToken = default)
        => RawRgbaTransforms.Resize(png, width, height, cancellationToken);

    public static PixelRect? FindTrimBounds(ReadOnlySpan<byte> png, double alphaThreshold, CancellationToken cancellationToken = default)
        => RawRgbaTransforms.FindTrimBounds(png, alphaThreshold, cancellationToken);

    public static byte[] RemoveBackground(ReadOnlySpan<byte> png, SKColor background, double tolerance, double feather, CancellationToken cancellationToken = default)
        => RawRgbaTransforms.RemoveBackground(png, background, tolerance, feather, cancellationToken);

    public static SKColor Sample(ReadOnlySpan<byte> png, int x, int y)
        => RawRgbaTransforms.Sample(png, x, y);

    public static void RemapAuthoringForCrop(GelConfig config, PixelRect crop, int oldWidth, int oldHeight)
    {
        var x0 = crop.X / (double)oldWidth;
        var y0 = crop.Y / (double)oldHeight;
        var sx = oldWidth / (double)crop.Width;
        var sy = oldHeight / (double)crop.Height;
        config.Cores.RemoveAll(core =>
            core.X + core.RadiusX < x0 || core.X - core.RadiusX > crop.Right / (double)oldWidth ||
            core.Y + core.RadiusY < y0 || core.Y - core.RadiusY > crop.Bottom / (double)oldHeight);
        foreach (var core in config.Cores)
        {
            core.X = Math.Clamp((core.X - x0) * sx, -1, 2);
            core.Y = Math.Clamp((core.Y - y0) * sy, -1, 2);
            core.RadiusX = Math.Clamp(core.RadiusX * sx, double.Epsilon, 2);
            core.RadiusY = Math.Clamp(core.RadiusY * sy, double.Epsilon, 2);
        }

        var remappedStrokes = new List<RigidityStroke>();
        foreach (var stroke in config.RigidityStrokes)
        {
            var clippedParts = ClipStroke(stroke.Points, x0 - stroke.Radius, y0 - stroke.Radius,
                crop.Right / (double)oldWidth + stroke.Radius, crop.Bottom / (double)oldHeight + stroke.Radius);
            foreach (var part in clippedParts)
            {
                if (remappedStrokes.Count >= GelValidator.MaxStrokes) break;
                var points = part.Select(point => new[]
                {
                    Math.Clamp((point[0] - x0) * sx, -1, 2),
                    Math.Clamp((point[1] - y0) * sy, -1, 2)
                }).ToList();
                if (points.Count == 0) continue;
                remappedStrokes.Add(new RigidityStroke
                {
                    Radius = Math.Clamp(stroke.Radius * Math.Sqrt(sx * sy), double.Epsilon, 1),
                    Strength = stroke.Strength,
                    Points = points
                });
            }
            if (remappedStrokes.Count >= GelValidator.MaxStrokes) break;
        }
        config.RigidityStrokes = remappedStrokes;
        config.Image.Width = crop.Width;
        config.Image.Height = crop.Height;
    }

    public static byte[] EncodePng(SKBitmap bitmap) => RawRgbaCodec.Encode(bitmap);

    private static List<List<double[]>> ClipStroke(IReadOnlyList<double[]> points, double left, double top, double right, double bottom)
    {
        var parts = new List<List<double[]>>();
        if (points.Count == 1)
        {
            var point = points[0];
            if (point[0] >= left && point[0] <= right && point[1] >= top && point[1] <= bottom)
                parts.Add([new[] { point[0], point[1] }]);
            return parts;
        }

        List<double[]>? current = null;
        for (var index = 1; index < points.Count; index++)
        {
            if (!ClipSegment(points[index - 1], points[index], left, top, right, bottom, out var start, out var end))
            {
                current = null;
                continue;
            }

            if (current is null || !SamePoint(current[^1], start))
            {
                current = [start];
                parts.Add(current);
            }
            if (!SamePoint(current[^1], end)) current.Add(end);
        }
        return parts;
    }

    private static bool ClipSegment(double[] a, double[] b, double left, double top, double right, double bottom,
        out double[] start, out double[] end)
    {
        var dx = b[0] - a[0];
        var dy = b[1] - a[1];
        var t0 = 0d;
        var t1 = 1d;
        if (!ClipTest(-dx, a[0] - left, ref t0, ref t1) ||
            !ClipTest(dx, right - a[0], ref t0, ref t1) ||
            !ClipTest(-dy, a[1] - top, ref t0, ref t1) ||
            !ClipTest(dy, bottom - a[1], ref t0, ref t1))
        {
            start = end = [];
            return false;
        }
        start = [a[0] + t0 * dx, a[1] + t0 * dy];
        end = [a[0] + t1 * dx, a[1] + t1 * dy];
        return true;
    }

    private static bool ClipTest(double p, double q, ref double t0, ref double t1)
    {
        if (Math.Abs(p) < 1e-12) return q >= 0;
        var ratio = q / p;
        if (p < 0)
        {
            if (ratio > t1) return false;
            if (ratio > t0) t0 = ratio;
        }
        else
        {
            if (ratio < t0) return false;
            if (ratio < t1) t1 = ratio;
        }
        return true;
    }

    private static bool SamePoint(double[] a, double[] b)
        => Math.Abs(a[0] - b[0]) < 1e-10 && Math.Abs(a[1] - b[1]) < 1e-10;
}
''')

write("tools/gelatin/src/Gelatin.Core/Imaging/RawRgbaTransforms.cs", r'''
using Gelatin.Core.Models;
using SkiaSharp;

namespace Gelatin.Core.Imaging;

public static class RawRgbaTransforms
{
    public static byte[] NormalizeToPng(ReadOnlySpan<byte> encoded)
    {
        var image = RawRgbaCodec.Decode(encoded);
        return RawRgbaCodec.Encode(image.Width, image.Height, image.Pixels);
    }

    public static byte[] Crop(ReadOnlySpan<byte> png, PixelRect rect, CancellationToken cancellationToken = default)
    {
        var source = RawRgbaCodec.Decode(png);
        if (rect.Width < 1 || rect.Height < 1 || rect.X < 0 || rect.Y < 0 || rect.Right > source.Width || rect.Bottom > source.Height)
            throw new GelFormatException("The crop rectangle must be inside the current image.");
        var result = new byte[checked(rect.Width * rect.Height * 4)];
        var sourceStride = checked(source.Width * 4);
        var resultStride = checked(rect.Width * 4);
        for (var y = 0; y < rect.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            source.Pixels.AsSpan(checked((rect.Y + y) * sourceStride + rect.X * 4), resultStride)
                .CopyTo(result.AsSpan(checked(y * resultStride), resultStride));
        }
        return RawRgbaCodec.Encode(rect.Width, rect.Height, result);
    }

    public static byte[] Resize(ReadOnlySpan<byte> png, int width, int height, CancellationToken cancellationToken = default)
    {
        if (width is < 1 or > GelValidator.MaxDimension || height is < 1 or > GelValidator.MaxDimension)
            throw new GelFormatException($"Resize dimensions must be between 1 and {GelValidator.MaxDimension} pixels.");
        var source = RawRgbaCodec.Decode(png);
        var result = new byte[checked(width * height * 4)];
        for (var y = 0; y < height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sy = (y + 0.5) * source.Height / height - 0.5;
            var y0 = Math.Clamp((int)Math.Floor(sy), 0, source.Height - 1);
            var y1 = Math.Min(source.Height - 1, y0 + 1);
            var fy = Math.Clamp(sy - Math.Floor(sy), 0, 1);
            for (var x = 0; x < width; x++)
            {
                var sx = (x + 0.5) * source.Width / width - 0.5;
                var x0 = Math.Clamp((int)Math.Floor(sx), 0, source.Width - 1);
                var x1 = Math.Min(source.Width - 1, x0 + 1);
                var fx = Math.Clamp(sx - Math.Floor(sx), 0, 1);
                var destination = (y * width + x) * 4;
                var p00 = (y0 * source.Width + x0) * 4;
                var p10 = (y0 * source.Width + x1) * 4;
                var p01 = (y1 * source.Width + x0) * 4;
                var p11 = (y1 * source.Width + x1) * 4;
                for (var channel = 0; channel < 4; channel++)
                {
                    var top = source.Pixels[p00 + channel] + (source.Pixels[p10 + channel] - source.Pixels[p00 + channel]) * fx;
                    var bottom = source.Pixels[p01 + channel] + (source.Pixels[p11 + channel] - source.Pixels[p01 + channel]) * fx;
                    result[destination + channel] = (byte)Math.Clamp(Math.Round(top + (bottom - top) * fy), 0, 255);
                }
            }
        }
        return RawRgbaCodec.Encode(width, height, result);
    }

    public static PixelRect? FindTrimBounds(ReadOnlySpan<byte> png, double alphaThreshold, CancellationToken cancellationToken = default)
    {
        var image = RawRgbaCodec.Decode(png);
        var threshold = (byte)Math.Clamp(Math.Round(alphaThreshold * 255), 0, 255);
        var minX = image.Width;
        var minY = image.Height;
        var maxX = -1;
        var maxY = -1;
        for (var y = 0; y < image.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < image.Width; x++)
            {
                if (image.Pixels[(y * image.Width + x) * 4 + 3] <= threshold) continue;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }
        return maxX < minX ? null : new PixelRect(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    public static byte[] RemoveBackground(ReadOnlySpan<byte> png, SKColor background, double tolerance, double feather, CancellationToken cancellationToken = default)
    {
        tolerance = Math.Clamp(tolerance, 0, 1);
        feather = Math.Clamp(feather, 0, 1);
        var image = RawRgbaCodec.Decode(png);
        var hard = tolerance * Math.Sqrt(3 * 255d * 255d);
        var soft = feather * Math.Sqrt(3 * 255d * 255d) * 0.35;
        for (var y = 0; y < image.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < image.Width; x++)
            {
                var offset = (y * image.Width + x) * 4;
                var dr = image.Pixels[offset] - background.Red;
                var dg = image.Pixels[offset + 1] - background.Green;
                var db = image.Pixels[offset + 2] - background.Blue;
                var distance = Math.Sqrt(dr * dr + dg * dg + db * db);
                var keep = soft <= 0.00001 ? (distance <= hard ? 0d : 1d) : SmoothStep(hard - soft, hard + soft, distance);
                var originalAlpha = image.Pixels[offset + 3];
                image.Pixels[offset + 3] = (byte)Math.Clamp(Math.Round(originalAlpha * keep), 0, originalAlpha);
            }
        }
        return RawRgbaCodec.Encode(image.Width, image.Height, image.Pixels);
    }

    public static SKColor Sample(ReadOnlySpan<byte> png, int x, int y)
    {
        var image = RawRgbaCodec.Decode(png);
        x = Math.Clamp(x, 0, image.Width - 1);
        y = Math.Clamp(y, 0, image.Height - 1);
        var offset = (y * image.Width + x) * 4;
        return new SKColor(image.Pixels[offset], image.Pixels[offset + 1], image.Pixels[offset + 2], image.Pixels[offset + 3]);
    }

    private static double SmoothStep(double edge0, double edge1, double value)
    {
        var t = Math.Clamp((value - edge0) / Math.Max(edge1 - edge0, 1e-9), 0, 1);
        return t * t * (3 - 2 * t);
    }
}
''')

rel = "tools/gelatin/src/Gelatin.Core/Imaging/RawRgbaCodec.cs"
replace_once(rel,
'''    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];\n    private static readonly uint[] CrcTable = BuildCrcTable();''',
'''    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];\n    private static readonly uint[] CrcTable = BuildCrcTable();\n\n    public static bool IsPng(ReadOnlySpan<byte> encoded)\n        => encoded.Length >= PngSignature.Length && encoded[..PngSignature.Length].SequenceEqual(PngSignature);''')

# -----------------------------------------------------------------------------
# Animation atlas work: decode an atlas once per operation, edit one frame in
# place when possible, and derive union-alpha/trim data directly from atlas RGBA.
# -----------------------------------------------------------------------------
replace_between(
    "tools/gelatin/src/Gelatin.Core/Imaging/AnimatedImageProcessor.cs",
    "    public static ImageStorageResult PackFrames(",
    "    public static long FrameStartTimeMilliseconds(",
    r'''
    public static ImageStorageResult PackFrames(IReadOnlyList<byte[]> framePngs, IReadOnlyList<int> durationsMs, int repetitionCount, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(framePngs);
        ArgumentNullException.ThrowIfNull(durationsMs);
        if (framePngs.Count is < 2 or > GelValidator.MaxAnimationFrames)
            throw new GelFormatException($"Animated assets must contain 2 to {GelValidator.MaxAnimationFrames} frames.");
        if (durationsMs.Count != framePngs.Count) throw new GelFormatException("Animation frame timing count does not match the frame count.");
        if (repetitionCount < -1 || repetitionCount > 1_000_000) throw new GelFormatException("Animation repetition count is invalid.");

        var decoded = new List<RgbaBuffer>(framePngs.Count);
        foreach (var png in framePngs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            decoded.Add(RawRgbaCodec.Decode(png));
        }
        return PackDecodedFrames(decoded, durationsMs, repetitionCount, cancellationToken);
    }

    private static ImageStorageResult PackDecodedFrames(IReadOnlyList<RgbaBuffer> decoded, IReadOnlyList<int> durationsMs, int repetitionCount, CancellationToken cancellationToken)
    {
        var width = decoded[0].Width;
        var height = decoded[0].Height;
        ValidateLogicalDimensions(width, height, decoded.Count);
        if (decoded.Any(frame => frame.Width != width || frame.Height != height))
            throw new GelFormatException("Every animation frame must have identical dimensions.");

        for (var i = 0; i < durationsMs.Count; i++)
            if (durationsMs[i] < 0 || durationsMs[i] > GelValidator.MaxAnimationFrameDurationMs)
                throw new GelFormatException($"Animation frame {i + 1} has an invalid duration.");

        var columns = Math.Min(decoded.Count, Math.Max(1, GelValidator.MaxDimension / width));
        var rows = (decoded.Count + columns - 1) / columns;
        var atlasWidth = checked(columns * width);
        var atlasHeight = checked(rows * height);
        if (atlasWidth > GelValidator.MaxDimension || atlasHeight > GelValidator.MaxDimension)
            throw new GelFormatException("The animation frames cannot fit inside the GEL atlas dimension limit.");
        var atlasPixels = checked((long)atlasWidth * atlasHeight);
        if (atlasPixels > MaxDecodedAnimationPixels)
            throw new GelFormatException("The animation atlas is too large to author safely.");

        var atlas = new byte[checked(atlasWidth * atlasHeight * 4)];
        var frameConfigs = new List<AnimationFrameConfig>(decoded.Count);
        for (var index = 0; index < decoded.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var column = index % columns;
            var row = index / columns;
            var x = column * width;
            var y = row * height;
            var frame = decoded[index];
            for (var sourceY = 0; sourceY < height; sourceY++)
            {
                var sourceOffset = checked(sourceY * width * 4);
                var destinationOffset = checked(((y + sourceY) * atlasWidth + x) * 4);
                frame.Pixels.AsSpan(sourceOffset, width * 4).CopyTo(atlas.AsSpan(destinationOffset, width * 4));
            }
            frameConfigs.Add(new AnimationFrameConfig
            {
                X = x,
                Y = y,
                Width = width,
                Height = height,
                DurationMs = durationsMs[index]
            });
        }

        return new ImageStorageResult(
            RawRgbaCodec.Encode(atlasWidth, atlasHeight, atlas),
            width,
            height,
            new AnimationConfig { RepetitionCount = repetitionCount, Frames = frameConfigs });
    }

    public static byte[] GetFramePng(GelDocument document, int frameIndex)
        => GetFramePng(document.PngBytes, document.Config, frameIndex);

    public static byte[] GetFramePng(ReadOnlySpan<byte> atlasPng, GelConfig config, int frameIndex)
    {
        if (!IsAnimated(config)) return atlasPng.ToArray();
        var atlas = RawRgbaCodec.Decode(atlasPng);
        ValidateAtlas(config, atlas.Width, atlas.Height);
        var animation = config.Animation!;
        frameIndex = Math.Clamp(frameIndex, 0, animation.Frames.Count - 1);
        var frame = animation.Frames[frameIndex];
        return RawRgbaCodec.Encode(frame.Width, frame.Height, ExtractFramePixels(atlas, frame));
    }

    public static List<byte[]> ExtractFrames(ReadOnlySpan<byte> atlasPng, GelConfig config, CancellationToken cancellationToken = default)
    {
        if (!IsAnimated(config)) return [atlasPng.ToArray()];
        var atlas = RawRgbaCodec.Decode(atlasPng);
        ValidateAtlas(config, atlas.Width, atlas.Height);
        var result = new List<byte[]>(config.Animation!.Frames.Count);
        foreach (var frame in config.Animation.Frames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.Add(RawRgbaCodec.Encode(frame.Width, frame.Height, ExtractFramePixels(atlas, frame)));
        }
        return result;
    }

    public static ImageStorageResult TransformAnimated(ReadOnlySpan<byte> atlasPng, GelConfig config, Func<byte[], byte[]> transform, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transform);
        if (!IsAnimated(config)) throw new GelFormatException("The requested operation requires an animated GEL asset.");
        var animation = config.Animation!;
        var frames = ExtractFrames(atlasPng, config, cancellationToken);
        var transformed = new List<byte[]>(frames.Count);
        foreach (var frame in frames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            transformed.Add(transform(frame));
        }
        return PackFrames(transformed, animation.Frames.Select(frame => frame.DurationMs).ToArray(), animation.RepetitionCount, cancellationToken);
    }

    public static ImageStorageResult TransformFrame(ReadOnlySpan<byte> atlasPng, GelConfig config, int frameIndex, Func<byte[], byte[]> transform, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transform);
        if (!IsAnimated(config)) throw new GelFormatException("The requested operation requires an animated GEL asset.");
        var animation = config.Animation!;
        if (frameIndex < 0 || frameIndex >= animation.Frames.Count) throw new ArgumentOutOfRangeException(nameof(frameIndex));

        var atlas = RawRgbaCodec.Decode(atlasPng);
        ValidateAtlas(config, atlas.Width, atlas.Height);
        var frame = animation.Frames[frameIndex];
        cancellationToken.ThrowIfCancellationRequested();
        var transformedPng = transform(RawRgbaCodec.Encode(frame.Width, frame.Height, ExtractFramePixels(atlas, frame)));
        cancellationToken.ThrowIfCancellationRequested();
        var transformed = RawRgbaCodec.Decode(transformedPng);
        if (transformed.Width != frame.Width || transformed.Height != frame.Height)
            throw new GelFormatException("A current-frame edit may not change the shared animation canvas dimensions.");
        CopyFramePixels(atlas, frame, transformed.Pixels);
        return new ImageStorageResult(
            RawRgbaCodec.Encode(atlas.Width, atlas.Height, atlas.Pixels),
            config.Image.Width,
            config.Image.Height,
            animation.DeepClone());
    }

    public static PixelRect? FindUnionTrimBounds(ReadOnlySpan<byte> atlasPng, GelConfig config, double alphaThreshold, CancellationToken cancellationToken = default)
    {
        if (!IsAnimated(config)) return RawRgbaTransforms.FindTrimBounds(atlasPng, alphaThreshold, cancellationToken);
        var atlas = RawRgbaCodec.Decode(atlasPng);
        ValidateAtlas(config, atlas.Width, atlas.Height);
        var threshold = (byte)Math.Clamp(Math.Round(alphaThreshold * 255), 0, 255);
        var minX = config.Image.Width;
        var minY = config.Image.Height;
        var maxX = -1;
        var maxY = -1;
        foreach (var frame in config.Animation!.Frames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var y = 0; y < frame.Height; y++)
            for (var x = 0; x < frame.Width; x++)
            {
                var offset = (((frame.Y + y) * atlas.Width) + frame.X + x) * 4 + 3;
                if (atlas.Pixels[offset] <= threshold) continue;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }
        return maxX < minX ? null : new PixelRect(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    public static byte[] BuildUnionAlphaPng(ReadOnlySpan<byte> atlasPng, GelConfig config, CancellationToken cancellationToken = default)
    {
        if (!IsAnimated(config)) return atlasPng.ToArray();
        var atlas = RawRgbaCodec.Decode(atlasPng);
        ValidateAtlas(config, atlas.Width, atlas.Height);
        var width = config.Image.Width;
        var height = config.Image.Height;
        var frames = config.Animation!.Frames;
        var union = ExtractFramePixels(atlas, frames[0]);
        for (var frameIndex = 1; frameIndex < frames.Count; frameIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var frame = frames[frameIndex];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var unionOffset = (y * width + x) * 4;
                var atlasOffset = (((frame.Y + y) * atlas.Width) + frame.X + x) * 4;
                if (atlas.Pixels[atlasOffset + 3] <= union[unionOffset + 3]) continue;
                atlas.Pixels.AsSpan(atlasOffset, 4).CopyTo(union.AsSpan(unionOffset, 4));
            }
        }
        return RawRgbaCodec.Encode(width, height, union);
    }

    private static byte[] ExtractFramePixels(RgbaBuffer atlas, AnimationFrameConfig frame)
    {
        var pixels = new byte[checked(frame.Width * frame.Height * 4)];
        var rowBytes = checked(frame.Width * 4);
        for (var y = 0; y < frame.Height; y++)
        {
            var sourceOffset = checked(((frame.Y + y) * atlas.Width + frame.X) * 4);
            atlas.Pixels.AsSpan(sourceOffset, rowBytes).CopyTo(pixels.AsSpan(y * rowBytes, rowBytes));
        }
        return pixels;
    }

    private static void CopyFramePixels(RgbaBuffer atlas, AnimationFrameConfig frame, ReadOnlySpan<byte> pixels)
    {
        var rowBytes = checked(frame.Width * 4);
        if (pixels.Length != checked(rowBytes * frame.Height))
            throw new GelFormatException("The edited animation frame has an invalid RGBA buffer length.");
        for (var y = 0; y < frame.Height; y++)
        {
            var destinationOffset = checked(((frame.Y + y) * atlas.Width + frame.X) * 4);
            pixels.Slice(y * rowBytes, rowBytes).CopyTo(atlas.Pixels.AsSpan(destinationOffset, rowBytes));
        }
    }

''')

# -----------------------------------------------------------------------------
# Editor playback displays a source rectangle from one cached atlas bitmap. A
# frame tick no longer decodes/crops/re-encodes/decodes the atlas on the UI thread.
# -----------------------------------------------------------------------------
rel = "tools/gelatin/src/Gelatin.App/Controls/EditorCanvas.cs"
replace_once(rel,
'''        var image = _preview ?? _bitmap;\n        if (image is not null) context.DrawImage(image, imageRect);''',
'''        var image = _preview ?? _bitmap;\n        if (image is not null)\n        {\n            if (_preview is null && AnimatedImageProcessor.IsAnimated(_controller.Document.Config) &&\n                _controller.Document.Config.Animation is { Frames.Count: > 0 } animation)\n            {\n                var frame = animation.Frames[Math.Clamp(_loadedFrameIndex, 0, animation.Frames.Count - 1)];\n                context.DrawImage(image, new Rect(frame.X, frame.Y, frame.Width, frame.Height), imageRect);\n            }\n            else context.DrawImage(image, imageRect);\n        }''')
replace_between(rel, "    private async void Reload()", "    private Rect ImageRect()", r'''
    private async void Reload()
    {
        var document = _controller.Document;
        var changedDocument = !ReferenceEquals(_bitmapDocument, document);
        if (changedDocument)
        {
            _bitmapDocument = document;
            _loadedFrameIndex = -1;
            var frameCount = document.Config.Animation?.Frames.Count ?? 1;
            if (_animationPlaying)
            {
                _manualFrameIndex = 0;
                _animationBaseMs = 0;
                _animationClock.Restart();
            }
            else
            {
                _manualFrameIndex = Math.Clamp(_manualFrameIndex, 0, Math.Max(0, frameCount - 1));
                _animationBaseMs = 0;
                _animationClock.Reset();
            }
        }

        var frameIndex = CurrentFrameIndex;
        if (!changedDocument)
        {
            if (frameIndex != _loadedFrameIndex)
            {
                _loadedFrameIndex = frameIndex;
                AnimationFrameChanged?.Invoke(frameIndex);
            }
            InvalidateVisual();
            return;
        }

        _loadedFrameIndex = frameIndex;
        AnimationFrameChanged?.Invoke(frameIndex);
        _bitmapCancellation?.Cancel();
        _previewCancellation?.Cancel();
        _previewCancellation = null;
        var cancellation = new CancellationTokenSource();
        _bitmapCancellation = cancellation;
        _preview?.Dispose();
        _preview = null;
        try
        {
            var bitmap = await Task.Run(() => DecodeBitmap(document.PngBytes, cancellation.Token), cancellation.Token);
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

''')
replace_between(rel, "    private Rect ImageRect()", "    private void OnPointerPressed", r'''
    private Rect ImageRect()
    {
        var size = new Size(
            Math.Max(1, _controller.Document.Config.Image.Width),
            Math.Max(1, _controller.Document.Config.Image.Height));
        var scale = Math.Min(Math.Max(1, Bounds.Width - 40) / size.Width, Math.Max(1, Bounds.Height - 40) / size.Height) * _zoom;
        var width = size.Width * scale;
        var height = size.Height * scale;
        return new Rect((Bounds.Width - width) / 2 + _pan.X, (Bounds.Height - height) / 2 + _pan.Y, width, height);
    }

''')

# -----------------------------------------------------------------------------
# Lab rebuild invalidation: metadata does nothing, render-only settings update in
# place, and actual simulation/image changes rebuild. Recovery pixels are omitted.
# -----------------------------------------------------------------------------
rel = "tools/gelatin/src/Gelatin.App/Controls/LabControl.cs"
replace_once(rel, "        _controller.Changed += (_, _) => Rebuild();", "        _controller.DetailedChanged += OnDocumentChanged;")
replace_once(rel, "            var document = _controller.Document.DeepClone();", "            var document = _controller.Document.DeepCloneWithoutRecovery();")
replace_once(rel, "                var mesh = GelMeshBuilder.Build(document, quality);", "                var mesh = GelMeshBuilder.Build(document, quality, cancellation.Token);")
insert_marker = "    private async void Rebuild()\n"
text = read(rel)
index = text.find(insert_marker)
if index < 0:
    raise RuntimeError("LabControl.cs: Rebuild marker missing")
addition = textwrap.dedent(r'''
    private void OnDocumentChanged(DocumentChangeKind kind)
    {
        if (kind == DocumentChangeKind.Metadata) return;
        if (kind == DocumentChangeKind.RenderOnly)
        {
            lock (_simulationLock)
            {
                var config = _controller.Document.Config;
                _runtimeRender = new RuntimeRenderSettings(config.Appearance.Opacity, config.BounceEffect.Tint, config.BounceEffect.TintIntensity);
                if (_runtimeRender.TintMode != GelRuntimeSemantics.TintRandomNeon) _bounceTint.Reset();
            }
            InvalidateVisual();
            return;
        }
        Rebuild();
    }

''')
text = text[:index] + addition + text[index:]
path(rel).write_text(text, encoding="utf-8", newline="\n")

# -----------------------------------------------------------------------------
# Mesh building honors cancellation between expensive stages.
# -----------------------------------------------------------------------------
rel = "tools/gelatin/src/Gelatin.Core/Physics/GelMeshBuilder.cs"
replace_once(rel,
"    public static GelMesh Build(GelDocument document, QualitySettings quality)\n    {",
"    public static GelMesh Build(GelDocument document, QualitySettings quality, CancellationToken cancellationToken = default)\n    {\n        cancellationToken.ThrowIfCancellationRequested();")
replace_once(rel,
"        var triangles = new List<int>((columns - 1) * (rows - 1) * 6);",
"        cancellationToken.ThrowIfCancellationRequested();\n        var triangles = new List<int>((columns - 1) * (rows - 1) * 6);")
replace_once(rel,
"        foreach (var pair in structural.ToArray()) AddDistance(pair.Item1, pair.Item2, 0, true);\n\n        var contourSource",
"        foreach (var pair in structural.ToArray()) AddDistance(pair.Item1, pair.Item2, 0, true);\n\n        cancellationToken.ThrowIfCancellationRequested();\n        var contourSource")
replace_once(rel,
"        var cores = new List<CoreBody>();\n        foreach (var definition in document.Config.Cores)\n        {",
"        cancellationToken.ThrowIfCancellationRequested();\n        var cores = new List<CoreBody>();\n        foreach (var definition in document.Config.Cores)\n        {\n            cancellationToken.ThrowIfCancellationRequested();")

# -----------------------------------------------------------------------------
# MainWindow: dynamic product version, truthful quality text, coalesced slider
# undo, render-only change categories, and cancellation passed into preview work.
# -----------------------------------------------------------------------------
rel = "tools/gelatin/src/Gelatin.App/MainWindow.cs"
text = read(rel)
text = text.replace("using Gelatin.App.Controls;\n", "using Gelatin.App.Controls;\nusing Gelatin.Core;\n", 1)
text = text.replace('Title = "Gelatin 0.1.5";', 'Title = $"Gelatin {GelatinProduct.Version}";', 1)
text = text.replace('"Gelatin 0.1.5\\nStandalone gel asset authoring and physics lab."', '$"Gelatin {GelatinProduct.Version}\\nStandalone gel asset authoring and physics lab."', 1)
text = text.replace('Title = $"Gelatin 0.1.5 — {_controller.Document.Config.AssetName}{dirty}";', 'Title = $"Gelatin {GelatinProduct.Version} — {_controller.Document.Config.AssetName}{dirty}";', 1)
text = text.replace('_controller.Mutate(config => config.Image.AlphaThreshold = (double)(alpha.Value ?? 0.0625m));', '_controller.Mutate(config => config.Image.AlphaThreshold = (double)(alpha.Value ?? 0.0625m), DocumentChangeKind.Simulation);', 1)
text = text.replace('_controller.Mutate(config => config.AssetName = value[..Math.Min(256, value.Length)]);', '_controller.Mutate(config => config.AssetName = value[..Math.Min(256, value.Length)], DocumentChangeKind.Metadata);', 1)
text = text.replace('_controller.Mutate(config => config.BounceEffect.Tint = mode);', '_controller.Mutate(config => config.BounceEffect.Tint = mode, DocumentChangeKind.RenderOnly);', 1)
text = text.replace('_controller.Mutate(config => config.Motion.SpeedPixelsPerSecond = (double)(movementSpeed.Value ?? 320m));', '_controller.Mutate(config => config.Motion.SpeedPixelsPerSecond = (double)(movementSpeed.Value ?? 320m), DocumentChangeKind.Simulation);', 1)
path(rel).write_text(text, encoding="utf-8", newline="\n")

replace_once(rel,
'''        tintIntensity.ValueChanged += (_, _) =>\n        {\n            tintPercent.Text = $"{tintIntensity.Value * 100:0}%";\n            _controller.Mutate(config => config.BounceEffect.TintIntensity = tintIntensity.Value);\n        };''',
'''        BindDocumentSlider(tintIntensity,\n            (config, value) => config.BounceEffect.TintIntensity = value,\n            DocumentChangeKind.RenderOnly,\n            value => tintPercent.Text = $"{value * 100:0}%");''')
replace_once(rel,
'''        opacity.ValueChanged += (_, _) =>\n        {\n            opacityPercent.Text = $"{opacity.Value * 100:0}%";\n            _controller.Mutate(config => config.Appearance.Opacity = opacity.Value);\n        };''',
'''        BindDocumentSlider(opacity,\n            (config, value) => config.Appearance.Opacity = value,\n            DocumentChangeKind.RenderOnly,\n            value => opacityPercent.Text = $"{value * 100:0}%");''')

old_material_calls = '''        AddMaterialSlider(right, "Softness", 0, 1, () => _controller.Document.Config.Material.Softness, value => _controller.Mutate(config => config.Material.Softness = value));\n        AddMaterialSlider(right, "Damping", 0, 1, () => _controller.Document.Config.Material.Damping, value => _controller.Mutate(config => config.Material.Damping = value));\n        AddMaterialSlider(right, "Area preservation", 0, 1, () => _controller.Document.Config.Material.AreaPreservation, value => _controller.Mutate(config => config.Material.AreaPreservation = value));\n        AddMaterialSlider(right, "Shape memory", 0, 1, () => _controller.Document.Config.Material.ShapeMemory, value => _controller.Mutate(config => config.Material.ShapeMemory = value));\n        AddMaterialSlider(right, "Bend resistance", 0, 1, () => _controller.Document.Config.Material.BendResistance, value => _controller.Mutate(config => config.Material.BendResistance = value));\n        AddMaterialSlider(right, "Max stretch", 1.05, 3, () => _controller.Document.Config.Material.MaxStretch, value => _controller.Mutate(config => config.Material.MaxStretch = value));'''
new_material_calls = '''        AddMaterialSlider(right, "Softness", 0, 1, () => _controller.Document.Config.Material.Softness, (config, value) => config.Material.Softness = value);\n        AddMaterialSlider(right, "Damping", 0, 1, () => _controller.Document.Config.Material.Damping, (config, value) => config.Material.Damping = value);\n        AddMaterialSlider(right, "Area preservation", 0, 1, () => _controller.Document.Config.Material.AreaPreservation, (config, value) => config.Material.AreaPreservation = value);\n        AddMaterialSlider(right, "Shape memory", 0, 1, () => _controller.Document.Config.Material.ShapeMemory, (config, value) => config.Material.ShapeMemory = value);\n        AddMaterialSlider(right, "Bend resistance", 0, 1, () => _controller.Document.Config.Material.BendResistance, (config, value) => config.Material.BendResistance = value);\n        AddMaterialSlider(right, "Max stretch", 1.05, 3, () => _controller.Document.Config.Material.MaxStretch, (config, value) => config.Material.MaxStretch = value);'''
replace_once(rel, old_material_calls, new_material_calls)

replace_once(rel,
'''    private void AddMaterialSlider(StackPanel panel, string label, double min, double max, Func<double> getter, Action<double> setter)\n    {\n        var slider = Slider(getter(), min, max);\n        slider.ValueChanged += (_, _) => setter(slider.Value);\n        panel.Children.Add(Labeled(label, slider));\n    }''',
'''    private void AddMaterialSlider(StackPanel panel, string label, double min, double max, Func<double> getter, Action<GelConfig, double> setter)\n    {\n        var slider = Slider(getter(), min, max);\n        BindDocumentSlider(slider, setter, DocumentChangeKind.Simulation);\n        panel.Children.Add(Labeled(label, slider));\n    }\n\n    private void BindDocumentSlider(Slider slider, Action<GelConfig, double> mutation, DocumentChangeKind kind, Action<double>? uiChanged = null)\n    {\n        var compound = false;\n        slider.PointerPressed += (_, e) =>\n        {\n            if (compound || !e.GetCurrentPoint(slider).Properties.IsLeftButtonPressed) return;\n            _controller.BeginCompoundEdit();\n            compound = true;\n        };\n        slider.PointerReleased += (_, _) => compound = false;\n        slider.ValueChanged += (_, _) =>\n        {\n            uiChanged?.Invoke(slider.Value);\n            if (compound) _controller.CompoundMutate(config => mutation(config, slider.Value), kind);\n            else _controller.Mutate(config => mutation(config, slider.Value), kind);\n        };\n    }''')

replace_once(rel,
'''        var quality = new ComboBox { ItemsSource = Enum.GetValues<PhysicsQuality>(), SelectedItem = _lab.Quality, HorizontalAlignment = HorizontalAlignment.Stretch };\n        quality.SelectionChanged += (_, _) => { if (quality.SelectedItem is PhysicsQuality value) _lab.Quality = value; };\n        right.Children.Add(Labeled("Physics preset", quality));\n        right.Children.Add(new TextBlock { Text = "Claire: ~64×64 mesh, 960 Hz, 24 solver iterations, dense contour.", TextWrapping = TextWrapping.Wrap, Foreground = MutedBrush() });''',
'''        var quality = new ComboBox { ItemsSource = Enum.GetValues<PhysicsQuality>(), SelectedItem = _lab.Quality, HorizontalAlignment = HorizontalAlignment.Stretch };\n        var qualityDescription = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = MutedBrush() };\n        void RefreshQualityDescription(PhysicsQuality value)\n        {\n            var settings = QualitySettings.For(value);\n            qualityDescription.Text = $"{value}: ~{settings.MeshTarget}×{settings.MeshTarget} target mesh, {settings.PhysicsHz} Hz, {settings.SolverIterations} solver iterations, {settings.ContourSamples} contour samples.";\n        }\n        RefreshQualityDescription(_lab.Quality);\n        quality.SelectionChanged += (_, _) =>\n        {\n            if (quality.SelectedItem is not PhysicsQuality value) return;\n            _lab.Quality = value;\n            RefreshQualityDescription(value);\n        };\n        right.Children.Add(Labeled("Physics preset", quality));\n        right.Children.Add(qualityDescription);''')

text = read(rel)
text = text.replace('RawRgbaTransforms.RemoveBackground(frame, color, tolerance, feather)), cancellation.Token)', 'RawRgbaTransforms.RemoveBackground(frame, color, tolerance, feather, cancellation.Token), cancellation.Token), cancellation.Token)', 2)
# The replacement above may not hit because TransformFrame/TransformAnimated have different closing shapes; do explicit safe replacements too.
text = text.replace('frame => RawRgbaTransforms.RemoveBackground(frame, color, tolerance, feather)), cancellation.Token);', 'frame => RawRgbaTransforms.RemoveBackground(frame, color, tolerance, feather, cancellation.Token), cancellation.Token), cancellation.Token);')
text = text.replace('var result = RawRgbaTransforms.RemoveBackground(png, color, tolerance, feather);', 'var result = RawRgbaTransforms.RemoveBackground(png, color, tolerance, feather, cancellationToken);', 1)
path(rel).write_text(text, encoding="utf-8", newline="\n")

# -----------------------------------------------------------------------------
# Dynamic packaging version sourced from Directory.Build.props.
# -----------------------------------------------------------------------------
rel = "tools/gelatin/scripts/publish.ps1"
replace_once(rel,
'''$LegalDirectory = Join-Path $PublishDirectory "licenses"\n$Archive = Join-Path $DistRoot "gelatin-0.1.5-win-x64.zip"''',
'''$LegalDirectory = Join-Path $PublishDirectory "licenses"\n[xml]$VersionDocument = Get-Content (Join-Path $ToolRoot "Directory.Build.props")\n$Version = [string]$VersionDocument.Project.PropertyGroup.Version\nif ([string]::IsNullOrWhiteSpace($Version)) { throw "Could not resolve Gelatin version from Directory.Build.props." }\n$Archive = Join-Path $DistRoot ("gelatin-" + $Version + "-win-x64.zip")''')

rel = ".github/workflows/gelatin.yml"
text = read(rel)
setup = '''      - name: Set up .NET 10\n        uses: actions/setup-dotnet@v5\n        with:\n          dotnet-version: "10.0.x"\n'''
resolve = setup + '''\n      - name: Resolve Gelatin version\n        id: version\n        shell: pwsh\n        run: |\n          [xml]$props = Get-Content "tools/gelatin/Directory.Build.props"\n          $version = [string]$props.Project.PropertyGroup.Version\n          if ([string]::IsNullOrWhiteSpace($version)) { throw "Missing Gelatin version." }\n          "version=$version" >> $env:GITHUB_OUTPUT\n'''
if setup not in text:
    raise RuntimeError("gelatin.yml: setup-dotnet block not found")
text = text.replace(setup, resolve, 1)
text = text.replace('$archive = "tools/gelatin/dist/gelatin-0.1.5-win-x64.zip"', '$archive = "tools/gelatin/dist/gelatin-${{ steps.version.outputs.version }}-win-x64.zip"', 1)
text = text.replace('name: gelatin-0.1.5-win-x64', 'name: gelatin-${{ steps.version.outputs.version }}-win-x64', 1)
text = text.replace('path: tools/gelatin/dist/gelatin-0.1.5-win-x64.zip', 'path: tools/gelatin/dist/gelatin-${{ steps.version.outputs.version }}-win-x64.zip', 1)
path(rel).write_text(text, encoding="utf-8", newline="\n")

# README no longer requires a manual version bump for titles/package instructions.
rel = "tools/gelatin/README.md"
text = read(rel)
text = text.replace("# Gelatin 0.1.5", "# Gelatin", 1)
text = text.replace("`gelatin-0.1.5-win-x64`", "`gelatin-<version>-win-x64`", 1)
text = text.replace("`gelatin-0.1.5-win-x64.zip`", "`gelatin-<version>-win-x64.zip`", 1)
text = text.replace("Gelatin 0.1.5 keeps", "Gelatin keeps", 1)
text = text.replace("tools/gelatin/dist/gelatin-0.1.5-win-x64.zip", "tools/gelatin/dist/gelatin-<version>-win-x64.zip", 1)
path(rel).write_text(text, encoding="utf-8", newline="\n")

# -----------------------------------------------------------------------------
# Regression coverage for the state bugs, centralized version, and cancellation.
# -----------------------------------------------------------------------------
write("tools/gelatin/tests/Gelatin.Tests/CleanupRegressionTests.cs", r'''
using Gelatin.App;
using Gelatin.Core;
using Gelatin.Core.Authoring;
using Gelatin.Core.Imaging;
using Gelatin.Core.Models;
using SkiaSharp;

namespace Gelatin.Tests;

public sealed class CleanupRegressionTests
{
    [Fact]
    public void HistoryPreservesStateIdentityAcrossUndoRedo()
    {
        var history = new DocumentHistory();
        var document = TestAssets.Document();
        history.Record(document, 41);
        document.Config.AssetName = "Changed";

        var undone = history.Undo(document, 42);
        Assert.Equal(41, undone.StateId);
        Assert.Equal("Round Trip Gel", undone.Document.Config.AssetName);

        var redone = history.Redo(undone.Document, undone.StateId);
        Assert.Equal(42, redone.StateId);
        Assert.Equal("Changed", redone.Document.Config.AssetName);
    }

    [Fact]
    public async Task UndoBackToSavedStateClearsDirtyFlag()
    {
        var controller = new DocumentController();
        var file = Path.Combine(Path.GetTempPath(), $"gelatin-cleanup-{Guid.NewGuid():N}.gel");
        try
        {
            await controller.SaveAsync(file);
            Assert.False(controller.IsDirty);
            controller.Mutate(config => config.AssetName = "Changed", DocumentChangeKind.Metadata);
            Assert.True(controller.IsDirty);
            controller.Undo();
            Assert.False(controller.IsDirty);
        }
        finally
        {
            if (File.Exists(file)) File.Delete(file);
        }
    }

    [Fact]
    public async Task EditingAfterSaveStartsRemainsDirtyWhenOlderSnapshotFinishes()
    {
        var controller = new DocumentController();
        var file = Path.Combine(Path.GetTempPath(), $"gelatin-save-race-{Guid.NewGuid():N}.gel");
        try
        {
            var saving = controller.SaveAsync(file);
            controller.Mutate(config => config.AssetName = "Edited during save", DocumentChangeKind.Metadata);
            await saving;
            Assert.True(controller.IsDirty);
        }
        finally
        {
            if (File.Exists(file)) File.Delete(file);
        }
    }

    [Fact]
    public void ProductVersionComesFromSharedAssemblyVersion()
    {
        Assert.Equal("0.1.6", GelatinProduct.Version);
        Assert.Equal(GelatinProduct.Version, new AuthoringConfig().ToolVersion);
    }

    [Fact]
    public void RawImageTransformsHonorCancellation()
    {
        var png = TestAssets.Png(32, 32, (_, _) => SKColors.White);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            RawRgbaTransforms.RemoveBackground(png, SKColors.White, 0.1, 0.1, cancellation.Token));
    }
}
''')

# Ensure current-version strings are no longer scattered through source/project/workflow files.
for rel in (
    "tools/gelatin/src/Gelatin.App/MainWindow.cs",
    "tools/gelatin/src/Gelatin.App/DocumentController.cs",
    "tools/gelatin/src/Gelatin.App/Gelatin.App.csproj",
    "tools/gelatin/src/Gelatin.Core/Gelatin.Core.csproj",
    "tools/gelatin/src/Gelatin.Core/Models/GelConfig.cs",
    "tools/gelatin/scripts/publish.ps1",
    ".github/workflows/gelatin.yml",
):
    if "0.1.5" in read(rel):
        raise RuntimeError(f"{rel}: stale 0.1.5 version string remains")

print("Gelatin cleanup patch applied successfully.")
