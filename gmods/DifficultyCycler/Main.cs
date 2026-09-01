#if GLOADER_CLIENT
using System;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent.UI.Elements;
using Terraria.ID;
using Terraria.IO;
using Terraria.UI;

public static class Mod
{
    public static void Load()
    {
        Console.WriteLine("[Difficulty Cycler] Character-select difficulty button enabled.");
    }
}

[HarmonyPatch]
internal static class DifficultyCyclerCharacterListPatch
{
    private const string TooltipText = "Cycle character difficulty";
    private const string RandomizeTexturePath = "Images/UI/CharCreation/Randomize";
    private const string FallbackTexturePath = "Images/UI/ButtonRename";

    private static readonly FieldInfo DataField =
        AccessTools.Field(typeof(UICharacterListItem), "_data")
        ?? throw new MissingFieldException(typeof(UICharacterListItem).FullName, "_data");

    private static readonly FieldInfo ButtonLabelField =
        AccessTools.Field(typeof(UICharacterListItem), "_buttonLabel")
        ?? throw new MissingFieldException(typeof(UICharacterListItem).FullName, "_buttonLabel");

    private static MethodBase TargetMethod()
    {
        return AccessTools.Constructor(
                   typeof(UICharacterListItem),
                   new[] { typeof(PlayerFileData), typeof(int) })
               ?? throw new MissingMethodException(
                   typeof(UICharacterListItem).FullName,
                   ".ctor(PlayerFileData, int)");
    }

    [HarmonyPostfix]
    private static void Postfix(UICharacterListItem __instance)
    {
        if (__instance == null)
            return;

        var data = DataField.GetValue(__instance) as PlayerFileData;
        var buttonLabel = ButtonLabelField.GetValue(__instance) as UIText;
        if (data?.Player == null || buttonLabel == null)
            return;

        var button = new UIImageButton(LoadButtonTexture()) {
            VAlign = 1f
        };

        // Vanilla leaves a four-pixel gap between its left-side action buttons and
        // the hover label. Put our 20x20 randomize button in that gap position, then
        // move the label right by one normal 24-pixel button slot.
        float labelLeftPixels = buttonLabel.Left.Pixels;
        float labelLeftPercent = buttonLabel.Left.Percent;
        button.Left.Set(labelLeftPixels - 4f, labelLeftPercent);
        buttonLabel.Left.Set(labelLeftPixels + 24f, labelLeftPercent);

        button.OnMouseOver += (_, _) => buttonLabel.SetText(TooltipText);
        button.OnMouseOut += (_, _) => buttonLabel.SetText("");
        button.OnLeftClick += (_, _) => CycleDifficulty(data);

        __instance.Append(button);
    }

    private static Asset<Texture2D> LoadButtonTexture()
    {
        try
        {
            return Main.Assets.Request<Texture2D>(RandomizeTexturePath, AssetRequestMode.ImmediateLoad);
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                "[Difficulty Cycler] Could not load vanilla randomize icon '" +
                RandomizeTexturePath + "' (" + ex.GetType().Name + "). Falling back to rename icon.");
            return Main.Assets.Request<Texture2D>(FallbackTexturePath, AssetRequestMode.ImmediateLoad);
        }
    }

    private static void CycleDifficulty(PlayerFileData data)
    {
        if (data?.Player == null)
            return;

        byte current = data.Player.difficulty;
        if (current > 3)
        {
            Console.WriteLine(
                "[Difficulty Cycler] Refusing to cycle unexpected difficulty value " + current +
                " for player '" + data.Player.name + "'.");
            return;
        }

        data.Player.difficulty = (byte)((current + 1) % 4);
        SoundEngine.PlaySound(SoundID.MenuTick);

        // Use Terraria's own serializer so inventory, stats, research, cloud/local
        // handling, backups, and every unrelated player field are preserved normally.
        Player.SavePlayer(data, false);

        // Rebuild the character-select UI immediately. The row's vanilla difficulty
        // text/color is therefore refreshed from the newly saved difficulty value.
        Main.OpenCharacterSelectUI();
    }
}
#endif
