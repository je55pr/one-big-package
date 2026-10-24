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
test viewport the original mesh-extrema implementation spanned
X=282..679; the later exact attachment-frame update below narrows
that to about X=290..670. The capture is ignored by Git.

**Historical limitation, resolved in the attachment-frame update below:**
this first implementation used all-vertex min/max instead of `Moby+0x78`
semantic attachment points 0 and 3. The exact original panel corners are
now implemented. The PS2 `convert_float_to_integer` rounding and original
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
is ready for the remaining typography pass; the attachment-corner
reconstruction is documented below.

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

## 23 October: exact menu attachment-frame corner indices and geometry

The previous native projected root used extremal skinned mesh Y/Z as the
panel rectangle. This was a grounded approximation, but the source renderer
uses a smaller four-point **attachment frame**, not those art vertices.

- `FUN_002196b8` copies four `Vec4` records through Moby pointer `+0x78`
  and projects records **0 and 3** with `FUN_00237a78`.
- The class-0x472 callback `FUN_0023a2c0` invokes `FUN_0023a318` to update
  those records. Its `FUN_0020cd48` batch transforms the semantic indices
  stored at NTSC-U ELF address `0x00160ED0`.
- That exact executable table contains little-endian words **0,1,2,3**;
  Veldin's original class-0x472 attachment-chain table maps them to
  skeleton joints **2,1,4,3** respectively. These are semantic attachment
  IDs, not indices into the 48 rendered mesh vertices.
- At animation 14 frame zero, `Rac1MobyPose.PoseLocalJoints` yields the
  first and opposite corners `(6,1.5,2.32)` and `(6,-1.5,1.82)` native
  units. The rendered mesh extends about 0.069 native units beyond these
  frame bounds in Y and Z. Slots 14–19 preserve the same 3-by-0.5 frame
  while changing vertical position.

`Rac1MenuPanelModel` now retains four source-derived attachment positions
for each frame of all 216 source sequences. `Rac1MenuPanelMotion` exposes
corner 0/3 with each panel pose; `Rac1MenuPanelCamera` projects those exact
semantic corners instead of the outer art extrema. Disc-backed tests pin
joint mapping, first-frame endpoints, frame census and the gap to mesh
bounds. The four semantic chains are dynamically read from source class
payloads; no retail bytes or synthetic point positions are embedded.

The integer projection and screen-offset gap is addressed in the next
section. Original output-target scaling still needs frame-matched PS2
verification.

## 23 October: original integer projection and one-pixel screen origin

The original menu's *integer* projector is `FUN_00237a78`, recovered in
`rendering/project_graphics_bounds.c`. It invokes `FUN_001FA6D0`
(`truncate_float_to_s32`) independently on these four expressions:

- `x = trunc(first_projected.x * 0.25 + half_width)`
- `y = trunc(first_projected.y * 0.25 + half_height)`
- `width = trunc((opposite.x - first.x) * 0.25)`
- `height = trunc((opposite.y - first.y) * 0.25)`

After the projection returns, `FUN_002196b8` **increments x and y by
one pixel**, then stores and draws the panel using those coordinates
**without reducing width or height**. The dark-blue clear rectangle
inside that projected panel uses a *second*, independent one-pixel inset
on every side; that interior clear is already implemented by the shader.

`Rac1MenuPanelProjection.RasterizePanel` now follows independent signed
truncation of origins and differences and the +1 origin correction,
with finite/overflow guards outside the ordinary retail projection domain.
`Rac1MenuPanelCamera.ProjectPanel` feeds its source-derived opposite
attachment corners into that rasterizer. Synthetic regression tests
separate truncation from floor, separately rounded endpoints, and even
subpixel-degenerate bounds. The retail fixture pins the actual projected
corners and verifies the discrete bounds derived from them.

This is a source-backed projection/coordinate correction, **not** a claim
of complete GS pixel parity. The native widescreen/display scale policy,
PS2 framebuffer coordinates and eventual float conversion edge behavior
still require emulator capture or deeper assembly evidence. The regular
OBP host viewport adaptation remains explicit, and secondary menus have
not been force-fit into the ordinary pause root's 3D slots.

