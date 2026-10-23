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


## 23 October rendered-pass correction

The screenshot of the newly icon-based Weapons page confirmed that the old opaque
`MenuCard` still covered the paused world and had a large invented
"RATCHET & CLANK" header. Retail `FUN_002196b8` projects individual menu
panel Mobys and their contents in front of the scene; this is *not*
evidence for a global solid navy dialog. The generic enclosing card, large
branding, debug footer and separator are hidden on retail pages. OBP
Development Tools retains its card to distinguish it from the game UI.
The Help topic panel also no longer prints implementation-status notes
in the player-facing interface; source-evidence qualifications live here.
The precise panel Moby frames and GS render state remain unimplemented.


## Native GS blending compositor pass (23 October 2026)

Native references checked directly against the local Lombyte R&C1 decompilation:

- `src/ui/menus/draw_menu_flashing_panel.c` / `FUN_00223e28`
- `src/ui/menus/render_level_effects_and_screen_sprites.c` / `FUN_002196b8`
- `src/ui/menus/update_menu_system.c` / `FUN_002192a8`
- `src/OBP.PS2/Textures/Ps2Texture.cs` (resident CLUT alpha expansion 0..128 to 0..255)

The original panel overlay sequence is explicit and differs substantially
from the formerly chosen near-zero Godot alpha/outline combination:

1. `get_icon_frame(0xE99E, 7)` covers the panel in native source-over mode
   (GS `ALPHA=0x44`, `(Cs-Cd)*As/128+Cd`).
2. Rare flash: 1-in-2000 probability when idle. Once active, native time
   advances by 2 per tick until 0x100. The flash samples effect texture
   `0x1A` with a new random (0..199, 0..199) UV origin each update and
   uses GS `ALPHA=0x68`, `(Cs-0)*FIX/128+Cd`, with FIX supplied as
   `min(0x80, 2*(0x80-clamp(time-0x80)))`. The translucent result is
   added to the framebuffer, not alpha-blended as a normal image.
3. `0x1C` draws with UV span `(panel_width, 1.5*panel_height)`
   and colour `0x50606060`, i.e. RGB*96/128 and alpha*80/128.
4. `0x19` draws with native sampled source rectangle `(1,1,62,62)`
   over an inset destination; the destination width/height adjust by
   -2 if below 0x4c, -1 from 0x4c to below 0x97, and 0 thereafter.

The new `game/shaders/Rac1RetailPanel.gdshader` implements these four
draw stages in order against the current scene colour with explicit native
alpha arithmetic; `Rac1RetailPanelSurface` draws that shader behind each
menu label. `Rac1RetailPanelBlend` contains engine-neutral time/threshold/
tint rules with deterministic xUnit tests. Godot scene-buffer sampling keeps
the original world visible instead of introducing a guessed solid dialog
behind the six-item pause menu. The original runtime-only R&C1 sprite/FX
decoder is still the sole source of all menu pixels.

Important scope boundary: **the compositor now follows original GS blend
*formulas* and layer order; it is NOT a pixel-perfect 2002 GS emulator.**
Godot's OpenGL/colour-space and sampling conventions may differ, the flash
alpha-test is an educated transparent-texel gate rather than a recovered
TEST_1 register, and a `Camera3D` projection of native 3D menu-Moby
vertices has not yet been reconstructed. We currently feed the shader
Godot's existing host-defined panel rectangles as `projected_size`.
Native `FUN_002196b8` instead draws 3D menu Mobys, projects four frame
corners, allocates an appropriate power-of-two render texture, draws menu
contents offscreen in two passes and blits that result to the projected
bounds. That stage remains the next fidelity task, not something claimed
as complete by this shader.

Validation:
- Assert six loaded panel compositor materials in native-disc Godot smoke.
- Compare `captures/rac1-retail-ui/root.png` captured by a rendered
  OpenGL Compatibility-mode smoke, never commit the capture/artwork.
- Preserve ordinary-controller, equipped/unowned and HelpDesk flow from the
  existing smoke suite.


Further 3D panel identity recovered while tracing this pass:
`src/overlays/shared/audio/voices/00276368.c`, original shared
scene initializer near `FUN_L00_002765xx`, creates **14 class-0x472
menu panel Mobys** via `create_menu_preview_moby(0x472)`, installs updater
`FUN_L00_002df638`, initializes their position at the player's camera
coordinates, sets zero rotation, and places each at the final frame of
its page-selected animation. The menu update later reverses/advances
those animations on screen change. `FUN_002196b8` projects their frame
corners to determine screen bounds before offscreen composition.

