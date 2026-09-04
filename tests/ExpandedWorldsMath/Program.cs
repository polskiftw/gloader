using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

internal static class Program
{
    private static int _assertions;

    private static int Main()
    {
        try
        {
            CheckDimensions();
            CheckServerSelectors();
            CheckMapRendererMath();
            CheckTierContinuations();
            CheckDungeonContinuations();
            CheckCapacityBounds();
            RejectRetiredRuntimeIdentifiers();
            ParseRuntimeSources();

            Console.WriteLine(
                $"Expanded Worlds continuity audit passed ({_assertions} assertions).\n" +
                "Canonical expanded ladder: THICC 10600x3000 through THICC 11 31600x9000.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void CheckDimensions()
    {
        Equal(4200, ExpandedWorldMath.SmallWidth, "Small width");
        Equal(1200, ExpandedWorldMath.SmallHeight, "Small height");
        Equal(6400, ExpandedWorldMath.MediumWidth, "Medium width");
        Equal(1800, ExpandedWorldMath.MediumHeight, "Medium height");
        Equal(8400, ExpandedWorldMath.LargeWidth, "Large width");
        Equal(2400, ExpandedWorldMath.LargeHeight, "Large height");

        string[] labels =
        {
            "THICC", "THICC 2", "THICC 3", "THICC 4", "THICC 5", "THICC 6",
            "THICC 7", "THICC 8", "THICC 9", "THICC 10", "THICC 11"
        };
        int[] widths = { 10600, 12600, 14800, 16800, 19000, 21000, 23200, 25200, 27400, 29400, 31600 };
        int[] heights = { 3000, 3600, 4200, 4800, 5400, 6000, 6600, 7200, 7800, 8400, 9000 };
        int[] horizontalSections = { 53, 63, 74, 84, 95, 105, 116, 126, 137, 147, 158 };
        int[] verticalSections = { 20, 24, 28, 32, 36, 40, 44, 48, 52, 56, 60 };

        Equal(11, ExpandedWorldMath.ExpandedPresetCount, "expanded preset count");
        Equal(14, ExpandedWorldMath.MaximumSupportedOverallTier, "maximum overall tier");
        Equal(31600, ExpandedWorldMath.MaximumSupportedWidth, "maximum supported width");
        Equal(9000, ExpandedWorldMath.MaximumSupportedHeight, "maximum supported height");

        for (int i = 0; i < labels.Length; i++)
        {
            ExpandedWorldDefinition definition = ExpandedWorldMath.DefinitionAt(i);
            int tier = i + 4;

            Equal(labels[i], definition.Label, $"expanded label {i + 1}");
            Equal(widths[i], definition.Width, $"expanded width {i + 1}");
            Equal(heights[i], definition.Height, $"expanded height {i + 1}");
            Equal(tier, definition.OverallTier, $"expanded overall tier {i + 1}");
            Equal((ExpandedWorldPreset)(i + 1), definition.Preset, $"expanded enum {i + 1}");
            Equal(horizontalSections[i], ExpandedWorldMath.HorizontalSections(definition.Width), $"expanded horizontal sections {i + 1}");
            Equal(verticalSections[i], ExpandedWorldMath.VerticalSections(definition.Height), $"expanded vertical sections {i + 1}");
            Equal(definition.Width, ExpandedWorldMath.CanonicalWidthForTier(tier), $"canonical width tier {tier}");
            Equal(definition.Height, ExpandedWorldMath.CanonicalHeightForTier(tier), $"canonical height tier {tier}");
            Equal(definition.Width, ExpandedWorldMath.WidthFor(definition.Preset), $"width lookup {definition.Label}");
            Equal(definition.Height, ExpandedWorldMath.HeightFor(definition.Preset), $"height lookup {definition.Label}");
            Equal(tier, ExpandedWorldMath.TierFor(definition.Preset), $"tier lookup {definition.Label}");
            Equal(definition.Label, ExpandedWorldMath.LabelFor(definition.Preset), $"label lookup {definition.Label}");

            True(
                ExpandedWorldMath.TryGetPresetByDimensions(definition.Width, definition.Height, out ExpandedWorldPreset byDimensions),
                $"dimension lookup {definition.Label}");
            Equal(definition.Preset, byDimensions, $"dimension preset {definition.Label}");
            True(ExpandedWorldMath.IsExpandedPresetDimensions(definition.Width, definition.Height), $"recognize {definition.Label}");
        }

        // Backward compatibility is purely dimensional: these three historical
        // canvases now present under their new public THICC names.
        True(ExpandedWorldMath.TryGetPresetByDimensions(10600, 3000, out ExpandedWorldPreset oldXl), "former XL dimensions");
        Equal(ExpandedWorldPreset.Thicc, oldXl, "former XL relabels THICC");
        True(ExpandedWorldMath.TryGetPresetByDimensions(12600, 3600, out ExpandedWorldPreset oldHuge), "former Huge dimensions");
        Equal(ExpandedWorldPreset.Thicc2, oldHuge, "former Huge relabels THICC 2");
        True(ExpandedWorldMath.TryGetPresetByDimensions(14800, 4200, out ExpandedWorldPreset oldThicc), "former THICC dimensions");
        Equal(ExpandedWorldPreset.Thicc3, oldThicc, "former THICC relabels THICC 3");

        False(ExpandedWorldMath.IsExpandedPresetDimensions(12600, 2400), "reject retired legacy XL dimensions");
        False(ExpandedWorldMath.IsExpandedPresetDimensions(16800, 2400), "reject retired legacy Huge dimensions");
        False(ExpandedWorldMath.IsExpandedPresetDimensions(16800, 4801), "reject near-miss THICC 4 dimensions");

        Equal(33600, ExpandedWorldMath.CanonicalWidthForTier(15), "next canonical width");
        Equal(9600, ExpandedWorldMath.CanonicalHeightForTier(15), "next canonical height");
        True(
            ExpandedWorldMath.CanonicalWidthForTier(15) > ExpandedWorldMath.SignedCoordinatePositiveMaximum,
            "tier 15 crosses signed Int16 positive coordinate boundary");
        True(
            ExpandedWorldMath.MaximumSupportedWidth <= ExpandedWorldMath.SignedCoordinatePositiveMaximum,
            "THICC 11 remains inside signed Int16 positive coordinate boundary");

        Equal(284400000L, ExpandedWorldMath.TileArea(31600, 9000), "THICC 11 tile area uses Int64");
    }

    private static void CheckServerSelectors()
    {
        for (int i = 0; i < ExpandedWorldMath.ExpandedPresetCount; i++)
        {
            ExpandedWorldDefinition definition = ExpandedWorldMath.DefinitionAt(i);
            string compact = i == 0 ? "THICC" : "THICC" + (i + 1);

            True(ExpandedWorldMath.TryParsePreset(compact, out ExpandedWorldPreset compactPreset), "parse " + compact);
            Equal(definition.Preset, compactPreset, "compact selector " + compact);

            True(ExpandedWorldMath.TryParsePreset(definition.Label, out ExpandedWorldPreset displayPreset), "parse display " + definition.Label);
            Equal(definition.Preset, displayPreset, "display selector " + definition.Label);
        }

        False(ExpandedWorldMath.TryParsePreset("XL", out _), "reject retired XL selector");
        False(ExpandedWorldMath.TryParsePreset("HUGE", out _), "reject retired Huge selector");
        False(ExpandedWorldMath.TryParsePreset("THICC1", out _), "reject redundant THICC1 selector");
        False(ExpandedWorldMath.TryParsePreset("THICC12", out _), "reject unsupported THICC12 selector");
        False(ExpandedWorldMath.TryParsePreset("", out _), "reject empty selector");
    }

    private static void CheckMapRendererMath()
    {
        for (int i = 0; i < ExpandedWorldMath.ExpandedPresetCount; i++)
        {
            ExpandedWorldDefinition definition = ExpandedWorldMath.DefinitionAt(i);
            int expectedColumns = (definition.Width + ExpandedWorldMapMath.TextureMaxWidth - 1) / ExpandedWorldMapMath.TextureMaxWidth;
            int expectedRows = (definition.Height + ExpandedWorldMapMath.TextureMaxHeight - 1) / ExpandedWorldMapMath.TextureMaxHeight;

            Equal(expectedColumns, ExpandedWorldMapMath.LogicalTargetColumns(definition.Width), $"logical map columns {definition.Label}");
            Equal(expectedRows, ExpandedWorldMapMath.LogicalTargetRows(definition.Height), $"logical map rows {definition.Label}");
            Equal(expectedColumns - 1, ExpandedWorldMapMath.LastRenderableTargetColumn(definition.Width), $"last map column {definition.Label}");
            True(ExpandedWorldMapMath.BackingTargetColumns(definition.Width) >= expectedColumns, $"backing columns cover {definition.Label}");
            True(ExpandedWorldMapMath.BackingTargetRows(definition.Height) >= expectedRows, $"backing rows cover {definition.Label}");
        }

        Equal(16, ExpandedWorldMapMath.LogicalTargetColumns(31600), "THICC 11 logical map columns");
        Equal(5, ExpandedWorldMapMath.LogicalTargetRows(9000), "THICC 11 logical map rows");
        Equal(17, ExpandedWorldMapMath.BackingTargetColumns(31600), "THICC 11 backing map columns");
        Equal(6, ExpandedWorldMapMath.BackingTargetRows(9000), "THICC 11 backing map rows");
        Equal(15, ExpandedWorldMapMath.LastRenderableTargetColumn(31600), "THICC 11 final renderable map column");
        Equal(1600, ExpandedWorldMapMath.PhysicalFinalColumnWidth(31600), "THICC 11 final physical map column width");
        Equal(1800, ExpandedWorldMapMath.PhysicalFinalRowHeight(9000), "THICC 11 final physical map row height");
        True(ExpandedWorldMapMath.NeedsGuardColumn(31600), "THICC 11 needs map guard column");
        True(ExpandedWorldMapMath.NeedsGuardRow(9000), "THICC 11 needs map guard row");

        // Former THICC's 4,200 height ends in the retail-special 600-pixel tail,
        // so the generalized math correctly needs no extra row there.
        False(ExpandedWorldMapMath.NeedsGuardRow(4200), "4,200-height map uses retail 600 tail directly");
        Equal(3, ExpandedWorldMapMath.BackingTargetRows(4200), "4,200-height backing map rows");
    }

    private static void CheckTierContinuations()
    {
        for (int tier = 1; tier <= ExpandedWorldMath.MaximumSupportedOverallTier; tier++)
        {
            Equal(tier + 1, ExpandedWorldTierMath.StatueMultiplier(tier), $"statue tier {tier}");
            Equal(tier, ExpandedWorldTierMath.SkyLakeBaseCount(tier), $"sky lake tier {tier}");
            Equal(2 * tier, ExpandedWorldDiscreteCountMath.GlowTulipCount(tier), $"Glow Tulip tier {tier}");
            Equal(2 * tier, ExpandedWorldDiscreteCountMath.BoulderPetBaseQuota(tier), $"Boulder Pet tier {tier}");
            Equal(3 * tier + 3, ExpandedWorldDiscreteCountMath.ChilletEggCount(tier), $"Chillet Egg tier {tier}");
            Equal(3 * tier, ExpandedWorldTierMath.DirtiestBlockBaseCount(tier), $"Dirtiest Block tier {tier}");

            IntRange spike = ExpandedWorldDiscreteCountMath.SpikeCaveCountRange(tier);
            Equal(2 * tier + 1, spike.Minimum, $"Spike Cave minimum tier {tier}");
            Equal(2 * tier + 2, spike.Maximum, $"Spike Cave maximum tier {tier}");
        }

        Equal(210, ExpandedWorldTierMath.DirtiestBlockCount(14, celebrationMk10: true), "THICC 11 Celebration keeps downstream x5");
    }

    private static void CheckDungeonContinuations()
    {
        for (int tier = 1; tier <= ExpandedWorldMath.MaximumSupportedOverallTier; tier++)
        {
            Equal(5 * tier, ExpandedWorldDungeonTierMath.BookshelfMinimum(tier), $"Dungeon bookshelf tier {tier}");
            Equal(5 * tier, ExpandedWorldDungeonTierMath.WaterCandleMinimum(tier), $"Dungeon water candle tier {tier}");
            Equal(10 * tier + 10, ExpandedWorldDungeonTierMath.EarlyAltarCount(tier), $"Dungeon altar tier {tier}");
            Equal(4 * tier + 4, ExpandedWorldDungeonTierMath.EarlyDesertDropTrapCount(tier), $"Dungeon desert drop trap tier {tier}");
            Equal(4 * tier + 2, ExpandedWorldDungeonTierMath.EarlySnowDropTrapCount(tier), $"Dungeon snow drop trap tier {tier}");
            Equal(2 * tier + 2, ExpandedWorldDungeonTierMath.EarlyCavernDropTrapCount(tier), $"Dungeon cavern drop trap tier {tier}");
            Equal(4 * tier, ExpandedWorldDungeonTierMath.EarlyPitTrapCount(tier), $"Dungeon pit trap tier {tier}");
            Equal(20 * tier + 20, ExpandedWorldDungeonTierMath.EarlyBiomeClumpCount(tier), $"Dungeon biome clump tier {tier}");
            Equal(2 * tier, ExpandedWorldDungeonTierMath.EarlyFloodedPitQuota(tier), $"Dungeon flooded pit tier {tier}");
            Equal(2 * tier, ExpandedWorldDungeonTierMath.SpecializedShimmerRoomCount(tier), $"Dungeon Shimmer room tier {tier}");
            Equal(4 * tier - 2, ExpandedWorldDungeonTierMath.SpecializedLivingTreeRoomCount(tier), $"Dungeon Living Tree room tier {tier}");
            Equal(4 * tier - 2, ExpandedWorldDungeonTierMath.SpecializedMahoganyRoomCount(tier), $"Dungeon Mahogany room tier {tier}");
            Equal(3 * tier + 2, ExpandedWorldDungeonTierMath.SpecializedBeehiveRoomCount(tier), $"Dungeon Beehive room tier {tier}");
            Equal(4 * tier + 2, ExpandedWorldDungeonTierMath.SpecializedCrystalRoomCount(tier), $"Dungeon Crystal room tier {tier}");
            Equal(tier + 2, ExpandedWorldDungeonTierMath.SpecializedHallCount(tier), $"Dungeon specialized hall tier {tier}");
            Equal(20 * tier + 10, ExpandedWorldDungeonTierMath.TempleTrapBase(tier), $"Dungeon temple trap base tier {tier}");
            Equal(5 * tier + 6, ExpandedWorldDungeonTierMath.TempleTrapRandomExclusive(tier), $"Dungeon temple trap random tier {tier}");
        }
    }

    private static void CheckCapacityBounds()
    {
        const int width = 31600;
        const int tier = 14;

        Equal(890, ExpandedWorldCapacityMath.FloatingIslandScratchCapacity(width, tier), "THICC 11 floating-island scratch bound");
        Equal(232, ExpandedWorldCapacityMath.CrimsonHeartScratchCapacity(width), "THICC 11 Crimson-heart scratch bound");
        Equal(46, ExpandedWorldCapacityMath.MountainCaveScratchCapacity(width), "THICC 11 Mountain Cave scratch bound");
        Equal(70, ExpandedWorldCapacityMath.SurfaceTunnelRecordUpperBound(width, remixWorld: true), "THICC 11 surface tunnel record bound");
        Equal(71, ExpandedWorldCapacityMath.SurfaceTunnelSentinelCapacity(width), "THICC 11 surface tunnel sentinel");
        Equal(74, ExpandedWorldCapacityMath.SurfaceOrePatchRecordUpperBound(width), "THICC 11 surface ore record bound");
        Equal(75, ExpandedWorldCapacityMath.SurfaceOrePatchSentinelCapacity(width), "THICC 11 surface ore sentinel");

        // Audited fixed stores that remain below retail capacity at the hard stop.
        Equal(44, ExpandedWorldCapacityMath.LakeRecordUpperBound(width), "THICC 11 lake record bound");
        True(ExpandedWorldCapacityMath.LakeRecordUpperBound(width) < 50, "lake capacity remains below 50");
        Equal(46, ExpandedWorldCapacityMath.MushroomBiomeRecordUpperBound(width), "THICC 11 mushroom biome bound");
        True(ExpandedWorldCapacityMath.MushroomBiomeRecordUpperBound(width) < 50, "mushroom capacity remains below 50");
        Equal(16, ExpandedWorldCapacityMath.OasisRecordUpperBound(width), "THICC 11 oasis bound");
        True(ExpandedWorldCapacityMath.OasisRecordUpperBound(width) < 20, "oasis capacity remains below 20");
        Equal(83, ExpandedWorldCapacityMath.JungleShrineRecordUpperBound(width), "THICC 11 jungle shrine bound");
        True(ExpandedWorldCapacityMath.JungleShrineRecordUpperBound(width) < 100, "jungle shrine capacity remains below 100");
        Equal(82, ExpandedWorldCapacityMath.BeeLarvaRecordUpperBound(width), "THICC 11 bee larva bound");
        True(ExpandedWorldCapacityMath.BeeLarvaRecordUpperBound(width) < 100, "bee larva capacity remains below 100");
    }

    private static void RejectRetiredRuntimeIdentifiers()
    {
        string repoRoot = Directory.GetCurrentDirectory();
        string modDirectory = Path.Combine(repoRoot, "gmods", "ExpandedWorlds");
        if (!Directory.Exists(modDirectory))
            throw new DirectoryNotFoundException("Run this audit from the gloader repository root: " + modDirectory);

        string[] retired =
        {
            "ExpandedWorldPreset.XL",
            "ExpandedWorldPreset.Huge",
            "XLWidth",
            "XLHeight",
            "HugeWidth",
            "HugeHeight"
        };

        foreach (string file in Directory.GetFiles(modDirectory, "*.cs", SearchOption.TopDirectoryOnly))
        {
            string text = File.ReadAllText(file);
            foreach (string token in retired)
            {
                False(text.Contains(token, StringComparison.Ordinal), $"retired runtime identifier {token} in {Path.GetFileName(file)}");
            }
        }
    }

    private static void ParseRuntimeSources()
    {
        string repoRoot = Directory.GetCurrentDirectory();
        string modDirectory = Path.Combine(repoRoot, "gmods", "ExpandedWorlds");
        string[] files = Directory.GetFiles(modDirectory, "*.cs", SearchOption.TopDirectoryOnly);

        foreach (string mode in new[] { "GLOADER_CLIENT", "GLOADER_SERVER" })
        {
            var options = new CSharpParseOptions(
                LanguageVersion.Latest,
                DocumentationMode.Parse,
                SourceCodeKind.Regular,
                preprocessorSymbols: new[] { "GLOADER", mode });

            foreach (string file in files)
            {
                SyntaxTree tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file), options, file);
                Diagnostic[] errors = tree.GetDiagnostics()
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .ToArray();
                if (errors.Length != 0)
                {
                    throw new InvalidOperationException(
                        $"{mode} syntax parse failed for {Path.GetFileName(file)}:\n" +
                        string.Join(Environment.NewLine, errors.Select(d => d.ToString())));
                }
                _assertions++;
            }
        }
    }

    private static void Equal<T>(T expected, T actual, string name)
    {
        _assertions++;
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{name}: expected {expected}, got {actual}.");
    }

    private static void True(bool value, string name)
    {
        _assertions++;
        if (!value) throw new InvalidOperationException(name + ": expected true.");
    }

    private static void False(bool value, string name)
    {
        _assertions++;
        if (value) throw new InvalidOperationException(name + ": expected false.");
    }
}
