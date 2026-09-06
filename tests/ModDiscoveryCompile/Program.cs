using System;
using System.IO;
using System.Linq;

internal static class Program
{
    private static int Main()
    {
        var root = Path.Combine(Path.GetTempPath(), "gloader-mod-discovery-" + Guid.NewGuid().ToString("N"));
        try
        {
            WriteMod(root, "Radio");
            WriteMod(root, "VGMRadio");
            WriteMod(root, "OtherMod");

            var withRadio = GLoader.ModDiscovery.Discover(root);
            if (withRadio.Any(mod => string.Equals(mod.DisplayName, "VGMRadio", StringComparison.OrdinalIgnoreCase)))
            {
                Console.Error.WriteLine("Legacy VGMRadio must be suppressed when Radio is installed.");
                return 1;
            }
            if (!withRadio.Any(mod => string.Equals(mod.DisplayName, "Radio", StringComparison.OrdinalIgnoreCase)) ||
                !withRadio.Any(mod => string.Equals(mod.DisplayName, "OtherMod", StringComparison.OrdinalIgnoreCase)))
            {
                Console.Error.WriteLine("Radio suppression rule hid an unrelated mod.");
                return 2;
            }

            Directory.Delete(Path.Combine(root, "Radio"), true);
            var withoutRadio = GLoader.ModDiscovery.Discover(root);
            if (!withoutRadio.Any(mod => string.Equals(mod.DisplayName, "VGMRadio", StringComparison.OrdinalIgnoreCase)))
            {
                Console.Error.WriteLine("VGMRadio should remain discoverable on an old install until Radio is present.");
                return 3;
            }

            Console.WriteLine("PASS: General Radio suppresses a leftover VGMRadio folder only when its replacement is installed.");
            return 0;
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
        }
    }

    private static void WriteMod(string root, string name)
    {
        var directory = Path.Combine(root, name);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "Main.cs"), "public static class Mod { public static void Load() { } }");
    }
}
