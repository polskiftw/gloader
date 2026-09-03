using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace GLoader
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            var loaderDirectory = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
            var defaultModsDirectory = Path.Combine(loaderDirectory, "gmods");
            var dependenciesDirectory = Path.Combine(loaderDirectory, "gdeps");
            var logsDirectory = Path.Combine(dependenciesDirectory, "logs");
            var startupResolver = new ManagedAssemblyResolver(dependenciesDirectory);
            var launchedFromGui = false;

            try
            {
                var options = LoaderOptions.Parse(args);

                if (args.Length == 0)
                {
                    ConsoleManager.DetachForGui();

                    var launch = LauncherForm.ShowLauncher(defaultModsDirectory, logsDirectory);
                    if (launch.Action == LauncherAction.Cancel)
                    {
                        return 0;
                    }

                    launchedFromGui = true;
                    if (launch.ShowConsole)
                    {
                        ConsoleManager.EnsureConsole();
                    }

                    if (launch.Action == LauncherAction.Vanilla)
                    {
                        options.DisableModsForRun();
                    }
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
            var gameDirectory = Path.GetDirectoryName(targetPath);
            var modsDirectory = string.IsNullOrWhiteSpace(options.ModsPath)
                ? defaultModsDirectory
                : Path.GetFullPath(options.ModsPath);
            var compilerPath = Path.Combine(dependenciesDirectory, "compiler", "gloader.compiler.exe");
            var isServerTarget = string.Equals(
                Path.GetFileName(targetPath),
                "TerrariaServer.exe",
                StringComparison.OrdinalIgnoreCase);

            Log.Initialize(logsDirectory, isServerTarget ? "server" : "client");
            Log.Info("gloader " + GetLoaderVersion());
            Log.Info("Target: " + targetPath);
            Log.Info("Target version: " + GetFileVersion(targetPath));
            Log.Info("Mode: " + (isServerTarget ? "server" : "client"));
            Log.Info("Mods: " + modsDirectory);
            Log.Info("Dependencies: " + dependenciesDirectory);
            Log.Info("Compiler helper: " + compilerPath);

            Directory.SetCurrentDirectory(gameDirectory);
            NativeLibrarySearch.UseDirectory(gameDirectory);

            var gameAssembly = GameBootstrap.Load(targetPath);
            var gameArguments = options.GameArguments.ToArray();

            using (var resolver = new ManagedAssemblyResolver(
                gameDirectory,
                dependenciesDirectory,
                modsDirectory))
            {
                if (!options.DisableMods)
                {
                    TerrariaStartupState.Prepare(gameAssembly, gameArguments);

                    // The Host & Play redirect is shipped as raw C# in
                    // gdeps\coremods rather than embedded inside gloader.exe. Give that
                    // built-in source mod the two runtime values it needs, then let the
                    // normal out-of-process source compiler load it like any other mod.
                    AppDomain.CurrentDomain.SetData(
                        "GLoader.LoaderPath",
                        Assembly.GetExecutingAssembly().Location);
                    AppDomain.CurrentDomain.SetData(
                        "GLoader.ModsDirectory",
                        modsDirectory);

                    ModRuntime.LoadAll(
                        modsDirectory,
                        gameAssembly,
                        gameDirectory,
                        dependenciesDirectory,
                        isServerTarget,
                        compilerPath);
                }
                else
                {
                    Log.Info("Mods disabled by --no-mods or the launcher.");
                }

                Log.Info("Starting Terraria.");
                return GameBootstrap.InvokeEntryPoint(gameAssembly, gameArguments);
            }
        }

        private static string GetLoaderVersion()
        {
            try
            {
                var path = Assembly.GetExecutingAssembly().Location;
                return FileVersionInfo.GetVersionInfo(path).ProductVersion ?? "unknown";
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
