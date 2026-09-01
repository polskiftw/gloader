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

        Console.WriteLine("PASS: Expanded Worlds raw source compile fixture, discrete/capacity regressions, and secret-seed scaling regressions.");
        return 0;
    }
}
