using System.Numerics;

namespace Gelatin.Core.Runtime;

public readonly record struct RuntimeRgb(byte R, byte G, byte B);

public static class GelRuntimeSemantics
{
    public const string TintOff = "off";
    public const string TintRandomNeon = "random_neon";
    public const double DefaultOpacity = 1.0;
    public const double DefaultTintIntensity = 1.0;
    public const double DefaultSpeedPixelsPerSecond = 320.0;
    public const double MinSpeedPixelsPerSecond = 1.0;
    public const double MaxSpeedPixelsPerSecond = 2000.0;
    public const double DefaultRestitution = 0.82;
    public const double DefaultFriction = 0.015;

    private static readonly RuntimeRgb[] Palette =
    [
        new(255, 45, 170),
        new(0, 245, 255),
        new(168, 255, 0),
        new(191, 64, 255),
        new(255, 90, 0),
        new(36, 123, 255),
        new(255, 235, 0),
        new(255, 0, 229)
    ];

    public static IReadOnlyList<RuntimeRgb> NeonPalette => Palette;
    public static Vector2 InitialDirection { get; } = Vector2.Normalize(new Vector2(0.34f, 0.21f));

    public static Vector2 InitialPixelVelocity(double speedPixelsPerSecond)
    {
        ValidateSpeed(speedPixelsPerSecond);
        return InitialDirection * (float)speedPixelsPerSecond;
    }

    public static Vector2 InitialWorldVelocity(double speedPixelsPerSecond, double viewportWidthPixels, double viewportHeightPixels)
    {
        ValidateSpeed(speedPixelsPerSecond);
        ValidateViewport(viewportWidthPixels, viewportHeightPixels);
        var pixels = InitialPixelVelocity(speedPixelsPerSecond);
        return new Vector2(pixels.X / (float)viewportWidthPixels, pixels.Y / (float)viewportHeightPixels);
    }

    public static double PixelSpeed(Vector2 worldVelocity, double viewportWidthPixels, double viewportHeightPixels)
    {
        ValidateViewport(viewportWidthPixels, viewportHeightPixels);
        var pixels = new Vector2(worldVelocity.X * (float)viewportWidthPixels, worldVelocity.Y * (float)viewportHeightPixels);
        return pixels.Length();
    }

    public static RuntimeRgb Blend(RuntimeRgb source, RuntimeRgb tint, double intensity)
    {
        intensity = Math.Clamp(intensity, 0, 1);
        return new RuntimeRgb(
            BlendChannel(source.R, tint.R, intensity),
            BlendChannel(source.G, tint.G, intensity),
            BlendChannel(source.B, tint.B, intensity));
    }

    public static byte ApplyOpacity(byte sourceAlpha, double opacity)
    {
        opacity = Math.Clamp(opacity, 0, 1);
        return checked((byte)Math.Clamp(
            Math.Round(sourceAlpha * opacity, MidpointRounding.AwayFromZero),
            byte.MinValue,
            byte.MaxValue));
    }

    public static float[] CreateColorMatrix(RuntimeRgb? tint, double tintIntensity, double opacity)
    {
        var intensity = tint is null ? 0f : (float)Math.Clamp(tintIntensity, 0, 1);
        var keep = 1f - intensity;
        var alpha = (float)Math.Clamp(opacity, 0, 1);
        var color = tint ?? default;
        return
        [
            keep, 0, 0, 0, intensity * color.R,
            0, keep, 0, 0, intensity * color.G,
            0, 0, keep, 0, intensity * color.B,
            0, 0, 0, alpha, 0
        ];
    }

    public static int NextNeonIndex(Random random, int previousIndex = -1)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (Palette.Length == 1) return 0;
        if (previousIndex < 0 || previousIndex >= Palette.Length) return random.Next(Palette.Length);
        var candidate = random.Next(Palette.Length - 1);
        if (candidate >= previousIndex) candidate++;
        return candidate;
    }

    public static double AdvanceAnimationElapsedMilliseconds(double currentMilliseconds, double elapsedSeconds, bool paused)
    {
        if (paused || elapsedSeconds <= 0 || !double.IsFinite(elapsedSeconds)) return currentMilliseconds;
        return currentMilliseconds + elapsedSeconds * 1000.0;
    }

    private static byte BlendChannel(byte source, byte tint, double intensity)
        => checked((byte)Math.Clamp(
            Math.Round(source * (1 - intensity) + tint * intensity, MidpointRounding.AwayFromZero),
            byte.MinValue,
            byte.MaxValue));

    private static void ValidateSpeed(double speedPixelsPerSecond)
    {
        if (!double.IsFinite(speedPixelsPerSecond) || speedPixelsPerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(speedPixelsPerSecond));
    }

    private static void ValidateViewport(double width, double height)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Preview dimensions must be finite and positive.");
    }
}

public sealed class BounceTintState
{
    public int CurrentIndex { get; private set; } = -1;
    public RuntimeRgb? CurrentTint => CurrentIndex >= 0 ? GelRuntimeSemantics.NeonPalette[CurrentIndex] : null;

    public void Reset() => CurrentIndex = -1;

    public bool OnBounce(string tintMode, Random random)
    {
        if (!string.Equals(tintMode, GelRuntimeSemantics.TintRandomNeon, StringComparison.Ordinal)) return false;
        CurrentIndex = GelRuntimeSemantics.NextNeonIndex(random, CurrentIndex);
        return true;
    }
}
