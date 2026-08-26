using System;
using System.Collections.Generic;
using System.Linq;

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

        // Terraria 1.4.5.8 calls UpdateTime_StartDay(ref bool) from UpdateTime().
        public static void UpdateTime()
        {
            if (!triggerDawnReset)
                return;

            triggerDawnReset = false;
            var stopEvents = false;
            UpdateTime_StartDay(ref stopEvents);
        }

        // Exact structural behavior relevant to the uploaded 1.4.5.8 server:
        // the dawn helper takes a ref bool and calls AnglerQuestSwap().
        public static void UpdateTime_StartDay(ref bool stopEvents)
        {
            AnglerQuestSwap();
        }

        // The real server clears the completion list, chooses the next quest, then
        // broadcasts personalized packet 74 state to every fully connected client.
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

        // Mirrors vanilla packet 75: the server only adds the player's name. It
        // does not automatically send packet 74 back to that player.
        public void GetData(int start, int length, out int messageType)
        {
            messageType = readBuffer[start];
            if (messageType != ID.MessageID.AnglerQuestFinished || Main.netMode != 2)
                return;

            var player = Main.player[whoAmI];
            if (!player.active || string.IsNullOrEmpty(player.name))
                return;

            if (!Main.anglerWhoFinishedToday.Contains(player.name))
                Main.anglerWhoFinishedToday.Add(player.name);
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

            ResetFixture();

            // First vanilla client finishes. The global quest must NOT move, but
            // that client must immediately receive packet 74 with finished=true so
            // the Angler tells them to wait instead of offering the same quest again.
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

            // Dawn must not reset the global quest or the per-player completion set.
            Terraria.Main.triggerDawnReset = true;
            Tick();
            Require(Terraria.Main.anglerQuest == 7, "dawn changed the Angler quest");
            Require(Terraria.Main.questSwapCount == 0, "dawn still called AnglerQuestSwap");
            Require(Terraria.Main.anglerWhoFinishedToday.SequenceEqual(new[] { "VanillaGuest" }),
                "dawn cleared the current round's completion state");
            Require(Terraria.NetMessage.clientQuestFinished[1],
                "dawn unlocked a player who had already finished the shared quest");

            // The second client finishes. This packet is the only event that should
            // advance the round. Vanilla AnglerQuestSwap then broadcasts quest 8 and
            // finished=false to both clients.
            Complete(2);
            Require(Terraria.Main.anglerQuest == 8, "all-player completion did not immediately advance the quest");
            Require(Terraria.Main.questSwapCount == 1, "all-player completion did not perform exactly one swap");
            Require(Terraria.Main.anglerWhoFinishedToday.Count == 0,
                "new shared round did not clear the completion list");
            Require(Terraria.NetMessage.clientQuest[1] == 8 && Terraria.NetMessage.clientQuest[2] == 8,
                "new shared quest was not broadcast to both vanilla clients");
            Require(!Terraria.NetMessage.clientQuestFinished[1] && !Terraria.NetMessage.clientQuestFinished[2],
                "new shared round did not unlock both vanilla clients");

            // A fully connected late joiner counts immediately. Existing players can
            // finish and must remain locked out until the late joiner also finishes.
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
            Require(!Terraria.NetMessage.clientQuestFinished[1] &&
                    !Terraria.NetMessage.clientQuestFinished[2] &&
                    !Terraria.NetMessage.clientQuestFinished[3],
                "new round did not unlock every connected vanilla client");

            // Connection state, not Player.active, defines the group. Leave the old
            // Player object active on purpose, disconnect its Netplay slot, and verify
            // the tick fallback advances once only the two real clients remain.
            Complete(1);
            Complete(2);
            Require(Terraria.Main.anglerQuest == 9, "round advanced while third client was still connected");

            Disconnect(3, leavePlayerActive: true);
            Tick();
            Require(Terraria.Main.anglerQuest == 10, "disconnect did not release the blocked round");
            Require(Terraria.Main.questSwapCount == 3, "disconnect-triggered round swapped incorrectly");
            Require(Terraria.Main.anglerWhoFinishedToday.Count == 0,
                "disconnect-triggered round did not start cleanly");

            // With only one fully connected client, that one completion is the whole
            // group even if stale Player.active objects remain in other slots.
            Disconnect(2, leavePlayerActive: true);
            Complete(1);
            Require(Terraria.Main.anglerQuest == 11, "single connected client did not advance the shared quest");
            Require(Terraria.Main.questSwapCount == 4, "single-client round swapped incorrectly");
            Require(Terraria.Main.anglerWhoFinishedToday.Count == 0,
                "single-client round did not clear completion state");
            Require(Terraria.NetMessage.clientQuest[1] == 11 && !Terraria.NetMessage.clientQuestFinished[1],
                "single connected vanilla client did not receive the next quest unlocked");

            Console.WriteLine(
                "PASS: vanilla clients stay locked after individual turn-in, dawn cannot reset the round, the quest advances only after every Netplay-connected client finishes, joiners count, and disconnects stop counting.");
            return 0;
        }

        private static void ResetFixture()
        {
            Terraria.Main.netMode = 2;
            Terraria.Main.anglerQuest = 7;
            Terraria.Main.anglerQuestFinished = true;
            Terraria.Main.anglerWhoFinishedToday.Clear();
            Terraria.Main.questSwapCount = 0;
            Terraria.Main.triggerDawnReset = false;
            Terraria.NetMessage.sendAnglerQuestCount = 0;

            for (var index = 0; index < Terraria.Main.player.Length; index++)
            {
                Terraria.Main.player[index].active = false;
                Terraria.Main.player[index].name = string.Empty;
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
