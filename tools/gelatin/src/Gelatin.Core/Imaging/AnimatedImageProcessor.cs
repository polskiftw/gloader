using Gelatin.Core.Models;
using SkiaSharp;

namespace Gelatin.Core.Imaging;

public sealed record ImageStorageResult(byte[] PngBytes, int Width, int Height, AnimationConfig? Animation)
{
    public bool IsAnimated => Animation is not null;
}

public static class AnimatedImageProcessor
{
    public const int MinimumPlaybackFrameDurationMs = 10;
    public const long MaxDecodedAnimationPixels = 64L * 1024 * 1024;

    public static bool IsAnimated(GelConfig config) => config.SchemaVersion == 2 && config.Animation is { Frames.Count: >= 2 };

    public static ImageStorageResult NormalizeInput(ReadOnlySpan<byte> encoded)
    {
        try
        {
            using var data = SKData.CreateCopy(encoded);
            using var codec = SKCodec.Create(data) ?? throw new GelFormatException("The image is unsupported or corrupt.");
            if (codec.EncodedFormat == SKEncodedImageFormat.Gif && codec.FrameCount > 1)
                return ImportGif(encoded);
        }
        catch (GelFormatException) { throw; }
        catch (Exception ex)
        {
            throw new GelFormatException("The image is unsupported or corrupt.", ex);
        }

        var png = RawRgbaTransforms.NormalizeToPng(encoded);
        var dimensions = ImageProcessor.GetDimensions(png);
        return new ImageStorageResult(png, dimensions.Width, dimensions.Height, null);
    }

