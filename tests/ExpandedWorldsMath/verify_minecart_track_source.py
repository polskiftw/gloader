#!/usr/bin/env python3
"""Fail-closed retail-source contracts for Expanded Worlds minecart track capacity."""
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
        raise AssertionError(f"Minecart source contract failed: {label}\nPattern: {pattern}")


def require_literal(text: str, literal: str, label: str) -> None:
    global ASSERTIONS
    ASSERTIONS += 1
    if compact(literal) not in compact(text):
        raise AssertionError(
            f"Minecart source contract failed: {label}\nMissing compact literal: {compact(literal)}"
        )


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("source_root", type=Path)
    args = parser.parse_args()
    root = args.source_root.resolve()
    if not root.is_dir():
        raise SystemExit(f"Not a source directory: {root}")

    track_generator = find_one(
        root, "Terraria.GameContent.Generation/TrackGenerator.cs"
    ).read_text(encoding="utf-8", errors="replace")
    worldgen_range = find_one(
        root, "Terraria.WorldBuilding/WorldGenRange.cs"
    ).read_text(encoding="utf-8", errors="replace")
    worldgen = find_one(root, "Terraria/WorldGen.cs").read_text(
        encoding="utf-8", errors="replace"
    )
    configuration = find_one(
        root, "Terraria.GameContent.WorldBuilding.Configuration.json"
    ).read_text(encoding="utf-8", errors="replace")

    require(
        track_generator,
        r"private\s+readonly\s+TrackHistory\[\]\s+_history\s*=\s*new\s+TrackHistory\[4096\]\s*;",
        "TrackGenerator._history remains the audited readonly 4096-entry scratch array",
    )
    require(
        track_generator,
        r"while\s*\(\s*_length\s*<\s*_history\.Length\s*-\s*100\s*\)",
        "TrackGenerator keeps the retail 100-entry tail reserve",
    )
    require(
        track_generator,
        r"private\s+readonly\s+TrackHistory\[\]\s+_rewriteHistory\s*=\s*new\s+TrackHistory\[25\]\s*;",
        "TrackGenerator rewrite history remains independent of path history",
    )

    require_literal(
        configuration,
        """
        "LongTrackLength": {
            "Min": 400,
            "Max": 1000,
            "ScaleWith": "WorldWidth"
        }
        """,
        "LongTrackLength remains 400..1000 scaled by WorldWidth",
    )

    require_literal(
        worldgen_range,
        """
        case ScalingMode.WorldWidth:
            num = (double)Main.maxTilesX / 4200.0;
            break;
        """,
        "WorldWidth scale remains maxTilesX / 4200.0",
    )
    require_literal(
        worldgen_range,
        """
        return (int)(num * (double)value);
        """,
        "scaled range values still truncate the positive double product to int",
    )

    require_literal(
        worldgen,
        """
        WorldGenRange worldGenRange = passConfig.Get<WorldGenRange>("LongTrackLength");
        """,
        "WorldGen still reads LongTrackLength from the active pass configuration",
    )
    require_literal(
        worldgen,
        """
        trackGenerator.Place(origin7, worldGenRange.ScaledMinimum, worldGenRange.ScaledMaximum)
        """,
        "WorldGen still sends scaled LongTrackLength bounds directly to TrackGenerator.Place",
    )

    print(f"Terraria 1.4.5.8 minecart source contracts passed ({ASSERTIONS} assertions).")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
