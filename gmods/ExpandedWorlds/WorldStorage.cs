#if GLOADER
using System;
using System.Collections.Generic;
using System.Linq;
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

#if GLOADER_CLIENT
/// <summary>
/// Terraria's client map has a second world-width ceiling independent of
/// WorldMap._tiles. In exact 1.4.5.8 MapRenderer allocates a 5x2 render-target
/// grid and DrawMap literally iterates X target indices 0..4. Large fits because
/// 8400 tiles occupies targets 0..4 at 2000 tiles each; XL requires 0..6 and
/// Huge requires 0..8.
///
/// The renderer also treats its final allocated X column specially as a 400-tile
/// tail. Huge's actual tail at index 8 is 800 tiles, so simply changing 5 -> 9
/// would retain a hidden truncation. We allocate one unused guard column (10
/// total) so target 8 takes the ordinary full-width path. No RenderTarget2D is
/// created for guard column 9 because current-world loops stop at the physical
/// world-derived target index.
/// </summary>
internal static class ExpandedWorldMapRendererContract
{
    public const int VanillaTargetColumns = 5;
    public const int HugeLastRenderableTargetIndex = ExpandedWorldMath.HugeWidth / 2000;
    public const int BackingTargetColumns = HugeLastRenderableTargetIndex + 2;

    public static Type RequireMapRendererType()
    {
        Type type = AccessTools.TypeByName("Terraria.MapRenderer");
        if (type == null)
            throw new TypeLoadException("[Expanded Worlds] Terraria.MapRenderer was not found.");
        return type;
    }

    public static FieldInfo RequireTargetColumnsField()
    {
        Type type = RequireMapRendererType();
        FieldInfo field = AccessTools.Field(type, "numTargetsX");
        if (field == null || field.FieldType != typeof(int) || !field.IsStatic)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] MapRenderer.numTargetsX no longer matches the audited static Int32 field.");
        }
        return field;
    }

    public static bool IsIntConstant(CodeInstruction instruction, int expected)
    {
        if (instruction.opcode == OpCodes.Ldc_I4 && instruction.operand is int)
            return (int)instruction.operand == expected;
        if (instruction.opcode == OpCodes.Ldc_I4_S && instruction.operand is sbyte)
            return (sbyte)instruction.operand == expected;
        if (expected == -1 && instruction.opcode == OpCodes.Ldc_I4_M1) return true;
        if (expected == 0 && instruction.opcode == OpCodes.Ldc_I4_0) return true;
        if (expected == 1 && instruction.opcode == OpCodes.Ldc_I4_1) return true;
        if (expected == 2 && instruction.opcode == OpCodes.Ldc_I4_2) return true;
        if (expected == 3 && instruction.opcode == OpCodes.Ldc_I4_3) return true;
        if (expected == 4 && instruction.opcode == OpCodes.Ldc_I4_4) return true;
        if (expected == 5 && instruction.opcode == OpCodes.Ldc_I4_5) return true;
        if (expected == 6 && instruction.opcode == OpCodes.Ldc_I4_6) return true;
        if (expected == 7 && instruction.opcode == OpCodes.Ldc_I4_7) return true;
        if (expected == 8 && instruction.opcode == OpCodes.Ldc_I4_8) return true;
        return false;
    }

    public static void ReplaceWithIntConstant(CodeInstruction instruction, int value)
    {
        instruction.opcode = OpCodes.Ldc_I4;
        instruction.operand = value;
    }
}

[HarmonyPatch]
internal static class ExpandedWorldMapRendererInitializerPatch
{
    private static readonly FieldInfo TargetColumnsField =
        ExpandedWorldMapRendererContract.RequireTargetColumnsField();

    private static MethodBase TargetMethod()
    {
        Type type = ExpandedWorldMapRendererContract.RequireMapRendererType();
        ConstructorInfo initializer = type.TypeInitializer;
        if (initializer == null)
            throw new MissingMethodException(type.FullName, ".cctor");
        return initializer;
    }

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions,
        MethodBase __originalMethod)
    {
        var code = instructions.ToList();
        int patched = 0;

        for (int i = 1; i < code.Count; i++)
        {
            if (code[i].opcode != OpCodes.Stsfld || !Equals(code[i].operand, TargetColumnsField))
                continue;

            if (!ExpandedWorldMapRendererContract.IsIntConstant(
                    code[i - 1],
                    ExpandedWorldMapRendererContract.VanillaTargetColumns))
            {
                throw new InvalidOperationException(
                    "[Expanded Worlds] MapRenderer.numTargetsX initializer no longer stores audited value 5.");
            }

            ExpandedWorldMapRendererContract.ReplaceWithIntConstant(
                code[i - 1],
                ExpandedWorldMapRendererContract.BackingTargetColumns);
            patched++;
        }

        if (patched != 1)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] MapRenderer initializer shape changed in " +
                (__originalMethod?.DeclaringType?.FullName ?? "Terraria.MapRenderer") +
                ": expected one numTargetsX assignment, found " + patched + ".");
        }

        return code;
    }
}

[HarmonyPatch]
internal static class ExpandedWorldMapRendererDrawPatch
{
    private static MethodBase TargetMethod()
    {
        Type type = ExpandedWorldMapRendererContract.RequireMapRendererType();
        MethodInfo method = AccessTools.Method(
            type,
            "DrawMap",
            new[]
            {
                typeof(float), typeof(float), typeof(float), typeof(float), typeof(float),
                typeof(float), typeof(float), typeof(float), typeof(float), typeof(byte)
            });
        if (method == null)
        {
            throw new MissingMethodException(
                type.FullName,
                "DrawMap(float,float,float,float,float,float,float,float,float,byte)");
        }
        return method;
    }

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions,
        MethodBase __originalMethod)
    {
        var code = instructions.ToList();
        int patched = 0;

        // Exact 1.4.5.8 source contains exactly one integer literal 4 here:
        //   for (int i = 0; i <= 4; i++)
        // Continue the renderer through Huge's final physical target index 8.
        for (int i = 0; i < code.Count; i++)
        {
            if (!ExpandedWorldMapRendererContract.IsIntConstant(code[i], 4))
                continue;

            ExpandedWorldMapRendererContract.ReplaceWithIntConstant(
                code[i],
                ExpandedWorldMapRendererContract.HugeLastRenderableTargetIndex);
            patched++;
        }

        if (patched != 1)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] MapRenderer.DrawMap source shape changed in " +
                (__originalMethod?.DeclaringType?.FullName ?? "Terraria.MapRenderer") +
                ": expected exactly one integer literal 4 loop bound, found " + patched + ".");
        }

        return code;
    }
}
#endif
#endif
