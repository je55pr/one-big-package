# R&C1 retail UI archaeology and Godot adoption plan

Status: investigated, 2026-10-22. **No retail UI renderer, sprite extraction or side-by-side timing verification is claimed complete.**
Authority: OBP's supported NTSC-U original retail R&C1 disc (`SCUS-97199`), with public decompilation consulted only as implementation-independent evidence leads.
Scope: in-game HUD, Quick Select, pause/inventory menus, help windows, and map. This document is intentionally separate from OBP's current designed title/source-manager presentation.

## Executive finding

OBP already has the right gameplay/presentation seam (`HudSnapshot` / `Rac1HudProjection`) and a retail-backed eight-slot quick-select array. Its Godot UI, however, is an OBP-designed set of text-based cards, grids and scroll panels rather than the original R&C1 sprite/animation-driven UI. **Recover the HUD asset bank first, then build reusable native-styled Godot components around the already authoritative gameplay state.** Do not put gameplay decisions into widgets.

## Evidence classes

- **Direct retail data (confirmed here):** Six global disc resource ranges were decoded from the user's authorized NTSC-U ISO using the retail index at LBA 1500. All ranges are in-bounds. Chunk 0 contains a coherent native animation/frame/page-index header including 56 non-sentinel animation entries.
- **Public decompilation lead (source, not yet independently instruction-matched by this investigation):** `lombyte-project/Lombyte`, inspected head `1e40bcb`; paths listed below. Original code structure and native addresses are useful hypotheses pending direct binary signature validation.
- **Visual comparison (reference only):** Retail gameplay/menu screenshots and documented PS2 controls corroborate a compact vertical pause menu, non-pausing eight-direction Quick Select, illustrated icons and frame-based highlights. Exact on-screen coordinates, colours, and timing still need runtime screenshot comparison.
- **OBP source baseline (confirmed):** `game/scripts/PlayerHud.cs`, `WeaponQuickSelectUi.cs`, `PauseMenuUi.cs`, `UiTheme.cs`, `Rac1PlanetTravelUi.cs`, `OBPGame.QuickSelect.cs`, and `docs/HUD_FOUNDATION.md`.

## Direct disc witness: native HUD resource archive

The shared index at LBA 1500 (2048 bytes per sector, 0x2960 index bytes) contains six `(u32 sector, u32 sectorCount)` entries at index-relative byte offset `0x14f8`. These are identified as `wad_chunks[6]` by the public decompilation's `include/rnc/storage/disc_table.h` and consumed by `src/gameplay/state/transition_do_transition.c`.

| Chunk | Sector (LBA) | Sector count | On-disc bytes | First bytes / interpretation |
|---:|---:|---:|---:|---|
| 0 | 18053 | 3 | 6,144 | native HUD bank header / tables |
| 1 | 18056 | 31 | 63,488 | begins `WAD`, compressed |
| 2 | 18087 | 79 | 161,792 | begins `WAD`, compressed |
| 3 | 18166 | 1 | 2,048 | begins `WAD`, compressed |
| 4 | 18167 | 1 | 2,048 | begins `WAD`, compressed |
| 5 | 18168 | 11 | 22,528 | begins `WAD`, compressed |

This is **not** the previously documented per-level terrain texture table. It is a separate shared HUD/UI asset route, recovered here using a bounded 48-byte index read, six 24-byte magic checks and a bounded read of the 6,144-byte chunk-0 header. No original resource bytes are retained in the repository.

Chunk 0 directly decodes with offsets `animationDefs=0xB4`, `frameRefs=0x27C`, `palettePages=0x794`, `imagePages=0x8E4`; `56` animation records precede the `id=0xffff` sentinel at offset `0x274`; room between frame refs and palette pages for `326` 4-byte frame refs. The first entries include `id=0x7544` (4 frames), `id=0x7533` (2), `id=0xE934` (2). The `id=0x7580` frame primitive has **two frames** beginning at frame-ref 305, consistent with a stretchable UI frame. These are table observations, not proof that any decoded art looks right until the palette+pixel data path is tested.

The public `src/ui/hud/load_hud_banks.c`, `get_icon_frame.c`, `hud_get_icon_index.c`, `link_hud_bank.c`, `hud_send_texture.c`, `load_compressed_hud_bank.c` indicate the sequence: read six chunks; decompress texture banks; resolve `HudAnimDef` (8 bytes each) → `HudFrameRef` (4 bytes each) → palette/image pages; stage them into GS textures. `src/ui/frames/draw_stretchable_ui_frame.c` renders `0x7580` frame 1 at left, frame 0 stretched as centre, and a mirrored frame 1 at right.

