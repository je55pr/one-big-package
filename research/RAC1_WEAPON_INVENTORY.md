# R&C1 weapon inventory, ammo and equip/session state

## Scope and authority

This recovery covers the reusable R&C1 item/weapon state needed above individual
weapon controllers: the 37-slot item tables, generic ammo accounting, quick-select
storage, persisted equipped-gadget state, and the distinction between persistent
selection state and the currently held runtime item.

Authority is NTSC-U retail SCUS-97199 (rac1-ntscu-original), ISO SHA-256
ab849fe7cc9cc81c487d61b0d3ea15b5849943481b6a6ebf4d9aa9cf7bc40d9d,
executable SHA-256
e050581032e4bb3f20341307da5b69b76f1574910519155380ea771e55c3c0c9.

The reproducible payload-free witness is
research/generated/rac1-weapon-inventory-state.json. Regenerate it with
tools/rac1-weapon-inventory-probe.py against the retained authorized savestates
and memory card. The probe verifies loaded-overlay instruction signatures and save
descriptors, then emits only hashes, addresses and decoded scalar/table values.
No ISO, executable, EE-memory, savestate or memory-card payload bytes are retained.

## Native save-backed layout

The loaded retail save-descriptor table at 0x001845c0 establishes the following
source-game fields:

| Role | Block | Runtime owner | Serialized width |
|---|---:|---:|---:|
| ammo counts | 9 | 0x0013d428 | 148 bytes = 37 x s32 |
| item/ownership bytes | 10 | 0x0013d4c0 | 37 bytes |
| secondary item/unlock flags | 11 | 0x0013d4e8 | 37 bytes |
| quick-select entries | 13 | 0x00141ea0 | 32 bytes = 8 x s32 |
| previous/last equipped gadget | 21 | 0x0015ed8c | 4 bytes |
| equipped-gadget entries | 32 | 0x00141660 | 28 bytes = 7 x s32 |

These widths are native save fields, not host-selected capacities.

### Ownership versus the second 37-byte array

Retail code distinguishes the two 37-byte arrays, but the available populated
save does not contain a state where their values differ.

The acquisition path rooted at 0x00260860 computes item-indexed addresses for
both arrays:

- 0x0026086c forms block-10 base 0x0013d4c0;
- 0x002608a0 forms block-11 base 0x0013d4e8;
- 0x002608cc writes 1 to the block-11 byte;
- 0x002608e4 reads the block-10 byte and skips the one-time acquisition path
  when it is already nonzero;
- on first acquisition, 0x00260904 writes 1 to block 10 before ammo and
  quick-select consequences.

This makes block 10 the reproducible ownership/first-acquisition gate. Block 11
is a separate persistent acquisition/availability flag and is retained as an
UnlockFlag in OBP. The historical Items/Unlocks labels are consistent with that
access pattern, but OBP does not use block 11 as an equip-admission rule: the
retained populated states have block 10 and block 11 identical, so no retail
witness yet proves what gameplay transition can separate them.

The controller code-entry/unlock router at `0x002831c0` provides independent
corroboration: `0x00283308..0x00283328` directly writes 1 to both arrays for
one of its low action-id ranges rather than collapsing them into one field.
This path is code-entry handling, not evidence for ordinary campaign
progression dispatch.

## Generic item descriptor and ammo contract

The item descriptor table begins at 0x001c40b0 with stride 0x18, indexed
directly by native item id. Three fields are relevant to generic ammo state:

- +0x08: nonzero gate used by the first-acquisition ammo branch;
- +0x0e: generic maximum-ammo field;
- +0x12: first-acquisition ammo floor.

On first acquisition, 0x0026090c..0x00260934 checks +0x08, reads the current
ammo slot, reads +0x12, and stores the larger of current ammo and the descriptor
floor. A zero-initialized slot therefore receives the descriptor value, while a
larger pre-existing count is preserved.

The generic helpers then establish the reusable runtime rules:

- 0x00233db8 consumes ammo. If descriptor +0x0e is zero, the operation succeeds
  without decrementing; otherwise it requires enough rounds and subtracts the
  requested amount.
- 0x00233e40 adds ammo to the raw 37-slot table. It clamps to +0x0e only when
  that maximum is nonzero; a zero maximum does not apply a clamp.
- 0x00233e98 queries ammo. For a nonzero maximum it returns the table value;
  for zero-maximum items it returns the retail ammo-free sentinel 1.
- passing item id -1 to the consume/query helpers resolves the item through the
  current-item mirror at 0x00140408.

The ammo-bearing descriptor rows recovered from the complete 37-slot table are:

| Native item | First-acquisition floor | Maximum |
|---:|---:|---:|
| 10 | 10 | 40 |
| 11 | 10 | 20 |
| 13 | 10 | 20 |
| 15 | 100 | 200 |
| 16 | 120 | 240 |
| 17 | 25 | 50 |
| 19 | 120 | 240 |
| 20 | 3 | 10 |
| 23 | 25 | 50 |
| 24 | 3 | 10 |
| 25 | 10 | 20 |

All other descriptor rows have zero +0x0e and zero +0x12 in this build.
Those numeric ids are intentionally not assigned weapon names here. Weapon-specific
class, projectile, damage and unusual resource behaviour remain separate recovery
lanes.

Item 10 is the already-recovered Bomb Glove. Its generic descriptor gives
maximum 40 and first-acquisition floor 10; the early Veldin witness happens
to contain six rounds because that save/session is already in progress.

## Quick-select state

Save block 13 is an eight-entry s32 array at 0x00141ea0.

