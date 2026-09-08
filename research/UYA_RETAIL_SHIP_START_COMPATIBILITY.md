# UYA retail ship/start-transform compatibility

Authority: `rac3-ntscu-original` / `SCUS-97353`.

This note records the evidence used to admit the shared GC/UYA/DL level-settings ship/start transform into the native RAC3 runtime. It does **not** claim that the retail executable field names have been recovered.

## Structural basis

The first `0x5c` bytes of the UYA settings block already pass the retail compatibility gate documented in `UYA_RETAIL_LEVEL_SETTINGS_COMPATIBILITY.md`. The shared layout places these candidate fields at:

- `+0x3c`: `f32 x`
- `+0x40`: `f32 y`
- `+0x44`: `f32 z`
- `+0x48`: `f32 rotation_z`

The labels originate from the established GC/UYA/DL shared-format lineage. UYA retail bytes independently establish that the enclosing structure and these four values are finite and in-range.

## All-row retail correlation

A local authority-only census decoded these four fields for every one of the 51 observed UYA main-level rows. No retail payload bytes are retained by this note.
Observed split:

- **21 / 51** rows carry non-default position/orientation values.
- **30 / 51** rows carry exactly `(20, 20, 20)` with rotation `0`.
- The repeated default includes the final-boss sample, multiplayer/split-screen-shaped rows and the zero-Moby multiplayer-menu row; it is therefore not admitted as a usable runtime spawn.

Representative retail values, still in native Z-up coordinates:

| table | position XYZ | rotation Z | runtime admission |
|---:|---|---:|---|
| 1 | `370.285889, 95.856995, 78.898117` | `0.884552` | admitted |
| 8 | `174.201019, 170.306076, 475.181122` | `0` | admitted |
| 20 | `20, 20, 20` | `0` | default/rejected |
| 50 | `20, 20, 20` | `0` | default/rejected |

The native importer converts admitted values Z-up to OBP Y-up as `(x, z, y)` while preserving the Z-axis rotation as Y-up yaw, matching the established RAC1/GC runtime convention.
## Executable archaeology boundary

The packed retail boot executable was decoded locally and searched with MIPS/R5900-aware load analysis. Several false-positive structures were rejected, including a generic `0x50`-byte record copier and vector/DMA helpers. No clean control-flow chain from the raw gameplay `+0x00` settings pointer to these four fields was established in this pass.

Therefore the current evidence level is **retail-corroborated shared-layout compatibility plus all-row behavioural correlation**, not recovered native symbol/control-flow proof. Future overlay/runtime tracing may strengthen that provenance without changing the preserved bytes.

## Runtime policy

`OBP.RAC3` preserves all four finite fields. It creates `RuntimeSpawn` only when the transform differs from the exact repeated `(20,20,20), 0` default. This rejection rule is an OBP admission policy, not a claim that retail itself names that tuple a sentinel.

Retail-backed tests pin the representative coordinates and the complete **21 admitted / 30 rejected** all-row split so later archaeology cannot silently broaden the interpretation.