**Asset next step:** implement a *read-only*, bounded `Rac1HudBank` extractor against user-owned ISO. Output local, ignored cached PNG frames plus a semantic, asset-ID-indexed manifest; validate compression, palette decoding (including PS2 CLUT ordering/alpha) and every referenced offset. Do not check in originals or generated frames.

## UI behaviour recoveries (decompilation leads)

### Quick Select

- Eight slots, 45° apart: `theta(i) = i*pi/4 - pi/2`, indices 0..7 from twelve o'clock clockwise; centres laid out around the viewport centre at `radius = min(width,height)/2 - 40` **in the wheel's source viewport**. `src/ui/menus/pause/draw_pause_ring.c` (`FUN_00220b20`).
- A selected slot draws a time-pulsing highlight based on `game_frame_counter & 0x3f`, with icon art approximately 32×32 source-view units. Empty entries use a gray blank slot. Colored, per-item frames are resolved from `get_icon_frame(id, variant)`, rather than text-only buttons.
- Quick Select in R&C1 **does not stop the gameplay simulation**. Holding Triangle reveals the wheel, directional input chooses an item, releasing Triangle equips; consecutive Triangle taps can recall the previous weapon. Input details/tap thresholds must be validated against retail, rather than adopting OBP's existing 350ms threshold as native.
- Native save block 13 at runtime `0x00141ea0` is exactly eight 32-bit quick-select entries; the retail first-acquisition routine fills the first zero slot. See `research/RAC1_WEAPON_INVENTORY.md`. No native multi-page wheel is established for R&C1.
- The Quick Select wheel should **not** be confused with the *separate inventory item grid*, which uses selectable per-item icons and variant frames (see `src/ui/menus/draw_menu_item_grid.c`).

### Pause, equipment and configuration menus

- Original pause screen uses compact menu choices (Weapons, Gadgets, Quick Select, Items, Help, Options, as seen in screenshots), not an all-in-one Godot scroll panel.
- Native menu screens have explicit current, next, back and pending-focus state, per-page enter/leave, up to 14 component screens and animation triggers; `src/ui/menus/update_menu_system.c` sets a **12-game-tick** page-switch timer. This is an evidence-backed transition *lead*; PAL timing and actual perceived animation must be validated before a timing-fidelity claim.
- `src/ui/menus/update_menu_grid_selection.c` contains directional focus movement, cross selection, unavailable-item rules and navigation across adjoining grids. `src/ui/menus/handle_menu_screen_buttons.c` shows page-focused back/close behaviour. Preserve per-screen semantics; avoid mapping every action to one global Godot accept/cancel default.
- Menu inventory grids draw icons whose frame changes with equipped/disabled/variant state, with a pulsing selection rectangle. See `src/ui/menus/draw_menu_item_grid.c`.
- Some menus render animated/rotating object previews, not only sprites; consult `src/ui/menus/create_menu_preview_moby.c` and `update_menu_preview_animation_transform.c` as leads.
- Frame primitives are illustrated and stretchable; `src/ui/menus/draw_menu_flashing_panel.c` adds pulsing/flashing visual treatment to some panels.

### Gameplay HUD and help system

- HUD is not a set of permanent rectangular status cards. It uses sprite assets, animation channels and time-dependent visibility. `src/ui/hud/init_hud.c` initializes **13** channel slots; `src/ui/hud/update_hud.c` walks all 13 and invokes each active per-slot update callback.
- `src/ui/hud/update_hud.c` also eases a transient framed message's alpha using `scale_game_frames(8)` and a lifetime counter; do not reinterpret this as one global rule for nanotech, Bolt pickup or ammo animations.
- Native HelpDesk prompts are their own state machine, including an opening frame, prompt symbol, expansion into a framed text window, text alpha fade and closure, plus queued help message state. See `src/ui/help/draw_help.c`, `update_help_state.c`, `help_draw_prompt.c`. This differs from a static generic interaction card.
- Keep `Rac1HudProjection` / `HudSnapshot` responsible only for observational data and feedback events. The artistically faithful sprite player belongs in the Godot presentation layer.

### Planet maps and transitions

- Retail planet/map menu screens load bitmap/map masks and render projected map markers, with native selection state; see `src/ui/map/draw_map_screen.c`, `draw_map_markers.c`.
- `src/ui/map/update_map_zoom_and_pan.c` applies analogue pan and zoom with numeric zoom clamps **0.65..4.0**, and bounds pan per selected map. The exact physical stick axis and viewport calibration must be checked in retail.
- Current `Rac1PlanetTravelUi.cs` uses a list of text destination buttons, not the game's map/image/marker presentation. Replacing it is high-value **after** the shared icon pipeline and Quick Select.
- Title, source selection, controller calibration and developer test tooling are OBP-designed capabilities; these should be visually coordinated with a R&C-inspired theme but not mislabelled as recovered retail screens.

