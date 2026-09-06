#if GLOADER
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Terraria;
using Terraria.IO;

namespace AuthenticRaces.Core
{
    [HarmonyPatch]
    internal static class SavePlayerRacePersistenceHook
    {
        private static MethodBase TargetMethod()
        {
            return typeof(Player)
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Where(method => method.Name == "SavePlayer")
                .FirstOrDefault(method =>
                {
                    var parameters = method.GetParameters();
                    return parameters.Length >= 1 && parameters[0].ParameterType == typeof(PlayerFileData);
                })
                ?? throw new MissingMethodException(typeof(Player).FullName, "SavePlayer(PlayerFileData, ...)");
        }

        [HarmonyPostfix]
        private static void Postfix(object[] __args)
        {
            if (__args != null && __args.Length > 0 && __args[0] is PlayerFileData playerFile)
                RacePersistence.Save(playerFile);
        }
    }

    [HarmonyPatch]
    internal static class LoadPlayerRacePersistenceHook
    {
        private static MethodBase TargetMethod()
        {
            return typeof(Player)
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Where(method => method.Name == "LoadPlayer" && method.ReturnType == typeof(PlayerFileData))
                .FirstOrDefault(method =>
                {
                    var parameters = method.GetParameters();
                    return parameters.Length >= 2 &&
                        parameters[0].ParameterType == typeof(string) &&
                        parameters[1].ParameterType == typeof(bool);
                })
                ?? throw new MissingMethodException(typeof(Player).FullName, "LoadPlayer(String, Boolean)");
        }

        [HarmonyPostfix]
        private static void Postfix(object[] __args, PlayerFileData __result)
        {
            if (__args == null || __args.Length < 2)
                return;

            RacePersistence.Load(
                __result,
                __args[0] as string,
                __args[1] is bool cloudSave && cloudSave);
        }
    }

    [HarmonyPatch(typeof(PlayerFileData), "MoveToCloud")]
    internal static class MovePlayerRacePersistenceToCloudHook
    {
        [HarmonyPrefix]
        private static void Prefix(PlayerFileData __instance, out string __state)
        {
            __state = __instance?.Path;
        }

        [HarmonyPostfix]
        private static void Postfix(PlayerFileData __instance, string __state)
        {
            if (__instance == null || string.IsNullOrWhiteSpace(__state) || string.IsNullOrWhiteSpace(__instance.Path))
                return;

            if (!string.Equals(__state, __instance.Path, StringComparison.OrdinalIgnoreCase))
                RacePersistence.MoveToCloud(__state, __instance.Path);
        }
    }

    [HarmonyPatch(typeof(PlayerFileData), "MoveToLocal")]
    internal static class MovePlayerRacePersistenceToLocalHook
    {
        [HarmonyPrefix]
        private static void Prefix(PlayerFileData __instance, out string __state)
        {
            __state = __instance?.Path;
        }

        [HarmonyPostfix]
        private static void Postfix(PlayerFileData __instance, string __state)
        {
            if (__instance == null || string.IsNullOrWhiteSpace(__state) || string.IsNullOrWhiteSpace(__instance.Path))
                return;

            if (!string.Equals(__state, __instance.Path, StringComparison.OrdinalIgnoreCase))
                RacePersistence.MoveToLocal(__state, __instance.Path);
        }
    }

    [HarmonyPatch]
    internal static class ErasePlayerRacePersistenceHook
    {
        private struct EraseState
        {
            public string Path;
            public bool CloudSave;
        }

        private static MethodBase TargetMethod()
        {
            return typeof(Main)
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Where(method => method.Name == "ErasePlayer")
                .FirstOrDefault(method =>
                {
                    var parameters = method.GetParameters();
                    return parameters.Length == 1 && parameters[0].ParameterType == typeof(int);
                })
                ?? throw new MissingMethodException(typeof(Main).FullName, "ErasePlayer(Int32)");
        }

        [HarmonyPrefix]
        private static void Prefix(object[] __args, out EraseState __state)
        {
            __state = default;

            if (__args == null || __args.Length == 0 || !(__args[0] is int index))
                return;

            try
            {
                var field = AccessTools.Field(typeof(Main), "PlayerList");
                var list = field?.GetValue(null) as IList;
                if (list == null || index < 0 || index >= list.Count || !(list[index] is PlayerFileData playerFile))
                    return;

                __state.Path = playerFile.Path;
                __state.CloudSave = playerFile.IsCloudSave;
            }
            catch
            {
                // Vanilla deletion should proceed even if our optional sidecar metadata cannot be inspected.
            }
        }

        [HarmonyPostfix]
        private static void Postfix(EraseState __state)
        {
            if (!string.IsNullOrWhiteSpace(__state.Path))
                RacePersistence.Erase(__state.Path, __state.CloudSave);
        }
    }
}
#endif
