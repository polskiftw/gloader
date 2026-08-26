using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Xna.Framework
{
    public struct Color
    {
        public static Color Yellow => new Color();
    }
}

namespace Terraria.Localization
{
    public sealed class NetworkText
    {
        private readonly string _text;

        private NetworkText(string text)
        {
            _text = text ?? string.Empty;
        }

        public static NetworkText FromLiteral(string text)
        {
            return new NetworkText(text);
        }

        public override string ToString()
        {
            return _text;
        }
    }
}

namespace Terraria.Chat
{
    public sealed class ChatMessage
    {
        public ChatMessage(string text)
        {
            Text = text;
        }

        public string Text { get; set; }
        public bool IsConsumed { get; private set; }

        public void Consume()
        {
            IsConsumed = true;
        }
    }

    public sealed class ChatCommandProcessor
    {
        public static int VanillaMessagesProcessed;

        public void ProcessIncomingMessage(ChatMessage message, int clientId)
        {
            VanillaMessagesProcessed++;
        }
    }

    public static class ChatHelper
    {
        public static readonly Dictionary<int, List<string>> Replies =
            new Dictionary<int, List<string>>();

        public static void SendChatMessageToClient(
            Terraria.Localization.NetworkText text,
            Microsoft.Xna.Framework.Color color,
            int playerId)
        {
            List<string> messages;
            if (!Replies.TryGetValue(playerId, out messages))
            {
                messages = new List<string>();
                Replies[playerId] = messages;
            }

            messages.Add(text == null ? string.Empty : text.ToString());
        }

        public static string LastReply(int playerId)
        {
            List<string> messages;
            if (!Replies.TryGetValue(playerId, out messages) || messages.Count == 0)
                return null;

            return messages[messages.Count - 1];
        }

        public static void Reset()
        {
            Replies.Clear();
        }
    }
}

namespace Terraria.ID
{
    public static class MessageID
    {
        public const byte AnglerQuest = 74;
        public const byte AnglerQuestFinished = 75;
    }
}

namespace Terraria
{
    public static class Main
    {
        public static int netMode = 2;
        public static int anglerQuest = 7;
        public static bool anglerQuestFinished = true;
        public static List<string> anglerWhoFinishedToday = new List<string>();
        public static Player[] player = Enumerable.Range(0, 8).Select(_ => new Player()).ToArray();

        public static bool triggerDawnReset;
        public static int questSwapCount;

        public static void UpdateTime()
        {
            if (!triggerDawnReset)
                return;

            triggerDawnReset = false;
            var stopEvents = false;
            UpdateTime_StartDay(ref stopEvents);
        }

        public static void UpdateTime_StartDay(ref bool stopEvents)
        {
            AnglerQuestSwap();
        }

        public static void AnglerQuestSwap()
        {
            anglerWhoFinishedToday.Clear();
            anglerQuestFinished = false;
            anglerQuest = (anglerQuest + 1) % 40;
            questSwapCount++;
            NetMessage.SendAnglerQuest(-1);
        }
    }

    public sealed class Player
    {
        public bool active;
        public string name = string.Empty;
    }

    public sealed class RemoteClient
    {
        public string Name = string.Empty;
        public int State;
    }

    public static class Netplay
    {
        public static RemoteClient[] Clients =
            Enumerable.Range(0, 8).Select(_ => new RemoteClient()).ToArray();
    }

    public static class NetMessage
    {
        public static bool[] clientQuestFinished = new bool[8];
        public static int[] clientQuest = new int[8];
        public static int sendAnglerQuestCount;

        public static void SendAnglerQuest(int remoteClient)
        {
            if (Main.netMode != 2)
                return;

            if (remoteClient == -1)
            {
                for (var index = 0; index < Netplay.Clients.Length; index++)
                {
                    if (Netplay.Clients[index].State == 10)
                        Sync(index);
                }

                return;
            }

            if (remoteClient < 0 || remoteClient >= Netplay.Clients.Length ||
                Netplay.Clients[remoteClient].State != 10)
            {
                return;
            }

            Sync(remoteClient);
        }

        private static void Sync(int remoteClient)
        {
            clientQuest[remoteClient] = Main.anglerQuest;
            clientQuestFinished[remoteClient] =
                Main.anglerWhoFinishedToday.Contains(Main.player[remoteClient].name);
            sendAnglerQuestCount++;
        }
    }

