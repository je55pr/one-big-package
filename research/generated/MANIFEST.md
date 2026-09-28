# Generated archaeology evidence manifest

This directory contains payload-free reports, catalogues and reductions derived from authorized retail sources or controlled runtime traces. Generated does **not** mean disposable.

## Lifecycle statuses

- **active/reproducible** — a current in-repository producer exists. Reproduction may still require an authorized retail ISO, savestate, local capture or other ignored input.
- **frozen evidence** — retained evidence used by research, tests or current reasoning, but there is no single current in-repository command guaranteed to reproduce the exact file byte-for-byte.
- **superseded** — retained only when a named replacement explicitly makes the older artifact obsolete. No current artifact is assigned this status.
- **archival** — historically valuable snapshot with no current consumer and/or a retired named generator. Archival artifacts are preserved unless a separate evidence review proves them valueless.

A generator path embedded inside an archival or frozen artifact is historical provenance unless this manifest marks the artifact active/reproducible. Do not infer that an old path is still runnable.

## Active / reproducible

| Artifact | Current producer |
|---|---|
| `rac1-analogue-movement-probe.json` | `tools/rac1-analogue-movement-harness.py` |
| `rac1-authored-moby-census.csv` | `obp rac1-authored-moby-census` in `OBP.Cli` |
| `rac1-authored-moby-census.json` | `obp rac1-authored-moby-census` in `OBP.Cli` |
| `rac1-camera-chase-framing.json` | `tools/rac1-camera-archaeology.py derive-chase` |
| `rac1-camera-control-heading.json` | `tools/rac1-camera-archaeology.py derive` |
| `rac1-camera-control-producer.json` | `tools/rac1-camera-archaeology.py probe-producer` |
| `rac1-campaign-travel-witness.json` | `tools/rac1-campaign-probe.py` |
| `rac1-checkpoint-boundary.json` | `tools/rac1-checkpoint-boundary-probe.py` |
| `rac1-enemy-attack-patterns.json` | `tools/rac1-enemy-attack-pattern-probe.py` |
| `rac1-ground-contact-orientation.json` | `tools/rac1-ground-contact-orientation.py` |
| `rac1-ground-contact-slope.json` | `tools/rac1-ground-contact-slope.py` |
| `rac1-ground-contact-static.json` | `tools/rac1-ground-contact-static.py` |
| `rac1-ground-edge-fall.json` | `tools/rac1-ground-edge-fall.py` |
| `rac1-ground-slope-stop.json` | `tools/rac1-ground-slope-stop.py` |
| `rac1-ground-support-admission.json` | `tools/rac1-ground-support-admission.py` |
| `rac1-ground-terrain-projection.json` | `tools/rac1-ground-terrain-projection.py` |
| `rac1-ground-turn-response.json` | `tools/rac1-ground-turn-response.py` |
| `rac1-hostile-common-state.json` | `tools/rac1-hostile-common-probe.py` |
| `rac1-level2-checkpoint.json` | `tools/rac1-level2-checkpoint-probe.py` |
| `rac1-level-entry-witness.json` | `tools/rac1-level-entry-probe.py` |
| `rac1-movement-contact-validation.json` | `tools/rac1-movement-contact-validation.py` |
| `rac1-movement-savestate-probe.json` | `tools/rac1-savestate-movement-probe.py` |
| `rac1-movement-static-probe.json` | `tools/rac1-movement-probe.py` |
| `rac1-projectile-hit-patterns.json` | `tools/rac1-projectile-hit-pattern-probe.py` |
| `rac1-ratchet-animation-states.json` | `tools/rac1-ratchet-animation-probe.mjs` / wrapper |
| `rac1-ratchet-animation-trace.json` | `tools/rac1-ratchet-animation-probe.mjs` / wrapper |
| `rac1-stick-heading-probe.json` | `tools/rac1-stick-heading-matrix.py` |
| `rac1-weapon-inventory-state.json` | `tools/rac1-weapon-inventory-probe.py` |
| `rac3-ntscu-original.uya-movement-witnesses.json` | `tools/uya-movement-witness.py` |
## Frozen evidence

