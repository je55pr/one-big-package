# Going Commando player animation state selection

**Authority:** NTSC-U v1.01 `SCUS-97268`, SHA-256
`9db2e33e…a9b1ce5`, Oozla `LEVEL1.WAD`.

This note records player-animation selection recovered from the loaded Going
Commando retail executable. It does **not** transfer R&C1 Ratchet sequence roles
to GC. Public `librac2` names are used only to label numeric player states after
the numeric state table was recovered independently from retail.

## Native carriers

GC has a dedicated player sequence table separate from ordinary Moby-class
sequence lists:

- `GcLevelCore.Header.RatchetSeqsOffset` points at a 256-entry `s32` offset
  table in the level-core index.
- Oozla's table is at index offset **0x80E0** and has **102 non-zero slots**.
- Sequence payloads live in the core asset blob. Their header byte `+0x10` is
  the frame count, matching the established GC/UYA Moby sequence container.
- The loaded player Moby pointer is the existing player-global `+0x2290`.
- Native helper **0x002C7928** is the recovered player sequence setter. At
  **0x002C7A08** it writes the requested sequence id to player-Moby byte
  **+0x43** after setting up transition metadata.
- A second player-specific helper at **0x002C7B80** can restore an exact
  sequence/frame and writes `+0x43` at **0x002C7BD8**. Its only retail callers
  are player initialization (`0x002A6EE4`) and a reset path (`0x002B9A10`)
  which re-applies the already-current sequence/frame.
- A full aligned-write census of the Oozla overlay finds 15 `sb *, +0x43(*)`
  sites. Outside the two player helpers they are explicit generic setters,
  animation-state copies, object initialization, or level-object code; there is
  no automatic clip-end sequence-id reassignment writer.

The state-change routine is **0x002BEB90**. It stores the new state at
player-global `+0x2294`, then indexes the 148-entry state-initializer jump
table at **0x002A1F30**. The ordinary states below are therefore tied to both a
retail state number and a retail init destination.

Corroborative names come from
`Metroynome/librac2 include/player.h` at commit
`04f9740b136d6fa91b3892c06c5ba0379935fa5a`: 0 idle, 2 walk, 3 skid,
4 crouch, 6 fall, 7 jump, 8 glide, 19 combo attack, 20 jump attack,
21 throw attack, 22 get hit, 29 targeting, 30 gun waiting, 57 death.
The names are not used as evidence for sequence ids.

## Recovered selector map

| State | Retail init | Recovered sequence rule |
|---|---:|---|
| 0 idle | `0x002BED6C` | Calls the weapon-context selector `0x002A7248` and passes that result to the animation setter. With no special weapon context the helper falls back to **0**. Idle is therefore context-sensitive, not a universal fixed-0 claim. |
| 2 walk | `0x002BF010` | Selects **3**. The complete state-2 update handler (`0x002BBCDC..0x002BC66F`) calls none of the player/generic sequence setters, and the full `+0x43` writer census has no automatic clip-end reassignment path. Sequence **3 therefore remains selected while ordinary state 2 persists**; GC sequence 4 is not assigned an R&C1 role by analogy. |
| 3 skid | `0x002BF338` | Chooses **5** only when helper `0x002A72C8` returns 4 and native scalar `+0xC38` is strictly `3 < x < 16`; otherwise chooses **6**. |
| 4 crouch | `0x002BF48C` | Crouch entry can select **13** when its transition-source check agrees with the current weapon-context base sequence. In the shared state handler, directional crouch movement switches to **15** when native scalar `+0x1A4 < 0`, otherwise **14**, once the movement-activation predicate has fired. |
| 6 fall | `0x002BF718` | Native scalar `+0x31C > 1.75` selects **11**; all other values select **10**. |
| 7 jump | `0x002BF990` | Ordinary Cross jump selects **7**. The direct transition path loads sequence 7 at `0x002C696C`, calls the player sequence setter at `0x002C6974`, then writes state 7 at `0x002C697C..0x002C6980`. A deterministic Oozla input-recording/PINE witness independently observes state **0 / sequence 0 -> state 7 / sequence 7** on the Cross transition. |
| 8 glide | `0x002BF91C` | Selects **19**. |
| 19 combo attack | `0x002C0A48` | Shared combat initializer derives a modulo-three combo stage and selects **23 + stage**, i.e. **23/24/25**. |
| 20 jump attack | `0x002C0A48` | The state-20 branch selects **43**. This corrects an earlier scratch interpretation that had swapped the state-20/state-21 clip roles. |
| 21 throw attack | `0x002C0A48` | The state-21 branch selects **26**. Context changes the transition-rate argument, not this sequence id. |
| 22 get hit | `0x002C126C` | Selects **16**. |
| 29 targeting | `0x002C165C` | Entry reuses the weapon-context base selector. State 29's targeting-only block and the shared movement tail issue no sequence write; the state therefore **preserves that context-selected sequence while strafing/targeting**, with no fixed left/right strafe sequence id promoted. |
| 30 gun waiting | `0x002C16A4` | Entry also reuses the weapon-context base selector and the shared handler does not replace it with a fixed GC clip id. |
| 57 death | `0x002BEEE0` | Selects **69**. |

