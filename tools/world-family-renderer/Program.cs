using TEdit.Terraria;

namespace WorldFamilyRenderer;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Any(arg => arg.Equals("--self-test", StringComparison.OrdinalIgnoreCase)))
            return SelfTest.Run();

        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        try
        {
            WorldConfiguration.Initialize();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "The embedded TEdit world data could not initialize.\n\n" + ex.Message,
                "World Family Renderer",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }

        Application.Run(new MainForm());
        return 0;
    }
}

internal static class SelfTest
{
    public static int Run()
    {
        try
        {
            WorldConfiguration.Initialize();
            if (WorldConfiguration.TileProperties.Count < 754)
                throw new InvalidOperationException("TEdit tile palette is incomplete.");
            if (WorldConfiguration.WallProperties.Count < 367)
                throw new InvalidOperationException("TEdit wall palette is incomplete.");

            foreach (string key in new[] { "Space", "Sky", "Earth", "Rock", "Hell", "Water", "Lava", "Honey", "Shimmer" })
            {
                if (!WorldConfiguration.GlobalColors.ContainsKey(key))
                    throw new InvalidOperationException("Missing TEdit global color: " + key);
            }

            if (WorldPreset.All.Count != 6 || WorldPreset.All[^1].Width != 16800 || WorldPreset.All[^1].Height != 4800)
                throw new InvalidOperationException("World preset table is invalid.");

            Console.WriteLine($"World Family Renderer self-test OK. Tiles={WorldConfiguration.TileProperties.Count}, Walls={WorldConfiguration.WallProperties.Count}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }
}