**Remaining genuine 3D task:** decode class 0x472 model and its animation
sequence from the user's disc, recover updater `FUN_L00_002df638`,
match camera projection and route the resulting bounds into
`Rac1RetailPanelSurface.projected_size`. Using another arbitrary 3D
cube or quad would not be more authentic.


## Source class-0x472 model and native projection (23 October continuation)

The original Veldin level core's **ordinary Moby table**, not the global
compressed gadget directory, contains class `0x472` (decimal 1138).
The supported disc census pins the following source facts:

| Evidence | Verified native data |
| --- | --- |
| Level0 Moby directory row | 94 (of 125), asset offset 9,732,224 |
| Class payload | 167,232 bytes (not copied to Git) |
| Base geometry | 48 vertices, 32 triangles, 5 joints |
| Material | Moby texture ID 91 |
| Mesh plane | local X=0; local Y/Z approximately -1.0688..+1.0688 |
| Animation table | 216 sequence slots; 0..13 static single-frame; 14..128 seven-frame; 129/130 and 133 single-frame; remaining mostly seven-frame |
| Initial pose | source hierarchy decoder accepts five joints and all tested sequence frames |

`Rac1MenuPanelModel.Read` now imports the canonical asset from Level0 with
the existing `Rac1StaticClasses` and `Rac1MobyPose` codecs. It retains
source-space bind vertices, triangle indices, native sequence identities,
per-frame skinned vertices and timing rates as engine-neutral runtime
presentation evidence. The `Rac1UiAssets` lifetime owns this decoded
instance; the Godot source attachment exposes it without writing asset bytes.

The native `project_graphics_bounds.c` (`FUN_00237a78`) establishes
the two-corner screen projection used by `FUN_002196b8`:
subtract camera position, multiply by 1024, set w=1, transform by
camera-relative view matrix, perspective divide, multiply X/Y by
`view_context.screen_scale`, multiply by 1/4 and offset by
`screen_extent.half_width/half_height`. The integer packet coordinates
then use `convert_float_to_integer` (the exact rounding instruction still
needs executable verification). `Rac1MenuPanelProjection.ProjectFloat`
implements the exact floating-point path with an explicit injected native
view transform; the unknown camera matrix is **not guessed**.

The same renderer allocates per-panel offscreen texture dimensions starting
at 2^7 by 2^7, growing each axis to enclose projected bounds and reducing
the *height* logarithm while `widthLog+heightLog >= 18`. This can crop
oversized panels (for instance 256x750 resolves to 256x512). The engine-neutral
`OffscreenTarget` port has deterministic boundary tests.

**Runtime integration achieved:** original class 0x472 is decoded on
source attachment, and the Godot retail UI smoke verifies its identity and
animation data alongside the six resident source-blend shader panels.

**Not complete:** `MenuPage.moby_anims[14]` mapping per actual page,
`FUN_L00_002df638` live panel-updater transform, the retail camera matrix
at the screen-transition instant, the exact quantised integer projection,
and the offscreen 2-pass framebuffer blit. The UI shader still uses host
rectangular bounds as inputs until the camera and slot animation mapping
are verified. Never treat this importer/projection function alone as
proof that class-0x472 is already driving visible panel movement.


## 23 October: Veldin overlay page assignments and source 3D motion

The original Veldin level-overlay section chain contains six independently pinned
MenuPage entrypoints, recovered from `FUN_L00_00276368` and read at runtime
from user-supplied disc bytes (no hard-coded image/text payloads).

| Overlay address | Initial menu state | Class-0x472 animation IDs, slots 0–6 |
| --- | --- | --- |
| `0x1B2588` | 2 | 14, 15, 16, 17, 18, 19, 20 |
| `0x1B86C8` | 46 | 0, 1, 16, 17, 18, 5, 6 |
| `0x1B3518` | 11 | 21, 22, 23, 24, 25, 5, 6 |
| `0x1B6088` | 15 | 99, 100, 101, 102, 103, 5, 6 |
| `0x1B6B38` | 37 | 134, 135, 136, 137, 157, 5, 6 |
| `0x1B71F0` | 34 | 158, 159, 2, 161, 4, 5, 6 |

All fourteen IDs for every page are checked in original-disc tests. The
native `MenuPage` struct begins with exactly fourteen signed animation IDs,
followed by its back-page pointer and state at +0x38 and +0x3c. Screen
slots 7–13 normally carry static sequences 7–13.

