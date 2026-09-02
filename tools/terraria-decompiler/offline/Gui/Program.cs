using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Gloader.TerrariaDecompiler
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args != null && args.Length > 0 && string.Equals(args[0], "--self-test", StringComparison.OrdinalIgnoreCase))
            {
                return BundlePaths.Validate(out _) ? 0 : 1;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
            return 0;
        }
    }

    internal static class BundlePaths
    {
        public static string Root => AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        public static string EngineScript => Path.Combine(Root, "Run-TerrariaDecompiler.ps1");
        public static string AuditScript => Path.Combine(Root, "Audit-Offline.ps1");
        public static string RuntimeDotnet => Path.Combine(Root, "runtime", "dotnet.exe");
        public static string ReferencesDirectory => Path.Combine(Root, "refs");
        public static string IlspyDirectory => Path.Combine(Root, "ilspy");

        public static bool Validate(out string problem)
        {
            if (!File.Exists(EngineScript)) { problem = "Missing Run-TerrariaDecompiler.ps1"; return false; }
            if (!File.Exists(AuditScript)) { problem = "Missing Audit-Offline.ps1"; return false; }
            if (!File.Exists(RuntimeDotnet)) { problem = "Missing bundled runtime\\dotnet.exe"; return false; }
            if (!Directory.Exists(ReferencesDirectory)) { problem = "Missing refs folder"; return false; }
            if (!Directory.Exists(IlspyDirectory) || Directory.GetFiles(IlspyDirectory, "ilspycmd.dll", SearchOption.AllDirectories).Length == 0)
            {
                problem = "Missing bundled ilspycmd.dll";
                return false;
            }
            problem = null;
            return true;
        }
    }

    internal sealed class MainForm : Form
    {
        private const string SettingsKey = @"Software\gloader\TerrariaDecompiler";

        private readonly TextBox terrariaBox = new TextBox();
        private readonly Button terrariaBrowse = new Button();
        private readonly Label terrariaInfo = new Label();
        private readonly TextBox outputBox = new TextBox();
        private readonly Button outputBrowse = new Button();
        private readonly Button decompileButton = new Button();
        private readonly Button cancelButton = new Button();
        private readonly ProgressBar progress = new ProgressBar();
        private readonly Label statusLabel = new Label();
        private readonly Button detailsButton = new Button();
        private readonly Button openOutputButton = new Button();
        private readonly Button viewAuditButton = new Button();
        private readonly TextBox logBox = new TextBox();

        private Process engineProcess;
        private bool outputManuallyChosen;
        private bool detailsVisible;
        private bool closing;

        public MainForm()
        {
            Text = "Terraria Decompiler";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(780, 520);
            ClientSize = new Size(780, 490);
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            BuildUi();
            Load += OnLoaded;
            FormClosing += OnFormClosing;
        }

        private void BuildUi()
        {
            var title = new Label
            {
                Text = "Terraria Decompiler",
                Font = new Font("Segoe UI Semibold", 20F, FontStyle.Bold, GraphicsUnit.Point),
                AutoSize = true,
                Location = new Point(24, 20)
            };
            Controls.Add(title);

            var subtitle = new Label
            {
                Text = "One click decompiles both Terraria.exe and TerrariaServer.exe, then audits them together.",
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Location = new Point(27, 61)
            };
            Controls.Add(subtitle);

            Controls.Add(MakeLabel("Terraria.exe", 27, 101));
            terrariaBox.Location = new Point(27, 124);
            terrariaBox.Size = new Size(605, 23);
            terrariaBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            terrariaBox.TextChanged += (_, __) =>
            {
                RefreshTerrariaInfo();
                if (!outputManuallyChosen) SetDefaultOutputForCurrentVersion();
            };
            Controls.Add(terrariaBox);

            terrariaBrowse.Text = "Browse...";
            terrariaBrowse.Location = new Point(642, 122);
            terrariaBrowse.Size = new Size(105, 27);
            terrariaBrowse.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            terrariaBrowse.Click += BrowseTerraria;
            Controls.Add(terrariaBrowse);

            terrariaInfo.AutoSize = false;
            terrariaInfo.Location = new Point(27, 155);
            terrariaInfo.Size = new Size(720, 42);
            terrariaInfo.ForeColor = SystemColors.GrayText;
            terrariaInfo.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(terrariaInfo);

            Controls.Add(MakeLabel("Output folder", 27, 205));
            outputBox.Location = new Point(27, 228);
            outputBox.Size = new Size(605, 23);
            outputBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(outputBox);

            outputBrowse.Text = "Browse...";
            outputBrowse.Location = new Point(642, 226);
            outputBrowse.Size = new Size(105, 27);
            outputBrowse.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            outputBrowse.Click += BrowseOutput;
            Controls.Add(outputBrowse);

            decompileButton.Text = "DECOMPILE BOTH";
            decompileButton.Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold, GraphicsUnit.Point);
            decompileButton.Location = new Point(27, 278);
            decompileButton.Size = new Size(190, 43);
            decompileButton.Click += async (_, __) => await StartDecompileAsync();
            Controls.Add(decompileButton);

            cancelButton.Text = "Cancel";
            cancelButton.Location = new Point(228, 286);
            cancelButton.Size = new Size(85, 29);
            cancelButton.Enabled = false;
            cancelButton.Click += (_, __) => CancelCurrentRun();
            Controls.Add(cancelButton);

            progress.Location = new Point(27, 337);
            progress.Size = new Size(720, 16);
            progress.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            progress.Minimum = 0;
            progress.Maximum = 100;
            Controls.Add(progress);

            statusLabel.Text = "Ready.";
            statusLabel.Location = new Point(27, 362);
            statusLabel.Size = new Size(720, 24);
            statusLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(statusLabel);

            detailsButton.Text = "Show Details";
            detailsButton.Location = new Point(27, 402);
            detailsButton.Size = new Size(105, 30);
            detailsButton.Click += (_, __) => ToggleDetails();
            Controls.Add(detailsButton);

            openOutputButton.Text = "Open Output";
            openOutputButton.Location = new Point(522, 402);
            openOutputButton.Size = new Size(105, 30);
            openOutputButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            openOutputButton.Enabled = false;
            openOutputButton.Click += (_, __) => OpenOutput();
            Controls.Add(openOutputButton);

            viewAuditButton.Text = "View Audit";
            viewAuditButton.Location = new Point(637, 402);
            viewAuditButton.Size = new Size(110, 30);
            viewAuditButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            viewAuditButton.Enabled = false;
            viewAuditButton.Click += (_, __) => ViewAudit();
            Controls.Add(viewAuditButton);

            logBox.Location = new Point(27, 452);
            logBox.Size = new Size(720, 220);
            logBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            logBox.Multiline = true;
            logBox.ReadOnly = true;
            logBox.ScrollBars = ScrollBars.Both;
            logBox.WordWrap = false;
            logBox.Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point);
            logBox.Visible = false;
            Controls.Add(logBox);
        }

        private static Label MakeLabel(string text, int x, int y)
        {
            return new Label
            {
                Text = text,
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold, GraphicsUnit.Point),
                AutoSize = true,
                Location = new Point(x, y)
            };
        }

        private void OnLoaded(object sender, EventArgs e)
        {
            if (!BundlePaths.Validate(out var bundleProblem))
            {
                statusLabel.Text = "Bundle problem: " + bundleProblem;
                decompileButton.Enabled = false;
                MessageBox.Show(this, bundleProblem, "Terraria Decompiler", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var savedTerraria = ReadSetting("TerrariaExe");
            var detected = File.Exists(savedTerraria) ? savedTerraria : DetectTerrariaExe();
            if (!string.IsNullOrWhiteSpace(detected)) terrariaBox.Text = detected;

            var savedOutput = ReadSetting("OutputDir");
            if (!string.IsNullOrWhiteSpace(savedOutput))
            {
                outputBox.Text = savedOutput;
                outputManuallyChosen = true;
            }
            else
            {
                SetDefaultOutputForCurrentVersion();
            }

            RefreshTerrariaInfo();
        }

        private void BrowseTerraria(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "Select Terraria.exe";
                dialog.Filter = "Terraria executable|Terraria.exe|Executable files|*.exe|All files|*.*";
                dialog.CheckFileExists = true;
                dialog.Multiselect = false;

                var current = terrariaBox.Text.Trim();
                if (File.Exists(current)) dialog.InitialDirectory = Path.GetDirectoryName(current);

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    terrariaBox.Text = dialog.FileName;
                    WriteSetting("TerrariaExe", dialog.FileName);
                }
            }
        }

        private void BrowseOutput(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Choose where client, server, and audit output should be written.";
                dialog.ShowNewFolderButton = true;
                if (Directory.Exists(outputBox.Text.Trim())) dialog.SelectedPath = outputBox.Text.Trim();
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    outputManuallyChosen = true;
                    outputBox.Text = dialog.SelectedPath;
                    WriteSetting("OutputDir", dialog.SelectedPath);
                }
            }
        }

        private static string GetServerExePath(string clientExe)
        {
            if (string.IsNullOrWhiteSpace(clientExe)) return null;
            var folder = Path.GetDirectoryName(clientExe);
            return string.IsNullOrWhiteSpace(folder) ? null : Path.Combine(folder, "TerrariaServer.exe");
        }

        private void RefreshTerrariaInfo()
        {
            var clientExe = terrariaBox.Text.Trim();
            if (!File.Exists(clientExe))
            {
                terrariaInfo.Text = "Select Terraria.exe. TerrariaServer.exe will be picked up automatically from the same folder.";
                return;
            }

            try
            {
                var clientVersion = FileVersionInfo.GetVersionInfo(clientExe).FileVersion ?? "unknown";
                var serverExe = GetServerExePath(clientExe);
                var serverExists = !string.IsNullOrWhiteSpace(serverExe) && File.Exists(serverExe);
                var serverVersion = serverExists ? (FileVersionInfo.GetVersionInfo(serverExe).FileVersion ?? "unknown") : "MISSING";
                var folder = Path.GetDirectoryName(clientExe);
                var managed = 0;
                var totalDlls = 0;
                foreach (var dll in Directory.GetFiles(folder, "*.dll", SearchOption.TopDirectoryOnly))
                {
                    totalDlls++;
                    try
                    {
                        AssemblyName.GetAssemblyName(dll);
                        managed++;
                    }
                    catch { }
                }

                var pairState = serverExists
                    ? (string.Equals(clientVersion, serverVersion, StringComparison.OrdinalIgnoreCase) ? "client/server match" : "CLIENT/SERVER VERSION MISMATCH")
                    : "TerrariaServer.exe MISSING";

                terrariaInfo.Text = $"Client {clientVersion}  |  Server {serverVersion}  |  {pairState}\r\n{managed} managed install DLL(s)  |  {totalDlls - managed} native/non-managed DLL(s) ignored by ILSpy";
            }
            catch (Exception ex)
            {
                terrariaInfo.Text = "Could not inspect this Terraria installation: " + ex.Message;
            }
        }

        private string GetCurrentVersion()
        {
            var exe = terrariaBox.Text.Trim();
            if (!File.Exists(exe)) return null;
            try { return FileVersionInfo.GetVersionInfo(exe).FileVersion; }
            catch { return null; }
        }

        private void SetDefaultOutputForCurrentVersion()
        {
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var root = Path.Combine(docs, "Terraria Decomp");
            var version = GetCurrentVersion();
            outputBox.Text = string.IsNullOrWhiteSpace(version) ? root : Path.Combine(root, version);
        }

        private async Task StartDecompileAsync()
        {
            var clientExe = terrariaBox.Text.Trim();
            var output = outputBox.Text.Trim();

            if (!File.Exists(clientExe))
            {
                MessageBox.Show(this, "Pick a valid Terraria.exe first.", "Terraria Decompiler", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var serverExe = GetServerExePath(clientExe);
            if (string.IsNullOrWhiteSpace(serverExe) || !File.Exists(serverExe))
            {
                MessageBox.Show(this, "TerrariaServer.exe was not found beside Terraria.exe. Update/verify the Terraria installation so both executables are present, then try again.", "Terraria Decompiler", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var clientVersion = FileVersionInfo.GetVersionInfo(clientExe).FileVersion ?? "unknown";
            var serverVersion = FileVersionInfo.GetVersionInfo(serverExe).FileVersion ?? "unknown";
            if (!string.Equals(clientVersion, serverVersion, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this, $"Client/server version mismatch.\r\n\r\nTerraria.exe: {clientVersion}\r\nTerrariaServer.exe: {serverVersion}\r\n\r\nUpdate/verify the Terraria installation before decompiling.", "Terraria Decompiler", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(output))
            {
                MessageBox.Show(this, "Pick an output folder first.", "Terraria Decompiler", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            WriteSetting("TerrariaExe", clientExe);
            WriteSetting("OutputDir", output);

            SetRunningUi(true);
            logBox.Clear();
            progress.Value = 3;
            statusLabel.Text = "Starting client + server decompile...";

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -File " + Quote(BundlePaths.EngineScript) +
                            " -TerrariaInput " + Quote(clientExe) +
                            " -OutputDirectory " + Quote(output),
                WorkingDirectory = BundlePaths.Root,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            try
            {
                engineProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };
                engineProcess.OutputDataReceived += (_, e) => { if (e.Data != null) HandleEngineLine(e.Data, false); };
                engineProcess.ErrorDataReceived += (_, e) => { if (e.Data != null) HandleEngineLine(e.Data, true); };

                if (!engineProcess.Start()) throw new InvalidOperationException("Could not start PowerShell decompile engine.");
                engineProcess.BeginOutputReadLine();
                engineProcess.BeginErrorReadLine();

                await Task.Run(() => engineProcess.WaitForExit());
                var exitCode = engineProcess.ExitCode;

                if (closing) return;

                if (exitCode == 0)
                {
                    progress.Value = 100;
                    openOutputButton.Enabled = Directory.Exists(output);
                    viewAuditButton.Enabled = File.Exists(Path.Combine(output, "audit", "audit.md"));
                    var issueCount = ReadAuditIssueCount(Path.Combine(output, "audit", "audit.json"));
                    statusLabel.Text = issueCount.HasValue
                        ? (issueCount.Value == 0 ? "Done. Client + server audit clean: 0 issues." : $"Done, but combined audit found {issueCount.Value} tracked issue(s).")
                        : "Done. Open the combined audit for details.";
                }
                else
                {
                    statusLabel.Text = "Decompiler failed. Show Details for the error log.";
                    if (!detailsVisible) ToggleDetails();
                    MessageBox.Show(this, "The decompiler exited with an error. The details log is open below.", "Terraria Decompiler", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                AppendLog("ERROR: " + ex);
                statusLabel.Text = "Decompiler failed to start.";
                if (!detailsVisible) ToggleDetails();
                MessageBox.Show(this, ex.Message, "Terraria Decompiler", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (!closing) SetRunningUi(false);
                if (engineProcess != null)
                {
                    engineProcess.Dispose();
                    engineProcess = null;
                }
            }
        }

        private void HandleEngineLine(string line, bool isError)
        {
            if (IsDisposed || closing) return;
            try
            {
                BeginInvoke((Action)(() =>
                {
                    AppendLog((isError ? "[stderr] " : string.Empty) + line);
                    UpdateProgressFromLine(line);
                }));
            }
            catch { }
        }

        private void UpdateProgressFromLine(string line)
        {
            if (line.IndexOf("Harvested ", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                progress.Value = Math.Max(progress.Value, 12);
                statusLabel.Text = "Scanning Terraria install references...";
            }
            else if (line.IndexOf("CLIENT pass 1/2", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                progress.Value = Math.Max(progress.Value, 20);
                statusLabel.Text = "Client: recovering embedded DLLs...";
            }
            else if (line.IndexOf("CLIENT pass 2/2", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                progress.Value = Math.Max(progress.Value, 34);
                statusLabel.Text = "Client: decompiling clean C#...";
            }
            else if (line.IndexOf("CLIENT clean source ZIP:", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                progress.Value = Math.Max(progress.Value, 48);
                statusLabel.Text = "Client decompile complete.";
            }
            else if (line.IndexOf("SERVER pass 1/2", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                progress.Value = Math.Max(progress.Value, 56);
                statusLabel.Text = "Server: recovering embedded DLLs...";
            }
            else if (line.IndexOf("SERVER pass 2/2", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                progress.Value = Math.Max(progress.Value, 72);
                statusLabel.Text = "Server: decompiling clean C#...";
            }
            else if (line.IndexOf("SERVER clean source ZIP:", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                progress.Value = Math.Max(progress.Value, 88);
                statusLabel.Text = "Server decompile complete.";
            }
            else if (line.IndexOf("Combined audit report:", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                progress.Value = Math.Max(progress.Value, 96);
                statusLabel.Text = "Finishing combined audit...";
            }
        }

        private void AppendLog(string line)
        {
            logBox.AppendText(line + Environment.NewLine);
        }

        private void SetRunningUi(bool running)
        {
            terrariaBox.Enabled = !running;
            terrariaBrowse.Enabled = !running;
            outputBox.Enabled = !running;
            outputBrowse.Enabled = !running;
            decompileButton.Enabled = !running;
            cancelButton.Enabled = running;
            if (running)
            {
                openOutputButton.Enabled = false;
                viewAuditButton.Enabled = false;
            }
        }

        private void CancelCurrentRun()
        {
            var process = engineProcess;
            if (process == null || process.HasExited) return;
            statusLabel.Text = "Cancelling...";
            try
            {
                using (var killer = Process.Start(new ProcessStartInfo
                {
                    FileName = "taskkill.exe",
                    Arguments = "/PID " + process.Id + " /T /F",
                    CreateNoWindow = true,
                    UseShellExecute = false
                }))
                {
                    killer?.WaitForExit(5000);
                }
            }
            catch (Exception ex)
            {
                AppendLog("Cancel error: " + ex.Message);
            }
        }

        private void ToggleDetails()
        {
            detailsVisible = !detailsVisible;
            logBox.Visible = detailsVisible;
            detailsButton.Text = detailsVisible ? "Hide Details" : "Show Details";
            ClientSize = new Size(ClientSize.Width, detailsVisible ? 710 : 490);
        }

        private void OpenOutput()
        {
            var path = outputBox.Text.Trim();
            if (!Directory.Exists(path)) return;
            Process.Start(new ProcessStartInfo("explorer.exe", Quote(path)) { UseShellExecute = false });
        }

        private void ViewAudit()
        {
            var audit = Path.Combine(outputBox.Text.Trim(), "audit", "audit.md");
            if (!File.Exists(audit)) return;
            try
            {
                Process.Start(new ProcessStartInfo(audit) { UseShellExecute = true });
            }
            catch
            {
                Process.Start(new ProcessStartInfo("notepad.exe", Quote(audit)) { UseShellExecute = false });
            }
        }

        private static int? ReadAuditIssueCount(string auditJson)
        {
            if (!File.Exists(auditJson)) return null;
            try
            {
                var json = File.ReadAllText(auditJson);
                var totalMatch = Regex.Match(json, "\\\"total_tracked_issues\\\"\\s*:\\s*(\\d+)", RegexOptions.IgnoreCase);
                if (totalMatch.Success) return int.Parse(totalMatch.Groups[1].Value);

                var keys = new[]
                {
                    "unknown_result_type", "encoded_constructor", "ref_cast_artifact", "failed_decompile",
                    "expected_unknown", "invalid_unknown_comparison", "old_velocity_statement",
                    "old_nullable_num52", "old_mouse_text_color_assignment"
                };
                var found = 0;
                var total = 0;
                foreach (var key in keys)
                {
                    var match = Regex.Match(json, "\\\"" + Regex.Escape(key) + "\\\"\\s*:\\s*(\\d+)", RegexOptions.IgnoreCase);
                    if (!match.Success) continue;
                    found++;
                    total += int.Parse(match.Groups[1].Value);
                }
                return found >= 4 ? total : (int?)null;
            }
            catch { return null; }
        }

        private static string DetectTerrariaExe()
        {
            var candidates = new List<string>
            {
                @"C:\Program Files (x86)\Steam\steamapps\common\Terraria\Terraria.exe",
                @"C:\Program Files\Steam\steamapps\common\Terraria\Terraria.exe"
            };

            foreach (var steamRoot in GetSteamRoots())
            {
                candidates.Add(Path.Combine(steamRoot, "steamapps", "common", "Terraria", "Terraria.exe"));
                var vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
                if (!File.Exists(vdf)) continue;
                try
                {
                    var text = File.ReadAllText(vdf);
                    foreach (Match match in Regex.Matches(text, "\\\"path\\\"\\s+\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase))
                    {
                        var library = match.Groups[1].Value.Replace("\\\\", "\\");
                        candidates.Add(Path.Combine(library, "steamapps", "common", "Terraria", "Terraria.exe"));
                    }
                }
                catch { }
            }

            foreach (var candidate in candidates)
            {
                try { if (File.Exists(candidate)) return Path.GetFullPath(candidate); }
                catch { }
            }
            return null;
        }

        private static IEnumerable<string> GetSteamRoots()
        {
            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var keys = new[]
            {
                Tuple.Create(Registry.CurrentUser, @"Software\Valve\Steam"),
                Tuple.Create(Registry.LocalMachine, @"Software\WOW6432Node\Valve\Steam"),
                Tuple.Create(Registry.LocalMachine, @"Software\Valve\Steam")
            };
            foreach (var item in keys)
            {
                try
                {
                    using (var key = item.Item1.OpenSubKey(item.Item2))
                    {
                        var value = key?.GetValue("SteamPath") as string ?? key?.GetValue("InstallPath") as string;
                        if (!string.IsNullOrWhiteSpace(value)) roots.Add(value.Replace('/', '\\'));
                    }
                }
                catch { }
            }
            return roots;
        }

        private static string Quote(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
        }

        private static string ReadSetting(string name)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(SettingsKey))
                {
                    return key?.GetValue(name) as string;
                }
            }
            catch { return null; }
        }

        private static void WriteSetting(string name, string value)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(SettingsKey))
                {
                    key?.SetValue(name, value ?? string.Empty, RegistryValueKind.String);
                }
            }
            catch { }
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            closing = true;
            if (engineProcess != null && !engineProcess.HasExited)
            {
                try
                {
                    using (var killer = Process.Start(new ProcessStartInfo
                    {
                        FileName = "taskkill.exe",
                        Arguments = "/PID " + engineProcess.Id + " /T /F",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    }))
                    {
                        killer?.WaitForExit(3000);
                    }
                }
                catch { }
            }
        }
    }
}
