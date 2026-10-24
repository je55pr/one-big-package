# R&C1 pause, inventory and HelpDesk Godot presentation

Status: historical first-pass audit (2026-10-22). **This is not a retail-perfect menu recreation.** The early statements below describing Quick Select as read-only, root panel placement as generic Godot, or Items as a paginated overflow list have since been superseded by the source-backed Quick Select/Items/model work. For the 2026-10-23 review milestone and exact native/provisional boundary, use the current PR #170 description and `research/RAC1_UI_RENDERING_EVIDENCE_AUDIT.md`.

## Source and ownership

The runtime requires a supported user-attached NTSC-U `SCUS-97199` disc. `Rac1UiAssets` decodes 326 HUD animation frames, three bitmap fonts with native spacing and the original localisation archive directly into memory. `Rac1RetailUiAssets` adapts those to Godot; no retail images, font byte data, executable bytes or game text content are checked into this repository.

The public Lombyte decompilation is an *architecture lead*, not imported code:

- `src/ui/menus/update_menu_system.c`: page transition timer of 12 game ticks; current/pending-focus and page enter/leave state
- `src/ui/menus/pause/draw_pause_ring.c`: native item icon family and pulsing focus treatment
- `src/ui/menus/draw_menu_item_grid.c`: availability, equipped frame variant and selected-grid rendering
- `src/ui/help/draw_help.c`, `update_help_state.c`, `help_draw_prompt.c`: prompt, frame expansion, text fades and dismissal phases

## Current implementation