    public sealed class MessageBuffer
    {
        public byte[] readBuffer = new byte[256];
        public int whoAmI;

        // Match TerrariaServer 1.4.5.8 packet 75 exactly for the state Infinite
        // Angler cares about. Vanilla stores Main.player[whoAmI].name without
        // filtering on Player.active or rejecting an empty name.
        public void GetData(int start, int length, out int messageType)
        {
            messageType = readBuffer[start];
            if (messageType != ID.MessageID.AnglerQuestFinished || Main.netMode != 2)
                return;

            var name = Main.player[whoAmI].name;
            if (Main.anglerWhoFinishedToday.Contains(name))
                return;

            Main.anglerWhoFinishedToday.Add(name);
        }
    }
}

namespace FixtureServer
{
    internal static class Program
    {
        public static int Main(string[] args)
        {
            Require(
                args.Length == 2 && args[0] == "--fixture-arg" && args[1] == "hello world",
                "Host & Play redirect did not preserve the original server arguments.");

            TestParticipationCommands();
            TestEmptyQuorumDoesNotAdvance();
            TestUnnamedConnectedSlotRegression();
            TestSharedRoundCoreBehavior();

            Console.WriteLine(
                "PASS: Steam Host & Play routed through gloader; Infinite Angler keeps vanilla clients synchronized, supports optional !fish participation commands, preserves opted-out turn-ins, never advances an empty quorum, ignores dawn, counts joiners, and releases disconnects.");
            return 0;
        }

        private static void TestParticipationCommands()
        {
            ResetFixture();

            var normalChat = Chat(1, "hello world");
            Require(Terraria.Chat.ChatCommandProcessor.VanillaMessagesProcessed == 1,
                "non-command chat was intercepted");
            Require(!normalChat.IsConsumed,
                "non-command chat was consumed by Infinite Angler");

            var status = Chat(1, "!fish");
            Require(status.IsConsumed, "!fish status command was not consumed");
            Require(Terraria.Chat.ChatCommandProcessor.VanillaMessagesProcessed == 1,
                "!fish status leaked into vanilla chat");
            Require((Terraria.Chat.ChatHelper.LastReply(1) ?? string.Empty).Contains("You are IN"),
                "!fish did not report the player's participation state");

            var outMessage = Chat(2, "!fish out");
            Require(outMessage.IsConsumed, "!fish out was not consumed");
            Require((Terraria.Chat.ChatHelper.LastReply(2) ?? string.Empty).Contains("You are OUT"),
                "!fish out did not confirm the opt-out");

            // Slot 2 is still connected and unfinished, but OUT means it no longer
            // blocks the shared-round quorum. Slot 1 finishing must advance exactly once.
            Complete(1);
            Require(Terraria.Main.anglerQuest == 8,
                "opted-out connected player still blocked the quest advance");
            Require(Terraria.Main.questSwapCount == 1,
                "opt-out round did not swap exactly once");

            // OUT removes obligation, not participation. The opted-out player can
            // turn in the new quest, gets locked out of repeating it, and the quest
            // stays put because required slot 1 is still unfinished.
            Complete(2);
            Require(Terraria.Main.anglerQuest == 8,
                "opted-out player's turn-in advanced the quest by itself");
            Require(Terraria.NetMessage.clientQuestFinished[2],
                "opted-out finisher was not locked out of repeating the same quest");

            // Rejoining the quorum must preserve that completion. Slot 2 is already
            // done, so the round should wait only for slot 1.
            Chat(2, "!fish in");
            Require((Terraria.Chat.ChatHelper.LastReply(2) ?? string.Empty).Contains("already finished"),
                "!fish in forgot completion earned while opted out");
            Require(Terraria.Main.anglerQuest == 8,
                "opting back in advanced before the other required player finished");

            Complete(1);
            Require(Terraria.Main.anglerQuest == 9,
                "round did not advance after all IN players were complete");
            Require(Terraria.Main.questSwapCount == 2,
                "rejoined-quorum round did not swap exactly once");
        }