### Ordinary root text renderer identified, shadow inheritance not assumed

A separate read of the original NTSC-U Veldin overlay's initialized
`MenuPage` at **0x001B2588** resolves its `screens[0..6]` at page offsets
`0x44..0x5C`. Those seven screens point to distinct records
`0x001B26B8 + slot * 0x50`, each with **update = 0x00278840** and
**draw = 0x00278E10**. Slots 7..13 are null; slot 6 is subject to the
original `menu_system.unkD8` admission gate. The active focus pointer
is `0x001B26B8` and state is 2.

This directly rules out assuming that `FUN_0021A328` (the separate
configured-label callback with the recovered +2/+2 shadow) draws the
ordinary root labels. The ordinary root uses its own Veldin-overlay
`0x00278E10` draw callback. Recover that callback and its text metrics
before altering the root's font baseline or claiming the generic
configured-label shadow is used there. This research did not copy raw
overlay payloads or alter host typography.

## 23 October: ordinary root text-list callback recovered and admitted

The Veldin overlay's initialized page `0x001B2588` contains seven
single-item `MenuTextListData` screens, addresses `0x001B26B8` through
`0x001B2898` in 0x50-byte steps. Every screen uses update callback
`0x00278840`, draw callback **`0x00278E10`**, and text-list flags 4.
The embedded text array at `0x001B2610` advances 0x18 bytes per entry;
each active entry has an action 3 (open page), no subtitle and a zero
terminator. The original authored text identities for slots 0..6 are
**20194, 20195, 20196, 20197, 20198, 20199, 20418**.

Disassembly of the local authorized Veldin overlay's draw routine
confirms the key typographic distinctions from `FUN_0021A328`:

- Flag 4 selects source large-font metrics (texture kind 3) and a
  14-native-pixel line-size parameter. Each root slot holds one item.
- With no fixed-step flag, each item's row height is
  `panel_height / (entry_count + 1)`; the initial vertical text anchor
  is `row_height - 14 / 2 - 1` for these single-item panels.
- Horizontal text placement measures the localized string and starts it
  at `panel_width/2 - measured_width/2`. Native enabled normal and
  selected colours match the already used `0x80FFA888` and
  `0x8020FFFF` values. Text entry action 0 is a distinct disabled mode.
- For these seven records there is no subtitle, and the callback does
  **not** call the configured-label renderer with its +2/+2 black
  shadow. Do not apply that other screen's shadow by assumption.
- Slot **6** is not an unconditional seventh pause button:
  `render_level_effects_and_screen_sprites` gates it on
  `menu_system.unkD8`. This branch intentionally leaves it dormant.

`Rac1MenuPanelPages.Read` now reads this typed seven-entry root census
from bounded initialized overlay sections instead of guessing labels.
It validates that each record has the supported callback/flag/list shape
and exposes its native text identity, action and admission status.
`PauseMenuUi.Rac1Projection` takes the six ordinary labels from these
source records rather than hardcoding their IDs; safe host navigation
is unchanged, and slot 6 remains dormant. Retail tests and rendered
Godot smoke assert the six labels and the extra gate.

**Still provisional:** Godot's bitmap-font scaling and ascent/baseline
are not yet raster-parity matched to the PS2 font print packet at a
native display size. The host currently centers the labels with the
source font and colours; no arbitrary shadow, new glyph style, or
seventh slot is added. Pixel-exact typography requires an emulator
reference capture or further native font packet recovery.

## 23 October: native large-font glyph quads replace Godot button shaping

Recovered resident renderer semantics from
`src/ui/text/font_print.c` (`FUN_001f62b0`) and
`src/ui/text/measure_text_width.c` (`FUN_001f6200`).
The original renderer advances by each **signed glyph byte+3** width,
places a **16x16** source texture cell at `(pen, y + glyph.top)`,
and draws no cell for the ASCII space, despite advancing the pen.
Normal ASCII root labels have no special palette/control/diacritic
drawing. `font_print_window.c` establishes that the generic label's
scissor, center flags, and shadow are independent from the Veldin
root's recovered text-list draw callback.

