namespace WorldFamilyRenderer;

internal static class WorldFamilyJob
{
    public static async Task<string> RunAsync(
        SourceWorldInfo source,
        RuntimePaths runtime,
        string outputBaseDirectory,
        QualityLevel quality,
        IProgress<string> status,
        IProgress<int> overallProgress,
        CancellationToken cancellationToken,
        Action<GenerationEngine> engineReady)
    {
        string safeName = MakeSafeName(source.Title);
        string outputDirectory = Path.Combine(
            outputBaseDirectory,
            $"WorldFamily_{safeName}_{DateTime.Now:yyyyMMdd_HHmmss}");
        Directory.CreateDirectory(outputDirectory);

        var rendered = new List<RenderedWorld>();
        var engine = new GenerationEngine(runtime);
        engineReady?.Invoke(engine);
        bool success = false;

        try
        {
            int steps = WorldPreset.All.Count * 2 + 1;
            int completed = 0;

            foreach (WorldPreset preset in WorldPreset.All)
            {
                cancellationToken.ThrowIfCancellationRequested();
                overallProgress?.Report(completed * 100 / steps);

                string worldPath = await engine.GenerateAsync(source, preset, status, cancellationToken).ConfigureAwait(false);
                completed++;
                overallProgress?.Report(completed * 100 / steps);

                string pngPath = Path.Combine(
                    outputDirectory,
                    $"{preset.Number:00}_{preset.Name}_{preset.Width}x{preset.Height}.png");

                RenderedWorld image = await TEditPaletteRenderer.RenderAsync(
                    worldPath,
                    preset,
                    quality.MaxWorldWidth,
                    pngPath,
                    status,
                    cancellationToken).ConfigureAwait(false);
                rendered.Add(image);

                try { File.Delete(worldPath); } catch { }
                completed++;
                overallProgress?.Report(completed * 100 / steps);
            }

            cancellationToken.ThrowIfCancellationRequested();
            status?.Report("Building the six-world comparison sheet...");
            string comparisonPath = Path.Combine(outputDirectory, "ExpandedWorlds_SameSeed_AllSizes.png");
            ComparisonComposer.Compose(rendered, comparisonPath, cancellationToken);
            overallProgress?.Report(100);
            status?.Report("Done. Comparison sheet + six individual PNGs created.");
            success = true;
            return outputDirectory;
        }
        finally
        {
            if (success || cancellationToken.IsCancellationRequested)
                engine.Cleanup();
            engineReady?.Invoke(null);
        }
    }

    private static string MakeSafeName(string value)
    {
        string name = string.IsNullOrWhiteSpace(value) ? "World" : value.Trim();
        foreach (char invalid in Path.GetInvalidFileNameChars())
            name = name.Replace(invalid, '_');
        if (name.Length > 48) name = name[..48];
        return name;
    }
}