These artifacts remain part of the current evidence base. Their exact acquisition may depend on local-only raw captures, an older one-off reducer, or code paths that no longer expose a standalone generator.

| Artifact | Retention reason |
|---|---|
| `gc-ratchet-animation-states.json` | GC animation-state evidence |
| `gc_moby_catalogue.json` | GC semantic Moby catalogue |
| `rac1-analogue-input-law.json` | dense retail input-law reduction used by movement evidence |
| `rac1-bolt-crate-reward-loop.json` | crate/bolt reward-loop witness |
| `rac1-class-family-census.json` | structural class-family census |
| `rac1-death-respawn-boundary.json` | death/respawn boundary witness |
| `rac1-dynamic-moby-runtime-census.json` | deterministic runtime census; exercised by tests |
| `rac1-instance-census.json` | authored instance census |
| `rac1-level-index-census.json` | level-index evidence used by retail travel tests |
| `rac1-level-settings-census.json` | level-settings evidence |
| `rac1-moby-bind-pose-census.json` | bind-pose census |
| `rac1-moby-census.json` | Moby census |
| `rac1-moby-skinning-census.json` | skinning interpretation census |
| `rac1-native-world-census.json` | sanitized all-level native-world census |
| `rac1-npc-interactable-census.json` | interactable census exercised by tests |
| `rac1-ratchet-locomotion-stop-phase.json` | retained phase-dependence witness |
| `rac1-ratchet-movement-controller.json` | selected movement authority summary |
| `rac1-red-plant-animation-selector.json` | selector witness used to constrain runtime admission |
| `rac1-special-surface-reachability.json` | retail reachability evidence used by movement validation |
| `rac1-static-geometry-census.json` | static-geometry census |
| `rac1_level0_world_summary.json` | deterministic Veldin world summary |
| `rac1_sky_census.json` | all-level sky census |
| `rac3-ntscu-original.production-import-census.json` | production importer reference exercised by tests |
| `rac3-ntscu-original.uya-gameplay-block-census.json` | semantic-free retail gameplay-block census |
| `rac3-ntscu-original.uya-moby-pvar-families.json` | compact 892-class PVar-family index; embedded `reference-ts` generator path is historical |
| `rac3-ntscu-original.uya-pvars.json` | retail PVar compatibility evidence |
| `rac3-ntscu-original.uya-toc.csv` | compact UYA table-of-contents evidence |

## Archival

| Artifact | Why it remains |
|---|---|
| `rac1-ntscu-original.stage0.json` | bounded authority-disc snapshot; retired `tools/disc-archaeology.mjs` path is historical provenance |
| `rac1-ntscu-original.stage0.md` | human-readable companion to the retained authority-disc snapshot |
| `rac2-ntscu-v1.01.stage0.json` | bounded authority-disc snapshot; retired `tools/disc-archaeology.mjs` path is historical provenance |
| `rac2-ntscu-v1.01.stage0.md` | human-readable companion to the retained authority-disc snapshot |
| `rac3-ntscu-original.stage0.json` | bounded authority-disc snapshot; retired `tools/disc-archaeology.mjs` path is historical provenance |
| `rac3-ntscu-original.stage0.md` | human-readable companion to the retained authority-disc snapshot |
| `rac2-level-catalogue.json` | exact GC catalogue snapshot; named `tools/gc-level-wad.mjs` generator is retired |
| `rac3-ntscu-original.uya-family-500-511-pvars.json` | unique detailed class-500/501/502/505/511 PVar profiles; retired generator, no live consumer |
| `rac3-ntscu-original.uya-level-dispatch.json` | unique 51-row level-overlay/dispatch snapshot; retired generator, no live consumer |

The two UYA archival reports above are intentionally retained. The family report contains detailed per-class PVar sizes, mode bits, relative-pointer offsets and dword profiles that are not present in the compact broad-family index. The level-dispatch report preserves the exact selected-class overlay/dispatch census and shared-update relationships. Neither should be removed merely because its original one-off generator was retired.

## Maintenance rule

When adding or regenerating an artifact, update this manifest in the same change. Move an artifact to **superseded** only when its replacement is explicit and preserves the evidence needed by current research/tests. Deletion is a separate evidence decision, not an automatic consequence of age or generator retirement.

