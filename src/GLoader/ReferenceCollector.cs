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
            string modDirectory,
            AssemblyResolver resolver)
        {
            var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                AddAssemblyLocation(paths, assembly, false);

            AddManagedFiles(paths, root, false);
            AddManagedFiles(paths, dependencies, false);
            AddManagedFiles(paths, modDirectory, false);

            RemoveOtherTerrariaAssemblies(paths, gameAssembly);
            AddAssemblyLocation(paths, gameAssembly, true);

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

            foreach (var embedded in resolver
                .GetEmbeddedAssemblyImages()
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                var duplicatesDiskAssembly = paths.Keys.Any(name =>
                    embedded.Key.EndsWith(
                        "." + name + ".dll",
                        StringComparison.OrdinalIgnoreCase));

                if (duplicatesDiskAssembly)
                    continue;

                try
                {
                    references.Add(MetadataReference.CreateFromImage(embedded.Value));
                }
                catch (BadImageFormatException)
                {
                }
                catch (ArgumentException ex)
                {
                    Log.Warn("Could not use embedded compiler reference " +
                        embedded.Key + ": " + ex.Message);
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

        private static void AddAssemblyLocation(
            IDictionary<string, string> paths,
            Assembly assembly,
            bool overwrite)
        {
            try
            {
                if (assembly == null || assembly.IsDynamic || string.IsNullOrWhiteSpace(assembly.Location))
                    return;

                AddManagedPath(paths, assembly.Location, overwrite);
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
