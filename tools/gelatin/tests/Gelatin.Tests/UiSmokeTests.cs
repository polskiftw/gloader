using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Gelatin.App;

[assembly: AvaloniaTestApplication(typeof(Gelatin.Tests.TestAppBuilder))]

namespace Gelatin.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<global::Gelatin.App.App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

public sealed class UiSmokeTests
{
    [AvaloniaFact]
    public void MainWindowConstructsAndSwitchesAllWorkspaces()
    {
        var window = new MainWindow();
        window.Show();

        Assert.Contains("Gelatin 0.1.0", window.Title);
        var buttons = window.GetLogicalDescendants().OfType<Button>().ToArray();
        Assert.Contains(buttons, button => Equals(button.Content, "Open"));
        Assert.Contains(buttons, button => Equals(button.Content, "Save .gel"));
        Assert.Contains(buttons, button => Equals(button.Content, "About"));

        Click(buttons, "Gel");
        Assert.Contains(window.GetLogicalDescendants().OfType<TextBlock>(), text => text.Text == "AUTHORING TOOLS");

        buttons = window.GetLogicalDescendants().OfType<Button>().ToArray();
        Click(buttons, "Lab");
        Assert.Contains(window.GetLogicalDescendants().OfType<TextBlock>(), text => text.Text == "LAB CONTROLS");

        window.Close();
    }

    private static void Click(IEnumerable<Button> buttons, string label)
    {
        var button = Assert.Single(buttons, candidate => Equals(candidate.Content, label));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
}
