from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]

VERSION_FILES = [
    "tools/gelatin/src/Gelatin.App/Gelatin.App.csproj",
    "tools/gelatin/src/Gelatin.App/DocumentController.cs",
    "tools/gelatin/src/Gelatin.App/MainWindow.cs",
    "tools/gelatin/tests/Gelatin.Tests/UiSmokeTests.cs",
    "tools/gelatin/scripts/publish.ps1",
    "tools/gelatin/README.md",
]

for rel in VERSION_FILES:
    path = ROOT / rel
    text = path.read_text(encoding="utf-8")
    if "0.1.3" not in text:
        raise RuntimeError(f"Expected at least one 0.1.3 reference in {rel}")
    path.write_text(text.replace("0.1.3", "0.1.4"), encoding="utf-8", newline="\n")

# README compatibility sentence should include the now-previous 0.1.3 release.
readme = ROOT / "tools/gelatin/README.md"
text = readme.read_text(encoding="utf-8")
text = text.replace(
    "Gelatin continues to read 0.1.0/0.1.1/0.1.2 static GEL1 files without migration.",
    "Gelatin continues to read 0.1.0/0.1.1/0.1.2/0.1.3 static GEL1 files without migration.",
)
readme.write_text(text, encoding="utf-8", newline="\n")

# The initial implementation script is no longer part of the finished tree.
old_script = ROOT / ".github/scripts/apply-gelatin-frame-edit.py"
if old_script.exists():
    old_script.unlink()

# Remove this finalizer after it has done its job; workflow cleanup is performed
# through the direct GitHub connection because GITHUB_TOKEN cannot mutate workflows.
Path(__file__).unlink()

print("Gelatin frame-edit finalization applied: version 0.1.4, temporary scripts removed.")
