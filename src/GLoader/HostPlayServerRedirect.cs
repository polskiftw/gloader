using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using HarmonyLib;

namespace GLoader
{
    internal static class HostPlayServerRedirect
    {
        private const string HarmonyId = "gloader.core.hostplay-linux";
        private static string _loaderPath;
        private static string _root;
        private static bool _installed;

        public static void TryInstall(string loaderPath, string root)
        {
            if (_installed)
                return;

            try
            {
                _loaderPath = Path.GetFullPath(loaderPath);
                _root = Path.GetFullPath(root);

                var harmony = new Harmony(HarmonyId);
                var patched = 0;

                var instanceStart = typeof(Process).GetMethod(
                    "Start",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    Type.EmptyTypes,
                    null);

                if (instanceStart != null)
                {
                    harmony.Patch(
                        instanceStart,
                        prefix: new HarmonyMethod(
                            typeof(HostPlayServerRedirect),
                            "BeforeInstanceStart"));
                    patched++;
                }

                var staticStartInfo = typeof(Process).GetMethod(
                    "Start",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(ProcessStartInfo) },
                    null);

                if (staticStartInfo != null)
                {
                    harmony.Patch(
                        staticStartInfo,
                        prefix: new HarmonyMethod(
                            typeof(HostPlayServerRedirect),
                            "BeforeStaticStartInfo"));
                    patched++;
                }

                var staticFile = typeof(Process).GetMethod(
                    "Start",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(string) },
                    null);

                if (staticFile != null)
                {
                    harmony.Patch(
                        staticFile,
                        prefix: new HarmonyMethod(
                            typeof(HostPlayServerRedirect),
                            "BeforeStaticFile"));
                    patched++;
                }

                var staticFileArgs = typeof(Process).GetMethod(
                    "Start",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(string), typeof(string) },
                    null);

                if (staticFileArgs != null)
                {
                    harmony.Patch(
                        staticFileArgs,
                        prefix: new HarmonyMethod(
                            typeof(HostPlayServerRedirect),
                            "BeforeStaticFileArgs"));
                    patched++;
                }

                if (patched == 0)
                {
                    Log.Warn("Host & Play process hooks were not found on this Mono runtime.");
                    return;
                }

                _installed = true;
                Log.Info("Host & Play server redirection enabled.");
            }
            catch (Exception ex)
            {
                Log.Warn("Host & Play redirection could not be installed: " + ex);
            }
        }

        private static void BeforeInstanceStart(Process __instance)
        {
            if (__instance != null)
                RedirectIfTerrariaServer(__instance.StartInfo);
        }

        private static void BeforeStaticStartInfo(ref ProcessStartInfo __0)
        {
            RedirectIfTerrariaServer(__0);
        }

        private static void BeforeStaticFile(ref string __0)
        {
            if (!IsTerrariaServer(__0))
                return;

            __0 = _loaderPath;
            Log.Info("Routing Host & Play dedicated server through Linux gloader.");
        }

        private static void BeforeStaticFileArgs(ref string __0, ref string __1)
        {
            if (!IsTerrariaServer(__0))
                return;

            __0 = _loaderPath;
            __1 = BuildArguments(__1);
            Log.Info("Routing Host & Play dedicated server through Linux gloader.");
        }

        private static void RedirectIfTerrariaServer(ProcessStartInfo startInfo)
        {
            if (startInfo == null || !IsTerrariaServer(startInfo.FileName))
                return;

            startInfo.FileName = _loaderPath;
            startInfo.WorkingDirectory = _root;
            startInfo.Arguments = BuildArguments(startInfo.Arguments);

            Log.Info("Routing Host & Play dedicated server through Linux gloader.");
        }

        private static bool IsTerrariaServer(string requested)
        {
            if (string.IsNullOrWhiteSpace(requested))
                return false;

            var fileName = Path.GetFileName(requested.Trim().Trim('"'));
            return !string.IsNullOrWhiteSpace(fileName) &&
                fileName.StartsWith("TerrariaServer", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildArguments(string originalArguments)
        {
            return "--server --" +
                (string.IsNullOrWhiteSpace(originalArguments)
                    ? string.Empty
                    : " " + originalArguments);
        }
    }
}
