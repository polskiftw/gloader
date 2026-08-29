#if GLOADER_SERVER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Terraria;

// Debug-only helper kept separate from Infinite Angler's real quest-completion path.
// A vanilla client simply talks to the Angler while a fishing rod is selected; the
// server verifies the character-name allowlist and gives that player the current
// quest fish through Terraria's own QuickSpawnItem path.
[HarmonyPatch]
internal static class InfiniteAnglerDebugQuestFishMessageBufferPatch
{
    private static MethodBase TargetMethod()
        => InfiniteAnglerDebugQuestFishRuntime.MessageBufferGetDataMethod;

    [HarmonyPrefix]
    private static void Prefix(MessageBuffer __instance, int __0, ref bool __state)
        => __state = InfiniteAnglerDebugQuestFishRuntime.IsTalkNpcPacket(__instance, __0);

    [HarmonyPostfix]
    private static void Postfix(MessageBuffer __instance, bool __state)
    {
        if (__state && __instance != null)
            InfiniteAnglerDebugQuestFishRuntime.HandleTalkNpc(__instance.whoAmI);
    }
}

internal static class InfiniteAnglerDebugQuestFishRuntime
{
    private const string ConfigFileName = "InfiniteAngler.ini";
    private const string EnabledConfigKey = "EnableDebugQuestFish";
    private const string PlayersConfigKey = "DebugQuestFishPlayers";
    private const string SpawnContext = "InfiniteAnglerDebugQuestFish";

    private static readonly HashSet<string> AllowedCharacterNames =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private static bool _initialized;
    private static bool _enabled;
    private static bool _failureLogged;
    private static int _syncTalkNpcMessageId = -1;
    private static int _anglerNpcId = -1;

    public static MethodBase MessageBufferGetDataMethod { get; } = ResolveMessageBufferGetDataMethod();

    public static bool IsTalkNpcPacket(MessageBuffer buffer, int start)
    {
        EnsureInitialized();

        if (!_enabled || buffer == null || buffer.readBuffer == null ||
            start < 0 || start >= buffer.readBuffer.Length)
        {
            return false;
        }

        return buffer.readBuffer[start] == _syncTalkNpcMessageId;
    }

    public static void HandleTalkNpc(int clientId)
    {
        if (!_enabled)
            return;

        try
        {
            if (ReadStaticInt(typeof(Main), "netMode") != 2)
                return;

            var players = ReadStaticArray(typeof(Main), "player");
            if (players == null || clientId < 0 || clientId >= players.Length)
                return;

            var player = players.GetValue(clientId);
            if (player == null)
                return;

            var playerName = ReadStringMember(player, "name");
            if (string.IsNullOrEmpty(playerName) || !AllowedCharacterNames.Contains(playerName))
                return;

            var finishedToday = ReadStaticMember(typeof(Main), "anglerWhoFinishedToday") as IList<string>;
            if (finishedToday != null && finishedToday.Contains(playerName))
                return;

            var talkNpc = ReadIntMember(player, "talkNPC");
            if (talkNpc < 0)
                return;

            var npcs = ReadStaticArray(typeof(Main), "npc");
            if (npcs == null || talkNpc >= npcs.Length)
                return;

            var npc = npcs.GetValue(talkNpc);
            if (npc == null || ReadIntMember(npc, "type") != _anglerNpcId)
                return;

            var inventory = ReadArrayMember(player, "inventory");
            var selectedItem = ReadIntMember(player, "selectedItem");
            if (inventory == null || selectedItem < 0 || selectedItem >= inventory.Length)
                return;

            var heldItem = inventory.GetValue(selectedItem);
            if (heldItem == null || ReadIntMember(heldItem, "fishingPole") <= 0)
                return;

            var questIndex = ReadStaticInt(typeof(Main), "anglerQuest");
            var questItems = ReadStaticArray(typeof(Main), "anglerQuestItemNetIDs");
            if (questItems == null || questIndex < 0 || questIndex >= questItems.Length)
                return;

            var questItemType = Convert.ToInt32(questItems.GetValue(questIndex));
            if (questItemType <= 0 || PlayerHasItem(player, questItemType, inventory))
                return;

            QuickSpawnQuestFish(player, questItemType);
            _failureLogged = false;
            Console.WriteLine(
                "[Infinite Angler] Debug quest fish granted to '" + playerName +
                "' (item " + questItemType + ").");
        }
        catch (Exception ex)
        {
            if (_failureLogged)
                return;

            _failureLogged = true;
            Console.Error.WriteLine(
                "[Infinite Angler] Debug quest-fish helper failed: " + Unwrap(ex));
        }
    }

