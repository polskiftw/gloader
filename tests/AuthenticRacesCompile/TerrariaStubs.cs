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
        public static bool Exists(string path, bool cloudSave) => false;
        public static void Copy(string source, string destination, bool cloudSave) { }
        public static void WriteAllBytes(string path, byte[] data, bool cloudSave) { }
        public static byte[] ReadAllBytes(string path, bool cloudSave) => null;
        public static bool MoveToCloud(string localPath, string cloudPath) => true;
        public static bool MoveToLocal(string cloudPath, string localPath) => true;
        public static void Delete(string path, bool cloudSave) { }
    }
}