For the seven authored single-text screens, the callback uses large
font metrics and a 14-unit text parameter, single-entry
`rowHeight=panelHeight/2`, and an initial text anchor at
`rowHeight-14/2-1`. Horizontal layout uses the native signed
advance-width and `(panelWidth >> 1)-(width >> 1)`.
The original US English texts produce these measured widths:
Weapons=87, Gadgets=84, Quick Select=118, Items=52, Help=43,
Options=77 native pixels.

`Rac1MenuRootTextLayout` now models this as engine-neutral, tested
native glyph quads, with source byte U/V offsets, signed vertical
offsets and advances. `Rac1RetailRootText` draws each atlas cell in
Godot without Button/Label text shaping; `Rac1RetailMenuButton`
retains its native text for accessibility and input semantics but
makes the built-in text ink transparent only on the six source root
buttons. The original large-font RGBA atlas is whitened in memory for
native selected/normal palette modulation. No texture or source game
byte is checked into the repo.

**Scaling boundary:** original glyph cells are 16px. The host adapter
retains its prior *25px* visual glyph size via explicit
`25/16` scaling, and maps the source integer layout into each
already-projected panel. This is a documented viewport adaptation, not
a recovered PS2 output-target scale. No evidence was invented for
final native framebuffer dimensions, GS sampler details or
interlaced raster offset. Native-vs-Godot pixel-difference assertions
must wait for a reference PS2 screenshot.

## 23 October: original Weapons submenu grid and source cell ordering

The ordinary Veldin root's *Weapons* text item (ID 20194, action 3)
links to native `MenuPage 0x001B2DB8`, back pointer
`0x001B2588`. Its focus screen `0x001B2F58` is a
`MenuItemGridData` driven by the original overlay callbacks
`0x002794A0` (update) and `0x0027B590` (draw).
The retail record definitively specifies **3 rows, 5 columns**,
margin fractions **0.08 / 0.08**, selected cell **0**, flags
**0x20** and pointer **0x001B3220** to 15 contiguous,
10-byte `MenuGridCell` records.

Native row-major slot identities are:

| Row | Slot 1 | Slot 2 | Slot 3 | Slot 4 | Slot 5 |
| --- | --- | --- | --- | --- | --- |
| 1 | Bomb Glove (10) | Pyrocitor (16) | Blaster (15) | Glove of Doom (20) | Mine Glove (17) |
| 2 | Taunter (14) | Suck Cannon (9) | Devastator (11) | Walloper (18) | Visibomb Gun (13) |
| 3 | Decoy Glove (25) | Drone Device (24) | Tesla Claw (19) | Morph-o-Ray (21) | RYNO (23) |

Original icon animations are read from each cell's `icon` field
(`EA6A EA70 EA6F EA74 EA71 / EA6E EA69 EA6B EA72 EA6D /
EA79 EA78 EA73 EA75 EA77`); the entire page has frame zero and
`kind = 0`. With grid flag `0x20`, source
`FUN_0021d948` **does not draw the +1 equipped frame** on
this screen. Unowned native items occupy their original slots but
draw no icon; selecting an unowned slot does not request an equip.

`Rac1MenuPanelPages.Read` validates this screen and reads all fifteen
original cells through initialized, bounded overlay sections. The
Godot Weapons menu now sorts its first fifteen entries by **the
original native grid** and uses exact native icon IDs, rather than
sorting by C# enum value. Missing entries become unowned native
placeholders without inventing unlocked equipment. Additional OBP or
cross-game entries are kept on clearly separate overflow pages,
preserving original R&C1 weapon positions and leaving the gameplay
inventory and equip handler as authority. This is intentionally a
first page, not a claim that OBP's overflow paging is native R&C1.

Other subpage layout facts were discovered without changing those UIs:
Gadgets has authored 2x3, 1x3, 1x3, and 1x2 grid
screens with distinct admission flags; Quick Select reuses the 3x5
weapon cell table alongside a 2x3 screen; Items has a 3x3 grid
including `kind = 1` alternative availability cells. These screens
require separate focus-transfer and menu lockout validation before
turning them into host replacements.

