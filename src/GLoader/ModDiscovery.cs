using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace GLoader
{
    internal static class ModDiscovery
    {
        public static IReadOnlyList<ModSource> Discover(string modsDirectory)
        {
            Directory.CreateDirectory(modsDirectory);

            var mods = new List<ModSource>();

            foreach (var directory in Directory
                .EnumerateDirectories(modsDirectory, "*", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                if (directory.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                    continue;

                var sources = Directory
                    .EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
                    .Where(path => !path.EndsWith(".disabled.cs", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (sources.Length == 0)
                    continue;

                var displayName = Path.GetFileName(directory);
                mods.Add(new ModSource(MakeId(displayName), displayName, sources, directory));
            }

            return mods;
        }

        private static string MakeId(string value)
        {
            var builder = new StringBuilder(value.Length);

            foreach (var character in value)
            {
                builder.Append(char.IsLetterOrDigit(character)
                    ? char.ToLowerInvariant(character)
                    : '.');
            }

            var id = builder.ToString().Trim('.');
            return string.IsNullOrWhiteSpace(id) ? "mod" : id;
        }
    }
}
