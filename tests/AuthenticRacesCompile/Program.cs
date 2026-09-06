using System;
using System.IO;
using System.Text;
using AuthenticRaces.Core;
using HarmonyLib;
using Terraria;
using Terraria.IO;
using Terraria.Utilities;

internal static class Program
{
    private static int Main()
    {
        RaceRegistry.Initialize();
        RacePlayerState.ResetAll();
        RaceAppearanceState.ResetAll();

        try
        {
            new Harmony("gloader.tests.authentic-races").PatchAll(typeof(RaceRegistry).Assembly);
        }
        catch (Exception ex)
        {
            return Fail(1, "AuthenticRaces Harmony targets did not resolve: " + ex);
        }

        if (RaceRegistry.Count != 1 || RaceRegistry.DefaultRace.Name != "Human")
            return Fail(2, "Human must remain the default proof race.");

        if (!RaceRegistry.TryGet("MrPlagueRaces/Human", out var upstreamHuman) || upstreamHuman.Id != 0)
            return Fail(3, "Upstream Human identity alias must resolve to race ID 0.");

        var player = new Player();
        if (RacePlayerState.GetRace(player).Name != "Human")
            return Fail(4, "New vanilla Player instances must resolve to the default Human race.");

        if (!RacePlayerState.TryRestoreRace(player, "MrPlagueRaces/Human"))
            return Fail(5, "Persisted upstream race identities must restore without a live race switch.");

        var unresolvedPlayer = new Player();
        if (RacePlayerState.TryRestoreRace(unresolvedPlayer, "MrPlagueRaces/Tabaxi"))
            return Fail(6, "An unported race must not claim to have resolved successfully.");
        if (RacePlayerState.GetRace(unresolvedPlayer).Name != "Human" ||
            RacePlayerState.GetPersistedRaceName(unresolvedPlayer) != "MrPlagueRaces/Tabaxi")
            return Fail(7, "Unported race identities must survive while Human is used as the temporary fallback.");

        const string savedRace = "MrPlagueRaces/Human";
        var appearance = RaceAppearanceData.Default;
        appearance.DetailColor = new Rgb24(12, 34, 56);
        appearance.AuxiliaryDetailColor1 = new Rgb24(78, 90, 123);
        appearance.AuxiliaryDetailColor2 = new Rgb24(45, 67, 89);
        appearance.AuxiliaryDetailColor3 = new Rgb24(210, 111, 9);
        appearance.AuxiliaryHairstyle1 = 7;
        appearance.AuxiliaryHairstyle2 = 42;
        appearance.AuxiliaryHairstyle3 = 164;
        RaceAppearanceState.Set(player, appearance);

        byte[] encoded = RacePersistence.EncodePayload(new RaceSaveData(savedRace, appearance));
        if (!RacePersistence.TryDecodePayload(encoded, out var decodedPayload, out var decodeError) ||
            decodedPayload.RaceName != savedRace || !decodedPayload.Appearance.Equals(appearance))
            return Fail(8, "Race/appearance sidecar codec failed round-trip: " + decodeError);

        if (!RacePersistence.TryDecode(encoded, out var decodedRaceOnly, out decodeError) || decodedRaceOnly != savedRace)
            return Fail(9, "Race-only compatibility decoder failed on schema 2: " + decodeError);

        byte[] legacy = EncodeLegacyRaceOnly(savedRace);
        if (!RacePersistence.TryDecodePayload(legacy, out var legacyPayload, out var legacyError) ||
            legacyPayload.RaceName != savedRace || !legacyPayload.Appearance.Equals(RaceAppearanceData.Default))
            return Fail(10, "Schema 1 race-only sidecars must migrate to default appearance: " + legacyError);

        var corrupted = (byte[])encoded.Clone();
        corrupted[0] ^= 0x7F;
        if (RacePersistence.TryDecode(corrupted, out _, out _))
            return Fail(11, "Corrupted sidecar magic must be rejected.");

        var unsupported = (byte[])encoded.Clone();
        unsupported[5] = 99;
        if (RacePersistence.TryDecode(unsupported, out _, out _))
            return Fail(12, "Unknown sidecar schema versions must be rejected.");

        int storageResult = ExerciseStorage(player, appearance);
        if (storageResult != 0)
            return storageResult;

        Console.WriteLine("PASS: AuthenticRaces Harmony targets, identity preservation, schema migration, custom appearance, backups, cloud moves, and erase behavior are deterministic.");
        return 0;
    }

