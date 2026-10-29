# UYA class-5821 damage-only staging, investigation for #297

**Source:** Up Your Arsenal NTSC-U, `SCUS-97353`, build `rac3-ntscu-original`. This is a deliberately **partial** integration, not one-eyed Tyhrranoid recovery or issue acceptance.

## Evidence already recovered

- `research/UYA_VELDIN_GAMEPLAY.md`: 62 TABLE1 class-5821 Mobies with 0x5F0-byte PVars; common UYA victim lifetime branch, native 0x00010000 damage-query mask, target selection request and state-8/state-10 attack facts.
- `src/OBP.RAC3/Gameplay/Rac3Class5821Actor.cs` and `Rac3Class5821Admission.cs`: strict authored TABLE1 PVar/placement profile, independent mutable runtime lifetime, UYA-owned victim consequence. These are the native authority for damage, not the host.
- `research/UYA_MOBY_ANIMATION.md`: class 5821 is **not** animation-admitted; decoded sequences escape their authored spheres. Rendering a static genuine mesh is **not** proof of recovered animation or one-eyed silhouette.
- The numeric class ID has **not** been identified as the requested one-eyed Tyhrranoid. Do not conflate it with the four-eyed/mecha variants or invent a mapping based on appearance labels.

## What this branch stages

`Rac3Class5821DamagePort` admits **only** the evidenced UYA TABLE1 class-5821 profile with a native instance payload and its source-qualified runtime identity. Each instance gets a **private copied PVar**, a separate class-specific damage consumer and a separate lifetime. A foreign weapon supplies its own positive damage amount; the **OBP-owned cross-game bridge** chooses the recovered TABLE1 victim mask and record-kind zero. Weapon-specific flags are not cast into UYA flags. The UYA consumer then applies the exact authored lifetime consequence (including its positive sub-unit clamp). Nonmatching class, target, missing context or unsupported payloads fail closed.

`Rac3Class5821DamageActorFactory` exposes this limited adapter through the existing `ActorRuntimeSet` actor-placement boundary, source-qualified by game/build/class/level/context. It uses an imported real mesh when present; no dummy mesh, chase controller, attack emissions or guessed action sequence is supplied. A lifetime-depleted actor loses **OBP-only host contact**, not a purported native death or reward. Native class update, animation, death, variant identity and reward capability are explicitly marked unimplemented.

This is a **damage-testing admission recipe**, not a normal one-eyed enemy or finished encounter. To request it through a version-1 placement use `sourceGame=Rac3`, `build=rac3-ntscu-original`, `levelId=1`, `nativeClassId=5821`, `adapterId=rac3/class5821-damage-only/v1`, `nativeContextId=uya/table1-class5821-damage-only/v1`, `initialNativeState=0` and an authored source instance/rigid transform. All other classes/contexts are rejected. This recipe belongs to OBP, not retail. Do not enable it for the Trial by Fire promotion until visual identity and native update are established.

## Portable regression and remaining blockers

`Rac3Class5821DamagePortTests` builds eight isolated UYA runtime instances with source-qualified Pyrocitor damage intents, asserts native lifetime clamp, one-hit receipt deduplication, no cross-instance damage, unmodified authored PVars, fresh replay and explicit wrong-target/context rejection. This validates a **staging harness**, not an eight-enemy retail melee.

The full #297 acceptance remains blocked by: verifying the one-eyed asset/class/skin against retail; recovering sound and safe skeletal clips; native target acquisition/loco/turning/state tick and contact timing; real attack/player consequence; native reaction/death/rewards; live crowd and obstacle motion; source-backed original-UYA rendered gameplay; admitted Maktar/Fusion rendered replay. Related #299 scene/camera work stays out of this scope.

No retail bytes, asset copies, executable payloads, captures, or local ISO paths are committed.
