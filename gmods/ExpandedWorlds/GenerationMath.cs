using System;

/// <summary>
/// Pure, Terraria-independent scaling rules for Expanded Worlds.
///
/// Vanilla worlds normally grow both axes together, so some Terraria code can
/// get away with using one dimension as a proxy for overall size. Expanded
/// Worlds intentionally changes the aspect ratio. Once that relationship is
/// broken, quantities are classified by dimensional meaning:
///
///   horizontal geometry/counts -> width
///   vertical geometry          -> height
///   area-density counts        -> width * height
///   isotropic linear geometry  -> sqrt(width * height)
///
/// The isotropic rule is the area-equivalent linear scale: if total relevant
/// area doubles while no axis is preferred, a radius/diameter grows by sqrt(2),
/// not 2. It collapses back to the ordinary linear scale when both axes grow by
/// the same factor.
///
/// Small/Medium/Large are never rewritten by runtime patches. For count/range
/// formulas that we extrapolate, CI first proves the rule reproduces known
/// vanilla Small / Medium / Large outputs. Discrete tier-only sequences are not
/// extrapolated merely because a curve can be fitted through three points.
/// </summary>
internal static class ExpandedWorldMath
{
    public const int SmallWidth = 4200;
    public const int SmallHeight = 1200;
    public const int MediumWidth = 6400;
    public const int MediumHeight = 1800;
    public const int LargeWidth = 8400;
    public const int LargeHeight = 2400;

    // 12,600 is intentional. Terraria has several legacy/current formulas that
    // use integer division by 4,200 as a coarse horizontal world-size quantum.
    // 12,000 would still evaluate to the same quotient as Large (2), while
    // 12,600 is the next exact quantum (3). Huge is the following quantum (4).
    public const int XLWidth = 12600;
    public const int XLHeight = 2400;
    public const int HugeWidth = 16800;
    public const int HugeHeight = 2400;

    public const long SmallArea = (long)SmallWidth * SmallHeight;
    public const long LargeArea = (long)LargeWidth * LargeHeight;

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

    public static double WidthScale(int width)
    {
        return HorizontalScale(width);
    }

    public static double AreaScale(int width, int height)
    {
        return TileArea(width, height) / (double)SmallArea;
    }

    public static double IsotropicLinearScale(int width, int height)
    {
        return Math.Sqrt(AreaScale(width, height));
    }

    public static double WidthRelativeToLarge(int width)
    {
        return width / (double)LargeWidth;
    }

    public static double HeightRelativeToLarge(int height)
    {
        return height / (double)LargeHeight;
    }

    public static double AreaRelativeToLarge(int width, int height)
    {
        return TileArea(width, height) / (double)LargeArea;
    }

    public static double IsotropicLinearRelativeToLarge(int width, int height)
    {
        return Math.Sqrt(AreaRelativeToLarge(width, height));
    }

    public static double ScaleLargeLinearByWidth(double vanillaLargeValue, int width)
    {
        return vanillaLargeValue * WidthRelativeToLarge(width);
    }

    public static double ScaleLargeLinearByHeight(double vanillaLargeValue, int height)
    {
        return vanillaLargeValue * HeightRelativeToLarge(height);
    }

    public static double ScaleLargeLinearIsotropically(double vanillaLargeValue, int width, int height)
    {
        return vanillaLargeValue * IsotropicLinearRelativeToLarge(width, height);
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

    // Floating Lakes are deliberately absent. Vanilla exposes a discrete
    // Small/Medium/Large threshold sequence (1/2/3), but that does not uniquely
    // define a continuation beyond Large. Until a source-backed rule is found,
    // Expanded Worlds does not pretend that fitting a convenient curve is proof.

    // ---- Underground Desert geometry ---------------------------------------
    // Current vanilla derives one scalar from maxTilesX and uses it for both X
    // and Y because normal world width/height grow together. With a custom aspect
    // ratio that is dimensionally wrong. Preserve the source arithmetic while
    // feeding each axis its own physical scale.

    public static int UndergroundDesertBlockColumns(int width)
    {
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
        // Only the source of the scale changes for expanded aspect ratios.
        return (int)((nextDouble * 0.5d + 1.5d) * 170d * VerticalScale(height));
    }

    public static int UndergroundDesertBlockRowsRemix(int height)
    {
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