**Critical decoder correction:** `Rac1MobyPose.PoseRigidHierarchy` returned
identical vertices for every frame because it only considers the local joint
quaternions and bind hierarchy. These menu sequences also contain five
packed translation entries per frame and optional fixed-point scale entries;
`Rac1MobyPose.PoseLocalHierarchy` already correctly decodes these channels
from the original Ratchet equipment work. Using the latter exposes genuinely
changing panel geometry: e.g., Veldin ordinary page animation 14 spans a
greater-than-6-unit difference from start to end and clearly translates
its first strip laterally as it progresses. Original sequence frame timestamps
are 0,16,..,96, each seven-frame clip has transition rate 0.5, consistent
with the native 12-update screen-change timer.

`Rac1MenuPanelPages.Read` recovers these tables from the bounded level-
overlay section chain (not guessed assignments). `Rac1MenuPanelMotion` is
the deterministic engine-neutral 14-slot track. Forward page changes set
incoming animation IDs to their first frame and speed +1; backward
changes play outgoing IDs from the last frame at speed -1; both complete
after 12 engine updates. Motion poses yield real per-frame world-space bounds.
These transitions follow `FUN_002192a8` and `FUN_0023a2c0`; runtime uses
the source per-sequence transition rates.

The Godot source loader now exposes both `NativeMenuPanels` and
`NativeMenuPages`, and disc-backed UI smoke rejects a missing page table.
The live visual root still uses host-calibrated screen rectangles because the
retail camera view transform and front-end Moby-frame projection remain to be
recovered. Source-valid animation trajectories are now available for use by
a future projected geometry adapter; DO NOT stretch native coordinates into
a guessed screen axis and call the result retail faithful.


## 23 October: camera projection now drives the visible six-panel pause root

The source-backed pause camera was recovered from actual R&C1 native functions:
- `FUN_00218d10` resets the camera position to **(256,256,64)** and
  its three basis directions to identity.
- Both menu initialisers position all fourteen class-`0x472` Mobys
  at this camera location, so source pose vertices are camera-relative.
- `FUN_001f2260` derives the view mapping **screen X=-world Y**,
  **screen Y=-world Z**, and **forward depth=world X** with identity basis.
- `FUN_L00_00276368` sets `view_context.fov[0]=0.63` before
  `FUN_001f2d98`, which derives NTSC vertical FOV as `0.775 * 0.63`.
- `FUN_001f2c60` and `FUN_001f33b8` set screen half extents and
  quadruple-space screen scale, undone by `FUN_00237a78`'s
  quarter-screen final projection.

The visible source panel meshes all have relative **X=6**, a consistent
six-unit distance directly in front of this camera. This yields a
well-defined, source-supported projection for screen coordinates:

`screen_x = viewport_width/2 - y * (viewport_width/2) / (0.63 * x)`
`screen_y = viewport_height/2 - z * (viewport_height/2) / (0.63*0.775*x)`

`Rac1MenuPanelCamera` implements that mapping and rejects nonpositive
depth. Dedicated retail-disc tests confirm the six visible root strips
are centred, nonoverlapping, and ordered correctly, and that the known
camera origin, axes and FOV are preserved.

`PauseMenuUi.Rac1Projection.cs` now renders the root's six buttons as
absolute-positioned Controls whose geometry comes from the real
class-`0x472` frame-zero mesh and native camera projection. The old
365x64 fixed-height VBox fallback remains ONLY when original assets
cannot be attached. Leaving the root page plays the source
`0x472` panel tracks from their visible starting frame to offscreen
over 12 native update ticks. Equipment, HelpDesk and OBP development
pages are intentionally not remapped to arbitrary 3D slots.

Rendered R&C1 source-backed OpenGL smoke on JESS-PC captures
`captures/rac1-retail-ui/root.png`: six dark striped panels are
centred horizontally and spaced across the picture. In the 960x720
test viewport they span approximately X=282..679, rather than the
previous host-defined 365-pixel width. The capture is ignored by Git.

**Remaining uncertainties:** `FUN_002196b8` gets the first/opposite
corner of a native transformed four-point attachment frame at
`Moby+0x78`, not the all-vertex min/max currently used as a geometrical
proxy. On class 0x472's planar six-unit geometry, the mesh extrema
reproduce the broad screen shape, but corner offsets may differ by
pixels. The exact PS2 `convert_float_to_integer` rounding and original
720/448/512/640 render-target scaling remain to be verified. Current
host viewport is passed through unchanged for widescreen, an explicit
OBP presentation adaptation. We have **not** invented 3D page
assignments for OBP-only screens, and the root entry animation starts
in the visible pose to preserve input smoke and accessibility; only the
source-defined exit track is currently played.

