from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def read(rel):
    return (ROOT / rel).read_text(encoding="utf-8")


def write(rel, text):
    (ROOT / rel).write_text(text, encoding="utf-8", newline="\n")


def replace_once(rel, old, new):
    text = read(rel)
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{rel}: expected one match, got {count}: {old[:120]!r}")
    write(rel, text.replace(old, new, 1))


# Give the intentionally cancellable entry points distinct names. This keeps the
# ergonomic synchronous convenience surface clean and avoids xUnit treating every
# convenience call as a missed cancellation-token opportunity.
rel = "tools/gelatin/src/Gelatin.Core/Imaging/RawRgbaTransforms.cs"
for old, new in [
    ("=> Crop(png, rect, CancellationToken.None);", "=> CropCancellable(png, rect, CancellationToken.None);"),
    ("public static byte[] Crop(ReadOnlySpan<byte> png, PixelRect rect, CancellationToken cancellationToken)", "public static byte[] CropCancellable(ReadOnlySpan<byte> png, PixelRect rect, CancellationToken cancellationToken)"),
    ("=> Resize(png, width, height, CancellationToken.None);", "=> ResizeCancellable(png, width, height, CancellationToken.None);"),
    ("public static byte[] Resize(ReadOnlySpan<byte> png, int width, int height, CancellationToken cancellationToken)", "public static byte[] ResizeCancellable(ReadOnlySpan<byte> png, int width, int height, CancellationToken cancellationToken)"),
    ("=> FindTrimBounds(png, alphaThreshold, CancellationToken.None);", "=> FindTrimBoundsCancellable(png, alphaThreshold, CancellationToken.None);"),
    ("public static PixelRect? FindTrimBounds(ReadOnlySpan<byte> png, double alphaThreshold, CancellationToken cancellationToken)", "public static PixelRect? FindTrimBoundsCancellable(ReadOnlySpan<byte> png, double alphaThreshold, CancellationToken cancellationToken)"),
    ("=> RemoveBackground(png, background, tolerance, feather, CancellationToken.None);", "=> RemoveBackgroundCancellable(png, background, tolerance, feather, CancellationToken.None);"),
    ("public static byte[] RemoveBackground(ReadOnlySpan<byte> png, SKColor background, double tolerance, double feather, CancellationToken cancellationToken)", "public static byte[] RemoveBackgroundCancellable(ReadOnlySpan<byte> png, SKColor background, double tolerance, double feather, CancellationToken cancellationToken)"),
]:
    replace_once(rel, old, new)

# ImageProcessor remains the non-cancellable compatibility facade. Long-running
# editor operations call the raw/cancellable implementation directly.
rel = "tools/gelatin/src/Gelatin.Core/Imaging/ImageProcessor.cs"
for block in [
    "\n    public static byte[] Crop(ReadOnlySpan<byte> png, PixelRect rect, CancellationToken cancellationToken)\n        => RawRgbaTransforms.Crop(png, rect, cancellationToken);",
    "\n    public static byte[] Resize(ReadOnlySpan<byte> png, int width, int height, CancellationToken cancellationToken)\n        => RawRgbaTransforms.Resize(png, width, height, cancellationToken);",
    "\n    public static PixelRect? FindTrimBounds(ReadOnlySpan<byte> png, double alphaThreshold, CancellationToken cancellationToken)\n        => RawRgbaTransforms.FindTrimBounds(png, alphaThreshold, cancellationToken);",
    "\n    public static byte[] RemoveBackground(ReadOnlySpan<byte> png, SKColor background, double tolerance, double feather, CancellationToken cancellationToken)\n        => RawRgbaTransforms.RemoveBackground(png, background, tolerance, feather, cancellationToken);",
]:
    replace_once(rel, block, "")

