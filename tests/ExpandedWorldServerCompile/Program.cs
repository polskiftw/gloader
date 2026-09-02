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
            ExpandedWorldBackingStorage.IsSupportedExpandedWorld(
                ExpandedWorldMath.LargeWidth,
                ExpandedWorldMath.LargeHeight))
        {
            Console.Error.WriteLine("Expanded Worlds server fixture: supported-dimension contract changed unexpectedly.");
            return 1;
        }

        if (ExpandedWorldBackingStorage.RequiredBackingWidth(ExpandedWorldMath.HugeWidth) != 16801 ||
            ExpandedWorldBackingStorage.RequiredBackingHeight(ExpandedWorldMath.HugeHeight) != 2401)
        {
            Console.Error.WriteLine("Expanded Worlds server fixture: backing-storage headroom changed unexpectedly.");
            return 1;
        }

        Console.WriteLine("PASS: Expanded Worlds shared client/server storage source compiles under GLOADER_SERVER.");
        return 0;
    }
}
