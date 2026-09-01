# WorldCapture

Client-only gloader mod for persistently collecting the multiplayer world sections that vanilla Terraria sends to the client.

## What it does

- Watches `Main.sectionManager` for 200 x 150 tile sections that the multiplayer client has actually received.
- Captures each newly seen section automatically.
- Remembers captured sections between Terraria sessions and continues filling the same world cache on later visits.
- Refreshes every loaded section once per session, even when it was already captured on an earlier visit.
- Listens for received tile changes and queues the affected loaded section(s) for a fresh snapshot.
- Never requests extra sections from the server and does not alter normal networking.
- Uses the world UUID as the persistent identity when vanilla supplied one, with world ID/dimensions as a fallback.

## Overlay

Press **F8** while connected to a multiplayer world.

The bottom-right overlay shows:

```text
World capture: 38.69%
Sections: 260 / 672
This session: +14
```

Press F8 again to hide it. Hiding the overlay does not stop capture.

## Files

Caches live under the normal Terraria save directory:

```text
<Terraria SavePath>/gloader/WorldCapture/<world-key>/
    manifest.txt
    sections/
        x00_y00.bin
        x01_y00.bin
        ...
```

For the normal Large world dimensions (8400 x 2400), the physical section grid is 42 x 16 = 672 sections.

`manifest.txt` is human-readable and records the world name, UUID/ID, dimensions, generator version, game mode, coverage, section dimensions, Terraria version, and last-seen time.

Each `.bin` is **not a custom tile schema** and is **not a `.wld` file**. It is a snapshot produced by Terraria's own:

```text
NetMessage.CompressTileBlock(x, y, width, height, stream)
```

That keeps the cache close to Terraria's native multiplayer tile representation and makes later reconstruction tooling much less fragile than serializing selected `Tile` fields ourselves.

Writes use a temporary file and replacement/overwrite step so a crash during a refresh is much less likely to destroy an existing good section.

## Coverage meaning

Coverage is based on physical world sections captured at least once:

```text
captured sections / total physical sections * 100
```

For all normal Terraria world sizes, the dimensions divide evenly into 200 x 150 sections, so section percentage also corresponds directly to tile-area percentage.

A section already present from an earlier session is refreshed when vanilla loads it again but does not increment the coverage percentage a second time.

## What 100% means

100% means the cache has a tile-block snapshot for every physical section of the world.

It does **not** mean the cache is byte-for-byte equivalent to the server's original `.wld`. The multiplayer client is not given the original seed, and some world-file data is synchronized through systems other than tile-section traffic (or may never be disclosed to a client at all).

The section cache is intentionally suitable as the geographic input to a future reconstructed-world exporter, but **WorldCapture v1 does not write `.wld` files**. A proper exporter should combine these tile blocks with the separately observed global/world state rather than pretending that 100% section coverage alone is the complete server save.

## Safety / scope

- Multiplayer client only (`NetmodeID.MultiplayerClient`).
- Does not touch local `.wld` files.
- Does not modify tiles.
- Does not send additional tile-section requests.
- Does not know or recover a remote server's seed.
- Capture errors disable the collector for the current process and are printed to the gloader console instead of risking silent bad data.
