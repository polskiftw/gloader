#if GLOADER
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Terraria;

/// <summary>
/// Terraria 1.4.5.8 keeps several world-sized backing arrays at the dimensions
/// that existed during process startup. Changing Main.maxTilesX/maxTilesY is
/// therefore not sufficient by itself for a world wider than vanilla Large.
///
/// This support is intentionally shared by GLOADER_CLIENT and GLOADER_SERVER:
/// a client needs it for generation, local reload and joining an expanded
/// server; the Host & Play / dedicated-server process needs it while loading
/// the expanded .wld before accepting players.
/// </summary>
internal static class ExpandedWorldBackingStorage
{
    private const string ActiveSectionsTypeName = "Terraria.DataStructures.ActiveSections";
    private const string LeashedEntityTypeName = "Terraria.GameContent.LeashedEntity";

    private static readonly FieldInfo MainMapField = AccessTools.Field(typeof(Main), "Map");

    public static bool IsSupportedExpandedWorld(int width, int height)
    {
        return height == ExpandedWorldMath.LargeHeight &&
               (width == ExpandedWorldMath.XLWidth || width == ExpandedWorldMath.HugeWidth);
    }

    public static int RequiredBackingWidth(int logicalWidth)
    {
        return checked(logicalWidth + 1);
    }

    public static int RequiredBackingHeight(int logicalHeight)
    {
        return checked(logicalHeight + 1);
    }

    public static void EnsureForCurrentDimensions(string stage)
    {
        int width = Main.maxTilesX;
        int height = Main.maxTilesY;

        if (!IsSupportedExpandedWorld(width, height))
            return;

        int requiredWidth = RequiredBackingWidth(width);
        int requiredHeight = RequiredBackingHeight(height);

        EnsureTileStorage(requiredWidth, requiredHeight, stage);

#if GLOADER_CLIENT
        // WorldMap owns a fixed MapTile[,] and exposes no resize operation in
        // exact 1.4.5.8. Use reflection so this shared file does not introduce
        // a client-only Terraria.Map compile-time dependency into the server mod.
        EnsureClientMapStorage(requiredWidth, requiredHeight, stage);
#endif
    }

    private static void EnsureTileStorage(int requiredWidth, int requiredHeight, string stage)
    {
        Tile[,] current = Main.tile;
        if (current != null &&
            current.GetLength(0) >= requiredWidth &&
            current.GetLength(1) >= requiredHeight)
        {
            return;
        }

        // clearWorld is about to discard/clear the previous world's tile state,
        // so copying the old canvas is both unnecessary and very expensive.
        Main.tile = new Tile[requiredWidth, requiredHeight];

        Console.WriteLine(
            "[Expanded Worlds] " + stage + ": tile backing storage enlarged to " +
            requiredWidth + "x" + requiredHeight + ".");
    }

#if GLOADER_CLIENT
    private static void EnsureClientMapStorage(int requiredWidth, int requiredHeight, string stage)
    {
        if (MainMapField == null || !MainMapField.IsStatic)
        {
            throw new MissingFieldException(typeof(Main).FullName, "Map");
        }

        object current = MainMapField.GetValue(null);
        Type mapType = MainMapField.FieldType;
        FieldInfo maxWidthField = AccessTools.Field(mapType, "MaxWidth");
        FieldInfo maxHeightField = AccessTools.Field(mapType, "MaxHeight");

        if (maxWidthField == null || maxHeightField == null ||
            maxWidthField.FieldType != typeof(int) ||
            maxHeightField.FieldType != typeof(int))
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] Terraria WorldMap no longer exposes the audited MaxWidth/MaxHeight shape.");
        }

        if (current != null)
        {
            int currentWidth = (int)maxWidthField.GetValue(current);
            int currentHeight = (int)maxHeightField.GetValue(current);
            if (currentWidth >= requiredWidth && currentHeight >= requiredHeight)
                return;
        }

        ConstructorInfo constructor = AccessTools.Constructor(
            mapType,
            new[] { typeof(int), typeof(int) });
        if (constructor == null)
        {
            throw new MissingMethodException(mapType.FullName, ".ctor(int,int)");
        }

        object replacement = constructor.Invoke(new object[] { requiredWidth, requiredHeight });
        MainMapField.SetValue(null, replacement);

        Console.WriteLine(
            "[Expanded Worlds] " + stage + ": map backing storage enlarged to " +
            requiredWidth + "x" + requiredHeight + ".");
    }
