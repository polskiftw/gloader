#if GLOADER
using System;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Terraria;

/// <summary>
/// Terraria 1.4.5.8 has two audited fixed worldgen record capacities that the
/// canonical expanded tiers can exceed: Floating Island metadata arrays and
/// WorldGen.heartPos. Growing these arrays changes no count, placement, RNG, or
/// seed behavior; it only gives Terraria's own generator enough scratch space.
/// </summary>
internal static class ExpandedWorldGenerationCapacity
{
    private const string GenVarsTypeName = "Terraria.WorldBuilding.GenVars";

    public static void EnsureForCurrentWorld()
    {
        if (!ExpandedWorldGenerationContext.IsActive)
            return;

        Type genVars = AccessTools.TypeByName(GenVarsTypeName)
            ?? throw new TypeLoadException("[Expanded Worlds] Terraria.WorldBuilding.GenVars was not found.");

        EnsureFloatingIslandStorage(genVars);
        EnsureCrimsonHeartStorage();
    }

    private static void EnsureFloatingIslandStorage(Type genVars)
    {
        int required = ExpandedWorldCapacityMath.FloatingIslandScratchCapacity(
            Main.maxTilesX,
            ExpandedWorldGenerationContext.ActiveTier);

        // Vanilla arrays contain 300 records. With the source-backed sky-lake
        // tier continuation and worst-case Error World + Care Bears x10:
        // XL 280, Huge 350, THICC 390.
        EnsureStaticArray(genVars, required, typeof(bool), "skyLake");
        EnsureStaticArray(genVars, required, typeof(int), "floatingIslandHouseX");
        EnsureStaticArray(genVars, required, typeof(int), "floatingIslandHouseY");
        EnsureStaticArray(genVars, required, typeof(int), "floatingIslandStyle");
    }

    private static void EnsureCrimsonHeartStorage()
    {
        int required = ExpandedWorldCapacityMath.CrimsonHeartScratchCapacity(Main.maxTilesX);

        // Vanilla heartPos contains 100 records. Remix worst-case bounds are:
        // XL 80, Huge 96, THICC 112.
        EnsureStaticArray(typeof(WorldGen), required, typeof(Point), "heartPos");
    }

    private static void EnsureStaticArray(Type holder, int requiredLength, Type elementType, string fieldName)
    {
        FieldInfo field = AccessTools.Field(holder, fieldName);
        if (field == null)
            throw new MissingFieldException(holder.FullName, fieldName);

        if (!field.IsStatic || !field.FieldType.IsArray || field.FieldType.GetElementType() != elementType)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] Capacity field " + holder.FullName + "." + field.Name +
                " no longer has the audited static " + elementType.Name + "[] shape.");
        }

        Array current = field.GetValue(null) as Array;
        if (current == null)
            throw new InvalidOperationException("[Expanded Worlds] Capacity field " + holder.FullName + "." + field.Name + " is null.");

        if (current.Length >= requiredLength)
            return;

        Array replacement = Array.CreateInstance(elementType, requiredLength);
        Array.Copy(current, replacement, current.Length);
        field.SetValue(null, replacement);

        Console.WriteLine(
            "[Expanded Worlds] Expanded " + holder.FullName + "." + field.Name +
            " scratch capacity from " + current.Length + " to " + requiredLength + ".");
    }
}

/// <summary>
/// clearWorld is late enough that physical dimensions are active and early
/// enough that generation passes have consumed either fixed record array.
/// Client and server share this exact capacity contract.
/// </summary>
[HarmonyPatch(typeof(WorldGen), "clearWorld")]
internal static class ExpandedWorldGenerationCapacityPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        ExpandedWorldGenerationCapacity.EnsureForCurrentWorld();
    }
}
#endif
