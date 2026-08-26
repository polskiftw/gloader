#if GLOADER_SERVER
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Terraria;

public static class Mod
{
    public static void Load()
    {
        InfiniteAnglerRuntime.Initialize();
        Console.WriteLine("[Infinite Angler] Slot-authoritative shared Angler rounds enabled on server.");
    }
}

// Terraria 1.4.5.8 rolls the vanilla daily quest from
// Main.UpdateTime_StartDay(ref bool). Suppress exactly that AnglerQuestSwap call.
// The shared round below is now the only thing allowed to advance the quest.
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

// A tick check handles disconnects and reconnects. Disconnected slots are removed
// from the round immediately; fully connected slots are restored as completed when
// vanilla's completion-name list proves that same connected player already turned in.
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

    // This is the actual shared-round authority. Names remain vanilla's mechanism
    // for packet-74 personalization, but they no longer decide how many players the
    // round contains. Every State-10 connection slot must be represented here.
    private static readonly HashSet<int> CompletedSlots = new HashSet<int>();

    private static FieldInfo _netMode;
    private static FieldInfo _players;
    private static FieldInfo _netplayClients;
    private static FieldInfo _clientState;
    private static FieldInfo _clientName;
    private static MethodInfo _sendAnglerQuest;
    private static int _anglerQuestFinishedMessageId;
    private static bool _advancing;
    private static bool _advanceFailureLogged;

    public static FieldInfo FinishedTodayField { get; private set; }
    public static MethodBase UpdateTimeMethod { get; private set; }
    public static MethodBase UpdateTimeStartDayMethod { get; private set; }
    public static MethodBase MessageBufferGetDataMethod { get; private set; }
    public static MethodInfo AnglerQuestSwapMethod { get; private set; }

    public static void Initialize()
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

        CompletedSlots.Clear();
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
        RefreshCompletedSlots(finishedToday);

        if (AllConnectedSlotsFinished())
        {
            AdvanceQuest(finishedToday);
            return;
        }

        // Keep this vanilla client locked out of the SAME quest. No global quest
        // state changes until every connected slot has completed this round.
        SendAnglerQuest(whoAmI);
    }

    public static void TryAdvanceQuest()
    {
        if (_advancing || GetNetMode() != 2)
            return;

        var finishedToday = GetFinishedToday();
        RefreshCompletedSlots(finishedToday);

        if (AllConnectedSlotsFinished())
            AdvanceQuest(finishedToday);
    }

    private static void RefreshCompletedSlots(IList<string> finishedToday)
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
                continue;
            }

            if (CompletedSlots.Contains(index))
                continue;

            // Reconstruct completion after a reconnect/slot transition from vanilla's
            // persisted name list. Prefer RemoteClient.Name because it belongs to the
            // connection; fall back to Main.player[].name when needed.
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

        // Netplay currently has at most the player-array count, but clean any stale
        // slot markers beyond the shared range defensively.
        CompletedSlots.RemoveWhere(index => index < 0 || index >= count);
    }

    private static bool AllConnectedSlotsFinished()
    {
        var clients = (Array)_netplayClients.GetValue(null);
        if (clients == null)
            return false;

        var anyConnected = false;
        for (var index = 0; index < clients.Length; index++)
        {
            var client = clients.GetValue(index);
            if (client == null || GetClientState(client) != FullyConnectedState)
                continue;

            anyConnected = true;
            if (!CompletedSlots.Contains(index))
                return false;
        }

        return anyConnected;
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
            // to every connected vanilla client.
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
            .SingleOrDefault(method =>
            {
                if (method.Name != name || method.ReturnType != typeof(void))
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
            })
            ?? throw new MissingMethodException(type.FullName, name + "()");
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