## OBP gap inventory

| Godot component | Current gap | Target approach |
|---|---|---|
| `PlayerHud.cs` | text cards without original icon art/presence rules | render icon atlas + presentation-side animations from existing snapshot |
| `WeaponQuickSelectUi.cs` | 3×3 text buttons, faux radial layout | eight true polar icon positions, native selection highlight, no fabricated paging |
| `OBPGame.QuickSelect.cs` | sets `GetTree().Paused=true`, relies on host-created extra pages | preserve R&C1 simulation during wheel, separate developer extras |
| `PauseMenuUi.cs` | tests/settings mixed with shipping menu | R&C1-oriented page structure, hide dev-only controls behind separate developer UI |
| `UiTheme.cs` | flat generic rounded OBP panels/fonts | a separate `Rac1UiSkin`, recovered frames, fonts and palette as available |
| `Rac1PlanetTravelUi.cs` | destinations as text buttons | map background/mask, overlay markers and native-style navigation |
| `TitleScreen.cs` | original OBP front door | retain OBP identity; coordinate style, don't confuse with retail R&C1 boot |

## Recommended order of work

1. **Asset archaeology (unlocker).** Build the six-chunk reader, validate 56 animation records / 326 frame-ref capacity, decompress referenced banks, produce PNG+JSON in a gitignored user cache. Cover title/icon/frame samples with pixel or hash tests against source bytes, no copyrighted assets in Git.
2. **Shared skin primitives.** Godot atlas adapter for icon ID/frame, sprite flip, horizontal stretch/frame-tiling, simple pulse highlight, font and scalable base-coordinate system. Allow designed fallbacks where asset/icon IDs remain unknown.
3. **Quick Select functional parity.** Eight true polar slots, slot metadata from native `Rac1WeaponInventory`, realtime non-pausing game, animated icon/frame selection, control-held/release semantics, empty-slot handling. Validate via deterministic input test and same-state screenshots.
4. **Retail-style pause and inventory.** Build explicit page navigation and illustrated item grid, with developer-only tooling removed from normal player view, retain controller settings as dedicated OBP page. Add menu sound and 12-tick transition where validated.
5. **In-game HUD, contextual help, map and travel.** Replace cards incrementally while retaining `HudSnapshot` authority; implement visibility/event animations, contextual help state flow, map art and markers.

## Validation matrix (required before 'retail faithful')

- PCSX2 R&C1 opening Veldin Quick Select (one filled slot), populated-save Quick Select (4), and pause-inventory-grid comparisons, including icon sizing, ordering, position and animation.
- Hold Triangle while hostile actors continue moving; check gameplay does not pause, and release/retap restores correct item. Verify uncertain double-tap timing.
- Display health, ammo and Bolts, then damage, pickup, equip, use one ammo, unload menu, and respawn. Verify each event shows only authoritative data.
- Pause, select each page, back, switch gamepad vs keyboard, and compare animation/selection sounds.
- Test Godot viewport/aspect ratios separately from PS2-native 4:3 layout; do not hardcode full desktop width as retail wheel radius.
- Keep all retail original files, decoded image assets and proprietary captures in ignored local caches. Commit only code, manifest schema and payload-free derived evidence.

## Research pointers / licensing

- `lombyte-project/Lombyte@1e40bcb` (public decompilation; inspected as evidence): `include/rnc/storage/disc_table.h`, `src/gameplay/state/transition_do_transition.c`, `src/ui/hud/load_hud_banks.c`, `get_icon_frame.c`, `include/rnc/ui/hud/hud_state.h`, `src/ui/menus/pause/draw_pause_ring.c`, `src/ui/menus/draw_menu_item_grid.c`, `src/ui/menus/update_menu_system.c`, `src/ui/help/draw_help.c`, `src/ui/map/update_map_zoom_and_pan.c`. Repository: https://github.com/lombyte-project/Lombyte
- OBP-specific retail authority and separation contract: `research/RAC1_DISC_INDEX.md`, `research/RAC1_WEAPON_INVENTORY.md`, `research/EXTERNAL_SOURCE_POLICY.md`, `docs/HUD_FOUNDATION.md`, `AGENTS.md`.
- **No public decompilation code was copied or translated into OBP production code.** All described algorithms/formats need a fresh OBP implementation validated against local authorized retail input.
