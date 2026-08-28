using System.Numerics;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Gelatin.App;
using Gelatin.App.Controls;
using Gelatin.Core.Physics;

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

        Assert.Contains("Gelatin 0.1.4", window.Title);
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

    [AvaloniaFact]
    public async Task LabHotkeysWorkAcrossWorkspaceControlsAndInputsSuppressThem()
    {
        var window = new MainWindow();
        window.Show();
        Click(window.GetLogicalDescendants().OfType<Button>(), "Lab");
        var lab = Assert.Single(window.GetLogicalDescendants().OfType<LabControl>());
        var simulation = await WaitForSimulationAsync(lab);

        var gravity = Assert.Single(window.GetLogicalDescendants().OfType<CheckBox>(), check => Equals(check.Content, "Gravity"));
        gravity.Focus();
        var hammerBefore = lab.HammerMode;
        RaiseKey(gravity, Key.H);
        Assert.NotEqual(hammerBefore, lab.HammerMode);

        var resetButton = Assert.Single(window.GetLogicalDescendants().OfType<Button>(), button => Equals(button.Content, "Reset (R)"));
        resetButton.Focus();
        var meshBefore = lab.ShowMesh;
        RaiseKey(resetButton, Key.M);
        Assert.NotEqual(meshBefore, lab.ShowMesh);

        var quality = Assert.Single(window.GetLogicalDescendants().OfType<ComboBox>());
        quality.Focus();
        Assert.False(lab.Paused);
        RaiseKey(quality, Key.Space);
        Assert.True(lab.Paused);
        RaiseKey(quality, Key.Space);
        Assert.False(lab.Paused);

        simulation.Solver.Smack(Vector2.UnitX, 2);
        simulation.Solver.Step(1f / simulation.Quality.PhysicsHz);
        Assert.True(simulation.Solver.KineticEnergy() > 0);
        RaiseKey(quality, Key.R);
        Assert.All(simulation.Solver.Mesh.Vertices, vertex =>
        {
            Assert.True(Vector2.Distance(vertex.Position, vertex.Rest) < 1e-6f);
            Assert.True(vertex.Velocity.Length() < 1e-6f);
        });

        var labPanel = window.GetLogicalDescendants().OfType<StackPanel>()
            .First(panel => panel.Children.OfType<TextBlock>().Any(text => text.Text == "LAB CONTROLS"));
        var textBox = new TextBox { Text = "editing" };
        labPanel.Children.Add(textBox);
        textBox.Focus();
        var hammerWhileTyping = lab.HammerMode;
        RaiseKey(textBox, Key.H);
        Assert.Equal(hammerWhileTyping, lab.HammerMode);

        var number = new NumericUpDown { Value = 1 };
        labPanel.Children.Add(number);
        number.Focus();
        var meshWhileTyping = lab.ShowMesh;
        RaiseKey(number, Key.M);
        Assert.Equal(meshWhileTyping, lab.ShowMesh);

        window.Close();
    }

    [Fact]
    public void AspectLinkRecapturesCurrentRatioAndRoundsStably()
    {
        var link = new ResizeAspectLink(1000, 1000);
        link.Capture(1000, 500);
        Assert.Equal(250, link.HeightForWidth(500));
        Assert.Equal(1000, link.WidthForHeight(500));

        link.Capture(500, 1000);
        Assert.Equal(500, link.HeightForWidth(250));
        Assert.Equal(250, link.WidthForHeight(500));

        link.Capture(1001, 667);
        var width = 731;
        for (var i = 0; i < 20; i++)
        {
            var height = link.HeightForWidth(width);
            width = link.WidthForHeight(height);
        }
        Assert.InRange(width, 730, 732);
    }

    private static async Task<FixedStepSimulation> WaitForSimulationAsync(LabControl lab)
    {
        var field = typeof(LabControl).GetField("_simulation", BindingFlags.Instance | BindingFlags.NonPublic)!;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (field.GetValue(lab) is FixedStepSimulation simulation) return simulation;
            await Task.Delay(10);
        }
        throw new Xunit.Sdk.XunitException("Lab simulation did not finish rebuilding in the headless test window.");
    }

    private static void RaiseKey(Control source, Key key)
    {
        source.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = KeyModifiers.None
        });
    }

    private static void Click(IEnumerable<Button> buttons, string label)
    {
        var button = Assert.Single(buttons, candidate => Equals(candidate.Content, label));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
}