        private static void TestEmptyQuorumDoesNotAdvance()
        {
            ResetFixture();

            Chat(1, "!fish out");
            Chat(2, "!fish out");
            Tick();
            Require(Terraria.Main.anglerQuest == 7,
                "all-opted-out empty quorum advanced on a server tick");
            Require(Terraria.Main.questSwapCount == 0,
                "all-opted-out empty quorum performed a quest swap");

            // Turning in while OUT is still allowed and still must not create a swap
            // when nobody is currently required.
            Complete(1);
            Require(Terraria.Main.anglerQuest == 7,
                "opted-out turn-in advanced an empty quorum");
            Require(Terraria.NetMessage.clientQuestFinished[1],
                "opted-out player was not marked finished after a valid turn-in");

            // If that already-finished player opts back in, they become the only
            // required player and are already complete, so the round may advance now.
            Chat(1, "!fish in");
            Require(Terraria.Main.anglerQuest == 8,
                "already-finished player opting back in did not satisfy the quorum");
            Require(Terraria.Main.questSwapCount == 1,
                "empty-quorum recovery swapped an unexpected number of times");
        }

        private static void TestUnnamedConnectedSlotRegression()
        {
            ResetFixture();
            Terraria.Main.player[2].name = string.Empty;
            Complete(1);
            Require(Terraria.Main.anglerQuest == 7,
                "connected slot with an empty player name vanished from the quorum");
            Require(Terraria.Main.questSwapCount == 0,
                "quest swapped while a State-10 client was still unfinished");
            Require(Terraria.NetMessage.clientQuestFinished[1],
                "first finisher was not kept locked while waiting for the connected host slot");

            Complete(2);
            Require(Terraria.Main.anglerQuest == 8,
                "round did not advance after the previously unnamed connected slot finished");
            Require(Terraria.Main.questSwapCount == 1,
                "unnamed-slot regression round did not swap exactly once");
        }

        private static void TestSharedRoundCoreBehavior()
        {
            ResetFixture();

            Complete(1);
            Require(Terraria.Main.anglerQuest == 7, "quest advanced before every connected player finished");
            Require(Terraria.Main.questSwapCount == 0, "quest swap ran after only one completion");
            Require(Terraria.Main.anglerWhoFinishedToday.SequenceEqual(new[] { "VanillaGuest" }),
                "first player's vanilla completion marker was not preserved");
            Require(Terraria.NetMessage.clientQuest[1] == 7,
                "first finisher was synced to the wrong quest");
            Require(Terraria.NetMessage.clientQuestFinished[1],
                "first finisher was not told that the current quest is already complete for them");
            Require(!Terraria.NetMessage.clientQuestFinished[2],
                "unfinished second client was incorrectly marked finished");

            Terraria.Main.triggerDawnReset = true;
            Tick();
            Require(Terraria.Main.anglerQuest == 7, "dawn changed the Angler quest");
            Require(Terraria.Main.questSwapCount == 0, "dawn still called AnglerQuestSwap");
            Require(Terraria.Main.anglerWhoFinishedToday.SequenceEqual(new[] { "VanillaGuest" }),
                "dawn cleared the current round's completion state");
            Require(Terraria.NetMessage.clientQuestFinished[1],
                "dawn unlocked a player who had already finished the shared quest");

            Complete(2);
            Require(Terraria.Main.anglerQuest == 8, "all-player completion did not immediately advance the quest");
            Require(Terraria.Main.questSwapCount == 1, "all-player completion did not perform exactly one swap");
            Require(Terraria.Main.anglerWhoFinishedToday.Count == 0,
                "new shared round did not clear the completion list");
            Require(Terraria.NetMessage.clientQuest[1] == 8 && Terraria.NetMessage.clientQuest[2] == 8,
                "new shared quest was not broadcast to both vanilla clients");
            Require(!Terraria.NetMessage.clientQuestFinished[1] && !Terraria.NetMessage.clientQuestFinished[2],
                "new shared round did not unlock both vanilla clients");

            Connect(3, "LateGuest");
            Complete(1);
            Complete(2);
            Require(Terraria.Main.anglerQuest == 8, "late joiner did not block the current round");
            Require(Terraria.NetMessage.clientQuestFinished[1] && Terraria.NetMessage.clientQuestFinished[2],
                "finished players were not kept locked while waiting for the late joiner");
            Require(!Terraria.NetMessage.clientQuestFinished[3],
                "late joiner was incorrectly marked finished");

            Complete(3);
            Require(Terraria.Main.anglerQuest == 9, "round did not advance after late joiner completed");
            Require(Terraria.Main.questSwapCount == 2, "late-join round swapped an unexpected number of times");
            Require(Terraria.Main.anglerWhoFinishedToday.Count == 0,
                "late-join round did not start cleanly");

            Complete(1);
            Complete(2);
            Require(Terraria.Main.anglerQuest == 9, "round advanced while third client was still connected");

            Disconnect(3, leavePlayerActive: true);
            Tick();
            Require(Terraria.Main.anglerQuest == 10, "disconnect did not release the blocked round");
            Require(Terraria.Main.questSwapCount == 3, "disconnect-triggered round swapped incorrectly");
            Require(Terraria.Main.anglerWhoFinishedToday.Count == 0,
                "disconnect-triggered round did not start cleanly");

            Disconnect(2, leavePlayerActive: true);
            Complete(1);
            Require(Terraria.Main.anglerQuest == 11, "single connected client did not advance the shared quest");
            Require(Terraria.Main.questSwapCount == 4, "single-client round swapped incorrectly");
            Require(Terraria.Main.anglerWhoFinishedToday.Count == 0,
                "single-client round did not clear completion state");
            Require(Terraria.NetMessage.clientQuest[1] == 11 && !Terraria.NetMessage.clientQuestFinished[1],
                "single connected vanilla client did not receive the next quest unlocked");
        }

