# R&C1 ordinary weapon quick-select icon mapping

Recovered/implemented 2026-10-22 in the isolated `three-big-package` dev clone.

## Evidence and confidence

The resident HUD animation bank exported from the authorised R&C1 NTSC-U original ISO contains a dense run of native sprite animation IDs `0xEA69..0xEA79`. The **sprite animation ID for each named weapon corresponds to `0xEA60 + native item ID`**. Their first-frame imagery independently corroborates the assignments, for example Bomb Glove's bomb, Devastator's rocket, Taunter's loudspeaker, Pyrocitor's flame, Tesla Claw's lightning bolt and Morph-o-Ray's chicken.

**Confidence: high (sequence and visual corroboration), not yet instruction/table exact.** The game's menu UI reads the animation ID from a 0x4C-byte item-info entry at offset 0x38, as established by the reference decompilation's `src/ui/menus/pause/draw_pause_ring.c`, `src/ui/menus/build_filtered_menu_item_list.c`, and `src/ui/menus/draw_menu_item_grid.c`. The table address `0x001863D0` is zero in the executable's file-backed contents and the level-0/planet overlays do not hold its live menu table. This investigation **did not** recover an authoritative initialized menu-state copy; the mapping is independently derived from the animation bank and visual identifications instead. It should be checked against a genuine retail menu-state capture before calling it exact.

No copyrighted sprites are committed: `Rac1HudBank` reads them from the attached user-owned ISO, and Godot's `Rac1RetailUiAssets.Sprite` generates an `ImageTexture` in memory. This runs automatically on source attachment/restoration; the optional `tools/rac1-ui-assets.py` research exporter is not a runtime dependency.

## Mapped named ordinary equipment

| Native item ID | Name | Animation ID | Ordinary frame |
|---:|---|---|---:|
| 9 | Suck Cannon | `EA69` | 0 |
| 10 | Bomb Glove | `EA6A` | 0 |
| 11 | Devastator | `EA6B` | 0 |
| 13 | Visibomb Gun | `EA6D` | 0 |
| 14 | Taunter | `EA6E` | 0 |
| 15 | Blaster | `EA6F` | 0 |
| 16 | Pyrocitor | `EA70` | 0 |
| 17 | Mine Glove | `EA71` | 0 |
| 18 | Walloper | `EA72` | 0 |
| 19 | Tesla Claw | `EA73` | 0 |
| 20 | Glove of Doom | `EA74` | 0 |
| 21 | Morph-o-Ray | `EA75` | 0 |
| 23 | RYNO | `EA77` | 0 |
| 24 | Drone Device | `EA78` | 0 |
| 25 | Decoy Glove | `EA79` | 0 |

Native item 8 is the Wrench. The resident bank has **no `EA68`** animation, consistent with its absence from the standard quick-select wheel. Unknown item IDs 12 and 22 are *not* forced into the typed weapon catalog simply because `EA6C` and `EA76` exist; both can later be used through the generic native item presentation, once gadget semantics are recovered. There are several further `EAxx` gadget/icon families whose gameplay identity isn't fully named.

The retail `draw_menu_item_grid.c` may select frame offset `1` when equipped, `2` when unavailable, or `4` for upgraded variants, dependent on the menu state. Quick Select's `draw_pause_ring.c` uses frame `0` for ordinary and `4` for upgraded. **This implementation maps frame 0 only**; gold-weapon rendering is left for a distinct save/upgrade-state addition, and several animations have fewer than five frames.

## Integration and validation

- Pure native→sprite ID mapping: `src/OBP.RAC1/Presentation/Rac1WeaponIconCatalog.cs`. Only explicit `Rac1WeaponId` variants receive icons. Wrench and unmapped IDs are left null.
- Godot wheel: `game/scripts/WeaponQuickSelectUi.cs`. Draws the decoded native sprite; retains the letter fallback for unmapped equipment or an unavailable source. No retail image is compiled into the executable.
- Tests: `tests/OBP.Tests/Rac1WeaponIconCatalogTests.cs` asserts every named quick-select weapon is covered and IDs are exactly the observed contiguous series, mappings are unique, and unknown IDs have no invented mapping.
- Local disc asset audit: all fifteen mapped animations have frame 0 in the source bank. `Rac1UiAssetsTests` verifies the whole bank against independently generated RGBA hashes; `ControllerSmoke` checks that all fifteen mapped icons become usable Godot textures without exported files.
- Still to validate: side-by-side original retail Quick Select screenshots, exact equipped/upgrade frame selection, and actual fully populated native menu-item table (prefer a read-only offline savestate probe rather than interaction with PCSX2).

## Public reference code considered as evidence only

- [Lombyte](https://github.com/lombyte-project/Lombyte) `src/ui/menus/pause/draw_pause_ring.c`
- `src/ui/menus/draw_menu_item_grid.c`
- `src/ui/hud/get_icon_frame.c`

No decompilation source implementation was copied into OBP.
