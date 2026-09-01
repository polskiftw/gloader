#if GLOADER_CLIENT
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Terraria;
using Terraria.IO;

public static class Mod
{
    public static void Load()
    {
        HardcoreSaveProtectionRuntime.ValidateTargets();
        Console.WriteLine("[Hardcore Save Protection] Hardcore death can no longer delete or overwrite the player save.");
    }
}

// Arms protection when vanilla enters the local player's Hardcore death penalty.
// Protection intentionally survives the penalty method itself: vanilla Hardcore can
// leave the dead player/ghost alive in memory for a while, so a later Save & Quit
// must not overwrite the last good on-disk player with that post-death state.
[HarmonyPatch]
internal static class HardcoreDeathPenaltyPatch
{
    private static MethodBase TargetMethod() => HardcoreSaveProtectionRuntime.HardcoreDeathPenaltyMethod;

    [HarmonyPrefix]
    private static void Prefix(Player __instance)
    {
        HardcoreSaveProtectionRuntime.Arm(__instance);
    }
}

// This is Terraria's normal player-erasure path. Suppress it only while the file
// belongs to the Hardcore character whose death armed protection. Character-select
// deletion remains completely normal once the character list is opened again.
[HarmonyPatch]
internal static class HardcoreErasePlayerPatch
{
    private static MethodBase TargetMethod() => HardcoreSaveProtectionRuntime.ErasePlayerMethod;

    [HarmonyPrefix]
    private static bool Prefix(int __0)
    {
        return !HardcoreSaveProtectionRuntime.ShouldBlockErase(__0);
    }
}

// Do not merely stop deletion. A post-death Player object can contain ghost/death
// state, dropped inventory, or other mutations. Until we are safely back at character
// select, the pre-death .plr is treated as read-only and cannot be overwritten.
[HarmonyPatch]
internal static class HardcoreSavePlayerPatch
{
    private static MethodBase TargetMethod() => HardcoreSaveProtectionRuntime.SavePlayerMethod;

    [HarmonyPrefix]
    private static bool Prefix(PlayerFileData __0)
    {
        return !HardcoreSaveProtectionRuntime.ShouldBlockSave(__0);
    }
}

// Opening character select is the clean boundary at which the protected file is no
// longer associated with a live dead/ghost Player instance. Clear protection here so
// normal saves, the difficulty cycler, and intentional Delete all work normally.
[HarmonyPatch]
internal static class HardcoreCharacterSelectPatch
{
    private static MethodBase TargetMethod() => HardcoreSaveProtectionRuntime.OpenCharacterSelectMethod;

    [HarmonyPrefix]
    private static void Prefix()
    {
        HardcoreSaveProtectionRuntime.ClearAtCharacterSelect();
    }
}

internal static class HardcoreSaveProtectionRuntime
{
    private static readonly BindingFlags AnyStatic =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

    private static readonly BindingFlags AnyInstance =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    internal static readonly MethodInfo HardcoreDeathPenaltyMethod = ResolveHardcoreDeathPenalty();
    internal static readonly MethodInfo ErasePlayerMethod = ResolveErasePlayer();
    internal static readonly MethodInfo SavePlayerMethod = ResolveSavePlayer();
    internal static readonly MethodInfo OpenCharacterSelectMethod = ResolveOpenCharacterSelect();

    private static readonly FieldInfo PlayerListField =
        AccessTools.Field(typeof(Main), "PlayerList")
        ?? throw new MissingFieldException(typeof(Main).FullName, "PlayerList");

    private static bool _active;
    private static string _protectedPath;
    private static bool _protectedCloudSave;
    private static string _protectedName;

    internal static void ValidateTargets()
    {
        // Merely touching the resolved members above makes version drift fail loudly
        // during mod load instead of silently pretending the save is protected.
        _ = HardcoreDeathPenaltyMethod;
        _ = ErasePlayerMethod;
        _ = SavePlayerMethod;
        _ = OpenCharacterSelectMethod;
        _ = PlayerListField;
    }

    internal static void Arm(Player player)
    {
        if (player == null || player.whoAmI != Main.myPlayer || player.difficulty != 2)
            return;

        var data = Main.ActivePlayerFileData;
        if (data == null || string.IsNullOrEmpty(data.Path))
        {
            throw new InvalidOperationException(
                "Hardcore death began, but Main.ActivePlayerFileData was unavailable. " +
                "Refusing to continue without a concrete save path to protect.");
        }

        _active = true;
        _protectedPath = data.Path;
        _protectedCloudSave = data.IsCloudSave;
        _protectedName = player.name ?? "<unnamed>";

        Console.WriteLine(
            "[Hardcore Save Protection] Armed for '" + _protectedName +
            "'. The existing player save is now read-only until character select.");
    }

