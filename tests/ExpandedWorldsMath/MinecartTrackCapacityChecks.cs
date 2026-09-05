using System.Runtime.CompilerServices;

internal static class MinecartTrackCapacityChecks
{
    [ModuleInitializer]
    internal static void Run()
    {
        Equal(2000, ExpandedWorldMinecartTrackCapacityMath.LongTrackLengthMaximum(8400), "Large long-track maximum");
        Equal(4096, ExpandedWorldMinecartTrackCapacityMath.ScratchHistoryCapacity(8400), "Large history stays retail");

        Equal(3523, ExpandedWorldMinecartTrackCapacityMath.LongTrackLengthMaximum(14800), "THICC 3 long-track maximum");
        Equal(4096, ExpandedWorldMinecartTrackCapacityMath.ScratchHistoryCapacity(14800), "THICC 3 history stays retail");

        Equal(4000, ExpandedWorldMinecartTrackCapacityMath.LongTrackLengthMaximum(16800), "THICC 4 long-track maximum");
        Equal(4100, ExpandedWorldMinecartTrackCapacityMath.ScratchHistoryCapacity(16800), "THICC 4 first expanded history");

        Equal(7523, ExpandedWorldMinecartTrackCapacityMath.LongTrackLengthMaximum(31600), "THICC 11 long-track maximum");
        Equal(7623, ExpandedWorldMinecartTrackCapacityMath.ScratchHistoryCapacity(31600), "THICC 11 history capacity");

        for (int i = 0; i < ExpandedWorldMath.ExpandedPresetCount; i++)
        {
            ExpandedWorldDefinition definition = ExpandedWorldMath.DefinitionAt(i);
            int requestedMaximum = ExpandedWorldMinecartTrackCapacityMath.LongTrackLengthMaximum(definition.Width);
            int required = checked(requestedMaximum + ExpandedWorldMinecartTrackCapacityMath.VanillaTunnelReserve);
            int capacity = ExpandedWorldMinecartTrackCapacityMath.ScratchHistoryCapacity(definition.Width);

            True(
                capacity >= required,
                "minecart history covers requested maximum plus retail tunnel reserve for " + definition.Label);
        }
    }

    private static void Equal<T>(T expected, T actual, string name)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{name}: expected {expected}, got {actual}.");
    }

    private static void True(bool value, string name)
    {
        if (!value)
            throw new InvalidOperationException(name + ": expected true.");
    }
}
