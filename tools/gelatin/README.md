# Gelatin

Gelatin is a standalone Windows 11 x64 editor for preparing images as deformable `.gel` assets. It does not need Terraria or GLoader, and it never launches or modifies either program.

The compact workflow is:

```text
prepare image -> place jello cores / paint rigidity -> abuse it in the Lab -> save .gel
```

## Run the packaged app

1. Download the `gelatin-<version>-win-x64` Actions artifact.
2. Extract `gelatin-<version>-win-x64.zip` to any normal folder.
3. Run `Gelatin.exe`.

The package is self-contained. A separate .NET installation is not required.

## Workspaces

### Asset

- Open or drag/drop PNG, JPEG, WebP, animated GIF, and `.gel` files. Animated GIF imports preserve per-frame delays and repetition semantics, decode into full RGBA frames, and are stored as a single PNG atlas plus timing metadata in GEL1 schema v2.
- Draw and apply the existing rectangular crop. Existing normalized cores and rigidity strokes are remapped; elements wholly outside the crop are removed.
- Use **Polygon cutout** for irregular shapes. Click source-pixel vertices, close with Enter/double-click/the first vertex, then drag vertices or insert one by clicking an edge. Delete removes a selected vertex while at least three remain; arrow keys nudge one source pixel and Shift+arrow nudges ten.
- Applying a polygon cutout makes everything outside the polygon transparent with an antialiased boundary, then automatically trims margins where alpha is exactly zero. The cutout and trim are one undoable edit.
- Resize with deterministic RGBA sampling and an optional aspect-ratio lock. Re-enabling the lock captures the current width/height ratio.
- Trim transparent edges using the editable alpha threshold.
- Pick a background color with the eyedropper, preview tolerance and feather changes live, then apply or cancel.
- **Erase alpha** paints alpha to zero with a hard circular source-pixel brush while retaining hidden RGB.
- **Restore alpha** copies exact RGBA pixels from a synchronized session-only recovery source. Brush drags are interpolated and each drag commits as one undo step.
- Export the current processed PNG or pretty-printed JSON for inspection.

Animated assets play automatically in the Asset and Gel workspaces with their preserved timing. In Asset, animation transport controls let you pause, step backward/forward, or jump directly to a frame. **Apply edits to** switches between the compatibility default **All frames** and **Current frame** for crop masking, polygon cutout, background removal, and alpha erase/restore. Selecting **Current frame** automatically pauses playback so the frame cannot change underneath a precision edit. Current-frame crop/cutout keeps the shared canvas dimensions and turns pixels outside the selected region transparent only on that frame. Resize and transparent trim remain animation-wide; trim uses the union of visible pixels across all frames so the asset never shifts between frames.

The canvas has a transparency checkerboard. Use the mouse wheel to zoom and middle/right drag to pan. Polygon coordinates and alpha brush size remain defined in source pixels at every zoom level.

#### Recovery-source behavior

Restore is intentionally an editor-session facility, not a new `.gel` format feature:

- Opening a normal image uses its normalized PNG as the recovery baseline.
- Opening a `.gel` uses the embedded processed PNG as the recovery baseline.
- Crop, resize, transparent trim, and the automatic post-cutout trim transform the recovery source in lockstep with the visible image.
- Background removal, polygon masking before trim, Erase, and Restore do **not** overwrite the recovery source.
- Undo/redo snapshots the visible image and recovery source together.
- Saving persists only the ordinary GEL1 JSON + processed PNG. Reopening that file starts a new recovery baseline from the saved processed PNG.

### Gel

- Create axis-aligned elliptical jello cores by dragging.
- Select, move, resize, duplicate, delete, name, and numerically edit cores.
- Edit per-core mass, coupling, damping, local softness, and influence falloff.
- Paint or erase gradient rigidity strokes with adjustable radius and strength. Full-strength erasing clips/splits stroke geometry, so erased gaps are not silently reconnected.
- Toggle core, combined influence heatmap, and rigidity overlays.
- Tune Softness, Damping, Area preservation, Shape memory, Bend resistance, Max stretch, and optional self-collision.
- Author the small runtime surface stored in GEL1: Bounce color (`Off` or `Random Neon`), tint intensity, visible-pixel opacity, and movement speed in pixels per second.