        private static void ResetFixture()
        {
            Terraria.Main.triggerDawnReset = false;

            // First disconnect every fixture slot and run one patched server tick so
            // Infinite Angler clears session-only OUT state from prior test phases.
            for (var index = 0; index < Terraria.Main.player.Length; index++)
                Terraria.Netplay.Clients[index].State = 0;
            Tick();

            Terraria.Main.netMode = 2;
            Terraria.Main.anglerQuest = 7;
            Terraria.Main.anglerQuestFinished = true;
            Terraria.Main.anglerWhoFinishedToday.Clear();
            Terraria.Main.questSwapCount = 0;
            Terraria.Main.triggerDawnReset = false;
            Terraria.NetMessage.sendAnglerQuestCount = 0;
            Terraria.Chat.ChatCommandProcessor.VanillaMessagesProcessed = 0;
            Terraria.Chat.ChatHelper.Reset();

            for (var index = 0; index < Terraria.Main.player.Length; index++)
            {
                Terraria.Main.player[index].active = false;
                Terraria.Main.player[index].name = string.Empty;
                Terraria.Netplay.Clients[index].Name = string.Empty;
                Terraria.Netplay.Clients[index].State = 0;
                Terraria.NetMessage.clientQuest[index] = -1;
                Terraria.NetMessage.clientQuestFinished[index] = false;
            }

            Connect(1, "VanillaGuest");
            Connect(2, "SecondGuest");
        }

        private static void Connect(int index, string name)
        {
            Terraria.Main.player[index].active = true;
            Terraria.Main.player[index].name = name;
            Terraria.Netplay.Clients[index].Name = name;
            Terraria.Netplay.Clients[index].State = 10;
            Terraria.NetMessage.SendAnglerQuest(index);
        }

        private static void Disconnect(int index, bool leavePlayerActive)
        {
            Terraria.Netplay.Clients[index].State = 0;
            if (!leavePlayerActive)
                Terraria.Main.player[index].active = false;
        }

        private static void Complete(int whoAmI)
        {
            var buffer = new Terraria.MessageBuffer { whoAmI = whoAmI };
            buffer.readBuffer[0] = Terraria.ID.MessageID.AnglerQuestFinished;
            int messageType;
            buffer.GetData(0, 1, out messageType);
            Require(messageType == Terraria.ID.MessageID.AnglerQuestFinished,
                "fixture completion message ID changed unexpectedly");
        }

        private static Terraria.Chat.ChatMessage Chat(int whoAmI, string text)
        {
            var message = new Terraria.Chat.ChatMessage(text);
            new Terraria.Chat.ChatCommandProcessor().ProcessIncomingMessage(message, whoAmI);
            return message;
        }

        private static void Tick()
        {
            Terraria.Main.UpdateTime();
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
