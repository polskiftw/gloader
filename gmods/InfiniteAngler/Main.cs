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
        Console.WriteLine("[Infinite Angler] Shared endless Angler quests enabled on server.");
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

// Vanilla packet 75 records the player's current Main.player[].name in
// anglerWhoFinishedToday. After vanilla handles that packet, keep the finisher's
// completely vanilla client locked to the current shared round until everyone who
// is actually connected has also finished it.
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

// Keep one lightweight server-tick check as a disconnect fallback. If an
// unfinished player leaves and everyone still connected had already finished,
// that disconnect itself satisfies the shared-round condition.
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

        // Match vanilla packet 75 exactly: an empty string is still a real list key.
        // Do not reject or skip it here. The old code did, while Terraria 1.4.5.8
        // itself simply adds Main.player[whoAmI].name to the list.
        if (playerName == null || !finishedToday.Contains(playerName))
            return;

        if (AllConnectedPlayersFinished(finishedToday))
        {
            AdvanceQuest(finishedToday);
            return;
        }

        // Keep this vanilla client locked out of the current quest while the other
        // connected players finish it. SendAnglerQuest is personalized: packet 74
        // contains the same global quest plus a bool based on this player's name in
        // anglerWhoFinishedToday.
        SendAnglerQuest(whoAmI);
    }

    public static void TryAdvanceQuest()
    {
        if (_advancing || GetNetMode() != 2)
            return;

        var finishedToday = GetFinishedToday();
        if (AllConnectedPlayersFinished(finishedToday))
            AdvanceQuest(finishedToday);
    }

    private static void AdvanceQuest(IList<string> finishedToday)
    {
        var completedNames = finishedToday.ToArray();

        try
        {
            _advancing = true;

            // Terraria 1.4.5.8 AnglerQuestSwap() clears anglerWhoFinishedToday,
            // selects the next quest, and calls NetMessage.SendAnglerQuest(-1).
            // That broadcast resets every connected vanilla client to unfinished for
            // the new shared round.
            AnglerQuestSwapMethod.Invoke(null, null);
            _advanceFailureLogged = false;
        }
        catch (Exception ex)
        {
            finishedToday.Clear();
            foreach (var name in completedNames)
                finishedToday.Add(name);

            // Restore each client's personalized finished flag if vanilla failed
            // after partially clearing/changing the round.
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

    private static bool AllConnectedPlayersFinished(IList<string> finishedToday)
    {
        var clients = (Array)_netplayClients.GetValue(null);
        var players = (Array)_players.GetValue(null);
        if (clients == null || players == null)
            return false;

        var anyConnected = false;
        var count = Math.Min(clients.Length, players.Length);

        for (var index = 0; index < count; index++)
        {
            var client = clients.GetValue(index);
            if (client == null || GetClientState(client) != FullyConnectedState)
                continue;

            // A State-10 slot is part of the round immediately. Never silently drop
            // it from the quorum because Main.player[index].name is temporarily empty
            // or not populated yet. That was the premature-swap bug in 0.1.9.
            anyConnected = true;

            var player = players.GetValue(index);
            if (!ConnectedSlotHasFinished(client, player, finishedToday))
                return false;
        }

        return anyConnected;
    }

    private static bool ConnectedSlotHasFinished(object client, object player, IList<string> finishedToday)
    {
        if (player != null)
        {
            var playerName = GetPlayerName(player);

            // Empty string is intentionally allowed: vanilla packet 75 also uses the
            // raw Main.player[].name without filtering it.
            if (playerName != null && finishedToday.Contains(playerName))
                return true;
        }

        // During connection/Host & Play transitions RemoteClient.Name can already be
        // populated while Main.player[].name is briefly unavailable. It is a safe
        // secondary identity for determining whether this connected slot completed.
        var clientName = GetClientName(client);
        return clientName != null && finishedToday.Contains(clientName);
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
