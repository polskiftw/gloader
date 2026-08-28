using Gelatin.Core.Authoring;
using Gelatin.Core.Format;
using Gelatin.Core.Imaging;
using Gelatin.Core.Models;
using SkiaSharp;

namespace Gelatin.App;

public sealed class DocumentController
{
    private const string ToolVersion = "0.1.1";
    private GelDocument _document;
    private readonly DocumentHistory _history = new();

    public GelDocument Document => _document;
    public string? CurrentPath { get; private set; }
    public bool IsDirty { get; private set; }
    public bool CanUndo => _history.CanUndo;
    public bool CanRedo => _history.CanRedo;
    public event EventHandler? Changed;

    public DocumentController() => _document = CreateWelcomeDocument();

    public async Task OpenAsync(string path)
    {
        var document = await Task.Run(() => Path.GetExtension(path).Equals(".gel", StringComparison.OrdinalIgnoreCase)
            ? GelFile.Read(path)
            : CreateFromImage(File.ReadAllBytes(path), Path.GetFileNameWithoutExtension(path)));
        _document = document;
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

    public void CommitImage(byte[] png, Action<GelConfig>? remap = null)
    {
        var dimensions = ImageProcessor.GetDimensions(png);
        _history.Record(_document);
        remap?.Invoke(_document.Config);
        _document.Config.Image.Width = dimensions.Width;
        _document.Config.Image.Height = dimensions.Height;
        StampVersion();
        _document = new GelDocument { Config = _document.Config, PngBytes = png };
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
        IsDirty = true;
        Notify();
    }

    public void Redo()
    {
        if (!CanRedo) return;
        _document = _history.Redo(_document);
        IsDirty = true;
        Notify();
    }

    private void StampVersion() => _document.Config.Authoring.ToolVersion = ToolVersion;
    private void Notify() => Changed?.Invoke(this, EventArgs.Empty);

    private static GelDocument CreateFromImage(byte[] bytes, string name)
    {
        var png = ImageProcessor.NormalizeToPng(bytes);
        var (width, height) = ImageProcessor.GetDimensions(png);
        return new GelDocument
        {
            PngBytes = png,
            Config = new GelConfig
            {
                AssetName = string.IsNullOrWhiteSpace(name) ? "Untitled Gel" : name,
                Image = new ImageConfig { Width = width, Height = height },
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
