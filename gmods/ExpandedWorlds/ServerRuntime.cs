#if GLOADER_SERVER
using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Terraria;

/// <summary>
/// Dedicated-server entry point for Expanded Worlds.
///
/// The normal client UI remains the primary way to select XL/Huge. For headless
/// generation, set GLOADER_EXPANDED_WORLD to XL or HUGE and launch the vanilla
/// dedicated server with its normal Large autocreate path. Terraria still sees
/// the custom world categorically as Large; only the physical dimensions change.
/// </summary>
public static class Mod
{
    public static void Load()
    {
        ExpandedWorldServerState.ConfigureFromEnvironment();
    }
}

internal enum ExpandedWorldServerPreset
{
    None = 0,
    XL = 1,
    Huge = 2,
}

internal static class ExpandedWorldServerState
{
    internal const int VanillaLargeWidth = 8400;
    internal const int VanillaLargeHeight = 2400;
    internal const int XLWidth = 12600;
    internal const int HugeWidth = 16800;

    internal static ExpandedWorldServerPreset Requested { get; private set; }
    internal static bool GenerationArmed { get; private set; }

    internal static void ConfigureFromEnvironment()
    {
        string raw = Environment.GetEnvironmentVariable("GLOADER_EXPANDED_WORLD");
        if (string.IsNullOrWhiteSpace(raw))
        {
            Requested = ExpandedWorldServerPreset.None;
            Console.WriteLine("[Expanded Worlds] Dedicated-server headless preset not requested; vanilla server sizing is untouched.");
            return;
        }

        switch (raw.Trim().ToUpperInvariant())
        {
            case "XL":
                Requested = ExpandedWorldServerPreset.XL;
                break;
            case "HUGE":
                Requested = ExpandedWorldServerPreset.Huge;
                break;
            default:
                throw new ArgumentException(
                    "GLOADER_EXPANDED_WORLD must be XL or HUGE; received '" + raw + "'.");
        }

        Console.WriteLine(
            "[Expanded Worlds] Dedicated-server headless preset: " +
            LabelFor(Requested) + " " + WidthFor(Requested) + "x" + VanillaLargeHeight + ".");
    }

    internal static void BeginGeneration()
    {
        if (Requested == ExpandedWorldServerPreset.None)
            return;

        GenerationArmed = true;
        ApplyGenerationDimensions("GenerateWorld");
    }

    internal static void EndGeneration()
    {
        GenerationArmed = false;
    }

    internal static int WidthFor(ExpandedWorldServerPreset preset)
    {
        switch (preset)
        {
            case ExpandedWorldServerPreset.XL:
                return XLWidth;
            case ExpandedWorldServerPreset.Huge:
                return HugeWidth;
            default:
                return VanillaLargeWidth;
        }
    }

    internal static string LabelFor(ExpandedWorldServerPreset preset)
    {
        switch (preset)
        {
            case ExpandedWorldServerPreset.XL:
                return "XL";
            case ExpandedWorldServerPreset.Huge:
                return "Huge";
            default:
                return "Vanilla";
        }
    }