The targeting finding is the important GC strafe boundary. GC does have
targeting-specific movement handling: state 29 takes the special path at
`0x002BA294..0x002BA36C` in the shared player handler. That block contains no
sequence-setter call, then state 29 skips the state-4-only crouch selector block
at `0x002BA394..0x002BA523` and enters a shared tail with no sequence setter.
The full `+0x43` writer census independently rules out an automatic clip-end
sequence-id swap. Ordinary targeting movement therefore preserves the
weapon-context sequence selected on entry; assigning fixed left/right strafe
sequence ids would be an invention.

## Deterministic ordinary-jump witness

The retained local GC authority state was replayed with PCSX2 2.6.3 input
recording and PINE, using only payload-free results in Git. The savestate SHA-256
is `7d787fd8fe82613fe7e0c9fa39d003e9bc5cc0e2005014a477b7c3e83b86abaf`.
The generated 92-frame movie SHA-256 is
`53bf52c4e83a8c0dbca9296ae139a1555026145758def81cb4efb79c97ec7659`:
4 neutral frames, 8 frames holding Cross, then 80 neutral-release frames.

The initial witness is player **state 0 / sequence 0**. At movie frame **5**,
while Cross is held, retail changes directly to **state 7 / sequence 7** with
previous-sequence byte `0xFF`. By frame **10** the transition settles to
previous/current sequence **7/7**, and the observed trace contains only states
`0,7` and sequences `0,7`. This independently matches the executable handoff
at `0x002C696C..0x002C6980`.

## Dedicated sequence witnesses

The Oozla Ratchet table contains the mapped slots with these retail frame counts:

| Sequence | Frames | Recovered role |
|---:|---:|---|
| 0 | 10 | default no-special-context idle/base |
| 3 | 33 | ordinary state-2 walk |
| 5 | 13 | skid conditional variant |
| 6 | 13 | skid default variant |
| 7 | 29 | ordinary Cross jump |
| 10 | 1 | fall low branch |
| 11 | 6 | fall high branch |
| 13 | 15 | crouch |
| 14 | 7 | crouch non-negative directional branch |
| 15 | 7 | crouch negative directional branch |
| 16 | 18 | get hit |
| 19 | 9 | glide |
| 23 | 21 | combo stage 0 |
| 24 | 22 | combo stage 1 |
| 25 | 35 | combo stage 2 |
| 26 | 45 | throw attack |
| 43 | 13 | jump attack |
| 69 | 25 | ordinary death |

Frame counts are asset-presence witnesses only. They are deliberately not used
as selector predicates or playback timing rules.

## Retained contract

`OBP.RAC2.Player.GcRatchetSequenceSelection` exposes only the facts above.
The recovered persistence and unresolved boundaries stay explicit:

- `WalkSequenceId == SustainedWalkSequenceId == 3`
- `JumpSequenceId == JumpLaunchSequenceId == 7`
- `TargetingPreservesContextSequence == true`
- `TargetingDirectionalSequenceId == null`
- `GunWaitingPreservesContextSequence == true`
- `GunWaitingSequenceId == null`

That boundary prevents the current cross-game R&C1 avatar fallback from becoming
accidental evidence for GC semantics. A future GC avatar/provider task can bind
these selectors to decoded GC-native player assets without changing their
provenance.

## Reproduction

With `OBP_GC_ISO` pointing at the authorized v1.01 retail image:

```text
dotnet test tests/OBP.Tests/OBP.Tests.csproj --filter GcRatchetSequenceSelectionTests
```

The retail-gated test reads `LEVEL1.WAD`, verifies the state-init and
state-update jump tables, checks Oozla's 256-slot Ratchet table has 102 populated
entries, verifies the mapped slots' frame counts, freezes all 15 aligned
`Moby+0x43` sequence-write sites, proves the ordinary walk/targeting persistence
boundaries, and pins the sequence-7/state-7 instruction handoff at
`0x002C696C..0x002C6980`. No retail bytes, savestate bytes, movie bytes or asset
payloads are committed. The payload-free normalized evidence is retained in
`research/generated/gc-ratchet-animation-states.json`.

## Remaining boundary

The ordinary selector categories requested here are now source-backed: idle/base
context, walking, skid/stop, crouch directional movement, jump/fall/glide,
ordinary attacks, hit reaction, targeting movement and death. GC-native player
model/avatar admission and production playback remain the separate avatar
recovery/integration boundary; special traversal and unenumerated weapon-specific
presentation are not promoted by this selector contract.
