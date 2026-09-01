using System;

internal static class Program
{
    private static int _checks;

    private static int Main()
    {
        try
        {
            ValidateVanillaParity();
            ValidateExpandedTargets();
            Console.WriteLine("Expanded Worlds math regression: " + _checks + " checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Expanded Worlds math regression FAILED: " + ex.Message);
            return 1;
        }
    }

    private static void ValidateVanillaParity()
    {
        // These are the known Small / Medium / Large Terraria outputs. If a
        // proposed mathematical model cannot reproduce these, it is not allowed
        // to define XL / Huge behavior.
        CheckCount("Life Crystals Small", 100, ExpandedWorldMath.LifeCrystals(4200, 1200));
        CheckCount("Life Crystals Medium", 230, ExpandedWorldMath.LifeCrystals(6400, 1800));
        CheckCount("Life Crystals Large", 403, ExpandedWorldMath.LifeCrystals(8400, 2400));

        CheckCount("Surface Chests Small", 21, ExpandedWorldMath.SurfaceChests(4200));
        CheckCount("Surface Chests Medium", 32, ExpandedWorldMath.SurfaceChests(6400));
        CheckCount("Surface Chests Large", 42, ExpandedWorldMath.SurfaceChests(8400));

        CheckCount("Floating Islands Small", 3, ExpandedWorldMath.FloatingIslands(4200));
        CheckCount("Floating Islands Medium", 5, ExpandedWorldMath.FloatingIslands(6400));
        CheckCount("Floating Islands Large", 6, ExpandedWorldMath.FloatingIslands(8400));

        CheckCount("Floating Lakes Small", 1, ExpandedWorldMath.FloatingLakes(4200));
        CheckCount("Floating Lakes Medium", 2, ExpandedWorldMath.FloatingLakes(6400));
        CheckCount("Floating Lakes Large", 3, ExpandedWorldMath.FloatingLakes(8400));

        CheckRange("Marble Small", 4, 8, ExpandedWorldMath.MarbleCaves(4200, 1200));
        CheckRange("Marble Medium", 9, 18, ExpandedWorldMath.MarbleCaves(6400, 1800));
        CheckRange("Marble Large", 16, 32, ExpandedWorldMath.MarbleCaves(8400, 2400));

        CheckRange("Granite Small", 4, 8, ExpandedWorldMath.GraniteCaves(4200));
        CheckRange("Granite Medium", 6, 12, ExpandedWorldMath.GraniteCaves(6400));
        CheckRange("Granite Large", 8, 16, ExpandedWorldMath.GraniteCaves(8400));

        CheckRange("Underground Cabins Small", 35, 40, ExpandedWorldMath.UndergroundCabins(4200, 1200));
        CheckRange("Underground Cabins Medium", 80, 91, ExpandedWorldMath.UndergroundCabins(6400, 1800));
        CheckRange("Underground Cabins Large", 140, 160, ExpandedWorldMath.UndergroundCabins(8400, 2400));

        CheckRange("Cave Chests Small", 35, 40, ExpandedWorldMath.CaveChests(4200, 1200));
        CheckRange("Cave Chests Medium", 80, 91, ExpandedWorldMath.CaveChests(6400, 1800));
        CheckRange("Cave Chests Large", 140, 160, ExpandedWorldMath.CaveChests(8400, 2400));

        CheckRange("Dead Man's Chests Small", 10, 20, ExpandedWorldMath.DeadMansChests(4200));
        CheckRange("Dead Man's Chests Medium", 15, 30, ExpandedWorldMath.DeadMansChests(6400));
        CheckRange("Dead Man's Chests Large", 20, 40, ExpandedWorldMath.DeadMansChests(8400));

        CheckCount("Extra Desert Cabins Small", 2, ExpandedWorldMath.AdditionalDesertCabins(4200, 1200));
        CheckCount("Extra Desert Cabins Medium", 4, ExpandedWorldMath.AdditionalDesertCabins(6400, 1800));
        CheckCount("Extra Desert Cabins Large", 8, ExpandedWorldMath.AdditionalDesertCabins(8400, 2400));

        CheckRange("Living Trees Small", 6, 11, ExpandedWorldMath.LivingTreeMicroBiomes(4200));
        CheckRange("Living Trees Medium", 9, 16, ExpandedWorldMath.LivingTreeMicroBiomes(6400));
        CheckRange("Living Trees Large", 12, 22, ExpandedWorldMath.LivingTreeMicroBiomes(8400));

        CheckRange("Long Tracks Small", 1, 2, ExpandedWorldMath.LongMinecartTrackCount(4200));
        CheckRange("Long Tracks Medium", 1, 3, ExpandedWorldMath.LongMinecartTrackCount(6400));
        CheckRange("Long Tracks Large", 2, 4, ExpandedWorldMath.LongMinecartTrackCount(8400));

        CheckRange("Bee Hives Small", 6, 8, ExpandedWorldMath.BeeHives(4200));
        CheckRange("Bee Hives Medium", 8, 12, ExpandedWorldMath.BeeHives(6400));
        CheckRange("Bee Hives Large", 11, 16, ExpandedWorldMath.BeeHives(8400));
    }

