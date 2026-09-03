using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace WorldFamilyRenderer;

internal static class RuntimeLocator
{
    public static string TryAutoDetect()
    {
        foreach (string candidate in EnumerateCandidates())
        {
            try
            {
                RuntimePaths.Validate(candidate);
                return candidate;
            }
            catch
            {
                // Keep probing.
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateCandidates()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void YieldCandidate(List<string> list, string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                string full = Path.GetFullPath(path);
                if (seen.Add(full)) list.Add(full);
            }
            catch
            {
                // Ignore malformed candidates.
            }
        }

        var candidates = new List<string>();

        YieldCandidate(candidates, SettingsStore.LoadTerrariaRoot());
        YieldCandidate(candidates, Environment.GetEnvironmentVariable("GLOADER_HOME"));

        string exeDir = AppContext.BaseDirectory;
        YieldCandidate(candidates, exeDir);
        DirectoryInfo cursor = new DirectoryInfo(exeDir);
        for (int i = 0; i < 5 && cursor != null; i++, cursor = cursor.Parent)
            YieldCandidate(candidates, cursor.FullName);

        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        YieldCandidate(candidates, Path.Combine(programFilesX86, "Steam", "steamapps", "common", "Terraria"));

        foreach (string steamRoot in GetSteamRoots())
        {
            YieldCandidate(candidates, Path.Combine(steamRoot, "steamapps", "common", "Terraria"));

            string libraryVdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(libraryVdf)) continue;

            try
            {
                string text = File.ReadAllText(libraryVdf);
                foreach (Match match in Regex.Matches(text, "\\\"path\\\"\\s+\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase))
                {
                    string library = match.Groups[1].Value.Replace("\\\\", "\\");
                    YieldCandidate(candidates, Path.Combine(library, "steamapps", "common", "Terraria"));
                }
            }
            catch
            {
                // A manual folder picker remains available.
            }
        }

        return candidates;
    }

    private static IEnumerable<string> GetSteamRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var probe in new[]
        {
            (RegistryHive.CurrentUser, @"Software\Valve\Steam", "SteamPath"),
            (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
            (RegistryHive.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath")
        })
        {
            try
            {
                using RegistryKey baseKey = RegistryKey.OpenBaseKey(probe.Item1, RegistryView.Default);
                using RegistryKey key = baseKey.OpenSubKey(probe.Item2);
                if (key?.GetValue(probe.Item3) is string path && Directory.Exists(path))
                    roots.Add(path);
            }
            catch
            {
                // Registry probing is best effort.
            }
        }

        return roots;
    }
}

internal static class SettingsStore
{
    private static string SettingsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "gloader", "WorldFamilyRenderer");

    private static string SettingsPath => Path.Combine(SettingsDirectory, "settings.txt");

    public static string LoadTerrariaRoot()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return null;
            string value = File.ReadAllText(SettingsPath).Trim();
            return Directory.Exists(value) ? value : null;
        }
        catch
        {
            return null;
        }
    }

    public static void SaveTerrariaRoot(string root)
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            File.WriteAllText(SettingsPath, root ?? string.Empty);
        }
        catch
        {
            // The tool still works without persisted settings.
        }
    }
}
