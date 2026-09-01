using System;

internal static class Program
{
    private static int _checks;

    private static int Main()
    {
        try
        {
            ValidateVanillaParity();
            ValidateAxisModel();
            ValidateExpandedTargets();
            ValidateStorageBounds();
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
        CheckCount("Life Crystals Small", 100, ExpandedWorldMath.LifeCrystals(4200, 1200));
        CheckCount("Life Crystals Medium", 230, ExpandedWorldMath.LifeCrystals(6400, 1800));
        CheckCount("Life Crystals Large", 403, ExpandedWorldMath.LifeCrystals(8400, 2400));

        CheckCount("Surface Chests Small", 21, ExpandedWorldMath.SurfaceChests(4200));
        CheckCount("Surface Chests Medium", 32, ExpandedWorldMath.SurfaceChests(6400));
        CheckCount("Surface Chests Large", 42, ExpandedWorldMath.SurfaceChests(8400));

        CheckCount("Floating Islands Small", 3, ExpandedWorldMath.FloatingIslands(4200));
        CheckCount("Floating Islands Medium", 5, ExpandedWorldMath.FloatingIslands(6400));
        CheckCount("Floating Islands Large", 6, ExpandedWorldMath.FloatingIslands(8400));

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

        CheckDouble("Drunk Hive Small", 1d, ExpandedWorldMath.DrunkHiveLinearScale(4200));
        CheckDouble("Drunk Hive Medium", 1d, ExpandedWorldMath.DrunkHiveLinearScale(6400));
        CheckDouble("Drunk Hive Large", 1.5d, ExpandedWorldMath.DrunkHiveLinearScale(8400));

        CheckCount("Underground Desert width Small", 320, ExpandedWorldMath.UndergroundDesertWidth(4200));
        CheckCount("Underground Desert width Medium", 484, ExpandedWorldMath.UndergroundDesertWidth(6400));
        CheckCount("Underground Desert width Large", 640, ExpandedWorldMath.UndergroundDesertWidth(8400));
    }

    private static void ValidateAxisModel()
    {
        CheckDouble("XL horizontal scale", 3d, ExpandedWorldMath.HorizontalScale(ExpandedWorldMath.XLWidth));
        CheckDouble("Huge horizontal scale", 4d, ExpandedWorldMath.HorizontalScale(ExpandedWorldMath.HugeWidth));
        CheckDouble("XL vertical scale", 2d, ExpandedWorldMath.VerticalScale(ExpandedWorldMath.XLHeight));
        CheckDouble("Huge vertical scale", 2d, ExpandedWorldMath.VerticalScale(ExpandedWorldMath.HugeHeight));
        CheckDouble("XL area scale", 6d, ExpandedWorldMath.AreaScale(ExpandedWorldMath.XLWidth, ExpandedWorldMath.XLHeight));
        CheckDouble("Huge area scale", 8d, ExpandedWorldMath.AreaScale(ExpandedWorldMath.HugeWidth, ExpandedWorldMath.HugeHeight));
        CheckDouble("XL isotropic relative to Large", Math.Sqrt(1.5d), ExpandedWorldMath.IsotropicLinearRelativeToLarge(ExpandedWorldMath.XLWidth, ExpandedWorldMath.XLHeight));
        CheckDouble("Huge isotropic relative to Large", Math.Sqrt(2d), ExpandedWorldMath.IsotropicLinearRelativeToLarge(ExpandedWorldMath.HugeWidth, ExpandedWorldMath.HugeHeight));

        CheckCount("Large Desert rows roll=0", 510, ExpandedWorldMath.UndergroundDesertBlockRows(0d, 2400));
        CheckCount("Large Desert rows roll=.5", 595, ExpandedWorldMath.UndergroundDesertBlockRows(0.5d, 2400));
        CheckCount("Large Remix Desert rows", 680, ExpandedWorldMath.UndergroundDesertBlockRowsRemix(2400));
        CheckCount("Large tenth-anniversary Desert Y offset", 40, ExpandedWorldMath.UndergroundDesertTenthAnniversaryYOffset(2400));
    }

    private static void ValidateExpandedTargets()
    {
        int xlW = ExpandedWorldMath.XLWidth;
        int xlH = ExpandedWorldMath.XLHeight;
        int hugeW = ExpandedWorldMath.HugeWidth;
        int hugeH = ExpandedWorldMath.HugeHeight;

        CheckCount("Life Crystals XL", 604, ExpandedWorldMath.LifeCrystals(xlW, xlH));
        CheckCount("Life Crystals Huge", 806, ExpandedWorldMath.LifeCrystals(hugeW, hugeH));
        CheckCount("Surface Chests XL", 63, ExpandedWorldMath.SurfaceChests(xlW));
        CheckCount("Surface Chests Huge", 84, ExpandedWorldMath.SurfaceChests(hugeW));
        CheckCount("Floating Islands XL", 10, ExpandedWorldMath.FloatingIslands(xlW));
        CheckCount("Floating Islands Huge", 13, ExpandedWorldMath.FloatingIslands(hugeW));
        CheckRange("Marble XL", 24, 48, ExpandedWorldMath.MarbleCaves(xlW, xlH));
        CheckRange("Marble Huge", 32, 64, ExpandedWorldMath.MarbleCaves(hugeW, hugeH));
        CheckRange("Granite XL", 12, 24, ExpandedWorldMath.GraniteCaves(xlW));
        CheckRange("Granite Huge", 16, 32, ExpandedWorldMath.GraniteCaves(hugeW));
        CheckRange("Underground Cabins XL", 210, 240, ExpandedWorldMath.UndergroundCabins(xlW, xlH));
        CheckRange("Underground Cabins Huge", 280, 320, ExpandedWorldMath.UndergroundCabins(hugeW, hugeH));
        CheckRange("Cave Chests XL", 210, 240, ExpandedWorldMath.CaveChests(xlW, xlH));
        CheckRange("Cave Chests Huge", 280, 320, ExpandedWorldMath.CaveChests(hugeW, hugeH));
        CheckRange("Dead Man's Chests XL", 30, 60, ExpandedWorldMath.DeadMansChests(xlW));
        CheckRange("Dead Man's Chests Huge", 40, 80, ExpandedWorldMath.DeadMansChests(hugeW));
        CheckCount("Extra Desert Cabins XL", 12, ExpandedWorldMath.AdditionalDesertCabins(xlW, xlH));
        CheckCount("Extra Desert Cabins Huge", 16, ExpandedWorldMath.AdditionalDesertCabins(hugeW, hugeH));
        CheckRange("Living Trees XL", 18, 33, ExpandedWorldMath.LivingTreeMicroBiomes(xlW));
        CheckRange("Living Trees Huge", 24, 44, ExpandedWorldMath.LivingTreeMicroBiomes(hugeW));
        CheckRange("Long Tracks XL", 3, 6, ExpandedWorldMath.LongMinecartTrackCount(xlW));
        CheckRange("Long Tracks Huge", 4, 8, ExpandedWorldMath.LongMinecartTrackCount(hugeW));
        CheckRange("Bee Hives XL", 16, 24, ExpandedWorldMath.BeeHives(xlW));
        CheckRange("Bee Hives Huge", 21, 32, ExpandedWorldMath.BeeHives(hugeW));
        CheckDouble("Drunk Hive XL", 2d, ExpandedWorldMath.DrunkHiveLinearScale(xlW));
        CheckDouble("Drunk Hive Huge", 2.5d, ExpandedWorldMath.DrunkHiveLinearScale(hugeW));
        CheckCount("Drunk larva upper bound XL", 48, ExpandedWorldMath.MaximumLarvaRecordsFromBeeHives(xlW, true));
        CheckCount("Drunk larva upper bound Huge", 64, ExpandedWorldMath.MaximumLarvaRecordsFromBeeHives(hugeW, true));
        CheckTrue("Huge larva fits vanilla 100-slot buffer", ExpandedWorldMath.MaximumLarvaRecordsFromBeeHives(hugeW, true) < 100);

        CheckCount("Underground Desert width XL", 960, ExpandedWorldMath.UndergroundDesertWidth(xlW));
        CheckCount("Underground Desert width Huge", 1280, ExpandedWorldMath.UndergroundDesertWidth(hugeW));
        CheckCount("Underground Desert rows XL roll=0", 510, ExpandedWorldMath.UndergroundDesertBlockRows(0d, xlH));
        CheckCount("Underground Desert rows Huge roll=0", 510, ExpandedWorldMath.UndergroundDesertBlockRows(0d, hugeH));
        CheckCount("Underground Desert rows XL roll=.5", 595, ExpandedWorldMath.UndergroundDesertBlockRows(0.5d, xlH));
        CheckCount("Underground Desert rows Huge roll=.5", 595, ExpandedWorldMath.UndergroundDesertBlockRows(0.5d, hugeH));
        CheckCount("Remix Underground Desert height XL", 1360, ExpandedWorldMath.UndergroundDesertHeightRemix(xlH));
        CheckCount("Remix Underground Desert height Huge", 1360, ExpandedWorldMath.UndergroundDesertHeightRemix(hugeH));
    }

    private static void ValidateStorageBounds()
    {
        int xlW = ExpandedWorldMath.XLWidth;
        int xlH = ExpandedWorldMath.XLHeight;
        int hugeW = ExpandedWorldMath.HugeWidth;
        int hugeH = ExpandedWorldMath.HugeHeight;

        CheckCount("XL Dungeon main-loop iterations", 279, ExpandedWorldCapacityMath.DungeonMainLoopMaxIterations(xlW));
        CheckCount("Huge Dungeon main-loop iterations", 372, ExpandedWorldCapacityMath.DungeonMainLoopMaxIterations(hugeW));
        CheckCount("XL Dungeon main room records", 57, ExpandedWorldCapacityMath.DungeonMainRoomRecordUpperBound(xlW));
        CheckCount("Huge Dungeon main room records", 76, ExpandedWorldCapacityMath.DungeonMainRoomRecordUpperBound(hugeW));
        CheckCount("Large-height Dungeon stair calls", 240, ExpandedWorldCapacityMath.DungeonEntranceStairCallUpperBound(2400));
        CheckCount("Large-height Dungeon entrance room events", 24, ExpandedWorldCapacityMath.DungeonEntranceRoomEventUpperBound(2400));
        CheckCount("XL Dungeon total room records", 81, ExpandedWorldCapacityMath.DungeonRoomRecordUpperBound(xlW, xlH));
        CheckCount("Huge Dungeon total room records", 100, ExpandedWorldCapacityMath.DungeonRoomRecordUpperBound(hugeW, hugeH));
        CheckCount("XL Dungeon hall calls", 358, ExpandedWorldCapacityMath.DungeonHallCallUpperBound(xlW, xlH));
        CheckCount("Huge Dungeon hall calls", 470, ExpandedWorldCapacityMath.DungeonHallCallUpperBound(hugeW, hugeH));
        CheckCount("XL Dungeon door scratch capacity", 878, ExpandedWorldCapacityMath.DungeonDoorRecordUpperBound(xlW, xlH));
        CheckCount("Huge Dungeon door scratch capacity", 1140, ExpandedWorldCapacityMath.DungeonDoorRecordUpperBound(hugeW, hugeH));
        CheckCount("XL Dungeon platform scratch capacity", 162, ExpandedWorldCapacityMath.DungeonPlatformRecordUpperBound(xlW, xlH));
        CheckCount("Huge Dungeon platform scratch capacity", 200, ExpandedWorldCapacityMath.DungeonPlatformRecordUpperBound(hugeW, hugeH));
        CheckTrue("Huge Dungeon rooms fit vanilla 100 slots", ExpandedWorldCapacityMath.DungeonRoomRecordUpperBound(hugeW, hugeH) <= 100);
        CheckTrue("XL Dungeon doors exceed current vanilla 500 slots", ExpandedWorldCapacityMath.DungeonDoorRecordUpperBound(xlW, xlH) > 500);
        CheckTrue("Huge Dungeon doors exceed current vanilla 500 slots", ExpandedWorldCapacityMath.DungeonDoorRecordUpperBound(hugeW, hugeH) > 500);
        CheckTrue("Huge Dungeon platforms fit current vanilla 500 slots", ExpandedWorldCapacityMath.DungeonPlatformRecordUpperBound(hugeW, hugeH) <= 500);

        CheckRange("Large Temple rooms", 20, 31, ExpandedWorldCapacityMath.JungleTempleRoomCountRange(8400));
        CheckRange("XL Temple rooms", 30, 47, ExpandedWorldCapacityMath.JungleTempleRoomCountRange(xlW));
        CheckRange("Huge Temple rooms", 40, 63, ExpandedWorldCapacityMath.JungleTempleRoomCountRange(hugeW));
        CheckCount("Large Temple scratch capacity", 40, ExpandedWorldCapacityMath.JungleTempleRoomScratchCapacity(8400));
        CheckCount("XL Temple scratch capacity", 48, ExpandedWorldCapacityMath.JungleTempleRoomScratchCapacity(xlW));
        CheckCount("Huge Temple scratch capacity", 64, ExpandedWorldCapacityMath.JungleTempleRoomScratchCapacity(hugeW));
    }

    private static void CheckCount(string name, int expected, int actual)
    {
        _checks++;
        if (actual != expected)
            throw new InvalidOperationException(name + ": expected " + expected + ", got " + actual + ".");
    }

    private static void CheckDouble(string name, double expected, double actual)
    {
        _checks++;
        if (Math.Abs(expected - actual) > 1e-12)
            throw new InvalidOperationException(name + ": expected " + expected + ", got " + actual + ".");
    }

    private static void CheckTrue(string name, bool actual)
    {
        _checks++;
        if (!actual)
            throw new InvalidOperationException(name + ": expected true.");
    }

    private static void CheckRange(string name, int minimum, int maximum, IntRange actual)
    {
        _checks++;
        var expected = new IntRange(minimum, maximum);
        if (!actual.Equals(expected))
            throw new InvalidOperationException(name + ": expected " + expected + ", got " + actual + ".");
    }
}
