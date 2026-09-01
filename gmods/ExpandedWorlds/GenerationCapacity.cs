#if GLOADER_CLIENT
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Terraria;

/// <summary>
/// Protects Expanded Worlds from source worldgen bookkeeping capacities without
/// changing generation counts, RNG, placement choices, or seed behavior.
///
/// Current Terraria 1.4.5 uses dynamic per-DungeonData List<T> collections for
/// Dungeon state, so those are validation-only. Floating Island metadata remains
/// fixed arrays, however, and the 1.4.5.8 Error World + Care Bears combination
/// can exceed vanilla's 300 records on XL/Huge. WorldGen's private Crimson-heart
/// position array is also fixed at 100; Remix can require 108 records on XL and
/// 144 on Huge. Those arrays are enlarged to their exact audited worst-case
/// record bounds.
///
/// Older Terraria builds used fixed Dungeon arrays. Keep the source-derived
/// legacy Dungeon resize as a compatibility fallback only when modern
/// DungeonData is absent.
/// </summary>
internal static class ExpandedWorldGenerationCapacity
{
    private const string ModernDungeonDataTypeName =
        "Terraria.GameContent.Generation.Dungeon.DungeonData";

    private const string ModernDungeonGenVarsTypeName =
        "Terraria.GameContent.Generation.Dungeon.DungeonGenVars";

    private const string GenVarsTypeName = "Terraria.WorldBuilding.GenVars";

    public static void EnsureForCurrentWorld()
    {
        if (!ExpandedWorldState.GenerationArmed || Main.maxTilesX <= ExpandedWorldMath.LargeWidth)
            return;

        Type generationHolder = AccessTools.TypeByName(GenVarsTypeName) ?? typeof(WorldGen);
        EnsureFloatingIslandStorage(generationHolder);
        EnsureCrimsonHeartStorage();

        Type modernDungeonData = AccessTools.TypeByName(ModernDungeonDataTypeName);
        if (modernDungeonData != null)
        {
            ValidateModernDynamicDungeonStorage(modernDungeonData);
            Console.WriteLine(
                "[Expanded Worlds] Terraria 1.4.5-style DungeonData uses dynamic List<T> storage; " +
                "no Dungeon scratch-capacity resize is required.");
        }
        else
        {
            EnsureLegacyFixedDungeonStorage(generationHolder);
        }
    }

    private static void EnsureFloatingIslandStorage(Type holder)
    {
        int required = ExpandedWorldCapacityMath.FloatingIslandScratchCapacity(Main.maxTilesX);

        // Terraria 1.4.5.8 indexes all four arrays by GenVars.numIslandHouses.
        // The Care Bears clamp occurs after the generation loop, so it cannot
        // protect these writes. Resize all parallel arrays together.
        EnsureStaticArray(holder, required, typeof(bool), "skyLake");
        EnsureStaticArray(holder, required, typeof(int), "floatingIslandHouseX");
        EnsureStaticArray(holder, required, typeof(int), "floatingIslandHouseY");
        EnsureStaticArray(holder, required, typeof(int), "floatingIslandStyle");

        Console.WriteLine(
            "[Expanded Worlds] Floating Island metadata capacity ensured at " +
            required + " records for width " + Main.maxTilesX + ".");
    }

    private static void EnsureCrimsonHeartStorage()
    {
        int required = ExpandedWorldCapacityMath.CrimsonHeartScratchCapacity(Main.maxTilesX);

        // Terraria 1.4.5.8 writes heartPos once per CrimVein (up to eight) and
        // once more from CrimEnt at the end of every CrimStart, with no bounds
        // check. Remix can therefore require 12 * 9 = 108 records on XL and
        // 16 * 9 = 144 on Huge. Resize only the scratch array; generation/RNG
        // stay untouched.
        EnsureStaticArray(
            typeof(WorldGen),
            required,
            typeof(Microsoft.Xna.Framework.Point),
            "heartPos");

        Console.WriteLine(
            "[Expanded Worlds] Crimson-heart metadata capacity ensured at " +
            required + " records for width " + Main.maxTilesX + ".");
    }

