using System;
using System.IO;
using AuthenticRaces.Core;
using Terraria;
using Terraria.IO;
using Terraria.Utilities;

internal static class Program
{
    private static int Main()
    {
        RaceRegistry.Initialize();
        RacePlayerState.ResetAll();

        if (RaceRegistry.Count != 1 || RaceRegistry.DefaultRace.Name != "Human")
            return Fail(1, "Human must remain the default proof race.");

        if (!RaceRegistry.TryGet("MrPlagueRaces/Human", out var upstreamHuman) || upstreamHuman.Id != 0)
            return Fail(2, "Upstream Human identity alias must resolve to race ID 0.");

        var player = new Player();
        if (RacePlayerState.GetRace(player).Name != "Human")
            return Fail(3, "New vanilla Player instances must resolve to the default Human race.");

        if (!RacePlayerState.TryRestoreRace(player, "MrPlagueRaces/Human"))
            return Fail(4, "Persisted upstream race identities must restore without a live race switch.");

        const string savedRace = "MrPlagueRaces/Human";
        byte[] encoded = RacePersistence.Encode(savedRace);
        if (!RacePersistence.TryDecode(encoded, out var decoded, out var decodeError) || decoded != savedRace)
            return Fail(5, "Race sidecar codec failed round-trip: " + decodeError);

        var corrupted = (byte[])encoded.Clone();
        corrupted[0] ^= 0x7F;
        if (RacePersistence.TryDecode(corrupted, out _, out _))
            return Fail(6, "Corrupted sidecar magic must be rejected.");

        var unsupported = (byte[])encoded.Clone();
        unsupported[5] = 99;
        if (RacePersistence.TryDecode(unsupported, out _, out _))
            return Fail(7, "Unknown sidecar schema versions must be rejected.");

        int storageResult = ExerciseStorage(player);
        if (storageResult != 0)
            return storageResult;

        Console.WriteLine("PASS: AuthenticRaces identity, .arplr codec, backups, cloud moves, and erase behavior are deterministic.");
        return 0;
    }

    private static int ExerciseStorage(Player player)
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
                return Fail(8, "Local race sidecar was not written.");

            // Saving again must preserve the previous valid sidecar as a backup.
            RacePersistence.Save(localFile);
            if (!File.Exists(localSidecarPath + ".bak"))
                return Fail(9, "Race sidecar backup was not created on replacement save.");

            var localReloadedPlayer = new Player();
            RacePersistence.Load(new PlayerFileData {
                Player = localReloadedPlayer,
                Path = localPlayerPath,
                IsCloudSave = false
            }, localPlayerPath, false);
            if (RacePlayerState.GetRace(localReloadedPlayer).Name != "Human")
                return Fail(10, "Local sidecar did not restore the saved race.");

            RacePersistence.MoveToCloud(localPlayerPath, cloudPlayerPath);
            if (File.Exists(localSidecarPath) || File.Exists(localSidecarPath + ".bak"))
                return Fail(11, "Moving to cloud must remove local race sidecars.");
            if (!FileUtilities.Exists(cloudSidecarPath, true) || !FileUtilities.Exists(cloudSidecarPath + ".bak", true))
                return Fail(12, "Moving to cloud must move both race sidecar and backup.");

            var cloudReloadedPlayer = new Player();
            RacePersistence.Load(new PlayerFileData {
                Player = cloudReloadedPlayer,
                Path = cloudPlayerPath,
                IsCloudSave = true
            }, cloudPlayerPath, true);
            if (RacePlayerState.GetRace(cloudReloadedPlayer).Name != "Human")
                return Fail(13, "Cloud sidecar did not restore the saved race.");

            RacePersistence.MoveToLocal(cloudPlayerPath, localPlayerPath);
            if (!File.Exists(localSidecarPath) || !File.Exists(localSidecarPath + ".bak"))
                return Fail(14, "Moving to local storage must restore race sidecar and backup.");
            if (FileUtilities.Exists(cloudSidecarPath, true) || FileUtilities.Exists(cloudSidecarPath + ".bak", true))
                return Fail(15, "Moving to local storage must remove cloud race sidecars.");

            RacePersistence.Erase(localPlayerPath, false);
            if (File.Exists(localSidecarPath) || File.Exists(localSidecarPath + ".bak"))
                return Fail(16, "Erasing a player must erase race sidecar and backup.");

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

    private static int Fail(int code, string message)
    {
        Console.Error.WriteLine(message);
        return code;
    }
}
