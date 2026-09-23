using System;
using System.IO;
using System.Reflection;

namespace GLoader
{
    internal static class TargetLocator
    {
        public static string Find(string root, bool dedicatedServer)
        {
            var candidates = dedicatedServer
                ? new[]
                {
                    Path.Combine(root, "TerrariaServer.exe"),
                    Path.Combine(root, "TerrariaServer", "TerrariaServer.exe")
                }
                : new[]
                {
                    Path.Combine(root, "Terraria.exe")
                };

            foreach (var candidate in candidates)
            {
                if (!File.Exists(candidate))
                    continue;

                try
                {
                    AssemblyName.GetAssemblyName(candidate);
                    return Path.GetFullPath(candidate);
                }
                catch (BadImageFormatException)
                {
                }
                catch (FileLoadException)
                {
                }
            }

            var expected = dedicatedServer ? "TerrariaServer.exe" : "Terraria.exe";
            throw new FileNotFoundException(
                "Could not find the native Linux Terraria managed target '" + expected +
                "' beneath the directory containing gloader. Linux gloader does not build or download a private Terraria runtime.");
        }
    }
}