    public static ImageStorageResult ImportGif(ReadOnlySpan<byte> encoded)
    {
        try
        {
            using var data = SKData.CreateCopy(encoded);
            using var codec = SKCodec.Create(data) ?? throw new GelFormatException("The GIF is unsupported or corrupt.");
            if (codec.EncodedFormat != SKEncodedImageFormat.Gif)
                throw new GelFormatException("The supplied image is not a GIF.");

            var frameCount = codec.FrameCount;
            if (frameCount <= 1)
            {
                var png = RawRgbaTransforms.NormalizeToPng(encoded);
                var dimensions = ImageProcessor.GetDimensions(png);
                return new ImageStorageResult(png, dimensions.Width, dimensions.Height, null);
            }
            if (frameCount > GelValidator.MaxAnimationFrames)
                throw new GelFormatException($"Animated GIFs may contain at most {GelValidator.MaxAnimationFrames} frames.");

            var width = codec.Info.Width;
            var height = codec.Info.Height;
            ValidateLogicalDimensions(width, height, frameCount);
            var frameInfo = codec.FrameInfo;
            var frames = new List<byte[]>(frameCount);
            var durations = new List<int>(frameCount);
            var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);

            for (var index = 0; index < frameCount; index++)
            {
                using var bitmap = new SKBitmap(info);
                bitmap.Erase(SKColors.Transparent);
                var result = codec.GetPixels(info, bitmap.GetPixels(), new SKCodecOptions(index));
                if (result != SKCodecResult.Success)
                    throw new GelFormatException($"GIF frame {index + 1} could not be decoded ({result}).");
                frames.Add(RawRgbaCodec.Encode(bitmap));
                durations.Add(index < frameInfo.Length ? Math.Max(0, frameInfo[index].Duration) : 0);
            }

            return PackFrames(frames, durations, codec.RepetitionCount);
        }
        catch (GelFormatException) { throw; }
        catch (Exception ex) when (ex is ArgumentException or OverflowException)
        {
            throw new GelFormatException("The animated GIF could not be decoded.", ex);
        }
    }

    public static ImageStorageResult PackFrames(IReadOnlyList<byte[]> framePngs, IReadOnlyList<int> durationsMs, int repetitionCount)
        => PackFrames(framePngs, durationsMs, repetitionCount, CancellationToken.None);

    public static ImageStorageResult PackFrames(IReadOnlyList<byte[]> framePngs, IReadOnlyList<int> durationsMs, int repetitionCount, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(framePngs);
        ArgumentNullException.ThrowIfNull(durationsMs);
        if (framePngs.Count is < 2 or > GelValidator.MaxAnimationFrames)
            throw new GelFormatException($"Animated assets must contain 2 to {GelValidator.MaxAnimationFrames} frames.");
        if (durationsMs.Count != framePngs.Count) throw new GelFormatException("Animation frame timing count does not match the frame count.");
        if (repetitionCount < -1 || repetitionCount > 1_000_000) throw new GelFormatException("Animation repetition count is invalid.");

        var decoded = new List<RgbaBuffer>(framePngs.Count);
        foreach (var png in framePngs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            decoded.Add(RawRgbaCodec.Decode(png));
        }
        return PackDecodedFrames(decoded, durationsMs, repetitionCount, cancellationToken);
    }

    private static ImageStorageResult PackDecodedFrames(IReadOnlyList<RgbaBuffer> decoded, IReadOnlyList<int> durationsMs, int repetitionCount, CancellationToken cancellationToken)
    {
        var width = decoded[0].Width;
        var height = decoded[0].Height;
        ValidateLogicalDimensions(width, height, decoded.Count);
        if (decoded.Any(frame => frame.Width != width || frame.Height != height))
            throw new GelFormatException("Every animation frame must have identical dimensions.");

        for (var i = 0; i < durationsMs.Count; i++)
            if (durationsMs[i] < 0 || durationsMs[i] > GelValidator.MaxAnimationFrameDurationMs)
                throw new GelFormatException($"Animation frame {i + 1} has an invalid duration.");

        var columns = Math.Min(decoded.Count, Math.Max(1, GelValidator.MaxDimension / width));
        var rows = (decoded.Count + columns - 1) / columns;
        var atlasWidth = checked(columns * width);
        var atlasHeight = checked(rows * height);
        if (atlasWidth > GelValidator.MaxDimension || atlasHeight > GelValidator.MaxDimension)
            throw new GelFormatException("The animation frames cannot fit inside the GEL atlas dimension limit.");
        var atlasPixels = checked((long)atlasWidth * atlasHeight);
        if (atlasPixels > MaxDecodedAnimationPixels)
            throw new GelFormatException("The animation atlas is too large to author safely.");

        var atlas = new byte[checked(atlasWidth * atlasHeight * 4)];
        var frameConfigs = new List<AnimationFrameConfig>(decoded.Count);
        for (var index = 0; index < decoded.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var column = index % columns;
            var row = index / columns;
            var x = column * width;
            var y = row * height;
            var frame = decoded[index];
            for (var sourceY = 0; sourceY < height; sourceY++)
            {
                var sourceOffset = checked(sourceY * width * 4);
                var destinationOffset = checked(((y + sourceY) * atlasWidth + x) * 4);
                frame.Pixels.AsSpan(sourceOffset, width * 4).CopyTo(atlas.AsSpan(destinationOffset, width * 4));
            }
            frameConfigs.Add(new AnimationFrameConfig
            {
                X = x,
                Y = y,
                Width = width,
                Height = height,
                DurationMs = durationsMs[index]
            });
        }

        return new ImageStorageResult(
            RawRgbaCodec.Encode(atlasWidth, atlasHeight, atlas),
            width,
            height,
            new AnimationConfig { RepetitionCount = repetitionCount, Frames = frameConfigs });
    }

    public static byte[] GetFramePng(GelDocument document, int frameIndex)
        => GetFramePng(document.PngBytes, document.Config, frameIndex);

    public static byte[] GetFramePng(ReadOnlySpan<byte> atlasPng, GelConfig config, int frameIndex)
    {
        if (!IsAnimated(config)) return atlasPng.ToArray();
        var atlas = RawRgbaCodec.Decode(atlasPng);
        ValidateAtlas(config, atlas.Width, atlas.Height);
        var animation = config.Animation!;
        frameIndex = Math.Clamp(frameIndex, 0, animation.Frames.Count - 1);
        var frame = animation.Frames[frameIndex];
        return RawRgbaCodec.Encode(frame.Width, frame.Height, ExtractFramePixels(atlas, frame));
    }

    public static List<byte[]> ExtractFrames(ReadOnlySpan<byte> atlasPng, GelConfig config)
        => ExtractFrames(atlasPng, config, CancellationToken.None);

    public static List<byte[]> ExtractFrames(ReadOnlySpan<byte> atlasPng, GelConfig config, CancellationToken cancellationToken)
    {
        if (!IsAnimated(config)) return [atlasPng.ToArray()];
        var atlas = RawRgbaCodec.Decode(atlasPng);
        ValidateAtlas(config, atlas.Width, atlas.Height);
        var result = new List<byte[]>(config.Animation!.Frames.Count);
        foreach (var frame in config.Animation.Frames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.Add(RawRgbaCodec.Encode(frame.Width, frame.Height, ExtractFramePixels(atlas, frame)));
        }
        return result;
    }

    public static ImageStorageResult TransformAnimated(ReadOnlySpan<byte> atlasPng, GelConfig config, Func<byte[], byte[]> transform)
        => TransformAnimated(atlasPng, config, transform, CancellationToken.None);

    public static ImageStorageResult TransformAnimated(ReadOnlySpan<byte> atlasPng, GelConfig config, Func<byte[], byte[]> transform, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transform);
        if (!IsAnimated(config)) throw new GelFormatException("The requested operation requires an animated GEL asset.");
        var animation = config.Animation!;
        var frames = ExtractFrames(atlasPng, config, cancellationToken);
        var transformed = new List<byte[]>(frames.Count);
        foreach (var frame in frames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            transformed.Add(transform(frame));
        }
        return PackFrames(transformed, animation.Frames.Select(frame => frame.DurationMs).ToArray(), animation.RepetitionCount, cancellationToken);
    }

    public static ImageStorageResult TransformFrame(ReadOnlySpan<byte> atlasPng, GelConfig config, int frameIndex, Func<byte[], byte[]> transform)
        => TransformFrame(atlasPng, config, frameIndex, transform, CancellationToken.None);

    public static ImageStorageResult TransformFrame(ReadOnlySpan<byte> atlasPng, GelConfig config, int frameIndex, Func<byte[], byte[]> transform, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transform);
        if (!IsAnimated(config)) throw new GelFormatException("The requested operation requires an animated GEL asset.");
        var animation = config.Animation!;
        if (frameIndex < 0 || frameIndex >= animation.Frames.Count) throw new ArgumentOutOfRangeException(nameof(frameIndex));

        var atlas = RawRgbaCodec.Decode(atlasPng);
        ValidateAtlas(config, atlas.Width, atlas.Height);
        var frame = animation.Frames[frameIndex];
        cancellationToken.ThrowIfCancellationRequested();
        var transformedPng = transform(RawRgbaCodec.Encode(frame.Width, frame.Height, ExtractFramePixels(atlas, frame)));
        cancellationToken.ThrowIfCancellationRequested();
        var transformed = RawRgbaCodec.Decode(transformedPng);
        if (transformed.Width != frame.Width || transformed.Height != frame.Height)
            throw new GelFormatException("A current-frame edit may not change the shared animation canvas dimensions.");
        CopyFramePixels(atlas, frame, transformed.Pixels);
        return new ImageStorageResult(
            RawRgbaCodec.Encode(atlas.Width, atlas.Height, atlas.Pixels),
            config.Image.Width,
            config.Image.Height,
            animation.DeepClone());
    }

    public static PixelRect? FindUnionTrimBounds(ReadOnlySpan<byte> atlasPng, GelConfig config, double alphaThreshold)
        => FindUnionTrimBounds(atlasPng, config, alphaThreshold, CancellationToken.None);

    public static PixelRect? FindUnionTrimBounds(ReadOnlySpan<byte> atlasPng, GelConfig config, double alphaThreshold, CancellationToken cancellationToken)
    {
        if (!IsAnimated(config)) return RawRgbaTransforms.FindTrimBounds(atlasPng, alphaThreshold, cancellationToken);
        var atlas = RawRgbaCodec.Decode(atlasPng);
        ValidateAtlas(config, atlas.Width, atlas.Height);
        var threshold = (byte)Math.Clamp(Math.Round(alphaThreshold * 255), 0, 255);
        var minX = config.Image.Width;
        var minY = config.Image.Height;
        var maxX = -1;
        var maxY = -1;
        foreach (var frame in config.Animation!.Frames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var y = 0; y < frame.Height; y++)
            for (var x = 0; x < frame.Width; x++)
            {
                var offset = (((frame.Y + y) * atlas.Width) + frame.X + x) * 4 + 3;
                if (atlas.Pixels[offset] <= threshold) continue;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }
        return maxX < minX ? null : new PixelRect(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    public static byte[] BuildUnionAlphaPng(ReadOnlySpan<byte> atlasPng, GelConfig config)
        => BuildUnionAlphaPng(atlasPng, config, CancellationToken.None);

    public static byte[] BuildUnionAlphaPng(ReadOnlySpan<byte> atlasPng, GelConfig config, CancellationToken cancellationToken)
    {
        if (!IsAnimated(config)) return atlasPng.ToArray();
        var atlas = RawRgbaCodec.Decode(atlasPng);
        ValidateAtlas(config, atlas.Width, atlas.Height);
        var width = config.Image.Width;
        var height = config.Image.Height;
        var frames = config.Animation!.Frames;
        var union = ExtractFramePixels(atlas, frames[0]);
        for (var frameIndex = 1; frameIndex < frames.Count; frameIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var frame = frames[frameIndex];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var unionOffset = (y * width + x) * 4;
                var atlasOffset = (((frame.Y + y) * atlas.Width) + frame.X + x) * 4;
                if (atlas.Pixels[atlasOffset + 3] <= union[unionOffset + 3]) continue;
                atlas.Pixels.AsSpan(atlasOffset, 4).CopyTo(union.AsSpan(unionOffset, 4));
            }
        }
        return RawRgbaCodec.Encode(width, height, union);
    }

    private static byte[] ExtractFramePixels(RgbaBuffer atlas, AnimationFrameConfig frame)
    {
        var pixels = new byte[checked(frame.Width * frame.Height * 4)];
        var rowBytes = checked(frame.Width * 4);
        for (var y = 0; y < frame.Height; y++)
        {
            var sourceOffset = checked(((frame.Y + y) * atlas.Width + frame.X) * 4);
            atlas.Pixels.AsSpan(sourceOffset, rowBytes).CopyTo(pixels.AsSpan(y * rowBytes, rowBytes));
        }
        return pixels;
    }

    private static void CopyFramePixels(RgbaBuffer atlas, AnimationFrameConfig frame, ReadOnlySpan<byte> pixels)
    {
        var rowBytes = checked(frame.Width * 4);
        if (pixels.Length != checked(rowBytes * frame.Height))
            throw new GelFormatException("The edited animation frame has an invalid RGBA buffer length.");
        for (var y = 0; y < frame.Height; y++)
        {
            var destinationOffset = checked(((frame.Y + y) * atlas.Width + frame.X) * 4);
            pixels.Slice(y * rowBytes, rowBytes).CopyTo(atlas.Pixels.AsSpan(destinationOffset, rowBytes));
        }
    }

    public static long FrameStartTimeMilliseconds(AnimationConfig? animation, int frameIndex)
    {
        if (animation is null || animation.Frames.Count == 0) return 0;
        frameIndex = Math.Clamp(frameIndex, 0, animation.Frames.Count - 1);
        long start = 0;
        for (var index = 0; index < frameIndex; index++)
            start = checked(start + EffectiveDuration(animation.Frames[index].DurationMs));
        return start;
    }

    public static int FrameIndexAtTime(AnimationConfig? animation, double elapsedMilliseconds)
    {
        if (animation is null || animation.Frames.Count == 0) return 0;
        if (!double.IsFinite(elapsedMilliseconds) || elapsedMilliseconds < 0) elapsedMilliseconds = 0;
        long passDuration = 0;
        foreach (var frame in animation.Frames) passDuration = checked(passDuration + EffectiveDuration(frame.DurationMs));
        if (passDuration <= 0) return 0;

        if (animation.RepetitionCount >= 0)
        {
            var totalPasses = (long)animation.RepetitionCount + 1;
            var totalDuration = checked(passDuration * totalPasses);
            if (elapsedMilliseconds >= totalDuration) return animation.Frames.Count - 1;
        }

        var position = elapsedMilliseconds % passDuration;
        long cursor = 0;
        for (var index = 0; index < animation.Frames.Count; index++)
        {
            cursor += EffectiveDuration(animation.Frames[index].DurationMs);
            if (position < cursor) return index;
        }
        return animation.Frames.Count - 1;
    }

    public static int EffectiveDuration(int durationMs) => Math.Max(MinimumPlaybackFrameDurationMs, durationMs);

    public static void ValidateAtlas(GelConfig config, int atlasWidth, int atlasHeight)
    {
        if (!IsAnimated(config)) return;
        foreach (var frame in config.Animation!.Frames)
        {
            if ((long)frame.X + frame.Width > atlasWidth || (long)frame.Y + frame.Height > atlasHeight)
                throw new GelFormatException("An animation frame rectangle lies outside the embedded PNG atlas.");
        }
    }

    private static void ValidateLogicalDimensions(int width, int height, int frameCount)
    {
        if (width is < 1 or > GelValidator.MaxDimension || height is < 1 or > GelValidator.MaxDimension)
            throw new GelFormatException($"Animation frame dimensions must be between 1 and {GelValidator.MaxDimension} pixels.");
        var decodedPixels = checked((long)width * height * frameCount);
        if (decodedPixels > MaxDecodedAnimationPixels)
            throw new GelFormatException("The animated image contains too many decoded pixels to author safely.");
    }
}

