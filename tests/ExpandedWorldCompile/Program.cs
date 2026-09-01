using System;

internal static class Program
{
    private static int Main()
    {
        // Reaching Main means every source file under gmods/ExpandedWorlds
        // compiled against the intentional Terraria API surface above.
        if (ExpandedWorldMath.XLWidth != 12600 || ExpandedWorldMath.HugeWidth != 16800)
        {
            Console.Error.WriteLine("Expanded Worlds compile fixture: preset constants changed unexpectedly.");
            return 1;
        }

        int[] expectedDirtiestBase = { 3, 6, 9, 12, 15 };
        int[] expectedDirtiestCelebration = { 15, 30, 45, 60, 75 };
        int[] expectedStatueMultiplier = { 2, 3, 4, 5, 6 };
        int[] expectedGlowTulips = { 2, 4, 6, 8, 10 };
        int[] expectedSpikeCaveMinimum = { 3, 5, 7, 9, 11 };
        int[] expectedSpikeCaveMaximum = { 4, 6, 8, 10, 12 };
        int[] expectedChilletEggs = { 6, 9, 12, 15, 18 };

        for (int tier = 1; tier <= 5; tier++)
        {
            int baseCount = ExpandedWorldTierMath.DirtiestBlockBaseCount(tier);
            if (baseCount != expectedDirtiestBase[tier - 1])
            {
                Console.Error.WriteLine(
                    "Expanded Worlds compile fixture: Dirtiest Block tier " + tier +
                    " expected " + expectedDirtiestBase[tier - 1] + ", got " + baseCount + ".");
                return 1;
            }

            int celebrationCount = ExpandedWorldTierMath.DirtiestBlockCount(tier, true);
            if (celebrationCount != expectedDirtiestCelebration[tier - 1])
            {
                Console.Error.WriteLine(
                    "Expanded Worlds compile fixture: Celebration Dirtiest Block tier " + tier +
                    " expected " + expectedDirtiestCelebration[tier - 1] + ", got " + celebrationCount + ".");
                return 1;
            }

            int statueMultiplier = ExpandedWorldStatueTierMath.Multiplier(tier);
            if (statueMultiplier != expectedStatueMultiplier[tier - 1])
            {
                Console.Error.WriteLine(
                    "Expanded Worlds compile fixture: statue multiplier tier " + tier +
                    " expected " + expectedStatueMultiplier[tier - 1] + ", got " + statueMultiplier + ".");
                return 1;
            }

            int glowTulips = ExpandedWorldDiscreteCountMath.GlowTulipCount(tier);
            if (glowTulips != expectedGlowTulips[tier - 1])
            {
                Console.Error.WriteLine(
                    "Expanded Worlds compile fixture: Glow Tulip tier " + tier +
                    " expected " + expectedGlowTulips[tier - 1] + ", got " + glowTulips + ".");
                return 1;
            }

            IntRange spikeCaves = ExpandedWorldDiscreteCountMath.SpikeCaveCountRange(tier);
            if (spikeCaves.Minimum != expectedSpikeCaveMinimum[tier - 1] ||
                spikeCaves.Maximum != expectedSpikeCaveMaximum[tier - 1])
            {
                Console.Error.WriteLine(
                    "Expanded Worlds compile fixture: Spike Cave tier " + tier +
                    " expected " + expectedSpikeCaveMinimum[tier - 1] + "-" + expectedSpikeCaveMaximum[tier - 1] +
                    ", got " + spikeCaves + ".");
                return 1;
            }

            int chilletEggs = ExpandedWorldDiscreteCountMath.ChilletEggCount(tier);
            if (chilletEggs != expectedChilletEggs[tier - 1])
            {
                Console.Error.WriteLine(
                    "Expanded Worlds compile fixture: Chillet Egg tier " + tier +
                    " expected " + expectedChilletEggs[tier - 1] + ", got " + chilletEggs + ".");
                return 1;
            }
        }

        if (ExpandedWorldCapacityMath.CrimsonHeartRecordUpperBound(8400, true, false) != 64 ||
            ExpandedWorldCapacityMath.CrimsonHeartRecordUpperBound(ExpandedWorldMath.XLWidth, true, false) != 96 ||
            ExpandedWorldCapacityMath.CrimsonHeartRecordUpperBound(ExpandedWorldMath.HugeWidth, false, false) != 64 ||
            ExpandedWorldCapacityMath.CrimsonHeartRecordUpperBound(ExpandedWorldMath.HugeWidth, true, false) != 128 ||
            ExpandedWorldCapacityMath.CrimsonHeartRecordUpperBound(ExpandedWorldMath.HugeWidth, true, true) != 64)
        {
            Console.Error.WriteLine("Expanded Worlds compile fixture: Crimson heart capacity regression changed unexpectedly.");
            return 1;
        }

        if (ExpandedWorldCapacityMath.CrimsonHeartScratchCapacity(ExpandedWorldMath.XLWidth) != 96 ||
            ExpandedWorldCapacityMath.CrimsonHeartScratchCapacity(ExpandedWorldMath.XLWidth) > 100 ||
            ExpandedWorldCapacityMath.CrimsonHeartScratchCapacity(ExpandedWorldMath.HugeWidth) != 128 ||
            ExpandedWorldCapacityMath.CrimsonHeartScratchCapacity(ExpandedWorldMath.HugeWidth) <= 100)
        {
            Console.Error.WriteLine("Expanded Worlds compile fixture: Crimson heart overflow guard is not preserved.");
            return 1;
        }

        if (ExpandedWorldSecretSeedMath.DontStarveWavyCaveBaseCount(4200, 1200) != 35 ||
            ExpandedWorldSecretSeedMath.DontStarveWavyCaveBaseCount(6400, 1800) != 81 ||
            ExpandedWorldSecretSeedMath.DontStarveWavyCaveBaseCount(8400, 2400) != 140)
        {
            Console.Error.WriteLine(
                "Expanded Worlds compile fixture: Don't Starve Wavy Caves no longer reproduce Terraria's Small/Medium/Large source counts.");
            return 1;
        }

        if (ExpandedWorldSecretSeedMath.DontStarveWavyCaveBaseCount(12600, 2400) != 210 ||
            ExpandedWorldSecretSeedMath.DontStarveWavyCaveBaseCount(16800, 2400) != 280 ||
            ExpandedWorldSecretSeedMath.DontStarveWavyCaveCount(16800, 2400, true) != 93)
        {
            Console.Error.WriteLine(
                "Expanded Worlds compile fixture: expanded Don't Starve Wavy Cave area continuation changed unexpectedly.");
            return 1;
        }

        double vanillaMediumLinear = 6400d / 4200d;
        double xlLinear = ExpandedWorldFeatureGeometryMath.AxisNeutralLinearScale(12600, 2400);
        double hugeLinear = ExpandedWorldFeatureGeometryMath.AxisNeutralLinearScale(16800, 2400);
        if (Math.Abs(ExpandedWorldFeatureGeometryMath.AxisNeutralLinearScale(4200, 1200) - 1d) > 1e-12 ||
            Math.Abs(ExpandedWorldFeatureGeometryMath.AxisNeutralLinearScale(6400, 1800) - vanillaMediumLinear) > 1e-12 ||
            Math.Abs(ExpandedWorldFeatureGeometryMath.AxisNeutralLinearScale(8400, 2400) - 2d) > 1e-12 ||
            Math.Abs(xlLinear - Math.Sqrt(6d)) > 1e-12 ||
            Math.Abs(hugeLinear - Math.Sqrt(8d)) > 1e-12)
        {
            Console.Error.WriteLine(
                "Expanded Worlds compile fixture: axis-neutral feature geometry scale changed unexpectedly.");
            return 1;
        }

        Console.WriteLine("PASS: Expanded Worlds raw source compile fixture, capacity/seed/tier regressions, and feature geometry regressions.");
        return 0;
    }
}
