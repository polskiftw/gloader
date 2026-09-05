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

/// <summary>
/// Extends Terraria 1.4.5.8's New World size selector after BuildPage() has created
/// the three vanilla controls. Terraria's original Small / Medium / Large row is
/// left untouched. Expanded physical presets are rendered in two separate rows
/// beneath it so the vanilla controls keep their native dimensions and the THICC
/// controls have enough room to remain readable.
/// </summary>
[HarmonyPatch]
internal static class ExpandedWorldBuildPageSizeRowFix
{
    private static readonly FieldInfo SizeButtonsField =
        AccessTools.Field(typeof(UIWorldCreation), "_sizeButtons");

    private static readonly FieldInfo DescriptionTextField =
        AccessTools.Field(typeof(UIWorldCreation), "_descriptionText");

    private static readonly MethodInfo UpdatePreviewPlateMethod =
        AccessTools.Method(typeof(UIWorldCreation), "UpdatePreviewPlate", Type.EmptyTypes);

    private static readonly PropertyInfo ParentProperty =
        AccessTools.Property(typeof(UIElement), "Parent");

    private static readonly MethodInfo RemoveMethod =
        AccessTools.Method(typeof(UIElement), "Remove", Type.EmptyTypes);

    private static UIWorldCreation _owner;
    private static UITextPanel<string>[] _expandedButtons = new UITextPanel<string>[0];

    private static MethodBase TargetMethod()
    {
        MethodBase method = AccessTools.GetDeclaredMethods(typeof(UIWorldCreation))
            .FirstOrDefault(candidate =>
                candidate.Name == "BuildPage" &&
                candidate.GetParameters().Length == 0);

        if (method == null)
            throw new MissingMethodException(typeof(UIWorldCreation).FullName, "BuildPage()");

        return method;
    }

    [HarmonyPostfix]
    private static void Postfix(UIWorldCreation __instance)
    {
        try
        {
            Inject(__instance);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[Expanded Worlds] Could not install THICC world-size buttons: " + ex);
        }
    }

    private static void Inject(UIWorldCreation owner)
    {
        if (owner == null)
            throw new ArgumentNullException(nameof(owner));
        if (SizeButtonsField == null)
            throw new MissingFieldException(typeof(UIWorldCreation).FullName, "_sizeButtons");
        if (ParentProperty == null)
            throw new MissingMemberException(typeof(UIElement).FullName, "Parent");

        Array vanillaButtons = SizeButtonsField.GetValue(owner) as Array;
        if (vanillaButtons == null || vanillaButtons.Length < 3)
            throw new InvalidOperationException("Terraria did not expose its three vanilla world-size buttons.");

        UIElement first = vanillaButtons.GetValue(0) as UIElement;
        if (first == null)
            throw new InvalidOperationException("Terraria's Small world-size button was not a UIElement.");

        UIElement container = ParentProperty.GetValue(first, null) as UIElement;
        if (container == null)
            throw new InvalidOperationException("Terraria's world-size row did not have a live parent container.");

        RemoveOwnButtons();

        _owner = owner;
        int expandedCount = ExpandedWorldMath.ExpandedPresetCount;
        _expandedButtons = new UITextPanel<string>[expandedCount];

        const int firstRowCount = 6;
        int secondRowCount = Math.Max(0, expandedCount - firstRowCount);

        // Keep Terraria's native Small / Medium / Large row exactly as-is.
        // The custom rows are positioned relative to the existing buttons so
        // vanilla width, alignment, padding, hitboxes, and snap points remain untouched.
        float buttonHeightPixels = first.Height.Pixels;
        float buttonHeightPercent = first.Height.Precent;
        float vanillaTopPixels = first.Top.Pixels;
        float vanillaTopPercent = first.Top.Precent;
        float rowGapPixels = 8f;

        for (int i = 0; i < expandedCount; i++)
        {
            ExpandedWorldDefinition definition = ExpandedWorldMath.DefinitionAt(i);
            bool secondRow = i >= firstRowCount;
            int rowIndex = secondRow ? i - firstRowCount : i;
            int rowCount = secondRow ? secondRowCount : Math.Min(firstRowCount, expandedCount);
            float topPixels = vanillaTopPixels + (buttonHeightPixels + rowGapPixels) * (secondRow ? 2f : 1f);

            UITextPanel<string> button = MakeButton(
                owner,
                definition,
                i + 3,
                rowIndex,
                rowCount,
                topPixels,
                vanillaTopPercent,
                buttonHeightPixels,
                buttonHeightPercent);

            _expandedButtons[i] = button;
            container.Append(button);
        }

        Refresh(owner);

        Console.WriteLine(
            "[Expanded Worlds] New World size controls installed below vanilla row: THICC-THICC 6 / THICC 7-THICC 11.");
    }