    private static int ExerciseStorage(Player player, RaceAppearanceData expectedAppearance)
    {
        string root = Path.Combine(Path.GetTempPath(), "authentic-races-" + Guid.NewGuid().ToString("N"));
        string localPlayerPath = Path.Combine(root, "Alice.plr");
        string localSidecarPath = Path.ChangeExtension(localPlayerPath, ".arplr");
        string cloudPlayerPath = "Players/Alice.plr";
        string cloudSidecarPath = Path.ChangeExtension(cloudPlayerPath, ".arplr");

        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllBytes(localPlayerPath, new byte[] { 1 });

            var localFile = new PlayerFileData {
                Player = player,
                Path = localPlayerPath,
                IsCloudSave = false
            };

            RacePersistence.Save(localFile);
            if (!File.Exists(localSidecarPath))
                return Fail(13, "Local race sidecar was not written.");

            // Saving again must preserve the previous valid sidecar as a backup.
            RacePersistence.Save(localFile);
            if (!File.Exists(localSidecarPath + ".bak"))
                return Fail(14, "Race sidecar backup was not created on replacement save.");

            var localReloadedPlayer = new Player();
            RacePersistence.Load(new PlayerFileData {
                Player = localReloadedPlayer,
                Path = localPlayerPath,
                IsCloudSave = false
            }, localPlayerPath, false);
            if (RacePlayerState.GetRace(localReloadedPlayer).Name != "Human")
                return Fail(15, "Local sidecar did not restore the saved race.");
            if (!RaceAppearanceState.Get(localReloadedPlayer).Equals(expectedAppearance))
                return Fail(16, "Local sidecar did not restore custom appearance state.");

            RacePersistence.MoveToCloud(localPlayerPath, cloudPlayerPath);
            if (File.Exists(localSidecarPath) || File.Exists(localSidecarPath + ".bak"))
                return Fail(17, "Moving to cloud must remove local race sidecars.");
            if (!FileUtilities.Exists(cloudSidecarPath, true) || !FileUtilities.Exists(cloudSidecarPath + ".bak", true))
                return Fail(18, "Moving to cloud must move both race sidecar and backup.");

            var cloudReloadedPlayer = new Player();
            RacePersistence.Load(new PlayerFileData {
                Player = cloudReloadedPlayer,
                Path = cloudPlayerPath,
                IsCloudSave = true
            }, cloudPlayerPath, true);
            if (RacePlayerState.GetRace(cloudReloadedPlayer).Name != "Human")
                return Fail(19, "Cloud sidecar did not restore the saved race.");
            if (!RaceAppearanceState.Get(cloudReloadedPlayer).Equals(expectedAppearance))
                return Fail(20, "Cloud sidecar did not restore custom appearance state.");

            RacePersistence.MoveToLocal(cloudPlayerPath, localPlayerPath);
            if (!File.Exists(localSidecarPath) || !File.Exists(localSidecarPath + ".bak"))
                return Fail(21, "Moving to local storage must restore race sidecar and backup.");
            if (FileUtilities.Exists(cloudSidecarPath, true) || FileUtilities.Exists(cloudSidecarPath + ".bak", true))
                return Fail(22, "Moving to local storage must remove cloud race sidecars.");

            RacePersistence.Erase(localPlayerPath, false);
            if (File.Exists(localSidecarPath) || File.Exists(localSidecarPath + ".bak"))
                return Fail(23, "Erasing a player must erase race sidecar and backup.");

            return 0;
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch
            {
            }
        }
    }

    private static byte[] EncodeLegacyRaceOnly(string raceName)
    {
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream, Encoding.UTF8))
        {
            writer.Write(Encoding.ASCII.GetBytes("ARPLR"));
            writer.Write((byte)1);
            writer.Write(raceName);
            writer.Flush();
            return stream.ToArray();
        }
    }

    private static int Fail(int code, string message)
    {
        Console.Error.WriteLine(message);
        return code;
    }
}
