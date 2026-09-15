# R&C1 ordinary wrench facing

Authority: NTSC-U original retail (`SCUS-97199`, build `rac1-ntscu-original`). This note promotes only the ordinary sequence-23 ground-swing facing relationship recovered from local controlled PCSX2/PINE captures. Raw captures remain local and are not committed.

## Retail witnesses

Both trials sampled live class-0 Ratchet position, Moby `+0x48` yaw and native sequence at roughly 60 Hz. The attack input was Square only during the sampled swing; the second trial first used the already-established crouch-turn path to establish a different player yaw.

| witness | raw SHA-256 | swing yaw | swing planar delta | delta heading | heading error |
| --- | --- | ---: | --- | ---: | ---: |
| neutral ground swing | `b48064406ce0e6ad528d66d3358495861ae042bf4da1d4444b9b8c9167f348c8` | `1.1110417843` | `(0.2976684570, 0.6013565063)` | `1.1111607375` | `+0.0001189532 rad` |
| pre-faced ground swing | `b89502827e1f9534953e4d4834b2d73ab868db88ee3f6345764d6597536fd5f2` | `0.3777317405` | `(0.6141510010, 0.2436218262)` | `0.3776416084` | `-0.0000901321 rad` |

In both witnesses the native yaw is constant for the observed sequence-23 interval. The attack lunge therefore follows `(cos(yaw), sin(yaw))` to substantially tighter than one thousandth of a radian at two distinct headings.

## Admission

For the ordinary first ground swing, the gameplay-facing axis is Ratchet's live native player yaw. `Rac1WrenchCombatController.ResolveFirstSwingFacing` exposes exactly that narrow rule as a unit native-XY direction.
The rule does not infer facing from animation sequence, camera forward, target position, or the maximum observed normal-turn sample. Those remain separate facts.

## Camera boundary

These captures do not sample retail camera state and do not exercise right-stick camera control. `RAC1_PLAYER_MOVEMENT.md` already leaves the exact camera-relative stick transform and normal-turn easing recurrence unresolved. This lane therefore does not promote a camera-to-attack snap, camera-relative hit axis, aim assist, or combat-camera turn law.

That negative boundary matters for the live host: the current Godot camera vector may be a presentation convenience, but it is not retail evidence for wrench hit authority. Replacing it with a guessed travel-vector or camera-follow rule would merely swap one unsupported rule for another. The recovered core contract instead requires an authoritative live player yaw before host contact geometry can claim retail-facing semantics.

Godot remains free to choose presentation-only combat camera feel so long as that presentation does not become the source of R&C1 hit direction.

## Reproduction boundary

The raw JSON witnesses are intentionally excluded by `.gitignore`. Their SHA-256 pins above identify the local captures used for this admission without adding retail execution payloads to Git. Deterministic portable tests preserve the admitted numeric relationship.
