using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace GLoader
{
    internal static class ReferenceCollector
    {
        public static IReadOnlyList<string> Collect(
            Assembly gameAssembly,
            string gameDirectory,
            string supportDirectory)
        {
            var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                AddAssemblyLocation(paths, assembly, overwrite: false);
            }

            // Terraria's XNA Framework dependencies normally live in the Windows GAC,
            // not beside Terraria.exe. Merely loading Terraria's managed assembly does
            // not force those references into the AppDomain before source-mod compilation,
            // so scanning loaded assemblies/directories alone misses Microsoft.Xna.Framework
            // and friends. Resolve the target's direct assembly references now and add any
            // file-backed locations (including GAC paths) to the Roslyn reference manifest.
            AddReferencedAssemblyLocations(paths, gameAssembly);

            // Source mods are allowed to use loader-provided runtime libraries such as
            // Harmony. Those assemblies may not have been JIT-loaded yet when reference
            // collection runs, so scan the loader output directory explicitly.
            AddManagedFiles(paths, AppDomain.CurrentDomain.BaseDirectory, overwrite: false);
            AddManagedFiles(paths, supportDirectory, overwrite: false);
            AddManagedFiles(paths, gameDirectory, overwrite: false);

            // Terraria.exe and TerrariaServer.exe both define Terraria.Main. Never feed
            // the opposite executable to the compiler or source mods get CS0433 ambiguity.
            RemoveOppositeTerrariaAssembly(paths, gameAssembly);

            // The exact Terraria assembly selected by the user always wins over a
            // same-named assembly that might already have been visible elsewhere.
            AddAssemblyLocation(paths, gameAssembly, overwrite: true);

            return paths.Values
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static void AddReferencedAssemblyLocations(
            IDictionary<string, string> paths,
            Assembly gameAssembly)
        {
            foreach (var referenceName in gameAssembly.GetReferencedAssemblies())
            {
                try
                {
                    var assembly = AppDomain.CurrentDomain.GetAssemblies()
                        .FirstOrDefault(candidate =>
                            AssemblyName.ReferenceMatchesDefinition(
                                candidate.GetName(),
                                referenceName));

                    if (assembly == null)
                    {
                        assembly = Assembly.Load(referenceName);
                    }

                    AddAssemblyLocation(paths, assembly, overwrite: false);
                }
                catch (FileNotFoundException)
                {
                    // Some Terraria dependencies are embedded and intentionally have no
                    // file-backed compiler reference. The embedded resolver can still load
                    // those later at runtime; only usable on-disk locations belong here.
                }
                catch (FileLoadException)
                {
                    // Keep collecting the remaining references. Roslyn will report a
                    // concrete missing-reference diagnostic if source actually needs this one.
                }
                catch (BadImageFormatException)
                {
                    // Native/wrong-architecture dependency, not a C# metadata reference.
                }
            }
        }

        private static void RemoveOppositeTerrariaAssembly(
            IDictionary<string, string> paths,
            Assembly gameAssembly)
        {
            var targetName = gameAssembly.GetName().Name;
            if (string.Equals(targetName, "Terraria", StringComparison.OrdinalIgnoreCase))
            {
                paths.Remove("TerrariaServer");
            }
            else if (string.Equals(targetName, "TerrariaServer", StringComparison.OrdinalIgnoreCase))
            {
                paths.Remove("Terraria");
            }
        }

        private static void AddManagedFiles(
            IDictionary<string, string> paths,
            string directory,
            bool overwrite)
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            foreach (var path in Directory.EnumerateFiles(directory, "*.dll", SearchOption.TopDirectoryOnly))
            {
                AddManagedPath(paths, path, overwrite);
            }

            foreach (var path in Directory.EnumerateFiles(directory, "*.exe", SearchOption.TopDirectoryOnly))
            {
                AddManagedPath(paths, path, overwrite);
            }
        }

        private static void AddAssemblyLocation(
            IDictionary<string, string> paths,
            Assembly assembly,
            bool overwrite)
        {
            try
            {
                if (assembly.IsDynamic || string.IsNullOrWhiteSpace(assembly.Location))
                {
                    return;
                }

                AddManagedPath(paths, assembly.Location, overwrite);
            }
            catch (NotSupportedException)
            {
                // Dynamic assembly with no usable location.
            }
        }

        private static void AddManagedPath(
            IDictionary<string, string> paths,
            string path,
            bool overwrite)
        {
            try
            {
                var fullPath = Path.GetFullPath(path);
                var name = AssemblyName.GetAssemblyName(fullPath).Name;

                if (overwrite || !paths.ContainsKey(name))
                {
                    paths[name] = fullPath;
                }
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
            catch (IOException ex)
            {
                Log.Warn("Could not inspect compiler reference " + path + ": " + ex.Message);
            }
        }
    }
}
