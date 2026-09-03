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

        public static readonly WorldPreset XL = new WorldPreset("XL", "XL", 10600, 3000);
        public static readonly WorldPreset Huge = new WorldPreset("HUGE", "Huge", 12600, 3600);
        public static readonly WorldPreset Thicc = new WorldPreset("THICC", "THICC", 14800, 4200);
    }

    internal sealed class SpecialSeedOption
    {
        public SpecialSeedOption(string configName, string label, string hint, int serializedValue)
        {
            ConfigName = configName;
            Label = label;
            Hint = hint;
            SerializedValue = serializedValue;
        }

        public string ConfigName { get; private set; }
        public string Label { get; private set; }
        public string Hint { get; private set; }
        public int SerializedValue { get; private set; }

        public static readonly SpecialSeedOption[] All =
        {
            new SpecialSeedOption("notthebees", "Not the Bees", "Bee-heavy world generation.", 2),
            new SpecialSeedOption("drunk", "Drunk", "Drunk world generation.", 1),
            new SpecialSeedOption("celebration", "Celebration Mk10", "10th-anniversary world generation.", 8),
            new SpecialSeedOption("theconstant", "The Constant", "Don't Starve crossover world generation.", 16),
            new SpecialSeedOption("fortheworthy", "For the Worthy", "Harder world-generation rules.", 4),
            new SpecialSeedOption("notraps", "No Traps", "The extremely trap-heavy special world. The name is lying.", 64),
            new SpecialSeedOption("remix", "Remix / Don't Dig Up", "Reversed progression / Remix world generation.", 32),
            new SpecialSeedOption("zenith", "Zenith / Get Fixed Boi", "Enables Terraria's classic special-seed bundle.", 128),
            new SpecialSeedOption("skyblock", "Skyblock", "Terraria 1.4.5 Skyblock world generation.", 256)
        };
    }

    internal sealed class SecretSeedOption
    {
        public SecretSeedOption(string seedText, string label)
        {
            SeedText = seedText;
            Label = label;
        }

        public string SeedText { get; private set; }
        public string Label { get; private set; }
        public string Hint { get { return "Terraria 1.4.5 secret seed code: " + SeedText; } }

        public static readonly SecretSeedOption[] All =
        {
            new SecretSeedOption("Abandoned manors", "Abandoned Manors"),
            new SecretSeedOption("Arachnophobia", "Arachnophobia"),
            new SecretSeedOption("Beam me up", "Beam Me Up"),
            new SecretSeedOption("Bring a towel", "Bring a Towel"),
            new SecretSeedOption("Calm before the storm", "Calm Before the Storm"),
            new SecretSeedOption("Double daring dangers", "Double Daring Dangers"),
            new SecretSeedOption("Electric Boogaloo", "Electric Boogaloo"),
            new SecretSeedOption("Fish Mox", "Fish Mox"),
            new SecretSeedOption("Hocus pocus", "Hocus Pocus"),
            new SecretSeedOption("How did I get here", "How Did I Get Here"),
            new SecretSeedOption("I am error", "I Am Error"),
            new SecretSeedOption("Invisible plane", "Invisible Plane"),
            new SecretSeedOption("Jagged rocks", "Jagged Rocks"),
            new SecretSeedOption("Jingle all the way", "Jingle All the Way"),
            new SecretSeedOption("Mole people", "Mole People"),
            new SecretSeedOption("Monochrome", "Monochrome"),
            new SecretSeedOption("More traps please", "More Traps Please"),
            new SecretSeedOption("Negative infinity", "Negative Infinity"),
            new SecretSeedOption("Night of the Living Dead", "Night of the Living Dead"),
            new SecretSeedOption("Planetoids", "Planetoids"),
            new SecretSeedOption("Pumpkin season", "Pumpkin Season"),
            new SecretSeedOption("Purify this", "Purify This"),
            new SecretSeedOption("Rainbow Road", "Rainbow Road"),
            new SecretSeedOption("Royale with cheese", "Royale With Cheese"),
            new SecretSeedOption("Does that sparkle", "Does That Sparkle"),
            new SecretSeedOption("Too easy", "Too Easy"),
            new SecretSeedOption("Waterpark", "Waterpark"),
            new SecretSeedOption("What a horrible night to have a curse", "What a Horrible Night to Have a Curse"),
            new SecretSeedOption("Winter is coming", "Winter Is Coming"),
            new SecretSeedOption("X-ray vision", "X-Ray Vision"),
            new SecretSeedOption("Truck stop", "Truck Stop"),
            new SecretSeedOption("Sandy britches", "Sandy Britches"),
            new SecretSeedOption("Save the rainforest", "Save the Rainforest"),
            new SecretSeedOption("Such great heights", "Such Great Heights"),
            new SecretSeedOption("The Care Bears Movie", "The Care Bears Movie"),
            new SecretSeedOption("Toadstool", "Toadstool"),
            new SecretSeedOption("We don't even test for that", "We Don't Even Test for That")
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
        public List<SpecialSeedOption> SpecialSeeds = new List<SpecialSeedOption>();
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

                int actualWidth = 0;
                int actualHeight = 0;
                string validationError = null;
                bool validHeader = false;
                DateTime fileDeadline = DateTime.UtcNow.AddSeconds(15);
                while (!validHeader && DateTime.UtcNow < fileDeadline)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    validHeader = TryReadWorldDimensions(generatedWorld, out actualWidth, out actualHeight, out validationError);
                    if (!validHeader)
                        await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                }

                if (!validHeader)
                {
                    throw new InvalidDataException(
                        "Terraria reached server-ready state, but the generated .wld header could not be read. " +
                        (string.IsNullOrWhiteSpace(validationError) ? "No parser detail was available." : validationError));
                }

                if (actualWidth != request.Preset.Width || actualHeight != request.Preset.Height)
                {
                    throw new InvalidDataException(
                        "Generated .wld has the wrong dimensions. Expected " + request.Preset.Label + " " +
                        request.Preset.Width + "x" + request.Preset.Height + ", got " +
                        actualWidth + "x" + actualHeight + ". The .wld was not copied into your Worlds folder.");
                }

                FileInfo generatedInfo = new FileInfo(generatedWorld);
                if (generatedInfo.Length <= 0)
                    throw new InvalidDataException("Terraria created an empty .wld file.");

                AppendLog(
                    "[World Maker] Validated generated .wld header: " + request.Preset.Label + " " +
                    actualWidth + "x" + actualHeight + ", " + generatedInfo.Length.ToString(CultureInfo.InvariantCulture) + " bytes.");

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
            bool hasSecretSeeds = request.SecretSeeds != null && request.SecretSeeds.Count > 0;
            string seedValue = hasSecretSeeds ? BuildCopiedSeedValue(request) : (request.Seed ?? string.Empty).Trim();

            var lines = new List<string>
            {
                "world=" + worldPath,
                "autocreate=3",
                "worldname=" + request.WorldName.Trim(),
                "seed=" + seedValue,
                "difficulty=" + request.Difficulty.ToString(CultureInfo.InvariantCulture),
                "maxplayers=1",
                "port=" + port.ToString(CultureInfo.InvariantCulture),
                "upnp=0",
                "secure=1",
                "worldrollbackstokeep=0"
            };

            // Terraria's dedicated-server config exposes seed_x flags for Special Seeds,
            // but not for the 1.4.5 Secret Seeds. When no Secret Seed is selected we keep
            // the simple server-flag path. With Secret Seeds selected, BuildCopiedSeedValue
            // serializes both sets into Terraria's native copied-seed format instead.
            if (!hasSecretSeeds && request.SpecialSeeds != null)
            {
                foreach (SpecialSeedOption option in request.SpecialSeeds)
                    lines.Add("seed_" + option.ConfigName + "=1");
            }

            return string.Join(Environment.NewLine, lines) + Environment.NewLine;
        }

        private static string BuildCopiedSeedValue(GenerationRequest request)
        {
            string baseSeed = (request.Seed ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(baseSeed))
                baseSeed = CreateRandomSeedText();

            int serializedSpecialSeeds = 0;
            if (request.SpecialSeeds != null)
            {
                foreach (SpecialSeedOption option in request.SpecialSeeds)
                    serializedSpecialSeeds |= option.SerializedValue;
            }

            var payload = new StringBuilder();
            foreach (SecretSeedOption option in request.SecretSeeds)
            {
                payload.Append(option.SeedText);
                payload.Append('|');
            }
            payload.Append(baseSeed);

            // Terraria's copied-seed parser requires an explicit evil value (1 Corruption,
            // 2 Crimson) rather than its UI's Random setting. Keep the World Maker's existing
            // no-evil-picker UI by choosing one deterministically from the effective seed text.
            int copiedEvil = SelectDeterministicEvil(payload.ToString());
            int copiedDifficulty = request.Difficulty + 1;

            // The copied-seed format only knows vanilla Small/Medium/Large. Large (3) is used
            // as the bootstrap size; ExpandedWorlds replaces it with the requested XL/Huge/THICC
            // dimensions before generation, just as it already does for autocreate=3.
            return "3." +
                copiedDifficulty.ToString(CultureInfo.InvariantCulture) + "." +
                copiedEvil.ToString(CultureInfo.InvariantCulture) + "." +
                serializedSpecialSeeds.ToString(CultureInfo.InvariantCulture) + "." +
                payload;
        }

        private static int SelectDeterministicEvil(string seedPayload)
        {
            unchecked
            {
                uint hash = 2166136261u;
                string text = seedPayload ?? string.Empty;
                for (int i = 0; i < text.Length; i++)
                {
                    hash ^= text[i];
                    hash *= 16777619u;
                }
                return (hash & 1u) == 0u ? 1 : 2;
            }
        }

        private static string CreateRandomSeedText()
        {
            int value = Guid.NewGuid().GetHashCode() & int.MaxValue;
            if (value == 0) value = 1;
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static bool TryReadWorldDimensions(string path, out int width, out int height, out string error)
        {
            width = 0;
            height = 0;
            error = null;

            try
            {
                if (!File.Exists(path))
                {
                    error = "The generated .wld does not exist yet.";
                    return false;
                }

                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new BinaryReader(stream, Encoding.UTF8))
                {
                    if (stream.Length < 32)
                    {
                        error = "The generated .wld is still too small to contain a complete header.";
                        return false;
                    }

                    uint version = reader.ReadUInt32();
                    if (version < 179)
                    {
                        error = "Unsupported Terraria world-file version " + version.ToString(CultureInfo.InvariantCulture) + ".";
                        return false;
                    }

                    // Terraria desktop world files from 1.3+ store the section count at 0x18.
                    // Pointer zero is the byte offset of the normal world header/flags section.
                    if (version < 140)
                    {
                        error = "Unsupported legacy Terraria world-file header.";
                        return false;
                    }

                    stream.Position = 0x18L;
                    short sectionCount = reader.ReadInt16();
                    if (sectionCount < 2 || sectionCount > 64)
                    {
                        error = "The generated .wld has an invalid section count: " + sectionCount.ToString(CultureInfo.InvariantCulture) + ".";
                        return false;
                    }

                    int headerOffset = reader.ReadInt32();
                    if (headerOffset <= stream.Position || headerOffset >= stream.Length)
                    {
                        error = "The generated .wld has an invalid header offset: " + headerOffset.ToString(CultureInfo.InvariantCulture) + ".";
                        return false;
                    }

                    stream.Position = headerOffset;
                    reader.ReadString(); // world name
                    if (version == 179)
                        reader.ReadInt32();
                    else
                        reader.ReadString(); // seed text
                    reader.ReadUInt64(); // world generator version

                    if (version >= 181)
                    {
                        byte[] guid = reader.ReadBytes(16);
                        if (guid.Length != 16)
                            throw new EndOfStreamException("World GUID is incomplete.");
                    }

                    reader.ReadInt32(); // world ID
                    reader.ReadInt32(); // left world edge
                    reader.ReadInt32(); // right world edge
                    reader.ReadInt32(); // top world edge
                    reader.ReadInt32(); // bottom world edge
                    height = reader.ReadInt32();
                    width = reader.ReadInt32();

                    if (width <= 0 || height <= 0)
                    {
                        error = "The generated .wld reported invalid dimensions " + width + "x" + height + ".";
                        return false;
                    }

                    return true;
                }
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
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
