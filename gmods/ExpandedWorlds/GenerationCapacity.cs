#if GLOADER_CLIENT
using System;
using System.Reflection;
using HarmonyLib;
using Terraria;

/// <summary>
/// Enlarges only vanilla Dungeon scratch-storage arrays when an expanded world
/// mathematically requires more bookkeeping capacity. This does not alter any
/// generation count, RNG call, placement, room/hall choice, or seed behavior.
/// </summary>
internal static class ExpandedWorldGenerationCapacity
{
    private static Type ResolveHolderType()
    {
        // Modern Terraria keeps these generation variables in GenVars. Retain a
        // WorldGen fallback for older source shapes; either path is validated by
        // exact field names and array element types below.
        return AccessTools.TypeByName("Terraria.WorldBuilding.GenVars") ?? typeof(WorldGen);
    }

    public static void EnsureForCurrentWorld()
    {
        if (!ExpandedWorldState.GenerationArmed || Main.maxTilesX <= ExpandedWorldMath.LargeWidth)
            return;

        Type holder = ResolveHolderType();
        int requiredRooms = ExpandedWorldCapacityMath.DungeonRoomRecordUpperBound(Main.maxTilesX, Main.maxTilesY);
        int requiredDoors = ExpandedWorldCapacityMath.DungeonDoorRecordUpperBound(Main.maxTilesX, Main.maxTilesY);
        int requiredPlatforms = ExpandedWorldCapacityMath.DungeonPlatformRecordUpperBound(Main.maxTilesX, Main.maxTilesY);

        EnsureArray(holder, requiredRooms, typeof(int), "dRoomX");
        EnsureArray(holder, requiredRooms, typeof(int), "dRoomY");
        EnsureArray(holder, requiredRooms, typeof(int), "dRoomSize");
        EnsureArray(holder, requiredRooms, typeof(bool), "dRoomTreasure");
        EnsureArray(holder, requiredRooms, typeof(int), "dRoomL");
        EnsureArray(holder, requiredRooms, typeof(int), "dRoomR");
        EnsureArray(holder, requiredRooms, typeof(int), "dRoomT");
        EnsureArray(holder, requiredRooms, typeof(int), "dRoomB");

        EnsureArray(holder, requiredDoors, typeof(int), "DDoorX");
        EnsureArray(holder, requiredDoors, typeof(int), "DDoorY");
        EnsureArray(holder, requiredDoors, typeof(int), "DDoorPos");

        // Modern GenVars renamed DPlatX/Y to dungeonPlatformX/Y. Accept only
        // those two audited names (or the historical exact fallback), not fuzzy
        // matches, so an upstream rename fails closed.
        EnsureArray(holder, requiredPlatforms, typeof(int), "dungeonPlatformX", "DPlatX");
        EnsureArray(holder, requiredPlatforms, typeof(int), "dungeonPlatformY", "DPlatY");

        Console.WriteLine(
            "[Expanded Worlds] Dungeon scratch capacity: rooms=" + requiredRooms +
            ", doors=" + requiredDoors +
            ", platforms=" + requiredPlatforms + ".");
    }

    private static void EnsureArray(Type holder, int requiredLength, Type elementType, params string[] candidateNames)
    {
        FieldInfo field = null;
        for (int i = 0; i < candidateNames.Length && field == null; i++)
            field = AccessTools.Field(holder, candidateNames[i]);

        if (field == null)
        {
            throw new MissingFieldException(
                holder.FullName,
                string.Join(" or ", candidateNames));
        }

        if (!field.IsStatic || !field.FieldType.IsArray || field.FieldType.GetElementType() != elementType)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] Dungeon capacity field " + holder.FullName + "." + field.Name +
                " no longer has the expected static " + elementType.Name + "[] shape. Refusing to guess.");
        }

        Array current = field.GetValue(null) as Array;
        if (current == null)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] Dungeon capacity field " + holder.FullName + "." + field.Name +
                " is null. Refusing to guess.");
        }

        if (current.Length >= requiredLength)
            return;

        Array replacement = Array.CreateInstance(elementType, requiredLength);
        Array.Copy(current, replacement, current.Length);
        field.SetValue(null, replacement);
    }
}

/// <summary>
/// clearWorld may reset generation bookkeeping. Run after that reset so expanded
/// capacity is guaranteed immediately before the generation passes consume it.
/// Ordinary world loading is untouched because the generation-only guard is off.
/// </summary>
[HarmonyPatch]
internal static class ExpandedWorldGenerationCapacityPatch
{
    private static MethodBase TargetMethod()
    {
        MethodBase method = AccessTools.Method(typeof(WorldGen), "clearWorld");
        if (method == null)
            throw new MissingMethodException(typeof(WorldGen).FullName, "clearWorld");
        return method;
    }

    [HarmonyPostfix]
    private static void Postfix()
    {
        ExpandedWorldGenerationCapacity.EnsureForCurrentWorld();
    }
}
#endif
