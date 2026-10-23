# R&C1 UI provenance and rendering fidelity audit (2026-10-23)

Purpose: separate source-validated R&C1 presentation data from OBP-only choices.
Do not describe any screen as "retail faithful" solely because it references
an original animation ID or bitmap font.

## What is source-backed

| Element | Retail evidence | Confidence |
|---|---|---|
| English menu labels (Weapons, Gadgets, Quick Select, Items, Help, Options) | Decoded native English localisation IDs 20194–20199 from original NTSC-U game disc | Direct data |
| Menu glyph appearance and individual advances | Three bitmap font atlas/metrics records in original executable + level core, read by Rac1UiAssets | Direct data |
| Original item icon animation frames | HUD bank anim/frame tables; native icon ID sequence EA69–EA79 and game inventory evidence | Direct sprites; item→ID mapping visually corroborated |
| Text highlight RGB | Source FUN_0021d4a8 (`src/ui/menus/draw_menu_text_list.c`): selected enabled `0x8020FFFF` = RGB FF/FF/20; ordinary enabled `0x80FFA888` = RGB 88/A8/FF (PS2 ABGR ordering); disabled `0x80006060` or `0x80303030` | Recovered code, requires executable verification for instruction-exactness |
| Page transition timer | FUN_002192a8 `src/ui/menus/update_menu_system.c`: `menu_system.timer = 12` plus per-screen leave/enter handlers and per-Moby animation changes | Recovered code; **not** proof of 12 static frames |
| Root panel image and intermittent flash | FUN_00223e28 `draw_menu_flashing_panel.c` draws HUD animation E99E frame 7, textures 0x1A/0x1C/0x19; native rare flash probability 1/2000 when idle, incrementing time by 2 until 0x100 | Recovered code, FX alpha equation/geometry not yet recreated |
| Panel stage animation | FUN_002192a8 explicitly calls `set_moby_animation(panel_slots[j], ...)` on the game's menu Moby instances and invokes screen enter/leave callbacks | Source lead. **Godot panels do not yet implement Moby vertex animation** |
| Menu click samples | Two decoded startup-bank samples (0,1), authentically present in user ISO | Direct audio; assigning them to focus/confirm is **provisional**, because native sounds are addressed through menu-panel Moby SoundDef entries, not these raw slots |

## What OBP chose/guessed (not claimed as retail)

- The large Godot inventory cards and 3-column grid, the 9-item paging rule,
  the visual dimensions and positioning of secondary screens and HelpDesk windows.
- Exact R&C1 menu panel dimensions/UVs and Godot transparency blending; PS2 GS
  alpha and 3D menu Moby transforms are **not implemented**.
- The original custom code applied a permanent selection sine-wave glow and
  cream font palette despite the native code using concrete packed colours;
  that approximation was removed from the root and all button selection text.
- The original custom code accidentally allowed mouse hover to highlight the
  Help row simultaneously with actual controller focus on Weapons.
  Root buttons now gate mouse hit-testing by *physical* last input and the
  pause-open cursor is forcibly hidden until a new physical mouse motion.
  Mouse-warp events immediately following menu activation are ignored briefly.
- Original panel flashing is **rare and timed**, not a continuously pulsing
  selection glow. The Godot display now follows that event trigger/time shape,
  but its GS blend and random-UV rendering remain provisional.
- Text baseline/sizing, outline thickness and root position are calibrated to
  one public PCSX2 screenshot, not derived from original vertex coordinates.
- The Godot HelpDesk trigger queue has source structure but uses invented
  phase durations and lacks retail voice IDs/selection glyph control bytes.
- Gameplay camera backdrop darkening has no validated R&C1 per-pixel filter
  yet. The custom deep-blue ~80% occluding overlay was removed on the six-row
  pause root. The game scene should remain visible through the menu.

## Actual capture comparison

Public screenshot: https://wiki.pcsx2.net/images/5/59/V1.5.0-dev-2155_-_Ratchet_%26_Clank_-software_mode-.jpg

