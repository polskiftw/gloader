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

        Equal(3, ExpandedWorldInferredTierMath.ExpandedLihzahrdPaintingMaxFromVanillaLarge(2), "expanded Lihzahrd painting low roll");
        Equal(4, ExpandedWorldInferredTierMath.ExpandedLihzahrdPaintingMaxFromVanillaLarge(3), "expanded Lihzahrd painting high roll");

        Throws<ArgumentOutOfRangeException>(
            () => ExpandedWorldInferredTierMath.ExpandedLihzahrdPaintingMaxFromVanillaLarge(4),
            "reject unexpected vanilla Lihzahrd painting source value");
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
