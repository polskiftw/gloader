using System.Runtime.CompilerServices;

internal static class PromotedInferenceChecks
{
    [ModuleInitializer]
    internal static void Run()
    {
        int[] evilExpected = { 8, 14, 18, 22, 26, 30 };
        int[] spiderExpected = { 2, 6, 8, 10, 12, 14 };

        for (int tier = 1; tier <= 6; tier++)
        {
            Equal(evilExpected[tier - 1], ExpandedWorldInferredTierMath.EvilOrbHeartQuota(tier), $"evil Orb/Heart quota tier {tier}");
            Equal(spiderExpected[tier - 1], ExpandedWorldInferredTierMath.SpiderSpecializedRoomCount(tier), $"Spider specialized-room quota tier {tier}");
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
