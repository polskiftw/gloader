using System.Runtime.CompilerServices;

internal static class PromotedInferenceChecks
{
    [ModuleInitializer]
    internal static void Run()
    {
        for (int tier = 1; tier <= ExpandedWorldMath.MaximumSupportedOverallTier; tier++)
        {
            int evilExpected = tier == 1 ? 8 : 4 * tier + 6;
            int spiderExpected = tier == 1 ? 2 : 2 * tier + 2;

            Equal(evilExpected, ExpandedWorldInferredTierMath.EvilOrbHeartQuota(tier), $"evil Orb/Heart quota tier {tier}");
            Equal(spiderExpected, ExpandedWorldInferredTierMath.SpiderSpecializedRoomCount(tier), $"Spider specialized-room quota tier {tier}");
        }

        // Large is included as the calibration point: the inferred width rule
        // must reproduce vanilla Large's existing 2/3 result exactly.
        var paintingCases = new[]
        {
            (Width: 8400, Low: 2, High: 3, Name: "Large"),
            (Width: 10600, Low: 3, High: 4, Name: "THICC"),
            (Width: 12600, Low: 3, High: 4, Name: "THICC 2"),
            (Width: 14800, Low: 4, High: 5, Name: "THICC 3"),
            (Width: 16800, Low: 5, High: 6, Name: "THICC 4"),
            (Width: 19000, Low: 5, High: 6, Name: "THICC 5"),
            (Width: 21000, Low: 6, High: 7, Name: "THICC 6"),
            (Width: 23200, Low: 7, High: 8, Name: "THICC 7"),
            (Width: 25200, Low: 7, High: 8, Name: "THICC 8"),
            (Width: 27400, Low: 8, High: 9, Name: "THICC 9"),
            (Width: 29400, Low: 9, High: 10, Name: "THICC 10"),
            (Width: 31600, Low: 9, High: 10, Name: "THICC 11")
        };

        foreach (var item in paintingCases)
        {
            Equal(
                item.Low,
                ExpandedWorldInferredTierMath.ExpandedLihzahrdPaintingMaxFromVanillaLarge(2, item.Width),
                item.Name + " Lihzahrd painting low roll");
            Equal(
                item.High,
                ExpandedWorldInferredTierMath.ExpandedLihzahrdPaintingMaxFromVanillaLarge(3, item.Width),
                item.Name + " Lihzahrd painting high roll");
        }

        Throws<ArgumentOutOfRangeException>(
            () => ExpandedWorldInferredTierMath.ExpandedLihzahrdPaintingMaxFromVanillaLarge(4, 10600),
            "reject unexpected vanilla Lihzahrd painting source value");
        Throws<ArgumentOutOfRangeException>(
            () => ExpandedWorldInferredTierMath.ExpandedLihzahrdPaintingMaxFromVanillaLarge(2, 8399),
            "reject pre-Large width for Lihzahrd painting continuation");
    }

    private static void Equal<T>(T expected, T actual, string name)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{name}: expected {expected}, got {actual}.");
    }

    private static void Throws<TException>(Action action, string name)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException(name + ": expected " + typeof(TException).Name + ".");
    }
}