    internal static void ApplyGenerationDimensions(string stage)
    {
        if (!GenerationArmed)
            return;

        int width = WidthFor(Requested);
        Main.maxTilesX = width;
        Main.maxTilesY = VanillaLargeHeight;

        // Mirror Terraria.WorldGen.setWorldSize's directly-audited derived state
        // before clearWorld allocates/clears world storage.
        Main.rightWorld = width * 16f;
        Main.bottomWorld = VanillaLargeHeight * 16f;
        Main.maxSectionsX = width / 200;
        Main.maxSectionsY = VanillaLargeHeight / 150;

        MethodInfo setWorldSizeDerived = AccessTools.Method(typeof(WorldGen), "setWorldSize", Type.EmptyTypes);
        if (setWorldSizeDerived == null)
            throw new MissingMethodException(typeof(WorldGen).FullName, "setWorldSize()");

        setWorldSizeDerived.Invoke(null, null);

        object worldFileData = Main.ActiveWorldFileData;
        if (worldFileData != null)
        {
            MethodInfo setMetadataSize = AccessTools.Method(
                worldFileData.GetType(),
                "SetWorldSize",
                new[] { typeof(int), typeof(int) });

            if (setMetadataSize == null)
            {
                throw new MissingMethodException(
                    worldFileData.GetType().FullName,
                    "SetWorldSize(int,int)");
            }

            setMetadataSize.Invoke(worldFileData, new object[] { width, VanillaLargeHeight });
        }

        int vanillaTier = WorldGen.GetWorldSize();
        if (vanillaTier != 2)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] Custom server world stopped categorizing as vanilla Large; GetWorldSize()=" +
                vanillaTier + ".");
        }

        Console.WriteLine(
            "[Expanded Worlds] " + stage + ": using " + LabelFor(Requested) +
            " " + width + "x" + VanillaLargeHeight + " (vanilla tier " + vanillaTier + ").");
    }
}

/// <summary>
/// TerrariaServer autocreate reaches WorldGen.GenerateWorld directly rather than
/// the client's WorldGen.CreateNewWorld wrapper. Arm the requested physical size
/// at that server-specific last safe point, before GenerateWorld calls clearWorld.
/// </summary>
[HarmonyPatch]
internal static class ExpandedWorldServerGenerateBeginPatch
{
    private static MethodBase TargetMethod()
    {
        MethodBase method = AccessTools.GetDeclaredMethods(typeof(WorldGen))
            .FirstOrDefault(candidate => candidate.Name == "GenerateWorld" && candidate.IsStatic);

        if (method == null)
            throw new MissingMethodException(typeof(WorldGen).FullName, "GenerateWorld");

        return method;
    }

    [HarmonyPrefix]
    private static void Prefix()
    {
        ExpandedWorldServerState.BeginGeneration();
    }
}

/// <summary>
/// Reassert dimensions immediately before Terraria allocates its world storage.
/// </summary>
[HarmonyPatch]
internal static class ExpandedWorldServerClearPatch
{
    private static MethodBase TargetMethod()
    {
        MethodBase method = AccessTools.GetDeclaredMethods(typeof(WorldGen))
            .FirstOrDefault(candidate => candidate.Name == "clearWorld" && candidate.IsStatic);

        if (method == null)
            throw new MissingMethodException(typeof(WorldGen).FullName, "clearWorld");

        return method;
    }

    [HarmonyPrefix]
    private static void Prefix()
    {
        ExpandedWorldServerState.ApplyGenerationDimensions("clearWorld");
    }
}

/// <summary>
/// Never leak one headless generation request into subsequent world activity in
/// the same server process, including exception paths.
/// </summary>
[HarmonyPatch]
internal static class ExpandedWorldServerGenerateEndPatch
{
    private static MethodBase TargetMethod()
    {
        MethodBase method = AccessTools.GetDeclaredMethods(typeof(WorldGen))
            .FirstOrDefault(candidate => candidate.Name == "GenerateWorld" && candidate.IsStatic);

        if (method == null)
            throw new MissingMethodException(typeof(WorldGen).FullName, "GenerateWorld");

        return method;
    }

    [HarmonyFinalizer]
    private static Exception Finalizer(Exception __exception)
    {
        if (ExpandedWorldServerState.GenerationArmed)
        {
            if (__exception == null)
            {
                Console.WriteLine(
                    "[Expanded Worlds] Dedicated-server generation completed at " +
                    Main.maxTilesX + "x" + Main.maxTilesY + ".");
            }
            else
            {
                Console.WriteLine("[Expanded Worlds] Dedicated-server generation failed: " + __exception);
            }

            ExpandedWorldServerState.EndGeneration();
        }

        return __exception;
    }
}
#endif