Still provisional: the exact VU/GS projection of weapon-icon
rectangles, native preview 3D Moby/camera, text details and full
weapons page transition animation. Only original grid structure,
cells, icons, slot ownership gating and the equipped-frame
suppression are asserted as source-backed.

## 23 October: original Gadgets submenu linked-grid census and navigation

The ordinary root's second action-3 entry points to
`MenuPage 0x001B28E8`, back `0x001B2588`, with initial
focused screen **0x001B2BE8**. The initialized Veldin overlay
defines four separate `MenuItemGridData` screens:

| Screen slot | Address | Shape | Flags | Authored item IDs |
| --- | --- | --- | --- | --- |
| 2 | 0x001B2B80 | 2 rows x 3 columns | 0x20 | 26, 22, 12 / 32, 27, 31 |
| 3 | 0x001B2BE8 | 1 x 3 | 0x04 | 2, 3, 4 |
| 9 | 0x001B2C50 | 1 x 3 | 0x08 | 6, 5, 7 |
| 10 | 0x001B2CB8 | 1 x 2 | 0x10 | 29, 28 |

Every grid uses update `0x002794A0` and draw
`0x0027B590`, margin fractions `0.1/0.1`,
`selected_cell = 0`, ten-byte `MenuGridCell` entries of
kind zero and base frame zero. Its per-item icon animation IDs
are, in order:

`EA7A EA76 EA6C EA80 EA7B EA7F / EA62 EA63 EA64 /
EA66 EA65 EA67 / EA7D EA7C`.

The four screens' `up`/`down` fields form the complete ring
2 ↔ 3 ↔ 9 ↔ 10 ↔ 2. The retail
`FUN_0021B858` grid updater traverses inside a screen, then
moves focus to the authored adjacent screen on edge input,
clamping the column for a narrower destination (3 → 2).
Initial focus is screen 3 (Heli-Pack / Thruster-Pack / Hydro-Pack
group), **not** screen 2. The page is not one flat grid.

`Rac1MenuPanelPages.Read` now validates and decodes this
entire bounded native grid/link/cell census. The Godot Gadgets
page draws each group separately, retains all fourteen native
positions and source icon IDs, keeps not-owned icons hidden,
and wires D-pad navigation to the source ring. UI-only
inspection does not mutate gameplay equipment. The real
game state supplies item ownership for *all* fourteen gadget
IDs, not merely the seven enum-backed traversal abilities.
Native gadget items are no longer duplicated in the host Items
list. Known traversal equipment names are preserved; where the
native localized name mapping hasn't been recovered, the host
uses explicit `Native item <id>` placeholders, not fabricated
retail text.

**Explicit limitations:** Native `menu_system.unk134` and
`unk138` can cause the updater to skip a whole grid when
flags 8 or 4 are set; the associated gameplay conditions
still need recovery, so this host does not claim conditional
focus-transfer parity. Screen sizes, positions, text detail,
presentation camera and transition animations are still Godot
adaptations. The original icon textures and authored identity,
column/row/group data and link topology are confirmed.

## 23 October: original Quick Select candidate grids and save-backed assignment

The ordinary root's Quick Select action points to
`MenuPage 0x001B32B8` (back `0x001B2588`,
initial focus `0x001B3398`, page state 5). This page
has **two source item-candidate grids**:

- Panel slot 2 / screen `0x001B3398`: **3 rows x 5
  columns**, flags `0x20`, margins `.08/.08`. Its 15
  weapon cells are the **same pointer `0x001B3220`**
  as the authored Weapons menu page; the native IDs/icons
  therefore follow the identical original ordering.
- Panel slot 3 / screen `0x001B3400`: **2 rows x 3
  columns**, flags `0`, margins `.1/.1`, cell pointer
  `0x001B2D20`, gadget candidates `26,22,12 / 32,27,31`
  and icon animations `EA7A,EA76,EA6C / EA80,EA7B,EA7F`.

Both screens call updater `0x002794A0` and drawer
`0x0027B590`. Both their up/down links point at each
other, crossing between five and three columns. The
resident `update_menu_grid_selection` advances a
column by +1 when moving 3→5, and decrements/clamps
when moving 5→3. This is a **candidate list**, not an
eight-slot quick-select assignment wheel.

