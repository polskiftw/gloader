using System;

internal static class Program
{
    private static int Main()
    {
        if (!ExpandedWorldBackingStorage.IsSupportedExpandedWorld(
                ExpandedWorldMath.XLWidth,
                ExpandedWorldMath.XLHeight) ||
            !ExpandedWorldBackingStorage.IsSupportedExpandedWorld(
                ExpandedWorldMath.HugeWidth,
                ExpandedWorldMath.HugeHeight) ||
            !ExpandedWorldBackingStorage.IsSupportedExpandedWorld(
                ExpandedWorldMath.ThiccWidth,
                ExpandedWorldMath.ThiccHeight) ||
            ExpandedWorldBackingStorage.IsSupportedExpandedWorld(
                ExpandedWorldMath.ThiccWidth,
                ExpandedWorldMath.HugeHeight + 1200) ||
            ExpandedWorldBackingStorage.IsSupportedExpandedWorld(
                ExpandedWorldMath.LargeWidth,
                ExpandedWorldMath.LargeHeight))
        {
            Console.Error.WriteLine("Expanded Worlds server fixture: supported-dimension contract changed unexpectedly.");
            return 1;
        }

        if (ExpandedWorldBackingStorage.RequiredBackingWidth(ExpandedWorldMath.ThiccWidth) != 16801 ||
            ExpandedWorldBackingStorage.RequiredBackingHeight(ExpandedWorldMath.ThiccHeight) != 4801)
        {
            Console.Error.WriteLine("Expanded Worlds server fixture: THICC backing-storage headroom changed unexpectedly.");
            return 1;
        }

        Console.WriteLine("PASS: Expanded Worlds shared client/server storage source compiles under GLOADER_SERVER with THICC support.");
        return 0;
    }
}