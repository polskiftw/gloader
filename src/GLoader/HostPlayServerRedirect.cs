using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace GLoader
{
    internal static class HostPlayServerRedirect
    {
        private const string HarmonyId = "gloader.core.hostplay-linux";
        private static string _loaderPath;
        private static string _root;
        private static bool _installed;

        public static void TryInstall(Assembly gameAssembly, string loaderPath, string root)
        {
            if (_installed)
                return;

            try
            {
                _loaderPath = Path.GetFullPath(loaderPath);
                _root = Path.GetFullPath(root);

                var launcher = FindServerLauncher(gameAssembly);
                if (launcher == null)
                {
                    Log.Warn("Host & Play server launcher was not found; automatic server redirection is unavailable for this Terraria build.");
                    return;
                }

                var transpiler = typeof(HostPlayServerRedirect).GetMethod(
                    "ServerLaunchTranspiler",
                    BindingFlags.NonPublic | BindingFlags.Static);

                new Harmony(HarmonyId).Patch(
                    launcher,
                    transpiler: new HarmonyMethod(transpiler));

                _installed = true;
                Log.Info("Host & Play server redirection enabled.");
            }
            catch (Exception ex)
            {
                Log.Warn("Host & Play redirection could not be installed: " + ex.Message);
            }
        }

        private static MethodInfo FindServerLauncher(Assembly gameAssembly)
        {
            var mainType = gameAssembly.GetType("Terraria.Main", false);
            if (mainType == null)
                return null;

            return mainType
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
                .FirstOrDefault(MethodMentionsTerrariaServer);
        }

        private static bool MethodMentionsTerrariaServer(MethodInfo method)
        {
            MethodBody body;
            try
            {
                body = method.GetMethodBody();
            }
            catch
            {
                return false;
            }

            var il = body == null ? null : body.GetILAsByteArray();
            if (il == null || il.Length < 5)
                return false;

            for (var index = 0; index <= il.Length - 5; index++)
            {
                if (il[index] != 0x72)
                    continue;

                try
                {
                    var token = BitConverter.ToInt32(il, index + 1);
                    var value = method.Module.ResolveString(token);
                    if (value != null && value.IndexOf("TerrariaServer", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
                catch
                {
                }

                index += 4;
            }

            return false;
        }

        private static IEnumerable<CodeInstruction> ServerLaunchTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var argumentsSetter = typeof(ProcessStartInfo)
                .GetProperty("Arguments")
                .GetSetMethod();

            var argumentsRedirect = typeof(HostPlayServerRedirect).GetMethod(
                "SetArgumentsAndRedirect",
                BindingFlags.NonPublic | BindingFlags.Static);

            var instanceStart = typeof(Process).GetMethod(
                "Start",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                Type.EmptyTypes,
                null);

            var staticStart = typeof(Process).GetMethod(
                "Start",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(ProcessStartInfo) },
                null);

            var instanceRedirect = typeof(HostPlayServerRedirect).GetMethod(
                "StartAndRedirect",
                BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new[] { typeof(Process) },
                null);

            var staticRedirect = typeof(HostPlayServerRedirect).GetMethod(
                "StartAndRedirect",
                BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new[] { typeof(ProcessStartInfo) },
                null);

            foreach (var instruction in instructions)
            {
                if (argumentsSetter != null && instruction.Calls(argumentsSetter))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = argumentsRedirect;
                }
                else if (instanceStart != null && instruction.Calls(instanceStart))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = instanceRedirect;
                }
                else if (staticStart != null && instruction.Calls(staticStart))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = staticRedirect;
                }

                yield return instruction;
            }
        }

        private static void SetArgumentsAndRedirect(ProcessStartInfo startInfo, string arguments)
        {
            startInfo.Arguments = arguments ?? string.Empty;
            RedirectIfTerrariaServer(startInfo);
        }

        private static bool StartAndRedirect(Process process)
        {
            RedirectIfTerrariaServer(process.StartInfo);
            return process.Start();
        }

        private static Process StartAndRedirect(ProcessStartInfo startInfo)
        {
            RedirectIfTerrariaServer(startInfo);
            return Process.Start(startInfo);
        }

        private static void RedirectIfTerrariaServer(ProcessStartInfo startInfo)
        {
            if (startInfo == null || string.IsNullOrWhiteSpace(startInfo.FileName))
                return;

            var requested = startInfo.FileName.Trim().Trim('"');
            var fileName = Path.GetFileName(requested);

            if (string.IsNullOrWhiteSpace(fileName) ||
                !fileName.StartsWith("TerrariaServer", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var originalArguments = startInfo.Arguments ?? string.Empty;
            startInfo.FileName = _loaderPath;
            startInfo.WorkingDirectory = _root;
            startInfo.Arguments = "--server --" +
                (string.IsNullOrWhiteSpace(originalArguments) ? string.Empty : " " + originalArguments);

            Log.Info("Routing Host & Play dedicated server through Linux gloader.");
        }
    }
}
