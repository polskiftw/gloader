using System;
using System.Collections.Generic;
using AuthenticRaces.Core;
using AuthenticRaces.Rendering;
using HarmonyLib;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;

internal static class Program
{
    private static int Main()
    {
        RaceRegistry.Initialize();
        RacePlayerState.ResetAll();
        RaceAppearanceState.ResetAll();
        RaceRendererRegistry.Initialize();

        try
        {
            new Harmony("gloader.tests.authentic-races-client").PatchAll(typeof(RaceRegistry).Assembly);
        }
        catch (Exception ex)
        {
            return Fail(1, "AuthenticRaces client Harmony targets did not resolve: " + ex);
        }

        var playerTexture = new Texture2D();
        var hairTexture = new Texture2D();
        var hairAltTexture = new Texture2D();
        var unrelatedTexture = new Texture2D();
        var replacementTexture = new Texture2D();

        const int skinVariant = 0;
        const int playerTextureSlot = 3;
        const int hair = 7;

        TextureAssets.Players[skinVariant, playerTextureSlot] = new Asset<Texture2D>(playerTexture);
        TextureAssets.PlayerHair[hair] = new Asset<Texture2D>(hairTexture);
        TextureAssets.PlayerHairAlt[hair] = new Asset<Texture2D>(hairAltTexture);

        var player = new Player { hair = hair };
        var drawInfo = new PlayerDrawSet {
            drawPlayer = player,
            skinVar = skinVariant,
            DrawDataCache = new List<DrawData> {
                new DrawData(playerTexture),
                new DrawData(hairTexture),
                new DrawData(hairAltTexture),
                new DrawData(unrelatedTexture)
            }
        };

        if (!VanillaPlayerDrawClassifier.TryClassify(
                ref drawInfo,
                drawInfo.DrawDataCache[0],
                out var playerSource) ||
            playerSource.Kind != VanillaPlayerDrawKind.PlayerTexture ||
            playerSource.Slot != playerTextureSlot)
        {
            return Fail(2, "Vanilla player texture record was not classified by skin slot.");
        }

        if (!VanillaPlayerDrawClassifier.TryClassify(
                ref drawInfo,
                drawInfo.DrawDataCache[1],
                out var hairSource) ||
            hairSource.Kind != VanillaPlayerDrawKind.Hair ||
            hairSource.Slot != hair)
        {
            return Fail(3, "Vanilla primary hair record was not classified.");
        }

        if (!VanillaPlayerDrawClassifier.TryClassify(
                ref drawInfo,
                drawInfo.DrawDataCache[2],
                out var hairAltSource) ||
            hairAltSource.Kind != VanillaPlayerDrawKind.HairAlt ||
            hairAltSource.Slot != hair)
        {
            return Fail(4, "Vanilla alternate hair record was not classified.");
        }

        if (VanillaPlayerDrawClassifier.TryClassify(
                ref drawInfo,
                drawInfo.DrawDataCache[3],
                out _))
        {
            return Fail(5, "Unrelated draw data must remain outside the race-owned classifier.");
        }

        var renderer = new ProbeRenderer(replacementTexture);
        RaceRendererRegistry.Register("MrPlagueRaces/Human", renderer, replace: true);

        PlayerDrawLayers.DrawPlayer_RenderAllLayers(ref drawInfo);

        if (renderer.InvocationCount != 1)
            return Fail(6, "Pre-render race pipeline did not run exactly once.");
        if (!ReferenceEquals(drawInfo.DrawDataCache[0].texture, replacementTexture))
            return Fail(7, "Race renderer did not rewrite the finished vanilla draw record in place.");
        if (!ReferenceEquals(drawInfo.DrawDataCache[3].texture, unrelatedTexture))
            return Fail(8, "Race renderer seam disturbed an unrelated draw record.");
        if (renderer.RaceIdentity != "MrPlagueRaces/Human")
            return Fail(9, "Selected upstream-compatible race identity did not reach the renderer.");
        if (!renderer.Appearance.Equals(RaceAppearanceData.Default))
            return Fail(10, "Per-player appearance state did not reach the renderer.");

        // Re-initializing the renderer registry restores Human's intentional no-op renderer.
        RaceRendererRegistry.Initialize();
        var original = drawInfo.DrawDataCache[0];
        original.texture = playerTexture;
        drawInfo.DrawDataCache[0] = original;
        PlayerDrawLayers.DrawPlayer_RenderAllLayers(ref drawInfo);

        if (renderer.InvocationCount != 1)
            return Fail(11, "Human pass-through reset did not replace the probe renderer.");
        if (!ReferenceEquals(drawInfo.DrawDataCache[0].texture, playerTexture))
            return Fail(12, "Human pass-through renderer must leave vanilla draw data untouched.");

        Console.WriteLine("PASS: AuthenticRaces client render hook, classifier, registry, and in-place rewrite seam are deterministic.");
        return 0;
    }

    private static int Fail(int code, string message)
    {
        Console.Error.WriteLine(message);
        return code;
    }

    private sealed class ProbeRenderer : RaceRenderer
    {
        private readonly Texture2D _replacement;

        public ProbeRenderer(Texture2D replacement)
        {
            _replacement = replacement;
        }

        public int InvocationCount { get; private set; }
        public string RaceIdentity { get; private set; }
        public RaceAppearanceData Appearance { get; private set; }

        public override void Rewrite(
            ref PlayerDrawSet drawInfo,
            Race race,
            RaceAppearanceData appearance)
        {
            InvocationCount++;
            RaceIdentity = race.UpstreamFullName;
            Appearance = appearance;

            var item = drawInfo.DrawDataCache[0];
            item.texture = _replacement;
            drawInfo.DrawDataCache[0] = item;
        }
    }
}
