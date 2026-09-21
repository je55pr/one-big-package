# R&C1 common weapon-use / firing framework

Authority: NTSC-U original retail `SCUS-97199`, using the already-authorized local retail input and retained Veldin savestates. This note retains addresses, derived control flow and deterministic semantics only; no retail payload bytes are stored.

## Scope and result

The reusable boundary is a **weapon-use admission result**, not one universal weapon state machine. Wrench item 8 and ranged item 10 both begin from a semantic primary-use request in OBP, but retail gives them materially different timing and spawn paths:

| fact | wrench / item 8 | first ranged / item 10 |
| --- | --- | --- |
| ammo | no ranged-ammo mutation | one round per accepted use |
| recovered timing owner | player action/contact window | class-`0xc0` weapon object |
| admitted player presentation | action `0x13`, sequence 23 | accepted-fire sequence 44 |
| projectile staging | none in recovered ordinary swing | class `0x79`, state 0, staged by the class-`0xc0` owner |
| repeated-use gate | no common ranged cooldown promoted | 20-tick accepted-fire gate; replacement staging is immediate |
| facing/origin | first-swing lunge follows live Ratchet yaw | dedicated weapon launch/step frame; exact item-10 origin remains unresolved |

Accordingly `Rac1WeaponUseAdmission` is intentionally small. It reports accepted/rejected use, optional ammo delta, and an independently recovered native player sequence id. Numeric cadence and projectile/contact state stay in their weapon-specific controllers.

## Item-10 admission and ammo ordering

The controlled item-10 weapon uses native class `0xc0` with loaded update `0x002c26c8`. Its accepted-use path consumes one round through `0x00233db8(-1, 1)` at `0x002c28d0` and enters native weapon state `3`.

Separately controlled class-0 Ratchet traces prove that accepted item-10 use selects sequence 44 and that zero ammo does not enter sequence 44. Therefore sequence 44 belongs on the accepted-use result without making animation playback authoritative for fire cadence.

A rejected request consumes no ammo. `Rac1WeaponUseAdmission` distinguishes not-equipped, no-ammo, cadence-blocked and spawn-not-ready failures while leaving their native timing owners weapon-specific.

## Projectile staging and ownership

The controlled item-10 weapon object owns staging rather than asking a host projectile service to invent it.

Weapon state `3` loads the staged object from owner PVar `+0x50`, calls constructor `0x002ace70`, and launches through `0x002acfd8`. The constructor:

- requests native class `0x79`;
- initializes native projectile state `0`;
- receives the class-`0xc0` weapon Moby as its source;
- stores that source Moby pointer at projectile PVar `+0x50`.

The retained ownership chain is therefore:

```text
class 0xc0 weapon Moby
  PVar +0x50 -> staged class 0x79 Moby
                   PVar +0x50 -> source class 0xc0 Moby
```

Launch writes projectile state `1`. The same accepted-fire update can stage a replacement class-`0x79` object immediately after the launched pointer is handed off. The controlled live trace shows the launched object and replacement staging object simultaneously on the ammo-decrement frame.

The weapon still retains a recovered 20-native-tick accepted-fire gate. The earlier 10-tick rearm claim belonged to a different class-`0xc0` / class-`0x4a` path and is not item-10 Bomb Glove timing.

`Rac1WeaponSpawnOwnership` remains the reusable source/spawn envelope. For item 10 it now records class `0xc0 -> 0x79`, staged offset `+0x50`, projectile source offset `+0x50`, state `0 -> 1`, and a dedicated weapon launch frame.

## Launch-frame boundary

The controlled item-10 class-`0x79` projectile owns a dedicated step vector at PVar `+0x00..+0x08`. Its state-1 update advances:

`position = position + step`

and then applies the recovered vertical-step recurrence documented in `RAC1_PROJECTILE_HIT_PATTERNS.md`.

The complete item-10 **launch-vector initialization and launch-origin formula remain unresolved**. The Godot host therefore maps native Ratchet yaw directly for visible direction and keeps its muzzle offset as an explicit presentation fallback.

This supersedes an earlier attribution from another class-`0xc0` path rooted at `0x002c1ad0`, constructor `0x002a9ed0`, class `0x4a`, and launch helper `0x002aa008`. That separate path remains valid retail evidence: it has class-`0x4a` ownership at projectile PVar `+0x30`, a 10/20-tick pair, and the previously decoded yaw/radius/height origin construction. Controlled item-10 replay proves it is **not** the Bomb Glove carrier, so those constants are retained as separate-family archaeology rather than exposed through `Rac1BombGlove`.

## Wrench contrast

Ordinary wrench use remains a player-action/contact contract:

- native action `0x13`, profile 0;
- admitted player sequence 23;
- first-swing contact age `17..23`;
- first-swing movement/facing evidence follows live Ratchet yaw;
- no item-10 ammo mutation or projectile staging is promoted onto it.

`Rac1WrenchCombatController.AdmitOrdinaryUse` therefore returns the same semantic admission envelope but keeps all wrench timing in the existing action/contact controller.

## Movement and animation integration

Gameplay remains authoritative over presentation. A primary input request is not itself an R&C1 attack animation admission.

For wrench, the host opens the recovered ordinary swing and only then pulses the neutral `PrimaryAttack` presentation role, whose admitted R&C1 clip is sequence 23.

For item 10, accepted fire reports native selector 44 through `Rac1WeaponUseAdmission`. The current neutral avatar provider does not decode/play sequence 44 because its playback timing is not yet promoted. Rejected item-10 requests therefore do not fall through to the wrench sequence-23 animation, and accepted shots do not invent sequence-44 timing.

This keeps three clocks separate: movement/controller state, weapon-use/contact cadence, and presentation playback.

## Reproduction

The corrected item-10 path is reproduced by the payload-free probe in `tools/rac1-projectile-hit-pattern-probe.py` and the retained managed live witness. Key loaded ranges are:

- `0x002c26c8..` — controlled item-10 class-`0xc0` update/admission;
- `0x002ace70..` — class-`0x79` constructor/source ownership;
- `0x002acfd8..` — item-10 launch handoff;
- `0x002adb30..` — class-`0x79` ballistic/contact update.

The older `0x002c1ad0 / 0x002a9ed0 / 0x002aa008` path is retained only as separate class-`0x4a` family evidence.

Unit coverage freezes the integrated admission/spawn contract in `Rac1BombGloveTests` and `Rac1WrenchCombatTests`.
