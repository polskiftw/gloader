using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace GLoader
{
    internal static class ReferenceCollector
    {
        private const string NetStandardIdentity =
            "netstandard, Version=2.0.0.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51";

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
            // Harmony and NAudio. Those assemblies may not have been JIT-loaded yet when
            // reference collection runs, so scan the loader output directories explicitly.
            AddManagedFiles(paths, AppDomain.CurrentDomain.BaseDirectory, overwrite: false);
            AddManagedFiles(paths, supportDirectory, overwrite: false);
            AddManagedFiles(paths, gameDirectory, overwrite: false);

            // NAudio 2.x is a .NET Standard library even when consumed by our net48 host.
            // Roslyn therefore needs the netstandard 2.0 facade as an explicit metadata
            // reference. The CLR can resolve the facade at runtime, but it is commonly not
            // loaded yet when source mods are compiled, which otherwise produces CS0012 on
            // Stream/Object/IDisposable in Radio's MediaFoundationReader pipeline.
            AddNetStandardFacade(paths);

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

        private static void AddNetStandardFacade(IDictionary<string, string> paths)
        {
            if (paths.ContainsKey("netstandard"))
                return;

            try
            {
                AddAssemblyLocation(paths, Assembly.Load(NetStandardIdentity), overwrite: false);
            }
            catch (FileNotFoundException)
            {
                // Fall through to the known framework facade locations below.
            }
            catch (FileLoadException)
            {
                // Fall through to the known framework facade locations below.
            }
            catch (BadImageFormatException)
            {
                // Fall through to the known framework facade locations below.
            }

            if (paths.ContainsKey("netstandard"))
                return;

            foreach (var candidate in EnumerateNetStandardFacadeCandidates())
            {
                if (!File.Exists(candidate))
                    continue;

                AddManagedPath(paths, candidate, overwrite: false);
                if (paths.ContainsKey("netstandard"))
                    return;
            }

            Log.Warn(
                "Could not locate the .NET Standard 2.0 facade for source-mod compilation. " +
                "Mods using .NET Standard libraries such as NAudio may fail to compile.");
        }

        private static IEnumerable<string> EnumerateNetStandardFacadeCandidates()
        {
            var windowsDirectory = Environment.GetEnvironmentVariable("WINDIR");
            if (!string.IsNullOrWhiteSpace(windowsDirectory))
            {
                yield return Path.Combine(
                    windowsDirectory,
                    "Microsoft.NET",
                    "Framework",
                    "v4.0.30319",
                    "Facades",
                    "netstandard.dll");
            }

            var programFilesX86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
            if (string.IsNullOrWhiteSpace(programFilesX86))
                programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            if (string.IsNullOrWhiteSpace(programFilesX86))
                yield break;

            var frameworkRoot = Path.Combine(
                programFilesX86,
                "Reference Assemblies",
                "Microsoft",
                "Framework",
                ".NETFramework");

            if (!Directory.Exists(frameworkRoot))
                yield break;

            IEnumerable<string> versionDirectories;
            try
            {
                versionDirectories = Directory
                    .EnumerateDirectories(frameworkRoot, "v4.*", SearchOption.TopDirectoryOnly)
                    .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (IOException)
            {
                yield break;
            }
            catch (UnauthorizedAccessException)
            {
                yield break;
            }

            foreach (var versionDirectory in versionDirectories)
            {
                yield return Path.Combine(versionDirectory, "Facades", "netstandard.dll");
            }
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
