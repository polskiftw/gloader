using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GLoader
{
    internal sealed class LoaderOptions
    {
        public bool DisableMods { get; private set; }
        public bool DedicatedServer { get; private set; }
        public bool ShowHelp { get; private set; }
        public List<string> GameArguments { get; private set; } = new List<string>();

        public static LoaderOptions Parse(string root, string[] args)
        {
            var options = new LoaderOptions();
            var index = 0;

            while (index < args.Length)
            {
                var argument = args[index];

                if (argument == "--")
                {
                    index++;
                    break;
                }

                if (EqualsOption(argument, "--vanilla") || EqualsOption(argument, "--no-mods"))
                {
                    options.DisableMods = true;
                    index++;
                    continue;
                }

                if (EqualsOption(argument, "--server"))
                {
                    options.DedicatedServer = true;
                    index++;
                    continue;
                }

                if (EqualsOption(argument, "--help") || EqualsOption(argument, "-h"))
                {
                    options.ShowHelp = true;
                    index++;
                    continue;
                }

                break;
            }

            options.GameArguments.AddRange(args.Skip(index));

            if (options.GameArguments.Count > 0 &&
                LooksLikeSteamTerrariaCommand(root, options.GameArguments[0]))
            {
                options.GameArguments.RemoveAt(0);
            }

            return options;
        }

        public static void PrintHelp()
        {
            Console.WriteLine("gloader - native Linux/Mono Terraria source-mod loader");
            Console.WriteLine();
            Console.WriteLine("Steam launch option:");
            Console.WriteLine("  ./gloader %command%");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --vanilla, --no-mods   Launch without loading gmods");
            Console.WriteLine("  --server               Launch the Linux dedicated-server managed target");
            Console.WriteLine("  --help, -h             Show this help");
            Console.WriteLine("  --                     Pass all remaining arguments to Terraria");
        }

        private static bool EqualsOption(string value, string expected)
        {
            return string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
        }

        private static bool LooksLikeSteamTerrariaCommand(string root, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var trimmed = value.Trim().Trim('"');
            var fileName = Path.GetFileName(trimmed);

            if (string.IsNullOrWhiteSpace(fileName))
                return false;

            if (fileName.Equals("Terraria", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("Terraria.exe", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("Terraria.bin.x86_64", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("TerrariaServer", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("TerrariaServer.exe", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("TerrariaServer.bin.x86_64", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            try
            {
                if (Path.IsPathRooted(trimmed))
                    return File.Exists(trimmed) && fileName.StartsWith("Terraria", StringComparison.OrdinalIgnoreCase);

                var local = Path.Combine(root, trimmed);
                return File.Exists(local) && fileName.StartsWith("Terraria", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }
}
