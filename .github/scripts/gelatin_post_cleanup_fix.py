from pathlib import Path

root = Path(__file__).resolve().parents[2]
path = root / "tools/gelatin/src/Gelatin.Core/Imaging/AnimatedImageProcessor.cs"
text = path.read_text(encoding="utf-8")
old = "RawRgbaTransforms.FindTrimBounds(atlasPng, alphaThreshold, cancellationToken)"
new = "RawRgbaTransforms.FindTrimBoundsCancellable(atlasPng, alphaThreshold, cancellationToken)"
if text.count(old) != 1:
    raise RuntimeError(f"Expected exactly one static-image cancellable trim fallback; found {text.count(old)}")
path.write_text(text.replace(old, new, 1), encoding="utf-8", newline="\n")
print("Gelatin post-cleanup fallback rename fixed.")
