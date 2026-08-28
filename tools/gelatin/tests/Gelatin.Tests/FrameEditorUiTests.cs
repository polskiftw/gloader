using Avalonia.Headless.XUnit;
using Gelatin.App;
using Gelatin.App.Controls;

namespace Gelatin.Tests;

public sealed class FrameEditorUiTests
{
    private const string TwoFrameGifBase64 = "R0lGODlhAgACAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQIBQAAACwAAAAAAgACAAAIBgABCAQQEAAh+QQIDAAAACwAAAAAAgACAIEA/wAAAAAAAAAAAAAIBgABCAQQEAA7";

    [AvaloniaFact]
    public async Task ManualFrameNavigationPausesAndWraps()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gelatin-frame-{Guid.NewGuid():N}.gif");
        await File.WriteAllBytesAsync(path, Convert.FromBase64String(TwoFrameGifBase64));
        try
        {
            var controller = new DocumentController();
            await controller.OpenAsync(path);
            var editor = new EditorCanvas(controller);
            try
            {
                Assert.True(editor.AnimationPlaying);
                editor.SetAnimationFrame(1);
                Assert.False(editor.AnimationPlaying);
                Assert.Equal(1, editor.CurrentFrameIndex);

                editor.StepAnimation(1);
                Assert.Equal(0, editor.CurrentFrameIndex);
                editor.StepAnimation(-1);
                Assert.Equal(1, editor.CurrentFrameIndex);

                editor.SetAnimationPlaying(true);
                Assert.True(editor.AnimationPlaying);
            }
            finally { editor.Shutdown(); }
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