    private static void ValidateExpandedTargets()
    {
        CheckCount("Life Crystals XL", 576, ExpandedWorldMath.LifeCrystals(12000, 2400));
        CheckCount("Life Crystals Huge", 806, ExpandedWorldMath.LifeCrystals(16800, 2400));

        CheckCount("Surface Chests XL", 60, ExpandedWorldMath.SurfaceChests(12000));
        CheckCount("Surface Chests Huge", 84, ExpandedWorldMath.SurfaceChests(16800));

        CheckCount("Floating Islands XL", 9, ExpandedWorldMath.FloatingIslands(12000));
        CheckCount("Floating Islands Huge", 13, ExpandedWorldMath.FloatingIslands(16800));

        CheckCount("Floating Lakes XL", 4, ExpandedWorldMath.FloatingLakes(12000));
        CheckCount("Floating Lakes Huge", 6, ExpandedWorldMath.FloatingLakes(16800));

        CheckRange("Marble XL", 22, 45, ExpandedWorldMath.MarbleCaves(12000, 2400));
        CheckRange("Marble Huge", 32, 64, ExpandedWorldMath.MarbleCaves(16800, 2400));

        CheckRange("Granite XL", 11, 22, ExpandedWorldMath.GraniteCaves(12000));
        CheckRange("Granite Huge", 16, 32, ExpandedWorldMath.GraniteCaves(16800));

        CheckRange("Underground Cabins XL", 200, 228, ExpandedWorldMath.UndergroundCabins(12000, 2400));
        CheckRange("Underground Cabins Huge", 280, 320, ExpandedWorldMath.UndergroundCabins(16800, 2400));

        CheckRange("Cave Chests XL", 200, 228, ExpandedWorldMath.CaveChests(12000, 2400));
        CheckRange("Cave Chests Huge", 280, 320, ExpandedWorldMath.CaveChests(16800, 2400));

        CheckRange("Dead Man's Chests XL", 28, 57, ExpandedWorldMath.DeadMansChests(12000));
        CheckRange("Dead Man's Chests Huge", 40, 80, ExpandedWorldMath.DeadMansChests(16800));

        CheckCount("Extra Desert Cabins XL", 11, ExpandedWorldMath.AdditionalDesertCabins(12000, 2400));
        CheckCount("Extra Desert Cabins Huge", 16, ExpandedWorldMath.AdditionalDesertCabins(16800, 2400));

        CheckRange("Living Trees XL", 17, 31, ExpandedWorldMath.LivingTreeMicroBiomes(12000));
        CheckRange("Living Trees Huge", 24, 44, ExpandedWorldMath.LivingTreeMicroBiomes(16800));

        CheckRange("Long Tracks XL", 2, 5, ExpandedWorldMath.LongMinecartTrackCount(12000));
        CheckRange("Long Tracks Huge", 4, 8, ExpandedWorldMath.LongMinecartTrackCount(16800));

        CheckRange("Bee Hives XL", 15, 22, ExpandedWorldMath.BeeHives(12000));
        CheckRange("Bee Hives Huge", 21, 32, ExpandedWorldMath.BeeHives(16800));
    }

    private static void CheckCount(string name, int expected, int actual)
    {
        _checks++;
        if (actual != expected)
            throw new InvalidOperationException(name + ": expected " + expected + ", got " + actual + ".");
    }

    private static void CheckRange(string name, int minimum, int maximum, IntRange actual)
    {
        _checks++;
        var expected = new IntRange(minimum, maximum);
        if (!actual.Equals(expected))
            throw new InvalidOperationException(name + ": expected " + expected + ", got " + actual + ".");
    }
}
