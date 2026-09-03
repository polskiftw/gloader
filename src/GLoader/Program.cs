using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace GLoader
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            var loaderDirectory = Path.GetFullPath(AppContext.BaseDirectory);
            var defaultModsDirectory = Path.Combine(loaderDirectory, "gmods");
            var dependenciesDirectory = Path.Combine(loaderDirectory, "gdeps");
            var logsDirectory = Path.Combine(dependenciesDirectory, "logs");
            var startupResolver = new ManagedAssemblyResolver(dependenciesDirectory);
            var launchedFromGui = false;

            try
            {
                if (!Environment.Is64BitProcess)
                    throw new PlatformNotSupportedException("gloader 0.2+ requires a 64-bit Windows process.");

                var options = LoaderOptions.Parse(args);

                if (args.Length == 0)
                {
                    ConsoleManager.DetachForGui();

                    var launch = LauncherForm.ShowLauncher(defaultModsDirectory, logsDirectory);
                    if (launch.Action == LauncherAction.Cancel)
                        return 0;

                    launchedFromGui = true;
                    if (launch.ShowConsole)
                        ConsoleManager.EnsureConsole();

                    if (launch.Action == LauncherAction.Vanilla)
                        options.DisableModsForRun();
                }

                if (options.ShowHelp)
                {
                    LoaderOptions.PrintHelp();
                    return 0;
                }

                return RunLoader(
                    loaderDirectory,
                    defaultModsDirectory,
                    dependenciesDirectory,
                    logsDirectory,
                    options);
            }
            catch (Exception ex)
            {
                try
                {
                    Log.Error(ex.ToString());
                }
                catch
                {
                    // Logging must never hide the original startup error.
                }

                if (launchedFromGui)
                {
                    LauncherForm.ShowStartupFailure(logsDirectory, ex);
                }
                else
                {
                    Console.Error.WriteLine();
                    Console.Error.WriteLine("gloader failed:");
                    Console.Error.WriteLine(ex);
                    Console.Error.WriteLine();
                    Console.Error.WriteLine("See gdeps\\logs\\gloader-client.log or gdeps\\logs\\gloader-server.log for details.");
                }

                return 1;
            }
            finally
            {
                Log.Dispose();
                startupResolver.Dispose();
            }
        }

        private static int RunLoader(
            string loaderDirectory,
            string defaultModsDirectory,
            string dependenciesDirectory,
            string logsDirectory,
            LoaderOptions options)
        {
            var targetPath = TargetLocator.Find(loaderDirectory, options);
            var runtimeInfo = TargetRuntimeInfo.Inspect(targetPath);

            // Modern .NET executables normally have a native apphost beside the managed
            // assembly. If somebody points gloader at TerrariaRelease.exe, silently use
            // TerrariaRelease.dll instead of treating the apphost as Terraria itself.
            if (!runtimeInfo.HasMetadata)
            {
                var managedSibling = Path.ChangeExtension(targetPath, ".dll");
                if (File.Exists(managedSibling))
                {
                    var siblingInfo = TargetRuntimeInfo.Inspect(managedSibling);
                    if (siblingInfo.HasMetadata)
                    {
                        targetPath = managedSibling;
                        runtimeInfo = siblingInfo;
                    }
                }
            }

            ValidateRuntimeTarget(targetPath, runtimeInfo);

            var gameDirectory = Path.GetDirectoryName(targetPath);
            var modsDirectory = string.IsNullOrWhiteSpace(options.ModsPath)
                ? defaultModsDirectory
                : Path.GetFullPath(options.ModsPath);
            var targetFileName = Path.GetFileName(targetPath);
            var isServerTarget = options.DedicatedServer ||
                targetFileName.StartsWith("TerrariaServer", StringComparison.OrdinalIgnoreCase);

            Log.Initialize(logsDirectory, isServerTarget ? "server" : "client");
            Log.Info("gloader " + GetLoaderVersion());
            Log.Info("Process: " + (Environment.Is64BitProcess ? "x64" : "x86") + " CoreCLR " + Environment.Version);
            Log.Info("Target: " + targetPath);
            Log.Info("Target version: " + GetFileVersion(targetPath));
            Log.Info("Target runtime: " + runtimeInfo.Description + " (machine " + runtimeInfo.Machine + ")");
            Log.Info("Mode: " + (isServerTarget ? "server" : "client"));
            Log.Info("Mods: " + modsDirectory);
            Log.Info("Dependencies: " + dependenciesDirectory);

            Directory.SetCurrentDirectory(gameDirectory);

            var runtimeDirectories = GetRuntimeDirectories(gameDirectory);
            var nativeDirectory = runtimeDirectories.FirstOrDefault(path =>
                path.EndsWith(Path.Combine("Native", "Windows"), StringComparison.OrdinalIgnoreCase))
                ?? runtimeDirectories.FirstOrDefault(path =>
                    path.IndexOf(Path.DirectorySeparatorChar + "native", StringComparison.OrdinalIgnoreCase) >= 0)
                ?? gameDirectory;
            NativeLibrarySearch.UseDirectory(nativeDirectory);

            using var runtimeResolver = new ManagedAssemblyResolver(
                runtimeDirectories
                    .Concat(new[] { dependenciesDirectory, modsDirectory })
                    .ToArray());

            var gameAssembly = GameBootstrap.Load(targetPath);
            var gameArguments = options.GameArguments.ToList();
            if (options.DedicatedServer && runtimeInfo.IsModernCoreClr &&
                !gameArguments.Any(argument => argument.Equals("-server", StringComparison.OrdinalIgnoreCase)))
            {
                gameArguments.Insert(0, "-server");
            }

            using (var resolver = new ManagedAssemblyResolver(
                gameAssembly,
                runtimeDirectories
                    .Concat(new[] { dependenciesDirectory, modsDirectory })
                    .ToArray()))
            {
                if (!options.DisableMods)
                {
                    TerrariaStartupState.Prepare(gameAssembly, gameArguments.ToArray());

                    if (!isServerTarget)
                    {
                        HostPlayServerRedirect.Install(
                            gameAssembly,
                            GetLoaderExecutablePath(),
                            modsDirectory,
                            IsUnifiedRuntimeTarget(targetPath) ? targetPath : null);
                    }

                    ModRuntime.LoadAll(
                        modsDirectory,
                        gameAssembly,
                        gameDirectory,
                        dependenciesDirectory,
                        isServerTarget);
                }
                else
                {
                    Log.Info("Mods disabled by --no-mods or the launcher.");
                }

                Log.Info("Starting Terraria.");
                return GameBootstrap.InvokeEntryPoint(gameAssembly, gameArguments.ToArray());
            }
        }

        private static void ValidateRuntimeTarget(string targetPath, TargetRuntimeInfo runtimeInfo)
        {
            if (!runtimeInfo.HasMetadata)
            {
                throw new BadImageFormatException(
                    "The selected Terraria target is not a managed assembly. For a modern .NET build, " +
                    "select TerrariaRelease.dll rather than its native .exe apphost.",
                    targetPath);
            }

            if (runtimeInfo.Requires32Bit || runtimeInfo.UsesLegacyXna || runtimeInfo.ReferencesMscorlib)
            {
                throw new PlatformNotSupportedException(
                    "The selected target is stock/legacy 32-bit XNA Terraria and cannot live inside 64-bit gloader. " +
                    "Install the rebuilt CoreCLR/FNA runtime at gdeps\\x64-runtime\\TerrariaRelease.dll. " +
                    "The x64 runtime builder uses your own Terraria installation as its source; gloader does not ship Terraria.");
            }

            if (!runtimeInfo.IsModernCoreClr)
            {
                throw new PlatformNotSupportedException(
                    "The selected Terraria assembly is managed, but it is not a supported modern CoreCLR target. " +
                    "Expected the TerrariaNetCore-style .NET runtime used by gloader x64.");
            }
        }

        private static string[] GetRuntimeDirectories(string gameDirectory)
        {
            var candidates = new[]
            {
                gameDirectory,
                Path.Combine(gameDirectory, "Libraries"),
                Path.Combine(gameDirectory, "Libraries", "Native"),
                Path.Combine(gameDirectory, "Libraries", "Native", "Windows"),
                Path.Combine(gameDirectory, "Libraries", "Native", "Windows", "x64"),
                Path.Combine(gameDirectory, "runtimes", "win-x64", "native")
            };

            return candidates
                .Where(Directory.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static bool IsUnifiedRuntimeTarget(string targetPath)
        {
            var name = Path.GetFileNameWithoutExtension(targetPath);
            return name.Equals("TerrariaRelease", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("TerrariaDebug", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetLoaderExecutablePath()
        {
            if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
                return Path.GetFullPath(Environment.ProcessPath);

            try
            {
                var mainModule = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(mainModule))
                    return Path.GetFullPath(mainModule);
            }
            catch
            {
                // Fall through to the historical assembly-location fallback.
            }

            return Path.GetFullPath(Assembly.GetExecutingAssembly().Location);
        }

        private static string GetLoaderVersion()
        {
            try
            {
                return FileVersionInfo.GetVersionInfo(GetLoaderExecutablePath()).ProductVersion ?? "unknown";
            }
            catch
            {
                return "unknown";
            }
        }

        private static string GetFileVersion(string path)
        {
            try
            {
                return FileVersionInfo.GetVersionInfo(path).FileVersion ?? "unknown";
            }
            catch
            {
                return "unknown";
            }
        }
    }
}
