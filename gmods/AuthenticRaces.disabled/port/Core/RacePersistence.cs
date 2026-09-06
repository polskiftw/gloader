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
        private const byte CurrentSchemaVersion = 2;
        private const byte RaceOnlySchemaVersion = 1;
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
                var saveData = new RaceSaveData(
                    RacePlayerState.GetPersistedRaceName(playerFile.Player),
                    RaceAppearanceState.Get(playerFile.Player));
                byte[] data = EncodePayload(saveData);

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
            RaceAppearanceState.RestoreDefault(playerFile.Player);

            if (string.IsNullOrWhiteSpace(playerPath))
                return;

            try
            {
                string path = GetSidecarPath(playerPath);
                string backupPath = path + ".bak";

                if (TryLoadPath(path, cloudSave, out var saveData, out var primaryError))
                {
                    RestoreData(playerFile, saveData);
                    return;
                }

                if (TryLoadPath(backupPath, cloudSave, out saveData, out var backupError))
                {
                    RestoreData(playerFile, saveData);
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

        // Compatibility helper retained for the small regression fixture and any early port code.
        // New writes use schema 2 with default custom appearance values.
        internal static byte[] Encode(string raceName)
        {
            return EncodePayload(new RaceSaveData(raceName, RaceAppearanceData.Default));
        }

        internal static byte[] EncodePayload(RaceSaveData saveData)
        {
            if (string.IsNullOrWhiteSpace(saveData.RaceName))
                throw new ArgumentException("Race name cannot be empty.", nameof(saveData));

            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write(Magic);
                writer.Write(CurrentSchemaVersion);
                writer.Write(saveData.RaceName);
                WriteAppearance(writer, saveData.Appearance);
                writer.Flush();
                return stream.ToArray();
            }
        }

        internal static bool TryDecode(byte[] data, out string raceName, out string error)
        {
            if (TryDecodePayload(data, out var saveData, out error))
            {
                raceName = saveData.RaceName;
                return true;
            }

            raceName = null;
            return false;
        }

        internal static bool TryDecodePayload(byte[] data, out RaceSaveData saveData, out string error)
        {
            saveData = default;
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
                    if (version != RaceOnlySchemaVersion && version != CurrentSchemaVersion)
                    {
                        error = "Unsupported sidecar schema version " + version + ".";
                        return false;
                    }

                    string raceName = reader.ReadString();
                    if (string.IsNullOrWhiteSpace(raceName))
                    {
                        error = "Saved race name is empty.";
                        return false;
                    }

                    RaceAppearanceData appearance = RaceAppearanceData.Default;
                    if (version >= CurrentSchemaVersion)
                    {
                        if (!TryReadAppearance(reader, out appearance, out error))
                            return false;
                    }

                    saveData = new RaceSaveData(raceName, appearance);
                    return true;
                }
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                saveData = default;
                return false;
            }
        }

        private static bool TryLoadPath(string path, bool cloudSave, out RaceSaveData saveData, out string error)
        {
            saveData = default;
            error = null;

            if (!FileUtilities.Exists(path, cloudSave))
                return false;

            try
            {
                return TryDecodePayload(FileUtilities.ReadAllBytes(path, cloudSave), out saveData, out error);
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private static void RestoreData(PlayerFileData playerFile, RaceSaveData saveData)
        {
            RaceAppearanceState.Set(playerFile.Player, saveData.Appearance);

            if (RacePlayerState.TryRestoreRace(playerFile.Player, saveData.RaceName))
                return;

            Console.WriteLine(
                "[Authentic Races] Saved race '" + saveData.RaceName + "' is not available in this port yet; using " +
                RaceRegistry.DefaultRace.Name + " temporarily while preserving the saved identity.");
        }

        private static void WriteAppearance(BinaryWriter writer, RaceAppearanceData appearance)
        {
            WriteRgb(writer, appearance.DetailColor);
            WriteRgb(writer, appearance.AuxiliaryDetailColor1);
            WriteRgb(writer, appearance.AuxiliaryDetailColor2);
            WriteRgb(writer, appearance.AuxiliaryDetailColor3);
            writer.Write(appearance.AuxiliaryHairstyle1);
            writer.Write(appearance.AuxiliaryHairstyle2);
            writer.Write(appearance.AuxiliaryHairstyle3);
        }

        private static bool TryReadAppearance(BinaryReader reader, out RaceAppearanceData appearance, out string error)
        {
            appearance = RaceAppearanceData.Default;
            error = null;

            try
            {
                appearance.DetailColor = ReadRgb(reader);
                appearance.AuxiliaryDetailColor1 = ReadRgb(reader);
                appearance.AuxiliaryDetailColor2 = ReadRgb(reader);
                appearance.AuxiliaryDetailColor3 = ReadRgb(reader);
                appearance.AuxiliaryHairstyle1 = reader.ReadInt32();
                appearance.AuxiliaryHairstyle2 = reader.ReadInt32();
                appearance.AuxiliaryHairstyle3 = reader.ReadInt32();

                if (appearance.AuxiliaryHairstyle1 < 0 ||
                    appearance.AuxiliaryHairstyle2 < 0 ||
                    appearance.AuxiliaryHairstyle3 < 0)
                {
                    error = "Saved auxiliary hairstyle ID is negative.";
                    appearance = RaceAppearanceData.Default;
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = "Appearance data is truncated or invalid: " + ex.GetType().Name + ": " + ex.Message;
                appearance = RaceAppearanceData.Default;
                return false;
            }
        }

        private static void WriteRgb(BinaryWriter writer, Rgb24 color)
        {
            writer.Write(color.R);
            writer.Write(color.G);
            writer.Write(color.B);
        }

        private static Rgb24 ReadRgb(BinaryReader reader)
        {
            return new Rgb24(reader.ReadByte(), reader.ReadByte(), reader.ReadByte());
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
