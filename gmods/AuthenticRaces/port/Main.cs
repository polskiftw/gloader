#if GLOADER
using System;
using AuthenticRaces.Core;

public static class Mod
{
    public static void Load()
    {
        RaceRegistry.Initialize();
        RacePlayerState.ResetAll();

        Console.WriteLine(
            "[Authentic Races] Core loaded. Registered " + RaceRegistry.Count +
            " race(s); default = " + RaceRegistry.DefaultRace.Name + ".");
    }
}
#endif
