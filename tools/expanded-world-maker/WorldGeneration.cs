using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace GLoader.ExpandedWorldMaker
{
    internal sealed class WorldPreset
    {
        public WorldPreset(string key, string label, int width, int height)
        {
            Key = key;
            Label = label;
            Width = width;
            Height = height;
        }

        public string Key { get; private set; }
        public string Label { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public string Dimensions { get { return Width.ToString("N0", CultureInfo.InvariantCulture) + " x " + Height.ToString("N0", CultureInfo.InvariantCulture); } }
        public long TileCount { get { return (long)Width * Height; } }

        public static readonly WorldPreset XL = new WorldPreset("XL", "XL", 12600, 2400);
        public static readonly WorldPreset Huge = new WorldPreset("HUGE", "Huge", 16800, 2400);
        public static readonly WorldPreset Thicc = new WorldPreset("THICC", "THICC", 16800, 4800);
    }

    internal sealed class SecretSeedOption
    {
        public SecretSeedOption(string configName, string label, string hint)
        {
            ConfigName = configName;
            Label = label;
            Hint = hint;
        }

        public string ConfigName { get; private set; }
        public string Label { get; private set; }
        public string Hint { get; private set; }

        public static readonly SecretSeedOption[] All =
        {
            new SecretSeedOption("notthebees", "Not the Bees", "Bee-heavy world generation."),
            new SecretSeedOption("drunk", "Drunk", "Drunk world generation."),
            new SecretSeedOption("celebration", "Celebration Mk10", "10th-anniversary world generation."),
            new SecretSeedOption("theconstant", "The Constant", "Don't Starve crossover world generation."),
            new SecretSeedOption("fortheworthy", "For the Worthy", "Harder world-generation rules."),
            new SecretSeedOption("notraps", "No Traps", "The extremely trap-heavy secret world. The name is lying."),
            new SecretSeedOption("remix", "Remix / Don't Dig Up", "Reversed progression / Remix world generation."),
            new SecretSeedOption("zenith", "Zenith / Get Fixed Boi", "Enables Terraria's classic secret-seed bundle."),
            new SecretSeedOption("skyblock", "Skyblock", "Terraria 1.4.5 Skyblock world generation.")
        };
    }

    internal sealed class GenerationRequest
    {
        public string GLoaderPath;
        public string ExpandedWorldsSourcePath;
        public string ServerPath;
        public string OutputFolder;
        public string WorldName;
        public string Seed;
        public int Difficulty;
        public WorldPreset Preset;
        public List<SecretSeedOption> SecretSeeds = new List<SecretSeedOption>();
    }

    internal sealed class GenerationResult
    {
        public string OutputPath;
        public string LogPath;
        public long Bytes;
    }

    internal static class RuntimeLocator
    {
        public static string FindPackageRoot()
        {
            string path = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
            DirectoryInfo current = new DirectoryInfo(path);
            for (int i = 0; current != null && i < 6; i++, current = current.Parent)
            {
                string gloader = Path.Combine(current.FullName, "gloader.exe");
                string expanded = Path.Combine(current.FullName, "gmods", "ExpandedWorlds");
                if (File.Exists(gloader) && Directory.Exists(expanded))
                    return current.FullName;
            }

            DirectoryInfo app = new DirectoryInfo(path);
            if (app.Parent != null)
                return app.Parent.FullName;
            return path;
        }

        public static string DefaultServerPath(string packageRoot)
        {
            return Path.Combine(packageRoot, "TerrariaServer.exe");
        }

        public static string DefaultOutputFolder()
        {
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (!string.IsNullOrWhiteSpace(documents))
                return Path.Combine(documents, "My Games", "Terraria", "Worlds");

            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(profile, "Documents", "My Games", "Terraria", "Worlds");
        }

        public static bool TryValidatePackage(string packageRoot, out string message)
        {
            string gloader = Path.Combine(packageRoot, "gloader.exe");
            string mod = Path.Combine(packageRoot, "gmods", "ExpandedWorlds");
            if (!File.Exists(gloader))
            {
                message = "gloader.exe was not found. Extract the complete gloader release into the Terraria install folder, then launch tools\\ExpandedWorldMaker.exe.";
                return false;
            }
            if (!Directory.Exists(mod) || !File.Exists(Path.Combine(mod, "ServerRuntime.cs")))
            {
                message = "gmods\\ExpandedWorlds is missing or incomplete. Extract the complete gloader release instead of copying only the World Maker EXE.";
                return false;
            }
            message = null;
            return true;
        }
    }

    internal static class FileNameTools
    {
        public static string MakeWorldFileName(string worldName)
        {
            string value = (worldName ?? string.Empty).Trim();
            foreach (char bad in Path.GetInvalidFileNameChars())
                value = value.Replace(bad, '_');
            value = value.Trim().TrimEnd('.');
            if (string.IsNullOrWhiteSpace(value))
                value = "World";
            return value + ".wld";
        }

        public static bool ContainsNewline(string text)
        {
            return text != null && (text.IndexOf('\r') >= 0 || text.IndexOf('\n') >= 0);
        }
    }

    internal sealed class WorldGenerationJob : IDisposable
    {
        private readonly object _sync = new object();
        private readonly StringBuilder _log = new StringBuilder();
        private Process _process;
        private volatile bool _sawExpectedVerification;
        private string _expectedVerification;

        public event Action<string> LogLine;
        public event Action<int, string> ProgressChanged;

        public async Task<GenerationResult> RunAsync(GenerationRequest request, CancellationToken cancellationToken)
        {
            Validate(request);

            string appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "gloader",
                "ExpandedWorldMaker");
            Directory.CreateDirectory(appData);
            string jobsRoot = Path.Combine(appData, "jobs");
            Directory.CreateDirectory(jobsRoot);
            string jobDir = Path.Combine(jobsRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(jobDir);

            string modsDir = Path.Combine(jobDir, "gmods");
            string stagedMod = Path.Combine(modsDir, "ExpandedWorlds");
            CopyDirectory(request.ExpandedWorldsSourcePath, stagedMod);

            string generatedWorld = Path.Combine(jobDir, "generated.wld");
            string configPath = Path.Combine(jobDir, "serverconfig.txt");
            int port = ReserveLoopbackPort();
            File.WriteAllText(configPath, BuildServerConfig(request, generatedWorld, port), new UTF8Encoding(false));

            _expectedVerification = "verified " + request.Preset.Label + " " + request.Preset.Width + "x" + request.Preset.Height;
            _sawExpectedVerification = false;

            string finalPath = Path.Combine(request.OutputFolder, FileNameTools.MakeWorldFileName(request.WorldName));
            string lastLogPath = Path.Combine(appData, "last-generation.log");
            Exception failure = null;

            try
            {
                ReportProgress(0, "Starting headless Terraria server...");
                StartProcess(request, modsDir, configPath);

                DateTime deadline = DateTime.UtcNow.AddMinutes(90);
                bool ready = false;
                while (!ready)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (DateTime.UtcNow > deadline)
                        throw new TimeoutException("World generation exceeded the 90-minute safety timeout.");

                    Process p;
                    lock (_sync) { p = _process; }
                    if (p == null)
                        throw new InvalidOperationException("The headless process disappeared unexpectedly.");
                    if (p.HasExited)
                    {
                        throw new InvalidOperationException(
                            "The headless Terraria process exited before the generated world was ready. Exit code: " + p.ExitCode + ". See the log for details.");
                    }

                    ready = await CanConnectAsync(port, cancellationToken).ConfigureAwait(false);
                    if (!ready)
                        await Task.Delay(750, cancellationToken).ConfigureAwait(false);
                }

                ReportProgress(98, "Terraria finished generation; validating the saved world...");

                DateTime verifyDeadline = DateTime.UtcNow.AddSeconds(8);
                while (!_sawExpectedVerification && DateTime.UtcNow < verifyDeadline)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                }

                if (!_sawExpectedVerification)
                {
                    throw new InvalidOperationException(
                        "The server became ready, but Expanded Worlds did not report the expected " + request.Preset.Label +
                        " dimensions. The .wld was not copied into your Worlds folder.");
                }
                if (!File.Exists(generatedWorld))
                    throw new FileNotFoundException("Terraria reached server-ready state but the generated .wld file is missing.", generatedWorld);

                FileInfo generatedInfo = new FileInfo(generatedWorld);
                if (generatedInfo.Length <= 0)
                    throw new InvalidDataException("Terraria created an empty .wld file.");

                StopServerGracefully();
                Directory.CreateDirectory(request.OutputFolder);
                File.Copy(generatedWorld, finalPath, true);

                ReportProgress(100, "Done. World saved to " + finalPath);
                return new GenerationResult
                {
                    OutputPath = finalPath,
                    LogPath = lastLogPath,
                    Bytes = new FileInfo(finalPath).Length
                };
            }
            catch (Exception ex)
            {
                failure = ex;
                throw;
            }
            finally
            {
                StopServerForcefully();
                try
                {
                    lock (_sync)
                    {
                        string header = "Expanded World Maker " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + Environment.NewLine;
                        if (failure != null)
                            header += "RESULT: FAILED - " + failure + Environment.NewLine + Environment.NewLine;
                        File.WriteAllText(lastLogPath, header + _log.ToString(), new UTF8Encoding(false));
                    }
                }
                catch { }

                try { Directory.Delete(jobDir, true); } catch { }
            }
        }

        private void StartProcess(GenerationRequest request, string modsDir, string configPath)
        {
            var start = new ProcessStartInfo
            {
                FileName = request.GLoaderPath,
                Arguments = "--target " + Quote(request.ServerPath) + " --mods " + Quote(modsDir) + " -- -config " + Quote(configPath),
                WorkingDirectory = Path.GetDirectoryName(request.ServerPath),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true
            };
            start.EnvironmentVariables["GLOADER_EXPANDED_WORLD"] = request.Preset.Key;

            var process = new Process { StartInfo = start, EnableRaisingEvents = true };
            process.OutputDataReceived += OnOutputDataReceived;
            process.ErrorDataReceived += OnErrorDataReceived;
            if (!process.Start())
                throw new InvalidOperationException("Windows could not start gloader.exe.");

            lock (_sync) { _process = process; }
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        private void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null) return;
            AppendLog(e.Data);
            ParseProgress(e.Data);
            if (e.Data.IndexOf(_expectedVerification, StringComparison.OrdinalIgnoreCase) >= 0)
                _sawExpectedVerification = true;
        }

        private void OnErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null) return;
            AppendLog("[stderr] " + e.Data);
        }

        private void ParseProgress(string line)
        {
            Match match = Regex.Match(line, @"^\s*(\d+(?:\.\d+)?)%\s*-\s*(.*?)\s*-\s*(\d+(?:\.\d+)?)%\s*$");
            if (!match.Success) return;

            double percent;
            if (!double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out percent))
                return;
            int value = Math.Max(0, Math.Min(97, (int)Math.Round(percent)));
            string stage = match.Groups[2].Value.Trim();
            ReportProgress(value, string.IsNullOrWhiteSpace(stage) ? "Generating world..." : stage);
        }

        private void AppendLog(string line)
        {
            lock (_sync)
            {
                _log.AppendLine(line);
            }
            Action<string> handler = LogLine;
            if (handler != null) handler(line);
        }

        private void ReportProgress(int value, string text)
        {
            Action<int, string> handler = ProgressChanged;
            if (handler != null) handler(value, text);
        }

        private static string BuildServerConfig(GenerationRequest request, string worldPath, int port)
        {
            var lines = new List<string>
            {
                "world=" + worldPath,
                "autocreate=3",
                "worldname=" + request.WorldName.Trim(),
                "seed=" + (request.Seed ?? string.Empty).Trim(),
                "difficulty=" + request.Difficulty.ToString(CultureInfo.InvariantCulture),
                "maxplayers=1",
                "port=" + port.ToString(CultureInfo.InvariantCulture),
                "upnp=0",
                "secure=1",
                "worldrollbackstokeep=0"
            };

            foreach (SecretSeedOption option in request.SecretSeeds)
                lines.Add("seed_" + option.ConfigName + "=1");

            return string.Join(Environment.NewLine, lines) + Environment.NewLine;
        }

        private static void Validate(GenerationRequest request)
        {
            if (request == null) throw new ArgumentNullException("request");
            if (request.Preset == null) throw new InvalidOperationException("Choose XL, Huge, or THICC.");
            if (string.IsNullOrWhiteSpace(request.WorldName)) throw new InvalidOperationException("Enter a world name.");
            if (request.WorldName.Trim().Length > 26) throw new InvalidOperationException("Terraria world names are limited to 26 characters.");
            if (FileNameTools.ContainsNewline(request.WorldName)) throw new InvalidOperationException("World name cannot contain line breaks.");
            if ((request.Seed ?? string.Empty).Trim().Length > 40) throw new InvalidOperationException("Terraria seed text is limited to 40 characters.");
            if (FileNameTools.ContainsNewline(request.Seed)) throw new InvalidOperationException("Seed cannot contain line breaks.");
            if (request.Difficulty < 0 || request.Difficulty > 3) throw new InvalidOperationException("Invalid world difficulty.");
            if (!File.Exists(request.GLoaderPath)) throw new FileNotFoundException("gloader.exe is missing.", request.GLoaderPath);
            if (!Directory.Exists(request.ExpandedWorldsSourcePath)) throw new DirectoryNotFoundException("Expanded Worlds source folder is missing: " + request.ExpandedWorldsSourcePath);
            if (!File.Exists(request.ServerPath)) throw new FileNotFoundException("TerrariaServer.exe is missing. Use Browse to select the 1.4.5.8 dedicated server executable.", request.ServerPath);
            if (string.IsNullOrWhiteSpace(request.OutputFolder)) throw new InvalidOperationException("Choose an output folder for the .wld file.");
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
            foreach (string dir in Directory.GetDirectories(source))
                CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
        }

        private static int ReserveLoopbackPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        private static async Task<bool> CanConnectAsync(int port, CancellationToken token)
        {
            using (var client = new TcpClient())
            {
                Task connect = client.ConnectAsync(IPAddress.Loopback, port);
                Task timeout = Task.Delay(300, token);
                Task winner = await Task.WhenAny(connect, timeout).ConfigureAwait(false);
                if (winner != connect)
                    return false;
                try
                {
                    await connect.ConfigureAwait(false);
                    return client.Connected;
                }
                catch
                {
                    return false;
                }
            }
        }

        private void StopServerGracefully()
        {
            Process p;
            lock (_sync) { p = _process; }
            if (p == null || p.HasExited) return;
            try
            {
                p.StandardInput.WriteLine("exit-nosave");
                p.StandardInput.Flush();
                if (p.WaitForExit(5000))
                    return;
            }
            catch { }
            StopServerForcefully();
        }

        public void Cancel()
        {
            StopServerForcefully();
        }

        private void StopServerForcefully()
        {
            Process p;
            lock (_sync)
            {
                p = _process;
                _process = null;
            }
            if (p == null) return;
            try
            {
                if (!p.HasExited)
                    p.Kill();
            }
            catch { }
            try { p.Dispose(); } catch { }
        }

        private static string Quote(string value)
        {
            if (value == null) return "\"\"";
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        public void Dispose()
        {
            StopServerForcefully();
        }
    }
}
