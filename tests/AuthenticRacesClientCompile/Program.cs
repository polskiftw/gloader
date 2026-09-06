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
        RaceTextureLoader.Initialize(AppDomain.CurrentDomain.BaseDirectory);

        if (string.IsNullOrWhiteSpace(RaceTextureLoader.AssetRoot))
            return Fail(1, "AuthenticRaces client fixture could not discover the staged race asset root.");

        try
        {
            new Harmony("gloader.tests.authentic-races-client").PatchAll(typeof(RaceRegistry).Assembly);
        }
        catch (Exception ex)
        {
            return Fail(2, "AuthenticRaces client Harmony targets did not resolve: " + ex);
        }

        var playerTexture = new Texture2D();
        var hairTexture = new Texture2D();
        var hairAltTexture = new Texture2D();
        var unrelatedTexture = new Texture2D();

        const int skinVariant = 0;
        const int playerTextureSlot = 3;
        const int hair = 7;

        TextureAssets.Players[skinVariant, playerTextureSlot] = new Asset<Texture2D>(playerTexture);
        TextureAssets.PlayerHair[hair] = new Asset<Texture2D>(hairTexture);
        TextureAssets.PlayerHairAlt[hair] = new Asset<Texture2D>(hairAltTexture);

        var player = new Player { hair = hair, Male = true };
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
            return Fail(3, "Vanilla player texture record was not classified by skin slot.");
        }

        if (!VanillaPlayerDrawClassifier.TryClassify(
                ref drawInfo,
                drawInfo.DrawDataCache[1],
                out var hairSource) ||
            hairSource.Kind != VanillaPlayerDrawKind.Hair ||
            hairSource.Slot != hair)
        {
            return Fail(4, "Vanilla primary hair record was not classified.");
        }

        if (!VanillaPlayerDrawClassifier.TryClassify(
                ref drawInfo,
                drawInfo.DrawDataCache[2],
                out var hairAltSource) ||
            hairAltSource.Kind != VanillaPlayerDrawKind.HairAlt ||
            hairAltSource.Slot != hair)
        {
            return Fail(5, "Vanilla alternate hair record was not classified.");
        }

        if (VanillaPlayerDrawClassifier.TryClassify(
                ref drawInfo,
                drawInfo.DrawDataCache[3],
                out _))
        {
            return Fail(6, "Unrelated draw data must remain outside the race-owned classifier.");
        }

        // The probe race exists only in this executable. Production still registers Human only,
        // so old Skeleton sidecars continue to fall back safely until Skeleton gameplay is ported.
        var skeletonProbe = RaceRegistry.Register(new SkeletonProbeRace());
        RacePlayerState.SetRace(player, skeletonProbe);

        var realSheetRenderer = new PlayerTextureSheetRenderer(
            raceAssetName: "Skeleton",
            playerTextureSlot: playerTextureSlot,
            sheetPath: "ColorSkin/Body");
        var recordingRenderer = new RecordingRenderer(realSheetRenderer);
        RaceRendererRegistry.Register(skeletonProbe.UpstreamFullName, recordingRenderer);

        PlayerDrawLayers.DrawPlayer_RenderAllLayers(ref drawInfo);

        if (recordingRenderer.InvocationCount != 1)
            return Fail(7, "Pre-render race pipeline did not run exactly once.");
        if (recordingRenderer.RaceIdentity != "MrPlagueRaces/Skeleton")
            return Fail(8, "Selected upstream-compatible Skeleton identity did not reach the renderer.");
        if (!recordingRenderer.Appearance.Equals(RaceAppearanceData.Default))
            return Fail(9, "Per-player appearance state did not reach the renderer.");

        var skeletonBody = drawInfo.DrawDataCache[0].texture;
        if (ReferenceEquals(skeletonBody, playerTexture))
            return Fail(10, "Real Skeleton body sheet did not replace vanilla player texture slot 3.");
        if (!skeletonBody.WasLoadedFromPngStream || skeletonBody.SourceByteLength <= 8)
            return Fail(11, "Skeleton body replacement was not created from the staged upstream PNG bytes.");
        if (!ReferenceEquals(drawInfo.DrawDataCache[3].texture, unrelatedTexture))
            return Fail(12, "Real sheet substitution disturbed an unrelated draw record.");

        // Reset the record and render again. The loader should return the exact same Texture2D,
        // proving repeated frames do not reopen/recreate the PNG.
        var reset = drawInfo.DrawDataCache[0];
        reset.texture = playerTexture;
        drawInfo.DrawDataCache[0] = reset;
        PlayerDrawLayers.DrawPlayer_RenderAllLayers(ref drawInfo);

        if (recordingRenderer.InvocationCount != 2)
            return Fail(13, "Second render did not pass through the race renderer.");
        if (!ReferenceEquals(drawInfo.DrawDataCache[0].texture, skeletonBody))
            return Fail(14, "Race texture cache did not reuse the previously loaded Skeleton Texture2D.");

        // The fixture intentionally stages only the male file. Female lookup must therefore
        // follow MrPlague's original fallback rule and reuse the male sheet.
        player.Male = false;
        reset = drawInfo.DrawDataCache[0];
        reset.texture = playerTexture;
        drawInfo.DrawDataCache[0] = reset;
        PlayerDrawLayers.DrawPlayer_RenderAllLayers(ref drawInfo);

        if (recordingRenderer.InvocationCount != 3)
            return Fail(15, "Female fallback render did not pass through the race renderer.");
        if (!ReferenceEquals(drawInfo.DrawDataCache[0].texture, skeletonBody))
            return Fail(16, "Missing female race sheet did not fall back to the male upstream sheet.");

        // Production initialization still means Human = vanilla pixels.
        RacePlayerState.RestoreDefaultRace(player);
        RaceRendererRegistry.Initialize();
        player.Male = true;
        reset = drawInfo.DrawDataCache[0];
        reset.texture = playerTexture;
        drawInfo.DrawDataCache[0] = reset;
        PlayerDrawLayers.DrawPlayer_RenderAllLayers(ref drawInfo);

        if (recordingRenderer.InvocationCount != 3)
            return Fail(17, "Human pass-through reset did not detach the Skeleton probe renderer.");
        if (!ReferenceEquals(drawInfo.DrawDataCache[0].texture, playerTexture))
            return Fail(18, "Human pass-through renderer must leave vanilla draw data untouched.");

        Console.WriteLine(
            "PASS: AuthenticRaces loaded the real upstream Skeleton Body.png, rewrote vanilla slot 3 in place, reused the cached texture, preserved female fallback, and kept Human pass-through deterministic.");
        return 0;
    }

    private static int Fail(int code, string message)
    {
        Console.Error.WriteLine(message);
        return code;
    }

    private sealed class SkeletonProbeRace : Race
    {
        public override string Name => "Skeleton";
    }

    private sealed class RecordingRenderer : RaceRenderer
    {
        private readonly RaceRenderer _inner;

        public RecordingRenderer(RaceRenderer inner)
        {
            _inner = inner;
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
            _inner.Rewrite(ref drawInfo, race, appearance);
        }
    }
}
