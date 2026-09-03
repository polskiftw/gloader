using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace FixtureClient
{
    internal static class Program
    {
        public static int Main(string[] args)
        {
            if (args.Length >= 2 && args[0] == "--cwd-probe")
            {
                var expected = Path.GetFullPath(args[1])
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var actual = Path.GetFullPath(Environment.CurrentDirectory)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                Console.WriteLine("[fixture client] expected game root: " + expected);
                Console.WriteLine("[fixture client] actual game root:   " + actual);

                if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                {
                    Console.Error.WriteLine("Fixture game/runtime root split is wrong.");
                    return 93;
                }

                return 0;
            }

            return Terraria.Main.LaunchHostAndPlay();
        }
    }
}

namespace Terraria.Social
{
    public static class SocialAPI
    {
        public static readonly NetworkSocialModule Network = new NetworkSocialModule();
    }

    public sealed class NetworkSocialModule
    {
        public bool LaunchLocalServer(Process process, int mode)
        {
            // Mirrors Steam Terraria: Main hands the already-configured Process to
            // SocialAPI.Network and Process.Start happens down here, outside Main.
            return process.Start();
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

            Console.WriteLine("[fixture client] handing TerrariaServer.exe to SocialAPI.Network...");
            if (!Social.SocialAPI.Network.LaunchLocalServer(process, 0))
            {
                Console.Error.WriteLine("SocialAPI.Network.LaunchLocalServer returned false.");
                return 92;
            }

            process.WaitForExit();
            Console.WriteLine("[fixture client] child exit code: " + process.ExitCode);
            return process.ExitCode;
        }
    }
}
