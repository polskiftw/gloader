using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using TEdit.Terraria;

namespace WorldFamilyRenderer;

internal sealed class GenerationEngine
{
    private readonly RuntimePaths _runtime;
    private readonly string _jobRoot;
    private readonly string _vanillaMods;
    private readonly string _expandedMods;
    private Process _currentProcess;

    public GenerationEngine(RuntimePaths runtime)
    {
        _runtime = runtime;
        _jobRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "gloader",
            "WorldFamilyRenderer",
            "jobs",
            Guid.NewGuid().ToString("N"));
        _vanillaMods = Path.Combine(_jobRoot, "mods-vanilla");
        _expandedMods = Path.Combine(_jobRoot, "mods-expanded");

        Directory.CreateDirectory(_vanillaMods);
        Directory.CreateDirectory(_expandedMods);
        CopyDirectory(_runtime.ExpandedWorldModDirectory, Path.Combine(_expandedMods, "ExpandedWorlds"));
    }

    public async Task<string> GenerateAsync(
        SourceWorldInfo source,
        WorldPreset preset,
        IProgress<string> status,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string presetDir = Path.Combine(_jobRoot, preset.Number.ToString("00") + "-" + preset.Name);
        Directory.CreateDirectory(presetDir);
        string worldPath = Path.Combine(presetDir, "fresh.wld");
        string configPath = Path.Combine(presetDir, "serverconfig.txt");
        string logPath = Path.Combine(presetDir, "server.log");
        int port = ReserveTcpPort();

        string copiedSeed = source.BuildCopiedSeed(preset).Replace("\r", string.Empty).Replace("\n", string.Empty);
        string config = string.Join(Environment.NewLine, new[]
        {
            "world=" + worldPath,
            "autocreate=" + preset.AutoCreate,
            "worldname=WorldFamily_" + preset.Name,
            "seed=" + copiedSeed,
            "difficulty=" + Math.Clamp(source.GameMode, 0, 3),
            "maxplayers=1",
            "port=" + port,
            "password=",
            "motd=World Family Renderer temporary generation job"
        }) + Environment.NewLine;
        File.WriteAllText(configPath, config, new UTF8Encoding(false));

        string modsPath = preset.IsExpanded ? _expandedMods : _vanillaMods;
        var psi = new ProcessStartInfo
        {
            FileName = _runtime.GLoaderExe,
            WorkingDirectory = _runtime.TerrariaRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true
        };
        psi.ArgumentList.Add("--run");
        psi.ArgumentList.Add("--server");
        psi.ArgumentList.Add("--mods");
        psi.ArgumentList.Add(modsPath);
        psi.ArgumentList.Add("--");
        psi.ArgumentList.Add("-config");
        psi.ArgumentList.Add(configPath);
        psi.ArgumentList.Add("-noupnp");
        psi.ArgumentList.Add("-ip");
        psi.ArgumentList.Add("127.0.0.1");

        psi.Environment.Remove("GLOADER_EXPANDED_WORLD");
        if (preset.IsExpanded)
            psi.Environment["GLOADER_EXPANDED_WORLD"] = preset.ExpandedEnvironment;

        var recent = new Queue<string>();
        var logLock = new object();
        using var log = new StreamWriter(logPath, append: false, new UTF8Encoding(false)) { AutoFlush = true };

        void Capture(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            lock (logLock)
            {
                log.WriteLine(line);
                recent.Enqueue(line);
                while (recent.Count > 24) recent.Dequeue();
            }

            string trimmed = line.Trim();
            if (trimmed.Length > 140) trimmed = trimmed[..140] + "...";
            status?.Report($"{preset.Name}: {trimmed}");
        }

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _currentProcess = process;
        process.OutputDataReceived += (_, e) => { if (e.Data != null) Capture(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) Capture(e.Data); };

        status?.Report($"{preset.Name}: generating {preset.Width:N0} x {preset.Height:N0} with 64-bit gloader...");
        if (!process.Start())
            throw new InvalidOperationException("Could not start gloader.exe.");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using CancellationTokenRegistration registration = cancellationToken.Register(() => TryKill(process));
        var deadline = DateTime.UtcNow.AddMinutes(30);
        bool ready = false;

        try
        {
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (process.HasExited)
                {
                    string tail;
                    lock (logLock) tail = string.Join(Environment.NewLine, recent);
                    throw new InvalidOperationException(
                        $"gloader/Terraria exited while generating {preset.Name} (code {process.ExitCode}).\n\n{tail}");
                }

                if (await CanConnectAsync(port, cancellationToken).ConfigureAwait(false) &&
                    TryValidateWorld(worldPath, preset, out _))
                {
                    ready = true;
                    break;
                }

                await Task.Delay(750, cancellationToken).ConfigureAwait(false);
            }

            if (!ready)
                throw new TimeoutException($"Timed out while generating {preset.Name}. See {logPath}");

            status?.Report($"{preset.Name}: world complete; stopping temporary server...");
            try
            {
                await process.StandardInput.WriteLineAsync("exit-nosave").ConfigureAwait(false);
                await process.StandardInput.FlushAsync().ConfigureAwait(false);
            }
            catch
            {
                // The generated file has already been validated. A hard stop is safe here.
            }

            using var exitCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            exitCts.CancelAfter(TimeSpan.FromSeconds(20));
            try
            {
                await process.WaitForExitAsync(exitCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                TryKill(process);
            }

            if (!TryValidateWorld(worldPath, preset, out string validationError))
                throw new InvalidDataException(validationError);

            return worldPath;
        }
        finally
        {
            _currentProcess = null;
            if (!process.HasExited)
                TryKill(process);
            process.Dispose();
        }
    }

    public void CancelCurrentProcess() => TryKill(_currentProcess);

    public void Cleanup()
    {
        CancelCurrentProcess();
        try
        {
            if (Directory.Exists(_jobRoot))
                Directory.Delete(_jobRoot, recursive: true);
        }
        catch
        {
            // Temp cleanup is best effort. A future run uses a new GUID directory.
        }
    }

    private static bool TryValidateWorld(string worldPath, WorldPreset preset, out string errorMessage)
    {
        errorMessage = null;
        if (!File.Exists(worldPath)) return false;

        try
        {
            var (world, error) = World.LoadWorld(worldPath, headersOnly: true);
            if (error != null || world == null)
            {
                errorMessage = "The generated world header could not be read yet.";
                return false;
            }

            if (world.TilesWide != preset.Width || world.TilesHigh != preset.Height)
            {
                errorMessage =
                    $"Generated {preset.Name} has the wrong dimensions: {world.TilesWide:N0} x {world.TilesHigh:N0}; " +
                    $"expected {preset.Width:N0} x {preset.Height:N0}.";
                return false;
            }

            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static int ReserveTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<bool> CanConnectAsync(int port, CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(250));
            await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static void TryKill(Process process)
    {
        if (process == null) return;
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best effort cancellation.
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(destination, relative));
        }

        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(source, file);
            string target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}
