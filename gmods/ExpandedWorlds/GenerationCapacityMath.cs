using System;

/// <summary>
/// Non-behavioral storage bounds derived from the audited vanilla Dungeon
/// control flow. These values do not decide how much Dungeon content generates;
/// they only guarantee that vanilla's scratch arrays cannot truncate or crash a
/// mathematically valid expanded generation.
/// </summary>
internal static class ExpandedWorldCapacityMath
{
    /// <summary>
    /// MakeDungeon initializes:
    ///   base = maxTilesX / 60
    ///   remaining = base + Next(0, base / 3)
    /// Random.Next's upper bound is exclusive.
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
    /// The main loop has a five-iteration room-event cooldown. One initial room
    /// and one final room are recorded outside that loop.
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
    /// DungeonStairs starts with Y velocity -1 (or -2) and executes at least ten
    /// steps per completed call. Once above worldSurface it damps Y by 0.98 per
    /// step; over the minimum ten steps the continuous displacement remains >9,
    /// which truncates the next positive integer dungeonY by at least 10 tiles.
    /// A call that reaches the surface can only terminate the entrance loop
    /// earlier. Therefore ceil(worldHeight / 10) is a conservative hard bound on
    /// completed non-terminating stair calls while dungeonY remains in-world.
    /// </summary>
    public static int DungeonEntranceStairCallUpperBound(int height)
    {
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));

        return (height + 9) / 10;
    }

    /// <summary>
    /// Entrance-room cooldown starts at 5, then resets to 10 after every room.
    /// Maximum successful event indices are 5, 15, 25, ...
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
    /// Every non-room main-loop iteration calls DungeonHalls once. A room-event
    /// iteration can call DungeonHalls twice, so replacing an ordinary iteration
    /// with the maximum room branch adds at most one hall call. Entrance room
    /// events add one forced-X hall each.
    /// </summary>
    public static int DungeonHallCallUpperBound(int width, int height)
    {
        int mainIterations = DungeonMainLoopMaxIterations(width);
        int mainRoomEvents = DungeonMainRoomEventUpperBound(width);
        int entranceRoomEvents = DungeonEntranceRoomEventUpperBound(height);
        return mainIterations + mainRoomEvents + entranceRoomEvents;
    }

    /// <summary>
    /// DungeonHalls can record at most two candidate doors (start/end of a
    /// horizontal hall). The later room-boundary scan can record at most two
    /// additional candidates per room. This deliberately over-approximates
    /// mutually exclusive orientations; storage may be larger than actual use,
    /// but output and RNG are unchanged.
    /// </summary>
    public static int DungeonDoorRecordUpperBound(int width, int height)
    {
        return checked(
            2 * DungeonHallCallUpperBound(width, height) +
            2 * DungeonRoomRecordUpperBound(width, height));
    }

    /// <summary>
    /// The room-boundary scan records at most one platform candidate at the top
    /// and one at the bottom of each room.
    /// </summary>
    public static int DungeonPlatformRecordUpperBound(int width, int height)
    {
        return checked(2 * DungeonRoomRecordUpperBound(width, height));
    }
}
