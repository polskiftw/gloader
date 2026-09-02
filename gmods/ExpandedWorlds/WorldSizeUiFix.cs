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
/// Retail-client fallback for the New World size row.
///
/// Terraria 1.4.5.8 builds the vanilla Small/Medium/Large buttons inside
/// UIWorldCreation.BuildPage().  The older Expanded Worlds hook tried to recover
/// that row from AddWorldSizeOptions' runtime argument array.  On the retail
/// client that path can leave the custom controls absent even though the mod is
/// otherwise loaded.  This postfix runs after the whole page exists, gets the
/// row directly from the live vanilla buttons, and installs XL/Huge/THICC there.
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
    private static UITextPanel<string> _xlButton;
    private static UITextPanel<string> _hugeButton;
    private static UITextPanel<string> _thiccButton;

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
            Console.WriteLine("[Expanded Worlds] Could not install XL/Huge/THICC world-size buttons: " + ex);
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

        // The original AddWorldSizeOptions hook may have managed to create its
        // temporary panels on some clients. Remove those first so this exact
        // BuildPage-based path is the single visible row.
        RemoveLegacyButton("_xlButton");
        RemoveLegacyButton("_hugeButton");
        RemoveLegacyButton("_thiccButton");

        RemoveOwnButton(_xlButton);
        RemoveOwnButton(_hugeButton);
        RemoveOwnButton(_thiccButton);

        const int totalChoices = 6;
        const int lastIndex = totalChoices - 1;
        const float usableWidthPercent = 1f;
        float widthPixels = -4f * lastIndex;
        float widthPercent = usableWidthPercent / totalChoices;

        for (int i = 0; i < 3; i++)
        {
            UIElement button = vanillaButtons.GetValue(i) as UIElement;
            if (button == null)
                continue;

            button.Width.Set(widthPixels, widthPercent);
            button.HAlign = i / (float)lastIndex;
        }

        _owner = owner;
        _xlButton = MakeButton(owner, first, "XL", ExpandedWorldPreset.XL, 3, lastIndex, widthPixels, widthPercent);
        _hugeButton = MakeButton(owner, first, "Huge", ExpandedWorldPreset.Huge, 4, lastIndex, widthPixels, widthPercent);
        _thiccButton = MakeButton(owner, first, "THICC", ExpandedWorldPreset.Thicc, 5, lastIndex, widthPixels, widthPercent);

        container.Append(_xlButton);
        container.Append(_hugeButton);
        container.Append(_thiccButton);
        Refresh(owner);

        Console.WriteLine("[Expanded Worlds] New World size row installed: Small | Medium | Large | XL | Huge | THICC.");
    }

    private static UITextPanel<string> MakeButton(
        UIWorldCreation owner,
        UIElement template,
        string text,
        ExpandedWorldPreset preset,
        int index,
        int lastIndex,
        float widthPixels,
        float widthPercent)
    {
        var button = new UITextPanel<string>(text, 0.8f, false);
        button.Width.Set(widthPixels, widthPercent);
        button.Height.Set(template.Height.Pixels, template.Height.Percent);
        button.Top.Set(template.Top.Pixels, template.Top.Percent);
        button.HAlign = index / (float)lastIndex;
        button.SetPadding(0f);
        button.SetSnapPoint("size", index);

        button.OnLeftClick += delegate
        {
            SelectCustom(owner, preset);
        };

        button.OnMouseOver += delegate
        {
            SetDescription(owner, DescriptionFor(preset));
        };

        button.OnMouseOut += delegate
        {
            SetDescription(owner, string.Empty);
        };

        return button;
    }

    private static string DescriptionFor(ExpandedWorldPreset preset)
    {
        switch (preset)
        {
            case ExpandedWorldPreset.XL:
                return "XL world: 12,600 x 2,400 tiles.";
            case ExpandedWorldPreset.Huge:
                return "Huge world: 16,800 x 2,400 tiles.";
            case ExpandedWorldPreset.Thicc:
                return "THICC world: 16,800 x 4,800 tiles.";
            default:
                return string.Empty;
        }
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
        SetButtonVisual(_xlButton, sameOwner && ExpandedWorldState.Selected == ExpandedWorldPreset.XL);
        SetButtonVisual(_hugeButton, sameOwner && ExpandedWorldState.Selected == ExpandedWorldPreset.Huge);
        SetButtonVisual(_thiccButton, sameOwner && ExpandedWorldState.Selected == ExpandedWorldPreset.Thicc);

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

    private static void RemoveLegacyButton(string fieldName)
    {
        try
        {
            FieldInfo field = AccessTools.Field(typeof(ExpandedWorldCreationSizeRowPatch), fieldName);
            UIElement button = field?.GetValue(null) as UIElement;
            RemoveOwnButton(button);
            field?.SetValue(null, null);
        }
        catch
        {
        }
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
