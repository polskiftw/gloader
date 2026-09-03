using System.Diagnostics;

namespace WorldFamilyRenderer;

internal sealed class MainForm : Form
{
    private readonly TextBox _sourceBox = new() { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly TextBox _terrariaBox = new() { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly TextBox _outputBox = new() { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly Label _sourceInfoLabel = new() { AutoSize = true, Text = "Choose a .wld. The seed, difficulty, evil, and classic special-seed flags are read from its header." };
    private readonly Label _runtimeInfoLabel = new() { AutoSize = true, Text = "Terraria/gloader folder not detected yet." };
    private readonly Label _qualityValueLabel = new() { AutoSize = true };
    private readonly TrackBar _qualitySlider = new()
    {
        Minimum = 1,
        Maximum = 6,
        Value = 4,
        TickStyle = TickStyle.BottomRight,
        Dock = DockStyle.Fill
    };
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 100, Dock = DockStyle.Fill };
    private readonly Label _statusLabel = new() { AutoSize = false, Height = 42, Text = "Ready." };
    private readonly Button _generateButton = new() { Text = "GENERATE SIX PNGs", AutoSize = true, Height = 38 };
    private readonly Button _cancelButton = new() { Text = "Cancel", AutoSize = true, Enabled = false, Height = 38 };
    private readonly Button _openOutputButton = new() { Text = "Open output", AutoSize = true, Enabled = false, Height = 38 };

    private SourceWorldInfo _sourceInfo;
    private CancellationTokenSource _cancellation;
    private GenerationEngine _currentEngine;
    private string _lastOutputDirectory;

    public MainForm()
    {
        Text = "World Family Renderer";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 500);
        Size = new Size(860, 545);
        Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
        AllowDrop = true;

        BuildLayout();
        WireEvents();
        UpdateQualityLabel();

        Shown += (_, _) => AutoDetectRuntime();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 1,
            RowCount = 12,
            AutoScroll = true
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var title = new Label
        {
            Text = "World Family Renderer",
            Font = new Font(Font.FontFamily, 18F, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 4)
        };
        var subtitle = new Label
        {
            Text = "Feed it one .wld. It generates fresh Small / Medium / Large / XL / Huge / THICC worlds from the same seed, renders them with TEdit colors, then throws the temporary worlds away.",
            AutoSize = true,
            MaximumSize = new Size(790, 0),
            Margin = new Padding(0, 0, 0, 12)
        };

        root.Controls.Add(title);
        root.Controls.Add(subtitle);
        root.Controls.Add(BuildFileRow("Source .wld", _sourceBox, "Browse...", BrowseSourceWorld));
        root.Controls.Add(_sourceInfoLabel);
        root.Controls.Add(BuildFileRow("Terraria / gloader folder", _terrariaBox, "Browse...", BrowseTerrariaFolder));
        root.Controls.Add(_runtimeInfoLabel);
        root.Controls.Add(BuildFileRow("Output folder", _outputBox, "Browse...", BrowseOutputFolder));
        root.Controls.Add(BuildQualityRow());

        var note = new Label
        {
            Text = "Output is always 7 PNGs: one comparison sheet like your example + six individual world maps. Higher quality means more sampled TEdit pixels. No world files are kept.",
            AutoSize = true,
            MaximumSize = new Size(790, 0),
            Margin = new Padding(0, 8, 0, 10)
        };
        root.Controls.Add(note);
        root.Controls.Add(_progress);
        root.Controls.Add(_statusLabel);
        root.Controls.Add(BuildButtonRow());

        Controls.Add(root);
    }

    private Control BuildFileRow(string labelText, TextBox textBox, string buttonText, EventHandler handler)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 3,
            Margin = new Padding(0, 5, 0, 3)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        panel.Controls.Add(new Label
        {
            Text = labelText,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 8, 0)
        }, 0, 0);
        panel.Controls.Add(textBox, 1, 0);
        var button = new Button { Text = buttonText, AutoSize = true, Margin = new Padding(8, 0, 0, 0) };
        button.Click += handler;
        panel.Controls.Add(button, 2, 0);
        return panel;
    }

    private Control BuildQualityRow()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 3,
            Margin = new Padding(0, 8, 0, 0)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));

        panel.Controls.Add(new Label
        {
            Text = "Quality",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 12, 8, 0)
        }, 0, 0);
        panel.Controls.Add(_qualitySlider, 1, 0);
        _qualityValueLabel.Anchor = AnchorStyles.Left;
        panel.Controls.Add(_qualityValueLabel, 2, 0);
        return panel;
    }

    private Control BuildButtonRow()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 8, 0, 0)
        };
        panel.Controls.Add(_generateButton);
        panel.Controls.Add(_cancelButton);
        panel.Controls.Add(_openOutputButton);
        return panel;
    }

    private void WireEvents()
    {
        _qualitySlider.ValueChanged += (_, _) => UpdateQualityLabel();
        _generateButton.Click += async (_, _) => await GenerateAsync();
        _cancelButton.Click += (_, _) => CancelWork();
        _openOutputButton.Click += (_, _) => OpenLastOutput();

        DragEnter += (_, e) =>
        {
            if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files?.Any(file => file.EndsWith(".wld", StringComparison.OrdinalIgnoreCase)) == true)
                    e.Effect = DragDropEffects.Copy;
            }
        };
        DragDrop += (_, e) =>
        {
            string[] files = (string[])e.Data?.GetData(DataFormats.FileDrop);
            string wld = files?.FirstOrDefault(file => file.EndsWith(".wld", StringComparison.OrdinalIgnoreCase));
            if (wld != null) SetSourceWorld(wld);
        };

        FormClosing += (_, _) => CancelWork();
    }

    private void AutoDetectRuntime()
    {
        string root = RuntimeLocator.TryAutoDetect();
        if (root == null) return;

        _terrariaBox.Text = root;
        SettingsStore.SaveTerrariaRoot(root);
        UpdateRuntimeStatus();
    }

    private void BrowseSourceWorld(object sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Terraria world (*.wld)|*.wld|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
            InitialDirectory = Directory.Exists(_outputBox.Text) ? _outputBox.Text : AppContext.BaseDirectory
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            SetSourceWorld(dialog.FileName);
    }

    private void SetSourceWorld(string path)
    {
        try
        {
            Cursor = Cursors.WaitCursor;
            _sourceInfo = SourceWorldInfo.Load(path);
            _sourceBox.Text = _sourceInfo.FilePath;
            _sourceInfoLabel.Text = _sourceInfo.DisplaySummary;
            _outputBox.Text = Path.GetDirectoryName(_sourceInfo.FilePath) ?? AppContext.BaseDirectory;
            _statusLabel.Text = "Source world loaded. No tiles from this source world will be rendered or reused.";
        }
        catch (Exception ex)
        {
            _sourceInfo = null;
            _sourceBox.Clear();
            _sourceInfoLabel.Text = "Could not read that world.";
            MessageBox.Show(this, ex.Message, "Could not read .wld", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void BrowseTerrariaFolder(object sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose the Terraria installation folder that contains gloader.exe.",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(_terrariaBox.Text) ? _terrariaBox.Text : string.Empty
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        _terrariaBox.Text = dialog.SelectedPath;
        SettingsStore.SaveTerrariaRoot(dialog.SelectedPath);
        UpdateRuntimeStatus(showError: true);
    }

    private void BrowseOutputFolder(object sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose where the PNG folder should be created.",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(_outputBox.Text) ? _outputBox.Text : string.Empty
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _outputBox.Text = dialog.SelectedPath;
    }

    private void UpdateRuntimeStatus(bool showError = false)
    {
        try
        {
            RuntimePaths paths = RuntimePaths.Validate(_terrariaBox.Text);
            _runtimeInfoLabel.Text =
                $"64-bit runtime: {Path.GetFileName(paths.X64RuntimeDll)}   |   Expanded Worlds gmod: ready";
        }
        catch (Exception ex)
        {
            _runtimeInfoLabel.Text = "Runtime not ready: " + ex.Message;
            if (showError)
                MessageBox.Show(this, ex.Message, "Terraria / gloader folder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void UpdateQualityLabel()
    {
        QualityLevel level = QualityLevel.FromSlider(_qualitySlider.Value);
        _qualityValueLabel.Text = $"{level.Name} — {level.MaxWorldWidth:N0}px max width";
    }

    private async Task GenerateAsync()
    {
        if (_sourceInfo == null)
        {
            MessageBox.Show(this, "Choose a source .wld first.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        RuntimePaths runtime;
        try
        {
            runtime = RuntimePaths.Validate(_terrariaBox.Text);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "64-bit gloader is not ready", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (string.IsNullOrWhiteSpace(_outputBox.Text) || !Directory.Exists(_outputBox.Text))
        {
            MessageBox.Show(this, "Choose a valid output folder.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        QualityLevel quality = QualityLevel.FromSlider(_qualitySlider.Value);
        _cancellation = new CancellationTokenSource();
        _generateButton.Enabled = false;
        _cancelButton.Enabled = true;
        _openOutputButton.Enabled = false;
        _progress.Value = 0;
        _statusLabel.Text = "Starting...";

        var status = new Progress<string>(message => _statusLabel.Text = message);
        var progress = new Progress<int>(value => _progress.Value = Math.Clamp(value, 0, 100));

        try
        {
            _lastOutputDirectory = await WorldFamilyJob.RunAsync(
                _sourceInfo,
                runtime,
                _outputBox.Text,
                quality,
                status,
                progress,
                _cancellation.Token,
                engine => _currentEngine = engine);

            _openOutputButton.Enabled = Directory.Exists(_lastOutputDirectory);
            System.Media.SystemSounds.Asterisk.Play();
            MessageBox.Show(
                this,
                "Done. The comparison sheet and all six individual PNGs are in:\n\n" + _lastOutputDirectory,
                "World family complete",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = "Canceled.";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Stopped on an error.";
            MessageBox.Show(
                this,
                ex.Message,
                "World Family Renderer stopped",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            _currentEngine = null;
            _cancellation?.Dispose();
            _cancellation = null;
            _generateButton.Enabled = true;
            _cancelButton.Enabled = false;
        }
    }

    private void CancelWork()
    {
        try { _cancellation?.Cancel(); } catch { }
        try { _currentEngine?.CancelCurrentProcess(); } catch { }
    }

    private void OpenLastOutput()
    {
        if (!Directory.Exists(_lastOutputDirectory)) return;
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = "\"" + _lastOutputDirectory + "\"",
            UseShellExecute = true
        });
    }
}