Core coordinates, radii, and rigidity stroke points are normalized, so resizing the image does not invalidate the programming.

### Lab

The Lab runs the same UI-independent XPBD solver used to interpret the saved material and core configuration. The PNG is texture-mapped over the live triangulated mesh; it is not a scaled rectangle animation. Animated assets select the correctly timed atlas frame while that same texture is deformed by the mesh, so animation and gel physics run together.

- Drag a local point and release to throw the gel.
- Use directional **SMACK** controls for large impulses.
- Enable **Hammer** and click a side of the gel for a localized inward hit.
- Toggle gravity, pause, reset, and run the diagnostic solver at 0.1x, 0.25x, 0.5x, or 1x. This time-scale control is not the authored movement speed and does not alter GIF timing.
- Clean/game view applies the saved opacity and bounce tint to the currently displayed still/GIF frame at render time; source pixels and the stored atlas are never rewritten.
- Reset returns to the exact rest pose and clears transient motion/backlog.
- Inspect mesh, core, heatmap, rigidity, alpha contour/contact, and velocity diagnostics independently.
- Use Clean/game view to hide every diagnostic.

Quality presets:

| Preset | Mesh target | Physics | Iterations | Self collision |
|---|---:|---:|---:|---:|
| Sane | ~24x24 | 240 Hz | 8 | every 2 substeps |
| High | ~32x32 | 480 Hz | 12 | every substep |
| Overkill | ~48x48 | 720 Hz | 16 | every substep |
| Claire | ~64x64 | 960 Hz | 24 | every substep with denser contour |

Extreme aspect ratios keep roughly equivalent vertex counts while avoiding degenerate cells. Material behavior is calibrated to remain broadly consistent across the published presets; higher presets increase numerical and mesh fidelity rather than intentionally changing the saved material.

## Shortcuts

Global:

| Shortcut | Action |
|---|---|
| `Ctrl+O` | Open image or `.gel` |
| `Ctrl+S` | Save current `.gel` |
| `Ctrl+Shift+S` | Save As |
| `Ctrl+Z` | Undo |
| `Ctrl+Y` | Redo |

Polygon cutout while the canvas/editor owns keyboard input:

| Shortcut | Action |
|---|---|
| `Enter` | Close an open polygon if valid |
| `Backspace` | Remove the last open vertex; on a closed polygon delete the selected vertex if 3 remain |
| `Delete` | Delete the selected closed-polygon vertex if 3 remain |
| `Arrow keys` | Nudge the selected vertex by 1 source pixel |
| `Shift+Arrow` | Nudge the selected vertex by 10 source pixels |
| `Escape` | Cancel/clear the polygon selection |

Text and numeric inputs suppress polygon editing shortcuts while focused.

Lab (workspace-wide when a text or numeric field is not active):

| Shortcut | Action |
|---|---|
| `Space` | Pause/resume |
| `R` | Reset |
| `H` | Toggle Hammer |
| `M` | Toggle mesh |

Gelatin tracks dirty state and prompts before discarding unsaved work. Undo/redo covers image operations, material changes, core editing, rigidity editing, polygon cutout/trim, and alpha-repair brush strokes through a bounded snapshot history.

## GEL1 format

`.gel` is deliberately unencrypted, uncompressed at the container level, and free of external paths:

```text
Offset  Size  Meaning
0       4     ASCII GEL1
4       4     JSON byte length (uint32 little-endian)
8       4     PNG byte length (uint32 little-endian)
12      N     UTF-8 JSON without BOM
12+N    M     exact PNG bytes
```

Gelatin keeps the `GEL1` binary container unchanged. Static assets remain `schemaVersion: 1`; animated assets use `schemaVersion: 2`, where the embedded PNG is a texture atlas and JSON stores each logical frame rectangle, exact source delay in milliseconds, and repetition count (`-1` means infinite). Gelatin continues to read 0.1.0/0.1.1/0.1.2/0.1.3 static GEL1 files without migration. Gello therefore only needs PNG-atlas sampling and timing logic; it never needs a GIF decoder. The recovery source is never serialized. The loader rejects incorrect magic, unsafe or impossible lengths, truncation, trailing bytes, invalid UTF-8/JSON, unsupported schema versions, invalid PNG data, invalid animation metadata, atlas rectangles outside the PNG, and dimension mismatches. Saves are atomic. The complete JSON schema is in `gel.schema.json`.