The native `update_menu_item_slots` (`FUN_0021C7A0`)
controls the separate **eight slot** array. Shoulder
directions move the selected slot modulo eight; Cross
on an owned nonzero candidate first clears any previous
instance of that item, stores it at the selected slot,
and moves the cursor to the following slot modulo eight.
An unowned or zero item does not assign anything.
The native updater separately records a usage statistic
not yet hosted by this implementation.

`Rac1MenuPanelPages.Read` now decodes and validates
both original grids and their links through bounded
initialized overlay data. `Rac1WeaponInventory.
TryAssignQuickSelect` implements the save-backed
item assignment rule with strict slot/ID/ownership
checks and uniqueness while preserving current
equipped item and ammo. The Godot pause page uses
21 source candidates in the native two-grid layout,
but represents the **eight save-backed slots** as an
explicit host strip until the rotating native wheel's
geometry has been recovered. The slot strip does
not claim to be retail artwork.

While Quick Select is open, the modal UI temporarily
suspends only Godot's default L1/R1 focus-cycle bindings.
The native slot cursor owns those buttons, while
D-pad continues to navigate candidate grids. Original
input bindings are restored on page navigation or
tree exit, and the frame-local focus guard prevents
Godot's default focus shift from selecting a different
candidate during a shoulder press. The rendered smoke
tests binding restoration and the selected candidate.

**Limits:** no fabrication of original Quick Select
wheel/preview geometry, native Moby poses, button
prompts, animations, or usage-stats effects. Cross-game
weapon overflow admission and assignment remain
separate work; this pass changes only the original
21 R&C1 candidate grid and the eight-slot R&C1
save-backed block.

## 23 October: original R&C1 Items grid

Ordinary pause root item 3 points to Items `MenuPage
0x001B5CB8`, state 6 and back to `0x001B2588`.
The initially focused grid is panel slot 1, screen
`0x001B5E60`, updater `0x002794A0`, draw callback
`0x0027B590`, cells at `0x001B5E00`, **3 rows x 3
columns**, margins `0.5,0.1`, selected cell 0, flags
`0x1`. The updater `FUN_0021B858` gates Cross-to-equip
on `!(grid.flags & 1)`, so Items cannot equip weapons
or toggle equipment. Its directional navigation is a
wrapping three-column grid, not Weapons' five columns.

Original ten-byte cell records (row-major), as
`(kind, id, icon animation, frame)`:

- Row 0: `(0,30,EA7E,0)`; `(0,35,EAC9,0)`;
  `(0,34,EAC9,2)`
- Row 1: `(1,4,EAC9,3)`; `(1,5,EAC9,4)`;
  `(0,33,EAC9,1)`
- Row 2: `(1,0,EAC8,0)`; `(1,1,EAC8,1)`;
  `(1,2,EAC8,2)`

The first `kind=0` entries are owned according to
`item_available[id]`; `kind=1` entries are drawn
**only** if `alternate_item_available[id]` is nonzero.
This distinct native array is also referenced by retail
mission-condition handling. Alternate index **zero is
a valid cell**. IDs with different kinds are not
interchangeable even if numerically equal. The original
grid renderer uses `get_icon_frame(cell.icon,
cell.frame+frame_offset)`, including alternate cells'
authored nonzero frames. The host must not collapse
them all to EAC9/EAC8 frame 0.

The decoded, bounded overlay data is exposed as
`Rac1MenuPanelPages.NativeItemsGrid`, with an
availability lookup based on exact source kind.
Godot builds all nine native cells and uses the
saved primary R&C1 item flags for `kind=0`.
**The current campaign host has no proven mapping
for alternate_item_available[] save/progression**, so
`kind=1` stays unavailable unless explicit alternate
flags are supplied; ordinary weapon/gadget ownership
is never guessed to be an alternate unlock. That
state requires separate research and implementation.
There is no invented selection/equip action, no
scrollbar or fabricated rewards.

The grid's absolute icon positions and per-panel
retail Moby projection remain unresolved; the current
Godot GridContainer is a bounded structural host,
not an assertion of pixel-exact retail placement.
