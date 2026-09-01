using System;

/// <summary>
/// Pure, Terraria-independent scaling rules for Expanded Worlds.
///
/// The point of this file is not to invent a "world size 4" or "world size 5".
/// Terraria scales different generators by different physical dimensions. These
/// helpers preserve that distinction and use the same truncation behavior as
/// vanilla WorldGenRange-style scaling.
///
/// CI exercises the Small / Medium / Large rows first. A formula is only used as
/// an XL / Huge extrapolation when it reproduces the known vanilla tiers.
/// </summary>
internal static class ExpandedWorldMath
{
    public const int SmallWidth = 4200;
    public const int SmallHeight = 1200;
    public const int MediumWidth = 6400;
    public const int MediumHeight = 1800;
    public const int LargeWidth = 8400;
    public const int LargeHeight = 2400;
    public const int XLWidth = 12000;
    public const int XLHeight = 2400;
    public const int HugeWidth = 16800;
    public const int HugeHeight = 2400;

    public const long SmallArea = (long)SmallWidth * SmallHeight; // 5,040,000

    public static long TileArea(int width, int height)
    {
        return (long)width * height;
    }

    public static double WidthScale(int width)
    {
        return width / (double)SmallWidth;
    }

    public static double AreaScale(int width, int height)
    {
        return TileArea(width, height) / (double)SmallArea;
    }

    // Mirrors Terraria's WorldGenRange scaling behavior: multiply the Small-world
    // base value by the selected physical scale, then truncate toward zero.
    public static int ScaleByWidth(int smallWorldValue, int width)
    {
        return (int)(smallWorldValue * WidthScale(width));
    }

    public static int ScaleByArea(int smallWorldValue, int width, int height)
    {
        return (int)(smallWorldValue * AreaScale(width, height));
    }

    public static IntRange ScaleRangeByWidth(int smallMinimum, int smallMaximum, int width)
    {
        return new IntRange(
            ScaleByWidth(smallMinimum, width),
            ScaleByWidth(smallMaximum, width));
    }

    public static IntRange ScaleRangeByArea(
        int smallMinimum,
        int smallMaximum,
        int width,
        int height)
    {
        return new IntRange(
            ScaleByArea(smallMinimum, width, height),
            ScaleByArea(smallMaximum, width, height));
    }

    // Source-derived vanilla formulas which already consume physical dimensions.
    public static int LifeCrystals(int width, int height)
    {
        // Vanilla: floor(maxTilesX * maxTilesY * 0.00002).
        return (int)(TileArea(width, height) / 50000L);
    }

    public static int SurfaceChests(int width)
    {
        // Vanilla: floor(maxTilesX * 0.005).
        return width / 200;
    }

    public static int FloatingIslands(int width)
    {
        // Vanilla: floor(maxTilesX * 0.0008).
        return (int)(width * 0.0008d);
    }

    public static int FloatingLakes(int width)
    {
        // Vanilla currently exposes the sequence 1 / 2 / 3 for
        // 4200 / 6400 / 8400-wide worlds via discrete thresholds.
        // floor(width / 2800) is the simplest width-density continuation that
        // reproduces all three existing tiers exactly, yielding XL=4, Huge=6.
        return Math.Max(1, width / 2800);
    }

    // WorldGenRange-backed families. Their base ranges are the Small-world
    // configuration; Terraria's own scaler distinguishes WorldArea vs WorldWidth.
    public static IntRange MarbleCaves(int width, int height)
    {
        return ScaleRangeByArea(4, 8, width, height);
    }

    public static IntRange GraniteCaves(int width)
    {
        return ScaleRangeByWidth(4, 8, width);
    }

    public static IntRange UndergroundCabins(int width, int height)
    {
        return ScaleRangeByArea(35, 40, width, height);
    }

    public static IntRange CaveChests(int width, int height)
    {
        return ScaleRangeByArea(35, 40, width, height);
    }

    public static IntRange DeadMansChests(int width)
    {
        return ScaleRangeByWidth(10, 20, width);
    }

    public static int AdditionalDesertCabins(int width, int height)
    {
        return ScaleByArea(2, width, height);
    }

    public static IntRange LivingTreeMicroBiomes(int width)
    {
        return ScaleRangeByWidth(6, 11, width);
    }

    public static IntRange LongMinecartTrackCount(int width)
    {
        return ScaleRangeByWidth(1, 2, width);
    }

    public static IntRange BeeHives(int width)
    {
        // The modern S/M/L sequence 6-8 / 8-12 / 11-16 is reproduced exactly by
        // the source-family formula:
        //   1 + Next((int)(5 * widthScale), (int)(8 * widthScale))
        // Random.Next's upper bound is exclusive, so the resulting inclusive
        // maximum is floor(8*scale).
        double scale = WidthScale(width);
        int minimum = 1 + (int)(5d * scale);
        int maximum = (int)(8d * scale);
        return new IntRange(minimum, maximum);
    }
}

internal readonly struct IntRange : IEquatable<IntRange>
{
    public int Minimum { get; }
    public int Maximum { get; }

    public IntRange(int minimum, int maximum)
    {
        if (maximum < minimum)
            throw new ArgumentOutOfRangeException(nameof(maximum));

        Minimum = minimum;
        Maximum = maximum;
    }

    public bool Equals(IntRange other)
    {
        return Minimum == other.Minimum && Maximum == other.Maximum;
    }

    public override bool Equals(object obj)
    {
        return obj is IntRange other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            return (Minimum * 397) ^ Maximum;
        }
    }

    public override string ToString()
    {
        return Minimum == Maximum
            ? Minimum.ToString()
            : Minimum + "-" + Maximum;
    }
}
