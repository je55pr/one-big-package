# R&C1 Ratchet player animation/state admissions

Scope: NTSC-U `SCUS-97199`, using the user-owned retail ISO pinned by `research/manifests/rac1-ntscu.json`. This is an evidence admission map, not an animation-name guess list.

## Controlled live result

A controlled read-only PCSX2/PINE trace on Veldin's live class-0 Ratchet at Moby `0x01845e80` now admits the useful locomotion/action family. Sixteen clean focused-input trials are reduced into `research/generated/rac1-ratchet-animation-trace.json`; each raw local trace is SHA-256 pinned there without committing the multi-thousand-line memory samples.

| Retail action / role | Native seq | Native timing | Controlled selector evidence | Admission |
| --- | ---: | --- | --- | --- |
| standing | 0 | 10 frames, 0.125 = 7.5 FPS | prior neutral class-0 witness | **ADMIT** |
| delayed idle/fidgets | 1, 2 | 77 frames each, variable | prior neutral cycle `0 -> 2 -> 0 -> 1` | **ADMIT, neutral only** |
| locomotion start | 3 | 33 frames, 0.25 = 15 FPS | precedes sustained movement in forward/left/right/run-jump trials | **ADMIT transition** |
| sustained locomotion | 4 | 23 frames, 0.5 = 30 FPS | repeated while stick remains held | **ADMIT** |
| locomotion stop/settle variants | 5, 20, 6 | 5/6 are 13 frames each at 0.25 = 15 FPS; 20 timing not yet promoted here | targeted class-0 release trials show phase-dependent 5/20/6 selection across W/A/D | **ADMIT transition-only; exact boundary predicate unresolved** |
| stationary jump | 7 | 29 frames, variable | `2 -> 7 -> 0`, repeated | **ADMIT** |
| moving jump | 8 | 16 frames, variable | `3 -> 4 -> 8 -> 4`, repeated | **ADMIT** |
| crouch | 13 | 15 frames, 0.25 = 15 FPS | `2 -> 13 -> 0` | **ADMIT** |
| crouch-turn right | 14 | 7 frames, 0.25 = 15 FPS | `2 -> 13 -> 14 -> 0` | **ADMIT** |
| crouch-turn left | 15 | 7 frames, 0.25 = 15 FPS | `2 -> 13 -> 15 -> 0` | **ADMIT** |
| Square wrench attack | 23 | 21 frames, variable | `2 -> 23 -> 0`, repeated | **ADMIT** |
| bind-linear anchor | 122 | 21 frames, 0.5 = 30 FPS | decoded asset/rest archaeology | diagnostic only |

## Airborne and landing boundary

Stationary jump stays on sequence 7 throughout the observed airborne interval and returns directly to 0. Moving jump stays on sequence 8 while airborne and returns directly to sustained locomotion 4 when movement remains held. No separate selector change was witnessed at jump apex, during fall, or at touchdown.

That is positive evidence for selector behaviour, not proof that retail lacks procedural airborne/landing work elsewhere. The semantic runtime may still distinguish `JumpRise`, `Fall`, and `Land` from controller facts, but it must not invent separate native clip ids from this trace. For a first binding, rise/fall may share the witnessed jump sequence selected from launch context, while `Land` is a semantic transition with no independently admitted native landing clip.

Ordinary left/right stick trials use the same `3 -> 4` locomotion family as forward input. They do not establish a separate standing turn-in-place animation. Crouch-turn is distinct and direction-specific: right selects 14, left selects 15, both entered through crouch sequence 13.

## Locomotion stop/settle phase family 5, 20, 6

Sequence 6 has an important provenance correction. Earlier static executable archaeology rejected call site `0x22478c` as Ratchet evidence because that path is gated by `oClass == 0x25f`; that rejection remains correct. The admission for sequence 6 comes from independent live class-0 Ratchet traces, not from rehabilitating that generic-Moby call site.

Issue #46 disproved the earlier tempting direction interpretation. HWND-targeted W/A/D release trials all produced the same broad phase family: early sequence-4 release witnesses select 5, middle-phase witnesses select 20, and late-phase witnesses select 6. Repeated A/D controls at observer frames 4, 14, and 20 reproduced 5, 20, and 6 respectively, so forward-versus-sideways direction is not the selection rule.

The exact retail tick/sub-frame boundary is intentionally not promoted. Host key-up delivery is asynchronous with the emulated game tick: repeated W releases posted while observer frame 13 was visible produced both 5 and 6, and observer frame 18 produced both 20 and 6. Therefore integer observer frames are evidence for phase dependence, not a safe gameplay predicate. The payload-free summary and representative raw-trace hashes are preserved in `research/generated/rac1-ratchet-locomotion-stop-phase.json`; raw samples remain local-only.

This bounded result closes the archaeology question without inventing behaviour: 5, 20, and 6 are admitted as context-sensitive locomotion stop/settle transitions, direction-only selection is rejected, and exact phase-boundary selection remains unresolved. OBP should continue to avoid choosing among these clips until a deterministic retail-side predicate is recovered.

## Evidence boundary

Three evidence classes remain separate: live class-0 selector witnesses assign gameplay semantics; decoded dedicated Ratchet assets establish frame/rate structure; executable selector mechanics explain stores/helpers but cannot name a player state without an independent class-0 witness. Raw dedicated-sequence playback is also not always the final retail pose because the established post-animation controller pass around `0x2111c4` can modify joints after sequence evaluation.

Accordingly this map admits **sequence selection**, not frozen controller corrections or a claim that raw clips alone reproduce the final retail pose.

## Reproduction

The static probe verifies the canonical ISO/executable hashes and selector-store census, and regenerates the admitted state report:

```text
C:\ChatGPT\Tools\pwsh-runner.cmd -File tools\rac1-ratchet-animation-probe.ps1
```

To re-verify the controlled live evidence and regenerate the trace summary, pass the four script parameters positionally through `pwsh-runner`: ISO path, state output, trace output, then the directory containing the 16 labelled `final-*.json` traces. The probe fails if a required trace is missing, targets a different Moby, or its observed `Moby+0x53` sequence path drifts.

The controlled trials sampled `Moby+0x20`, `+0x50`, `+0x51`, `+0x52`, `+0x53`, `+0x54`, `+0x5c`, and `+0x74`. PINE access was read-only. A dedicated portable PCSX2 profile was loaded from the same Veldin savestate before each trial; temporary keyboard bindings existed only in that isolated profile and were restored after capture.

## Runtime-facing admission

A conservative first semantic binding now has enough authority for the next milestone: standing uses 0; sustained movement uses 4 with optional start transition 3; stationary and moving jump use 7 and 8 respectively; wrench uses 23. Semantic `Fall` may continue the launch-context jump clip because retail showed no selector split, and semantic `Land` must not claim a dedicated native clip yet. Sequences 5/20/6 are admitted context-sensitive stop/settle transitions, but OBP should not select among them until a deterministic retail-side phase predicate is recovered.

This is sufficient to unblock a movement-fact-driven playable Ratchet animation binding without making animation the source of truth for physics or controller state.