    private static void EnsureInitialized()
    {
        if (_initialized)
            return;

        _initialized = true;
        LoadConfig();

        if (!_enabled)
            return;

        var gameAssembly = typeof(Main).Assembly;
        var messageIdType = gameAssembly.GetType("Terraria.ID.MessageID", throwOnError: true);
        var npcIdType = gameAssembly.GetType("Terraria.ID.NPCID", throwOnError: true);

        _syncTalkNpcMessageId = ReadConstantInt(messageIdType, "SyncTalkNPC");
        _anglerNpcId = ReadConstantInt(npcIdType, "Angler");

        Console.WriteLine(
            "[Infinite Angler] Debug quest-fish helper enabled for " +
            AllowedCharacterNames.Count + " allowlisted character(s).");
    }

    private static void LoadConfig()
    {
        _enabled = false;
        AllowedCharacterNames.Clear();

        var configPath = Path.Combine(ResolveModDirectory(), ConfigFileName);
        if (!File.Exists(configPath))
            return;

        foreach (var rawLine in File.ReadAllLines(configPath))
        {
            var line = (rawLine ?? string.Empty).Trim();
            if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";"))
                continue;

            var equals = line.IndexOf('=');
            if (equals <= 0)
                continue;

            var key = line.Substring(0, equals).Trim();
            var value = line.Substring(equals + 1).Trim();

            if (key.Equals(EnabledConfigKey, StringComparison.OrdinalIgnoreCase))
            {
                bool enabled;
                if (bool.TryParse(value, out enabled))
                {
                    _enabled = enabled;
                }
                else
                {
                    Console.Error.WriteLine(
                        "[Infinite Angler] Invalid " + EnabledConfigKey + " value '" + value +
                        "'; expected true or false. Debug quest-fish helper remains disabled.");
                    _enabled = false;
                }

                continue;
            }

