using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace FixtureClient
{
    internal static class Program
    {
        public static int Main(string[] args)
        {
            var singlePlayerResult = Terraria.Main.VerifyInfiniteAnglerSinglePlayer();
            if (singlePlayerResult != 0)
                return singlePlayerResult;

            Terraria.Main.netMode = 1;
            return Terraria.Main.LaunchHostAndPlay();
        }
    }
}

namespace Terraria
{
    public static class Program
    {
        public static string SavePath;
    }

    public static class Main
    {
        // Mirrors vanilla Terraria's startup dependency: Main's static initializer
        // needs Program.SavePath to already be valid before mods touch Main.
        private static readonly string FavoritePath = Path.Combine(Program.SavePath, "favorites.json");

        public static int netMode;
        public static int anglerQuest = 7;
        public static bool anglerQuestFinished = true;
        public static List<string> anglerWhoFinishedToday = new List<string>();
        public static Player[] player = new[] { new Player(), new Player(), new Player(), new Player() };
        public static bool triggerDawnReset;
        public static int questSwapCount;

        public static int VerifyInfiniteAnglerSinglePlayer()
        {
            if (string.IsNullOrWhiteSpace(Program.SavePath) || string.IsNullOrWhiteSpace(FavoritePath))
            {
                Console.Error.WriteLine("Fixture SavePath was not initialized before Terraria.Main.");
                return 90;
            }

            netMode = 0;
            anglerQuest = 7;
            anglerQuestFinished = true;
            anglerWhoFinishedToday.Clear();
            questSwapCount = 0;
            triggerDawnReset = false;

            foreach (var entry in player)
            {
                entry.active = false;
                entry.name = string.Empty;
            }

            player[0].active = true;
            player[0].name = "SoloPlayer";

            // Dawn must not roll the quest in single-player anymore.
            triggerDawnReset = true;
            UpdateTime();
            if (anglerQuest != 7 || questSwapCount != 0)
            {
                Console.Error.WriteLine("Infinite Angler single-player dawn suppression failed.");
                return 93;
            }

            // Once the only active player completes the current quest, the next
            // time tick should immediately start the next normal Angler quest.
            anglerWhoFinishedToday.Add("SoloPlayer");
            UpdateTime();
            if (anglerQuest != 8 || questSwapCount != 1 || anglerWhoFinishedToday.Count != 0)
            {
                Console.Error.WriteLine("Infinite Angler single-player quest advance failed.");
                return 94;
            }

            Console.WriteLine("PASS: Infinite Angler works in single-player.");
            return 0;
        }

        // Synthetic vanilla dawn behavior. Infinite Angler replaces only these two
        // operations and leaves the rest of UpdateTime intact.
        public static void UpdateTime()
        {
            if (triggerDawnReset)
            {
                triggerDawnReset = false;
                anglerWhoFinishedToday.Clear();
                AnglerQuestSwap();
            }
        }

        public static void AnglerQuestSwap()
        {
            anglerQuestFinished = false;
            anglerQuest = (anglerQuest + 1) % 40;
            questSwapCount++;
        }

        public static int LaunchHostAndPlay()
        {
            var gameDirectory = Environment.CurrentDirectory;
            var server = Path.Combine(gameDirectory, "TerrariaServer.exe");
            if (!File.Exists(server))
            {
                Console.Error.WriteLine("Fixture server missing: " + server);
                return 91;
            }

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "TerrariaServer.exe",
                    WorkingDirectory = gameDirectory,
                    Arguments = "--fixture-arg \"hello world\"",
                    UseShellExecute = false
                }
            };

            Console.WriteLine("[fixture client] launching TerrariaServer.exe through Terraria Host & Play path...");
            if (!process.Start())
            {
                Console.Error.WriteLine("Process.Start returned false.");
                return 92;
            }

            process.WaitForExit();
            Console.WriteLine("[fixture client] child exit code: " + process.ExitCode);
            return process.ExitCode;
        }
    }

    public sealed class Player
    {
        public bool active;
        public string name = string.Empty;
    }
}
