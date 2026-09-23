using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace GLoader
{
    public static class Entry
    {
        private static readonly object Gate = new object();
        private static AssemblyResolver _resolver;
        private static bool _logReady;
        private static bool _initialized;
        private static bool _processExitRegistered;

        public static int Initialize()
        {
            lock (Gate)
            {
                if (_initialized)
                    return 0;

                var root = ResolveRoot();
                var dependenciesDirectory = Path.Combine(root, "gdeps");
                var modsDirectory = Path.Combine(root, "gmods");
                var logsDirectory = Path.Combine(dependenciesDirectory, "logs");
                var options = LoaderOptions.FromEnvironment();

                try
                {
                    Directory.CreateDirectory(dependenciesDirectory);
                    Directory.CreateDirectory(modsDirectory);

                    Log.Initialize(logsDirectory, options.DedicatedServer ? "server" : "client");
                    _logReady = true;

                    if (typeof(object).Assembly.GetType("Mono.Runtime", false) == null)
                        throw new PlatformNotSupportedException("Linux gloader must run inside Terraria's Mono runtime.");

                    Log.Info("gloader 0.3.0-alpha (Linux/MonoKickstart)");
                    Log.Info("Terraria root: " + root);
                    Log.Info("Mode: " + (options.DedicatedServer ? "server" : "client"));
                    Log.Info("Mods: " + (options.DisableMods ? "disabled for this run" : modsDirectory));

                    _resolver = new AssemblyResolver(root, dependenciesDirectory);
                    _resolver.Install();

                    Directory.SetCurrentDirectory(root);
                    var gameAssembly = FindLoadedTerraria(options.DedicatedServer);
                    _resolver.PreferAssembly(gameAssembly);

                    Log.Info("Attached target: " + gameAssembly.FullName);

                    if (!options.DedicatedServer)
                    {
                        _resolver.LoadEmbedded(
                            gameAssembly,
                            "ReLogic",
                            "Terraria.Libraries.ReLogic.ReLogic.dll");
                    }

                    if (!options.DisableMods)
                    {
                        if (!options.DedicatedServer)
                            HostPlayServerRedirect.TryInstall(gameAssembly, Path.Combine(root, "gloader"), root);

                        ModRuntime.LoadAll(
                            modsDirectory,
                            gameAssembly,
                            root,
                            dependenciesDirectory,
                            _resolver,
                            options.DedicatedServer);
                    }
                    else
                    {
                        Log.Info("Source mods skipped for this run.");
                    }

                    RegisterProcessExit();
                    _initialized = true;
                    Log.Info("gloader initialization complete; returning control to Terraria's Mono host.");
                    return 0;
                }
                catch (Exception ex)
                {
                    if (_logReady)
                        Log.Error(ex.ToString());

                    Console.Error.WriteLine("gloader failed:");
                    Console.Error.WriteLine(ex);
                    Cleanup();
                    return 1;
                }
            }
        }

        private static Assembly FindLoadedTerraria(bool dedicatedServer)
        {
            var expected = dedicatedServer ? "TerrariaServer" : "Terraria";
            var loaded = AppDomain.CurrentDomain.GetAssemblies();

            var exact = loaded.FirstOrDefault(assembly =>
            {
                try
                {
                    return string.Equals(assembly.GetName().Name, expected, StringComparison.OrdinalIgnoreCase);
                }
                catch
                {
                    return false;
                }
            });

            if (exact != null)
                return exact;

            throw new InvalidOperationException(
                "The gloader profiler was injected, but the expected already-loaded managed assembly '" +
                expected + "' was not visible in the current Mono AppDomain.");
        }

        private static string ResolveRoot()
        {
            var explicitRoot = Environment.GetEnvironmentVariable("GLOADER_ROOT");
            if (!string.IsNullOrWhiteSpace(explicitRoot))
                return Path.GetFullPath(explicitRoot);

            var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (string.IsNullOrWhiteSpace(assemblyDirectory))
                return Path.GetFullPath(Environment.CurrentDirectory);

            var parent = Directory.GetParent(Path.GetFullPath(assemblyDirectory));
            return parent == null ? Path.GetFullPath(assemblyDirectory) : parent.FullName;
        }

        private static void RegisterProcessExit()
        {
            if (_processExitRegistered)
                return;

            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            _processExitRegistered = true;
        }

        private static void OnProcessExit(object sender, EventArgs args)
        {
            lock (Gate)
                Cleanup();
        }

        private static void Cleanup()
        {
            if (_resolver != null)
            {
                _resolver.Dispose();
                _resolver = null;
            }

            if (_logReady)
            {
                Log.Dispose();
                _logReady = false;
            }
        }
    }
}
