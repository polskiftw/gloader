using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace GLoader
{
    internal static class ModRuntime
    {
        private const string ModDirectoryDataKey = "GLoader.ModDirectory";

        public static void LoadAll(
            string modsDirectory,
            Assembly gameAssembly,
            string root,
            string dependencies,
            AssemblyResolver resolver,
            bool isServerTarget)
        {
            var mods = ModDiscovery.Discover(modsDirectory);
            Log.Info("Discovered " + mods.Count + " source mod(s).");

            foreach (var mod in mods)
            {
                LoadOne(mod, gameAssembly, root, dependencies, resolver, isServerTarget);
            }
        }

        private static void LoadOne(
            ModSource mod,
            Assembly gameAssembly,
            string root,
            string dependencies,
            AssemblyResolver resolver,
            bool isServerTarget)
        {
            var harmonyId = "gloader.mod." + mod.Id;
            Harmony harmony = null;

            try
            {
                resolver.AddDirectory(mod.Directory);

                var references = ReferenceCollector.Collect(
                    gameAssembly,
                    root,
                    dependencies,
                    mod.Directory);

                Log.Info("Compiling mod: " + mod.DisplayName);
                var assembly = ModCompiler.Compile(mod, references, isServerTarget);

                InvokeOptionalLoad(assembly, mod.Directory);

                harmony = new Harmony(harmonyId);
                harmony.PatchAll(assembly);

                Log.Info("Loaded mod: " + mod.DisplayName);
            }
            catch (Exception ex)
            {
                try
                {
                    if (harmony != null)
                        harmony.UnpatchAll(harmonyId);
                }
                catch (Exception cleanupEx)
                {
                    Log.Warn("Patch cleanup failed for " + mod.DisplayName + ": " + cleanupEx.Message);
                }

                Log.Error("Mod failed: " + mod.DisplayName + Environment.NewLine + Unwrap(ex));
            }
        }

        private static void InvokeOptionalLoad(Assembly assembly, string modDirectory)
        {
            var candidates = assembly
                .GetTypes()
                .Where(type => string.Equals(type.Name, "Mod", StringComparison.Ordinal))
                .Select(type => new
                {
                    Type = type,
                    Method = type.GetMethod(
                        "Load",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                        null,
                        Type.EmptyTypes,
                        null)
                })
                .Where(candidate => candidate.Method != null)
                .OrderBy(candidate => candidate.Type.FullName, StringComparer.Ordinal)
                .ToArray();

            if (candidates.Length == 0)
                return;

            if (candidates.Length > 1)
                throw new AmbiguousMatchException("A source mod may contain at most one class named Mod with a static parameterless Load() method.");

            var previous = AppDomain.CurrentDomain.GetData(ModDirectoryDataKey);
            try
            {
                AppDomain.CurrentDomain.SetData(ModDirectoryDataKey, modDirectory);
                candidates[0].Method.Invoke(null, null);
            }
            finally
            {
                AppDomain.CurrentDomain.SetData(ModDirectoryDataKey, previous);
            }
        }

        private static Exception Unwrap(Exception exception)
        {
            while (exception is TargetInvocationException &&
                   ((TargetInvocationException)exception).InnerException != null)
            {
                exception = ((TargetInvocationException)exception).InnerException;
            }

            return exception;
        }
    }
}
