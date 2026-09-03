#!/usr/bin/env python3
from pathlib import Path
import sys


def require(text: str, needle: str, label: str) -> None:
    if needle not in text:
        raise SystemExit(f"Missing promoted-inference source contract: {label}: {needle!r}")


def read(root: Path, relative: str) -> str:
    path = root / relative
    if not path.is_file():
        raise SystemExit(f"Missing Terraria source file: {path}")
    return path.read_text(encoding="utf-8")


def main() -> int:
    if len(sys.argv) != 2:
        raise SystemExit("usage: verify_promoted_inferences.py <Terraria source root>")

    root = Path(sys.argv[1])

    early = read(
        root,
        "Terraria.GameContent.Generation.Dungeon.Features/DungeonGlobalEarlyDualDungeonFeatures.cs",
    )
    require(early, "num2 = 8;", "Small evil Orb/Heart quota")
    require(early, "num2 = 14;", "Medium evil Orb/Heart quota")
    require(early, "num2 = 18;", "Large evil Orb/Heart quota")
    require(early, "int num13 = num2;", "Shadow Orb quota consumes num2")
    require(early, "int num14 = num2;", "Crimson Heart quota consumes num2")
    require(early, "WorldGen.AddShadowOrb(center.X, center.Y, crimsonHeart: false);", "Shadow Orb placement")
    require(early, "WorldGen.AddShadowOrb(center2.X, center2.Y, crimsonHeart: true);", "Crimson Heart placement")

    layout = read(
        root,
        "Terraria.GameContent.Generation.Dungeon.LayoutProviders/DualDungeonLayoutProvider.cs",
    )
    require(layout, "num4 = 2;", "Small Spider specialized-room quota")
    require(layout, "num4 = 6;", "Medium Spider specialized-room quota")
    require(layout, "num4 = 8;", "Large Spider specialized-room quota")
    require(layout, "dungeonRoom5.settings.StyleData = DungeonGenerationStyles.Spider;", "Spider room conversion")
    require(layout, "num4--;", "Spider quota decrement")

    paintings = read(
        root,
        "Terraria.GameContent.Generation.Dungeon.Features/DungeonGlobalPaintings.cs",
    )
    require(paintings, "lihzahrdPaintingsMax = 1;", "Small Lihzahrd painting cap")
    require(paintings, "lihzahrdPaintingsMax = 2;", "Medium Lihzahrd painting cap")
    require(paintings, "lihzahrdPaintingsMax = 2 + genRand.Next(2);", "Large Lihzahrd painting cap RNG")
    require(
        paintings,
        "lihzahrdPaintingsPlaced >= lihzahrdPaintingsMax",
        "Lihzahrd painting placement cap",
    )

    print("Promoted inference source contracts passed (1.4.5.8 shapes intact).")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
