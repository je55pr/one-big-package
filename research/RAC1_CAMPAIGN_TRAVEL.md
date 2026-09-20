# R&C1 campaign state and travel provenance

Authority: NTSC-U retail `SCUS-97199`, ISO SHA-256
`ab849fe7cc9cc81c487d61b0d3ea15b5849943481b6a6ebf4d9aa9cf7bc40d9d`,
executable SHA-256
`e050581032e4bb3f20341307da5b69b76f1574910519155380ea771e55c3c0c9`.

The reproducible payload-free witness is
`research/generated/rac1-campaign-travel-witness.json`; regenerate it with
`tools/rac1-campaign-probe.py` against an authorized SCUS-97199 savestate and
memory card. The tool emits hashes, addresses, decoded scalar/table values, block
boundaries, and code-reference addresses only.

## Runtime ownership and serialization

SCUS-97199 keeps 16-byte game-save descriptors at `0x001845c0`. The retail
descriptor table directly establishes:

| Save block | Runtime owner | Width | Descriptor |
|---|---:|---:|---:|
| CurrentLevel (0) | `0x0015ed84` | 4 | `0x001845c0` |
| VisitedPlanets (14) | `0x0013dd40` | 20 | `0x001846a0` |
| GalacticMap (20) | `0x0013d510` | 80 | `0x001846b0` |
| per-level Visited (3001) | `0x0013dd58` | 1 per record | `0x001848c0` |

The opening retail savestate has `CurrentLevel=0`, all 20 VisitedPlanets bytes
zero, all 20 GalacticMap entries zero, and per-level state
`[1,0,0,...]`. The map-navigation owner at `0x001602a0` points directly to
`0x0013d510`.

The populated native `save0.bin` on the retained memory card has a
`0x1530`-byte game stream and twenty `0x0aa4` per-level streams. Its exact
campaign blocks are:

- block 0 payload offset `0x18`: CurrentLevel `2`;
- block 14 payload offset `0x260`: VisitedPlanets
  `[0,1,1,1,1,0,...]`;
- block 20 payload offset `0x27c`: GalacticMap
  `[1,2,3,4,0,...]`;
- block 3001 appears in every per-level stream. Its state bytes are
  `[2,2,1,2,0,...]`.

Record `i` begins at `0x1538 + i * 0x0aa4`; its block-3001 byte is at
`0x1548 + i * 0x0aa4`.

## Admission

`0x002607d0` is the retail admission primitive. For destination `d` it:

1. reads `VisitedPlanets[d]` and returns if already nonzero;
2. counts nonzero VisitedPlanets entries across the fixed 20-byte table;
3. writes `d` to `GalacticMap[count]`;
4. writes `1` to `VisitedPlanets[d]`;
5. calls the follow-up at `0x00262d38` only when `d != CurrentLevel`.

Direct callers are `0x0023d16c`, `0x00283340`, and `0x002d6a20`.
The normal progression dispatcher is exact: `0x002832f4` forms
`destination = event - 0x24`; `0x00283330..0x00283338` admits only
`(event - 0x25) < 0x12`; and `0x00283340` calls the admission primitive.
Therefore dispatcher values `0x25..0x36` discover destination IDs `1..18`
one-for-one. These are retained as progression-dispatch values, not relabelled
as mission-completion or checkpoint events without a separate witness.

The initialization path is also explicit: `0x0023d160` loads CurrentLevel,
`0x0023d164` skips admission when it is zero, and `0x0023d16c` otherwise
calls the same admission primitive. This explains the opening snapshot:
CurrentLevel is 0 and per-level state 0 is already `Visited`, while
VisitedPlanets and GalacticMap are entirely zero. Starting on level 0 is
therefore not destination discovery. The third direct caller at `0x002d6a20`
takes its destination from a gameplay-state data field; no stronger semantic
label is claimed here.

This proves the existing `Rac1CampaignState.AdmitDestination` ordering and
idempotence. It also proves that the 20-byte storage capacity is not the valid
destination range: retail gameplay bounds destinations/levels with `<19`.
The model therefore retains 20 serialized slots but accepts IDs only `0..18`.

## Travel and source/target transition

The ship/map UI keeps its selected destination at `0x00184414`. On map setup,
`0x002762e8` loads CurrentLevel and `0x00276320` seeds that selected value
from it. The launch path at `0x00276d38/0x00276d3c` then passes the selected
destination unchanged as argument 0 to travel routine `0x0028ed58`.

Different-level travel is a two-phase handoff, not an immediate CurrentLevel
assignment. The travel routine raises a transition-active scalar at
`0x0015f5d8`, calls transition core `0x0024d430`, then stores the requested
target at `0x0015f5c0`. Inside the transition core, retail snapshots the source
CurrentLevel, temporarily installs the target while swapping/loading per-level
state, restores the target's previous visit byte, and finally restores the
source CurrentLevel at `0x0024d628`. The temporary `0 -> 1` byte write at
`0x0024d544` is therefore transition bookkeeping, not evidence that campaign
visit progression becomes durable at that instruction.

