using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Terraria.DataStructures
{
    public interface IEntitySource
    {
    }

    public sealed class EntitySource_Misc : IEntitySource
    {
    }
}

namespace Terraria.ID
{
    public static class MessageID
    {
        public const byte SyncTalkNPC = 40;
    }

    public static class NPCID
    {
        public const int Angler = 369;
    }
}

namespace Terraria
{
    public static class Main
    {
        public static int netMode = 2;
        public static int anglerQuest = 1;
        public static int[] anglerQuestItemNetIDs = { 0, 1337, 2442 };
        public static List<string> anglerWhoFinishedToday = new List<string>();
        public static Player[] player = Enumerable.Range(0, 8).Select(_ => new Player()).ToArray();
        public static NPC[] npc = Enumerable.Range(0, 8).Select(_ => new NPC()).ToArray();
    }

    public sealed class NPC
    {
        public int type;
    }

    public sealed class Item
    {
        public int type;
        public int stack;
        public int fishingPole;
    }

    public sealed class Player
    {
        public string name = string.Empty;
        public int talkNPC = -1;
        public Item[] inventory = Enumerable.Range(0, 58).Select(_ => new Item()).ToArray();
        public int selectedItem { get; set; }
        public int SpawnCount { get; private set; }
        public Terraria.DataStructures.IEntitySource LastSource { get; private set; }

        public bool HasItem(int itemType)
            => inventory.Any(item => item != null && item.type == itemType && item.stack > 0);

        public Terraria.DataStructures.IEntitySource GetSource_GiftOrReward(string context = null)
            => new Terraria.DataStructures.EntitySource_Misc();

        public void QuickSpawnItem(Terraria.DataStructures.IEntitySource source, int item, int stack = 1)
        {
            var slot = inventory.FirstOrDefault(candidate => candidate != null && candidate.type == 0);
            if (slot == null)
                throw new InvalidOperationException("Fixture inventory unexpectedly full.");

            slot.type = item;
            slot.stack = stack;
            SpawnCount++;
            LastSource = source;
        }
    }

    public sealed class MessageBuffer
    {
        public byte[] readBuffer = new byte[256];
        public int whoAmI;

        public void GetData(int start, int length, out int messageType)
        {
            messageType = readBuffer[start];
        }
    }
}

internal static class Program
{
    private static int Main(string[] args)
    {
        var modsDirectory = ReadModsDirectory(args);
        var anglerDirectory = Path.Combine(modsDirectory, "InfiniteAngler");
        Directory.CreateDirectory(anglerDirectory);
        File.WriteAllText(
            Path.Combine(anglerDirectory, "InfiniteAngler.ini"),
            "EnableDebugQuestFish=true" + Environment.NewLine +
            "DebugQuestFishPlayers=Allowed Player, Other Person" + Environment.NewLine);

        ResetWorld();

        var allowed = Terraria.Main.player[0];
        allowed.name = "allowed player";
        allowed.talkNPC = 0;
        allowed.selectedItem = 0;
        allowed.inventory[0].type = 2291;
        allowed.inventory[0].stack = 1;
        allowed.inventory[0].fishingPole = 20;
        Terraria.Main.npc[0].type = Terraria.ID.NPCID.Angler;

        var packet = new Terraria.MessageBuffer { whoAmI = 0 };
        packet.readBuffer[0] = Terraria.ID.MessageID.SyncTalkNPC;

        Require(InfiniteAnglerDebugQuestFishRuntime.IsTalkNpcPacket(packet, 0),
            "SyncTalkNPC was not recognized while the debug helper was enabled.");
        InfiniteAnglerDebugQuestFishRuntime.HandleTalkNpc(0);

        Require(allowed.HasItem(1337),
            "allowlisted character with a spaced name did not receive the current quest fish.");
        Require(allowed.SpawnCount == 1,
            "quest fish was not spawned exactly once.");
        Require(allowed.LastSource != null,
            "quest fish did not use Terraria's gift/reward item source.");

        InfiniteAnglerDebugQuestFishRuntime.HandleTalkNpc(0);
        Require(allowed.SpawnCount == 1,
            "talking to the Angler again duplicated a quest fish already in inventory.");

        var disallowed = Terraria.Main.player[1];
        PreparePlayer(disallowed, "Friend", fishingPole: 20);
        InfiniteAnglerDebugQuestFishRuntime.HandleTalkNpc(1);
        Require(disallowed.SpawnCount == 0,
            "non-allowlisted character received a debug quest fish.");

        var noRod = Terraria.Main.player[2];
        PreparePlayer(noRod, "Other Person", fishingPole: 0);
        InfiniteAnglerDebugQuestFishRuntime.HandleTalkNpc(2);
        Require(noRod.SpawnCount == 0,
            "allowlisted character received a debug quest fish without holding a fishing rod.");

        var finished = Terraria.Main.player[3];
        PreparePlayer(finished, "Other Person", fishingPole: 20);
        Terraria.Main.anglerWhoFinishedToday.Add("Other Person");
        InfiniteAnglerDebugQuestFishRuntime.HandleTalkNpc(3);
        Require(finished.SpawnCount == 0,
            "already-finished character received another debug quest fish.");

        packet.readBuffer[0] = 82;
        Require(!InfiniteAnglerDebugQuestFishRuntime.IsTalkNpcPacket(packet, 0),
            "non-talk packet was mistaken for SyncTalkNPC.");

        Console.WriteLine(
            "PASS: Infinite Angler debug quest-fish helper respects spaced/case-insensitive character allowlists, rod gating, current quest selection, completion gating, and duplicate suppression.");
        return 0;
    }

    private static void ResetWorld()
    {
        Terraria.Main.netMode = 2;
        Terraria.Main.anglerQuest = 1;
        Terraria.Main.anglerWhoFinishedToday.Clear();
        Terraria.Main.player = Enumerable.Range(0, 8).Select(_ => new Terraria.Player()).ToArray();
        Terraria.Main.npc = Enumerable.Range(0, 8).Select(_ => new Terraria.NPC()).ToArray();
    }

    private static void PreparePlayer(Terraria.Player player, string name, int fishingPole)
    {
        player.name = name;
        player.talkNPC = 0;
        player.selectedItem = 0;
        player.inventory[0].type = 2291;
        player.inventory[0].stack = 1;
        player.inventory[0].fishingPole = fishingPole;
        Terraria.Main.npc[0].type = Terraria.ID.NPCID.Angler;
    }

    private static string ReadModsDirectory(string[] args)
    {
        for (var index = 0; index + 1 < args.Length; index++)
        {
            if (string.Equals(args[index], "--mods", StringComparison.OrdinalIgnoreCase))
                return Path.GetFullPath(args[index + 1]);
        }

        throw new ArgumentException("Fixture requires --mods <directory>.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
