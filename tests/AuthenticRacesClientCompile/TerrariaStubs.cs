using System;
using System.Collections.Generic;
using System.IO;
using Terraria.IO;

namespace Microsoft.Xna.Framework.Graphics
{
    public class Texture2D
    {
    }
}

namespace ReLogic.Content
{
    public class Asset<T>
    {
        public Asset(T value)
        {
            Value = value;
        }

        public T Value { get; }
    }
}

namespace Terraria
{
    public class Player
    {
        public int hair;

        public void ResetEffects() { }
        public void Update(int whoAmI) { }

        public static void SavePlayer(PlayerFileData playerFile, bool skipMapSave = false) { }

        public static PlayerFileData LoadPlayer(string playerPath, bool cloudSave)
        {
            return new PlayerFileData {
                Player = new Player(),
                Path = playerPath,
                IsCloudSave = cloudSave
            };
        }
    }

    public class Main
    {
        public static readonly List<PlayerFileData> PlayerList = new List<PlayerFileData>();

        public static void ErasePlayer(int index)
        {
            if (index >= 0 && index < PlayerList.Count)
                PlayerList.RemoveAt(index);
        }
    }
}

namespace Terraria.DataStructures
{
    using Microsoft.Xna.Framework.Graphics;

    public struct DrawData
    {
        public Texture2D texture;

        public DrawData(Texture2D texture)
        {
            this.texture = texture;
        }
    }

    public struct PlayerDrawSet
    {
        public List<DrawData> DrawDataCache;
        public Terraria.Player drawPlayer;
        public int skinVar;
    }

    public static class PlayerDrawLayers
    {
        public static void DrawPlayer_RenderAllLayers(ref PlayerDrawSet drawinfo)
        {
        }
    }
}

namespace Terraria.GameContent
{
    using Microsoft.Xna.Framework.Graphics;
    using ReLogic.Content;

    public static class TextureAssets
    {
        public static readonly Asset<Texture2D>[,] Players = new Asset<Texture2D>[10, 16];
        public static readonly Asset<Texture2D>[] PlayerHair = new Asset<Texture2D>[165];
        public static readonly Asset<Texture2D>[] PlayerHairAlt = new Asset<Texture2D>[165];
    }
}

namespace Terraria.IO
{
    public class PlayerFileData
    {
        public Terraria.Player Player { get; set; }
        public string Path { get; set; }
        public bool IsCloudSave { get; set; }

        public void MoveToCloud() { }
        public void MoveToLocal() { }
    }
}

namespace Terraria.Utilities
{
    public static class FileUtilities
    {
        private static readonly Dictionary<string, byte[]> CloudFiles =
            new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        public static bool Exists(string path, bool cloudSave)
        {
            return cloudSave ? CloudFiles.ContainsKey(path) : File.Exists(path);
        }

        public static void Copy(string source, string destination, bool cloudSave)
        {
            if (cloudSave)
            {
                CloudFiles[destination] = (byte[])CloudFiles[source].Clone();
                return;
            }

            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destination));
            File.Copy(source, destination, overwrite: true);
        }

        public static void WriteAllBytes(string path, byte[] data, bool cloudSave)
        {
            if (cloudSave)
            {
                CloudFiles[path] = (byte[])data.Clone();
                return;
            }

            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            File.WriteAllBytes(path, data);
        }

        public static byte[] ReadAllBytes(string path, bool cloudSave)
        {
            return cloudSave ? (byte[])CloudFiles[path].Clone() : File.ReadAllBytes(path);
        }

        public static bool MoveToCloud(string localPath, string cloudPath)
        {
            if (!File.Exists(localPath))
                return false;

            CloudFiles[cloudPath] = File.ReadAllBytes(localPath);
            File.Delete(localPath);
            return true;
        }

        public static bool MoveToLocal(string cloudPath, string localPath)
        {
            if (!CloudFiles.TryGetValue(cloudPath, out var data))
                return false;

            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(localPath));
            File.WriteAllBytes(localPath, data);
            CloudFiles.Remove(cloudPath);
            return true;
        }

        public static void Delete(string path, bool cloudSave)
        {
            if (cloudSave)
                CloudFiles.Remove(path);
            else if (File.Exists(path))
                File.Delete(path);
        }
    }
}
