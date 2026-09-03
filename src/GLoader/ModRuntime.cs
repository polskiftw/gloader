using System;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace GLoader
{
    internal static class ModRuntime
    {
        private const string ModDirectoryDataKey = "GLoader.ModDirectory";
        private const string PatchPolicyTypeName = "PatchPolicy";

        public static void LoadAll(
            string modsDirectory,
            Assembly gameAssembly,
            string gameDirectory,
            string supportDirectory,
            bool isServerTarget)
        {
            var mods = ModDiscovery.Discover(modsDirectory);
            Log.Info("Discovered " + mods.Count + " source mod(s).");

            if (mods.Count == 0)
            {
                return;
            }

            var references = ReferenceCollector.Collect(
                gameAssembly,
                gameDirectory,
                supportDirectory);
            var compiledModCache = Path.Combine(supportDirectory, "cache", "compiled-mods");

            foreach (var mod in mods)
            {
                LoadOne(mod, references, isServerTarget, compiledModCache);
            }
        }

        private static void LoadOne(
            ModSource mod,
            System.Collections.Generic.IReadOnlyList<Microsoft.CodeAnalysis.MetadataReference> references,
            bool isServerTarget,
            string compiledModCache)
        {
            var harmonyId = "gloader.mod." + mod.Id;
            Harmony harmony = null;

            try
            {
                Log.Info("Compiling mod: " + mod.DisplayName);
                var assembly = ModCompiler.Compile(
                    mod,
                    references,
                    isServerTarget,
                    compiledModCache);

                InvokeOptionalLoad(assembly, GetModDirectory(mod));

                harmony = new Harmony(harmonyId);
                int optionalPatchFailures = ApplyHarmonyPatches(
                    harmony,
                    assembly,
                    mod.DisplayName);

                if (optionalPatchFailures == 0)
                {
                    Log.Info("Loaded mod: " + mod.DisplayName);
                }
                else
                {
                    Log.Warn(
                        "Loaded mod: " + mod.DisplayName + " with " + optionalPatchFailures +
                        " optional Harmony patch class failure(s). See the warnings above for exact patch names.");
                }
            }
            catch (Exception ex)
            {
                try
                {
                    harmony?.UnpatchAll(harmonyId);
                }
                catch (Exception cleanupEx)
                {
                    Log.Warn("Patch cleanup failed for " + mod.DisplayName + ": " + cleanupEx.Message);
                }

                Log.Error("Mod failed: " + mod.DisplayName + Environment.NewLine + Unwrap(ex));
            }
        }

        private static int ApplyHarmonyPatches(
            Harmony harmony,
            Assembly assembly,
            string modDisplayName)
        {
            var shouldPatchMethod = FindPatchPolicyMethod(assembly, "ShouldPatch");
            var isOptionalMethod = FindPatchPolicyMethod(assembly, "IsOptional");

            // Keep the original all-or-nothing behavior for ordinary source mods.
            // A mod must explicitly provide PatchPolicy hooks before GLoader will
            // isolate individual Harmony classes.
            if (shouldPatchMethod == null && isOptionalMethod == null)
            {
                harmony.PatchAll(assembly);
                return 0;
            }

            var patchTypes = assembly
                .GetTypes()
                .Where(type => type.GetCustomAttributes(typeof(HarmonyPatch), inherit: false).Length != 0)
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
                .ToArray();

            int optionalFailures = 0;

            foreach (var patchType in patchTypes)
            {
                if (!InvokePatchPolicy(shouldPatchMethod, patchType, defaultValue: true))
                {
                    Log.Info(
                        "Patch policy skipped " + modDisplayName + ": " +
                        (patchType.FullName ?? patchType.Name));
                    continue;
                }

                var processor = harmony.CreateClassProcessor(patchType);
                try
                {
                    processor.Patch();
                }
                catch (Exception ex)
                {
                    bool optional = InvokePatchPolicy(
                        isOptionalMethod,
                        patchType,
                        defaultValue: false);

                    if (!optional)
                    {
                        throw new InvalidOperationException(
                            "Required Harmony patch class failed for " + modDisplayName + ": " +
                            (patchType.FullName ?? patchType.Name),
                            Unwrap(ex));
                    }

                    optionalFailures++;

                    try
                    {
                        processor.Unpatch();
                    }
                    catch (Exception cleanupEx)
                    {
                        Log.Warn(
                            "Optional patch cleanup also failed for " + modDisplayName + ": " +
                            (patchType.FullName ?? patchType.Name) + Environment.NewLine +
                            Unwrap(cleanupEx));
                    }

                    Log.Warn(
                        "Optional Harmony patch skipped for " + modDisplayName + ": " +
                        (patchType.FullName ?? patchType.Name) + Environment.NewLine +
                        Unwrap(ex));
                }
            }

            return optionalFailures;
        }

        private static MethodInfo FindPatchPolicyMethod(Assembly assembly, string methodName)
        {
            var candidates = assembly
                .GetTypes()
                .Where(type => string.Equals(type.Name, PatchPolicyTypeName, StringComparison.Ordinal))
                .Select(type => type.GetMethod(
                    methodName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                    binder: null,
                    types: new[] { typeof(Type) },
                    modifiers: null))
                .Where(method => method != null)
                .OrderBy(method => method.DeclaringType?.FullName, StringComparer.Ordinal)
                .ToArray();

            if (candidates.Length == 0)
                return null;

            if (candidates.Length > 1)
            {
                throw new AmbiguousMatchException(
                    "A source mod may contain at most one PatchPolicy." + methodName + "(Type) method.");
            }

            if (candidates[0].ReturnType != typeof(bool))
            {
                throw new InvalidOperationException(
                    "PatchPolicy." + methodName + "(Type) must return bool.");
            }

            return candidates[0];
        }

        private static bool InvokePatchPolicy(
            MethodInfo method,
            Type patchType,
            bool defaultValue)
        {
            if (method == null)
                return defaultValue;

            try
            {
                return (bool)method.Invoke(null, new object[] { patchType });
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Patch policy hook failed while evaluating " +
                    (patchType?.FullName ?? "<unknown patch type>") + ".",
                    Unwrap(ex));
            }
        }

        private static string GetModDirectory(ModSource mod)
        {
            if (mod == null || mod.SourceFiles == null || mod.SourceFiles.Count == 0)
                return null;

            return Path.GetDirectoryName(mod.SourceFiles[0]);
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
                        binder: null,
                        types: Type.EmptyTypes,
                        modifiers: null)
                })
                .Where(candidate => candidate.Method != null)
                .OrderBy(candidate => candidate.Type.FullName, StringComparer.Ordinal)
                .ToArray();

            if (candidates.Length == 0)
            {
                return;
            }

            if (candidates.Length > 1)
            {
                throw new AmbiguousMatchException(
                    "A source mod may contain at most one class named Mod with a static parameterless Load() method.");
            }

            var previousModDirectory = AppDomain.CurrentDomain.GetData(ModDirectoryDataKey);
            try
            {
                AppDomain.CurrentDomain.SetData(ModDirectoryDataKey, modDirectory);
                candidates[0].Method.Invoke(null, null);
            }
            finally
            {
                AppDomain.CurrentDomain.SetData(ModDirectoryDataKey, previousModDirectory);
            }
        }

        private static Exception Unwrap(Exception exception)
        {
            while (exception is TargetInvocationException invocation &&
                   invocation.InnerException != null)
            {
                exception = invocation.InnerException;
            }

            return exception;
        }
    }
}
