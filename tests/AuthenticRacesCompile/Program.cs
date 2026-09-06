using System;
using AuthenticRaces.Core;
using Terraria;

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

        Console.WriteLine("PASS: AuthenticRaces core identity, restore path, and .arplr codec are deterministic.");
        return 0;
    }

    private static int Fail(int code, string message)
    {
        Console.Error.WriteLine(message);
        return code;
    }
}
