using System;

/// <summary>
/// Pure, Terraria-independent scaling rules for Expanded Worlds.
///
/// Vanilla worlds grow in both axes, so Terraria can sometimes use width as a
/// proxy for overall world size. Expanded Worlds intentionally breaks that
/// relationship: XL/Huge are wider while remaining 2400 tiles tall. The correct
/// generalization is therefore dimension-aware:
///
///   horizontal geometry/counts -> width / 4200
///   vertical geometry          -> height / 1200
///   area-density counts        -> (width * height) / (4200 * 1200)
///
/// Small/Medium/Large are never rewritten by the runtime patches. For formulas
/// we extrapolate, CI first proves the rule reproduces known vanilla outputs.
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

    public const long SmallArea = (long)SmallWidth * SmallHeight;

    public static long TileArea(int width, int height)
    {
        return (long)width * height;
    }

    public static double HorizontalScale(int width)
    {
        return width / (double)SmallWidth;
    }

    public static double VerticalScale(int height)
    {
        return height / (double)SmallHeight;
    }

    // Kept as an alias because many vanilla configuration families explicitly
    // call their scaling mode WorldWidth.
    public static double WidthScale(int width)
    {
        return HorizontalScale(width);
    }

    public static double AreaScale(int width, int height)
    {
        return TileArea(width, height) / (double)SmallArea;
    }

    // Mirrors Terraria's WorldGenRange scaling behavior: multiply the Small-world
    // base value by the selected physical scale, then truncate toward zero.
    public static int ScaleByWidth(int smallWorldValue, int width)
    {
        return (int)(smallWorldValue * HorizontalScale(width));
    }

    public static int ScaleByHeight(int smallWorldValue, int height)
    {
        return (int)(smallWorldValue * VerticalScale(height));
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
        // Vanilla currently exposes 1 / 2 / 3 for 4200 / 6400 / 8400 via
        // thresholds. floor(width / 2800) is the width-density continuation that
        // reproduces all three and yields XL=4, Huge=6.
        return Math.Max(1, width / 2800);
    }

    // ---- Underground Desert geometry ---------------------------------------
    // Current vanilla derives one scalar from maxTilesX and uses it for both X
    // and Y because normal world width/height grow together. With a custom aspect
    // ratio that is dimensionally wrong: Huge's width scalar (4x Small) would
    // also try to make the desert 4x Small's height inside a world only 2x Small
    // height. We preserve the exact source arithmetic but feed each axis its own
    // physical scale.

    public static int UndergroundDesertBlockColumns(int width)
    {
        // Vanilla: (int)(80 * widthScale).
        return (int)(80d * HorizontalScale(width));
    }

    public static int UndergroundDesertWidth(int width)
    {
        // DefaultBlockScale.X = 4.
        return UndergroundDesertBlockColumns(width) * 4;
    }

    public static int UndergroundDesertBlockRows(double nextDouble, int height)
    {
        if (nextDouble < 0d || nextDouble >= 1d)
            throw new ArgumentOutOfRangeException(nameof(nextDouble));

        // Vanilla normal seed:
        // (int)((NextDouble() * 0.5 + 1.5) * 170 * overallScale).
        // Only the scale source changes for expanded aspect ratios.
        return (int)((nextDouble * 0.5d + 1.5d) * 170d * VerticalScale(height));
    }

    public static int UndergroundDesertBlockRowsRemix(int height)
    {
        // Vanilla Remix: (int)(340 * overallScale).
        return (int)(340d * VerticalScale(height));
    }

    public static int UndergroundDesertHeight(double nextDouble, int height)
    {
        // DefaultBlockScale.Y = 2.
        return UndergroundDesertBlockRows(nextDouble, height) * 2;
    }

    public static int UndergroundDesertHeightRemix(int height)
    {
        return UndergroundDesertBlockRowsRemix(height) * 2;
    }

    public static int UndergroundDesertTenthAnniversaryYOffset(int height)
    {
        // Vanilla: (int)(20 * overallScale).
        return (int)(20d * VerticalScale(height));
    }

    // WorldGenRange-backed families. Their base ranges are the Small-world
    // configuration; Terraria's scaler distinguishes WorldArea vs WorldWidth.
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
        // Source-family formula:
        //   1 + Next((int)(5 * widthScale), (int)(8 * widthScale))
        // Random.Next's upper bound is exclusive, so inclusive maximum is
        // floor(8*scale).
        double scale = HorizontalScale(width);
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