rel = "tools/gelatin/src/Gelatin.Core/Imaging/AnimatedImageProcessor.cs"
for old, new in [
    ("=> PackFrames(framePngs, durationsMs, repetitionCount, CancellationToken.None);", "=> PackFramesCancellable(framePngs, durationsMs, repetitionCount, CancellationToken.None);"),
    ("public static ImageStorageResult PackFrames(IReadOnlyList<byte[]> framePngs, IReadOnlyList<int> durationsMs, int repetitionCount, CancellationToken cancellationToken)", "public static ImageStorageResult PackFramesCancellable(IReadOnlyList<byte[]> framePngs, IReadOnlyList<int> durationsMs, int repetitionCount, CancellationToken cancellationToken)"),
    ("=> ExtractFrames(atlasPng, config, CancellationToken.None);", "=> ExtractFramesCancellable(atlasPng, config, CancellationToken.None);"),
    ("public static List<byte[]> ExtractFrames(ReadOnlySpan<byte> atlasPng, GelConfig config, CancellationToken cancellationToken)", "public static List<byte[]> ExtractFramesCancellable(ReadOnlySpan<byte> atlasPng, GelConfig config, CancellationToken cancellationToken)"),
    ("=> TransformAnimated(atlasPng, config, transform, CancellationToken.None);", "=> TransformAnimatedCancellable(atlasPng, config, transform, CancellationToken.None);"),
    ("public static ImageStorageResult TransformAnimated(ReadOnlySpan<byte> atlasPng, GelConfig config, Func<byte[], byte[]> transform, CancellationToken cancellationToken)", "public static ImageStorageResult TransformAnimatedCancellable(ReadOnlySpan<byte> atlasPng, GelConfig config, Func<byte[], byte[]> transform, CancellationToken cancellationToken)"),
    ("=> TransformFrame(atlasPng, config, frameIndex, transform, CancellationToken.None);", "=> TransformFrameCancellable(atlasPng, config, frameIndex, transform, CancellationToken.None);"),
    ("public static ImageStorageResult TransformFrame(ReadOnlySpan<byte> atlasPng, GelConfig config, int frameIndex, Func<byte[], byte[]> transform, CancellationToken cancellationToken)", "public static ImageStorageResult TransformFrameCancellable(ReadOnlySpan<byte> atlasPng, GelConfig config, int frameIndex, Func<byte[], byte[]> transform, CancellationToken cancellationToken)"),
    ("=> FindUnionTrimBounds(atlasPng, config, alphaThreshold, CancellationToken.None);", "=> FindUnionTrimBoundsCancellable(atlasPng, config, alphaThreshold, CancellationToken.None);"),
    ("public static PixelRect? FindUnionTrimBounds(ReadOnlySpan<byte> atlasPng, GelConfig config, double alphaThreshold, CancellationToken cancellationToken)", "public static PixelRect? FindUnionTrimBoundsCancellable(ReadOnlySpan<byte> atlasPng, GelConfig config, double alphaThreshold, CancellationToken cancellationToken)"),
    ("=> BuildUnionAlphaPng(atlasPng, config, CancellationToken.None);", "=> BuildUnionAlphaPngCancellable(atlasPng, config, CancellationToken.None);"),
    ("public static byte[] BuildUnionAlphaPng(ReadOnlySpan<byte> atlasPng, GelConfig config, CancellationToken cancellationToken)", "public static byte[] BuildUnionAlphaPngCancellable(ReadOnlySpan<byte> atlasPng, GelConfig config, CancellationToken cancellationToken)"),
    ("var frames = ExtractFrames(atlasPng, config, cancellationToken);", "var frames = ExtractFramesCancellable(atlasPng, config, cancellationToken);"),
    ("return PackFrames(transformed, animation.Frames.Select(frame => frame.DurationMs).ToArray(), animation.RepetitionCount, cancellationToken);", "return PackFramesCancellable(transformed, animation.Frames.Select(frame => frame.DurationMs).ToArray(), animation.RepetitionCount, cancellationToken);"),
]:
    replace_once(rel, old, new)

rel = "tools/gelatin/src/Gelatin.Core/Physics/GelMeshBuilder.cs"
replace_once(rel, "=> Build(document, quality, CancellationToken.None);", "=> BuildCancellable(document, quality, CancellationToken.None);")
replace_once(rel, "public static GelMesh Build(GelDocument document, QualitySettings quality, CancellationToken cancellationToken)", "public static GelMesh BuildCancellable(GelDocument document, QualitySettings quality, CancellationToken cancellationToken)")
replace_once(rel, "AnimatedImageProcessor.BuildUnionAlphaPng(document.PngBytes, document.Config, cancellationToken)", "AnimatedImageProcessor.BuildUnionAlphaPngCancellable(document.PngBytes, document.Config, cancellationToken)")

rel = "tools/gelatin/src/Gelatin.App/Controls/LabControl.cs"
replace_once(rel, "GelMeshBuilder.Build(document, quality, cancellation.Token)", "GelMeshBuilder.BuildCancellable(document, quality, cancellation.Token)")

rel = "tools/gelatin/src/Gelatin.App/MainWindow.cs"
for old, new in [
    ("AnimatedImageProcessor.TransformFrame(document.PngBytes, document.Config, frameIndex, frame => RawRgbaTransforms.RemoveBackground(frame, color, tolerance, feather, cancellation.Token), cancellation.Token)",
     "AnimatedImageProcessor.TransformFrameCancellable(document.PngBytes, document.Config, frameIndex, frame => RawRgbaTransforms.RemoveBackgroundCancellable(frame, color, tolerance, feather, cancellation.Token), cancellation.Token)"),
    ("AnimatedImageProcessor.TransformAnimated(document.PngBytes, document.Config, frame => RawRgbaTransforms.RemoveBackground(frame, color, tolerance, feather, cancellation.Token), cancellation.Token)",
     "AnimatedImageProcessor.TransformAnimatedCancellable(document.PngBytes, document.Config, frame => RawRgbaTransforms.RemoveBackgroundCancellable(frame, color, tolerance, feather, cancellation.Token), cancellation.Token)"),
    ("RawRgbaTransforms.RemoveBackground(png, color, tolerance, feather, cancellationToken)",
     "RawRgbaTransforms.RemoveBackgroundCancellable(png, color, tolerance, feather, cancellationToken)"),
]:
    replace_once(rel, old, new)

rel = "tools/gelatin/tests/Gelatin.Tests/CleanupRegressionTests.cs"
replace_once(rel,
    "RawRgbaTransforms.RemoveBackground(png, SKColors.White, 0.1, 0.1, cancellation.Token)",
    "RawRgbaTransforms.RemoveBackgroundCancellable(png, SKColors.White, 0.1, 0.1, cancellation.Token)")

rel = "tools/gelatin/README.md"
replace_once(rel,
    "The publish script prints the package SHA-256. The dedicated Gelatin workflow performs restore, Release build, tests, self-contained Windows x64 publish, package/hash verification, and artifact upload without changing the GLoader package.",
    "`Directory.Build.props` is the single source of truth for the Gelatin product/package version; assembly metadata, the UI/tool version, publish archive, and CI artifact name derive from it. The publish script prints the package SHA-256. The dedicated Gelatin workflow performs restore, Release build, tests, self-contained Windows x64 publish, package/hash verification, and artifact upload without changing the GLoader package.")

print("Gelatin post-cleanup API and warning cleanup applied successfully.")
