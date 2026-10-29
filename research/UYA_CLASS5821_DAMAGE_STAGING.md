# UYA class-5821 damage-only staging, investigation for #297

**Source:** Up Your Arsenal NTSC-U, `SCUS-97353`, build `rac3-ntscu-original`. This is a deliberately **partial** integration of the identified one-eyed variant, not complete native behavior or issue acceptance.

## Evidence already recovered

- `research/UYA_VELDIN_GAMEPLAY.md`: 62 TABLE1 class-5821 Mobies with 0x5F0-byte PVars; common UYA victim lifetime branch, native 0x00010000 damage-query mask, target selection request and state-8/state-10 attack facts.
- `src/OBP.RAC3/Gameplay/Rac3Class5821Actor.cs` and `Rac3Class5821Admission.cs`: strict authored TABLE1 PVar/placement profile, independent mutable runtime lifetime, UYA-owned victim consequence. These are the native authority for damage, not the host.
- `research/UYA_MOBY_ANIMATION.md` documents the older strict-sphere exclusion. The current decoder includes extra native scale/translation channels, and the 10%-tolerant native-pose policy now supports the 5821 frames geometrically. The class is **still not animation-admitted in ordinary gameplay** because native state-to-sequence selection is not recovered.
- **Visual-class identity now established for TABLE1 and cross-level identical geometry:** local NTSC-U class-5821 decoded mesh has one ocular stalk and the rounded, limbed Tyhrranoid body; distinct class-5860 has two stalks. These match the visual distinction in the [public one-eyed reference](https://models.spriters-resource.com/playstation_3/ratchetclankupyourarsenalhd/asset/328459/). Public PS3 art is cross-check only; the PS2 disc is the mesh authority. The four-eyed/mecha/other skin variants are not inferred.

## Retail visual-class verification (29 October 2026)

A read-only inspection decoded native class meshes and authored Moby placements using
`Rac3LevelCore.Open`, `Rac3Gameplay.Read` and `Rac3Assets.ReadMobyClasses`
with the authorized local NTSC-U retail image. A separately generated,
**uncommitted** mesh contact sheet was visually inspected. Unlike the earlier
semantic class-name guess, this compares the actual class geometry:

| Native class | TABLE1 population | Triangles | Joint count | Geometry observation |
| --- | ---: | ---: | ---: | --- |
| 5821 | 62 | 1,203 | 67 | Rounded body, one ocular stalk |
| 5860 | 23 | 1,300 | 72 | Related rounded body, **two** ocular stalks |

**Eight independently authored retail tables** contain oClass 5821:
TABLE1 (62), TABLE4 (126), TABLE9 (75), TABLE12 (30), TABLE13 (66),
TABLE16 (32), TABLE18 (30), TABLE29 (50). Total **471 authored 5821
instances**. Their source-decoded native-initial-pose position arrays all have
the same SHA-256:
`276AD55B98180F7CBBC8020A19123F0B5A96930CB990BEB41F4CC188741EEF76`.
All eight share 1,203 triangles, 67 joints and 13 sequence definitions.
TABLE1's two-eye control class 5860 differs geometrically and has 20
sequence definitions. `Rac3TyhrranoidVisualRetailTests` pins these numerical
facts using only the user's authorized disc; no geometry or screenshot enters Git.

A separate current-decoder sweep evaluates all **13 sequences / 192 native
frames** of TABLE1 class 5821 using `Ps2MobyNativePose.Pose`. All frames
resolve their extra channels; all frame vertices fit within the existing
`Rac3WorldImport.Animation` **1.10× approximate retail sphere** threshold.
The greatest observed ratio is 1.0688 (sequence 7). This is an OBP
**geometric eligibility** check, not complete retail state-to-clip timing
or ordinary-play validation. One conditional native mapping **is** recovered:
TABLE1 state 8 can enter attack state 10 and select action/sequence **12**;
that retail clip contains **20 fully poseable frames**, while the native
state-10 damage emitter is separately gated on action progress and byte
`Moby+0x42 == 12`. The preceding entry into state 8, continuous action clock,
state-10 exit and every other animation selection remain unsupported. The
`Rac3TyhrranoidVisualRetailTests` pin this observation.

This establishes the **one-eyed geometry's class identity** across the
eight witnessed tables. It does **not** establish texture/palette variants,
pose fidelity across every frame, one universal AI profile for all level contexts,
the four-eyed or Mecha class IDs, or source-native swarm spawn behavior.

## What this branch stages

`Rac3Class5821DamagePort` admits **only** the evidenced UYA TABLE1 class-5821 profile with a native instance payload and its source-qualified runtime identity. Each instance gets a **private copied PVar**, a separate class-specific damage consumer and a separate lifetime. A foreign weapon supplies its own positive damage amount; the **OBP-owned cross-game bridge** chooses the recovered TABLE1 victim mask and record-kind zero. Weapon-specific flags are not cast into UYA flags. The UYA consumer then applies the exact authored lifetime consequence (including its positive sub-unit clamp). Nonmatching class, target, missing context or unsupported payloads fail closed.

`Rac3Class5821DamageActorFactory` exposes this limited adapter through the existing `ActorRuntimeSet` actor-placement boundary, source-qualified by game/build/class/level/context. It uses an imported real mesh when present; no dummy mesh, chase controller, attack emissions or guessed action sequence is supplied. A lifetime-depleted actor loses **OBP-only host contact**, not a purported native death or reward. Native class update, animation, death and reward capability remain explicitly unimplemented; TABLE1 visual-class identity is now marked ready.

This is a **damage-testing admission recipe**, not a normal one-eyed enemy or finished encounter. To request it through a version-1 placement use `sourceGame=Rac3`, `build=rac3-ntscu-original`, `levelId=1`, `nativeClassId=5821`, `adapterId=rac3/class5821-damage-only/v1`, `nativeContextId=uya/table1-class5821-damage-only/v1`, `initialNativeState=0` and an authored source instance/rigid transform. All other classes/contexts are rejected. This recipe belongs to OBP, not retail. Do not enable it for the Trial by Fire promotion until native animation, attack and movement progression are established.

## Live R&C1 Pyrocitor-to-source-qualified actor contact bridge

The original R&C1 `StepRac1NativeFlameContacts` still uses the existing
`Rac1ParticleSession.Pyrocitor` particles and identical original-world
victim path. During **ordinary OBP sessions only**, those same moving native
particle observations also enter
`ActorCombatContactService.ResolveFlame`:

- Hit candidates are source-qualified registered **non-R&C1** actors, not
  arbitrary same-numbered UYA classes. The existing inflated particle
  envelope and host line-of-sight must both admit the target.
- The original R&C1 weapon's source, use-generation, particle-creation
  sequence, damage amount and flags travel as a **foreign weapon intent**.
  Native UYA class 5821 still decides lifetime subtraction through its
  own explicit victim-mask/record-kind adapter, never through R&C1 flag
  reinterpretation.
- Each native flame carries its own source-qualified successful-target
  receipts; the world-scoped actor registry additionally rejects duplicate
  shot/contact receipts. This path does **not** synthesize a new fireball,
  AI state, native kill animation or rewards. Original R&C1 contacts remain
  handled by the unchanged game-specific code.
- The host captures accepted cross-source damage facts for diagnostic
  recording. The class-5821 factory still does not claim
  `SupportsDeterministicReplay`; complete fresh-process replay and rendered
  eight-actor Pyrocitor combat are **not** established by these changes.

`ActorContactGeometryTests` pin the shared particle-radius / bounding-box
envelope and non-finite rejection; the synthetic eight-victim test validates
the target's independent native consumer, **not** a filmed ordinary encounter.

## Portable regression and remaining blockers

`Rac3Class5821DamagePortTests` builds eight isolated UYA runtime instances with source-qualified Pyrocitor damage intents, asserts native lifetime clamp, one-hit receipt deduplication, no cross-instance damage, unmodified authored PVars, fresh replay and explicit wrong-target/context rejection. This validates a **staging harness**, not an eight-enemy retail melee.

The full #297 acceptance remains blocked by: verifying intended palette/material/skin variants and recovering sounds, source-native clip selection and animated presentation; native target acquisition/loco/turning/state tick and contact timing; real attack/player consequence; native reaction/death/rewards; live crowd and obstacle motion; source-backed original-UYA rendered gameplay; admitted Maktar/Fusion rendered replay. Related #299 scene/camera work stays out of this scope.

No retail bytes, asset copies, executable payloads, captures, or local ISO paths are committed.
