using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Gelatin.App;

internal static class Dialogs
{
    public static async Task ShowErrorAsync(Window owner, string title, string message)
        => await ShowAsync(owner, title, message, [("OK", true)]);

    public static async Task ShowInfoAsync(Window owner, string title, string message)
        => await ShowAsync(owner, title, message, [("OK", true)]);

    public static Task<bool> ConfirmAsync(Window owner, string title, string message, string confirm = "Continue", string cancel = "Cancel")
        => ShowAsync(owner, title, message, [(cancel, false), (confirm, true)]);

    private static Task<bool> ShowAsync(Window owner, string title, string message, IReadOnlyList<(string Label, bool Result)> choices)
    {
        var completion = new TaskCompletionSource<bool>();
        var dialog = new Window
        {
            Title = title,
            Width = 460,
            MinHeight = 170,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.Parse("#1C1C22"))
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        foreach (var choice in choices)
        {
            var button = new Button { Content = choice.Label, MinWidth = 88, HorizontalContentAlignment = HorizontalAlignment.Center };
            button.Click += (_, _) => { completion.TrySetResult(choice.Result); dialog.Close(); };
            buttons.Children.Add(button);
        }
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 18,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 14 },
                buttons
            }
        };
        dialog.Closed += (_, _) => completion.TrySetResult(false);
        _ = dialog.ShowDialog(owner);
        return completion.Task;
    }
}
