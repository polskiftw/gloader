using Gelatin.Core.Format;
using Gelatin.Core.Imaging;
using Gelatin.Core.Models;

namespace Gelatin.Tests;

public sealed class AnimatedImageTests
{
    private const string TwoFrameGifBase64 = "R0lGODlhAgACAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQIBQAAACwAAAAAAgACAAAIBgABCAQQEAAh+QQIDAAAACwAAAAAAgACAIEA/wAAAAAAAAAAAAAIBgABCAQQEAA7";
    private const string DependentFrameGifBase64 = "R0lGODlhBAAEAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQEBAAAACwAAAAABAAEAAAICQABCBxIsCCAgAAh+QQFBwACACwAAAAAAgACAIH/AAAAAP8AAAAAAAAIBgADCAwQEAAh+QQFCQADACwCAAIAAgACAIH/AAAAAP8A/wAAAAAIBgAFCBQQEAA7";

    [Fact]
    public void GifImportPreservesFrameTimingAndInfiniteRepeat()
    {
        var result = AnimatedImageProcessor.ImportGif(Convert.FromBase64String(TwoFrameGifBase64));

        Assert.True(result.IsAnimated);
        Assert.Equal(2, result.Width);
        Assert.Equal(2, result.Height);
        Assert.NotNull(result.Animation);
        Assert.Equal(-1, result.Animation!.RepetitionCount);
        Assert.Equal([50, 120], result.Animation.Frames.Select(frame => frame.DurationMs).ToArray());
        Assert.All(result.Animation.Frames, frame =>
        {
            Assert.Equal(2, frame.Width);
            Assert.Equal(2, frame.Height);
        });
    }

    [Fact]
    public void GifImportDecodesDistinctCompositedFrames()
    {
        var result = AnimatedImageProcessor.ImportGif(Convert.FromBase64String(TwoFrameGifBase64));
        var config = new GelConfig
        {
            SchemaVersion = 2,
            Image = new ImageConfig { Width = 2, Height = 2 },
            Animation = result.Animation,
            Cores = []
        };
        var first = RawRgbaCodec.Decode(AnimatedImageProcessor.GetFramePng(result.PngBytes, config, 0));
        var second = RawRgbaCodec.Decode(AnimatedImageProcessor.GetFramePng(result.PngBytes, config, 1));

        Assert.Equal(255, first.Pixels[0]);
        Assert.Equal(0, first.Pixels[1]);
        Assert.Equal(0, second.Pixels[0]);
        Assert.Equal(255, second.Pixels[1]);
    }

    [Fact]
    public void GifImportCompositesDependentPartialFrames()
    {
        var result = AnimatedImageProcessor.ImportGif(Convert.FromBase64String(DependentFrameGifBase64));
        var config = new GelConfig
        {
            SchemaVersion = 2,
            Image = new ImageConfig { Width = 4, Height = 4 },
            Animation = result.Animation,
            Cores = []
        };
        var third = RawRgbaCodec.Decode(AnimatedImageProcessor.GetFramePng(result.PngBytes, config, 2));

        // Frame 3 only encodes a 2x2 patch at the bottom-right. Correct GIF compositing
        // must retain the blue 2x2 patch introduced by frame 2 at the top-left.
        Assert.Equal(0, third.Pixels[0]);
        Assert.Equal(0, third.Pixels[1]);
        Assert.Equal(255, third.Pixels[2]);
        var bottomRight = ((3 * 4) + 3) * 4;
        Assert.Equal(0, third.Pixels[bottomRight]);
        Assert.Equal(255, third.Pixels[bottomRight + 1]);
        Assert.Equal(0, third.Pixels[bottomRight + 2]);
        Assert.Equal([40, 70, 90], result.Animation!.Frames.Select(frame => frame.DurationMs).ToArray());
    }

