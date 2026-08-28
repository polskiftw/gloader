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
