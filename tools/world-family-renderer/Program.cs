using ReactiveUI.Builder;
using TEdit.Terraria;

namespace WorldFamilyRenderer;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            InitializeTEditRuntime();
        }
        catch (Exception ex)
        {
            if (args.Any(arg => arg.Equals("--self-test", StringComparison.OrdinalIgnoreCase)))
            {
                Console.Error.WriteLine(ex);
                return 1;
            }

            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            MessageBox.Show(
                "The embedded TEdit/ReactiveUI runtime could not initialize.\n\n" + FormatException(ex),
                "World Family Renderer",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }

        if (args.Any(arg => arg.Equals("--self-test", StringComparison.OrdinalIgnoreCase)))
            return SelfTest.Run();

        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
        return 0;
    }

    private static void InitializeTEditRuntime()
    {
        // TEdit.Terraria uses ReactiveObject for its World model. ReactiveUI 24+
        // deliberately no longer self-initializes: touching a generated reactive
        // property before BuildApp() causes ReactiveNotifyPropertyChangedMixins'
        // type initializer to throw. We only consume TEdit's model/parser, so the
        // headless core service set is sufficient; no ReactiveUI UI/binding layer
        // is needed by this WinForms shell.
        _ = RxAppBuilder.CreateReactiveUIBuilder()
            .WithCoreServices()
            .BuildApp();

        WorldConfiguration.Initialize();
    }

    internal static string FormatException(Exception exception)
    {
        var parts = new List<string>();
        for (Exception current = exception; current != null; current = current.InnerException)
        {
            if (!string.IsNullOrWhiteSpace(current.Message) &&
                !parts.Contains(current.Message, StringComparer.Ordinal))
            {
                parts.Add(current.Message.Trim());
            }
        }

        return parts.Count == 0 ? exception.GetType().Name : string.Join("\n→ ", parts);
    }
}

internal static class SelfTest
{
    public static int Run()
    {
        try
        {
            if (WorldConfiguration.TileProperties.Count < 754)
                throw new InvalidOperationException("TEdit tile palette is incomplete.");
            if (WorldConfiguration.WallProperties.Count < 367)
                throw new InvalidOperationException("TEdit wall palette is incomplete.");

            foreach (string key in new[] { "Space", "Sky", "Earth", "Rock", "Hell", "Water", "Lava", "Honey", "Shimmer" })
            {
                if (!WorldConfiguration.GlobalColors.ContainsKey(key))
                    throw new InvalidOperationException("Missing TEdit global color: " + key);
            }

            // This is the regression that v0.1.0 missed: World is a ReactiveObject.
            // Setting a reactive property forces ReactiveNotifyPropertyChangedMixins
            // to initialize, which failed on real .wld loads when ReactiveUI had not
            // been explicitly built first.
            var reactiveWorldProbe = new World();
            reactiveWorldProbe.Title = "World Family Renderer self-test";
            if (reactiveWorldProbe.Title != "World Family Renderer self-test")
                throw new InvalidOperationException("TEdit ReactiveObject model probe failed.");

            WorldPreset[] presets = WorldPreset.All.ToArray();
            if (presets.Length != 6 ||
                presets[3].Width != 10600 || presets[3].Height != 3000 ||
                presets[4].Width != 12600 || presets[4].Height != 3600 ||
                presets[5].Width != 14800 || presets[5].Height != 4200)
            {
                throw new InvalidOperationException("World preset table is not synchronized with canonical vanilla-continuity dimensions.");
            }

            Console.WriteLine($"World Family Renderer self-test OK. Tiles={WorldConfiguration.TileProperties.Count}, Walls={WorldConfiguration.WallProperties.Count}, ReactiveUI=OK, ContinuityPresets=OK");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }
}
