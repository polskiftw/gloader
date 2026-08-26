#if GLOADER_SERVER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Chat;
using Terraria.Localization;

public static class Mod
{
    public static void Load()
    {
        var modDirectory = AppDomain.CurrentDomain.GetData("GLoader.ModDirectory") as string;
        InfiniteAnglerRuntime.Initialize(modDirectory);
        Console.WriteLine(
            "[Infinite Angler] Slot-authoritative shared Angler rounds enabled on server. Participation commands: " +
            (InfiniteAnglerRuntime.ParticipationCommandsEnabled ? "enabled" : "disabled") + ".");
    }
}

// Terraria 1.4.5.8 rolls the vanilla daily quest from
// Main.UpdateTime_StartDay(ref bool). Suppress exactly that AnglerQuestSwap call.
// The shared round below is the only thing allowed to advance the quest.
[HarmonyPatch]
internal static class InfiniteAnglerDawnPatch
{
    private static MethodBase TargetMethod() => InfiniteAnglerRuntime.UpdateTimeStartDayMethod;

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var list = instructions.ToList();
        var swap = InfiniteAnglerRuntime.AnglerQuestSwapMethod;
        var swapIndexes = list
            .Select((instruction, index) => new { instruction, index })
            .Where(entry => entry.instruction.Calls(swap))
            .Select(entry => entry.index)
            .ToArray();

        if (swapIndexes.Length != 1)
        {
            throw new InvalidOperationException(
                "Expected exactly one Main.AnglerQuestSwap() call in Main.UpdateTime_StartDay(ref bool), found " +
                swapIndexes.Length + ".");
        }

        list[swapIndexes[0]].opcode = OpCodes.Nop;
        list[swapIndexes[0]].operand = null;
        return list;
    }
}

// Packet 75 is the authoritative event that a particular network connection
// turned in the current Angler quest. Let vanilla record its name first, then mark
// that exact connection slot complete for our shared round.
[HarmonyPatch]
internal static class InfiniteAnglerCompletionPatch
{
    private static MethodBase TargetMethod() => InfiniteAnglerRuntime.MessageBufferGetDataMethod;

    [HarmonyPrefix]
    private static void Prefix(MessageBuffer __instance, int __0, ref bool __state)
    {
        __state = InfiniteAnglerRuntime.IsAnglerCompletionPacket(__instance, __0);
    }

    [HarmonyPostfix]
    private static void Postfix(MessageBuffer __instance, bool __state)
    {
        if (__state && __instance != null)
            InfiniteAnglerRuntime.HandleQuestCompletion(__instance.whoAmI);
    }
}

// Optional vanilla-client commands are intercepted at the same server-side chat
// processor Terraria uses for ordinary Say messages. Returning false suppresses the
// command from normal chat; non-!fish messages continue through untouched.
[HarmonyPatch]
internal static class InfiniteAnglerChatPatch
{
    [HarmonyPrepare]
    private static bool Prepare() => InfiniteAnglerRuntime.ParticipationCommandsEnabled;

    private static MethodBase TargetMethod() => InfiniteAnglerRuntime.ChatProcessIncomingMessageMethod;

    [HarmonyPrefix]
    private static bool Prefix(ChatMessage __0, int __1)
    {
        return !InfiniteAnglerRuntime.TryHandleChatCommand(__0, __1);
    }
}

// A tick check handles disconnects and reconnects. Disconnected slots are removed
// from both the completion set and the optional participation opt-out set.
[HarmonyPatch]
internal static class InfiniteAnglerRoundPatch
{
    private static MethodBase TargetMethod() => InfiniteAnglerRuntime.UpdateTimeMethod;

    [HarmonyPostfix]
    private static void Postfix() => InfiniteAnglerRuntime.TryAdvanceQuest();
}

internal static class InfiniteAnglerRuntime
{
    private const int FullyConnectedState = 10;
    private const string ConfigFileName = "InfiniteAngler.ini";
    private const string ParticipationConfigKey = "EnableParticipationCommands";

    // Completion and participation are deliberately separate. A player may opt out
    // of being required while still turning in the fish and becoming completed for
    // the current round. Opt-outs persist across quest swaps for that connection,
    // but are cleared when the slot disconnects.
    private static readonly HashSet<int> CompletedSlots = new HashSet<int>();
    private static readonly HashSet<int> OptedOutSlots = new HashSet<int>();

