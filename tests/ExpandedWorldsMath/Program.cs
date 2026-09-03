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
            CheckTierContinuations();
            CheckDungeonContinuations();
            CheckCapacityBounds();
            ParseRuntimeSources();

            Console.WriteLine($"Expanded Worlds continuity audit passed ({_assertions} assertions).\n" +
                              "Canonical tiers: XL 10600x3000, Huge 12600x3600, THICC 14800x4200.");
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

        Equal(10600, ExpandedWorldMath.XLWidth, "XL width");
        Equal(3000, ExpandedWorldMath.XLHeight, "XL height");
        Equal(12600, ExpandedWorldMath.HugeWidth, "Huge width");
        Equal(3600, ExpandedWorldMath.HugeHeight, "Huge height");
        Equal(14800, ExpandedWorldMath.ThiccWidth, "THICC width");
        Equal(4200, ExpandedWorldMath.ThiccHeight, "THICC height");

        int[] widths = { 4200, 6400, 8400, 10600, 12600, 14800 };
        int[] heights = { 1200, 1800, 2400, 3000, 3600, 4200 };
        int[] horizontalSections = { 21, 32, 42, 53, 63, 74 };
        int[] verticalSections = { 8, 12, 16, 20, 24, 28 };

        for (int i = 0; i < widths.Length; i++)
        {
            Equal(horizontalSections[i], ExpandedWorldMath.HorizontalSections(widths[i]), $"tier {i + 1} horizontal sections");
            Equal(verticalSections[i], ExpandedWorldMath.VerticalSections(heights[i]), $"tier {i + 1} vertical sections");
        }

        for (int i = 1; i < heights.Length; i++)
            Equal(600, heights[i] - heights[i - 1], $"tier {i + 1} height delta");

        int[] expectedWidthDeltas = { 2200, 2000, 2200, 2000, 2200 };
        for (int i = 1; i < widths.Length; i++)
            Equal(expectedWidthDeltas[i - 1], widths[i] - widths[i - 1], $"tier {i + 1} width delta");

        True(ExpandedWorldMath.IsExpandedPresetDimensions(10600, 3000), "recognize XL");
        True(ExpandedWorldMath.IsExpandedPresetDimensions(12600, 3600), "recognize Huge");
        True(ExpandedWorldMath.IsExpandedPresetDimensions(14800, 4200), "recognize THICC");
        False(ExpandedWorldMath.IsExpandedPresetDimensions(12600, 2400), "reject legacy XL");
        False(ExpandedWorldMath.IsExpandedPresetDimensions(16800, 2400), "reject legacy Huge");
        False(ExpandedWorldMath.IsExpandedPresetDimensions(16800, 4800), "reject legacy THICC");

        Equal(4, ExpandedWorldMath.TierFor(ExpandedWorldPreset.XL), "XL tier");
        Equal(5, ExpandedWorldMath.TierFor(ExpandedWorldPreset.Huge), "Huge tier");
        Equal(6, ExpandedWorldMath.TierFor(ExpandedWorldPreset.Thicc), "THICC tier");
    }

    private static void CheckTierContinuations()
    {
        for (int tier = 1; tier <= 6; tier++)
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

        Equal(60, ExpandedWorldTierMath.DirtiestBlockCount(4, celebrationMk10: true), "Celebration keeps downstream x5");
    }

    private static void CheckDungeonContinuations()
    {
        for (int tier = 1; tier <= 6; tier++)
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
        Equal(280, ExpandedWorldCapacityMath.FloatingIslandScratchCapacity(10600, 4), "XL floating-island scratch bound");
        Equal(350, ExpandedWorldCapacityMath.FloatingIslandScratchCapacity(12600, 5), "Huge floating-island scratch bound");
        Equal(390, ExpandedWorldCapacityMath.FloatingIslandScratchCapacity(14800, 6), "THICC floating-island scratch bound");

        Equal(80, ExpandedWorldCapacityMath.CrimsonHeartScratchCapacity(10600), "XL Crimson-heart scratch bound");
        Equal(96, ExpandedWorldCapacityMath.CrimsonHeartScratchCapacity(12600), "Huge Crimson-heart scratch bound");
        Equal(112, ExpandedWorldCapacityMath.CrimsonHeartScratchCapacity(14800), "THICC Crimson-heart scratch bound");
    }

    private static void ParseRuntimeSources()
    {
        string repoRoot = Directory.GetCurrentDirectory();
        string modDirectory = Path.Combine(repoRoot, "gmods", "ExpandedWorlds");
        if (!Directory.Exists(modDirectory))
            throw new DirectoryNotFoundException("Run this audit from the gloader repository root: " + modDirectory);

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
