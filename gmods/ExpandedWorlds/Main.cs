#if GLOADER_CLIENT
using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent.UI.Elements;
using Terraria.GameContent.UI.States;
using Terraria.ID;
using Terraria.UI;

public static class Mod
{
    public static void Load()
    {
        Console.WriteLine("[Expanded Worlds] XL (12600x2400) and Huge (16800x2400) world sizes enabled.");
        Console.WriteLine("[Expanded Worlds] Vanilla still categorizes both custom sizes as Large for compatibility.");
    }
}

internal enum ExpandedWorldPreset
{
    None = 0,
    XL = 1,
    Huge = 2,
}

internal static class ExpandedWorldState
{
    public const int VanillaLargeWidth = 8400;
    public const int VanillaLargeHeight = 2400;
    public const int XLWidth = 12600;
    public const int HugeWidth = 16800;

    public static ExpandedWorldPreset Selected { get; private set; }
    public static ExpandedWorldPreset GenerationPreset { get; private set; }

    public static bool IsCustomSelected => Selected != ExpandedWorldPreset.None;
    public static bool GenerationArmed => GenerationPreset != ExpandedWorldPreset.None;

    public static void Select(ExpandedWorldPreset preset)
    {
        Selected = preset;
    }

    public static void ClearSelection()
    {
        Selected = ExpandedWorldPreset.None;
    }

    public static void ArmGeneration()
    {
        GenerationPreset = Selected;
    }

    public static void EndGeneration()
    {
        GenerationPreset = ExpandedWorldPreset.None;
    }

    public static int WidthFor(ExpandedWorldPreset preset)
    {
        switch (preset)
        {
            case ExpandedWorldPreset.XL:
                return XLWidth;
            case ExpandedWorldPreset.Huge:
                return HugeWidth;
            default:
                return VanillaLargeWidth;
        }
    }

    public static string LabelFor(ExpandedWorldPreset preset)
    {
        switch (preset)
        {
            case ExpandedWorldPreset.XL:
                return "XL";
            case ExpandedWorldPreset.Huge:
                return "Huge";
            default:
                return "Vanilla";
        }
    }

    public static void ApplyGenerationDimensions(string stage)
    {
        if (!GenerationArmed)
            return;

        int width = WidthFor(GenerationPreset);
        Main.maxTilesX = width;
        Main.maxTilesY = VanillaLargeHeight;

        // Keep all derived world bounds coherent before tile/section allocation.
        Main.rightWorld = width * 16f;
        Main.bottomWorld = VanillaLargeHeight * 16f;
        Main.maxSectionsX = width / 200;
        Main.maxSectionsY = VanillaLargeHeight / 150;

        // Let Terraria recalculate any additional derived size state it owns.
        // Reflection keeps a visibility change from turning this into a compile-time break.
        MethodInfo setWorldSizeDerived = AccessTools.Method(typeof(WorldGen), "setWorldSize", Type.EmptyTypes);
        if (setWorldSizeDerived != null)
        {
            try
            {
                setWorldSizeDerived.Invoke(null, null);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Expanded Worlds] setWorldSize recalculation failed at " + stage + ": " + ex.GetType().Name);
            }
        }

        Console.WriteLine(
            "[Expanded Worlds] " + stage + ": using " + LabelFor(GenerationPreset) +
            " " + width + "x" + VanillaLargeHeight +
            " (vanilla tier " + WorldGen.GetWorldSize() + ").");
    }
}

/// <summary>
/// Injects two extra choices into Terraria's existing world-size row.
/// We intentionally do NOT add values to Terraria's private WorldSizeId enum.
/// XL/Huge set the vanilla selection to Large, then carry their real dimensions
/// separately until CreateNewWorld. This keeps code that branches on Small /
/// Medium / Large seeing a known vanilla value.
/// </summary>
[HarmonyPatch]
internal static class ExpandedWorldCreationSizeRowPatch
{
    private static readonly FieldInfo SizeButtonsField =
        AccessTools.Field(typeof(UIWorldCreation), "_sizeButtons");

    private static readonly FieldInfo DescriptionTextField =
        AccessTools.Field(typeof(UIWorldCreation), "_descriptionText");

    private static readonly MethodInfo UpdatePreviewPlateMethod =
        AccessTools.Method(typeof(UIWorldCreation), "UpdatePreviewPlate", Type.EmptyTypes);

    private static UIWorldCreation _owner;
    private static UITextPanel<string> _xlButton;
    private static UITextPanel<string> _hugeButton;

    private static MethodBase TargetMethod()
    {
        MethodBase method = AccessTools.GetDeclaredMethods(typeof(UIWorldCreation))
            .FirstOrDefault(candidate => candidate.Name == "AddWorldSizeOptions");

        if (method == null)
            throw new MissingMethodException(typeof(UIWorldCreation).FullName, "AddWorldSizeOptions");

        return method;
    }

