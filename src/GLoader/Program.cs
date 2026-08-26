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
            var supportDirectory = Path.Combine(loaderDirectory, "gmods");
            var startupResolver = new ManagedAssemblyResolver(supportDirectory);
            var options = LoaderOptions.Parse(args);

            if (options.ShowHelp)
            {
                LoaderOptions.PrintHelp();
                return 0;
            }

            try
            {
                var targetPath = TargetLocator.Find(loaderDirectory, options);
                var gameDirectory = Path.GetDirectoryName(targetPath);
                var modsDirectory = string.IsNullOrWhiteSpace(options.ModsPath)
                    ? supportDirectory
                    : Path.GetFullPath(options.ModsPath);
                var isServerTarget = string.Equals(
                    Path.GetFileName(targetPath),
                    "TerrariaServer.exe",
                    StringComparison.OrdinalIgnoreCase);

                Log.Initialize(
                    Path.Combine(supportDirectory, "logs"),
                    isServerTarget ? "server" : "client");
                Log.Info("gloader 0.1.2-alpha");
                Log.Info("Target: " + targetPath);
                Log.Info("Target version: " + GetFileVersion(targetPath));
                Log.Info("Mode: " + (isServerTarget ? "server" : "client"));
                Log.Info("Mods: " + modsDirectory);

                Directory.SetCurrentDirectory(gameDirectory);
                NativeLibrarySearch.UseDirectory(gameDirectory);

                var gameAssembly = GameBootstrap.Load(targetPath);
                var gameArguments = options.GameArguments.ToArray();

                using (var resolver = new ManagedAssemblyResolver(
                    gameAssembly,
                    gameDirectory,
                    supportDirectory,
                    modsDirectory))
                {
                    if (!options.DisableMods)
                    {
                        TerrariaStartupState.Prepare(gameAssembly, gameArguments);

                        if (!isServerTarget)
                        {
                            HostPlayServerRedirect.Install(
                                gameAssembly,
                                Assembly.GetExecutingAssembly().Location,
                                modsDirectory);
                        }

                        ModRuntime.LoadAll(
                            modsDirectory,
                            gameAssembly,
                            gameDirectory,
                            supportDirectory,
                            isServerTarget);
                    }
                    else
                    {
                        Log.Info("Mods disabled by --no-mods.");
                    }

                    Log.Info("Starting Terraria.");
                    return GameBootstrap.InvokeEntryPoint(gameAssembly, gameArguments);
                }
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

                Console.Error.WriteLine();
                Console.Error.WriteLine("gloader failed:");
                Console.Error.WriteLine(ex);
                Console.Error.WriteLine();
                Console.Error.WriteLine("See gmods\\logs\\gloader-client.log or gmods\\logs\\gloader-server.log for details.");
                return 1;
            }
            finally
            {
                Log.Dispose();
                startupResolver.Dispose();
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