## 23 October: definitive panel clear colour, from the NTSC-U executable

**Major readability fix.** `FUN_002196b8`
(`src/ui/menus/render_level_effects_and_screen_sprites.c`)
does not begin each projected panel from the level's bright scene pixels.
Before it draws text and later invokes the source sprite/effect stack, it
issues an `append_screen_rect_packet` with `panel_clear_color_gp`
(`D_001601B0`) for the rectangle
`(projected_x + 1, projected_y + 1)` to
`(projected_x + width - 1, projected_y + height - 1)`.
It also clears each offscreen menu-content render target to that same
colour via `draw_hud_rect_depth`.

The exact source value was recovered from **the user's actual R&C1
NTSC-U ISO**, not chosen by inspection:
`SCUS_971.99;1`, ELF PT_LOAD segment (file offset `0x1000`,
virtual address `0x00100080`), memory address `0x001601B0`,
little-endian dword `0x80100808`.

| Native value | PS2 RGBAQ meaning |
| --- | --- |
| `R=0x08` | Red 8/255 |
| `G=0x08` | Green 8/255 |
| `B=0x10` | Blue 16/255 |
| `A=0x80` | Full coverage at GS alpha denominator 128, **not** half opacity |

The previous Godot shader skipped the rectangle entirely and began its
`E99E / 0x1A / 0x1C / 0x19` composite from the underlying scene colour.
Partially transparent source art therefore produced broad, medium-grey
panels, especially over bright level geometry.

The shader now applies the exact opaque blue-black source clear in the
one-pixel-inset projected region **before** compositing the original art
and effects. This preserves the genuine texture's stripes and edge
pixels without adding a guessed gradient, global scene overlay, or
invented colour.

**Verification:** `Rac1RetailPanelBackingTests` opens the user's disc,
reads the ISO9660 root, locates `SCUS_971.99`, follows the ELF PT_LOAD
virtual address mapping and asserts the original word is exactly
`Rac1RetailPanelBlend.NativePanelClearRgba`. A separate rendered Godot
`Rac1RetailUiSmoke` creates an almost-white (`1,1,0.88`) world
background, captures `captures/rac1-retail-ui/root-bright.png`,
samples an uncovered screen pixel to prove the fixture is bright,
then samples a panel interior pixel to prove the dark clear is present
(`R,G < .26`, `B < .30`), alongside its normal grey-world capture.
This protects the precise user-reported readability regression in a
real GPU frame. Both captures remain ignored local evidence.

**Not yet claimed:** exact GS framebuffer quantisation/dither,
the offscreen content's separate target pass and clipping on secondary
pages, four menu Moby frame attachment-corner IDs, and exact retail
font baseline and shadow settings.

### Adjacent original menu text configuration

Further ELF constants from `SCUS_971.99`:
`D_001601B4 = 10` (fade duration),
`D_001601B8 = 2` (text shadow X),
`D_001601BC = 2` (text shadow Y),
`D_00160258 = 4` (text region vertical inset),
`D_00160268 = 16` (line advance).
These are used by `FUN_0021a328`
(`render_configured_text_label.c`), which prints a second black
shadow copy of the label at the +2,+2 offset. **They are not yet
applied to every pause-menu choice:** the exact label draw callback
associated with the ordinary six-row root needs verification before
we can claim that all six choices use this shadow path. This evidence
is ready for the remaining typography/attachment-corner pass.

## 23 October: actual Veldin gameplay pause contrast gate

The ordinary `./tools/rac1-veldin-play-smoke.ps1` now opens and closes
the exact gameplay `PauseMenuUi` through `OBPGame.TryOpenPauseMenu` while
standing on Veldin's authored collision. This is distinct from the
isolated `Rac1RetailUiSmoke` fixture, and does not stage a replacement
world/background or teleport the player.

`OBPGame.Rac1VeldinRetailPauseSmoke.cs` asserts the attached retail
source supplied six original class-`0x472` projected panel buttons, each
with its source-backed shader surface, and the initial Weapons focus.
The rendered gate reads each panel's interior from the actual composed
world viewport and rejects washed-out pixels using the same conservative
contrast bounds as the white-background UI fixture. It verifies the
player cannot move while paused, closes the UI through the ordinary
host path, and confirms the tree resumes. The local, ignored capture is
`captures/rac1-ui-veldin/root.png`.

On JESS-PC the full rendered ordinary Veldin play smoke passed, including
the added panel checks, native collision/movement, combat and natural
restart. This establishes *Godot gameplay integration and readability*,
not pixel-exact PS2 GS output or a frame-matched PCSX2 comparison.
