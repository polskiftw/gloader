#if GLOADER_CLIENT
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Terraria;

/// <summary>
/// Protects Expanded Worlds from fixed-capacity Dungeon bookkeeping without
/// changing generation behavior.
///
/// Terraria 1.4.5 replaced the historical fixed room/door/platform scratch
/// arrays with per-DungeonData List<T> collections so secret seeds can generate
/// multiple independent Dungeons. On that source shape there is nothing to
/// resize: validate the audited dynamic-storage contract and leave it untouched.
///
/// Older Terraria builds used fixed static arrays. Keep the source-derived
/// legacy resize as a compatibility fallback only when the modern DungeonData
/// type is absent.
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

        Type modernDungeonData = AccessTools.TypeByName(ModernDungeonDataTypeName);
        if (modernDungeonData != null)
        {
            ValidateModernDynamicDungeonStorage(modernDungeonData);
            Console.WriteLine(
                "[Expanded Worlds] Terraria 1.4.5-style DungeonData uses dynamic List<T> storage; " +
                "no Dungeon scratch-capacity resize is required.");
            return;
        }

        EnsureLegacyFixedDungeonStorage();
    }

    private static void ValidateModernDynamicDungeonStorage(Type dungeonData)
    {
        // 1.4.5.6 audited source stores every count-growing Dungeon collection
        // in List<T>. This is what makes Dual Dungeons and larger dungeons free
        // of the old 100/500-record ceilings. Exact names are intentional: an
        // upstream source change must fail closed rather than silently falling
        // back to obsolete static-array assumptions.
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

    private static void EnsureLegacyFixedDungeonStorage()
    {
        Type holder = AccessTools.TypeByName(GenVarsTypeName) ?? typeof(WorldGen);
        int requiredRooms = ExpandedWorldCapacityMath.DungeonRoomRecordUpperBound(Main.maxTilesX, Main.maxTilesY);
        int requiredDoors = ExpandedWorldCapacityMath.DungeonDoorRecordUpperBound(Main.maxTilesX, Main.maxTilesY);
        int requiredPlatforms = ExpandedWorldCapacityMath.DungeonPlatformRecordUpperBound(Main.maxTilesX, Main.maxTilesY);

        EnsureLegacyStaticArray(holder, requiredRooms, typeof(int), "dRoomX");
        EnsureLegacyStaticArray(holder, requiredRooms, typeof(int), "dRoomY");
        EnsureLegacyStaticArray(holder, requiredRooms, typeof(int), "dRoomSize");
        EnsureLegacyStaticArray(holder, requiredRooms, typeof(bool), "dRoomTreasure");
        EnsureLegacyStaticArray(holder, requiredRooms, typeof(int), "dRoomL");
        EnsureLegacyStaticArray(holder, requiredRooms, typeof(int), "dRoomR");
        EnsureLegacyStaticArray(holder, requiredRooms, typeof(int), "dRoomT");
        EnsureLegacyStaticArray(holder, requiredRooms, typeof(int), "dRoomB");

        EnsureLegacyStaticArray(holder, requiredDoors, typeof(int), "DDoorX");
        EnsureLegacyStaticArray(holder, requiredDoors, typeof(int), "DDoorY");
        EnsureLegacyStaticArray(holder, requiredDoors, typeof(int), "DDoorPos");

        EnsureLegacyStaticArray(holder, requiredPlatforms, typeof(int), "dungeonPlatformX", "DPlatX");
        EnsureLegacyStaticArray(holder, requiredPlatforms, typeof(int), "dungeonPlatformY", "DPlatY");

        Console.WriteLine(
            "[Expanded Worlds] Legacy Dungeon scratch capacity: rooms=" + requiredRooms +
            ", doors=" + requiredDoors +
            ", platforms=" + requiredPlatforms + ".");
    }

    private static void EnsureLegacyStaticArray(
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
                "[Expanded Worlds] Legacy Dungeon capacity field " + holder.FullName + "." + field.Name +
                " no longer has the expected static " + elementType.Name + "[] shape. Refusing to guess.");
        }

        Array current = field.GetValue(null) as Array;
        if (current == null)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] Legacy Dungeon capacity field " + holder.FullName + "." + field.Name +
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
/// clearWorld is late enough that the selected expanded dimensions are active.
/// For modern 1.4.5 Terraria this hook validates the dynamic Dungeon storage
/// contract. For legacy layouts it also runs after worldgen bookkeeping resets,
/// so any required fixed-array enlargement survives into the generation passes.
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
