#if GLOADER_SERVER
using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Terraria;

/// <summary>
/// Dedicated-server entry point for Expanded Worlds.
///
/// The normal client UI remains the primary way to select XL/Huge/THICC. For
/// headless generation, set GLOADER_EXPANDED_WORLD to XL, HUGE, or THICC and
/// launch the vanilla dedicated server with its normal Large autocreate path.
/// Terraria still sees every custom world categorically as Large; only the
/// physical dimensions change.
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
    Thicc = 3,
}

internal static class ExpandedWorldServerState
{
    internal const int VanillaLargeWidth = 8400;
    internal const int VanillaLargeHeight = 2400;
    internal const int XLWidth = 12600;
    internal const int XLHeight = 2400;
    internal const int HugeWidth = 16800;
    internal const int HugeHeight = 2400;
    internal const int ThiccWidth = 16800;
    internal const int ThiccHeight = 4800;

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
            case "THICC":
                Requested = ExpandedWorldServerPreset.Thicc;
                break;
            default:
                throw new ArgumentException(
                    "GLOADER_EXPANDED_WORLD must be XL, HUGE, or THICC; received '" + raw + "'.");
        }

        Console.WriteLine(
            "[Expanded Worlds] Dedicated-server headless preset: " +
            LabelFor(Requested) + " " + WidthFor(Requested) + "x" + HeightFor(Requested) + ".");
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
            case ExpandedWorldServerPreset.Thicc:
                return ThiccWidth;
            default:
                return VanillaLargeWidth;
        }
    }

    internal static int HeightFor(ExpandedWorldServerPreset preset)
    {
        switch (preset)
        {
            case ExpandedWorldServerPreset.XL:
                return XLHeight;
            case ExpandedWorldServerPreset.Huge:
                return HugeHeight;
            case ExpandedWorldServerPreset.Thicc:
                return ThiccHeight;
            default:
                return VanillaLargeHeight;
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
            case ExpandedWorldServerPreset.Thicc:
                return "THICC";
            default:
                return "Vanilla";
        }
    }

    internal static void ApplyGenerationDimensions(string stage)
    {
        if (!GenerationArmed)
            return;

        int width = WidthFor(Requested);
        int height = HeightFor(Requested);
        Main.maxTilesX = width;
        Main.maxTilesY = height;

        // Mirror Terraria.WorldGen.setWorldSize's directly-audited derived state
        // before clearWorld allocates/clears world storage.
        Main.rightWorld = width * 16f;
        Main.bottomWorld = height * 16f;
        Main.maxSectionsX = width / 200;
        Main.maxSectionsY = height / 150;

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

            setMetadataSize.Invoke(worldFileData, new object[] { width, height });
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
            " " + width + "x" + height + " (vanilla tier " + vanillaTier + ").");
    }

    internal static void VerifyLoadedDimensions(string stage)
    {
        if (Requested == ExpandedWorldServerPreset.None)
            return;

        int expectedWidth = WidthFor(Requested);
        int expectedHeight = HeightFor(Requested);
        if (Main.maxTilesX != expectedWidth || Main.maxTilesY != expectedHeight)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] " + stage + " dimension verification failed. Expected " +
                expectedWidth + "x" + expectedHeight + ", got " +
                Main.maxTilesX + "x" + Main.maxTilesY + ".");
        }

        int vanillaTier = WorldGen.GetWorldSize();
        if (vanillaTier != 2)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] " + stage + " loaded world is no longer categorically Large; GetWorldSize()=" +
                vanillaTier + ".");
        }

        Console.WriteLine(
            "[Expanded Worlds] " + stage + " verified " + LabelFor(Requested) + " " +
            Main.maxTilesX + "x" + Main.maxTilesY + " (vanilla tier " + vanillaTier + ").");
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

/// <summary>
/// Validate the dimensions that Terraria itself reports after its world-load path
/// completes. This covers both the just-generated autocreate path and a later
/// process that reloads the saved .wld from disk.
/// </summary>
[HarmonyPatch]
internal static class ExpandedWorldServerLoadVerificationPatch
{
    private static MethodBase TargetMethod()
    {
        Type worldFileType = typeof(Main).Assembly.GetType("Terraria.IO.WorldFile", false);
        if (worldFileType == null)
            throw new TypeLoadException("Terraria.IO.WorldFile was not found in the loaded Terraria assembly.");

        MethodBase method = AccessTools.Method(worldFileType, "LoadWorld", Type.EmptyTypes);
        if (method == null)
            throw new MissingMethodException(worldFileType.FullName, "LoadWorld()");

        return method;
    }

    [HarmonyPostfix]
    private static void Postfix()
    {
        ExpandedWorldServerState.VerifyLoadedDimensions("LoadWorld");
    }
}
#endif