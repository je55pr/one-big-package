# R&C1 Pyrocitor: class-176 evidence and cross-world contact boundary

Issue: [#296](https://github.com/je55pr/one-big-package/issues/296).
This is a bounded implementation/evidence ledger, **not** a retail side-by-side signoff.
All native values below come from the existing authorized NTSC-U overlay study
in [RAC1_ADDITIONAL_WEAPONS.md](RAC1_ADDITIONAL_WEAPONS.md) and
[WORLD_EFFECTS.md](WORLD_EFFECTS.md); the retail game/ISO is not redistributed.

## Evidence-backed pieces already in OBP

| Feature | Recovered fact / code path | Boundary |
| --- | --- | --- |
| Weapon and pose | Item 16, class 176, equipment table `0x179ac0`, hand joint 56, body pose 51; source model compressed hash recorded in `Rac1WeaponProfiles` | `Rac1EquipmentAssets` and `Rac1EquipmentView` decode the real skin, sockets, clips and material IDs; attachment/pose blending is Godot presentation |
| Ammo clock | Initialized overlay SDATA `0x161530` and class 176 `0x2b80f8` specify ten-tick global modulo consumption | `Rac1AdditionalWeaponSession` owns the 60 Hz host clock, which is *not* a replay of retail's global clock |
| Flame origin and reach | SDATA `0x161520` gives reach 12; `0x26b230/0x26b500` type-12 constructors and motion | Native socket origin transformed into host space; reach is reduced by a Godot obstruction ray, not the complete retail world-query routine |
| Visuals | Two long/short type-12 flame looks per step (one long below reach 4); native Part texture 24 / eight short textures; type-2 secondary near death | Actual source-driven texture, size, drift, colour and blending recurrences; type-25 embers and full original GS output not recovered |
| Damage | `0x2b8538..2b857c`: source damage 1 / flags `0x10000`; one tracked emitter index 2 per fourth tick, fifteen travelling records | Native R&C1 receivers keep `Rac1WeaponProfiles.DirectDamage`. Other registered actor receivers get a source-qualified `WeaponDamageIntent` through `ActorCombatContactService` |
| Sustained sound | Class 176 local sound 1; decoded sound bank and owned looping voice in `Rac1WeaponAudioPresenter` | Recovered program/loop data; host spatial mixing/pitch is still approximate |

The host records per-particle **identity**, never a recyclable pool slot, as the
cross-source shot generation. Its contact repeat window uses the complete
`WeaponTargetKey(source, class, instance)`; an admitted hit reserves the window,
a rejected hit does not. R&C1 actors placed in Fusion Lab still receive the native
class-176 envelope when explicitly admitted by `FusionDamagePolicy`. GC/UYA
actors retain their own controllers and decide whether to accept a hit. A source
game or a matching native class number alone is never proof of target identity.

## Host policies, not recovered retail facts

- Twelve-tick victim repeat inhibition and Godot mesh AABB overlap / line-of-sight
  queries; particle size is converted to a spherical contact approximation.
- Current nozzle-to-wall clipping, spread radius and stream acceleration choices;
  world geometry obstruction uses a ray rather than native primitive collision.
- Fifteen tracked native-style flame records but not the full flame PVar/flags
  algorithm or complete native pool bookkeeping.
- Moving Pyrocitor pose: the retail item row has no separate moving upper-body
  sequence, and the previous host implementation fell back to an ordinary
  running pose while the flame muzzle remained attached to the moving hand.
  OBP now layers the recovered sequence 51 over the arm joints while the legs
  continue their locomotion clip. This is a **host visual fallback**, not a
  claim that retail uses sequence 51 as its native moving selector.
- Host 60 Hz sound start/stop and fade/pause behavior; complete retail audio
  transitions and player-body animation interruption still need comparison.
- No claimed exact side-by-side flame density, camera-response, type-25 ember
  coverage, source palette output or rendering-performance validation here.

## Verification

Portable tests in `Rac1PyrocitorContactWindowTests` and
`FusionPyrocitorDamageTests` cover source-key collisions, repeat admission,
unload reset, exact native flag/weapon requirements and rejection of incorrectly
qualified contacts. Existing `Rac1ParticleTests`, `Rac1AdditionalWeaponTests`
and `FusionWeaponTests` retain emitter, ammunition and fusion regression
coverage. Build `game/OBP.Game.csproj`; no commercial game files are needed.

For a genuine in-engine retail comparison, use **authorized local** source files,
`--rac1-weapons-smoke` and a locally available Godot Mono renderer. Repeat
sustained fire at near/far walls and crowds, release and re-equip, pause, die,
unload/reload, and fire in the Fusion Lab at two actors with colliding numeric
native IDs from different games. On Maktar, verify a live admitted UYA hostile
through its actor adapter when ready, and capture the flame cone at ordinary
camera angles. Preserve the source, frame and comparison provenance locally.
No captured frame or visual-equivalence certification is supplied by this change.

This does **not** complete the retail-study, visual side-by-side or promotional
capture acceptance of #296. It closes the specific source-qualified contact
and identity-collision gap without stepping into #282 effects work or #297 AI.
