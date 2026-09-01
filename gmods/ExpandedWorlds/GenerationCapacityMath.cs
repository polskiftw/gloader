using System;

/// <summary>
/// Non-behavioral storage bounds derived from audited vanilla worldgen control
/// flow. These values do not decide how much content generates; they only ensure
/// that vanilla scratch arrays cannot truncate or crash a mathematically valid
/// expanded generation.
/// </summary>
internal static class ExpandedWorldCapacityMath
{
    /// <summary>
    /// Terraria 1.4.5.8 Floating Islands starts with:
    ///   islands = floor(maxTilesX * 0.0008)
    /// Error World can triple that island count. Care Bears can then multiply
    /// both islands and the existing sky-lake count by either 2 or 10. Each
    /// outer-loop iteration can record at most one island/lake metadata entry.
    ///
    /// Expanded Worlds keeps Terraria's categorical world size at Large, so the
    /// unmodified Large sky-lake count is 3. This function accepts the base lake
    /// count explicitly so the source assumption stays visible and testable.
    /// </summary>
    public static int FloatingIslandRecordUpperBound(
        int width,
        int baseSkyLakes,
        bool errorWorldTriple,
        int careBearsMultiplier)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (baseSkyLakes < 0)
            throw new ArgumentOutOfRangeException(nameof(baseSkyLakes));
        if (careBearsMultiplier != 1 && careBearsMultiplier != 2 && careBearsMultiplier != 10)
            throw new ArgumentOutOfRangeException(nameof(careBearsMultiplier));

        int islands = (int)(width * 0.0008d);
        if (errorWorldTriple)
            islands = checked(islands * 3);

