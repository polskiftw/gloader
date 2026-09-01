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

        int[] expectedBase = { 3, 6, 9, 12, 15 };
        int[] expectedCelebration = { 15, 30, 45, 60, 75 };

        for (int tier = 1; tier <= 5; tier++)
        {
            int baseCount = ExpandedWorldTierMath.DirtiestBlockBaseCount(tier);
            if (baseCount != expectedBase[tier - 1])
            {
                Console.Error.WriteLine(
                    "Expanded Worlds compile fixture: Dirtiest Block tier " + tier +
                    " expected " + expectedBase[tier - 1] + ", got " + baseCount + ".");
                return 1;
            }

            int celebrationCount = ExpandedWorldTierMath.DirtiestBlockCount(tier, true);
            if (celebrationCount != expectedCelebration[tier - 1])
            {
                Console.Error.WriteLine(
                    "Expanded Worlds compile fixture: Celebration Dirtiest Block tier " + tier +
                    " expected " + expectedCelebration[tier - 1] + ", got " + celebrationCount + ".");
                return 1;
            }
        }

        Console.WriteLine("PASS: Expanded Worlds raw source compile fixture and discrete-tier regression.");
        return 0;
    }
}
