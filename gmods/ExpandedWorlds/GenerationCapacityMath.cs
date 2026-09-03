using System;

/// <summary>
/// Exact non-behavioral scratch-storage bounds from Terraria 1.4.5.8 source.
/// These helpers never decide how much content Terraria generates. They only
/// size fixed record arrays that valid expanded generation can exceed.
/// </summary>
internal static class ExpandedWorldCapacityMath
{
    /// <summary>
    /// Floating Islands starts with floor(maxTilesX * 0.0008). Error World can
    /// triple that value. Care Bears can then multiply islands plus sky lakes.
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

    public static int FloatingIslandScratchCapacity(int width, int oneBasedWorldTier)
    {
        return FloatingIslandRecordUpperBound(
            width,
            ExpandedWorldTierMath.SkyLakeBaseCount(oneBasedWorldTier),
            errorWorldTriple: true,
            careBearsMultiplier: 10);
    }

    /// <summary>
    /// Corruption/Crimson starts from maxTilesX * 0.00045 attempts. Remix
    /// doubles it and Drunk halves it. The source compares an integer loop index
    /// to the resulting double, so positive fractional values execute ceil(N)
    /// iterations.
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
    /// Each CrimStart rolls Next(5, 9), so at most eight CrimVein calls append
    /// to WorldGen.heartPos per region. CrimEnt does not append there.
    /// </summary>
    public static int CrimsonHeartRecordUpperBound(int width, bool remixWorld, bool drunkWorld)
    {
        return checked(CrimsonRegionAttemptUpperBound(width, remixWorld, drunkWorld) * 8);
    }

    public static int CrimsonHeartScratchCapacity(int width)
    {
        return CrimsonHeartRecordUpperBound(width, remixWorld: true, drunkWorld: false);
    }
}
