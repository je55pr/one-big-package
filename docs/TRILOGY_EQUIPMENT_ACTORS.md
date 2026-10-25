# Shared equipment, audio, pause UI and native actors

This implements #271, #272, #259 and the bounded mixed encounter in #256.
The ownership rules remain in [PLAYER_WORLD_CONTRACT.md](PLAYER_WORLD_CONTRACT.md).
Ordinary play uses the persistent selected player and catalogue. `--retail-parity`
retains the original R&C1 menus and source-world diagnostic routes.

## Equipment and sound ownership

An equipment binding contains the existing `SourceWeaponKey`, original
game/build/model identity, actual avatar identity, and measured hand socket.
The original R&C1 attachment tracks and compatible GC/UYA skeletal layers remain
responsible for poses and muzzle positions. A missing model, source or compatible
rig produces a catalogue reason rather than an advertised working attachment.

The ordinary R&C1 avatar's shared effect/audio library is its authentic LEVEL0
bank, prepared once per attached source stamp. World pickups keep their own
level banks. Equal texture, class and sound numbers in those banks are separate
identities. Foreign/unclassified contact surfaces use LEVEL0 dry footsteps as an
explicit **OBP fallback**; this does not claim a recovered GC/UYA material map.
World ambience and music keep their destination-owned lifetimes.

The native Pyrocitor coordinate-cube cull is retained in explicit retail parity.
Ordinary shared worlds use their terrain contacts to limit flame reach and the
original particle lifetimes to retire flames; R&C1's coordinate range cannot
discard an equipped weapon's effect merely because the Lab uses different coordinates.

Player audio traces identify source, build, bank level, class, local sound,
level-table versus class-table lookup, looping and native player tick. Playback
variation uses a separate random stream from native simulation. Foreign loop
owners include the source weapon and sound number. Pause suspends existing
voices; equipment changes, death and lease teardown stop the relevant owners.

Effects continue through the existing native weapon/effect sessions and
`WeaponEffectIntent`/shot identities. Their Godot nodes belong to the current
world lease; held views belong to the current actual avatar. The persistent
session retains selection and ammunition, never the old scene's projectiles,
beams or voices. Future emitter research can replace presentation through these
same source-qualified boundaries without changing loadout or damage ownership.

## Common pause interface

Weapons, Gadgets, Appearance and Quick Select consume the single #255 catalogue
and action ports. Lists page six entries at a time and scroll within the window.
L1/R1 change sections; D-pad/stick navigate; Cross inspects/confirms; Start or
Back resumes. Mouse controls use the same actions. Settings and Developer retain
their existing services. Developer includes individual Lab actor resets.

The appearance preview uses the actual admitted R&C1 model. GC/UYA providers and
unrecovered gadgets remain disabled with reasons. Original icons are used where
available; the fallback SVG is original OBP UI art and is identified in tooltips.
The menu owns no second inventory or source campaign save state.

## Version-1 actor placement and native adapters

`ActorPlacement` is an OBP-authored rigid anchor plus source/build/asset,
source level/class/optional original instance, initial native state, named context,
adapter, scenario group and schema version. Placement preserves the native
model's authored local scale/orientation. An `ActorRuntimeSet` belongs to one
world lease. Actors have source-qualified target keys and fresh runtime indices;
retired keys cannot be reused. Update groups execute once per 60 Hz tick in
ordinal source order, then actors in source/class/instance order.

Decoded actor libraries and animations are retained across world leases. Changed
source stamps evict obsolete versions, and source reattachment invalidates the
caches. World teardown retires live actors without repeating the source decoding.

The Lab's three recipes are deliberately narrow:

