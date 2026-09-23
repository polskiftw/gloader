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

                var instanceStart = typeof(Process).GetMethod(
                    "Start",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    Type.EmptyTypes,
                    null);

                if (instanceStart == null)
                {
                    Log.Warn("Host & Play Process.Start() hook was not found on this Mono runtime.");
                    return;
                }

                var harmony = new Harmony(HarmonyId);
                harmony.Patch(
                    instanceStart,
                    prefix: new HarmonyMethod(
                        typeof(HostPlayServerRedirect),
                        "BeforeInstanceStart"));

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
            if (__instance == null)
                return;

            var startInfo = __instance.StartInfo;
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
