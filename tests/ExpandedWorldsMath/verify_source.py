#!/usr/bin/env python3
"""Fail-closed source-contract audit for Terraria 1.4.5.8 Expanded Worlds.

This validates only retail source facts the mod relies on. It intentionally does
not reimplement Terraria worldgen.
"""
from __future__ import annotations

import argparse
import re
from pathlib import Path

ASSERTIONS = 0


def compact(text: str) -> str:
    return re.sub(r"\s+", " ", text).strip()


def find_one(root: Path, suffix: str) -> Path:
    matches = [p for p in root.rglob(Path(suffix).name) if p.as_posix().endswith(suffix)]
    if len(matches) != 1:
        raise AssertionError(f"Expected exactly one {suffix}, found {len(matches)}: {matches[:8]}")
    return matches[0]


def require(text: str, pattern: str, label: str, *, flags: int = 0) -> None:
    global ASSERTIONS
    ASSERTIONS += 1
    if re.search(pattern, text, flags) is None:
        raise AssertionError(f"Source contract failed: {label}\nPattern: {pattern}")


def require_literal(text: str, literal: str, label: str) -> None:
    global ASSERTIONS
    ASSERTIONS += 1
    if compact(literal) not in compact(text):
        raise AssertionError(f"Source contract failed: {label}\nMissing compact literal: {compact(literal)}")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("source_root", type=Path)
    args = parser.parse_args()
    root = args.source_root.resolve()
    if not root.is_dir():
        raise SystemExit(f"Not a source directory: {root}")

    worldgen = find_one(root, "Terraria/WorldGen.cs").read_text(encoding="utf-8", errors="replace")
    main_cs = find_one(root, "Terraria/Main.cs").read_text(encoding="utf-8", errors="replace")
    genvars = find_one(root, "Terraria.WorldBuilding/GenVars.cs").read_text(encoding="utf-8", errors="replace")
    chest = find_one(root, "Terraria/Chest.cs").read_text(encoding="utf-8", errors="replace")
    cave_house = find_one(root, "Terraria.GameContent.Biomes.CaveHouse/HouseBuilder.cs").read_text(encoding="utf-8", errors="replace")
    dual = find_one(root, "Terraria.GameContent.Generation.Dungeon.LayoutProviders/DualDungeonLayoutProvider.cs").read_text(encoding="utf-8", errors="replace")
    early = find_one(root, "Terraria.GameContent.Generation.Dungeon.Features/DungeonGlobalEarlyDualDungeonFeatures.cs").read_text(encoding="utf-8", errors="replace")
    shelves = find_one(root, "Terraria.GameContent.Generation.Dungeon.Features/DungeonGlobalBookshelves.cs").read_text(encoding="utf-8", errors="replace")
    furniture = find_one(root, "Terraria.GameContent.Generation.Dungeon.Features/DungeonGlobalGroundFurniture.cs").read_text(encoding="utf-8", errors="replace")
    traps = find_one(root, "Terraria.GameContent.Generation.Dungeon.Features/DungeonGlobalTraps.cs").read_text(encoding="utf-8", errors="replace")
    map_renderer = find_one(root, "Terraria/MapRenderer.cs").read_text(encoding="utf-8", errors="replace")
    world_file_data = find_one(root, "Terraria.IO/WorldFileData.cs").read_text(encoding="utf-8", errors="replace")

    # Canonical vanilla size facts.
    for name, value in (
        ("WorldSizeSmallX", 4200), ("WorldSizeSmallY", 1200),
        ("WorldSizeMediumX", 6400), ("WorldSizeMediumY", 1800),
        ("WorldSizeLargeX", 8400), ("WorldSizeLargeY", 2400),
    ):
        require(worldgen, rf"public\s+const\s+int\s+{name}\s*=\s*{value}\s*;", f"{name}={value}")

    require_literal(worldgen, """
        public static int GetWorldSize()
        {
            if (Main.maxTilesX <= 4200) { return 0; }
            if (Main.maxTilesX <= 6400) { return 1; }
            return 2;
        }
    """, "GetWorldSize remains the audited 3-tier width classifier")

    require_literal(worldgen, """
        Main.bottomWorld = Main.maxTilesY * 16;
        Main.rightWorld = Main.maxTilesX * 16;
        Main.maxSectionsX = Main.maxTilesX / 200;
        Main.maxSectionsY = Main.maxTilesY / 150;
    """, "setWorldSize derived physical state")

    # Reset tier sequences.
    require_literal(worldgen, """
        GenVars.skyLakes = 1;
        if (Main.maxTilesX > 8000) { GenVars.skyLakes++; }
        if (Main.maxTilesX > 6000) { GenVars.skyLakes++; }
    """, "sky-lake 1/2/3 source progression")
    require_literal(worldgen, """
        int num13 = 0;
        if (Main.maxTilesX >= 8400) { num13 = 2; }
        else if (Main.maxTilesX >= 6400) { num13 = 1; }
        GenVars.extraBastStatueCountMax = 2 + num13;
    """, "statue 2/3/4 source progression")

    # WorldGen discrete tables and downstream modifiers.
    require_literal(worldgen, """
        num4 = num switch { 1 => 4, 2 => 6, _ => 2, };
    """, "Glow Tulip 2/4/6")
    require_literal(worldgen, """
        int worldSize = GetWorldSize();
        int num = 100;
        int num2 = 8;
        num2 = worldSize switch { 1 => 9, 2 => 12, _ => 6, };
    """, "Chillet Egg 6/9/12")
    require_literal(worldgen, """
        switch (GetWorldSize()) { case 0: num2 = 3; break; case 1: num2 = 5; break; case 2: num2 = 7; break; }
        num2 += genRand.Next(2);
    """, "Spike Cave 3/5/7 then vanilla Next(2)")
    require_literal(worldgen, """
        int num20 = GetWorldSize() switch { 1 => 4, 2 => 6, _ => 2, };
        if (noTrapsWorldGen) { num20 *= 2; SetBoulderSolidity(solid: true); }
    """, "Boulder Pet 2/4/6 then No Traps x2")
    require_literal(worldgen, """
        num17 = GetWorldSize() switch { 1 => 6, 2 => 9, _ => 3, };
        if (tenthAnniversaryWorldGen) { num17 *= 5; }
    """, "Dirtiest Block 3/6/9 then Celebration x5")

    # 1.4.5 Dual Dungeon tables.
    require_literal(shelves, """
        num4 = WorldGen.GetWorldSize() switch { 1 => 10, 2 => 15, _ => 5, };
    """, "Dual Dungeon bookshelf 5/10/15")
    ground = compact("minimumWaterCandles = WorldGen.GetWorldSize() switch { 1 => 10, 2 => 15, _ => 5, };")
    furniture_compact = compact(furniture)
    global ASSERTIONS
    ASSERTIONS += 1
    if furniture_compact.count(ground) != 2:
        raise AssertionError("Expected exactly two GroundFurniture 5/10/15 water-candle tables")

    require_literal(early, """
        case 2:
            num = 40; num2 = 18; num3 = 16; num4 = 14;
            num5 = 8; num6 = 12; num7 = 80; num8 = 80; break;
    """, "Early Dual Dungeon Large assignment block")
    require_literal(early, """
        num16 = WorldGen.GetWorldSize() switch { 1 => 4, 2 => 6, _ => 2, };
    """, "Early Dual Dungeon flooded-pit 2/4/6")
    require_literal(dual, """
        case 2:
            num = 6; num2 = 10; num3 = 10; num4 = 8; num5 = 11; num6 = 14; break;
    """, "Dual Dungeon specialized-room Large block")
    require_literal(dual, """
        switch (WorldGen.GetWorldSize()) { case 0: num = 3; break; case 1: num = 4; break; case 2: num = 5; break; }
    """, "Dual Dungeon specialized halls 3/4/5")
    require_literal(traps, """
        case 0: num8 = 30 + genRand.Next(11); break;
        case 1: num8 = 50 + genRand.Next(16); break;
        case 2: num8 = 70 + genRand.Next(21); break;
    """, "Dual Dungeon trap 30/50/70 + Next(11/16/21)")

    # Fixed worldgen bookkeeping capacities and the exact formulas that can reach them.
    for field, element, length in (
        ("skyLake", "bool", 300),
        ("floatingIslandHouseX", "int", 300),
        ("floatingIslandHouseY", "int", 300),
        ("floatingIslandStyle", "int", 300),
        ("mCaveX", "int", 30),
        ("mCaveY", "int", 30),
        ("larvaX", "int", 100),
        ("larvaY", "int", 100),
        ("JChestX", "int", 100),
        ("JChestY", "int", 100),
    ):
        require(genvars, rf"public\s+static\s+{element}\[\]\s+{field}\s*=\s*new\s+{element}\[{length}\]\s*;", f"GenVars.{field} fixed capacity {length}")

    require(worldgen, r"Point\[\]\s+heartPos\s*=\s*new\s+Point\[100\]\s*;", "WorldGen.heartPos fixed capacity 100")
    require(genvars, r"public\s+static\s+readonly\s+int\s+maxTunnels\s*=\s*50\s*;", "GenVars.maxTunnels=50")
    require(genvars, r"tunnelX\s*=\s*new\s+int\[maxTunnels\]\s*;", "tunnelX follows maxTunnels")
    require(genvars, r"public\s+static\s+readonly\s+int\s+maxOrePatch\s*=\s*50\s*;", "GenVars.maxOrePatch=50")
    require(genvars, r"orePatchX\s*=\s*new\s+int\[maxOrePatch\]\s*;", "orePatchX follows maxOrePatch")
    require(genvars, r"maxMushroomBiomes\s*=\s*50\s*;", "GenVars.maxMushroomBiomes=50")
    require(genvars, r"maxLakes\s*=\s*50\s*;", "GenVars.maxLakes=50")
    require(genvars, r"maxOasis\s*=\s*20\s*;", "GenVars.maxOasis=20")

    require_literal(worldgen, """
        int num = (int)((double)Main.maxTilesX * 0.0015);
        if (remixWorldGen) { num = (int)((double)num * 1.5); }
    """, "surface tunnel width formula")
    require_literal(worldgen, """
        if (GenVars.numTunnels >= GenVars.maxTunnels - 1) { break; }
    """, "surface tunnel tracking sentinel")
    require_literal(worldgen, """
        int num = (int)((double)Main.maxTilesX * 0.001);
        if (remixWorldGen) { num = (int)((double)num * 1.5); }
    """, "Mountain Cave width formula")
    require_literal(worldgen, """
        GenVars.mCaveX[GenVars.numMCaves] = num3;
        GenVars.mCaveY[GenVars.numMCaves] = k;
        GenVars.numMCaves++;
    """, "Mountain Cave write has no retail capacity guard")
    require_literal(worldgen, """
        int num = genRand.Next(Main.maxTilesX * 5 / 4200, Main.maxTilesX * 10 / 4200);
    """, "surface ore width formula")
    require_literal(worldgen, """
        if (GenVars.numOrePatch < GenVars.maxOrePatch - 1)
        {
            GenVars.orePatchX[GenVars.numOrePatch] = num3;
            GenVars.numOrePatch++;
        }
    """, "surface ore tracking sentinel")
    require_literal(worldgen, """
        int num = (int)((double)Main.maxTilesX * 0.0008);
    """, "floating-island width formula")
    require_literal(worldgen, """
        if (SecretSeed.errorWorld.Enabled && SecretSeed.Variations.errorWorldAdjustment(1.0) < 3) { num *= 3; }
    """, "Error World floating-island x3")
    require_literal(worldgen, """
        num *= 10;
        GenVars.skyLakes *= 10;
    """, "Care Bears floating-island and sky-lake x10")
    require_literal(worldgen, """
        double num10 = (double)Main.maxTilesX * 0.00045;
        if (remixWorldGen) { num10 *= 2.0; }
    """, "evil-region width formula and Remix x2")

    # The audited safe fixed stores still exist at their retail capacities.
    require_literal(worldgen, """
        if (num > (double)GenVars.maxMushroomBiomes) { num = GenVars.maxMushroomBiomes; }
    """, "mushroom retail capacity clamp")
    require(worldgen, r"GenVars\.numLakes\s*>=\s*GenVars\.maxLakes\s*-\s*1", "lake retail sentinel")
    require(worldgen, r"GenVars\.numOasis\s*<\s*GenVars\.maxOasis", "oasis retail capacity guard")

    # Global chest contract stays retail unless runtime stress proves it reachable.
    require(main_cs, r"public\s+const\s+int\s+maxChests\s*=\s*8000\s*;", "Main.maxChests=8000")
    require(main_cs, r"Chest\[\]\s+chest\s*=\s*new\s+Chest\[8000\]\s*;", "Main.chest fixed capacity 8000")
    require(chest, r"for\s*\(\s*int\s+i\s*=\s*0\s*;\s*i\s*<\s*8000\s*;\s*i\+\+\s*\)", "Chest.FindEmptyChest scans 8000 slots")
    require_literal(cave_house, """
        if (flag) { break; }
    """, "CaveHouse chest placement stops after first successful room chest")

    # Storage and map-renderer formulas that support, rather than reinterpret, worldgen.
    require(main_cs, r"new\s+Tile\s*\[\s*maxTilesX\s*,\s*maxTilesY\s*\]", "Main.tile exact physical allocation")
    require(map_renderer, r"numTargetsX\s*=\s*5\s*;", "MapRenderer vanilla X target count")
    require(map_renderer, r"numTargetsY\s*=\s*2\s*;", "MapRenderer vanilla Y target count")
    require(map_renderer, r"if\s*\(\s*i\s*==\s*numTargetsX\s*-\s*1\s*\)\s*\{\s*width\s*=\s*400\s*;", "MapRenderer special final 400-wide target", flags=re.S)
    require(map_renderer, r"if\s*\(\s*j\s*==\s*numTargetsY\s*-\s*1\s*\)\s*\{\s*height\s*=\s*600\s*;", "MapRenderer special final 600-high target", flags=re.S)
    require(map_renderer, r"for\s*\(\s*int\s+i\s*=\s*0\s*;\s*i\s*<=\s*4\s*;\s*i\+\+\s*\)", "MapRenderer hard-coded X draw ceiling")
    require_literal(map_renderer, """
        int num = Main.maxTilesX / textureMaxWidth;
        int num2 = Main.maxTilesY / textureMaxHeight;
        for (int i = 0; i <= num; i++)
        {
            for (int j = 0; j <= num2; j++)
    """, "MapRenderer floor-plus-one target initialization loops")
    require(world_file_data, r"WorldSizeX\s*==\s*8400\s*&&\s*WorldSizeY\s*==\s*2400", "WorldFileData exact Large seed/name recognition")

    print(f"Terraria 1.4.5.8 source continuity contracts passed ({ASSERTIONS} assertions).")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