    private static FieldInfo _netMode;
    private static FieldInfo _players;
    private static FieldInfo _netplayClients;
    private static FieldInfo _clientState;
    private static FieldInfo _clientName;
    private static MethodInfo _sendAnglerQuest;
    private static int _anglerQuestFinishedMessageId;
    private static bool _advancing;
    private static bool _advanceFailureLogged;

    public static bool ParticipationCommandsEnabled { get; private set; }
    public static FieldInfo FinishedTodayField { get; private set; }
    public static MethodBase UpdateTimeMethod { get; private set; }
    public static MethodBase UpdateTimeStartDayMethod { get; private set; }
    public static MethodBase MessageBufferGetDataMethod { get; private set; }
    public static MethodBase ChatProcessIncomingMessageMethod { get; private set; }
    public static MethodInfo AnglerQuestSwapMethod { get; private set; }

    public static void Initialize(string modDirectory)
    {
        var gameAssembly = typeof(Main).Assembly;

        _netMode = RequireField(typeof(Main), "netMode", typeof(int));
        FinishedTodayField = RequireField(typeof(Main), "anglerWhoFinishedToday", null);
        _players = RequireField(typeof(Main), "player", null);

        AnglerQuestSwapMethod = RequireStaticVoidMethod(typeof(Main), "AnglerQuestSwap", Type.EmptyTypes);
        UpdateTimeMethod = RequireStaticVoidMethod(typeof(Main), "UpdateTime", Type.EmptyTypes);
        UpdateTimeStartDayMethod = RequireStaticVoidMethod(
            typeof(Main),
            "UpdateTime_StartDay",
            new[] { typeof(bool).MakeByRefType() });

        MessageBufferGetDataMethod = typeof(MessageBuffer)
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

        var netplayType = gameAssembly.GetType("Terraria.Netplay", throwOnError: true);
        _netplayClients = RequireField(netplayType, "Clients", null);
        if (!_netplayClients.FieldType.IsArray)
            throw new InvalidOperationException("Terraria.Netplay.Clients is no longer an array.");

        var clientType = _netplayClients.FieldType.GetElementType();
        _clientState = RequireField(clientType, "State", typeof(int));
        _clientName = AccessTools.Field(clientType, "Name");
        if (_clientName != null && _clientName.FieldType != typeof(string))
            _clientName = null;

        var netMessageType = gameAssembly.GetType("Terraria.NetMessage", throwOnError: true);
        _sendAnglerQuest = RequireStaticVoidMethod(netMessageType, "SendAnglerQuest", new[] { typeof(int) });

        var messageIdType = gameAssembly.GetType("Terraria.ID.MessageID", throwOnError: true);
        _anglerQuestFinishedMessageId = ReadConstantInt(messageIdType, "AnglerQuestFinished");

        if (!typeof(IList<string>).IsAssignableFrom(FinishedTodayField.FieldType))
            throw new InvalidOperationException("Main.anglerWhoFinishedToday is no longer an IList<string>.");

        if (!_players.FieldType.IsArray)
            throw new InvalidOperationException("Main.player is no longer an array.");

        ParticipationCommandsEnabled = LoadParticipationConfig(modDirectory);
        ChatProcessIncomingMessageMethod = null;

        if (ParticipationCommandsEnabled)
        {
            var chatProcessorType = gameAssembly.GetType("Terraria.Chat.ChatCommandProcessor", throwOnError: true);
            var chatMessageType = gameAssembly.GetType("Terraria.Chat.ChatMessage", throwOnError: true);
            ChatProcessIncomingMessageMethod = RequireInstanceVoidMethod(
                chatProcessorType,
                "ProcessIncomingMessage",
                new[] { chatMessageType, typeof(int) });
        }

        CompletedSlots.Clear();
        OptedOutSlots.Clear();
    }

    public static bool IsAnglerCompletionPacket(MessageBuffer buffer, int start)
    {
        if (buffer == null || GetNetMode() != 2 || buffer.readBuffer == null ||
            start < 0 || start >= buffer.readBuffer.Length)
        {
            return false;
        }

        return buffer.readBuffer[start] == _anglerQuestFinishedMessageId;
    }