    [Fact]
    public void AnimatedTransformTouchesEveryFrameAndPreservesTiming()
    {
        var result = AnimatedImageProcessor.ImportGif(Convert.FromBase64String(TwoFrameGifBase64));
        var config = new GelConfig
        {
            SchemaVersion = 2,
            Image = new ImageConfig { Width = 2, Height = 2 },
            Animation = result.Animation,
            Cores = []
        };

        var resized = AnimatedImageProcessor.TransformAnimated(result.PngBytes, config, frame => RawRgbaTransforms.Resize(frame, 4, 3));

        Assert.Equal((4, 3), (resized.Width, resized.Height));
        Assert.Equal([50, 120], resized.Animation!.Frames.Select(frame => frame.DurationMs).ToArray());
        Assert.All(resized.Animation.Frames, frame => Assert.Equal((4, 3), (frame.Width, frame.Height)));
    }

    [Fact]
    public void SelectedFrameTransformTouchesOnlyThatFrameAndKeepsTiming()
    {
        var imported = AnimatedImageProcessor.ImportGif(Convert.FromBase64String(TwoFrameGifBase64));
        var config = new GelConfig
        {
            SchemaVersion = 2,
            Image = new ImageConfig { Width = 2, Height = 2 },
            Animation = imported.Animation,
            Cores = []
        };

        var edited = AnimatedImageProcessor.TransformFrame(imported.PngBytes, config, 1, frame =>
            ImageAlphaEditing.ApplyRectCutout(frame, new PixelRect(0, 0, 1, 2)));
        var editedConfig = config.DeepClone();
        editedConfig.Animation = edited.Animation;
        var first = RawRgbaCodec.Decode(AnimatedImageProcessor.GetFramePng(edited.PngBytes, editedConfig, 0));
        var second = RawRgbaCodec.Decode(AnimatedImageProcessor.GetFramePng(edited.PngBytes, editedConfig, 1));

        Assert.Equal(255, first.Pixels[3]);
        Assert.Equal(255, first.Pixels[7]);
        Assert.Equal(255, second.Pixels[3]);
        Assert.Equal(0, second.Pixels[7]);
        Assert.Equal([50, 120], edited.Animation!.Frames.Select(frame => frame.DurationMs).ToArray());
        Assert.All(edited.Animation.Frames, frame => Assert.Equal((2, 2), (frame.Width, frame.Height)));
    }

    [Fact]
    public void SelectedFrameAlphaBrushDoesNotTouchOtherFrames()
    {
        var imported = AnimatedImageProcessor.ImportGif(Convert.FromBase64String(TwoFrameGifBase64));
        var config = new GelConfig
        {
            SchemaVersion = 2,
            Image = new ImageConfig { Width = 2, Height = 2 },
            Animation = imported.Animation,
            Cores = []
        };

        using var brush = new AnimationAlphaBrushSession(imported.PngBytes, imported.PngBytes, config, AlphaBrushMode.Erase, 1, 1);
        brush.ApplyPoint(new PixelPoint(0.5, 0.5));
        var edited = brush.Encode();
        var editedConfig = config.DeepClone();
        editedConfig.Animation = edited.Animation;
        var first = RawRgbaCodec.Decode(AnimatedImageProcessor.GetFramePng(edited.PngBytes, editedConfig, 0));
        var second = RawRgbaCodec.Decode(AnimatedImageProcessor.GetFramePng(edited.PngBytes, editedConfig, 1));

        Assert.Equal(255, first.Pixels[3]);
        Assert.Equal(0, second.Pixels[3]);
    }

    [Fact]
    public void FrameStartTimeUsesPreservedPerFrameDurations()
    {
        var animation = new AnimationConfig
        {
            Frames =
            [
                new AnimationFrameConfig { Width = 1, Height = 1, DurationMs = 50 },
                new AnimationFrameConfig { Width = 1, Height = 1, DurationMs = 120 },
                new AnimationFrameConfig { Width = 1, Height = 1, DurationMs = 30 }
            ]
        };

        Assert.Equal(0, AnimatedImageProcessor.FrameStartTimeMilliseconds(animation, 0));
        Assert.Equal(50, AnimatedImageProcessor.FrameStartTimeMilliseconds(animation, 1));
        Assert.Equal(170, AnimatedImageProcessor.FrameStartTimeMilliseconds(animation, 2));
    }

