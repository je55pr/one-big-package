# R&C1 Ratchet player animation/state admissions

Scope: NTSC-U `SCUS-97199`, using the user-owned retail ISO pinned by `research/manifests/rac1-ntscu.json`. This is an evidence admission map, not an animation-name guess list.

## Result

The preserved evidence still only admits the neutral family. No forward-locomotion, jump, fall, landing, wrench, turn, or crouch-turn sequence id is safe to put into the playable-avatar state machine yet.

| Runtime state | Native seq | Native timing | Evidence | Admission |
| --- | ---: | --- | --- | --- |
| standing | 0 | 10 frames, constant 0.125 = 7.5 FPS | live class-0 selector + decoded asset | **ADMIT** |
| delayed idle/fidget A | 1 | 77 frames, variable per-frame rate | live neutral selector + decoded asset | **ADMIT, neutral only** |
| delayed idle/fidget B | 2 | 77 frames, variable per-frame rate | live neutral selector + decoded asset | **ADMIT, neutral only** |
| bind-linear anchor | 122 | 21 frames, constant 0.5 = 30 FPS | decoded asset/rest archaeology | diagnostic only |
| forward locomotion | unknown | unknown | no controlled class-0 live witness | **DO NOT ADMIT** |
| jump rise | unknown | unknown | no controlled class-0 live witness | **DO NOT ADMIT** |
| apex / fall | unknown | unknown | no controlled class-0 live witness | **DO NOT ADMIT** |
| landing | unknown | unknown | no controlled class-0 live witness | **DO NOT ADMIT** |
| Square wrench attack | unknown | unknown | no controlled class-0 live witness | **DO NOT ADMIT** |
| turning | unknown | unknown | no controlled class-0 live witness | **DO NOT ADMIT** |
| crouch-turn | unknown | unknown | no controlled class-0 live witness | **DO NOT ADMIT** |

Sequences 1 and 2 deliberately keep the coarse “delayed idle/fidget” names. Existing untouched Veldin evidence establishes the neutral selector cycle `0 -> 2 -> 0 -> 1`, but does not justify finer semantic names for the two 77-frame clips.

## Evidence boundary

Three evidence classes stay separate:

1. **Live Ratchet selector witness.** Existing untouched PCSX2/PINE captures on the Veldin class-0 player observed `Moby+0x53` as the current native sequence selector and the neutral cycle above. This is the only evidence class that can assign gameplay semantics by itself.
2. **Decoded dedicated Ratchet assets.** The level-core `+0x78` table has 256 slots; Veldin has slots 0..133 populated. Assets establish clip structure, frame counts, rates, and pose data, but attractive-looking motion is not sufficient to name a gameplay state.
3. **Executable selector mechanics.** `SCUS-97199` contains stores to `Moby+0x53`, including helpers rooted at `0x212ed8`, `0x212f90`, and `0x2130d8`. Their callers are generic Moby code unless the target is independently proven to be class-0 Ratchet.

The negative control remains important: static call site `0x22478c` passes immediate sequence `6`, but the surrounding path is gated by `oClass == 0x25f` at `0x224760..0x22476c`. Sequence 6 is therefore a generic-Moby false positive and is **not** a Ratchet locomotion admission.

## Procedural/post-controller involvement

Raw dedicated-sequence playback is not always the final retail pose. The established post-animation controller pass around executable `0x2111c4`, with the player chain at `Moby+0x64`, can modify joints after native sequence evaluation. Neutral sequence-0 evidence already showed time-varying controller output while the source frame repeated.

This table therefore maps **sequence selection only**. It does not admit frozen controller quaternions, guessed correction poses, or a claim that a raw clip reproduces the final retail player pose.

## Reproducible static probe

From the repository root on Jess-Laptop, route PowerShell through the workspace helper:

```text
cmd.exe /d /c C:\ChatGPT\Tools\pwsh-runner.cmd -File tools\rac1-ratchet-animation-probe.ps1
```

The probe hashes the canonical user-owned ISO, extracts root file `SCUS_971.99` directly from ISO-9660 without creating a retail payload file, hashes that executable in memory, verifies the seven executable `sb ...,0x53(...)` selector-store sites, and regenerates both JSON artifacts. Immediate `a1` values in the executable census remain syntactic evidence only and are never assigned Ratchet semantics without a class-0 witness.

`research/generated/rac1-ratchet-animation-trace.json` is deliberately an empty controlled-trace envelope in this checkpoint. It records the exact target, required raw Moby fields, action labels, negative control, and admission rule so the next live trace can be compared without changing the evidence contract.

## Required controlled live trace

Use the known Veldin class-0 player and read-only PINE memory access. Keep each action isolated and record raw `Moby+0x20`, `+0x50`, `+0x51`, `+0x52`, `+0x53`, `+0x54`, `+0x5c`, and `+0x74` while performing: forward locomotion; jump rise; apex/fall; landing; Square wrench attacks; turning; and crouch-turn.

Only admit an ID after it is repeatedly witnessed on the class-0 player in independently repeated labelled trials. Preserve transition sequences rather than collapsing them into one guessed state, and never promote an executable immediate or visually plausible decoded clip by itself.

## Current blocker

At this task slice on 2026-09-10, Remote Desktop Commander showed no running PCSX2 process on Jess-Laptop, so there was no live PINE endpoint to sample without starting or mutating shared emulator state. The existing `rc1-sol-2.6.3` profile confirms PINE is configured on slot `28031`, but its locomotion and Square controls remain DInput-bound rather than keyboard-driven. No live locomotion trace was captured in this slice.

Accordingly the locomotion map remains **TASK_BLOCKED** rather than guessing sequence IDs. The static authority/probe and empty trace schema are checkpointed so a later slice can begin directly with controlled live sampling.
