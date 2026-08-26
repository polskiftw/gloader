using System;
using System.Diagnostics;
using System.IO;

namespace FixtureClient
{
    internal static class Program
    {
        public static int Main(string[] args)
        {
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

        public static int LaunchHostAndPlay()
        {
            if (string.IsNullOrWhiteSpace(Program.SavePath) || string.IsNullOrWhiteSpace(FavoritePath))
            {
                Console.Error.WriteLine("Fixture SavePath was not initialized before Terraria.Main.");
                return 90;
            }

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
}
