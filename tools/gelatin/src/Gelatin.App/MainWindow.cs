using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Gelatin.App.Controls;
using Gelatin.Core;
using Gelatin.Core.Imaging;
using Gelatin.Core.Models;
using Gelatin.Core.Physics;
using Gelatin.Core.Runtime;
using SkiaSharp;
using ImagePixelRect = Gelatin.Core.Imaging.PixelRect;

namespace Gelatin.App;

public sealed class MainWindow : Window
{
    private readonly DocumentController _controller = new();
    private readonly EditorCanvas _editor;
    private readonly LabControl _lab;
    private readonly Border _leftHost;
    private readonly Border _rightHost;
    private readonly Grid _canvasHost;
    private readonly TextBlock _status;
    private Workspace _workspace = Workspace.Asset;
    private bool _forceClose;
    private SKColor _backgroundColor = SKColors.White;
    private double _backgroundTolerance = 0.12;
    private double _backgroundFeather = 0.08;
    private byte[]? _backgroundPreview;
    private CancellationTokenSource? _backgroundPreviewCancellation;
    private Button? _polygonApplyButton;
    private Button? _animationPlayButton;
    private NumericUpDown? _animationFrameNumber;
    private bool _syncAnimationControls;

    public MainWindow()
    {
        Title = $"Gelatin {GelatinProduct.Version}";
        Width = 1360;
        Height = 880;
        MinWidth = 980;
        MinHeight = 650;
        Background = new SolidColorBrush(Color.Parse("#18181D"));
        _editor = new EditorCanvas(_controller);
        _lab = new LabControl(_controller) { IsVisible = false };
        _leftHost = PanelHost(250);
        _rightHost = PanelHost(300);
        _canvasHost = new Grid();
        _canvasHost.Children.Add(_editor);
        _canvasHost.Children.Add(_lab);
        _status = new TextBlock { Margin = new Thickness(12, 6), Foreground = new SolidColorBrush(Color.Parse("#B8B8C4")) };

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        root.Children.Add(BuildToolbar());
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("250,*,300") };
        Grid.SetRow(body, 1);
        body.Children.Add(_leftHost);
        Grid.SetColumn(_canvasHost, 1);
        body.Children.Add(_canvasHost);
        Grid.SetColumn(_rightHost, 2);
        body.Children.Add(_rightHost);
        root.Children.Add(body);
        Grid.SetRow(_status, 2);
        root.Children.Add(_status);
        Content = root;