The first-acquisition routine contains an eight-entry insertion path at
0x00260a14..0x00260a60: for acquisitions that reach that branch it scans for
the first zero slot and stores the acquired native item id there. The retail
weapon-selection UI independently reads this same table at
0x00238770..0x00238794, then indexes the item descriptor and ammo table from
the selected quick-select entry.

The opening Veldin witness has:

[10, 0, 0, 0, 0, 0, 0, 0]

The populated save0.bin and retained resume witness have:

[10, 16, 15, 12, 0, 0, 0, 0]

OBP therefore preserves quick-select as native persistent state. It does not infer
a trilogy-wide quick-select abstraction or names for ids 12/15/16.

## Persisted equipped gadgets versus current held item

Retail has at least two distinct layers of selection state.

Save block 32 is seven s32 entries at 0x00141660. The level/player restore
path references this base at 0x002769e4 and transfers active front entries into
player equipment state. The populated save and retained resume witness contain:

[12, 0, 0, 2, 0, 0, 0]

Save block 21 is a single s32 at 0x0015ed8c. Switch code writes previous
gadget selection to this field at 0x0021047c and 0x002104fc. The populated
save/resume value is 16.

Neither field is the same thing as the currently held runtime item.

Two independent live current-item witnesses remain at 0x00140408 and
0x00141424. Their separation from persisted gadget state is directly observable:

| Witness | persisted gadget slot 0 | live current-item mirrors |
|---|---:|---:|
| opening Veldin savestate | 10 | 8, 8 |
| retained resume savestate | 12 | 12, 12 |

Thus holding the wrench (8) can coexist with remembered/persisted gadget 10.
The current item is session state and must not be serialized merely because the
live mirrors are convenient to read.

The two mirror addresses are retained as runtime witnesses, not promoted as a
canonical save layout. Their equality under controlled selection changes is useful
corroboration, but block 32/block 21 are the actual save-backed fields recovered here.

## Populated save witness

The retained memory-card save0.bin has the following inventory state:

- nonzero ownership/item bytes: ids 2, 10, 12, 15, 16;
- nonzero secondary unlock flags: ids 2, 10, 12, 15, 16;
- ammo: item 10 = 40, item 15 = 200, item 16 = 190, all other slots zero;
- quick select: [10,16,15,12,0,0,0,0];
- previous/last equipped gadget: 16;
- equipped gadgets: [12,0,0,2,0,0,0].

The retained resume savestate contains the same six save-backed fields and has live
current-item mirrors [12,12]. The generated witness pins both hashes and values.

The opening Veldin savestate is intentionally different and provides the useful
session split: item 10 is the only nonzero ownership/unlock byte, quick-select and
persisted gadget slot 0 both contain 10, ammo[10] is 6, yet the live current item
is wrench 8.

## OBP promotion boundary

Rac1WeaponInventory now preserves the source-specific 37-slot ammo/item/unlock
shape, eight quick-select entries, block-21 previous/last gadget value, seven
persisted equipped-gadget entries, and a separate transient CurrentItemId.

The generic model exposes the recovered descriptor gate/floor/cap fields,
descriptor-backed raw ammo consumption/addition, and the common acquisition prefix
that sets the secondary flag, first-acquisition ownership byte, and gated ammo
floor. It intentionally stops before the later quick-select insertion because that
branch's complete admission predicate remains unresolved.

It intentionally keeps weapon-use consequences narrow:

- native id 8: Wrench, ammo-free;
- native id 10: Bomb Glove, generic ammo plus its separately recovered firing,
  projectile and damage controller;
- other ids: persistent inventory/ammo state may be retained, but no firing,
  damage, projectile, acquisition presentation or weapon name is invented.

Godot keyboard 1/2 selection remains a host-only convenience. Retail
quick-select persistence and current-item state are now represented without
claiming that those keys are native input semantics.

The live host now persists the six recovered weapon/inventory save blocks beside
campaign state in host schema 2. Missing host state and older campaign-only schema
1 files use the retained opening Veldin witness explicitly: item 10 owned/unlocked,
six rounds, quick-select/equipped-gadget slot 0 = 10, and transient current item =
wrench. After that migration boundary, acquisition flags and generic ammo grants
persist immediately, and each accepted Bomb Glove shot persists the post-consumption
ammo table, so player-visible ammo no longer resets on a later host reload.

Two host entry points deliberately consume only already-identified native facts:
`ApplyRac1ItemAcquisition(nativeItemId)` runs the recovered common acquisition
prefix, while `ApplyRac1AmmoGrant(nativeItemId, amount)` runs the recovered generic
clamped add helper and reports the effective post-cap delta to HUD presentation.
Neither entry point claims where a shop, script, crate or loose pickup obtains its
item id or amount.

Class 511 remains only an authored ammo-crate family at this boundary. Its break
requirements, refill target policy, refill amount, emitted resource/pickup identity
and collection semantics are not recovered here, so production does not connect
class 511 to the generic ammo-grant entry point. Vendor prices/payment and the
acquisition routine's conditional quick-select insertion likewise remain outside
the promoted contract.

## Verification

Rac1WeaponInventoryTests freezes the source-specific layout, all recovered ammo
capacities/floors, save/session separation, generic ammo clamp behaviour, and the
existing Wrench/Bomb Glove compatibility path.

tests/tooling/test_rac1_weapon_inventory_probe.py freezes the generated witness
shape and the two retained state comparisons. The generator itself fails if any
promoted save descriptor or loaded-overlay instruction signature changes.