    [Fact]
    public void AnimatedGelRoundTripsThroughGel1Container()
    {
        var imported = AnimatedImageProcessor.ImportGif(Convert.FromBase64String(TwoFrameGifBase64));
        var document = new GelDocument
        {
            PngBytes = imported.PngBytes,
            Config = new GelConfig
            {
                SchemaVersion = 2,
                AssetName = "Timing Test",
                Image = new ImageConfig { Width = imported.Width, Height = imported.Height },
                Animation = imported.Animation,
                Cores = []
            }
        };

        var roundTrip = GelFile.Read(new MemoryStream(GelFile.WriteBytes(document)));

        Assert.Equal(2, roundTrip.Config.SchemaVersion);
        Assert.Equal(-1, roundTrip.Config.Animation!.RepetitionCount);
        Assert.Equal([50, 120], roundTrip.Config.Animation.Frames.Select(frame => frame.DurationMs).ToArray());
        Assert.Equal((2, 2), (roundTrip.Config.Image.Width, roundTrip.Config.Image.Height));
    }

    [Fact]
    public void FrameSelectionUsesExactPerFrameDurations()
    {
        var animation = new AnimationConfig
        {
            RepetitionCount = -1,
            Frames =
            [
                new AnimationFrameConfig { Width = 2, Height = 2, DurationMs = 50 },
                new AnimationFrameConfig { Width = 2, Height = 2, DurationMs = 120 }
            ]
        };

        Assert.Equal(0, AnimatedImageProcessor.FrameIndexAtTime(animation, 0));
        Assert.Equal(0, AnimatedImageProcessor.FrameIndexAtTime(animation, 49));
        Assert.Equal(1, AnimatedImageProcessor.FrameIndexAtTime(animation, 50));
        Assert.Equal(1, AnimatedImageProcessor.FrameIndexAtTime(animation, 169));
        Assert.Equal(0, AnimatedImageProcessor.FrameIndexAtTime(animation, 170));
    }

    [Fact]
    public void FiniteAnimationStopsOnLastFrameAfterFinalPass()
    {
        var animation = new AnimationConfig
        {
            RepetitionCount = 0,
            Frames =
            [
                new AnimationFrameConfig { Width = 1, Height = 1, DurationMs = 20 },
                new AnimationFrameConfig { Width = 1, Height = 1, DurationMs = 30 }
            ]
        };

        Assert.Equal(0, AnimatedImageProcessor.FrameIndexAtTime(animation, 19));
        Assert.Equal(1, AnimatedImageProcessor.FrameIndexAtTime(animation, 20));
        Assert.Equal(1, AnimatedImageProcessor.FrameIndexAtTime(animation, 50));
        Assert.Equal(1, AnimatedImageProcessor.FrameIndexAtTime(animation, 5_000));
    }

    [Fact]
    public void UnionTrimIncludesVisiblePixelsFromEveryFrame()
    {
        static byte[] Frame(int x, int y)
        {
            var pixels = new byte[4 * 4 * 4];
            pixels[(y * 4 + x) * 4 + 3] = 255;
            return RawRgbaCodec.Encode(4, 4, pixels);
        }

        var packed = AnimatedImageProcessor.PackFrames([Frame(0, 0), Frame(3, 3)], [40, 40], -1);
        var config = new GelConfig
        {
            SchemaVersion = 2,
            Image = new ImageConfig { Width = 4, Height = 4 },
            Animation = packed.Animation,
            Cores = []
        };

        var bounds = AnimatedImageProcessor.FindUnionTrimBounds(packed.PngBytes, config, 0);
        Assert.Equal(new PixelRect(0, 0, 4, 4), bounds);
    }

    [Fact]
    public void GelReaderRejectsFrameOutsideAtlas()
    {
        var packed = AnimatedImageProcessor.ImportGif(Convert.FromBase64String(TwoFrameGifBase64));
        var config = new GelConfig
        {
            SchemaVersion = 2,
            Image = new ImageConfig { Width = 2, Height = 2 },
            Animation = packed.Animation,
            Cores = []
        };
        config.Animation!.Frames[1].X = 32766;
        var document = new GelDocument { Config = config, PngBytes = packed.PngBytes };

        Assert.Throws<GelFormatException>(() => GelFile.WriteBytes(document));
    }
}
