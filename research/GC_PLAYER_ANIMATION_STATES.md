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
- Native helper **0x002C7928** is the recovered sequence setter. At
  **0x002C7A08** it writes the requested sequence id to player-Moby byte
  **+0x43** after setting up transition metadata.

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
| 2 walk | `0x002BF010` | Ordinary entry reaches **3**. No later fixed sustained-walk sequence has been proved from the state-2 handler; specifically, GC sequence 4 is **not** assigned the R&C1 sustained-locomotion role by analogy. |
| 3 skid | `0x002BF338` | Chooses **5** only when helper `0x002A72C8` returns 4 and native scalar `+0xC38` is strictly `3 < x < 16`; otherwise chooses **6**. |
| 4 crouch | `0x002BF48C` | Crouch entry can select **13** when its transition-source check agrees with the current weapon-context base sequence. In the shared state handler, directional crouch movement switches to **15** when native scalar `+0x1A4 < 0`, otherwise **14**, once the movement-activation predicate has fired. |
| 6 fall | `0x002BF718` | Native scalar `+0x31C > 1.75` selects **11**; all other values select **10**. |
| 7 jump | `0x002BF990` | The shared jump-family initializer does not directly call `0x002C7928`. The ordinary jump-launch sequence is therefore **unresolved** rather than guessed from R&C1. |
| 8 glide | `0x002BF91C` | Selects **19**. |
| 19 combo attack | `0x002C0A48` | Shared combat initializer derives a modulo-three combo stage and selects **23 + stage**, i.e. **23/24/25**. |
| 20 jump attack | `0x002C0A48` | The state-20 branch selects **43**. This corrects an earlier scratch interpretation that had swapped the state-20/state-21 clip roles. |
| 21 throw attack | `0x002C0A48` | The state-21 branch selects **26**. Context changes the transition-rate argument, not this sequence id. |
| 22 get hit | `0x002C126C` | Selects **16**. |
| 29 targeting | `0x002C165C` | Entry reuses the weapon-context base selector. The shared movement handler has no recovered fixed directional sequence write for state 29, so no dedicated strafe clip id is promoted. |
| 30 gun waiting | `0x002C16A4` | Entry also reuses the weapon-context base selector; no fixed clip id is promoted. |
| 57 death | `0x002BEEE0` | Selects **69**. |

The targeting finding is the important GC strafe boundary. GC does have
targeting-specific movement handling: state 29 takes the special path at
`0x002BA294..0x002BA36C` in the shared player handler. But that path does not
directly select a fixed directional animation through `0x002C7928`. The only
direct sequence writes in the nearby shared locomotion section are the crouch
14/15 selectors guarded by state 4. Until a lower animation layer or another
selector is recovered, representing GC targeting as a fixed left/right strafe
clip would be an invention.

## Dedicated sequence witnesses

The Oozla Ratchet table contains the mapped slots with these retail frame counts:

| Sequence | Frames | Recovered role |
|---:|---:|---|
| 0 | 10 | default no-special-context idle/base |
| 3 | 33 | walk entry |
| 5 | 13 | skid conditional variant |
| 6 | 13 | skid default variant |
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
Context-dependent or unresolved categories stay explicit:

- `SustainedWalkSequenceId == null`
- `JumpLaunchSequenceId == null`
- `TargetingDirectionalSequenceId == null`
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

The retail-gated test reads `LEVEL1.WAD`, verifies the state-init jump-table
destinations above, checks Oozla's 256-slot Ratchet table has 102 populated
entries, and verifies the mapped slots' frame counts. No retail bytes or asset
payloads are committed. The payload-free normalized evidence is also retained in
`research/generated/gc-ratchet-animation-states.json`.

## Remaining boundary

The next useful archaeology is narrow: find the exact ordinary jump-launch
sequence handoff and determine whether sustained walk or targeting strafe uses a
lower-layer animation selector rather than the recovered `0x002C7928` path.
Those are selector questions only; GC player model/avatar admission remains the
separate player-avatar recovery task.