Later, `0x00293034` copies the pending target into the loader selector.
`0x0029341c` reloads the pending target and `0x00293434` is the late
CurrentLevel commit. New-level initialization eventually reaches
`0x00291df8`, which clears the transition-active scalar. The pending-target
storage is not required to be zeroed at that point, so it is semantically live
only while the active flag is set.

Completion remains separate: `0x00293408` prepares value `2`,
`0x0029340c` forms the per-level-state base, and `0x00293414` stores that
byte for the current level, with additional retail gates for IDs 7 and 14.
No VisitedPlanets or GalacticMap mutation occurs in that completion write.

The populated save admits destinations 1, 2, 3, and 4 while CurrentLevel is 2,
so the smallest concrete revisit supported by that save and the proven travel
path is `2 -> 1 -> 2`. Revisit does not append another GalacticMap entry or
change VisitedPlanets.

## Persistence boundary

Destination discovery persists in the game-save blocks VisitedPlanets (14) and
GalacticMap (20). CurrentLevel persists independently in block 0, while the
0/1/2 per-level state persists in block 3001 inside each level record. The
tracked populated memory-card witness proves all four survive serialization
together without collapsing their meanings.

`Rac1CampaignState` now exposes those four fields through
`Rac1CampaignPersistentState`, and restore derives the next GalacticMap append
slot the same way retail does: by counting nonzero VisitedPlanets bytes. The
model deliberately contains no checkpoint field because no checkpoint owner or
serialization block is established by this evidence. Discovery, travel,
completion, and checkpoint state therefore remain separate contracts.

## Destination identity and native level-entry selector

The direct loader bridge closes the former namespace gap. Runtime disc index
base `0x00137b80` contains the 19-pair level table at offset `0x28c8`,
therefore address `0x0013a448`. Loader routine `0x0012f368` multiplies
its argument by eight, indexes that exact table, reads the pair's first word as
the header LBA, reads five sectors, and copies exactly `0x2434` bytes to
`0x0013a4e0`. The late travel handoff calls it at `0x00293444` with the
loader selector as argument 0.

The live table contents match the independently decoded retail disc-index
census byte-for-byte. That census proves table slot `n` points to a native
header whose first word is level id `n`, for all `0..18`. Consequently the
normal campaign destination/current-level/loader identity is the same native
level-id namespace used by `rac1:LEVEL0..18`; this is no longer inferred from
display order or the debug browser.

## Player entry and session boundary

After a native level header selects the target world, ordinary authored player
placement remains the class-0 instance-0 contract documented in
`research/RAC1_VELDIN_SPAWN.md`. Retail gameplay data has exactly one
class-0 Moby at instance 0 for every native level `0..18`; OBP's
`Rac1PlayerStartProvider` uses that authored transform and deliberately does
not substitute the separate ship position. Planet travel therefore selects the
native level first, then level-local authored player-start data supplies the
entry transform.

`Rac1PlanetTravelSession` models only the recovered transient handoff:
map selection starts from CurrentLevel, a different admitted destination becomes
the pending/loader target while the source remains current, CurrentLevel changes
at the late commit, and completion of level initialization clears transition
activity. Persistent discovery, GalacticMap order, completion state, and the
serialized CurrentLevel remain owned by `Rac1CampaignState`; the raw pending
slot is intentionally not treated as durable campaign state.

## Player-facing map presentation boundary

`Rac1PlanetTravelSession.OpenPlanetMap` now returns a
`Rac1PlanetMapSnapshot`: `CurrentLevel`, the selection seeded from that current
level, and only the admitted destination IDs copied from `GalacticMap` in their
recovered admission order. The Godot `Rac1PlanetTravelUi` renders that snapshot
and forwards the chosen native destination ID back to
`Rac1CampaignRuntimeSession.BeginTravel`; it never reads the provider catalogue
to decide what is unlocked.

The current list/menu styling and the OBP `M` / controller Start shortcut are
host presentation choices, not claims about retail pixels or the original ship
menu binding. The normal opening campaign state therefore presents no fabricated
travel targets: level 0 is current but is not auto-admitted, exactly as the
recovered opening save/runtime state requires. Provider availability is checked
only after native campaign admission, at the host loading boundary.

## OBP persistence migration/default policy

`Rac1CampaignSavePolicy` keeps host persistence versioning outside the recovered
`Rac1CampaignPersistentState` payload, so migration metadata cannot become a
second source of campaign truth. Schema version 1 is exactly the four recovered
persistent fields above.

For an OBP save that has no R&C1 campaign payload because it predates this
contract, the field defaults to the recovered opening state: `CurrentLevel=0`,
no admitted destinations, and level 0 in native `Visited` state. Existing
unversioned `Rac1CampaignPersistentState` snapshots migrate to schema v1 without
changing any campaign value. Unknown schema versions are rejected rather than
being guessed or partially defaulted.
