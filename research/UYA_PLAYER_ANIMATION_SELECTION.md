# UYA player animation selection

**Authority:** UYA NTSC-U `SCUS-97353`, build `rac3-ntscu-original`.

This note retains the payload-free evidence behind
`UyaRatchetSequenceSelection`. It covers ordinary idle/locomotion, the
ordinary fall selector, recovered combat and damage handlers, and directly
identified death handlers. Special traversal and unresolved player-frame pose
payloads are deliberately outside this contract.

## Dedicated Ratchet carrier

UYA does not animate the playable Ratchet through an ordinary class-local Moby
sequence table. The decoded class-0 Ratchet carrier at asset offset `0xAED6C0`
has **111 joints** and **127 sequences**, while its embedded class-local sequence
table is empty. The core-header `RatchetSeqsOffset` table supplies all 127
non-zero external sequence offsets.

Across those sequences there are **2,968 frame references**. Every reference
uses the high-nibble `0xF` special player-frame encoding. Sequence headers,
frame counts and selector IDs are decoded, but the special frame bodies are not
yet admitted to the ordinary GC/UYA pose decoder. This task therefore recovers
selection semantics without claiming a UYA player-avatar renderer.

## Native setter and state dispatcher

The loaded Veldin overlay identifies `0x50BB98` as the native Ratchet
sequence setter. It reads the live player pointer from player-global
`0x001A4BE0 + 0x25C0` and writes the selected sequence byte to live Moby
`+0x43`, along with native frame/blend state.

The player dispatcher at `0x4FC518` switches on the native player state.
Relevant ordinary cases are:

| Native state | Handler | Recovered category |
|---:|---:|---|
| 0 | `0x4FC674` | idle |
| 2 | `0x4FD054` | ordinary locomotion |
| 3 | `0x4FD474` | locomotion stop/settle |
| 6 | `0x4FDE24` | ordinary fall |
| 7 | `0x4FE004` | jump/shared airborne |
| 19-21 | `0x4FF83C` | ordinary combat family |
| 23-26 | `0x4FDCB8`..`0x4FDDC4` | damage reactions |

States used by grindrails, vehicles, scripted moves and other special traversal
are not promoted here.

## Idle and locomotion

Native idle helper `0x4DC0D0` returns sequence **0** on the neutral ordinary
fallback reached by state 0. The same helper can return contextual alternatives
(among the recovered values are 1, 20, 29, 74 and 85), but their complete
equipment/context predicates are not yet bounded. The production contract
therefore promotes sequence 0 as neutral idle and leaves a contextual idle cycle
unclaimed.

State 2 reads player-global `+0x25C8` and selects:

`sequence = 3 + nativeLocomotionSubstate`

at `0x4FD400` through `0x4FD41C`. Retained ordinary paths establish substate 0/1,
giving sequence family **3, 4**. State 3 separately selects stop/settle family
**5, 6** from native cycle/context state; its exact phase predicate is kept out
of the public selector until bounded.

The controlled L2-right witness proves a distinct strafe facing basis: stable
planar step remains about 0.10283 while facing stays about pi/2 from travel.
That witness did **not** retain `+0x25C8` or live Moby `+0x43`, so this task
does not invent a dedicated strafe clip or alias strafe to 3/4 without evidence.

## Airborne selection

A controlled stationary jump enters native state **7**. The shared airborne
handler contains recovered branches that select sequence **7**, but no retained
live sequence-byte witness proves that every ordinary state-7 frame universally
maps to sequence 7. The contract records the native jump state while leaving a
universal jump-sequence mapping unresolved.

Neutral-mode native fall state 6 has a direct two-way selector. It compares the
native `+0x33C` metric against **1.75**:

- metric <= 1.75 selects sequence **10**;
- metric > 1.75 selects sequence **11**.

Other `+0x25E4` modes select different airborne sequences and are not folded
into the ordinary neutral selector.

## Combat and damage

States 19-21 share the ordinary combat handler but select distinct recovered
families. State 19 computes a three-stage sequence family **23, 24, 25**.
State 20 selects **40**. State 21 selects **26**. The contract preserves these
native state distinctions rather than mapping them onto the host's single
generic `Attack` label.

The four contiguous damage handlers are direct and deterministic:

| Native state | Handler | Sequence |
|---:|---:|---:|
| 23 | `0x4FDCB8` | 32 |
| 24 | `0x4FDD54` | 33 |
| 25 | `0x4FDD90` | 36 |
| 26 | `0x4FDDC4` | 37 |

State 23 changes playback rate from another native metric, but its sequence
remains 32. The other three handlers directly load their sequence IDs before
tailing into the common setter.

## Death selection

Three independently identified death handlers provide stable sequence mappings:

| Native state | Handler | Retail meaning | Sequence |
|---:|---:|---|---:|
| 118 | `0x4FDBC8` | fall/environment death path | 90 |
| 123 | `0x4FDDF8` | lava death path | 11 |
| 127 | `0x500320` | electric death path | 17 |

Other terminal-looking states exist, including state 57 selecting sequence 64,
but their gameplay cause has not been retained strongly enough to promote a
semantic death label here.

## Boundary retained in code

`src/OBP.RAC3/Player/UyaRatchetSequenceSelection.cs` freezes the recovered
native states, sequence families and exact deterministic selectors. Focused
tests live in `tests/OBP.Tests/UyaRatchetSequenceSelectionTests.cs`.

The class intentionally reports four unresolved boundaries rather than filling
them with host heuristics:

- no recovered dedicated strafe sequence;
- no universal state-7 jump-sequence mapping;
- no complete contextual idle-cycle predicate;
- no decoded UYA Ratchet special-frame pose playback.

This keeps source-game animation rules in `OBP.RAC3` and avoids expanding the
generic runtime presentation enum to encode claims the retail evidence does not
yet support.

## Reproducibility anchors

The retained archaeology is payload-free: state/handler addresses, sequence IDs,
counts and scalar thresholds only. Retail bytes, ISO sectors and decoded frame
payloads are not committed. The evidence derives from the authorized local
SCUS-97353 authority and the already-decoded `RatchetSeqsOffset` sequence
headers, plus controlled movement witnesses in
`research/generated/rac3-ntscu-original.uya-movement-witnesses.json`.
