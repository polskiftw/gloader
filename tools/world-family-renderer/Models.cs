using System.Globalization;
using TEdit.Terraria;

namespace WorldFamilyRenderer;

internal sealed record WorldPreset(
    int Number,
    string Name,
    int Width,
    int Height,
    int AutoCreate,
    string ExpandedEnvironment)
{
    public bool IsExpanded => !string.IsNullOrWhiteSpace(ExpandedEnvironment);

    public static IReadOnlyList<WorldPreset> All { get; } = new[]
    {
        new WorldPreset(1, "Small", 4200, 1200, 1, null),
        new WorldPreset(2, "Medium", 6400, 1800, 2, null),
        new WorldPreset(3, "Large", 8400, 2400, 3, null),
        new WorldPreset(4, "XL", 10600, 3000, 3, "XL"),
        new WorldPreset(5, "Huge", 12600, 3600, 3, "HUGE"),
        new WorldPreset(6, "THICC", 14800, 4200, 3, "THICC")
    };
}

internal sealed record QualityLevel(int SliderValue, string Name, int MaxWorldWidth)
{
    public static IReadOnlyList<QualityLevel> All { get; } = new[]
    {
        new QualityLevel(1, "Draft", 720),
        new QualityLevel(2, "Normal", 1080),
        new QualityLevel(3, "Good", 1440),
        new QualityLevel(4, "High", 1920),
        new QualityLevel(5, "Very High", 2880),
        new QualityLevel(6, "Ultra", 3840)
    };

    public static QualityLevel FromSlider(int value) =>
        All.First(level => level.SliderValue == value);
}

internal sealed record SourceWorldInfo(
    string FilePath,
    string Title,
    string Seed,
    int Width,
    int Height,
    int GameMode,
    bool IsCrimson,
    int SpecialSeedMask)
{
    public string DifficultyName => GameMode switch
    {
        0 => "Classic",
        1 => "Expert",
        2 => "Master",
        3 => "Journey",
        _ => "Mode " + GameMode.ToString(CultureInfo.InvariantCulture)
    };

    public string EvilName => IsCrimson ? "Crimson" : "Corruption";

    public string DisplaySummary =>
        $"Seed: {Seed}   |   {DifficultyName}   |   {EvilName}   |   {Width:N0} x {Height:N0}";

    public static SourceWorldInfo Load(string filePath)
    {
        WorldConfiguration.Initialize();
        var (world, error) = World.LoadWorld(filePath, headersOnly: true);
        if (error != null)
            throw new InvalidDataException("TEdit could not read this world header.", error);

        if (world == null || world.TilesWide <= 0 || world.TilesHigh <= 0)
            throw new InvalidDataException("The world header did not contain valid dimensions.");

        if (string.IsNullOrWhiteSpace(world.Seed))
            throw new InvalidDataException("This .wld does not contain a recoverable text seed.");

        int mask = 0;
        if (world.DrunkWorld) mask |= 1;
        if (world.NotTheBeesWorld) mask |= 2;
        if (world.GoodWorld) mask |= 4;
        if (world.TenthAnniversaryWorld) mask |= 8;
        if (world.DontStarveWorld) mask |= 16;
        if (world.RemixWorld) mask |= 32;
        if (world.NoTrapsWorld) mask |= 64;
        if (world.ZenithWorld) mask |= 128;
        if (world.SkyblockWorld) mask |= 256;

        return new SourceWorldInfo(
            Path.GetFullPath(filePath),
            world.Title ?? Path.GetFileNameWithoutExtension(filePath),
            world.Seed.Trim(),
            world.TilesWide,
            world.TilesHigh,
            world.GameMode,
            world.IsCrimson,
            mask);
    }

    public string BuildCopiedSeed(WorldPreset preset)
    {
        int copiedSize = preset.AutoCreate;
        int copiedDifficulty = Math.Clamp(GameMode + 1, 1, 4);
        int copiedEvil = IsCrimson ? 2 : 1;
        return string.Join(
            ".",
            copiedSize.ToString(CultureInfo.InvariantCulture),
            copiedDifficulty.ToString(CultureInfo.InvariantCulture),
            copiedEvil.ToString(CultureInfo.InvariantCulture),
            SpecialSeedMask.ToString(CultureInfo.InvariantCulture),
            Seed);
    }
}

internal sealed record RenderedWorld(WorldPreset Preset, string PngPath, int PixelWidth, int PixelHeight);

internal sealed record RuntimePaths(string TerrariaRoot, string GLoaderExe, string X64RuntimeDll, string ExpandedWorldModDirectory)
{
    public static RuntimePaths Validate(string terrariaRoot)
    {
        if (string.IsNullOrWhiteSpace(terrariaRoot))
            throw new DirectoryNotFoundException("Choose the Terraria folder that contains gloader.exe.");

        string root = Path.GetFullPath(terrariaRoot.Trim());
        string loader = Path.Combine(root, "gloader.exe");
        if (!File.Exists(loader))
            throw new FileNotFoundException("gloader.exe was not found in the selected Terraria folder.", loader);

        string runtimeDir = Path.Combine(root, "gdeps", "x64-runtime");
        string runtime = new[] { "TerrariaRelease.dll", "TerrariaDebug.dll", "Terraria.dll" }
            .Select(name => Path.Combine(runtimeDir, name))
            .FirstOrDefault(File.Exists);
        if (runtime == null)
            throw new FileNotFoundException(
                "The private 64-bit Terraria runtime was not found under gdeps\\x64-runtime. Build it with gloader first.");

        string expanded = Path.Combine(root, "gmods", "ExpandedWorlds");
        if (!File.Exists(Path.Combine(expanded, "Main.cs")) &&
            !File.Exists(Path.Combine(expanded, "ServerRuntime.cs")))
        {
            throw new DirectoryNotFoundException(
                "gmods\\ExpandedWorlds was not found. The XL/Huge/THICC worlds need that bundled gmod.");
        }

        return new RuntimePaths(root, loader, runtime, expanded);
    }
}