    private static void ValidateModernDynamicDungeonStorage(Type dungeonData)
    {
        // Exact Terraria 1.4.5.8 source stores every count-growing Dungeon
        // collection in List<T>. This is what lets Dual Dungeons have independent
        // state without the historical 100/500-record ceilings.
        ValidateInstanceListField(dungeonData, "dungeonRooms");
        ValidateInstanceListField(dungeonData, "dungeonHalls");
        ValidateInstanceListField(dungeonData, "dungeonFeatures");
        ValidateInstanceListField(dungeonData, "dungeonDoorData");
        ValidateInstanceListField(dungeonData, "dungeonPlatformData");
        ValidateInstanceListField(dungeonData, "protectedDungeonBounds");

        Type genVars = AccessTools.TypeByName(GenVarsTypeName)
            ?? throw new TypeLoadException(
                "[Expanded Worlds] Modern DungeonData exists but Terraria.WorldBuilding.GenVars was not found.");

        Type dungeonGenVars = AccessTools.TypeByName(ModernDungeonGenVarsTypeName)
            ?? throw new TypeLoadException(
                "[Expanded Worlds] Modern DungeonData exists but DungeonGenVars was not found.");

        FieldInfo dungeonGenVarsField = AccessTools.Field(genVars, "dungeonGenVars");
        if (dungeonGenVarsField == null ||
            !dungeonGenVarsField.IsStatic ||
            !IsListOf(dungeonGenVarsField.FieldType, dungeonGenVars))
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] Terraria 1.4.5 Dungeon state no longer exposes the audited static " +
                "GenVars.dungeonGenVars List<DungeonGenVars> shape. Refusing to infer capacity behavior.");
        }
    }

    private static void ValidateInstanceListField(Type holder, string fieldName)
    {
        FieldInfo field = AccessTools.Field(holder, fieldName);
        if (field == null || field.IsStatic || !IsListType(field.FieldType))
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] Modern Dungeon capacity field " + holder.FullName + "." + fieldName +
                " no longer has the audited instance List<T> shape. Refusing to guess.");
        }
    }

    private static bool IsListType(Type type)
    {
        return type != null &&
               type.IsGenericType &&
               type.GetGenericTypeDefinition() == typeof(List<>);
    }

    private static bool IsListOf(Type type, Type elementType)
    {
        return IsListType(type) && type.GetGenericArguments()[0] == elementType;
    }

    private static void EnsureLegacyFixedDungeonStorage(Type holder)
    {
        int requiredRooms = ExpandedWorldCapacityMath.DungeonRoomRecordUpperBound(Main.maxTilesX, Main.maxTilesY);
        int requiredDoors = ExpandedWorldCapacityMath.DungeonDoorRecordUpperBound(Main.maxTilesX, Main.maxTilesY);
        int requiredPlatforms = ExpandedWorldCapacityMath.DungeonPlatformRecordUpperBound(Main.maxTilesX, Main.maxTilesY);

        EnsureStaticArray(holder, requiredRooms, typeof(int), "dRoomX");
        EnsureStaticArray(holder, requiredRooms, typeof(int), "dRoomY");
        EnsureStaticArray(holder, requiredRooms, typeof(int), "dRoomSize");
        EnsureStaticArray(holder, requiredRooms, typeof(bool), "dRoomTreasure");
        EnsureStaticArray(holder, requiredRooms, typeof(int), "dRoomL");
        EnsureStaticArray(holder, requiredRooms, typeof(int), "dRoomR");
        EnsureStaticArray(holder, requiredRooms, typeof(int), "dRoomT");
        EnsureStaticArray(holder, requiredRooms, typeof(int), "dRoomB");

        EnsureStaticArray(holder, requiredDoors, typeof(int), "DDoorX");
        EnsureStaticArray(holder, requiredDoors, typeof(int), "DDoorY");
        EnsureStaticArray(holder, requiredDoors, typeof(int), "DDoorPos");

        EnsureStaticArray(holder, requiredPlatforms, typeof(int), "dungeonPlatformX", "DPlatX");
        EnsureStaticArray(holder, requiredPlatforms, typeof(int), "dungeonPlatformY", "DPlatY");

        Console.WriteLine(
            "[Expanded Worlds] Legacy Dungeon scratch capacity: rooms=" + requiredRooms +
            ", doors=" + requiredDoors +
            ", platforms=" + requiredPlatforms + ".");
    }

    private static void EnsureStaticArray(
        Type holder,
        int requiredLength,
        Type elementType,
        params string[] candidateNames)
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
                "[Expanded Worlds] Capacity field " + holder.FullName + "." + field.Name +
                " no longer has the expected static " + elementType.Name + "[] shape. Refusing to guess.");
        }

        Array current = field.GetValue(null) as Array;
        if (current == null)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] Capacity field " + holder.FullName + "." + field.Name +
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
/// clearWorld is late enough that selected expanded dimensions are active and
/// early enough that worldgen passes have not consumed their scratch storage.
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