    public static void HandleQuestCompletion(int whoAmI)
    {
        if (_advancing || GetNetMode() != 2)
            return;

        var clients = (Array)_netplayClients.GetValue(null);
        var players = (Array)_players.GetValue(null);
        if (clients == null || players == null ||
            whoAmI < 0 || whoAmI >= clients.Length || whoAmI >= players.Length)
        {
            return;
        }

        var client = clients.GetValue(whoAmI);
        if (client == null || GetClientState(client) != FullyConnectedState)
            return;

        var player = players.GetValue(whoAmI);
        if (player == null)
            return;

        var finishedToday = GetFinishedToday();
        var playerName = GetPlayerName(player);

        // Let vanilla validate/record packet 75 first. Terraria 1.4.5.8 uses the raw
        // player name, including an empty string, so match that behavior exactly.
        if (playerName == null || !finishedToday.Contains(playerName))
            return;

        CompletedSlots.Add(whoAmI);
        RefreshRoundState(finishedToday);

        if (AllRequiredConnectedSlotsFinished())
        {
            AdvanceQuest(finishedToday);
            return;
        }

        // This applies equally to IN and OUT players: a successful turn-in always
        // locks that vanilla client out of repeating the same quest.
        SendAnglerQuest(whoAmI);
    }

    public static bool TryHandleChatCommand(ChatMessage message, int clientId)
    {
        if (!ParticipationCommandsEnabled || message == null || GetNetMode() != 2)
            return false;

        var text = (message.Text ?? string.Empty).Trim();
        if (!text.Equals("!fish", StringComparison.OrdinalIgnoreCase) &&
            !text.StartsWith("!fish ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!IsFullyConnectedSlot(clientId))
            return false;

        // The command belongs to Infinite Angler, not Terraria's Say command.
        message.Consume();

        var argument = text.Length == 5 ? "status" : text.Substring(5).Trim();
        if (argument.Length == 0)
            argument = "status";

        var finishedToday = GetFinishedToday();
        RefreshRoundState(finishedToday);

        if (argument.Equals("out", StringComparison.OrdinalIgnoreCase))
        {
            var changed = OptedOutSlots.Add(clientId);
            SendCommandReply(
                clientId,
                changed
                    ? "You are OUT. You can still turn in fish, but you will not block the next quest."
                    : "You are already OUT. You can still turn in fish, but you are not required.");

            if (AllRequiredConnectedSlotsFinished())
                AdvanceQuest(finishedToday);

            return true;
        }

        if (argument.Equals("in", StringComparison.OrdinalIgnoreCase))
        {
            var changed = OptedOutSlots.Remove(clientId);
            SendCommandReply(
                clientId,
                changed
                    ? (CompletedSlots.Contains(clientId)
                        ? "You are IN and already finished this quest."
                        : "You are IN and now count toward this quest.")
                    : "You are already IN.");

            // If this player completed while OUT, opting back IN preserves that
            // completion and may be the final condition needed for a swap.
            if (AllRequiredConnectedSlotsFinished())
                AdvanceQuest(finishedToday);

            return true;
        }

        if (argument.Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            SendCommandReply(clientId, BuildStatusMessage(clientId));
            return true;
        }

        SendCommandReply(clientId, "Usage: !fish in, !fish out, or !fish status.");
        return true;
    }

    public static void TryAdvanceQuest()
    {
        if (_advancing || GetNetMode() != 2)
            return;

        var finishedToday = GetFinishedToday();
        RefreshRoundState(finishedToday);

        if (AllRequiredConnectedSlotsFinished())
            AdvanceQuest(finishedToday);
    }

    private static void RefreshRoundState(IList<string> finishedToday)
    {
        var clients = (Array)_netplayClients.GetValue(null);
        var players = (Array)_players.GetValue(null);
        if (clients == null || players == null)
            return;

        var count = Math.Min(clients.Length, players.Length);
        for (var index = 0; index < count; index++)
        {
            var client = clients.GetValue(index);
            if (client == null || GetClientState(client) != FullyConnectedState)
            {
                CompletedSlots.Remove(index);
                OptedOutSlots.Remove(index);
                continue;
            }

            if (CompletedSlots.Contains(index))
                continue;

            // Reconstruct completion after reconnect/slot transitions from vanilla's
            // persisted completion-name list. Participation itself is session-only:
            // a disconnected slot's OUT state was removed above, so reconnects are IN.
            var clientName = GetClientName(client);
            if (!string.IsNullOrEmpty(clientName) && finishedToday.Contains(clientName))
            {
                CompletedSlots.Add(index);
                continue;
            }

            var player = players.GetValue(index);
            var playerName = player == null ? null : GetPlayerName(player);
            if (playerName != null && finishedToday.Contains(playerName))
                CompletedSlots.Add(index);
        }

        CompletedSlots.RemoveWhere(index => index < 0 || index >= count);
        OptedOutSlots.RemoveWhere(index => index < 0 || index >= count);
    }

    private static bool AllRequiredConnectedSlotsFinished()
    {
        var clients = (Array)_netplayClients.GetValue(null);
        if (clients == null)
            return false;

        var anyRequired = false;
        for (var index = 0; index < clients.Length; index++)
        {
            var client = clients.GetValue(index);
            if (client == null || GetClientState(client) != FullyConnectedState)
                continue;

            if (ParticipationCommandsEnabled && OptedOutSlots.Contains(index))
                continue;

            anyRequired = true;
            if (!CompletedSlots.Contains(index))
                return false;
        }

        // If everyone opted out, keep the current quest parked. This prevents an
        // empty quorum from continuously swapping quests every server tick.
        return anyRequired;
    }

    private static bool IsFullyConnectedSlot(int index)
    {
        var clients = (Array)_netplayClients.GetValue(null);
        if (clients == null || index < 0 || index >= clients.Length)
            return false;

        var client = clients.GetValue(index);
        return client != null && GetClientState(client) == FullyConnectedState;
    }

    private static void AdvanceQuest(IList<string> finishedToday)
    {
        var completedNames = finishedToday.ToArray();
        var completedSlots = CompletedSlots.ToArray();

        try
        {
            _advancing = true;

            // The ONLY successful path that starts another quest. Vanilla clears its
            // name list, chooses the next quest, and broadcasts personalized packet 74
            // to every connected vanilla client. OUT players receive the new quest too;
            // only their quorum obligation is different.
            AnglerQuestSwapMethod.Invoke(null, null);
            CompletedSlots.Clear();
            _advanceFailureLogged = false;
        }
        catch (Exception ex)
        {
            finishedToday.Clear();
            foreach (var name in completedNames)
                finishedToday.Add(name);

            CompletedSlots.Clear();
            foreach (var slot in completedSlots)
                CompletedSlots.Add(slot);

            SendAnglerQuest(-1);

            if (!_advanceFailureLogged)
            {
                _advanceFailureLogged = true;
                Console.Error.WriteLine("[Infinite Angler] Quest advance failed: " + Unwrap(ex));
            }
        }
        finally
        {
            _advancing = false;
        }
    }

    private static string BuildStatusMessage(int clientId)
    {
        var clients = (Array)_netplayClients.GetValue(null);
        if (clients == null)
            return "Status unavailable.";

        var waiting = new List<string>();
        var finished = new List<string>();
        var optedOut = new List<string>();

        for (var index = 0; index < clients.Length; index++)
        {
            var client = clients.GetValue(index);
            if (client == null || GetClientState(client) != FullyConnectedState)
                continue;

            var name = GetDisplayName(index, client);
            if (OptedOutSlots.Contains(index))
                optedOut.Add(name);
            else if (CompletedSlots.Contains(index))
                finished.Add(name);
            else
                waiting.Add(name);
        }

        var ownState = OptedOutSlots.Contains(clientId) ? "OUT" : "IN";
        return "You are " + ownState +
               ". Waiting: " + FormatNames(waiting) +
               ". Finished: " + FormatNames(finished) +
               ". Out: " + FormatNames(optedOut) + ".";
    }

    private static string GetDisplayName(int index, object client)
    {
        var name = GetClientName(client);
        if (!string.IsNullOrEmpty(name))
            return name;

        var players = (Array)_players.GetValue(null);
        if (players != null && index >= 0 && index < players.Length)
        {
            var player = players.GetValue(index);
            name = player == null ? null : GetPlayerName(player);
            if (!string.IsNullOrEmpty(name))
                return name;
        }

        return "Player " + index;
    }

    private static string FormatNames(IList<string> names)
        => names == null || names.Count == 0 ? "none" : string.Join(", ", names);

    private static void SendCommandReply(int clientId, string text)
    {
        try
        {
            ChatHelper.SendChatMessageToClient(
                NetworkText.FromLiteral("[Infinite Angler] " + text),
                Color.Yellow,
                clientId);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[Infinite Angler] Failed to send command reply: " + Unwrap(ex));
        }
    }

    private static bool LoadParticipationConfig(string modDirectory)
    {
        if (string.IsNullOrWhiteSpace(modDirectory))
        {
            modDirectory = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "gmods",
                "InfiniteAngler");
        }

        var configPath = Path.Combine(modDirectory, ConfigFileName);
        if (!File.Exists(configPath))
        {
            Console.WriteLine(
                "[Infinite Angler] " + ConfigFileName + " not found; participation commands default to disabled.");
            return false;
        }

        foreach (var rawLine in File.ReadAllLines(configPath))
        {
            var line = (rawLine ?? string.Empty).Trim();
            if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";"))
                continue;

            var equals = line.IndexOf('=');
            if (equals <= 0)
                continue;

            var key = line.Substring(0, equals).Trim();
            if (!key.Equals(ParticipationConfigKey, StringComparison.OrdinalIgnoreCase))
                continue;

            var value = line.Substring(equals + 1).Trim();
            bool enabled;
            if (bool.TryParse(value, out enabled))
                return enabled;

            Console.Error.WriteLine(
                "[Infinite Angler] Invalid " + ParticipationConfigKey + " value '" + value +
                "'; expected true or false. Commands remain disabled.");
            return false;
        }

