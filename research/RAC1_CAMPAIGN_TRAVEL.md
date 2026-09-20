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

## Travel and per-level mutation

The ship/map UI keeps its selected destination at `0x00184414`. The ship
state path at `0x00276d38` loads that value and calls `0x0028ed58`.
The travel routine compares the destination against CurrentLevel and, when
different, passes it unchanged as the destination argument to transition core
`0x0024d430`.

The transition core stores the selected ID into `0x0015ed84`. For IDs
`<19`, it indexes `0x0013dd58 + id` and promotes state `0 -> 1`; state
`2` is not demoted. Completion is a separate path: `0x00293408` prepares
value `2`, `0x0029340c` forms the per-level-state base, and
`0x00293414` stores that byte for the current level, with additional retail
gates for IDs 7 and 14. No VisitedPlanets or GalacticMap mutation occurs in
that completion write.

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

## Destination identity boundary

Campaign destination IDs are proven to flow unchanged through admission,
planet selection, CurrentLevel, and per-level-state indexing. Separately, the
retail disc index contains 19 native level headers with native level IDs
0 through 18.

This work does **not** yet contain a direct SCUS-97199 loader trace proving
that campaign destination ID `n` selects disc-index/native `LEVELn`.
Accordingly, `Rac1CampaignDestinationIdentity` planet names and the neutral
`rac1:LEVEL0..18` world catalogue remain separate namespaces. No campaign
ordering or bridge is inferred from the debug browser or unit-test examples.