public sealed class AnimationAlphaBrushSession : IDisposable
{
    private readonly List<AlphaBrushSession> _frames;
    private readonly AnimationConfig _animation;
    private readonly int? _targetFrameIndex;
    private bool _disposed;

    public int FrameCount => _frames.Count;

    public AnimationAlphaBrushSession(ReadOnlySpan<byte> atlasPng, ReadOnlySpan<byte> recoveryAtlasPng, GelConfig config, AlphaBrushMode mode, double size, int? targetFrameIndex = null)
    {
        if (!AnimatedImageProcessor.IsAnimated(config)) throw new GelFormatException("Animated alpha painting requires an animated GEL asset.");
        _animation = config.Animation!.DeepClone();
        if (targetFrameIndex is int target && (target < 0 || target >= _animation.Frames.Count)) throw new ArgumentOutOfRangeException(nameof(targetFrameIndex));
        _targetFrameIndex = targetFrameIndex;
        var current = AnimatedImageProcessor.ExtractFrames(atlasPng, config);
        var recovery = AnimatedImageProcessor.ExtractFrames(recoveryAtlasPng, config);
        if (current.Count != recovery.Count) throw new GelFormatException("The animated alpha recovery source has the wrong frame count.");
        _frames = new List<AlphaBrushSession>(current.Count);
        try
        {
            for (var index = 0; index < current.Count; index++)
                _frames.Add(new AlphaBrushSession(current[index], recovery[index], mode, size));
        }
        catch
        {
            foreach (var frame in _frames) frame.Dispose();
            throw;
        }
    }

    public void ApplyPoint(PixelPoint point)
    {
        ThrowIfDisposed();
        foreach (var frame in TargetFrames()) frame.ApplyPoint(point);
    }

    public void ApplySegment(PixelPoint start, PixelPoint end)
    {
        ThrowIfDisposed();
        foreach (var frame in TargetFrames()) frame.ApplySegment(start, end);
    }

    public byte[] EncodePreview(int frameIndex)
    {
        ThrowIfDisposed();
        return _frames[Math.Clamp(frameIndex, 0, _frames.Count - 1)].Encode();
    }

    public ImageStorageResult Encode()
    {
        ThrowIfDisposed();
        return AnimatedImageProcessor.PackFrames(
            _frames.Select(frame => frame.Encode()).ToArray(),
            _animation.Frames.Select(frame => frame.DurationMs).ToArray(),
            _animation.RepetitionCount);
    }

    private IEnumerable<AlphaBrushSession> TargetFrames()
    {
        if (_targetFrameIndex is int target)
        {
            yield return _frames[target];
            yield break;
        }
        foreach (var frame in _frames) yield return frame;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var frame in _frames) frame.Dispose();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
