# Trilogy weapon board

The ordinary player Weapons menu is an OBP-authored 7 by 7 board: 47 weapon families, one OmniWrench family and one spare cell. All families remain visible. Returning copies and native upgrade forms share a display cell; RYNO, RYNO II and RY3NO remain separate families. Glove of Doom includes the Agents of Doom/Dread line, and Lava Gun includes Meteor Gun and Liquid Nitrogen Gun.

`PlayerWeaponMenuRoster` is presentation grouping over existing `SourceWeaponKey` identities. It does not register weapons, create ownership, combine ammunition, unlock unavailable implementations, or replace source inventories. Equipping re-reads the real equipment catalog and calls its existing port with the resolved source-qualified catalog ID, preferring usable members before equipped/selected members. Unregistered families show “Not implemented yet”; registered unavailable entries use generic availability messages, with detailed admission reasons in debug mode. The currently implemented RAC3 V5 Shock Cannon appears under Shock Blaster, with its actual name in the inspector.

The board has directional controller neighbors across seven columns. Unavailable cells remain focusable for inspection; the spare cell is not focusable. Cross/Enter equips an admitted weapon, and the inspector offers the same action. Other sections retain their existing paging and the eight-direction Quick Select remains independent of board positions. Native retail diagnostic menus retain their recovered layouts.

## Icons and provenance

All three supported sources are required in normal play. Missing expected glyphs or failed source UI decoding are fatal errors, not partial-icon fallback cases. The RAC1 adapter retains its source-specific bank admission (0/1/4 to chunks 1/2/5); other bank references are rejected.

No recovered PNGs or exported sprites are distributed. Source-owned readers locate the supported RAC2 global HUD and RAC3 boot HUD; RAC1 retains its existing global HUD reader. `Ps2HudSpriteBank` owns only the shared bounded animation/frame/palette/image codec, returning neutral decoded PS2 pixels. Disc identity, container addresses and native item-to-glyph bindings remain in the respective source projects.

The Godot presentation caches 128 by 128 white masks in memory for each attached source. Detaching or invalidating a source clears that source's icon cache. RAC2/3 masks retain the original alpha; RGB becomes white. The fifteen RAC1 normal weapon glyphs expose complementary portions of their shared blue gradient. An authored matte estimates glyph coverage against that background, retains soft edges, removes isolated quantization speckles and fits the result with an eight-pixel safe margin. This cleanup is OBP presentation, not a recovered retail alpha channel. Its rule matches the local icon-catalog preview.

RAC1 has no native EA68 wrench icon. The authored board uses the explicitly OBP-authored `game/assets/ui/obp-wrench.svg`; `Rac1WeaponIconCatalog` and native Quick Select do not invent that missing native glyph. All expected native weapon glyphs must decode successfully; no other weapon's picture is used as a substitute.

The supporting local survey inspected 19 RAC1, 27 RAC2 and 51 RAC3 level HUD banks, the RAC1/RAC2 global banks and RAC3 boot bank. Menu source facts are pinned to SCUS-97199 original, SCUS-97268 NTSC-U v1.01 and SCUS-97353 original. Captures and exported images remain ignored local artifacts.