#endif

    public static IEnumerable<MethodBase> GetSectionStorageInitializers()
    {
        string[] typeNames = { ActiveSectionsTypeName, LeashedEntityTypeName };
        for (int i = 0; i < typeNames.Length; i++)
        {
            Type type = AccessTools.TypeByName(typeNames[i]);
            if (type == null)
                throw new TypeLoadException("[Expanded Worlds] Required Terraria type not found: " + typeNames[i]);

            ConstructorInfo initializer = type.TypeInitializer;
            if (initializer == null)
            {
                throw new MissingMethodException(
                    type.FullName,
                    ".cctor");
            }

            yield return initializer;
        }
    }
}

/// <summary>
/// Grow the physical tile/map canvas immediately before Terraria clearWorld()
/// touches it. Priority.Last deliberately runs after ExpandedWorldClearPatch's
/// normal-priority generation-dimension prefix on the client. For .wld loads and
/// multiplayer joins Terraria has already read/received maxTilesX/maxTilesY.
/// </summary>
[HarmonyPatch(typeof(WorldGen), "clearWorld")]
internal static class ExpandedWorldBackingStoragePatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.Last)]
    private static void Prefix()
    {
        ExpandedWorldBackingStorage.EnsureForCurrentDimensions("clearWorld");
    }
}

/// <summary>
/// Two 1.4.5.8 section tables are allocated once during type initialization:
/// ActiveSections.LastActiveTime and LeashedEntity.BySection. The latter is
/// static readonly, so trying to resize it later is runtime-dependent and not a
/// safe contract. Patch both initializers before Terraria's entry point runs and
/// give them the inexpensive Huge section capacity from the start.
///
/// Exact client source contains one Main.maxTilesX and one Main.maxTilesY load
/// in each initializer's section-array allocation. Any shape change fails closed.
/// </summary>
[HarmonyPatch]
internal static class ExpandedWorldSectionStorageInitializerPatch
{
    private static readonly FieldInfo MaxTilesXField = AccessTools.Field(typeof(Main), nameof(Main.maxTilesX));
    private static readonly FieldInfo MaxTilesYField = AccessTools.Field(typeof(Main), nameof(Main.maxTilesY));

    private static IEnumerable<MethodBase> TargetMethods()
    {
        return ExpandedWorldBackingStorage.GetSectionStorageInitializers();
    }

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions,
        MethodBase __originalMethod)
    {
        if (MaxTilesXField == null || MaxTilesYField == null)
            throw new MissingFieldException(typeof(Main).FullName, "maxTilesX/maxTilesY");

        int widthLoads = 0;
        int heightLoads = 0;

        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Ldsfld && Equals(instruction.operand, MaxTilesXField))
            {
                instruction.opcode = OpCodes.Ldc_I4;
                instruction.operand = ExpandedWorldMath.HugeWidth;
                widthLoads++;
            }
            else if (instruction.opcode == OpCodes.Ldsfld && Equals(instruction.operand, MaxTilesYField))
            {
                instruction.opcode = OpCodes.Ldc_I4;
                instruction.operand = ExpandedWorldMath.HugeHeight;
                heightLoads++;
            }

            yield return instruction;
        }

        if (widthLoads != 1 || heightLoads != 1)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] Section-storage initializer shape changed in " +
                (__originalMethod?.DeclaringType?.FullName ?? "<unknown>") +
                ": expected one maxTilesX and one maxTilesY load, found " +
                widthLoads + " and " + heightLoads + ".");
        }
    }
}
#endif