Runtime properties are declarative JSON blocks in both static schema 1 and animated schema 2 assets. They are optional when reading older GEL1 files, but Gelatin writes them explicitly on the next save:

```json
"appearance": { "opacity": 1.0 },
"motion": { "speedPixelsPerSecond": 320.0 },
"physics": { "restitution": 0.82, "friction": 0.015 },
"bounceEffect": { "tint": "off", "tintIntensity": 1.0 }
```

The compatibility defaults exactly preserve the prior preview intent: tint is `off`, tint intensity is `1.0`, opacity is `1.0`, movement speed is `320 px/s` (the old `(0.34, 0.21)` launch vector at the Lab's roughly 800-pixel preview scale), restitution is `0.82`, and wall friction is `0.015`. Unknown tint strings are rejected rather than interpreted as commands. GEL1 does not contain scripts, effect graphs, expressions, or arbitrary executable behavior.

The canonical runtime rules are implemented in `GelRuntimeSemantics` and consumed by the Lab preview:

- Initial motion uses normalized direction `(0.34, 0.21)` and the authored pixel speed. Integration uses elapsed time through the existing fixed-step solver; render cadence does not define distance traveled.
- While the body is moving, its center translation is normalized back to the authored pixel speed after each fixed physics step. Deformation and relative vertex motion remain solver-controlled.
- A bounce event is emitted only when a wall contact has inward normal velocity and the solver actually performs a reflection. A continuing contact does not repeatedly retrigger the bounce color.
- Collision reflection uses the saved restitution and friction. The Lab's visible chamber inset is only a viewport representation; a future player should apply the same reflection rules at its actual play bounds.
- `bounceEffect.tint: "random_neon"` chooses from the fixed palette `#FF2DAA`, `#00F5FF`, `#A8FF00`, `#BF40FF`, `#FF5A00`, `#247BFF`, `#FFEB00`, `#FF00E5`. Immediate repeats are avoided. The exact random sequence is not part of file compatibility.
- Tint intensity is an 8-bit sRGB-channel lerp performed independently for R, G, and B: `out = round(source * (1 - intensity) + tint * intensity)`. Tint never changes source alpha.
- Opacity is applied after source-frame selection as `outAlpha = round(sourceAlpha * opacity)`. At `1` alpha is unchanged; at `0` all visible pixels render transparent. Source PNG/atlas bytes stay unchanged.
- GIF frame selection uses preserved frame delays and repetition metadata. Movement speed and the Lab diagnostic time-scale do not multiply animation time. Tint and opacity apply identically to whichever GIF frame is currently selected.

Lab-only state--gravity, visible chamber size, simulation quality/time-scale, pause state, transient velocity/deformation, current random neon choice, and editor pan/zoom--is not serialized.

## Source layout

```text
tools/gelatin/
  Gelatin.sln
  src/Gelatin.Core/   format, image operations, authoring math, contour and XPBD physics
  src/Gelatin.App/    Avalonia desktop UI and Skia textured rendering
  tests/Gelatin.Tests/
  scripts/publish.ps1
```

`Gelatin.Core` contains no Avalonia window/control types and has no Terraria, XNA, MonoGame, or GLoader dependency.

## Build and test

Requirements: .NET 10 SDK. From `tools/gelatin/`:

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = "1"
dotnet restore Gelatin.sln
dotnet build Gelatin.sln -c Release --no-restore
dotnet test Gelatin.sln -c Release --no-build
```

The authoritative repository workflow is `.github/workflows/gelatin.yml`; it runs the Windows build/test/package loop used for release verification.

## Publish

From the repository root on Windows:

```powershell
tools\gelatin\scripts\publish.ps1
```

Outputs:

```text
tools/gelatin/dist/gelatin/Gelatin.exe
tools/gelatin/dist/gelatin-<version>-win-x64.zip
```

The publish script prints the package SHA-256. The dedicated Gelatin workflow performs restore, Release build, tests, self-contained Windows x64 publish, package/hash verification, and artifact upload without changing the GLoader package.