        return false;
    }

    private static int GetClientState(object client)
        => (int)_clientState.GetValue(client);

    private static string GetClientName(object client)
        => _clientName == null || client == null ? null : _clientName.GetValue(client) as string;

    private static string GetPlayerName(object player)
    {
        var field = AccessTools.Field(player.GetType(), "name")
                    ?? throw new MissingFieldException(player.GetType().FullName, "name");

        if (field.FieldType != typeof(string))
            throw new InvalidOperationException(player.GetType().FullName + ".name is no longer string.");

        return field.GetValue(player) as string;
    }

    private static void SendAnglerQuest(int remoteClient)
    {
        try
        {
            _sendAnglerQuest.Invoke(null, new object[] { remoteClient });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[Infinite Angler] Failed to sync quest state: " + Unwrap(ex));
        }
    }

    private static IList<string> GetFinishedToday()
        => (IList<string>)FinishedTodayField.GetValue(null);

    private static int GetNetMode()
        => (int)_netMode.GetValue(null);

    private static MethodInfo RequireStaticVoidMethod(Type type, string name, Type[] parameterTypes)
    {
        return type
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .SingleOrDefault(method => MethodMatches(method, name, typeof(void), parameterTypes))
            ?? throw new MissingMethodException(type.FullName, name + "()");
    }

    private static MethodInfo RequireInstanceVoidMethod(Type type, string name, Type[] parameterTypes)
    {
        return type
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .SingleOrDefault(method => MethodMatches(method, name, typeof(void), parameterTypes))
            ?? throw new MissingMethodException(type.FullName, name + "()");
    }

    private static bool MethodMatches(MethodInfo method, string name, Type returnType, Type[] parameterTypes)
    {
        if (method.Name != name || method.ReturnType != returnType)
            return false;

        var parameters = method.GetParameters();
        if (parameters.Length != parameterTypes.Length)
            return false;

        for (var index = 0; index < parameters.Length; index++)
        {
            if (parameters[index].ParameterType != parameterTypes[index])
                return false;
        }

        return true;
    }

    private static FieldInfo RequireField(Type type, string name, Type expectedType)
    {
        var field = AccessTools.Field(type, name)
                    ?? throw new MissingFieldException(type.FullName, name);

        if (expectedType != null && field.FieldType != expectedType)
        {
            throw new InvalidOperationException(
                type.FullName + "." + name + " changed type from " +
                expectedType.FullName + " to " + field.FieldType.FullName + ".");
        }

        return field;
    }

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
#else
public static class Mod
{
    public static void Load() { }
}
#endif
