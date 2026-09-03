using System;

/// <summary>
/// Terraria 1.4.5.8 world-size continuation model.
///
/// Vanilla physical sizes are all exact 200 x 150 network-section grids:
///   Small  =  4200 x 1200 = 21 x  8 sections
///   Medium =  6400 x 1800 = 32 x 12 sections
///   Large  =  8400 x 2400 = 42 x 16 sections
///
/// The expanded tiers continue the section cadence rather than inventing a new
/// aspect ratio. Horizontal section deltas repeat +11, +10; vertical sections
/// always add +4. This preserves vanilla Medium's small width/height wobble:
///   XL     = 10600 x 3000 = 53 x 20 sections
///   Huge   = 12600 x 3600 = 63 x 24 sections
///   THICC  = 14800 x 4200 = 74 x 28 sections
/// </summary>
internal enum ExpandedWorldPreset
{
    None = 0,
    XL = 1,
    Huge = 2,
    Thicc = 3,
}

internal static class ExpandedWorldMath
{
    public const int SectionWidth = 200;
    public const int SectionHeight = 150;

    public const int SmallWidth = 4200;
    public const int SmallHeight = 1200;
    public const int MediumWidth = 6400;
    public const int MediumHeight = 1800;
    public const int LargeWidth = 8400;
    public const int LargeHeight = 2400;

    public const int XLWidth = 10600;
    public const int XLHeight = 3000;
    public const int HugeWidth = 12600;
    public const int HugeHeight = 3600;
    public const int ThiccWidth = 14800;
    public const int ThiccHeight = 4200;

    public static bool IsExpandedPresetDimensions(int width, int height)
    {
        return (width == XLWidth && height == XLHeight) ||
               (width == HugeWidth && height == HugeHeight) ||
               (width == ThiccWidth && height == ThiccHeight);
    }

    public static int WidthFor(ExpandedWorldPreset preset)
    {
        switch (preset)
        {
            case ExpandedWorldPreset.XL:
                return XLWidth;
            case ExpandedWorldPreset.Huge:
                return HugeWidth;
            case ExpandedWorldPreset.Thicc:
                return ThiccWidth;
            default:
                return LargeWidth;
        }
    }

    public static int HeightFor(ExpandedWorldPreset preset)
    {
        switch (preset)
        {
            case ExpandedWorldPreset.XL:
                return XLHeight;
            case ExpandedWorldPreset.Huge:
                return HugeHeight;
            case ExpandedWorldPreset.Thicc:
                return ThiccHeight;
            default:
                return LargeHeight;
        }
    }

    public static int TierFor(ExpandedWorldPreset preset)
    {
        switch (preset)
        {
            case ExpandedWorldPreset.XL:
                return 4;
            case ExpandedWorldPreset.Huge:
                return 5;
            case ExpandedWorldPreset.Thicc:
                return 6;
            default:
                return 3;
        }
    }

    public static int HorizontalSections(int width)
    {
        if (width <= 0 || width % SectionWidth != 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        return width / SectionWidth;
    }

    public static int VerticalSections(int height)
    {
        if (height <= 0 || height % SectionHeight != 0)
            throw new ArgumentOutOfRangeException(nameof(height));
        return height / SectionHeight;
    }

    public static string LabelFor(ExpandedWorldPreset preset)
    {
        switch (preset)
        {
            case ExpandedWorldPreset.XL:
                return "XL";
            case ExpandedWorldPreset.Huge:
                return "Huge";
            case ExpandedWorldPreset.Thicc:
                return "THICC";
            default:
                return "Vanilla";
        }
    }

    public static long TileArea(int width, int height)
    {
        return checked((long)width * height);
    }
}

/// <summary>
/// One generation context is shared by client and server builds. Every
/// source-backed discrete continuation and capacity guard therefore sees the
/// same selected physical tier regardless of how Terraria was launched.
/// </summary>
internal static class ExpandedWorldGenerationContext
{
    public static ExpandedWorldPreset ActivePreset { get; private set; }
    public static bool IsActive => ActivePreset != ExpandedWorldPreset.None;
    public static int ActiveTier => ExpandedWorldMath.TierFor(ActivePreset);

    public static void Begin(ExpandedWorldPreset preset)
    {
        if (preset == ExpandedWorldPreset.None)
            throw new ArgumentOutOfRangeException(nameof(preset));

        ActivePreset = preset;
    }

    public static void End()
    {
        ActivePreset = ExpandedWorldPreset.None;
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