| Actor | Original context | Relocation policy |
| --- | --- | --- |
| R&C1 class 749 | LEVEL0 representative with native HP 1, state 5 | OBP supplies a new home and player target; original steering, pose clock, attack, reaction, death and reward sessions run. |
| GC class 2827 | LEVEL0 instance 205, authored mode 1 | OBP begins at witnessed resumed chase/state 12, bypassing the original lift/door introduction. The witnessed approach ramp, native strict attack admission, attack aggregation, cooldown and terminal consumer remain source-owned. |
| UYA class 500 | TABLE1 instance with the recovered deactivation route | Original PVars and state-1 damage admission select state 3/inactivation. No roaming UYA enemy is invented. |

GC chase 11, attacks 16/27 and recovery 17 come from the opening-pair witness in
[GC_ARANOS_OPENING.md](../research/GC_ARANOS_OPENING.md). Clips use original
vertices, skeleton and frame speeds. The relocated GC adapter uses an explicitly
OBP planar facing/contact projection, not a claim to the entire native physics
engine. Contact bodies use genuine model bounds as an OBP collision proxy.
Missing recipes, authored payloads or source assets fail visibly without a dummy
actor. Adding another admitted family extends the source adapter registry and
native importer, not a planet-specific scene override.

Weapon queries retain source-qualified target identity. Lancer/Cannon, Bomb
Glove blasts and wrench sweeps reach each registered victim's own consumer.
Original damage amounts pass through unchanged; OBP constructs victim-owned
query masks rather than reinterpreting foreign flags. The bounded class-749
consumer currently admits lethal HP-1 reactions only: Lancer's 0.5 sublethal hit
reports unsupported instead of inventing a reaction or rounding its damage up.
R&C1's other arsenal routes keep their existing native target coverage.

Enemy attacks use the persistent #270 player health policy: the recovered integer
source consequence enters the selected R&C1-derived victim controller. Native
protection, one shared life and attack receipts remain shared across sources.
Actor resets allocate a new identity, retire old damage registrations, discard
uncollected actor-owned pickups, and stop owned voices/collision. Collected
currency is persistent player state. R&C1 native rewards remain once per actor
life. Wider GC/UYA reward scatter, hit presentation and enemy sound recovery are
still bounded by the original adapters; this change does not fabricate them.

## Verification and human playtest

`--trilogy-gameplay-smoke` is an ordinary-session headless diagnostic over
Veldin, Oozla, UYA Veldin and the Lab. A common contact deck checks identical
physics-clock movement/jump/wrench audio intents, menus, the three main weapon uses,
Pyrocitor flame rendering and release cleanup,
and native ammunition. The Lab then checks simultaneous native motion/animation,
shared player damage, real Cannon/Lancer/Bomb contacts, rejected native contexts,
independent resets, pickup/audio ownership and complete lease cleanup.

`controller-smoke` exercises the common UI through real Godot input events,
including a future 66-weapon fixture, mouse/controller paging, unsupported
appearances, lost sources, eight-direction assignment and a 640x360 window.
The asset-free CI bootstrap includes this UI regression. Desktop automation
remains unfocusable and cannot change the operating-system mouse mode.

All automated runtime checks for this work are **headless**. Visual and audible
ordinary-control confirmation is a human playtest, not claimed by those checks:

1. Enter the Lab with all three sources attached. Walk into Test Chamber Alpha;
   both hostiles should move and attack while the UYA target remains separate.
2. Pause, browse all weapon pages, equip the Cannon, resume and defeat the toad.
   Use the Lancer against the GC hostile and Bomb Glove against the UYA prop.
3. Check held arms, idle/run/jump transitions, muzzle origins, sounds and hit
   presentation. Square should leave the wrench held until another weapon draw.
4. Under Pause > Developer, reset each actor independently. Check for old
   colliders, leftover pickup duplicates or continuing voices.
5. Assign a foreign weapon to Quick Select direction 8; resume and use the
   wheel. Toggle a supported pack, inspect disabled gadgets/appearances, resize
   the menu and open/return from Controller Settings.
6. Travel through Veldin, Oozla and UYA Veldin. The same selected weapons,
   ammunition, movement and Ratchet sounds should persist. Old projectiles,
   beams, actors and voices should disappear. Review appearance/muzzle/sound
   quality in each world before merge.
