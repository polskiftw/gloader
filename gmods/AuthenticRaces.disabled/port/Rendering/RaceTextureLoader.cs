#if GLOADER_CLIENT
using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace AuthenticRaces.Rendering
{
    /// <summary>
    /// Loads upstream race PNG sheets directly from disk without tModLoader ModContent.
    /// Initialization only discovers the asset root; Texture2D creation is lazy so the
    /// graphics device is guaranteed to exist by the time a renderer actually needs art.
    /// </summary>
    internal static class RaceTextureLoader
    {
        private const string ModDirectoryDataKey = "GLoader.ModDirectory";

        private static readonly object Gate = new object();
        private static readonly Dictionary<string, Texture2D> Cache =
            new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

        private static string _assetRoot;

        public static string AssetRoot => _assetRoot;

        public static void Initialize(string modDirectory = null)
        {
            var startingDirectory = modDirectory ??
                AppDomain.CurrentDomain.GetData(ModDirectoryDataKey) as string;

            lock (Gate)
            {
                Cache.Clear();
                _assetRoot = ResolveAssetRoot(startingDirectory);
            }
        }

        public static Texture2D GetRaceSheet(
            string raceName,
            bool female,
            string sheetPath)
        {
            ValidateRaceName(raceName);
            ValidateSheetPath(sheetPath);

            string assetRoot = _assetRoot;
            if (string.IsNullOrWhiteSpace(assetRoot))
            {
                throw new DirectoryNotFoundException(
                    "Authentic Races could not locate Assets/Textures/Players/Races. " +
                    "The staged source assets must remain beside port/ until the port is flattened.");
            }

            string relativeSheet = sheetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                ? sheetPath
                : sheetPath + ".png";

            string gender = female ? "Female" : "Male";
            string path = BuildContainedPath(assetRoot, raceName, gender, relativeSheet);

            // MrPlague's Race.GetRaceSheet falls back to the male sheet whenever a
            // female-specific sheet is absent. Preserve that behavior directly.
            if (female && !File.Exists(path))
            {
                path = BuildContainedPath(assetRoot, raceName, "Male", relativeSheet);
            }

            return LoadTexture(path);
        }

        private static Texture2D LoadTexture(string path)
        {
            lock (Gate)
            {
                if (Cache.TryGetValue(path, out var cached))
                    return cached;

                if (!File.Exists(path))
                    throw new FileNotFoundException("Authentic Races race sheet was not found.", path);

                if (Main.instance == null || Main.instance.GraphicsDevice == null)
                {
                    throw new InvalidOperationException(
                        "Authentic Races cannot create a race texture before Terraria's graphics device exists.");
                }

                Texture2D texture;
                using (var stream = File.OpenRead(path))
                {
                    texture = Texture2D.FromStream(Main.instance.GraphicsDevice, stream);
                }

                Cache.Add(path, texture);
                return texture;
            }
        }

        private static string ResolveAssetRoot(string startingDirectory)
        {
            if (string.IsNullOrWhiteSpace(startingDirectory))
                return null;

            string current;
            try
            {
                current = Path.GetFullPath(startingDirectory);
            }
            catch
            {
                return null;
            }

            while (!string.IsNullOrWhiteSpace(current))
            {
                string flattened = Path.Combine(
                    current,
                    "Assets",
                    "Textures",
                    "Players",
                    "Races");
                if (Directory.Exists(flattened))
                    return flattened;

                string staged = Path.Combine(
                    current,
                    "source",
                    "Assets",
                    "Textures",
                    "Players",
                    "Races");
                if (Directory.Exists(staged))
                    return staged;

                var parent = Directory.GetParent(current);
                current = parent?.FullName;
            }

            return null;
        }

        private static string BuildContainedPath(
            string assetRoot,
            string raceName,
            string gender,
            string relativeSheet)
        {
            string normalizedSheet = relativeSheet
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);

            string fullRoot = Path.GetFullPath(assetRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            string fullPath = Path.GetFullPath(Path.Combine(assetRoot, raceName, gender, normalizedSheet));

            if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Race sheet path escaped the Authentic Races asset root.");

            return fullPath;
        }

        private static void ValidateRaceName(string raceName)
        {
            if (string.IsNullOrWhiteSpace(raceName) ||
                raceName.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }) >= 0 ||
                raceName == "." ||
                raceName == "..")
            {
                throw new ArgumentException("Race asset name must be one directory name.", nameof(raceName));
            }
        }

        private static void ValidateSheetPath(string sheetPath)
        {
            if (string.IsNullOrWhiteSpace(sheetPath) || Path.IsPathRooted(sheetPath))
                throw new ArgumentException("Race sheet path must be relative.", nameof(sheetPath));
        }
    }
}
#endif