    [HarmonyPostfix]
    private static void Postfix(UIWorldCreation __instance, object[] __args)
    {
        try
        {
            UIElement container = __args != null && __args.Length > 0
                ? __args[0] as UIElement
                : null;

            if (container == null || SizeButtonsField == null)
                return;

            Array vanillaButtons = SizeButtonsField.GetValue(__instance) as Array;
            if (vanillaButtons == null || vanillaButtons.Length < 3)
                return;

            UIElement first = vanillaButtons.GetValue(0) as UIElement;
            if (first == null)
                return;

            float usableWidthPercent = 1f;
            if (__args.Length > 4 && __args[4] is float)
                usableWidthPercent = (float)__args[4];

            // Match vanilla's spacing formula, but distribute five choices instead of three.
            const int totalChoices = 5;
            float widthPixels = -4f * (totalChoices - 1);
            float widthPercent = usableWidthPercent / totalChoices;

            for (int i = 0; i < vanillaButtons.Length; i++)
            {
                UIElement button = vanillaButtons.GetValue(i) as UIElement;
                if (button == null)
                    continue;

                button.Width.Set(widthPixels, widthPercent);
                button.HAlign = i / (float)(totalChoices - 1);
            }

            _owner = __instance;
            _xlButton = MakeCustomButton(__instance, first, "XL", ExpandedWorldPreset.XL, 3, widthPixels, widthPercent);
            _hugeButton = MakeCustomButton(__instance, first, "Huge", ExpandedWorldPreset.Huge, 4, widthPixels, widthPercent);

            container.Append(_xlButton);
            container.Append(_hugeButton);
            RefreshVisuals(__instance);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[Expanded Worlds] Could not extend world-size row: " + ex);
        }
    }

    private static UITextPanel<string> MakeCustomButton(
        UIWorldCreation owner,
        UIElement template,
        string text,
        ExpandedWorldPreset preset,
        int index,
        float widthPixels,
        float widthPercent)
    {
        var button = new UITextPanel<string>(text, 0.9f, false);
        button.Width.Set(widthPixels, widthPercent);
        button.Height.Set(template.Height.Pixels, template.Height.Percent);
        button.Top.Set(template.Top.Pixels, template.Top.Percent);
        button.HAlign = index / 4f;
        button.SetPadding(0f);
        button.SetSnapPoint("size", index);

        button.OnLeftClick += delegate
        {
            SelectCustom(owner, preset);
        };

        button.OnMouseOver += delegate
        {
            SetDescription(owner, preset == ExpandedWorldPreset.XL
                ? "XL world: 12,600 x 2,400 tiles. The next exact 4,200-tile horizontal size quantum after Large."
                : "Huge world: 16,800 x 2,400 tiles. Twice the width and tile area of vanilla Large.");
        };

        button.OnMouseOut += delegate
        {
            SetDescription(owner, "");
        };

        return button;
    }

    private static void SelectCustom(UIWorldCreation owner, ExpandedWorldPreset preset)
    {
        ExpandedWorldState.Select(preset);

        // Keep Terraria's own categorical state at vanilla Large. The true width
        // is armed only when CreateNewWorld actually begins.
        WorldGen.SetWorldSize(2);
        DeselectVanillaButtons(owner);
        RefreshVisuals(owner);

        try
        {
            UpdatePreviewPlateMethod?.Invoke(owner, null);
        }
        catch
        {
            // Preview staying on the vanilla Large plate is harmless and intentional.
        }

        SoundEngine.PlaySound(SoundID.MenuTick);
        Console.WriteLine("[Expanded Worlds] Selected " + ExpandedWorldState.LabelFor(preset) + ".");
    }

    internal static void ClearCustomSelection(UIWorldCreation owner)
    {
        ExpandedWorldState.ClearSelection();
        RefreshVisuals(owner);
    }

    internal static void RefreshVisuals(UIWorldCreation owner)
    {
        bool sameOwner = owner != null && ReferenceEquals(owner, _owner);
        SetButtonVisual(_xlButton, sameOwner && ExpandedWorldState.Selected == ExpandedWorldPreset.XL);
        SetButtonVisual(_hugeButton, sameOwner && ExpandedWorldState.Selected == ExpandedWorldPreset.Huge);

        if (sameOwner && ExpandedWorldState.IsCustomSelected)
            DeselectVanillaButtons(owner);
    }

    private static void SetButtonVisual(UITextPanel<string> button, bool selected)
    {
        if (button == null)
            return;

        button.BackgroundColor = selected
            ? new Color(73, 94, 171)
            : new Color(63, 82, 151) * 0.72f;
        button.BorderColor = selected
            ? new Color(255, 240, 140)
            : new Color(89, 116, 213);
    }

    private static void DeselectVanillaButtons(UIWorldCreation owner)
    {
        if (owner == null || SizeButtonsField == null)
            return;

        Array vanillaButtons = SizeButtonsField.GetValue(owner) as Array;
        if (vanillaButtons == null)
            return;

        for (int i = 0; i < vanillaButtons.Length; i++)
        {
            object button = vanillaButtons.GetValue(i);
            if (button == null)
                continue;

            MethodInfo setter = AccessTools.Method(button.GetType(), "SetCurrentOption");
            if (setter == null)
                continue;

            ParameterInfo[] parameters = setter.GetParameters();
            if (parameters.Length != 1 || !parameters[0].ParameterType.IsEnum)
                continue;

            try
            {
                object noVanillaOption = Enum.ToObject(parameters[0].ParameterType, -1);
                setter.Invoke(button, new[] { noVanillaOption });
            }
            catch
            {
                // Cosmetic only. Generation state is independent of the highlight.
            }
        }
    }