        _controller.Changed += (_, _) => RefreshChrome();
        _editor.CoreSelected += id => { _editor.SelectedCoreId = id; if (_workspace == Workspace.Gel) BuildGelPanels(); };
        _editor.PixelPicked += (x, y) => { _backgroundColor = RawRgbaTransforms.Sample(_controller.GetFramePng(_editor.CurrentFrameIndex), x, y); UpdateBackgroundPreview(); };
        _editor.AnimationFrameChanged += _ => RefreshAnimationControls();
        _editor.AnimationPlaybackChanged += RefreshAnimationControls;
        _editor.CropChanged += crop =>
        {
            if (crop is { } value) _status.Text = $"Crop: {value.Width} × {value.Height} px at ({value.X}, {value.Y})";
            else RefreshChrome();
        };
        _editor.PolygonChanged += UpdatePolygonChrome;
        _editor.ImageError += message => _status.Text = message;
        _editor.EditorError += message => _status.Text = message;
        _lab.SimulationError += message => _status.Text = message;
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            _backgroundPreviewCancellation?.Cancel();
            _editor.Shutdown();
            _lab.Shutdown();
        };
        AddHandler(InputElement.KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DropEvent, OnDrop);
        ShowWorkspace(Workspace.Asset);
        RefreshChrome();
    }

    private Control BuildToolbar()
    {
        var panel = new WrapPanel { Margin = new Thickness(8), Orientation = Orientation.Horizontal };
        panel.Children.Add(ActionButton("Open", async () => await OpenPickerAsync()));
        panel.Children.Add(ActionButton("Save .gel", async () => await SaveAsync(false)));
        panel.Children.Add(ActionButton("Save As", async () => await SaveAsync(true)));
        panel.Children.Add(Separator());
        panel.Children.Add(ActionButton("Undo", () => _controller.Undo()));
        panel.Children.Add(ActionButton("Redo", () => _controller.Redo()));
        panel.Children.Add(Separator());
        panel.Children.Add(ActionButton("Asset", () => ShowWorkspace(Workspace.Asset), accent: true));
        panel.Children.Add(ActionButton("Gel", () => ShowWorkspace(Workspace.Gel), accent: true));
        panel.Children.Add(ActionButton("Lab", () => ShowWorkspace(Workspace.Lab), accent: true));
        panel.Children.Add(Separator());
        panel.Children.Add(ActionButton("About", () => Dialogs.ShowInfoAsync(this, "About Gelatin", $"Gelatin {GelatinProduct.Version}\nStandalone gel asset authoring and physics lab.")));
        return new Border { Background = new SolidColorBrush(Color.Parse("#222229")), BorderBrush = new SolidColorBrush(Color.Parse("#33333C")), BorderThickness = new Thickness(0, 0, 0, 1), Child = panel };
    }

    private void ShowWorkspace(Workspace workspace)
    {
        if (workspace != _workspace) _editor.CancelTransientInteraction();
        _workspace = workspace;
        _editor.IsVisible = workspace != Workspace.Lab;
        _lab.IsVisible = workspace == Workspace.Lab;
        if (workspace == Workspace.Asset) BuildAssetPanels();
        else if (workspace == Workspace.Gel) BuildGelPanels();
        else BuildLabPanels();
    }

    private void BuildAssetPanels()
    {
        _editor.Mode = EditorMode.Select;
        var left = SectionStack("IMAGE PREP");
        _animationPlayButton = null;
        _animationFrameNumber = null;
        left.Children.Add(ActionButton("Open image / .gel", async () => await OpenPickerAsync(), wide: true));
        if (_controller.Document.Config.Animation is { } animation)
        {
            var repeat = animation.RepetitionCount < 0 ? "loops forever" : animation.RepetitionCount == 0 ? "plays once" : $"repeats {animation.RepetitionCount} time(s)";
            var cycleMs = animation.Frames.Sum(frame => AnimatedImageProcessor.EffectiveDuration(frame.DurationMs));
            left.Children.Add(new TextBlock { Text = $"Animated GIF: {animation.Frames.Count} frames • {cycleMs} ms/cycle • {repeat}", TextWrapping = TextWrapping.Wrap, Foreground = MutedBrush(), FontSize = 11 });
            AddAnimationControls(left, animation);
        }
        left.Children.Add(Header("Crop"));
        left.Children.Add(ActionButton("Draw crop rectangle", () => _editor.Mode = EditorMode.Crop, wide: true));
        var cropRow = Row();
        cropRow.Children.Add(ActionButton("Apply", ApplyCropAsync));
        cropRow.Children.Add(ActionButton("Cancel", () => { _editor.CancelCrop(); _editor.Mode = EditorMode.Select; }));
        left.Children.Add(cropRow);

        left.Children.Add(Header("Irregular cutout"));
        left.Children.Add(ActionButton("Polygon cutout", () =>
        {
            _editor.BeginPolygonCutout();
            UpdatePolygonChrome();
        }, wide: true));
        var polygonRow = Row();
        _polygonApplyButton = ActionButton("Apply Cutout", ApplyPolygonCutoutAsync);
        _polygonApplyButton.IsEnabled = _editor.PolygonCanApply;
        polygonRow.Children.Add(_polygonApplyButton);
        polygonRow.Children.Add(ActionButton("Cancel", () => { _editor.Mode = EditorMode.Select; RefreshChrome(); }));
        left.Children.Add(polygonRow);
        left.Children.Add(new TextBlock
        {
            Text = "Click points; Enter/first point/double-click closes. Closed shapes support vertex drag, edge insertion, Delete, and pixel arrow nudging.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = MutedBrush(),
            FontSize = 11
        });

        left.Children.Add(Header("Resize"));
        var width = Number(_controller.Document.Config.Image.Width, 1, GelValidator.MaxDimension, 1);
        var height = Number(_controller.Document.Config.Image.Height, 1, GelValidator.MaxDimension, 1);
        var aspect = new CheckBox { Content = "Lock aspect ratio", IsChecked = true };
        var aspectLink = new ResizeAspectLink((double)(width.Value ?? 1), (double)(height.Value ?? 1));
        var syncing = false;
        aspect.IsCheckedChanged += (_, _) =>
        {
            if (aspect.IsChecked == true && !syncing)
                aspectLink.Capture((double)(width.Value ?? 1), (double)(height.Value ?? 1));
        };
        width.ValueChanged += (_, _) =>
        {
            if (aspect.IsChecked != true || syncing) return;
            syncing = true;
            height.Value = aspectLink.HeightForWidth((double)(width.Value ?? 1));
            syncing = false;
        };
        height.ValueChanged += (_, _) =>
        {
            if (aspect.IsChecked != true || syncing) return;
            syncing = true;
            width.Value = aspectLink.WidthForHeight((double)(height.Value ?? 1));
            syncing = false;
        };
        left.Children.Add(Labeled("Width", width));
        left.Children.Add(Labeled("Height", height));
        left.Children.Add(aspect);
        left.Children.Add(ActionButton("Resize image", () => ResizeImageAsync((int)(width.Value ?? 1), (int)(height.Value ?? 1)), wide: true));

        left.Children.Add(Header("Transparency"));
        var alpha = Number(_controller.Document.Config.Image.AlphaThreshold, 0, 1, 0.005, "0.000");
        alpha.ValueChanged += (_, _) => _controller.Mutate(config => config.Image.AlphaThreshold = (double)(alpha.Value ?? 0.0625m), DocumentChangeKind.Simulation);
        left.Children.Add(Labeled("Alpha threshold", alpha));
        left.Children.Add(ActionButton("Trim transparent edges", TrimTransparencyAsync, wide: true));

        left.Children.Add(Header("Precision alpha cleanup"));
        var alphaModes = Row();
        alphaModes.Children.Add(ActionButton("Erase alpha", () => _editor.BeginAlphaBrush(AlphaBrushMode.Erase)));
        alphaModes.Children.Add(ActionButton("Restore alpha", () => _editor.BeginAlphaBrush(AlphaBrushMode.Restore)));
        left.Children.Add(alphaModes);
        var alphaBrush = Number(_editor.AlphaBrushSize, 1, 256, 1, "0");
        alphaBrush.ValueChanged += (_, _) => _editor.AlphaBrushSize = (double)(alphaBrush.Value ?? 24);
        left.Children.Add(Labeled("Brush size (source px)", alphaBrush));

        var right = SectionStack("BACKGROUND REMOVAL");
        right.Children.Add(new TextBlock { Text = "Pick the background, tune the live preview, then apply.", TextWrapping = TextWrapping.Wrap, Foreground = MutedBrush() });
        right.Children.Add(ActionButton("Eyedropper", () => _editor.Mode = EditorMode.Eyedropper, wide: true));
        var tolerance = Slider(_backgroundTolerance, 0, 1);
        tolerance.ValueChanged += (_, _) => { _backgroundTolerance = tolerance.Value; UpdateBackgroundPreview(); };
        right.Children.Add(Labeled("Tolerance", tolerance));
        var feather = Slider(_backgroundFeather, 0, 1);
        feather.ValueChanged += (_, _) => { _backgroundFeather = feather.Value; UpdateBackgroundPreview(); };
        right.Children.Add(Labeled("Feather", feather));
        var backgroundRow = Row();
        backgroundRow.Children.Add(ActionButton("Apply", ApplyBackgroundAsync));
        backgroundRow.Children.Add(ActionButton("Cancel", CancelBackground));
        right.Children.Add(backgroundRow);
        right.Children.Add(Header("Inspection exports"));
        right.Children.Add(ActionButton("Export embedded PNG / atlas", async () => await ExportPngAsync(), wide: true));
        right.Children.Add(ActionButton("Export JSON", async () => await ExportJsonAsync(), wide: true));
        SetPanels(left, right);
    }

    private void AddAnimationControls(StackPanel panel, AnimationConfig animation)
    {
        panel.Children.Add(Header("Animation frames"));
        var transport = Row();
        transport.Children.Add(ActionButton("◀ Prev", () => { ClearBackgroundPreview(); _editor.StepAnimation(-1); }));
        _animationPlayButton = ActionButton(_editor.AnimationPlaying ? "Pause" : "Play", () =>
        {
            ClearBackgroundPreview();
            _editor.SetAnimationPlaying(!_editor.AnimationPlaying);
            RefreshAnimationControls();
        });
        transport.Children.Add(_animationPlayButton);
        transport.Children.Add(ActionButton("Next ▶", () => { ClearBackgroundPreview(); _editor.StepAnimation(1); }));
        panel.Children.Add(transport);

        _animationFrameNumber = Number(_editor.CurrentFrameIndex + 1, 1, animation.Frames.Count, 1, "0");
        _animationFrameNumber.ValueChanged += (_, _) =>
        {
            if (_syncAnimationControls) return;
            ClearBackgroundPreview();
            _editor.SetAnimationFrame((int)(_animationFrameNumber.Value ?? 1) - 1);
        };
        panel.Children.Add(Labeled("Frame", _animationFrameNumber));

        var scope = new ComboBox
        {
            ItemsSource = new[] { "All frames", "Current frame" },
            SelectedIndex = _editor.EditCurrentAnimationFrameOnly ? 1 : 0,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        scope.SelectionChanged += (_, _) =>
        {
            _editor.EditCurrentAnimationFrameOnly = scope.SelectedIndex == 1;
            if (_editor.EditCurrentAnimationFrameOnly) _editor.SetAnimationPlaying(false);
            RefreshAnimationControls();
        };
        panel.Children.Add(Labeled("Apply edits to", scope));
        panel.Children.Add(new TextBlock
        {
            Text = "Current-frame crop/cutout masks only that frame and keeps the shared canvas aligned. Resize and Trim transparent edges remain animation-wide; Trim uses the union of every frame.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = MutedBrush(),
            FontSize = 11
        });
        RefreshAnimationControls();
    }

    private void RefreshAnimationControls()
    {
        if (_animationPlayButton is null || _animationFrameNumber is null) return;
        var animation = _controller.Document.Config.Animation;
        if (animation is null || animation.Frames.Count == 0) return;
        _syncAnimationControls = true;
        try
        {
            _animationPlayButton.Content = _editor.AnimationPlaying ? "Pause" : "Play";
            _animationFrameNumber.Maximum = animation.Frames.Count;
            _animationFrameNumber.Value = _editor.CurrentFrameIndex + 1;
        }
        finally { _syncAnimationControls = false; }
    }

    private void BuildGelPanels()
    {
        var left = SectionStack("AUTHORING TOOLS");
        left.Children.Add(Header("Mode"));
        var modes = Row();
        modes.Children.Add(ActionButton("Select", () => _editor.Mode = EditorMode.Select));
        modes.Children.Add(ActionButton("Core", () => _editor.Mode = EditorMode.Core));
        left.Children.Add(modes);
        var paintModes = Row();
        paintModes.Children.Add(ActionButton("Rigid", () => _editor.Mode = EditorMode.Rigid));
        paintModes.Children.Add(ActionButton("Erase", () => _editor.Mode = EditorMode.Erase));
        left.Children.Add(paintModes);
        left.Children.Add(Header("Rigidity brush"));
        var radius = Slider(_editor.BrushRadius, 0.005, 0.25);
        radius.ValueChanged += (_, _) => _editor.BrushRadius = radius.Value;
        left.Children.Add(Labeled("Radius", radius));
        var strength = Slider(_editor.BrushStrength, 0, 1);
        strength.ValueChanged += (_, _) => _editor.BrushStrength = strength.Value;
        left.Children.Add(Labeled("Strength", strength));
        left.Children.Add(Header("Overlays"));
        left.Children.Add(Check("Show authoring overlays", _editor.ShowOverlays, value => { _editor.ShowOverlays = value; _editor.InvalidateVisual(); }));
        left.Children.Add(Check("Core influence heatmap", _editor.ShowHeatmap, value => { _editor.ShowHeatmap = value; _editor.InvalidateVisual(); }));
        left.Children.Add(Check("Rigidity field", _editor.ShowRigidity, value => { _editor.ShowRigidity = value; _editor.InvalidateVisual(); }));
        left.Children.Add(Header("Cores"));
        var list = new ListBox { MinHeight = 120, ItemsSource = _controller.Document.Config.Cores.Select(core => $"{core.Id}: {core.Name}").ToArray() };
        var selectedIndex = _controller.Document.Config.Cores.FindIndex(core => core.Id == _editor.SelectedCoreId);
        list.SelectedIndex = selectedIndex;
        list.SelectionChanged += (_, _) =>
        {
            _editor.SelectedCoreId = list.SelectedIndex >= 0 ? _controller.Document.Config.Cores[list.SelectedIndex].Id : null;
            BuildGelPanels();
            _editor.InvalidateVisual();
        };
        left.Children.Add(list);
        var coreButtons = Row();
        coreButtons.Children.Add(ActionButton("Duplicate", DuplicateCore));
        coreButtons.Children.Add(ActionButton("Delete", DeleteCore));
        left.Children.Add(coreButtons);

        var right = SectionStack("GEL PROPERTIES");
        var assetName = new TextBox { Text = _controller.Document.Config.AssetName };
        assetName.LostFocus += (_, _) =>
        {
            var value = assetName.Text?.Trim();
            if (!string.IsNullOrEmpty(value)) _controller.Mutate(config => config.AssetName = value[..Math.Min(256, value.Length)], DocumentChangeKind.Metadata);
        };
        right.Children.Add(Labeled("Asset name", assetName));
        right.Children.Add(Header("Runtime preview"));
        right.Children.Add(new TextBlock
        {
            Text = "These values are stored in GEL1 and drive the clean Lab preview.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = MutedBrush(),
            FontSize = 11
        });

        var bounceTint = new ComboBox
        {
            ItemsSource = new[] { "Off", "Random Neon" },
            SelectedIndex = _controller.Document.Config.BounceEffect.Tint == GelRuntimeSemantics.TintRandomNeon ? 1 : 0,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var tintIntensity = Slider(_controller.Document.Config.BounceEffect.TintIntensity, 0, 1);
        tintIntensity.IsEnabled = bounceTint.SelectedIndex == 1;
        var tintPercent = new TextBlock { Text = $"{tintIntensity.Value * 100:0}%", Foreground = MutedBrush(), FontSize = 11 };
        BindDocumentSlider(tintIntensity,
            (config, value) => config.BounceEffect.TintIntensity = value,
            DocumentChangeKind.RenderOnly,
            value => tintPercent.Text = $"{value * 100:0}%");
        bounceTint.SelectionChanged += (_, _) =>
        {
            var mode = bounceTint.SelectedIndex == 1 ? GelRuntimeSemantics.TintRandomNeon : GelRuntimeSemantics.TintOff;
            tintIntensity.IsEnabled = mode == GelRuntimeSemantics.TintRandomNeon;
            _controller.Mutate(config => config.BounceEffect.Tint = mode, DocumentChangeKind.RenderOnly);
        };
        right.Children.Add(Labeled("Bounce color", bounceTint));
        var tintPanel = new StackPanel { Spacing = 3 };
        tintPanel.Children.Add(tintIntensity);
        tintPanel.Children.Add(tintPercent);
        right.Children.Add(Labeled("Tint intensity", tintPanel));

        var opacity = Slider(_controller.Document.Config.Appearance.Opacity, 0, 1);
        var opacityPercent = new TextBlock { Text = $"{opacity.Value * 100:0}%", Foreground = MutedBrush(), FontSize = 11 };
        BindDocumentSlider(opacity,
            (config, value) => config.Appearance.Opacity = value,
            DocumentChangeKind.RenderOnly,
            value => opacityPercent.Text = $"{value * 100:0}%");
        var opacityPanel = new StackPanel { Spacing = 3 };
        opacityPanel.Children.Add(opacity);
        opacityPanel.Children.Add(opacityPercent);
        right.Children.Add(Labeled("Opacity", opacityPanel));

        var movementSpeed = Number(
            _controller.Document.Config.Motion.SpeedPixelsPerSecond,
            GelRuntimeSemantics.MinSpeedPixelsPerSecond,
            GelRuntimeSemantics.MaxSpeedPixelsPerSecond,
            10,
            "0");
        movementSpeed.ValueChanged += (_, _) => _controller.Mutate(config => config.Motion.SpeedPixelsPerSecond = (double)(movementSpeed.Value ?? 320m), DocumentChangeKind.Simulation);
        right.Children.Add(Labeled("Movement speed (px/s)", movementSpeed));

        right.Children.Add(Header("Material"));
        AddMaterialSlider(right, "Softness", 0, 1, () => _controller.Document.Config.Material.Softness, (config, value) => config.Material.Softness = value);
        AddMaterialSlider(right, "Damping", 0, 1, () => _controller.Document.Config.Material.Damping, (config, value) => config.Material.Damping = value);
        AddMaterialSlider(right, "Area preservation", 0, 1, () => _controller.Document.Config.Material.AreaPreservation, (config, value) => config.Material.AreaPreservation = value);
        AddMaterialSlider(right, "Shape memory", 0, 1, () => _controller.Document.Config.Material.ShapeMemory, (config, value) => config.Material.ShapeMemory = value);
        AddMaterialSlider(right, "Bend resistance", 0, 1, () => _controller.Document.Config.Material.BendResistance, (config, value) => config.Material.BendResistance = value);
        AddMaterialSlider(right, "Max stretch", 1.05, 3, () => _controller.Document.Config.Material.MaxStretch, (config, value) => config.Material.MaxStretch = value);
        right.Children.Add(Check("Self collision", _controller.Document.Config.Material.SelfCollision, value => _controller.Mutate(config => config.Material.SelfCollision = value)));
        var thickness = Number(_controller.Document.Config.Material.SelfCollisionThickness, 0.0001, 0.1, 0.001, "0.0000");
        thickness.ValueChanged += (_, _) => _controller.Mutate(config => config.Material.SelfCollisionThickness = (double)(thickness.Value ?? 0.008m));
        right.Children.Add(Labeled("Self-collision thickness", thickness));
        if (_controller.Document.Config.Cores.FirstOrDefault(core => core.Id == _editor.SelectedCoreId) is { } selected) AddCoreProperties(right, selected);
        SetPanels(left, right);
    }

    private void BuildLabPanels()
    {
        var left = SectionStack("LAB CONTROLS");
        left.Children.Add(Check("Gravity", _lab.GravityEnabled, value => _lab.GravityEnabled = value));
        left.Children.Add(Check("Hammer mode (H)", _lab.HammerMode, value => _lab.HammerMode = value));
        var pause = ActionButton(_lab.Paused ? "Resume (Space)" : "Pause (Space)", () => _lab.Paused = !_lab.Paused, wide: true);
        left.Children.Add(pause);
        left.Children.Add(ActionButton("Reset (R)", _lab.Reset, wide: true));
        left.Children.Add(Header("Simulation speed"));
        var speeds = new WrapPanel();
        foreach (var speed in new[] { 0.1, 0.25, 0.5, 1.0 }) speeds.Children.Add(ActionButton($"{speed:0.##}x", () => _lab.Speed = speed));
        left.Children.Add(speeds);
        left.Children.Add(Header("Directional SMACK"));
        left.Children.Add(ActionButton("▲ UP", () => _lab.Smack(-Vector2.UnitY), wide: true));
        var horizontal = Row();
        horizontal.Children.Add(ActionButton("◀ LEFT", () => _lab.Smack(-Vector2.UnitX)));
        horizontal.Children.Add(ActionButton("RIGHT ▶", () => _lab.Smack(Vector2.UnitX)));
        left.Children.Add(horizontal);
        left.Children.Add(ActionButton("▼ DOWN", () => _lab.Smack(Vector2.UnitY), wide: true));
        left.Children.Add(new TextBlock { Text = "Drag the gel to grab and throw it. In Hammer mode, click the side you want to hit.", TextWrapping = TextWrapping.Wrap, Foreground = MutedBrush() });

        var right = SectionStack("QUALITY & DIAGNOSTICS");
        var quality = new ComboBox { ItemsSource = Enum.GetValues<PhysicsQuality>(), SelectedItem = _lab.Quality, HorizontalAlignment = HorizontalAlignment.Stretch };
        var qualityDescription = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = MutedBrush() };
        void RefreshQualityDescription(PhysicsQuality value)
        {
            var settings = QualitySettings.For(value);
            qualityDescription.Text = $"{value}: ~{settings.MeshTarget}×{settings.MeshTarget} target mesh, {settings.PhysicsHz} Hz, {settings.SolverIterations} solver iterations, {settings.ContourSamples} contour samples.";
        }
        RefreshQualityDescription(_lab.Quality);
        quality.SelectionChanged += (_, _) =>
        {
            if (quality.SelectedItem is not PhysicsQuality value) return;
            _lab.Quality = value;
            RefreshQualityDescription(value);
        };
        right.Children.Add(Labeled("Physics preset", quality));
        right.Children.Add(qualityDescription);
        right.Children.Add(Header("Diagnostics"));
        right.Children.Add(Check("Deformation mesh (M)", _lab.ShowMesh, value => { _lab.ShowMesh = value; _lab.InvalidateVisual(); }));
        right.Children.Add(Check("Core ellipses", _lab.ShowCores, value => { _lab.ShowCores = value; _lab.InvalidateVisual(); }));
        right.Children.Add(Check("Core influence heatmap", _lab.ShowHeatmap, value => { _lab.ShowHeatmap = value; _lab.InvalidateVisual(); }));
        right.Children.Add(Check("Rigidity field", _lab.ShowRigidity, value => { _lab.ShowRigidity = value; _lab.InvalidateVisual(); }));
        right.Children.Add(Check("Alpha contour / contacts", _lab.ShowContour, value => { _lab.ShowContour = value; _lab.InvalidateVisual(); }));
        right.Children.Add(Check("Velocity vectors", _lab.ShowVelocity, value => { _lab.ShowVelocity = value; _lab.InvalidateVisual(); }));
        right.Children.Add(ActionButton("Clean / game view", _lab.SetCleanView, wide: true));
        SetPanels(left, right);
    }

    private void AddCoreProperties(StackPanel panel, CoreConfig core)
    {
        panel.Children.Add(Header($"Selected: {core.Name}"));
        var name = new TextBox { Text = core.Name };
        name.LostFocus += (_, _) => _controller.Mutate(_ => core.Name = (name.Text ?? string.Empty)[..Math.Min(128, (name.Text ?? string.Empty).Length)]);
        panel.Children.Add(Labeled("Name", name));
        AddCoreNumber(panel, "Center X", core.X, -1, 2, value => core.X = value);
        AddCoreNumber(panel, "Center Y", core.Y, -1, 2, value => core.Y = value);
        AddCoreNumber(panel, "Radius X", core.RadiusX, 0.001, 2, value => core.RadiusX = value);
        AddCoreNumber(panel, "Radius Y", core.RadiusY, 0.001, 2, value => core.RadiusY = value);
        AddCoreNumber(panel, "Mass", core.Mass, 0.1, 20, value => core.Mass = value);
        AddCoreNumber(panel, "Coupling", core.Coupling, 0, 1, value => core.Coupling = value);
        AddCoreNumber(panel, "Damping", core.Damping, 0, 1, value => core.Damping = value);
        AddCoreNumber(panel, "Local softness", core.SoftnessMultiplier, 0.1, 4, value => core.SoftnessMultiplier = value);
        AddCoreNumber(panel, "Influence falloff", core.Falloff, 0, 1, value => core.Falloff = value);
    }

    private void AddCoreNumber(StackPanel panel, string label, double value, double min, double max, Action<double> setter)
    {
        var number = Number(value, min, max, (max - min) / 100, "0.000");
        number.ValueChanged += (_, _) => _controller.Mutate(_ => setter((double)(number.Value ?? 0)));
        panel.Children.Add(Labeled(label, number));
    }

    private void AddMaterialSlider(StackPanel panel, string label, double min, double max, Func<double> getter, Action<GelConfig, double> setter)
    {
        var slider = Slider(getter(), min, max);
        BindDocumentSlider(slider, setter, DocumentChangeKind.Simulation);
        panel.Children.Add(Labeled(label, slider));
    }

    private void BindDocumentSlider(Slider slider, Action<GelConfig, double> mutation, DocumentChangeKind kind, Action<double>? uiChanged = null)
    {
        var compound = false;
        slider.PointerPressed += (_, e) =>
        {
            if (compound || !e.GetCurrentPoint(slider).Properties.IsLeftButtonPressed) return;
            _controller.BeginCompoundEdit();
            compound = true;
        };
        slider.PointerReleased += (_, _) => compound = false;
        slider.ValueChanged += (_, _) =>
        {
            uiChanged?.Invoke(slider.Value);
            if (compound) _controller.CompoundMutate(config => mutation(config, slider.Value), kind);
            else _controller.Mutate(config => mutation(config, slider.Value), kind);
        };
    }

    private async Task ApplyCropAsync()
    {
        if (_editor.CropRect is not ImagePixelRect crop) return;
        var document = _controller.Document;
        var oldWidth = document.Config.Image.Width;
        var oldHeight = document.Config.Image.Height;
        try
        {
            if (AnimatedImageProcessor.IsAnimated(document.Config) && _editor.EditCurrentAnimationFrameOnly)
            {
                var frameIndex = _editor.CurrentFrameIndex;
                _status.Text = $"Masking frame {frameIndex + 1} outside crop…";
                var visible = await Task.Run(() => AnimatedImageProcessor.TransformFrame(document.PngBytes, document.Config, frameIndex, frame => ImageAlphaEditing.ApplyRectCutout(frame, crop)));
                if (!ReferenceEquals(document, _controller.Document)) return;
                _controller.CommitStorage(visible, recoveryStorage: _controller.GetRecoveryStorage());
                _status.Text = $"Frame {frameIndex + 1}: pixels outside the crop are transparent; animation canvas unchanged.";
            }
            else
            {
                _status.Text = "Cropping image…";
                if (AnimatedImageProcessor.IsAnimated(document.Config))
                {
                    var visible = await Task.Run(() => AnimatedImageProcessor.TransformAnimated(document.PngBytes, document.Config, frame => RawRgbaTransforms.Crop(frame, crop)));
                    var recovery = await Task.Run(() => AnimatedImageProcessor.TransformAnimated(document.RecoveryPngBytes ?? document.PngBytes, document.Config, frame => RawRgbaTransforms.Crop(frame, crop)));
                    if (!ReferenceEquals(document, _controller.Document)) return;
                    _controller.CommitStorage(visible, config => ImageProcessor.RemapAuthoringForCrop(config, crop, oldWidth, oldHeight), recovery);
                }
                else
                {
                    var png = await Task.Run(() => RawRgbaTransforms.Crop(document.PngBytes, crop));
                    if (!ReferenceEquals(document, _controller.Document)) return;
                    _controller.CommitImage(png,
                        config => ImageProcessor.RemapAuthoringForCrop(config, crop, oldWidth, oldHeight),
                        recovery => RawRgbaTransforms.Crop(recovery, crop));
                }
            }
            _editor.CancelCrop();
            _editor.Mode = EditorMode.Select;
        }
        catch (Exception ex) { await Dialogs.ShowErrorAsync(this, "Crop failed", ex.Message); }
    }

    private async Task ApplyPolygonCutoutAsync()
    {
        if (!_editor.PolygonClosed)
        {
            _status.Text = "Close the polygon before applying the cutout.";
            return;
        }
        var polygon = _editor.GetPolygonSnapshot();
        var validation = PolygonGeometry.Validate(polygon);
        if (!validation.IsValid)
        {
            _status.Text = validation.Error!;
            return;
        }

        var document = _controller.Document;
        var oldWidth = document.Config.Image.Width;
        var oldHeight = document.Config.Image.Height;
        try
        {
            if (AnimatedImageProcessor.IsAnimated(document.Config) && _editor.EditCurrentAnimationFrameOnly)
            {
                var frameIndex = _editor.CurrentFrameIndex;
                _status.Text = $"Applying polygon cutout to frame {frameIndex + 1}…";
                var visible = await Task.Run(() => AnimatedImageProcessor.TransformFrame(document.PngBytes, document.Config, frameIndex, frame => ImageAlphaEditing.ApplyPolygonCutout(frame, polygon)));
                if (!ReferenceEquals(document, _controller.Document)) return;
                _controller.CommitStorage(visible, recoveryStorage: _controller.GetRecoveryStorage());
                _status.Text = $"Frame {frameIndex + 1}: polygon mask applied; animation canvas unchanged.";
            }
            else if (AnimatedImageProcessor.IsAnimated(document.Config))
            {
                _status.Text = "Applying polygon cutout and trimming transparent margins…";
                var result = await Task.Run(() =>
                {
                    var masked = AnimatedImageProcessor.TransformAnimated(document.PngBytes, document.Config, frame => ImageAlphaEditing.ApplyPolygonCutout(frame, polygon));
                    var maskedConfig = document.Config.DeepClone();
                    maskedConfig.Animation = masked.Animation?.DeepClone();
                    var bounds = AnimatedImageProcessor.FindUnionTrimBounds(masked.PngBytes, maskedConfig, 0);
                    if (bounds is null) return (Bounds: (ImagePixelRect?)null, Visible: (ImageStorageResult?)null, Recovery: (ImageStorageResult?)null);
                    var visible = AnimatedImageProcessor.TransformAnimated(masked.PngBytes, maskedConfig, frame => RawRgbaTransforms.Crop(frame, bounds.Value));
                    var recovery = AnimatedImageProcessor.TransformAnimated(document.RecoveryPngBytes ?? document.PngBytes, document.Config, frame => RawRgbaTransforms.Crop(frame, bounds.Value));
                    return (Bounds: (ImagePixelRect?)bounds.Value, Visible: (ImageStorageResult?)visible, Recovery: (ImageStorageResult?)recovery);
                });
                if (!ReferenceEquals(document, _controller.Document)) return;
                if (result.Bounds is not ImagePixelRect bounds || result.Visible is null || result.Recovery is null)
                {
                    _status.Text = "The polygon cutout would make every animation frame completely transparent; nothing was changed.";
                    return;
                }
                _controller.CommitStorage(result.Visible, config => ImageProcessor.RemapAuthoringForCrop(config, bounds, oldWidth, oldHeight), result.Recovery);
            }
            else
            {
                _status.Text = "Applying polygon cutout and trimming transparent margins…";
                var result = await Task.Run(() =>
                {
                    var masked = ImageAlphaEditing.ApplyPolygonCutout(document.PngBytes, polygon);
                    var bounds = RawRgbaTransforms.FindTrimBounds(masked, 0);
                    return bounds is null ? (Bounds: (ImagePixelRect?)null, Png: (byte[]?)null) :
                        (Bounds: (ImagePixelRect?)bounds.Value, Png: (byte[]?)RawRgbaTransforms.Crop(masked, bounds.Value));
                });
                if (!ReferenceEquals(document, _controller.Document)) return;
                if (result.Bounds is not ImagePixelRect bounds || result.Png is null)
                {
                    _status.Text = "The polygon cutout would make the image completely transparent; nothing was changed.";
                    return;
                }
                _controller.CommitImage(result.Png,
                    config => ImageProcessor.RemapAuthoringForCrop(config, bounds, oldWidth, oldHeight),
                    recovery => RawRgbaTransforms.Crop(recovery, bounds));
            }
            _editor.Mode = EditorMode.Select;
            RefreshChrome();
        }
        catch (GelFormatException ex) { _status.Text = ex.Message; }
        catch (Exception ex) { await Dialogs.ShowErrorAsync(this, "Polygon cutout failed", ex.Message); }
    }

    private async Task ResizeImageAsync(int width, int height)
    {
        var document = _controller.Document;
        try
        {
            _status.Text = $"Resizing image to {width} × {height}…";
            if (AnimatedImageProcessor.IsAnimated(document.Config))
            {
                var visible = await Task.Run(() => AnimatedImageProcessor.TransformAnimated(document.PngBytes, document.Config, frame => RawRgbaTransforms.Resize(frame, width, height)));
                var recovery = await Task.Run(() => AnimatedImageProcessor.TransformAnimated(document.RecoveryPngBytes ?? document.PngBytes, document.Config, frame => RawRgbaTransforms.Resize(frame, width, height)));
                if (ReferenceEquals(document, _controller.Document)) _controller.CommitStorage(visible, recoveryStorage: recovery);
            }
            else
            {
                var png = await Task.Run(() => RawRgbaTransforms.Resize(document.PngBytes, width, height));
                if (ReferenceEquals(document, _controller.Document))
                    _controller.CommitImage(png, recoveryTransform: recovery => RawRgbaTransforms.Resize(recovery, width, height));
            }
        }
        catch (Exception ex) { await Dialogs.ShowErrorAsync(this, "Resize failed", ex.Message); }
    }

    private async Task TrimTransparencyAsync()
    {
        try
        {
            var document = _controller.Document;
            var oldWidth = document.Config.Image.Width;
            var oldHeight = document.Config.Image.Height;
            var threshold = document.Config.Image.AlphaThreshold;
            _status.Text = "Finding transparent edges…";
            var bounds = await Task.Run(() => AnimatedImageProcessor.IsAnimated(document.Config)
                ? AnimatedImageProcessor.FindUnionTrimBounds(document.PngBytes, document.Config, threshold)
                : RawRgbaTransforms.FindTrimBounds(document.PngBytes, threshold));
            if (!ReferenceEquals(document, _controller.Document)) return;
            if (bounds is null)
            {
                await Dialogs.ShowErrorAsync(this, "Nothing to trim", "The image is completely transparent at the current alpha threshold.");
                RefreshChrome();
                return;
            }
            if (bounds.Value is { X: 0, Y: 0 } full && full.Width == oldWidth && full.Height == oldHeight)
            {
                RefreshChrome();
                return;
            }
            var trim = bounds.Value;
            if (AnimatedImageProcessor.IsAnimated(document.Config))
            {
                var visible = await Task.Run(() => AnimatedImageProcessor.TransformAnimated(document.PngBytes, document.Config, frame => RawRgbaTransforms.Crop(frame, trim)));
                var recovery = await Task.Run(() => AnimatedImageProcessor.TransformAnimated(document.RecoveryPngBytes ?? document.PngBytes, document.Config, frame => RawRgbaTransforms.Crop(frame, trim)));
                if (ReferenceEquals(document, _controller.Document))
                    _controller.CommitStorage(visible, config => ImageProcessor.RemapAuthoringForCrop(config, trim, oldWidth, oldHeight), recovery);
            }
            else
            {
                var png = await Task.Run(() => RawRgbaTransforms.Crop(document.PngBytes, trim));
                if (ReferenceEquals(document, _controller.Document))
                    _controller.CommitImage(png, config => ImageProcessor.RemapAuthoringForCrop(config, trim, oldWidth, oldHeight), recovery => RawRgbaTransforms.Crop(recovery, trim));
            }
        }
        catch (Exception ex) { await Dialogs.ShowErrorAsync(this, "Trim failed", ex.Message); }
    }

    private async void UpdateBackgroundPreview()
    {
        _backgroundPreviewCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _backgroundPreviewCancellation = cancellation;
        _backgroundPreview = null;
        var document = _controller.Document;
        try
        {
            await Task.Delay(75, cancellation.Token);
            _status.Text = "Updating background-removal preview…";
            var preview = await GenerateBackgroundPreviewAsync(cancellation.Token);
            if (!ReferenceEquals(_backgroundPreviewCancellation, cancellation) || !ReferenceEquals(document, _controller.Document)) return;
            _backgroundPreview = preview;
            _editor.SetPreview(preview);
            RefreshChrome();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (ReferenceEquals(_backgroundPreviewCancellation, cancellation))
            {
                _backgroundPreview = null;
                _editor.SetPreview(null);
                _status.Text = $"Background preview failed: {ex.Message}";
            }
        }
        finally
        {
            if (ReferenceEquals(_backgroundPreviewCancellation, cancellation)) _backgroundPreviewCancellation = null;
            cancellation.Dispose();
        }
    }

    private async Task ApplyBackgroundAsync()
    {
        _backgroundPreviewCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _backgroundPreviewCancellation = cancellation;
        var document = _controller.Document;
        try
        {
            _status.Text = "Applying background removal…";
            if (AnimatedImageProcessor.IsAnimated(document.Config))
            {
                var color = _backgroundColor;
                var tolerance = _backgroundTolerance;
                var feather = _backgroundFeather;
                ImageStorageResult visible;
                if (_editor.EditCurrentAnimationFrameOnly)
                {
                    var frameIndex = _editor.CurrentFrameIndex;
                    visible = await Task.Run(() => AnimatedImageProcessor.TransformFrame(document.PngBytes, document.Config, frameIndex, frame => RawRgbaTransforms.RemoveBackground(frame, color, tolerance, feather, cancellation.Token), cancellation.Token), cancellation.Token);
                }
                else
                {
                    visible = await Task.Run(() => AnimatedImageProcessor.TransformAnimated(document.PngBytes, document.Config, frame => RawRgbaTransforms.RemoveBackground(frame, color, tolerance, feather, cancellation.Token), cancellation.Token), cancellation.Token);
                }
                if (!ReferenceEquals(document, _controller.Document)) return;
                _controller.CommitStorage(visible, recoveryStorage: _controller.GetRecoveryStorage());
            }
            else
            {
                var preview = _backgroundPreview ?? await GenerateBackgroundPreviewAsync(cancellation.Token);
                if (!ReferenceEquals(document, _controller.Document)) return;
                _controller.CommitImage(preview);
            }
            _backgroundPreview = null;
            _editor.SetPreview(null);
            _editor.Mode = EditorMode.Select;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex) { await Dialogs.ShowErrorAsync(this, "Background removal failed", ex.Message); }
        finally
        {
            if (ReferenceEquals(_backgroundPreviewCancellation, cancellation)) _backgroundPreviewCancellation = null;
            cancellation.Dispose();
        }
    }

    private Task<byte[]> GenerateBackgroundPreviewAsync(CancellationToken cancellationToken)
    {
        var png = _controller.GetFramePng(_editor.CurrentFrameIndex);
        var color = _backgroundColor;
        var tolerance = _backgroundTolerance;
        var feather = _backgroundFeather;
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = RawRgbaTransforms.RemoveBackground(png, color, tolerance, feather, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }, cancellationToken);
    }

    private void ClearBackgroundPreview()
    {
        _backgroundPreviewCancellation?.Cancel();
        _backgroundPreviewCancellation = null;
        _backgroundPreview = null;
        _editor.SetPreview(null);
    }

    private void CancelBackground()
    {
        ClearBackgroundPreview();
        _editor.Mode = EditorMode.Select;
    }

    private void DuplicateCore()
    {
        var source = _controller.Document.Config.Cores.FirstOrDefault(core => core.Id == _editor.SelectedCoreId);
        if (source is null) return;
        if (_controller.Document.Config.Cores.Count >= GelValidator.MaxCores)
        {
            _status.Text = $"A GEL asset may contain at most {GelValidator.MaxCores} cores.";
            return;
        }
        var usedIds = _controller.Document.Config.Cores.Select(core => core.Id).ToHashSet();
        var id = 1;
        while (usedIds.Contains(id)) id++;
        _controller.Mutate(config => config.Cores.Add(new CoreConfig
        {
            Id = id, Name = $"{source.Name} Copy", X = Math.Clamp(source.X + 0.03, -1, 2), Y = Math.Clamp(source.Y + 0.03, -1, 2),
            RadiusX = source.RadiusX, RadiusY = source.RadiusY, Mass = source.Mass, Coupling = source.Coupling,
            Damping = source.Damping, SoftnessMultiplier = source.SoftnessMultiplier, Falloff = source.Falloff
        }));
        _editor.SelectedCoreId = id;
        BuildGelPanels();
    }

    private void DeleteCore()
    {
        if (_editor.SelectedCoreId is not int id) return;
        _controller.Mutate(config => config.Cores.RemoveAll(core => core.Id == id));
        _editor.SelectedCoreId = null;
        BuildGelPanels();
    }

    private async Task OpenPickerAsync()
    {
        if (!await ConfirmDiscardAsync()) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open image or GEL asset",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Gelatin assets and images") { Patterns = ["*.gel", "*.png", "*.jpg", "*.jpeg", "*.webp", "*.gif"] }]
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path is not null) await OpenPathAsync(path);
    }

    private async Task OpenPathAsync(string path)
    {
        try
        {
            await _controller.OpenAsync(path);
            _editor.EditCurrentAnimationFrameOnly = false;
            _editor.ResetAnimationPlayback();
            ShowWorkspace(Workspace.Asset);
        }
        catch (Exception ex) { await Dialogs.ShowErrorAsync(this, "Could not open asset", ex.Message); }
    }

    private async Task SaveAsync(bool saveAs)
    {
        try
        {
            var path = !saveAs ? _controller.CurrentPath : null;
            if (path is null)
            {
                var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Save GEL asset",
                    SuggestedFileName = SanitizeFileName(_controller.Document.Config.AssetName) + ".gel",
                    DefaultExtension = "gel",
                    FileTypeChoices = [new FilePickerFileType("Gelatin asset") { Patterns = ["*.gel"] }]
                });
                path = file?.TryGetLocalPath();
            }
            if (path is not null) await _controller.SaveAsync(path);
        }
        catch (Exception ex) { await Dialogs.ShowErrorAsync(this, "Could not save asset", ex.Message); }
    }

    private async Task ExportPngAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Export processed PNG", SuggestedFileName = SanitizeFileName(_controller.Document.Config.AssetName) + ".png", DefaultExtension = "png", FileTypeChoices = [new FilePickerFileType("PNG image") { Patterns = ["*.png"] }] });
        var path = file?.TryGetLocalPath();
        if (path is not null) try { _controller.ExportPng(path); } catch (Exception ex) { await Dialogs.ShowErrorAsync(this, "Export failed", ex.Message); }
    }

    private async Task ExportJsonAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Export GEL JSON", SuggestedFileName = SanitizeFileName(_controller.Document.Config.AssetName) + ".json", DefaultExtension = "json", FileTypeChoices = [new FilePickerFileType("JSON configuration") { Patterns = ["*.json"] }] });
        var path = file?.TryGetLocalPath();
        if (path is not null) try { _controller.ExportJson(path); } catch (Exception ex) { await Dialogs.ShowErrorAsync(this, "Export failed", ex.Message); }
    }

    private async Task<bool> ConfirmDiscardAsync() => !_controller.IsDirty || await Dialogs.ConfirmAsync(this, "Unsaved changes", "Discard the current unsaved changes?", "Discard");

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        var file = e.DataTransfer.TryGetFiles()?.FirstOrDefault();
        var path = file?.TryGetLocalPath();
        if (path is not null && await ConfirmDiscardAsync()) await OpenPathAsync(path);
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_forceClose || !_controller.IsDirty) return;
        e.Cancel = true;
        if (await Dialogs.ConfirmAsync(this, "Unsaved changes", "Close Gelatin and discard the current unsaved changes?", "Discard and close"))
        {
            _forceClose = true;
            Close();
        }
    }

    private async void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is TextBox or NumericUpDown) return;
        var control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (control && e.Key == Key.O) { e.Handled = true; await OpenPickerAsync(); }
        else if (control && e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Shift)) { e.Handled = true; await SaveAsync(true); }
        else if (control && e.Key == Key.S) { e.Handled = true; await SaveAsync(false); }
        else if (control && e.Key == Key.Z) { e.Handled = true; _controller.Undo(); }
        else if (control && e.Key == Key.Y) { e.Handled = true; _controller.Redo(); }
        else if (_workspace == Workspace.Asset && _editor.HandleEditorKey(e.Key, e.KeyModifiers)) e.Handled = true;
        else if (_workspace == Workspace.Lab && e.KeyModifiers == KeyModifiers.None)
        {
            if (e.Key == Key.Space) { _lab.Paused = !_lab.Paused; e.Handled = true; }
            else if (e.Key == Key.R) { _lab.Reset(); e.Handled = true; }
            else if (e.Key == Key.H) { _lab.HammerMode = !_lab.HammerMode; e.Handled = true; }
            else if (e.Key == Key.M) { _lab.ShowMesh = !_lab.ShowMesh; _lab.InvalidateVisual(); e.Handled = true; }
        }
    }

    private void UpdatePolygonChrome()
    {
        if (_polygonApplyButton is not null) _polygonApplyButton.IsEnabled = _editor.PolygonCanApply;
        if (_workspace != Workspace.Asset || _editor.Mode != EditorMode.PolygonCutout) return;
        var validation = _editor.PolygonClosed ? PolygonGeometry.Validate(_editor.PolygonPoints) : default;
        var state = _editor.PolygonClosed ? validation.IsValid ? "closed / ready" : validation.Error : "placing points";
        _status.Text = $"Polygon: {_editor.PolygonPoints.Count} vertices — {state}";
    }

    private void RefreshChrome()
    {
        var dirty = _controller.IsDirty ? " *" : string.Empty;
        Title = $"Gelatin {GelatinProduct.Version} — {_controller.Document.Config.AssetName}{dirty}";
        var config = _controller.Document.Config;
        var animation = config.Animation is { } animated ? $"   |   {animated.Frames.Count} animated frame(s)" : string.Empty;
        _status.Text = $"{config.Image.Width} × {config.Image.Height} px{animation}   |   {config.Cores.Count} core(s)   |   {config.RigidityStrokes.Count} rigidity stroke(s)   |   {(_controller.IsDirty ? "Unsaved changes" : Path.GetFileName(_controller.CurrentPath))}";
        RefreshAnimationControls();
    }

    private void SetPanels(Control left, Control right)
    {
        _leftHost.Child = new ScrollViewer { Content = left, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        _rightHost.Child = new ScrollViewer { Content = right, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
    }

    private static Border PanelHost(double width) => new()
    {
        Width = width,
        Background = new SolidColorBrush(Color.Parse("#202027")),
        BorderBrush = new SolidColorBrush(Color.Parse("#34343D")),
        BorderThickness = new Thickness(1, 0)
    };

    private static StackPanel SectionStack(string title) => new()
    {
        Margin = new Thickness(12),
        Spacing = 9,
        Children = { new TextBlock { Text = title, FontWeight = FontWeight.Bold, FontSize = 12, Foreground = new SolidColorBrush(Color.Parse("#B9A6FF")) } }
    };

    private static TextBlock Header(string text) => new() { Text = text, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 8, 0, 0) };
    private static StackPanel Row() => new() { Orientation = Orientation.Horizontal, Spacing = 6 };
    private static Control Separator() => new Border { Width = 1, Height = 28, Margin = new Thickness(7, 2), Background = new SolidColorBrush(Color.Parse("#4A4A55")) };
    private static IBrush MutedBrush() => new SolidColorBrush(Color.Parse("#A7A7B2"));

    private static Button ActionButton(string label, Action action, bool wide = false, bool accent = false)
    {
        var button = CreateActionButton(label, wide, accent);
        button.Click += (_, _) => action();
        return button;
    }

    private static Button ActionButton(string label, Func<Task> action, bool wide = false, bool accent = false)
    {
        var button = CreateActionButton(label, wide, accent);
        button.Click += async (_, _) => await action();
        return button;
    }

    private static Button CreateActionButton(string label, bool wide, bool accent) => new()
    {
        Content = label,
        MinWidth = wide ? 150 : 74,
        HorizontalAlignment = wide ? HorizontalAlignment.Stretch : HorizontalAlignment.Left,
        HorizontalContentAlignment = HorizontalAlignment.Center,
        Margin = new Thickness(2),
        Background = accent ? new SolidColorBrush(Color.Parse("#423664")) : null
    };

    private static Control Labeled(string label, Control control) => new StackPanel { Spacing = 3, Children = { new TextBlock { Text = label, Foreground = MutedBrush(), FontSize = 12 }, control } };
    private static Slider Slider(double value, double min, double max) => new() { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), HorizontalAlignment = HorizontalAlignment.Stretch };

    private static NumericUpDown Number(double value, double min, double max, double increment, string format = "0") => new()
    {
        Minimum = (decimal)min,
        Maximum = (decimal)max,
        Value = (decimal)Math.Clamp(value, min, max),
        Increment = (decimal)Math.Max(increment, 0.0001),
        FormatString = format,
        ClipValueToMinMax = true,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };

    private static CheckBox Check(string label, bool value, Action<bool> changed)
    {
        var check = new CheckBox { Content = label, IsChecked = value };
        check.IsCheckedChanged += (_, _) => changed(check.IsChecked == true);
        return check;
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '_');
        return string.IsNullOrWhiteSpace(name) ? "asset" : name.Trim();
    }

    private enum Workspace { Asset, Gel, Lab }
}