    private static UITextPanel<string> MakeButton(
        UIWorldCreation owner,
        ExpandedWorldDefinition definition,
        int snapIndex,
        int rowIndex,
        int rowCount,
        float topPixels,
        float topPercent,
        float heightPixels,
        float heightPercent)
    {
        var button = new UITextPanel<string>(definition.Label, 0.8f, false);

        const float horizontalGapPixels = 8f;
        float widthPercent = rowCount > 0 ? 1f / rowCount : 1f;
        float widthPixels = -horizontalGapPixels * Math.Max(0, rowCount - 1) / Math.Max(1, rowCount);

        button.Width.Set(widthPixels, widthPercent);
        button.Height.Set(heightPixels, heightPercent);
        button.Top.Set(topPixels, topPercent);
        button.HAlign = rowCount <= 1 ? 0.5f : rowIndex / (float)(rowCount - 1);
        button.SetPadding(0f);
        button.SetSnapPoint("size", snapIndex);

        button.OnLeftClick += delegate
        {
            SelectCustom(owner, definition.Preset);
        };

        button.OnMouseOver += delegate
        {
            SetDescription(owner, DescriptionFor(definition.Preset));
        };

        button.OnMouseOut += delegate
        {
            SetDescription(owner, string.Empty);
        };

        return button;
    }

    private static string DescriptionFor(ExpandedWorldPreset preset)
    {
        if (preset == ExpandedWorldPreset.None)
            return string.Empty;

        ExpandedWorldDefinition definition = ExpandedWorldMath.DefinitionFor(preset);
        return definition.Label + " world: " +
               definition.Width.ToString("N0") + " x " + definition.Height.ToString("N0") +
               " tiles. Vanilla-continuity tier " + definition.OverallTier + ".";
    }

    private static void SelectCustom(UIWorldCreation owner, ExpandedWorldPreset preset)
    {
        ExpandedWorldState.Select(preset);

        // Terraria must continue to see a vanilla Large categorical value. The
        // real physical dimensions are applied when generation is armed.
        WorldGen.SetWorldSize(2);
        DeselectVanillaButtons(owner);
        Refresh(owner);

        try
        {
            UpdatePreviewPlateMethod?.Invoke(owner, null);
        }
        catch
        {
            // The preview plate intentionally remains the vanilla Large plate.
        }

        SoundEngine.PlaySound(SoundID.MenuTick);
        Console.WriteLine("[Expanded Worlds] Selected " + ExpandedWorldState.LabelFor(preset) + ".");
    }

    internal static void ClearCustomSelection(UIWorldCreation owner)
    {
        ExpandedWorldState.ClearSelection();
        Refresh(owner);
    }

    internal static void Refresh(UIWorldCreation owner)
    {
        bool sameOwner = owner != null && ReferenceEquals(owner, _owner);
        for (int i = 0; i < _expandedButtons.Length; i++)
        {
            ExpandedWorldDefinition definition = ExpandedWorldMath.DefinitionAt(i);
            SetButtonVisual(
                _expandedButtons[i],
                sameOwner && ExpandedWorldState.Selected == definition.Preset);
        }

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

        // _sizeButtons is Terraria's original three-element array. Keeping it
        // that way means private enum assumptions elsewhere remain untouched.
        int count = Math.Min(3, vanillaButtons.Length);
        for (int i = 0; i < count; i++)
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
                // Cosmetic only. Generation state is separate.
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

    private static void RemoveOwnButtons()
    {
        if (_expandedButtons == null)
            return;

        for (int i = 0; i < _expandedButtons.Length; i++)
            RemoveOwnButton(_expandedButtons[i]);
    }

    private static void RemoveOwnButton(UIElement button)
    {
        if (button == null || RemoveMethod == null)
            return;

        try
        {
            RemoveMethod.Invoke(button, null);
        }
        catch
        {
        }
    }
}

[HarmonyPatch]
internal static class ExpandedWorldBuildPageVanillaSizeSyncPatch
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
        ExpandedWorldBuildPageSizeRowFix.ClearCustomSelection(__instance);
    }
}

[HarmonyPatch]
internal static class ExpandedWorldBuildPageDefaultSyncPatch
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
        ExpandedWorldBuildPageSizeRowFix.ClearCustomSelection(__instance);
    }
}

[HarmonyPatch]
internal static class ExpandedWorldBuildPageSliderSyncPatch
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
        ExpandedWorldBuildPageSizeRowFix.Refresh(__instance);
    }
}
#endif
