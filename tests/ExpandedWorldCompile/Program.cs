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

        Console.WriteLine("PASS: Expanded Worlds raw source compile fixture.");
        return 0;
    }
}
