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
        raise RuntimeError(f"{rel}: expected one match, got {count}: {old[:100]!r}")
    write(rel, text.replace(old, new, 1))


# Keep the public convenience signatures cancellation-token-free so xUnit callers
# do not acquire cancellation analyzer warnings merely because cancellable overloads exist.
rel = "tools/gelatin/src/Gelatin.Core/Imaging/RawRgbaTransforms.cs"
for old, new in [
    ("    public static byte[] Crop(ReadOnlySpan<byte> png, PixelRect rect, CancellationToken cancellationToken = default)\n",
     "    public static byte[] Crop(ReadOnlySpan<byte> png, PixelRect rect) => Crop(png, rect, CancellationToken.None);\n\n    public static byte[] Crop(ReadOnlySpan<byte> png, PixelRect rect, CancellationToken cancellationToken)\n"),
    ("    public static byte[] Resize(ReadOnlySpan<byte> png, int width, int height, CancellationToken cancellationToken = default)\n",
     "    public static byte[] Resize(ReadOnlySpan<byte> png, int width, int height) => Resize(png, width, height, CancellationToken.None);\n\n    public static byte[] Resize(ReadOnlySpan<byte> png, int width, int height, CancellationToken cancellationToken)\n"),
    ("    public static PixelRect? FindTrimBounds(ReadOnlySpan<byte> png, double alphaThreshold, CancellationToken cancellationToken = default)\n",
     "    public static PixelRect? FindTrimBounds(ReadOnlySpan<byte> png, double alphaThreshold) => FindTrimBounds(png, alphaThreshold, CancellationToken.None);\n\n    public static PixelRect? FindTrimBounds(ReadOnlySpan<byte> png, double alphaThreshold, CancellationToken cancellationToken)\n"),
    ("    public static byte[] RemoveBackground(ReadOnlySpan<byte> png, SKColor background, double tolerance, double feather, CancellationToken cancellationToken = default)\n",
     "    public static byte[] RemoveBackground(ReadOnlySpan<byte> png, SKColor background, double tolerance, double feather)\n        => RemoveBackground(png, background, tolerance, feather, CancellationToken.None);\n\n    public static byte[] RemoveBackground(ReadOnlySpan<byte> png, SKColor background, double tolerance, double feather, CancellationToken cancellationToken)\n"),
]:
    replace_once(rel, old, new)

rel = "tools/gelatin/src/Gelatin.Core/Imaging/ImageProcessor.cs"
for old, new in [
    ("    public static byte[] Crop(ReadOnlySpan<byte> png, PixelRect rect, CancellationToken cancellationToken = default)\n        => RawRgbaTransforms.Crop(png, rect, cancellationToken);",
     "    public static byte[] Crop(ReadOnlySpan<byte> png, PixelRect rect)\n        => RawRgbaTransforms.Crop(png, rect);\n\n    public static byte[] Crop(ReadOnlySpan<byte> png, PixelRect rect, CancellationToken cancellationToken)\n        => RawRgbaTransforms.Crop(png, rect, cancellationToken);"),
    ("    public static byte[] Resize(ReadOnlySpan<byte> png, int width, int height, CancellationToken cancellationToken = default)\n        => RawRgbaTransforms.Resize(png, width, height, cancellationToken);",
     "    public static byte[] Resize(ReadOnlySpan<byte> png, int width, int height)\n        => RawRgbaTransforms.Resize(png, width, height);\n\n    public static byte[] Resize(ReadOnlySpan<byte> png, int width, int height, CancellationToken cancellationToken)\n        => RawRgbaTransforms.Resize(png, width, height, cancellationToken);"),
    ("    public static PixelRect? FindTrimBounds(ReadOnlySpan<byte> png, double alphaThreshold, CancellationToken cancellationToken = default)\n        => RawRgbaTransforms.FindTrimBounds(png, alphaThreshold, cancellationToken);",
     "    public static PixelRect? FindTrimBounds(ReadOnlySpan<byte> png, double alphaThreshold)\n        => RawRgbaTransforms.FindTrimBounds(png, alphaThreshold);\n\n    public static PixelRect? FindTrimBounds(ReadOnlySpan<byte> png, double alphaThreshold, CancellationToken cancellationToken)\n        => RawRgbaTransforms.FindTrimBounds(png, alphaThreshold, cancellationToken);"),
    ("    public static byte[] RemoveBackground(ReadOnlySpan<byte> png, SKColor background, double tolerance, double feather, CancellationToken cancellationToken = default)\n        => RawRgbaTransforms.RemoveBackground(png, background, tolerance, feather, cancellationToken);",
     "    public static byte[] RemoveBackground(ReadOnlySpan<byte> png, SKColor background, double tolerance, double feather)\n        => RawRgbaTransforms.RemoveBackground(png, background, tolerance, feather);\n\n    public static byte[] RemoveBackground(ReadOnlySpan<byte> png, SKColor background, double tolerance, double feather, CancellationToken cancellationToken)\n        => RawRgbaTransforms.RemoveBackground(png, background, tolerance, feather, cancellationToken);"),
]:
    replace_once(rel, old, new)

