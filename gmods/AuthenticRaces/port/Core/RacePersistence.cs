#if GLOADER
using System;
using System.IO;
using System.Text;
using Terraria.IO;
using Terraria.Utilities;

namespace AuthenticRaces.Core
{
    /// <summary>
    /// Small, loader-independent player sidecar. The vanilla .plr remains completely untouched.
    /// Terraria's FileUtilities is used so the same code works for local and Steam Cloud players.
    /// </summary>
    internal static class RacePersistence
    {
        private const string Extension = ".arplr";
        private const byte SchemaVersion = 1;
        private const int MaxSidecarBytes = 64 * 1024;
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("ARPLR");

        public static void Save(PlayerFileData playerFile)
        {
            if (playerFile?.Player == null || string.IsNullOrWhiteSpace(playerFile.Path))
                return;

            try
            {
                string path = GetSidecarPath(playerFile.Path);
                bool cloud = playerFile.IsCloudSave;
                byte[] data = Encode(RacePlayerState.GetRace(playerFile.Player).UpstreamFullName);

                if (FileUtilities.Exists(path, cloud))
                    FileUtilities.Copy(path, path + ".bak", cloud);

                FileUtilities.WriteAllBytes(path, data, cloud);
            }
            catch (Exception ex)
            {
                // Never turn a valid vanilla player save into a failed save because optional
                // race metadata could not be written.
                Console.WriteLine("[Authentic Races] Could not save race sidecar: " + ex.Message);
            }
        }

        public static void Load(PlayerFileData playerFile, string playerPath, bool cloudSave)
        {
            if (playerFile?.Player == null)
                return;

            RacePlayerState.RestoreDefaultRace(playerFile.Player);

            if (string.IsNullOrWhiteSpace(playerPath))
                return;

            try
            {
                string path = GetSidecarPath(playerPath);
                string backupPath = path + ".bak";

                if (TryLoadPath(path, cloudSave, out var raceName, out var primaryError))
                {
                    RestoreRace(playerFile, raceName);
                    return;
                }

                if (TryLoadPath(backupPath, cloudSave, out raceName, out var backupError))
                {
                    RestoreRace(playerFile, raceName);
                    if (!string.IsNullOrWhiteSpace(primaryError))
                        Console.WriteLine("[Authentic Races] Primary race sidecar was unreadable; loaded its backup instead. " + primaryError);
                    return;
                }

                if (!string.IsNullOrWhiteSpace(primaryError))
                {
                    Console.WriteLine(
                        "[Authentic Races] Race sidecar could not be read; using " +
                        RaceRegistry.DefaultRace.Name + ". " + primaryError +
                        (string.IsNullOrWhiteSpace(backupError) ? string.Empty : " Backup: " + backupError));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    "[Authentic Races] Could not load race sidecar; using " +
                    RaceRegistry.DefaultRace.Name + ". " + ex.Message);
            }
        }

        public static void MoveToCloud(string localPlayerPath, string cloudPlayerPath)
        {
            try
            {
                MoveLocalSidecarToCloud(GetSidecarPath(localPlayerPath), GetSidecarPath(cloudPlayerPath));
                MoveLocalSidecarToCloud(GetSidecarPath(localPlayerPath) + ".bak", GetSidecarPath(cloudPlayerPath) + ".bak");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Authentic Races] Could not move race sidecar to cloud storage: " + ex.Message);
            }
        }

        public static void MoveToLocal(string cloudPlayerPath, string localPlayerPath)
        {
            try
            {
                MoveCloudSidecarToLocal(GetSidecarPath(cloudPlayerPath), GetSidecarPath(localPlayerPath));
                MoveCloudSidecarToLocal(GetSidecarPath(cloudPlayerPath) + ".bak", GetSidecarPath(localPlayerPath) + ".bak");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Authentic Races] Could not move race sidecar to local storage: " + ex.Message);
            }
        }

        public static void Erase(string playerPath, bool cloudSave)
        {
            if (string.IsNullOrWhiteSpace(playerPath))
                return;

            try
            {
                string path = GetSidecarPath(playerPath);
                if (FileUtilities.Exists(path, cloudSave))
                    FileUtilities.Delete(path, cloudSave);
                if (FileUtilities.Exists(path + ".bak", cloudSave))
                    FileUtilities.Delete(path + ".bak", cloudSave);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Authentic Races] Could not erase race sidecar: " + ex.Message);
            }
        }

        internal static byte[] Encode(string raceName)
        {
            if (string.IsNullOrWhiteSpace(raceName))
                throw new ArgumentException("Race name cannot be empty.", nameof(raceName));

            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write(Magic);
                writer.Write(SchemaVersion);
                writer.Write(raceName);
                writer.Flush();
                return stream.ToArray();
            }
        }

        internal static bool TryDecode(byte[] data, out string raceName, out string error)
        {
            raceName = null;
            error = null;

            if (data == null || data.Length == 0)
            {
                error = "Sidecar is empty.";
                return false;
            }
            if (data.Length > MaxSidecarBytes)
            {
                error = "Sidecar is unexpectedly large.";
                return false;
            }

            try
            {
                using (var stream = new MemoryStream(data, writable: false))
                using (var reader = new BinaryReader(stream, Encoding.UTF8))
                {
                    var magic = reader.ReadBytes(Magic.Length);
                    if (magic.Length != Magic.Length)
                    {
                        error = "Sidecar header is truncated.";
                        return false;
                    }
                    for (int i = 0; i < Magic.Length; i++)
                    {
                        if (magic[i] != Magic[i])
                        {
                            error = "Sidecar header is not Authentic Races data.";
                            return false;
                        }
                    }

                    byte version = reader.ReadByte();
                    if (version != SchemaVersion)
                    {
                        error = "Unsupported sidecar schema version " + version + ".";
                        return false;
                    }

                    raceName = reader.ReadString();
                    if (string.IsNullOrWhiteSpace(raceName))
                    {
                        error = "Saved race name is empty.";
                        raceName = null;
                        return false;
                    }

                    return true;
                }
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                raceName = null;
                return false;
            }
        }

        private static bool TryLoadPath(string path, bool cloudSave, out string raceName, out string error)
        {
            raceName = null;
            error = null;

            if (!FileUtilities.Exists(path, cloudSave))
                return false;

            try
            {
                return TryDecode(FileUtilities.ReadAllBytes(path, cloudSave), out raceName, out error);
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private static void RestoreRace(PlayerFileData playerFile, string raceName)
        {
            if (RacePlayerState.TryRestoreRace(playerFile.Player, raceName))
                return;

            Console.WriteLine(
                "[Authentic Races] Saved race '" + raceName + "' is not available in this port yet; using " +
                RaceRegistry.DefaultRace.Name + ".");
        }

        private static string GetSidecarPath(string playerPath)
        {
            return Path.ChangeExtension(playerPath, Extension);
        }

        private static void MoveLocalSidecarToCloud(string localPath, string cloudPath)
        {
            if (File.Exists(localPath))
                FileUtilities.MoveToCloud(localPath, cloudPath);
        }

        private static void MoveCloudSidecarToLocal(string cloudPath, string localPath)
        {
            if (FileUtilities.Exists(cloudPath, true))
                FileUtilities.MoveToLocal(cloudPath, localPath);
        }
    }
}
#endif