        return checked((islands + baseSkyLakes) * careBearsMultiplier);
    }

    /// <summary>
    /// Worst current 1.4.5.8 secret-seed combination for Floating Island record
    /// storage: Error World's x3 island count followed by Care Bears' full x10
    /// multiplier. Large-category worlds start with three sky lakes.
    /// </summary>
    public static int FloatingIslandScratchCapacity(int width)
    {
        return FloatingIslandRecordUpperBound(
            width,
            baseSkyLakes: 3,
            errorWorldTriple: true,
            careBearsMultiplier: 10);
    }

    /// <summary>
    /// Current Mountain Caves uses floor(maxTilesX * 0.001), then Remix multiplies
    /// the count by 1.5 and truncates. Successful placements are the only records.
    /// </summary>
    public static int MountainCaveRecordUpperBound(int width, bool remixWorld)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));

        int count = (int)(width * 0.001d);
        if (remixWorld)
            count = (int)(count * 1.5d);
        return count;
    }

    /// <summary>
    /// Current ordinary Lakes rolls Next((int)(3*scale), (int)(6*scale)), where
    /// scale = maxTilesX / 4200. Random.Next's upper bound is exclusive.
    /// This is an attempt bound; the pass also explicitly stops before its
    /// 50-slot LakeX buffer can overflow.
    /// </summary>
    public static int OrdinaryLakeAttemptUpperBound(int width)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));

        double scale = width / (double)ExpandedWorldMath.SmallWidth;
        int exclusiveMaximum = (int)(6d * scale);
        return Math.Max(0, exclusiveMaximum - 1);
    }

    /// <summary>
    /// Terraria 1.4.5.8 Corruption/Crimson starts from:
    ///   attempts = maxTilesX * 0.00045
    /// Remix doubles that value; Drunk then halves it. The source loop compares
    /// an integer index against the resulting double, so a positive fractional
    /// attempt count executes ceil(attempts) times.
    /// </summary>
    public static int CrimsonRegionAttemptUpperBound(int width, bool remixWorld, bool drunkWorld)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));

        double attempts = width * 0.00045d;
        if (remixWorld)
            attempts *= 2d;
        if (drunkWorld)
            attempts /= 2d;

        return (int)Math.Ceiling(attempts);
    }

    /// <summary>
    /// Every CrimStart rolls Next(5, 9) and calls CrimVein once per result. Each
    /// CrimVein appends exactly one position to WorldGen.heartPos with no bounds
    /// guard. Eight records per Crimson region is therefore the hard source bound.
    /// </summary>
    public static int CrimsonHeartRecordUpperBound(int width, bool remixWorld, bool drunkWorld)
    {
        return checked(CrimsonRegionAttemptUpperBound(width, remixWorld, drunkWorld) * 8);
    }

    /// <summary>
    /// Remix without Drunk is the maximum 1.4.5.8 Crimson-heart producer. Large
    /// and XL remain within vanilla's 100-slot heartPos array; Huge can require
    /// 128 records and therefore needs a non-behavioral scratch resize.
    /// </summary>
    public static int CrimsonHeartScratchCapacity(int width)
    {
        return CrimsonHeartRecordUpperBound(width, remixWorld: true, drunkWorld: false);
    }

    /// <summary>
    /// Historical pre-1.4.5 MakeDungeon initializes:
    ///   base = maxTilesX / 60
    ///   remaining = base + Next(0, base / 3)
    /// Random.Next's upper bound is exclusive.
    ///
    /// Terraria 1.4.5 uses dynamic per-DungeonData List&lt;T&gt; storage instead;
    /// these Dungeon bounds now exist only to support the legacy compatibility
    /// fallback in GenerationCapacity.cs.
    /// </summary>
    public static int DungeonMainLoopMaxIterations(int width)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));

        int baseIterations = width / 60;
        int randomExclusive = baseIterations / 3;
        int maximumRandomAddition = randomExclusive > 0 ? randomExclusive - 1 : 0;
        return baseIterations + maximumRandomAddition;
    }

    /// <summary>
    /// The legacy main loop has a five-iteration room-event cooldown. One initial
    /// room and one final room are recorded outside that loop.
    /// </summary>
    public static int DungeonMainRoomEventUpperBound(int width)
    {
        return DungeonMainLoopMaxIterations(width) / 5;
    }

    public static int DungeonMainRoomRecordUpperBound(int width)
    {
        return 2 + DungeonMainRoomEventUpperBound(width);
    }

    /// <summary>
    /// Legacy DungeonStairs starts with Y velocity -1 (or -2) and executes at
    /// least ten steps per completed call. Once above worldSurface it damps Y by
    /// 0.98 per step; over the minimum ten steps the continuous displacement
    /// remains >9, which truncates the next positive integer dungeonY by at least
    /// 10 tiles. Therefore ceil(worldHeight / 10) is a conservative hard bound.
    /// </summary>
    public static int DungeonEntranceStairCallUpperBound(int height)
    {
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));

        return (height + 9) / 10;
    }

    /// <summary>
    /// Legacy entrance-room cooldown starts at 5, then resets to 10 after every
    /// room. Maximum successful event indices are 5, 15, 25, ...
    /// </summary>
    public static int DungeonEntranceRoomEventUpperBound(int height)
    {
        int calls = DungeonEntranceStairCallUpperBound(height);
        if (calls < 5)
            return 0;

        return 1 + (calls - 5) / 10;
    }

    public static int DungeonRoomRecordUpperBound(int width, int height)
    {
        return DungeonMainRoomRecordUpperBound(width) +
               DungeonEntranceRoomEventUpperBound(height);
    }

    /// <summary>
    /// Every non-room legacy main-loop iteration calls DungeonHalls once. A
    /// room-event iteration can call DungeonHalls twice, so replacing an ordinary
    /// iteration with the maximum room branch adds at most one hall call.
    /// </summary>
    public static int DungeonHallCallUpperBound(int width, int height)
    {
        int mainIterations = DungeonMainLoopMaxIterations(width);
        int mainRoomEvents = DungeonMainRoomEventUpperBound(width);
        int entranceRoomEvents = DungeonEntranceRoomEventUpperBound(height);
        return mainIterations + mainRoomEvents + entranceRoomEvents;
    }

    /// <summary>
    /// Legacy DungeonHalls can record at most two candidate doors (start/end of a
    /// horizontal hall). The later room-boundary scan can record at most two
    /// additional candidates per room.
    /// </summary>
    public static int DungeonDoorRecordUpperBound(int width, int height)
    {
        return checked(
            2 * DungeonHallCallUpperBound(width, height) +
            2 * DungeonRoomRecordUpperBound(width, height));
    }

    /// <summary>
    /// The legacy room-boundary scan records at most one platform candidate at
    /// the top and one at the bottom of each room.
    /// </summary>
    public static int DungeonPlatformRecordUpperBound(int width, int height)
    {
        return checked(2 * DungeonRoomRecordUpperBound(width, height));
    }

    /// <summary>
    /// The audited makeTemple source computes:
    ///   tier = maxTilesX / 4200       (integer division)
    ///   roomCount = Next(10*tier, 16*tier)
    /// and historically stores those room rectangles in a fixed 40-slot array.
    /// The exclusive upper bound itself is therefore the exact scratch capacity
    /// required to represent every possible room-count roll for a width tier.
    /// </summary>
    public static int JungleTempleRoomScratchCapacity(int width)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));

        int tier = width / ExpandedWorldMath.SmallWidth;
        return Math.Max(40, checked(16 * tier));
    }

    public static IntRange JungleTempleRoomCountRange(int width)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));

        int tier = width / ExpandedWorldMath.SmallWidth;
        int minimum = 10 * tier;
        int maximumInclusive = 16 * tier - 1;
        return new IntRange(minimum, maximumInclusive);
    }
}