    internal static bool ShouldBlockErase(int playerListIndex)
    {
        if (!_active)
            return false;

        var list = PlayerListField.GetValue(null) as IList;
        if (list == null || playerListIndex < 0 || playerListIndex >= list.Count)
        {
            // We know we are inside a protected Hardcore-death session but cannot
            // identify the erase target safely. Fail closed: preserving an unrelated
            // character is preferable to allowing the protected save to be destroyed.
            Console.WriteLine(
                "[Hardcore Save Protection] Blocked player erase while protection was active " +
                "because the erase target could not be resolved safely.");
            return true;
        }

        var data = list[playerListIndex] as PlayerFileData;
        if (!MatchesProtectedFile(data))
            return false;

        Console.WriteLine(
            "[Hardcore Save Protection] Suppressed vanilla Hardcore deletion for '" +
            _protectedName + "'.");
        return true;
    }

    internal static bool ShouldBlockSave(PlayerFileData data)
    {
        if (!_active || !MatchesProtectedFile(data))
            return false;

        Console.WriteLine(
            "[Hardcore Save Protection] Suppressed post-death player save for '" +
            _protectedName + "'.");
        return true;
    }

    internal static void ClearAtCharacterSelect()
    {
        if (!_active)
            return;

        Console.WriteLine(
            "[Hardcore Save Protection] Released protection for '" + _protectedName +
            "' at character select. The preserved save can be used normally again.");

        _active = false;
        _protectedPath = null;
        _protectedCloudSave = false;
        _protectedName = null;
    }

    private static bool MatchesProtectedFile(PlayerFileData data)
    {
        if (data == null || string.IsNullOrEmpty(data.Path) || string.IsNullOrEmpty(_protectedPath))
            return false;

        return data.IsCloudSave == _protectedCloudSave &&
               string.Equals(data.Path, _protectedPath, StringComparison.OrdinalIgnoreCase);
    }

    private static MethodInfo ResolveHardcoreDeathPenalty()
    {
        var matches = typeof(Player)
            .GetMethods(AnyInstance)
            .Where(method =>
                method.Name == "HardcoreDeathPenalty" &&
                method.ReturnType == typeof(void) &&
                method.GetParameters().Length == 0)
            .ToArray();

        if (matches.Length != 1)
            throw new MissingMethodException(
                "Expected exactly one Player.HardcoreDeathPenalty() method, found " + matches.Length + ".");

        return matches[0];
    }

    private static MethodInfo ResolveErasePlayer()
    {
        var matches = typeof(Main)
            .GetMethods(AnyStatic)
            .Where(method =>
            {
                if (method.Name != "ErasePlayer" || method.ReturnType != typeof(void))
                    return false;

                var parameters = method.GetParameters();
                return parameters.Length == 1 && parameters[0].ParameterType == typeof(int);
            })
            .ToArray();

        if (matches.Length != 1)
            throw new MissingMethodException(
                "Expected exactly one Main.ErasePlayer(int) method, found " + matches.Length + ".");

        return matches[0];
    }

    private static MethodInfo ResolveSavePlayer()
    {
        var matches = typeof(Player)
            .GetMethods(AnyStatic)
            .Where(method =>
            {
                if (method.Name != "SavePlayer" || method.ReturnType != typeof(void))
                    return false;

                var parameters = method.GetParameters();
                return parameters.Length == 2 &&
                       parameters[0].ParameterType == typeof(PlayerFileData) &&
                       parameters[1].ParameterType == typeof(bool);
            })
            .ToArray();

        if (matches.Length != 1)
            throw new MissingMethodException(
                "Expected exactly one Player.SavePlayer(PlayerFileData, bool) method, found " +
                matches.Length + ".");

        return matches[0];
    }

    private static MethodInfo ResolveOpenCharacterSelect()
    {
        var matches = typeof(Main)
            .GetMethods(AnyStatic)
            .Where(method =>
                method.Name == "OpenCharacterSelectUI" &&
                method.ReturnType == typeof(void) &&
                method.GetParameters().Length == 0)
            .ToArray();

        if (matches.Length != 1)
            throw new MissingMethodException(
                "Expected exactly one Main.OpenCharacterSelectUI() method, found " + matches.Length + ".");

        return matches[0];
    }
}
#endif
