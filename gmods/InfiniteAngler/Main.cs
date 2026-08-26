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

// Terraria 1.4.5.8 does not roll the Angler quest directly inside UpdateTime().
// Dawn is split into Main.UpdateTime_StartDay(), which calls AnglerQuestSwap().
// AnglerQuestSwap() itself clears anglerWhoFinishedToday before selecting and
// broadcasting the next quest. Suppress only that dawn call; our own explicit
// swap after the whole connected group finishes still uses vanilla code.
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
                "Expected exactly one Main.AnglerQuestSwap() call in Main.UpdateTime_StartDay(), found " +
                swapIndexes.Length + ".");
        }

        var swapIndex = swapIndexes[0];
        list[swapIndex].opcode = OpCodes.Nop;
        list[swapIndex].operand = null;
        return list;
    }
}

// Check the shared round after ordinary server time processing. Once every active
// player name appears in vanilla's anglerWhoFinishedToday list, call the normal
// AnglerQuestSwap(). Vanilla clears the completion list, picks a valid next quest,
// resets anglerQuestFinished, and broadcasts the new quest to connected clients.
[HarmonyPatch]
internal static class InfiniteAnglerRoundPatch
{
    private static MethodBase TargetMethod() => InfiniteAnglerRuntime.UpdateTimeMethod;

    [HarmonyPostfix]
    private static void Postfix() => InfiniteAnglerRuntime.TryAdvanceQuest();
}

internal static class InfiniteAnglerRuntime
{
    private static FieldInfo _netMode;
    private static FieldInfo _players;
    private static bool _advancing;
    private static bool _advanceFailureLogged;

    public static FieldInfo FinishedTodayField { get; private set; }
    public static MethodBase UpdateTimeMethod { get; private set; }
    public static MethodBase UpdateTimeStartDayMethod { get; private set; }
    public static MethodInfo AnglerQuestSwapMethod { get; private set; }

    public static void Initialize()
    {
        _netMode = RequireField(typeof(Main), "netMode", typeof(int));
        FinishedTodayField = RequireField(typeof(Main), "anglerWhoFinishedToday", null);
        _players = RequireField(typeof(Main), "player", null);

        AnglerQuestSwapMethod = RequireStaticVoidMethod("AnglerQuestSwap");
        UpdateTimeMethod = RequireStaticVoidMethod("UpdateTime");
        UpdateTimeStartDayMethod = RequireStaticVoidMethod("UpdateTime_StartDay");

        if (!typeof(IList<string>).IsAssignableFrom(FinishedTodayField.FieldType))
        {
            throw new InvalidOperationException(
                "Main.anglerWhoFinishedToday is no longer an IList<string>.");
        }

        if (!_players.FieldType.IsArray)
            throw new InvalidOperationException("Main.player is no longer an array.");
    }

    public static void TryAdvanceQuest()
    {
        if (_advancing || GetNetMode() != 2)
            return;

        var finishedToday = GetFinishedToday();
        if (!AllConnectedPlayersFinished(finishedToday))
            return;

        var completedNames = finishedToday.ToArray();

        try
        {
            _advancing = true;

            // Terraria 1.4.5.8 AnglerQuestSwap() begins by clearing
            // anglerWhoFinishedToday, so do not duplicate that operation here.
            AnglerQuestSwapMethod.Invoke(null, null);
            _advanceFailureLogged = false;
        }
        catch (Exception ex)
        {
            // If vanilla cleared the list and then failed partway through the swap,
            // restore this round so nobody can claim the same quest twice.
            finishedToday.Clear();
            foreach (var name in completedNames)
                finishedToday.Add(name);

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
        var players = (Array)_players.GetValue(null);
        if (players == null)
            return false;

        var anyConnected = false;

        for (var index = 0; index < players.Length; index++)
        {
            var player = players.GetValue(index);
            if (player == null || !IsActive(player))
                continue;

            anyConnected = true;
            var name = GetPlayerName(player);
            if (string.IsNullOrEmpty(name) || !finishedToday.Contains(name))
                return false;
        }

        return anyConnected;
    }

    private static bool IsActive(object player)
    {
        var field = AccessTools.Field(player.GetType(), "active")
                    ?? throw new MissingFieldException(player.GetType().FullName, "active");

        if (field.FieldType != typeof(bool))
        {
            throw new InvalidOperationException(
                player.GetType().FullName + ".active is no longer bool.");
        }

        return (bool)field.GetValue(player);
    }

    private static string GetPlayerName(object player)
    {
        var field = AccessTools.Field(player.GetType(), "name")
                    ?? throw new MissingFieldException(player.GetType().FullName, "name");

        if (field.FieldType != typeof(string))
        {
            throw new InvalidOperationException(
                player.GetType().FullName + ".name is no longer string.");
        }

        return field.GetValue(player) as string;
    }

    private static IList<string> GetFinishedToday()
        => (IList<string>)FinishedTodayField.GetValue(null);

    private static int GetNetMode()
        => (int)_netMode.GetValue(null);

    private static MethodInfo RequireStaticVoidMethod(string name)
    {
        return typeof(Main)
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .SingleOrDefault(method =>
                method.Name == name &&
                method.ReturnType == typeof(void) &&
                method.GetParameters().Length == 0)
            ?? throw new MissingMethodException(typeof(Main).FullName, name + "()");
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
