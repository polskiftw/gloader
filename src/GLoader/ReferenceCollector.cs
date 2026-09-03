using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;

namespace GLoader
{
    internal static class ReferenceCollector
    {
        public static IReadOnlyList<MetadataReference> Collect(
            Assembly gameAssembly,
            string gameDirectory,
            string supportDirectory)
        {
            var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            AddTrustedPlatformAssemblies(paths);

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                AddAssemblyLocation(paths, assembly, overwrite: false);

            AddManagedFiles(paths, supportDirectory, overwrite: false, recursive: false);
            AddManagedFiles(paths, gameDirectory, overwrite: false, recursive: false);
            AddManagedFiles(paths, Path.Combine(gameDirectory, "Libraries"), overwrite: false, recursive: true);

            // Terraria.exe and TerrariaServer.exe both define Terraria.Main. Never feed
            // the opposite executable to Roslyn or source mods get CS0433 ambiguity.
            RemoveOppositeTerrariaAssembly(paths, gameAssembly);

            // The exact Terraria assembly selected by the user always wins over a
            // same-named assembly that might already have been visible elsewhere.
            AddAssemblyLocation(paths, gameAssembly, overwrite: true);

            var references = new List<MetadataReference>();
            foreach (var path in paths.Values.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    references.Add(MetadataReference.CreateFromFile(path));
                }
                catch (BadImageFormatException)
                {
                    // Ignore native binaries.
                }
                catch (IOException ex)
                {
                    Log.Warn("Could not use compiler reference " + path + ": " + ex.Message);
                }
            }

            return references;
        }

        private static void AddTrustedPlatformAssemblies(IDictionary<string, string> paths)
        {
            var trusted = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
            if (string.IsNullOrWhiteSpace(trusted))
                return;

            foreach (var path in trusted.Split(Path.PathSeparator))
                AddManagedPath(paths, path, overwrite: false);
        }

        private static void RemoveOppositeTerrariaAssembly(
            IDictionary<string, string> paths,
            Assembly gameAssembly)
        {
            var targetName = gameAssembly.GetName().Name;
            if (string.Equals(targetName, "Terraria", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(targetName, "TerrariaRelease", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(targetName, "TerrariaDebug", StringComparison.OrdinalIgnoreCase))
            {
                paths.Remove("TerrariaServer");
            }
            else if (string.Equals(targetName, "TerrariaServer", StringComparison.OrdinalIgnoreCase))
            {
                paths.Remove("Terraria");
                paths.Remove("TerrariaRelease");
                paths.Remove("TerrariaDebug");
            }
        }

        private static void AddManagedFiles(
            IDictionary<string, string> paths,
            string directory,
            bool overwrite,
            bool recursive)
        {
            if (!Directory.Exists(directory))
                return;

            var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            foreach (var path in Directory.EnumerateFiles(directory, "*.dll", searchOption))
                AddManagedPath(paths, path, overwrite);

            foreach (var path in Directory.EnumerateFiles(directory, "*.exe", searchOption))
                AddManagedPath(paths, path, overwrite);
        }

        private static void AddAssemblyLocation(
            IDictionary<string, string> paths,
            Assembly assembly,
            bool overwrite)
        {
            try
            {
                if (assembly.IsDynamic || string.IsNullOrWhiteSpace(assembly.Location))
                    return;

                AddManagedPath(paths, assembly.Location, overwrite);
            }
            catch (NotSupportedException)
            {
                // Dynamic or byte-loaded assembly with no usable location.
            }
        }

        private static void AddManagedPath(
            IDictionary<string, string> paths,
            string path,
            bool overwrite)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    return;

                var fullPath = Path.GetFullPath(path);
                var name = AssemblyName.GetAssemblyName(fullPath).Name;

                if (overwrite || !paths.ContainsKey(name))
                    paths[name] = fullPath;
            }
            catch (BadImageFormatException)
            {
                // Native DLL/exe.
            }
            catch (FileLoadException)
            {
                // Not a usable managed reference.
            }
            catch (FileNotFoundException)
            {
                // File disappeared while scanning; ignore it.
            }
        }
    }
}
