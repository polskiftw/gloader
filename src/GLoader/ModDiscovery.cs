using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace GLoader
{
    internal static class ModDiscovery
    {
        private const string IgnoreMarkerName = ".gloaderignore";

        public static IReadOnlyList<ModSource> Discover(string modsDirectory)
        {
            Directory.CreateDirectory(modsDirectory);

            var mods = new List<ModSource>();
            var directories = Directory
                .EnumerateDirectories(modsDirectory, "*", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            // General Radio supersedes VGMRadio. Package upgrades are commonly copied
            // over an existing Terraria folder, which does not delete the user's old
            // gmods/VGMRadio directory. If Radio is installed, ignore that leftover
            // legacy source folder so an overlay upgrade cannot start two audio clients.
            var hasGeneralRadio = directories.Any(directory =>
                !directory.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(Path.GetFileName(directory), "Radio", StringComparison.OrdinalIgnoreCase) &&
                Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories).Any(path => !IsDisabled(path, directory)));

            foreach (var directory in directories)
            {
                if (directory.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var displayName = Path.GetFileName(directory);
                if (hasGeneralRadio && string.Equals(displayName, "VGMRadio", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var sources = Directory
                    .EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
                    .Where(path => !IsDisabled(path, directory))
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (sources.Length == 0)
                {
                    continue;
                }

                mods.Add(new ModSource(
                    MakeId(displayName),
                    displayName,
                    sources));
            }

            return mods;
        }

        private static bool IsDisabled(string path, string modDirectory)
        {
            if (path.EndsWith(".disabled.cs", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var modRoot = Path.GetFullPath(modDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var current = Path.GetDirectoryName(Path.GetFullPath(path));

            while (!string.IsNullOrWhiteSpace(current))
            {
                if (File.Exists(Path.Combine(current, IgnoreMarkerName)))
                {
                    return true;
                }

                var normalized = current.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (string.Equals(normalized, modRoot, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                current = Path.GetDirectoryName(current);
            }

            return false;
        }

        private static string MakeId(string value)
        {
            var builder = new StringBuilder(value.Length);

            foreach (var character in value)
            {
                if (char.IsLetterOrDigit(character))
                {
                    builder.Append(char.ToLowerInvariant(character));
                }
                else
                {
                    builder.Append('.');
                }
            }

            var id = builder.ToString().Trim('.');
            return string.IsNullOrWhiteSpace(id) ? "mod" : id;
        }
    }
}
