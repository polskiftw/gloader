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

        if (ExpandedWorldCapacityMath.CrimsonHeartScratchCapacity(ExpandedWorldMath.XLWidth) > 100 ||
            ExpandedWorldCapacityMath.CrimsonHeartScratchCapacity(ExpandedWorldMath.HugeWidth) != 128)
        {
            Console.Error.WriteLine("Expanded Worlds compile fixture: Huge Remix Crimson heart overflow guard is not preserved.");
            return 1;
        }

        Console.WriteLine("PASS: Expanded Worlds raw source compile fixture and discrete/capacity regressions.");
        return 0;
    }
}
