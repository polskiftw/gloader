using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GLoader.ExpandedWorldMaker
{
    internal sealed class WorldMakerForm : Form
    {
        private readonly Color _bg = Color.FromArgb(24, 22, 31);
        private readonly Color _panel = Color.FromArgb(36, 33, 47);
        private readonly Color _field = Color.FromArgb(49, 45, 63);
        private readonly Color _accent = Color.FromArgb(151, 112, 232);
        private readonly Color _muted = Color.FromArgb(187, 181, 201);
        private readonly Color _text = Color.FromArgb(245, 243, 250);

        private readonly string _packageRoot;
        private readonly SettingsStore _settings;

        private RadioButton _xl;
        private RadioButton _huge;
        private RadioButton _thicc;
        private TextBox _worldName;
        private TextBox _seed;
        private ComboBox _difficulty;
        private readonly Dictionary<SecretSeedOption, CheckBox> _secretChecks = new Dictionary<SecretSeedOption, CheckBox>();
        private TextBox _outputFolder;
        private TextBox _serverPath;
        private Label _runtimeStatus;
        private Button _generate;
        private Button _cancel;
        private Button _openFolder;
        private Button _logToggle;
        private ProgressBar _progress;
        private Label _status;
        private TextBox _log;
        private CancellationTokenSource _cts;
        private WorldGenerationJob _job;
        private string _lastOutputPath;

        public WorldMakerForm()
        {
            _packageRoot = RuntimeLocator.FindPackageRoot();
            _settings = new SettingsStore();

            Text = "Expanded World Maker";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(820, 760);
            Size = new Size(900, 900);
            BackColor = _bg;
            ForeColor = _text;
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
            AutoScaleMode = AutoScaleMode.Dpi;
            FormClosing += OnFormClosing;

            BuildUi();
            LoadSettings();
            RefreshRuntimeStatus();
        }

        private void BuildUi()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 9,
                Padding = new Padding(22, 18, 22, 18),
                BackColor = _bg,
                AutoScroll = true
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            Controls.Add(root);

            var title = new Label
            {
                AutoSize = true,
                Text = "Expanded World Maker",
                Font = new Font("Segoe UI Semibold", 22f, FontStyle.Bold),
                ForeColor = _text,
                Margin = new Padding(0, 0, 0, 2)
            };
            root.Controls.Add(title);

            var subtitle = new Label
            {
                AutoSize = true,
                Text = "Generate XL, Huge, and THICC Terraria 1.4.5.8 worlds in the headless server instead of inside the game.",
                ForeColor = _muted,
                Margin = new Padding(2, 0, 0, 15)
            };
            root.Controls.Add(subtitle);

            root.Controls.Add(BuildWorldCard());
            root.Controls.Add(BuildSecretSeedCard());
            root.Controls.Add(BuildOutputCard());
            root.Controls.Add(BuildRuntimeCard());
            root.Controls.Add(BuildActionCard());

            _log = new TextBox
            {
                Dock = DockStyle.Top,
                Height = 180,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                ReadOnly = true,
                WordWrap = false,
                BackColor = Color.FromArgb(17, 16, 23),
                ForeColor = Color.FromArgb(215, 211, 226),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 8.5f),
                Visible = false,
                Margin = new Padding(0, 10, 0, 0)
            };
            root.Controls.Add(_log);
        }

        private Control BuildWorldCard()
        {
            var card = MakeCard("WORLD");
            var body = (TableLayoutPanel)card.Controls[0];

            var sizeLabel = MakeLabel("Size", false);
            body.Controls.Add(sizeLabel, 0, body.RowCount++);

            var sizes = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, Margin = new Padding(0, 4, 0, 12) };
            sizes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
            sizes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
            sizes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
            _xl = MakePresetRadio("XL\r\n12,600 x 2,400");
            _huge = MakePresetRadio("Huge\r\n16,800 x 2,400");
            _thicc = MakePresetRadio("THICC\r\n16,800 x 4,800");
            sizes.Controls.Add(_xl, 0, 0);
            sizes.Controls.Add(_huge, 1, 0);
            sizes.Controls.Add(_thicc, 2, 0);
            body.Controls.Add(sizes, 0, body.RowCount++);

            var fields = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = new Padding(0) };
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68f));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32f));

            var namePanel = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, RowCount = 2, Margin = new Padding(0, 0, 8, 0) };
            namePanel.Controls.Add(MakeLabel("World name", false));
            _worldName = MakeTextBox();
            _worldName.MaxLength = 26;
            namePanel.Controls.Add(_worldName);

            var diffPanel = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, RowCount = 2, Margin = new Padding(8, 0, 0, 0) };
            diffPanel.Controls.Add(MakeLabel("Difficulty", false));
            _difficulty = new ComboBox
            {
                Dock = DockStyle.Top,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = _field,
                ForeColor = _text,
                Height = 30
            };
            _difficulty.Items.AddRange(new object[] { "Classic", "Expert", "Master", "Journey" });
            _difficulty.SelectedIndex = 0;
            diffPanel.Controls.Add(_difficulty);

            fields.Controls.Add(namePanel, 0, 0);
            fields.Controls.Add(diffPanel, 1, 0);
            body.Controls.Add(fields, 0, body.RowCount++);

            body.Controls.Add(MakeLabel("Seed", false), 0, body.RowCount++);
            _seed = MakeTextBox();
            _seed.MaxLength = 40;
            body.Controls.Add(_seed, 0, body.RowCount++);
            body.Controls.Add(MakeLabel("Leave blank for random. Legacy magic seed text still works here; the special-seed switches below can also be combined with any seed.", true), 0, body.RowCount++);

            return card;
        }

        private Control BuildSecretSeedCard()
        {
            var card = MakeCard("SPECIAL / SECRET SEEDS");
            var body = (TableLayoutPanel)card.Controls[0];
            body.Controls.Add(MakeLabel("Terraria 1.4.5 server flags. Pick none, one, or combine them.", true), 0, body.RowCount++);

            var grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, Margin = new Padding(0, 6, 0, 0) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
            var tip = new ToolTip { AutomaticDelay = 250, AutoPopDelay = 12000 };

            for (int i = 0; i < SecretSeedOption.All.Length; i++)
            {
                SecretSeedOption option = SecretSeedOption.All[i];
                var check = new CheckBox
                {
                    AutoSize = true,
                    Text = option.Label,
                    ForeColor = _text,
                    BackColor = _panel,
                    Margin = new Padding(3, 6, 10, 6)
                };
                _secretChecks[option] = check;
                tip.SetToolTip(check, option.Hint + "  Server flag: seed_" + option.ConfigName + "=1");
                grid.Controls.Add(check, i % 3, i / 3);
            }

            body.Controls.Add(grid, 0, body.RowCount++);
            return card;
        }

        private Control BuildOutputCard()
        {
            var card = MakeCard("SAVE .WLD TO");
            var body = (TableLayoutPanel)card.Controls[0];
            body.Controls.Add(MakeLabel("Choose your Terraria Worlds folder or any other folder. The existing file is only replaced after generation succeeds.", true), 0, body.RowCount++);

            var row = MakeBrowseRow(out _outputFolder, "Browse...", BrowseOutputFolder);
            body.Controls.Add(row, 0, body.RowCount++);
            return card;
        }

        private Control BuildRuntimeCard()
        {
            var card = MakeCard("HEADLESS RUNTIME");
            var body = (TableLayoutPanel)card.Controls[0];
            body.Controls.Add(MakeLabel("Uses gloader + only the ExpandedWorlds mod + TerrariaServer.exe. Your other gmods are not loaded into the generator.", true), 0, body.RowCount++);

            var row = MakeBrowseRow(out _serverPath, "Server...", BrowseServer);
            _serverPath.TextChanged += delegate { RefreshRuntimeStatus(); };
            body.Controls.Add(row, 0, body.RowCount++);

            _runtimeStatus = MakeLabel("", true);
            _runtimeStatus.Margin = new Padding(0, 4, 0, 0);
            body.Controls.Add(_runtimeStatus, 0, body.RowCount++);
            return card;
        }

        private Control BuildActionCard()
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 1,
                Margin = new Padding(0, 12, 0, 0),
                BackColor = _bg
            };

            _progress = new ProgressBar { Dock = DockStyle.Top, Height = 17, Minimum = 0, Maximum = 100, Value = 0, Style = ProgressBarStyle.Continuous };
            panel.Controls.Add(_progress);
            _status = MakeLabel("Ready.", true);
            _status.Margin = new Padding(0, 7, 0, 8);
            panel.Controls.Add(_status);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0) };
            _generate = MakeButton("GENERATE WORLD", true);
            _generate.Width = 180;
            _generate.Click += async delegate { await GenerateAsync(); };
            _cancel = MakeButton("Cancel", false);
            _cancel.Enabled = false;
            _cancel.Click += delegate { CancelGeneration(); };
            _openFolder = MakeButton("Open output folder", false);
            _openFolder.Enabled = false;
            _openFolder.Click += delegate { OpenOutputFolder(); };
            _logToggle = MakeButton("Show log", false);
            _logToggle.Click += delegate { ToggleLog(); };
            buttons.Controls.Add(_generate);
            buttons.Controls.Add(_cancel);
            buttons.Controls.Add(_openFolder);
            buttons.Controls.Add(_logToggle);
            panel.Controls.Add(buttons);
            return panel;
        }

        private Panel MakeCard(string title)
        {
            var card = new Panel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                BackColor = _panel,
                Padding = new Padding(16, 13, 16, 15),
                Margin = new Padding(0, 0, 0, 11)
            };
            var body = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 0, BackColor = _panel };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            body.Controls.Add(new Label
            {
                AutoSize = true,
                Text = title,
                Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold),
                ForeColor = _accent,
                Margin = new Padding(0, 0, 0, 8)
            }, 0, body.RowCount++);
            card.Controls.Add(body);
            return card;
        }

        private RadioButton MakePresetRadio(string text)
        {
            var radio = new RadioButton
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                Height = 58,
                Text = text,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(9, 0, 6, 0),
                BackColor = _field,
                ForeColor = _text,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI Semibold", 9.5f),
                Margin = new Padding(0, 0, 8, 0)
            };
            radio.FlatAppearance.BorderColor = Color.FromArgb(91, 84, 112);
            radio.CheckedChanged += delegate
            {
                radio.BackColor = radio.Checked ? Color.FromArgb(69, 54, 95) : _field;
                radio.FlatAppearance.BorderColor = radio.Checked ? _accent : Color.FromArgb(91, 84, 112);
            };
            return radio;
        }

        private TextBox MakeTextBox()
        {
            return new TextBox
            {
                Dock = DockStyle.Top,
                BackColor = _field,
                ForeColor = _text,
                BorderStyle = BorderStyle.FixedSingle,
                Height = 30,
                Margin = new Padding(0, 3, 0, 4)
            };
        }

        private Label MakeLabel(string text, bool muted)
        {
            return new Label
            {
                AutoSize = true,
                MaximumSize = new Size(800, 0),
                Text = text,
                ForeColor = muted ? _muted : _text,
                Margin = new Padding(0, 2, 0, 2)
            };
        }

        private Button MakeButton(string text, bool primary)
        {
            var button = new Button
            {
                AutoSize = false,
                Height = 36,
                Width = 120,
                Text = text,
                FlatStyle = FlatStyle.Flat,
                BackColor = primary ? _accent : _field,
                ForeColor = primary ? Color.White : _text,
                Margin = new Padding(0, 0, 8, 0),
                Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderColor = primary ? Color.FromArgb(190, 159, 248) : Color.FromArgb(91, 84, 112);
            return button;
        }

        private Control MakeBrowseRow(out TextBox textBox, string buttonText, EventHandler browseHandler)
        {
            var row = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 6, 0, 0) };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105f));
            textBox = MakeTextBox();
            textBox.Margin = new Padding(0, 0, 8, 0);
            var button = MakeButton(buttonText, false);
            button.Dock = DockStyle.Top;
            button.Width = 97;
            button.Click += browseHandler;
            row.Controls.Add(textBox, 0, 0);
            row.Controls.Add(button, 1, 0);
            return row;
        }

        private void LoadSettings()
        {
            string savedOutput = _settings.Get("OutputFolder", null);
            _outputFolder.Text = string.IsNullOrWhiteSpace(savedOutput) ? RuntimeLocator.DefaultOutputFolder() : savedOutput;

            string savedServer = _settings.Get("ServerPath", null);
            _serverPath.Text = string.IsNullOrWhiteSpace(savedServer) ? RuntimeLocator.DefaultServerPath(_packageRoot) : savedServer;

            string preset = _settings.Get("Preset", "THICC");
            if (string.Equals(preset, "XL", StringComparison.OrdinalIgnoreCase)) _xl.Checked = true;
            else if (string.Equals(preset, "HUGE", StringComparison.OrdinalIgnoreCase)) _huge.Checked = true;
            else _thicc.Checked = true;

            int difficulty;
            if (!int.TryParse(_settings.Get("Difficulty", "0"), out difficulty) || difficulty < 0 || difficulty > 3)
                difficulty = 0;
            _difficulty.SelectedIndex = difficulty;
        }

        private void SaveSettings()
        {
            _settings.Set("OutputFolder", _outputFolder.Text.Trim());
            _settings.Set("ServerPath", _serverPath.Text.Trim());
            _settings.Set("Preset", SelectedPreset().Key);
            _settings.Set("Difficulty", _difficulty.SelectedIndex.ToString());
            _settings.Save();
        }

        private WorldPreset SelectedPreset()
        {
            if (_xl.Checked) return WorldPreset.XL;
            if (_huge.Checked) return WorldPreset.Huge;
            return WorldPreset.Thicc;
        }

        private async Task GenerateAsync()
        {
            if (_cts != null) return;

            try
            {
                string packageError;
                if (!RuntimeLocator.TryValidatePackage(_packageRoot, out packageError))
                {
                    MessageBox.Show(this, packageError, "Expanded World Maker", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                string server = _serverPath.Text.Trim();
                if (!File.Exists(server))
                {
                    MessageBox.Show(this, "TerrariaServer.exe was not found. Use Server... to point at the official 1.4.5.8 Windows dedicated server executable.", "Expanded World Maker", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                FileVersionInfo version = FileVersionInfo.GetVersionInfo(server);
                string versionText = version.FileVersion ?? string.Empty;
                if (versionText.IndexOf("1.4.5.8", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    MessageBox.Show(this, "This build of Expanded Worlds is audited against Terraria 1.4.5.8. The selected server reports version '" + versionText + "'. Choose TerrariaServer.exe from a 1.4.5.8 install.", "Wrong TerrariaServer version", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                GenerationRequest request = BuildRequest();
                string finalPath = Path.Combine(request.OutputFolder, FileNameTools.MakeWorldFileName(request.WorldName));
                if (File.Exists(finalPath))
                {
                    DialogResult overwrite = MessageBox.Show(
                        this,
                        "A world file already exists here:\r\n\r\n" + finalPath + "\r\n\r\nGenerate the replacement? The existing file is not overwritten unless the new world finishes and validates successfully.",
                        "Replace existing world?",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning,
                        MessageBoxDefaultButton.Button2);
                    if (overwrite != DialogResult.Yes) return;
                }

                SaveSettings();
                SetBusy(true);
                _progress.Value = 0;
                _status.Text = "Starting...";
                _log.Clear();
                _openFolder.Enabled = false;
                _lastOutputPath = null;

                _cts = new CancellationTokenSource();
                _job = new WorldGenerationJob();
                _job.LogLine += OnJobLogLine;
                _job.ProgressChanged += OnJobProgress;

                GenerationResult result = await _job.RunAsync(request, _cts.Token);
                _lastOutputPath = result.OutputPath;
                _openFolder.Enabled = true;
                _status.Text = "Done — " + SelectedPreset().Label + " world saved: " + Path.GetFileName(result.OutputPath);
                _progress.Value = 100;
                System.Media.SystemSounds.Asterisk.Play();
                MessageBox.Show(this, "World generated successfully.\r\n\r\n" + result.OutputPath, "Expanded World Maker", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (OperationCanceledException)
            {
                _status.Text = "Cancelled. No .wld was copied into your output folder.";
                _progress.Value = 0;
            }
            catch (Exception ex)
            {
                _status.Text = "Generation failed. Open the log for details.";
                if (!_log.Visible) ToggleLog();
                MessageBox.Show(this, ex.Message + "\r\n\r\nThe detailed log is also saved under %LOCALAPPDATA%\\gloader\\ExpandedWorldMaker\\last-generation.log.", "World generation failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (_job != null)
                {
                    _job.Dispose();
                    _job = null;
                }
                if (_cts != null)
                {
                    _cts.Dispose();
                    _cts = null;
                }
                SetBusy(false);
            }
        }

        private GenerationRequest BuildRequest()
        {
            var request = new GenerationRequest
            {
                GLoaderPath = Path.Combine(_packageRoot, "gloader.exe"),
                ExpandedWorldsSourcePath = Path.Combine(_packageRoot, "gmods", "ExpandedWorlds"),
                ServerPath = _serverPath.Text.Trim(),
                OutputFolder = _outputFolder.Text.Trim(),
                WorldName = _worldName.Text.Trim(),
                Seed = _seed.Text.Trim(),
                Difficulty = _difficulty.SelectedIndex,
                Preset = SelectedPreset()
            };
            foreach (KeyValuePair<SecretSeedOption, CheckBox> item in _secretChecks)
            {
                if (item.Value.Checked)
                    request.SecretSeeds.Add(item.Key);
            }
            return request;
        }

        private void OnJobLogLine(string line)
        {
            if (IsDisposed) return;
            BeginInvoke((Action)delegate
            {
                if (_log.TextLength > 250000)
                    _log.Clear();
                _log.AppendText(line + Environment.NewLine);
                _log.SelectionStart = _log.TextLength;
                _log.ScrollToCaret();
            });
        }

        private void OnJobProgress(int value, string text)
        {
            if (IsDisposed) return;
            BeginInvoke((Action)delegate
            {
                _progress.Value = Math.Max(_progress.Minimum, Math.Min(_progress.Maximum, value));
                _status.Text = text;
            });
        }

        private void SetBusy(bool busy)
        {
            _generate.Enabled = !busy;
            _cancel.Enabled = busy;
            _worldName.Enabled = !busy;
            _seed.Enabled = !busy;
            _difficulty.Enabled = !busy;
            _xl.Enabled = !busy;
            _huge.Enabled = !busy;
            _thicc.Enabled = !busy;
            _outputFolder.Enabled = !busy;
            _serverPath.Enabled = !busy;
            foreach (CheckBox box in _secretChecks.Values) box.Enabled = !busy;
        }

        private void CancelGeneration()
        {
            if (_cts == null) return;
            _status.Text = "Cancelling...";
            try { _cts.Cancel(); } catch { }
            try { if (_job != null) _job.Cancel(); } catch { }
        }

        private void BrowseOutputFolder(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Choose where Expanded World Maker saves the finished .wld file";
                dialog.ShowNewFolderButton = true;
                if (Directory.Exists(_outputFolder.Text)) dialog.SelectedPath = _outputFolder.Text;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    _outputFolder.Text = dialog.SelectedPath;
            }
        }

        private void BrowseServer(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "Choose TerrariaServer.exe (1.4.5.8)";
                dialog.Filter = "Terraria dedicated server|TerrariaServer.exe|Executable files|*.exe|All files|*.*";
                dialog.CheckFileExists = true;
                if (File.Exists(_serverPath.Text))
                    dialog.InitialDirectory = Path.GetDirectoryName(_serverPath.Text);
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    _serverPath.Text = dialog.FileName;
            }
        }

        private void RefreshRuntimeStatus()
        {
            if (_runtimeStatus == null || _serverPath == null) return;
            string packageError;
            if (!RuntimeLocator.TryValidatePackage(_packageRoot, out packageError))
            {
                _runtimeStatus.Text = "Package: NOT READY — " + packageError;
                _runtimeStatus.ForeColor = Color.FromArgb(255, 179, 179);
                return;
            }

            string server = _serverPath.Text.Trim();
            if (!File.Exists(server))
            {
                _runtimeStatus.Text = "Package: ready • Server: choose TerrariaServer.exe";
                _runtimeStatus.ForeColor = _muted;
                return;
            }

            string version = FileVersionInfo.GetVersionInfo(server).FileVersion ?? "unknown";
            bool correct = version.IndexOf("1.4.5.8", StringComparison.OrdinalIgnoreCase) >= 0;
            _runtimeStatus.Text = "Package: ready • TerrariaServer: " + version + (correct ? " • audited version" : " • WRONG VERSION");
            _runtimeStatus.ForeColor = correct ? Color.FromArgb(181, 232, 192) : Color.FromArgb(255, 179, 179);
        }

        private void ToggleLog()
        {
            _log.Visible = !_log.Visible;
            _logToggle.Text = _log.Visible ? "Hide log" : "Show log";
            if (_log.Visible && Height < 940) Height = 940;
        }

        private void OpenOutputFolder()
        {
            string folder = _outputFolder.Text.Trim();
            if (_lastOutputPath != null && File.Exists(_lastOutputPath))
                folder = Path.GetDirectoryName(_lastOutputPath);
            if (!Directory.Exists(folder)) return;
            Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (_cts != null)
            {
                DialogResult result = MessageBox.Show(this, "World generation is still running. Cancel it and close?", "Expanded World Maker", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (result != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
                CancelGeneration();
            }
            try { SaveSettings(); } catch { }
        }
    }

    internal sealed class SettingsStore
    {
        private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly string _path;

        public SettingsStore()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "gloader", "ExpandedWorldMaker");
            Directory.CreateDirectory(dir);
            _path = Path.Combine(dir, "settings.txt");
            Load();
        }

        public string Get(string key, string fallback)
        {
            string value;
            return _values.TryGetValue(key, out value) ? value : fallback;
        }

        public void Set(string key, string value)
        {
            _values[key] = value ?? string.Empty;
        }

        public void Save()
        {
            var lines = _values.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => pair.Key + "=" + Uri.EscapeDataString(pair.Value ?? string.Empty));
            File.WriteAllLines(_path, lines);
        }

        private void Load()
        {
            if (!File.Exists(_path)) return;
            foreach (string line in File.ReadAllLines(_path))
            {
                int equals = line.IndexOf('=');
                if (equals <= 0) continue;
                string key = line.Substring(0, equals).Trim();
                string encoded = line.Substring(equals + 1);
                try { _values[key] = Uri.UnescapeDataString(encoded); } catch { }
            }
        }
    }
}