            if (!key.Equals(PlayersConfigKey, StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var rawName in value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var name = rawName.Trim();
                if (name.Length > 0)
                    AllowedCharacterNames.Add(name);
            }
        }
    }

    private static string ResolveModDirectory()
    {
        // GLoader exposes this key only while Mod.Load() is running. Keep support for
        // it in case initialization ever moves there, then reproduce GLoader's --mods
        // resolution for the current lazy-on-first-packet initialization path.
        var current = AppDomain.CurrentDomain.GetData("GLoader.ModDirectory") as string;
        if (!string.IsNullOrWhiteSpace(current))
            return current;

        var args = Environment.GetCommandLineArgs();
        for (var index = 1; index + 1 < args.Length; index++)
        {
            if (!string.Equals(args[index], "--mods", StringComparison.OrdinalIgnoreCase))
                continue;

            var modsDirectory = (args[index + 1] ?? string.Empty).Trim().Trim('"');
            if (!string.IsNullOrWhiteSpace(modsDirectory))
                return Path.Combine(modsDirectory, "InfiniteAngler");
        }

        return Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "gmods",
            "InfiniteAngler");
    }

    private static bool PlayerHasItem(object player, int itemType, Array inventory)
    {
        var hasItem = player.GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .SingleOrDefault(method =>
            {
                if (method.Name != "HasItem" || method.ReturnType != typeof(bool))
                    return false;

                var parameters = method.GetParameters();
                return parameters.Length == 1 && parameters[0].ParameterType == typeof(int);
            });

        if (hasItem != null)
            return (bool)hasItem.Invoke(player, new object[] { itemType });

        for (var index = 0; index < inventory.Length; index++)
        {
            var item = inventory.GetValue(index);
            if (item == null)
                continue;

            if (ReadIntMember(item, "type") == itemType && ReadIntMember(item, "stack") > 0)
                return true;
        }

        return false;
    }

    private static void QuickSpawnQuestFish(object player, int itemType)
    {
        var playerType = player.GetType();
        var methods = playerType
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(method => method.Name == "QuickSpawnItem")
            .ToArray();

        var modern = methods.SingleOrDefault(method =>
        {
            var parameters = method.GetParameters();
            return parameters.Length == 3 &&
                   parameters[1].ParameterType == typeof(int) &&
                   parameters[2].ParameterType == typeof(int);
        });

        if (modern != null)
        {
            var source = CreateGiftSource(player, modern.GetParameters()[0].ParameterType);
            modern.Invoke(player, new object[] { source, itemType, 1 });
            return;
        }

        var legacy = methods.SingleOrDefault(method =>
        {
            var parameters = method.GetParameters();
            return parameters.Length == 2 &&
                   parameters[0].ParameterType == typeof(int) &&
                   parameters[1].ParameterType == typeof(int);
        });

        if (legacy != null)
        {
            legacy.Invoke(player, new object[] { itemType, 1 });
            return;
        }

        throw new MissingMethodException(playerType.FullName, "QuickSpawnItem");
    }

    private static object CreateGiftSource(object player, Type expectedSourceType)
    {
        var playerType = player.GetType();
        var sourceMethod = playerType
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(method => SourceMethodMatches(method, "GetSource_GiftOrReward", expectedSourceType))
            ?? playerType
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(method => SourceMethodMatches(method, "GetSource_Misc", expectedSourceType));

        if (sourceMethod == null)
            return null;

        var parameters = sourceMethod.GetParameters();
        return sourceMethod.Invoke(
            player,
            parameters.Length == 0 ? null : new object[] { SpawnContext });
    }

    private static bool SourceMethodMatches(MethodInfo method, string name, Type expectedSourceType)
    {
        if (method.Name != name || !expectedSourceType.IsAssignableFrom(method.ReturnType))
            return false;

        var parameters = method.GetParameters();
        return parameters.Length == 0 ||
               (parameters.Length == 1 && parameters[0].ParameterType == typeof(string));
    }

    private static MethodBase ResolveMessageBufferGetDataMethod()
    {
        return typeof(MessageBuffer)
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .SingleOrDefault(method =>
            {
                if (method.Name != "GetData" || method.ReturnType != typeof(void))
                    return false;

                var parameters = method.GetParameters();
                return parameters.Length == 3 &&
                       parameters[0].ParameterType == typeof(int) &&
                       parameters[1].ParameterType == typeof(int) &&
                       parameters[2].ParameterType == typeof(int).MakeByRefType();
            })
            ?? throw new MissingMethodException(typeof(MessageBuffer).FullName, "GetData(int, int, out int)");
    }

    private static object ReadStaticMember(Type type, string name)
    {
        var field = AccessTools.Field(type, name);
        if (field != null)
            return field.GetValue(null);

        var property = AccessTools.Property(type, name);
        if (property != null)
            return property.GetValue(null, null);

        throw new MissingMemberException(type.FullName, name);
    }

    private static int ReadStaticInt(Type type, string name)
        => Convert.ToInt32(ReadStaticMember(type, name));

    private static Array ReadStaticArray(Type type, string name)
        => ReadStaticMember(type, name) as Array;

    private static object ReadMember(object instance, string name)
    {
        var type = instance.GetType();
        var field = AccessTools.Field(type, name);
        if (field != null)
            return field.GetValue(instance);

        var property = AccessTools.Property(type, name);
        if (property != null)
            return property.GetValue(instance, null);

        throw new MissingMemberException(type.FullName, name);
    }

    private static int ReadIntMember(object instance, string name)
        => Convert.ToInt32(ReadMember(instance, name));

    private static string ReadStringMember(object instance, string name)
        => ReadMember(instance, name) as string;

    private static Array ReadArrayMember(object instance, string name)
        => ReadMember(instance, name) as Array;

    private static int ReadConstantInt(Type type, string name)
    {
        var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    ?? throw new MissingFieldException(type.FullName, name);
        var value = field.IsLiteral ? field.GetRawConstantValue() : field.GetValue(null);
        return Convert.ToInt32(value);
    }

    private static Exception Unwrap(Exception exception)
    {
        while (exception is TargetInvocationException invocation && invocation.InnerException != null)
            exception = invocation.InnerException;

        return exception;
    }
}
#endif
