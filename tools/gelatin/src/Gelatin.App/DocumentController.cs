using Gelatin.Core.Authoring;
using Gelatin.Core.Format;
using Gelatin.Core.Imaging;
using Gelatin.Core.Models;
using SkiaSharp;

namespace Gelatin.App;

public sealed class DocumentController
{
    private const string ToolVersion = "0.1.4";
    private GelDocument _document;
    private readonly DocumentHistory _history = new();

    public GelDocument Document => _document;
    public byte[] RecoveryPngBytes => EnsureRecoverySource();
    public string? CurrentPath { get; private set; }
    public bool IsDirty { get; private set; }
    public bool CanUndo => _history.CanUndo;
    public bool CanRedo => _history.CanRedo;
    public event EventHandler? Changed;

    public bool IsAnimated => AnimatedImageProcessor.IsAnimated(_document.Config);
    public int AnimationFrameCount => _document.Config.Animation?.Frames.Count ?? 1;
    public byte[] GetFramePng(int frameIndex) => AnimatedImageProcessor.GetFramePng(_document, frameIndex);
    public ImageStorageResult GetRecoveryStorage() => new(
        (byte[])EnsureRecoverySource().Clone(),
        _document.Config.Image.Width,
        _document.Config.Image.Height,
        _document.Config.Animation?.DeepClone());

    public DocumentController() => _document = EstablishRecoveryBaseline(CreateWelcomeDocument());

    public async Task OpenAsync(string path)
    {
        var document = await Task.Run(() => Path.GetExtension(path).Equals(".gel", StringComparison.OrdinalIgnoreCase)
            ? GelFile.Read(path)
            : CreateFromImage(File.ReadAllBytes(path), Path.GetFileNameWithoutExtension(path)));
        _document = EstablishRecoveryBaseline(document);
        CurrentPath = Path.GetExtension(path).Equals(".gel", StringComparison.OrdinalIgnoreCase) ? path : null;
        IsDirty = CurrentPath is null;
        _history.Clear();
        Notify();
    }

    public async Task SaveAsync(string path)
    {
        var snapshot = _document.DeepClone();
        await Task.Run(() => GelFile.WriteAtomic(path, snapshot));
        CurrentPath = path;
        IsDirty = false;
        Notify();
    }

    public void ExportPng(string path) => File.WriteAllBytes(path, _document.PngBytes);
    public void ExportJson(string path) => File.WriteAllBytes(path, GelJson.Serialize(_document.Config, true));

    public void Mutate(Action<GelConfig> mutation)
    {
        _history.Record(_document);
        mutation(_document.Config);
        StampVersion();
        IsDirty = true;
        Notify();
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
        nextConfig.Authoring.ToolVersion = ToolVersion;

        _history.Record(_document);
        _document = new GelDocument { Config = nextConfig, PngBytes = png, RecoveryPngBytes = nextRecovery };
        IsDirty = true;
        Notify();
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
        nextConfig.Authoring.ToolVersion = ToolVersion;
        GelValidator.Validate(nextConfig);

        var storageDimensions = ImageProcessor.GetDimensions(storage.PngBytes);
        if (storage.IsAnimated) AnimatedImageProcessor.ValidateAtlas(nextConfig, storageDimensions.Width, storageDimensions.Height);
        else if (storageDimensions != (storage.Width, storage.Height))
            throw new GelFormatException("The processed image dimensions do not match the logical image dimensions.");

        var recovery = recoveryStorage?.PngBytes is { } bytes ? (byte[])bytes.Clone() : (byte[])storage.PngBytes.Clone();
        var recoveryDimensions = ImageProcessor.GetDimensions(recovery);
        if (storage.IsAnimated) AnimatedImageProcessor.ValidateAtlas(nextConfig, recoveryDimensions.Width, recoveryDimensions.Height);
        else if (recoveryDimensions != (storage.Width, storage.Height)) recovery = (byte[])storage.PngBytes.Clone();

        _history.Record(_document);
        _document = new GelDocument { Config = nextConfig, PngBytes = storage.PngBytes, RecoveryPngBytes = recovery };
        IsDirty = true;
        Notify();
    }

    public void BeginCompoundEdit() => _history.Record(_document);

    public void CompoundMutate(Action<GelConfig> mutation)
    {
        mutation(_document.Config);
        StampVersion();
        IsDirty = true;
        Notify();
    }

    public void Undo()
    {
        if (!CanUndo) return;
        _document = _history.Undo(_document);
        EnsureRecoverySource();
        IsDirty = true;
        Notify();
    }

    public void Redo()
    {
        if (!CanRedo) return;
        _document = _history.Redo(_document);
        EnsureRecoverySource();
        IsDirty = true;
        Notify();
    }

    private void StampVersion() => _document.Config.Authoring.ToolVersion = ToolVersion;
    private void Notify() => Changed?.Invoke(this, EventArgs.Empty);

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
