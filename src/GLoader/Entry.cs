using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace GLoader
{
    public static class Entry
    {
        public static int Run()
        {
            var root = ResolveRoot();
            var dependenciesDirectory = Path.Combine(root, "gdeps");
            var modsDirectory = Path.Combine(root, "gmods");
            var logsDirectory = Path.Combine(dependenciesDirectory, "logs");
            var options = LoaderOptions.Parse(root, NativeArguments.Decode());

            if (options.ShowHelp)
            {
                LoaderOptions.PrintHelp();
                return 0;
            }

            AssemblyResolver resolver = null;
            var logReady = false;

            try
            {
                Directory.CreateDirectory(dependenciesDirectory);
                Directory.CreateDirectory(modsDirectory);

                Log.Initialize(logsDirectory, options.DedicatedServer ? "server" : "client");
                logReady = true;

                if (typeof(object).Assembly.GetType("Mono.Runtime", false) == null)
                    throw new PlatformNotSupportedException("Linux gloader must run inside Mono. CoreCLR is not a supported Linux host.");

                Log.Info("gloader 0.3.0-alpha (Linux/Mono)");
                Log.Info("Terraria root: " + root);
                Log.Info("Mode: " + (options.DedicatedServer ? "server" : "client"));
                Log.Info("Mods: " + (options.DisableMods ? "disabled for this run" : modsDirectory));

                resolver = new AssemblyResolver(root, dependenciesDirectory);
                resolver.Install();

                Directory.SetCurrentDirectory(root);
                var targetPath = TargetLocator.Find(root, options.DedicatedServer);
                var gameAssembly = GameBootstrap.Load(targetPath);
                resolver.PreferAssembly(gameAssembly);

                Log.Info("Target: " + targetPath);
                Log.Info("Target assembly: " + gameAssembly.FullName);

                if (!options.DisableMods)
                {
                    if (!options.DedicatedServer)
                        HostPlayServerRedirect.TryInstall(gameAssembly, Path.Combine(root, "gloader"), root);

                    ModRuntime.LoadAll(
                        modsDirectory,
                        gameAssembly,
                        root,
                        dependenciesDirectory,
                        resolver,
                        options.DedicatedServer);
                }
                else
                {
                    Log.Info("Source mods skipped for this run.");
                }

                Log.Info("Starting Terraria.");
                return GameBootstrap.InvokeEntryPoint(gameAssembly, options.GameArguments.ToArray());
            }
            catch (Exception ex)
            {
                if (logReady)
                    Log.Error(ex.ToString());

                Console.Error.WriteLine("gloader failed:");
                Console.Error.WriteLine(ex);
                return 1;
            }
            finally
            {
                if (resolver != null)
                    resolver.Dispose();

                if (logReady)
                    Log.Dispose();
            }
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
    }

    internal static class NativeArguments
    {
        public static string[] Decode()
        {
            var countText = Environment.GetEnvironmentVariable("GLOADER_ARGC");
            int count;
            if (!int.TryParse(countText, out count) || count < 0)
                throw new FormatException("Malformed GLOADER_ARGC value.");

            if (count == 0)
                return Array.Empty<string>();

            var encoded = Environment.GetEnvironmentVariable("GLOADER_ARGV_HEX") ?? string.Empty;
            var parts = encoded.Split(new[] { ',' }, StringSplitOptions.None);

            if (parts.Length != count)
                throw new FormatException("GLOADER_ARGV_HEX does not match GLOADER_ARGC.");

            return parts.Select(DecodeOne).ToArray();
        }

        private static string DecodeOne(string value)
        {
            if (value.Length == 0)
                return string.Empty;

            if ((value.Length & 1) != 0)
                throw new FormatException("Malformed GLOADER_ARGV_HEX argument.");

            var bytes = new byte[value.Length / 2];
            for (var index = 0; index < bytes.Length; index++)
            {
                var high = Hex(value[index * 2]);
                var low = Hex(value[index * 2 + 1]);
                if (high < 0 || low < 0)
                    throw new FormatException("Malformed GLOADER_ARGV_HEX argument.");

                bytes[index] = (byte)((high << 4) | low);
            }

            return Encoding.UTF8.GetString(bytes);
        }

        private static int Hex(char value)
        {
            if (value >= '0' && value <= '9')
                return value - '0';
            if (value >= 'a' && value <= 'f')
                return value - 'a' + 10;
            if (value >= 'A' && value <= 'F')
                return value - 'A' + 10;
            return -1;
        }
    }
}
