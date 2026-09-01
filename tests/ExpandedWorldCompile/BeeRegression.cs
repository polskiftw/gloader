using System;
using System.Runtime.CompilerServices;

internal static class ExpandedWorldBeeRegression
{
    [ModuleInitializer]
    internal static void Run()
    {
        double small = ExpandedWorldBeeGeometryMath.DrunkHiveTunnelScale(4200, 1200);
        double medium = ExpandedWorldBeeGeometryMath.DrunkHiveTunnelScale(6400, 1800);
        double large = ExpandedWorldBeeGeometryMath.DrunkHiveTunnelScale(8400, 2400);
        double xl = ExpandedWorldBeeGeometryMath.DrunkHiveTunnelScale(12600, 2400);
        double huge = ExpandedWorldBeeGeometryMath.DrunkHiveTunnelScale(16800, 2400);

        double expectedMedium = ((6400d / 4200d) + 1d) / 2d;
        double expectedXL = 1.5d * Math.Sqrt(1.5d);
        double expectedHuge = 1.5d * Math.Sqrt(2d);

        if (Math.Abs(small - 1d) > 1e-12 ||
            Math.Abs(medium - expectedMedium) > 1e-12 ||
            Math.Abs(large - 1.5d) > 1e-12 ||
            Math.Abs(xl - expectedXL) > 1e-12 ||
            Math.Abs(huge - expectedHuge) > 1e-12)
        {
            throw new InvalidOperationException(
                "Expanded Worlds compile fixture: Drunk Hive tunnel geometry continuation changed unexpectedly.");
        }

        // Terraria's untouched `(proxy + 1) / 2` arithmetic must recover the
        // requested final multiplier exactly.
        double hugeProxy = ExpandedWorldBeeGeometryMath.DrunkHiveSourceWidthProxy(16800, 2400);
        double recoveredHuge = (hugeProxy + 1d) / 2d;
        if (Math.Abs(recoveredHuge - expectedHuge) > 1e-12)
        {
            throw new InvalidOperationException(
                "Expanded Worlds compile fixture: Drunk Hive source proxy no longer composes with vanilla arithmetic.");
        }
    }
}
