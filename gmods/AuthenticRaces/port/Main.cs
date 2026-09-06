#if GLOADER
using System;
using AuthenticRaces.Core;
#if GLOADER_CLIENT
using AuthenticRaces.Rendering;
#endif

public static class Mod
{
    public static void Load()
    {
        RaceRegistry.Initialize();
        RacePlayerState.ResetAll();
        RaceAppearanceState.ResetAll();
#if GLOADER_CLIENT
        RaceRendererRegistry.Initialize();
#endif

        Console.WriteLine(
            "[Authentic Races] Core loaded. Registered " + RaceRegistry.Count +
            " race(s); default = " + RaceRegistry.DefaultRace.Name + ".");
    }
}
#endif
