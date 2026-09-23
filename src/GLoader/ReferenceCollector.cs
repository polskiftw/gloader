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
            string root,
            string dependencies,
            string modDirectory)
        {
            var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var inMemoryAssemblies = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!AddAssemblyLocation(paths, assembly, false))
                    AddInMemoryAssembly(inMemoryAssemblies, assembly);
            }

            AddManagedFiles(paths, root, false);
            AddManagedFiles(paths, dependencies, false);
            AddManagedFiles(paths, modDirectory, false);

            RemoveOtherTerrariaAssemblies(paths, gameAssembly);
            AddAssemblyLocation(paths, gameAssembly, true);
            inMemoryAssemblies.Remove(gameAssembly.GetName().Name);

            var references = new List<MetadataReference>();
            foreach (var path in paths.Values.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    references.Add(MetadataReference.CreateFromFile(path));
                }
                catch (BadImageFormatException)
                {
                }
                catch (IOException ex)
                {
                    Log.Warn("Could not use compiler reference " + path + ": " + ex.Message);
                }
            }

            foreach (var pair in inMemoryAssemblies.OrderBy(
                pair => pair.Key,
                StringComparer.OrdinalIgnoreCase))
            {
                if (paths.ContainsKey(pair.Key))
                    continue;

                try
                {
#pragma warning disable 618
                    references.Add(MetadataReference.CreateFromAssembly(pair.Value));
#pragma warning restore 618
                }
                catch (Exception ex)
                {
                    Log.Warn("Could not use in-memory compiler reference " +
                        pair.Key + ": " + ex.Message);
                }
            }

            return references;
        }

        private static void RemoveOtherTerrariaAssemblies(
            IDictionary<string, string> paths,
            Assembly gameAssembly)
        {
            var targetName = gameAssembly.GetName().Name;
            var known = new[] { "Terraria", "TerrariaRelease", "TerrariaDebug", "TerrariaServer" };

            foreach (var name in known)
            {
                if (!string.Equals(name, targetName, StringComparison.OrdinalIgnoreCase))
                    paths.Remove(name);
            }
        }

        private static void AddManagedFiles(
            IDictionary<string, string> paths,
            string directory,
            bool overwrite)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return;

            foreach (var path in Directory.EnumerateFiles(directory, "*.dll", SearchOption.TopDirectoryOnly))
                AddManagedPath(paths, path, overwrite);

            foreach (var path in Directory.EnumerateFiles(directory, "*.exe", SearchOption.TopDirectoryOnly))
                AddManagedPath(paths, path, overwrite);
        }

        private static bool AddAssemblyLocation(
            IDictionary<string, string> paths,
            Assembly assembly,
            bool overwrite)
        {
            try
            {
                if (assembly == null || assembly.IsDynamic || string.IsNullOrWhiteSpace(assembly.Location))
                    return false;

                AddManagedPath(paths, assembly.Location, overwrite);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void AddInMemoryAssembly(
            IDictionary<string, Assembly> assemblies,
            Assembly assembly)
        {
            try
            {
                if (assembly == null || assembly.IsDynamic)
                    return;

                var name = assembly.GetName().Name;
                if (!string.IsNullOrWhiteSpace(name) && !assemblies.ContainsKey(name))
                    assemblies[name] = assembly;
            }
            catch
            {
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
            }
            catch (FileLoadException)
            {
            }
            catch (FileNotFoundException)
            {
            }
        }
    }
}