    private static void SetDescription(UIWorldCreation owner, string text)
    {
        if (owner == null || DescriptionTextField == null)
            return;

        try
        {
            UIText description = DescriptionTextField.GetValue(owner) as UIText;
            description?.SetText(text ?? string.Empty);
        }
        catch
        {
        }
    }
}

/// <summary>
/// Any click on Terraria's original Small / Medium / Large buttons returns to
/// completely vanilla sizing.
/// </summary>
[HarmonyPatch]
internal static class ExpandedWorldVanillaSizeClickPatch
{
    private static MethodBase TargetMethod()
    {
        MethodBase method = AccessTools.GetDeclaredMethods(typeof(UIWorldCreation))
            .FirstOrDefault(candidate => candidate.Name == "ClickSizeOption");
        if (method == null)
            throw new MissingMethodException(typeof(UIWorldCreation).FullName, "ClickSizeOption");
        return method;
    }

    [HarmonyPostfix]
    private static void Postfix(UIWorldCreation __instance)
    {
        ExpandedWorldCreationSizeRowPatch.ClearCustomSelection(__instance);
    }
}

/// <summary>
/// A newly opened world-creation screen should always start from Terraria's own
/// defaults rather than remembering a custom choice from a previous creation.
/// </summary>
[HarmonyPatch]
internal static class ExpandedWorldDefaultOptionsPatch
{
    private static MethodBase TargetMethod()
    {
        MethodBase method = AccessTools.GetDeclaredMethods(typeof(UIWorldCreation))
            .FirstOrDefault(candidate => candidate.Name == "SetDefaultOptions");
        if (method == null)
            throw new MissingMethodException(typeof(UIWorldCreation).FullName, "SetDefaultOptions");
        return method;
    }

    [HarmonyPostfix]
    private static void Postfix(UIWorldCreation __instance)
    {
        ExpandedWorldCreationSizeRowPatch.ClearCustomSelection(__instance);
    }
}

/// <summary>
/// Terraria refreshes its original size buttons after some UI changes. Reassert
/// the custom highlight without changing any worldgen/seed state.
/// </summary>
[HarmonyPatch]
internal static class ExpandedWorldSliderRefreshPatch
{
    private static MethodBase TargetMethod()
    {
        MethodBase method = AccessTools.GetDeclaredMethods(typeof(UIWorldCreation))
            .FirstOrDefault(candidate => candidate.Name == "UpdateSliders");
        if (method == null)
            throw new MissingMethodException(typeof(UIWorldCreation).FullName, "UpdateSliders");
        return method;
    }

    [HarmonyPostfix]
    private static void Postfix(UIWorldCreation __instance)
    {
        ExpandedWorldCreationSizeRowPatch.RefreshVisuals(__instance);
    }
}

/// <summary>
/// Arm this one generation job and apply the real dimensions at the last safe
/// moment before vanilla begins its asynchronous world-generation pipeline.
/// </summary>
[HarmonyPatch]
internal static class ExpandedWorldCreatePatch
{
    private static MethodBase TargetMethod()
    {
        MethodBase method = AccessTools.GetDeclaredMethods(typeof(WorldGen))
            .FirstOrDefault(candidate => candidate.Name == "CreateNewWorld" && candidate.IsStatic);
        if (method == null)
            throw new MissingMethodException(typeof(WorldGen).FullName, "CreateNewWorld");
        return method;
    }

    [HarmonyPrefix]
    private static void Prefix()
    {
        if (!ExpandedWorldState.IsCustomSelected)
            return;

        ExpandedWorldState.ArmGeneration();
        ExpandedWorldState.ApplyGenerationDimensions("CreateNewWorld");
    }
}

/// <summary>
/// Safety net: clearWorld is where Terraria allocates/clears world storage.
/// Reapply the requested dimensions before that allocation. The generation-only
/// guard is critical because clearWorld is also used while loading existing worlds.
/// </summary>
[HarmonyPatch]
internal static class ExpandedWorldClearPatch
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
        ExpandedWorldState.ApplyGenerationDimensions("clearWorld");
    }
}

/// <summary>
/// Disarm the custom dimensions no matter whether generation succeeds or throws.
/// This prevents later world loads in the same process from inheriting a creation preset.
/// </summary>
[HarmonyPatch]
internal static class ExpandedWorldGenerateWorldLifetimePatch
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
        if (ExpandedWorldState.GenerationArmed)
        {
            if (__exception == null)
            {
                Console.WriteLine(
                    "[Expanded Worlds] Generation finished at " + Main.maxTilesX + "x" + Main.maxTilesY + ".");
            }
            else
            {
                Console.WriteLine("[Expanded Worlds] Generation failed: " + __exception);
            }

            ExpandedWorldState.EndGeneration();
        }

        return __exception;
    }
}
#endif