Original screenshot: six stacked dark/black striped panels, thin beige/gold borders;
selected Weapons text yellow, other text light blue; game scene visible.

OBP before audit: seven-plus rows and OBP developer links inside a huge blue-grey
panel, cream label text, large opaque backdrop and gold rectangular focus outlines;
stale cursor could highlight Help. This was objectively wrong.

OBP after audit: six source-labelled rows without enclosing Godot card/OBP links,
native font + text colours and source `E99E` panel pixels, focused Weapons alone;
world is uncovered (on isolated fixture, Godot's default grey clear colour fills
behind the transparent pause). Native GS blending/3D transforms still differ from
retail, particularly the panel interior luminosity and stripe effect.

## Further fidelity gates

1. Capture an original retail pause state in PCSX2 at native frame resolution,
   plus an identical paused world state in OBP. Record panel bounds and actual
   per-pixel colour samples. Public screenshots are only a temporary reference.
2. Recover original menu panel Moby identity and model/animation, UV mapping and
   PS2 GS ALPHA test/blend equations. Implement source-evidenced vertex/panel
   effects rather than recolouring a Godot rectangle.
3. Recover `panel_slots[0]` SoundDef indices used by menu event 3/4.
   Validate actual sample hash/audio before calling focus/confirm sounds native.
4. Rebuild equipment page's layout around the original menu screen hierarchy
   instead of treating a three-column grid as retail.
5. Validate actual controller Start/X/Triangle/cancel and pointer transitions
   during ordinary gameplay, not only an isolated UI smoke.

Do not claim a visual component is retail-sourced merely because an original
sprite is blended over a generic Godot button.


## 23 October follow-up: source-style equipment grid

Native decompilation functions audited:

- `FUN_0021d948`, `src/ui/menus/draw_menu_item_grid.c`: item grid is an icon-only texture presentation drawn at positions derived from a `MenuScreen` grid's rows, columns, margins, and the panel Moby's font dimensions. Selected cells receive two nested GS rectangles; the icon animation frame is `+1` when equipped, `+4` when a variant is available (and not equipped), or `+2` when a menu locking condition applies. Unowned cells do **not** draw an item icon.
- `FUN_0021b858`, `src/ui/menus/update_menu_grid_selection.c`: directional input changes selected cell by ±1 horizontally or ±column count vertically. Without grid flag `0x8000` it wraps at the appropriate row/column boundary. The function explicitly translates between **three-column and five-column** grids on focus transfer. Confirm uses `item_available[id]` to gate entry actions and chooses `menu_system.equipped[menu_item_info[id].slot]` by item slot. Do not port mutation into the widget: the existing OBP gameplay inventory remains the save authority.
- `FUN_00221d68` text-choice operations and `FUN_0021d4a8` text menus do **not** imply that each item icon is a big text-bearing menu button.

This replaces the three-column named card UI with a five-column, fifteen-visible-slot icon grid, a single selected-item label, and source-shaped two-layer focus rectangle and icon variant selection. The 5-by-3 page arrangement is a **working configuration**: the code proves both three-/five-column layouts exist, but we have not recovered the specific original page's authored `cols`/`rows` from a live menu-screen instance. Multi-page host navigation remains OBP fallback for inventory overflow. Empty cells render without invented icons; selection of unavailable entries does not send an equip request.

The `Rac1RetailEquipmentCell` stores native animation IDs, ownership and current equipped state as readonly presentation inputs. `PauseMenuUi` forwards eligible equip requests to the existing game owner rather than updating save-backed state locally. Godot directional focus paths follow native modulo-column navigation, including row-edge wrap.

Visual caveats: native `D_00160290`/`D_00160294` cell dimensions and the Moby panel's camera/projection are not yet replicated, nor are GS selection-rectangle blending and exact item preview Moby camera; those are host approximations. The Help page remains provisional and must not be claimed native-faithful.

Verification target: native-disc Godot UI smoke for 15 icons on the first page and overflow page when necessary, left/right/up/down gamepad focus, locked equip rejection, original bitmap glyphs, no Godot scrollbars. Full regular gameplay still needs user playtest after review.