- `Rac1RetailMenuNavigation` is source-game-specific but purely presentational. It owns seven retail/OBP page identities, a deterministic 12-tick page transition, per-page focus restoration, and an explicit equipped/locked admission predicate. It **does not** mutate inventory/campaign data.
- `PauseMenuUi` now takes a `RetailEnabled` branch only for an active R&C1 session with successfully attached source data. The fallback original OBP pause menu is preserved for other games/unattached assets.
- Root contains the original six menu topic names in the documented order: Weapons, Gadgets, Quick Select, Items, Help and Options. Start/Resume, World selector, configuration and developer commands are OBP host navigation and visibly distinguished. The Development page contains all original opt-in debug output, equipment preview/refill, movement pack, swingshot preview, save/load reproduction callbacks.
- Weapons and gadgets use recovered native IDs, icon animation IDs and runtime-decoded font/PNG-equivalent sprites, with owned/equipped/locked display flags. The weapon page emits an equip *request* only for owned weapons; `OBPGame.OnRac1WeaponSelectionRequested` remains the final gameplay authority. Gadgets/items and Quick Select are **read-only presentations** until the native menu's actual assignment/admission semantics can be integrated safely with campaign save state. They are not fake progression toggles.
- Godot skin primitives accept icon animation/frame IDs, render a stretchable-ish `0x7580` panel, expose font/text lookup and a highlight pulse. Source glyph bitmap RGBA is whitened only in a transient **Godot tintable copy** so labels achieve readable contrast; glyph alpha, UV coordinates and horizontal advances remain as decoded. The original palette/color and panel stretch geometry still need retail screenshot matching.
- `Rac1HelpDeskPresentation` is a presentation-only queued state machine, with explicit opening, prompt, expansion, text fade, steady text and closing. `Rac1HelpDeskView` renders with source fonts and HUD frame/button sprites. The Help page uses the original Help category headings while displaying provisional category-label text. The in-world `PlayerHud` accepts the existing `HudContextPrompt` snapshot as input and renders a HelpDesk-style framed prompt instead of another permanent generic card when retail assets are attached.
- Game, keyboard and pad input continue to route through the existing `ControllerBindings` and `ControllerMenuNavigation` rather than taking gamepad stick events in the widgets. No change to `WeaponQuickSelectUi.cs` or movement input ownership (#164).

## Validation and screenshots

- `tests/OBP.Tests/Rac1RetailMenuNavigationTests.cs` covers atomic 12-tick page handoff, focus restoration, owned/locked gating, HelpDesk phases, queued messages and invalid tick inputs.
- An isolated Godot `res://diagnostics/Rac1RetailUiSmoke.tscn` actually instantiates the retail widget with in-memory game assets (with a locally attached R&C1 ISO). It sends injected joystick D-pad/A and keyboard Escape events, verifies page navigation, locked/owned presentation, equipped callback, HelpDesk expansion/dismissal, application options callback and development page separation, and closes with a nonzero exit code on failures.
- Non-headless rendering outputs local ignored captures under `captures/rac1-retail-ui/`: root, weapons, gadgets, helpdesk, options, development. These captures are private visual comparisons and must not enter Git.
- The existing full `ControllerSmoke` was updated to navigate separate Options and OBP Development pages. Isolated Godot smoke is a **GUI functional integration**, not a proof that an ordinary retail campaign was played in full.

### Explicit visual comparison vs retail

Reference: [PCSX2 Wiki R&C1 2002 pause screenshot](https://wiki.pcsx2.net/images/5/59/V1.5.0-dev-2155_-_Ratchet_%26_Clank_-software_mode-.jpg), source-original visual reference only.

| Aspect | Original retail reference | This Godot implementation | Gap |
|---|---|---|---|
| Pause root | Six compact horizontally-striped, gold-border rows on top of the 3D scene, selected yellow | Six source-text-labelled, focused rows, outlined and centred; OBP navigation below | Still uses Godot panel/backdrop; no PS2 scrolling stripes or blur; wider layout |
| Typeface | Original pixel bitmap glyphs with varied color | Exact native font atlases/widths, Godot tintable alpha copy | Colour, baseline, shadow intensity and size need measurement |
| Weapons | Icons with native frame selection and preview object | 15 native item icon IDs, owned/equipped/locked cards | No rotating 3D preview, source equipment description or confirmed gold frame state |
| Gadgets | Native inventory/category previews | Selected named gadget IDs with icon and ownership, presentation-only | Actual native gadget equip and preview still missing |
| Quick Select page | Retail eight-slot reassignment/selection | Read-only current save-slot identities | Slot-edit navigation and confirmation not yet recovered |
| HelpDesk | Contextual queued message activation, opening, audio gate, automatic timeout | Queued text/animation state driven by injected UI requests or `HudContextPrompt` snapshots | No retail native help-message ID/voice playback, button-glyph substitution, or fade/audio timing |
| Controls | Original gamepad semantics | Existing OBP pad/keyboard focus, back and accept | Need native button sound/edge semantics and handheld-controller testing |

Do not claim per-frame/menu fidelity until PCSX2 matched-state captures show colour, spacing, sprite animation, input gating and 3D previews comparable. A screenshot captured from isolated UI state is not the same as retail campaign gameplay.

## Work remaining (bounded follow-ons)

1. Recover native `MenuItemInfo` and HelpDesk message bindings from an actual retail menu session, not a zero-initialized file-backed table, including voice subtracks and button-glyph controls.
2. Implement quick-slot assignment and gadget equipment navigation through native campaign sessions and save-backed state when admitted; no source-owned save values should be edited by UI.
3. Recreate retail's 3D item-preview camera, fly-in/out frame geometry, per-item gold states, audio and HelpDesk timeouts.
4. Test handheld joypad on real ordinary Veldin and verify interaction with pause, open dialogs, death/restart, and non-R&C1 worlds.

## Follow-up: retail panels, no scrollbars, input modality (2026-10-22)

- **No Godot scrollbars:** Retail `PauseMenuUi` no longer instantiates a `ScrollContainer`. The weapon/gadget/Quick Select/item grid has an explicit **nine-item maximum** per visible page and Next/Previous page buttons; the OBP Development view uses two explicit pages. Help categories use two columns instead of a scrolling list. Other OBP scrollable screens keep scroll-wheel/focus traversal but set `VerticalScrollMode=ShowNever`; the global Godot theme also supplies transparent scrollbar track/grabber styles. None of these screens should display an engine thumb or scrollbar track.
- **Last physical input owns the cursor:** New `UiPointerModality` watches real joypad button events/axis deflections and physical mouse motion. The cursor hides when a gamepad is used in a visible UI, reappears after real mouse movement (or clicking), and hides again on renewed controller input. The existing captured-mouse gameplay camera is left alone. Opening R&C1 pause respects the last input type. Headless tests assert the modality state; the rendered Godot smoke asserts actual `Input.MouseMode`.
- **Source panel artwork:** `Rac1RetailMenuButton` uses the original `E99E` animation frames 7 (panel) and 1 (selection marker) together with native FX textures `0x19` (textured overlay), `0x1A` (animated noise/flash), and `0x1C` (streak/glow). `Rac1UiAssets` reads the three FX images from the authenticated Veldin level core at runtime; they are never committed. Timing, opacity, texture-coordinate choice and dimensions are explicitly *Godot host approximations* from the source drawing functions (`draw_menu_flashing_panel.c`), not pixel-exact claims.
- **Source SFX:** Disc index `0x14E0` supplies the separate global 989snd startup bank (in this NTSC-U disc: LBA 14507, 75 sectors). `Rac1MenuAudioBank` decodes its two short sample-only slots (0 and 1) into playback intents without exporting sound files; `Rac1RetailMenuAudio` uses them as focus/confirm feedback with a per-slot 85ms repeat guard. **These are genuine original-game samples, but their association with particular menu events remains provisional.** The native menu code's `allocate_voice_for_target_entry(entry, 0x11, panel_slots[0])` uses a menu-Moby SoundDef table that has not yet been recovered; `0x11` there is a playback flag, **not** a Moby class 17. Do not describe this playback pairing as retail-exact.
- Long item names display status on their own line; focus and icon semantics remain source-owned. Missing icons continue to show text fallback, not arbitrary sprite replacements.

Regression: the dedicated Godot menu smoke tests real Next/Previous-equivalent pagination with 15 items, no retail `ScrollContainer`, controller/keyboard inputs, HelpDesk, and cursor transitions. `Rac1RetailMenuMediaTests` can validate source FX and two startup voices on machines with `OBP_RAC1_ISO` configured. Retail screenshots and captured audio remain ignored local evidence; none are committed.

Remaining limitations: rotating native Moby previews, exact screen layout/blur and PS2 palette blending, native menu-Moby SoundDef association, gold-equipped icon variant, HelpDesk voice/control markers and true quick-slot editing. These are separate fidelity tasks rather than silently fabricated behavior.
