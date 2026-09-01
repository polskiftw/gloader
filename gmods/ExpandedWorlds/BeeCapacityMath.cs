using System;

/// <summary>
/// Source-derived upper bounds for Terraria 1.4.5.8 Hive/larva bookkeeping.
/// These do not change generation behavior; they exist to prove fixed larva
/// storage remains safe at expanded widths.
/// </summary>
internal static class ExpandedWorldBeeCapacityMath
{
    public static int HiveCountUpperBound(int width, bool drunkWorld)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));

        double scale = width / (double)ExpandedWorldMath.SmallWidth;

        // Source: 1 + Next((int)(5*scale), (int)(8*scale)). Random.Next upper
        // bound is exclusive, so the maximum simplifies to floor(8*scale).
        int baseMaximum = (int)(8d * scale);

        if (!drunkWorld)
            return baseMaximum;

        // Source keeps num2 as double and decrements once per successful Hive;
        // therefore a positive fractional product requires ceil() successes.
        return (int)Math.Ceiling(baseMaximum * 0.667d);
    }

    public static int LarvaRecordUpperBound(int width, bool drunkWorld)
    {
        int hives = HiveCountUpperBound(width, drunkWorld);

        // HiveBiome always creates one larva stand. Drunk World makes at most
        // one additional successful CreateStandForLarva call per Hive.
        return checked(hives * (drunkWorld ? 2 : 1));
    }
}
