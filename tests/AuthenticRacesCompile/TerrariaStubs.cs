using System;
using System.Collections.Generic;
using System.IO;

namespace Terraria
{
    public class Player
    {
    }

    public class Main
    {
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
