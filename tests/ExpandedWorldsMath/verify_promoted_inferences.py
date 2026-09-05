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
    require(paintings, "lihzahrdPaintingsMax = 1;", "Small Dual Dungeon Lihzahrd painting cap")
    require(paintings, "lihzahrdPaintingsMax = 2;", "Medium Dual Dungeon Lihzahrd painting cap")
    require(paintings, "lihzahrdPaintingsMax = 2 + genRand.Next(2);", "Large Dual Dungeon Lihzahrd painting cap RNG")
    require(
        paintings,
        "lihzahrdPaintingsPlaced >= lihzahrdPaintingsMax",
        "Dual Dungeon Lihzahrd painting placement cap",
    )

    biome_room = read(
        root,
        "Terraria.GameContent.Generation.Dungeon.Rooms/BiomeDungeonRoom.cs",
    )
    require(biome_room, "float num = (float)Main.maxTilesX / 4200f;", "Dual Dungeon biome-room width scalar")
    require(biome_room, "return (int)(50f * num);", "Dual Dungeon Temple biome-room physical scaling")

    worldgen = read(root, "Terraria/WorldGen.cs")
    require(worldgen, "double num = (double)Main.maxTilesX / 4200.0;", "legacy Temple width scalar")
    require(
        worldgen,
        "int num2 = genRand.Next((int)(num * 10.0), (int)(num * 16.0));",
        "legacy Temple room budget",
    )
    require(worldgen, "int num18 = 1;", "legacy Temple Lihzahrd painting base")
    require(worldgen, "if (Main.maxTilesX > 4200)", "legacy Temple Medium painting gate")
    require(worldgen, "if (Main.maxTilesX > 6400)", "legacy Temple Large painting gate")
    require(worldgen, "num18 += genRand.Next(2);", "legacy Temple Large painting RNG")

    print("Promoted inference source contracts passed (1.4.5.8 shapes intact).")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
