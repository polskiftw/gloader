using Gelatin.Core.Format;
using Gelatin.Core.Imaging;
using Gelatin.Core.Models;

namespace Gelatin.Tests;

public sealed class AnimatedImageTests
{
    private const string TwoFrameGifBase64 = "R0lGODlhAgACAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQIBQAAACwAAAAAAgACAAAIBgABCAQQEAAh+QQIDAAAACwAAAAAAgACAIEA/wAAAAAAAAAAAAAIBgABCAQQEAA7";

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