rel = "tools/gelatin/src/Gelatin.Core/Imaging/AnimatedImageProcessor.cs"
for old, new in [
    ("    public static ImageStorageResult PackFrames(IReadOnlyList<byte[]> framePngs, IReadOnlyList<int> durationsMs, int repetitionCount, CancellationToken cancellationToken = default)\n",
     "    public static ImageStorageResult PackFrames(IReadOnlyList<byte[]> framePngs, IReadOnlyList<int> durationsMs, int repetitionCount)\n        => PackFrames(framePngs, durationsMs, repetitionCount, CancellationToken.None);\n\n    public static ImageStorageResult PackFrames(IReadOnlyList<byte[]> framePngs, IReadOnlyList<int> durationsMs, int repetitionCount, CancellationToken cancellationToken)\n"),
    ("    public static List<byte[]> ExtractFrames(ReadOnlySpan<byte> atlasPng, GelConfig config, CancellationToken cancellationToken = default)\n",
     "    public static List<byte[]> ExtractFrames(ReadOnlySpan<byte> atlasPng, GelConfig config)\n        => ExtractFrames(atlasPng, config, CancellationToken.None);\n\n    public static List<byte[]> ExtractFrames(ReadOnlySpan<byte> atlasPng, GelConfig config, CancellationToken cancellationToken)\n"),
    ("    public static ImageStorageResult TransformAnimated(ReadOnlySpan<byte> atlasPng, GelConfig config, Func<byte[], byte[]> transform, CancellationToken cancellationToken = default)\n",
     "    public static ImageStorageResult TransformAnimated(ReadOnlySpan<byte> atlasPng, GelConfig config, Func<byte[], byte[]> transform)\n        => TransformAnimated(atlasPng, config, transform, CancellationToken.None);\n\n    public static ImageStorageResult TransformAnimated(ReadOnlySpan<byte> atlasPng, GelConfig config, Func<byte[], byte[]> transform, CancellationToken cancellationToken)\n"),
    ("    public static ImageStorageResult TransformFrame(ReadOnlySpan<byte> atlasPng, GelConfig config, int frameIndex, Func<byte[], byte[]> transform, CancellationToken cancellationToken = default)\n",
     "    public static ImageStorageResult TransformFrame(ReadOnlySpan<byte> atlasPng, GelConfig config, int frameIndex, Func<byte[], byte[]> transform)\n        => TransformFrame(atlasPng, config, frameIndex, transform, CancellationToken.None);\n\n    public static ImageStorageResult TransformFrame(ReadOnlySpan<byte> atlasPng, GelConfig config, int frameIndex, Func<byte[], byte[]> transform, CancellationToken cancellationToken)\n"),
    ("    public static PixelRect? FindUnionTrimBounds(ReadOnlySpan<byte> atlasPng, GelConfig config, double alphaThreshold, CancellationToken cancellationToken = default)\n",
     "    public static PixelRect? FindUnionTrimBounds(ReadOnlySpan<byte> atlasPng, GelConfig config, double alphaThreshold)\n        => FindUnionTrimBounds(atlasPng, config, alphaThreshold, CancellationToken.None);\n\n    public static PixelRect? FindUnionTrimBounds(ReadOnlySpan<byte> atlasPng, GelConfig config, double alphaThreshold, CancellationToken cancellationToken)\n"),
    ("    public static byte[] BuildUnionAlphaPng(ReadOnlySpan<byte> atlasPng, GelConfig config, CancellationToken cancellationToken = default)\n",
     "    public static byte[] BuildUnionAlphaPng(ReadOnlySpan<byte> atlasPng, GelConfig config)\n        => BuildUnionAlphaPng(atlasPng, config, CancellationToken.None);\n\n    public static byte[] BuildUnionAlphaPng(ReadOnlySpan<byte> atlasPng, GelConfig config, CancellationToken cancellationToken)\n"),
]:
    replace_once(rel, old, new)

rel = "tools/gelatin/src/Gelatin.Core/Physics/GelMeshBuilder.cs"
replace_once(rel,
    "    public static GelMesh Build(GelDocument document, QualitySettings quality, CancellationToken cancellationToken = default)\n",
    "    public static GelMesh Build(GelDocument document, QualitySettings quality)\n        => Build(document, quality, CancellationToken.None);\n\n    public static GelMesh Build(GelDocument document, QualitySettings quality, CancellationToken cancellationToken)\n")
replace_once(rel,
    "        var contourSource = AnimatedImageProcessor.BuildUnionAlphaPng(document.PngBytes, document.Config);",
    "        var contourSource = AnimatedImageProcessor.BuildUnionAlphaPng(document.PngBytes, document.Config, cancellationToken);")

# The smoke test should follow the centralized product version instead of pinning an old literal.
rel = "tools/gelatin/tests/Gelatin.Tests/UiSmokeTests.cs"
text = read(rel)
if "using Gelatin.Core;\n" not in text:
    text = text.replace("using Gelatin.App.Controls;\n", "using Gelatin.App.Controls;\nusing Gelatin.Core;\n", 1)
text = text.replace('Assert.Contains("Gelatin 0.1.5", window.Title);', 'Assert.Contains($"Gelatin {GelatinProduct.Version}", window.Title);', 1)
write(rel, text)

print("Gelatin cleanup validation fixes applied successfully.")
