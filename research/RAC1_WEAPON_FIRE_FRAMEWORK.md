# R&C1 common weapon-use / firing framework

Authority: NTSC-U original retail `SCUS-97199`, using the already-authorized local retail input and retained Veldin savestates. This note retains addresses, derived control flow and deterministic semantics only; no retail payload bytes are stored.

## Scope and result

The reusable boundary is a **weapon-use admission result**, not one universal weapon state machine. Wrench item 8 and ranged item 10 both begin from a semantic primary-use request in OBP, but retail gives them materially different timing and spawn paths:

| fact | wrench / item 8 | first ranged / item 10 |
| --- | --- | --- |
| ammo | no ranged-ammo mutation | one round per accepted use |
| recovered timing owner | player action/contact window | class-`0xc0` weapon object |
| admitted player presentation | action `0x13`, sequence 23 | accepted-fire sequence 44 |
| projectile staging | none in recovered ordinary swing | class `0x4a`, state 0, staged before use |
| repeated-use gate | no common ranged cooldown promoted | independent 20-tick fire gate and 10-tick projectile rearm |
| facing/origin | first-swing lunge follows live Ratchet yaw | exact class-`0xc0` launch origin recovered from Ratchet position/yaw; downstream aim vector remains a separate weapon path |

Accordingly `Rac1WeaponUseAdmission` is intentionally small. It reports accepted/rejected use, optional ammo delta, and an independently recovered native player sequence id. Numeric cadence and contact/spawn state stay in their weapon-specific controllers.

## Item-10 admission and ammo ordering

The loaded item-10 weapon update starts at `0x002c1ad0`. The accepted-use branch at `0x002c1c28..0x002c1cd8` first consults the weapon's fire timer at PVar `+0x58`, applies the surrounding player/input gates, and only reaches the use consequence when those gates pass.

The accepted branch then:

1. calls `0x00216de8` with argument `0x1a` and zero as its second argument;
2. calls `0x00233db8(-1, 1)`, the already-recovered one-round ammo consumer;
3. writes native weapon state `3` to class-`0xc0` Moby `+0x20`.

The `0x1a` argument is retained only as an executable fact here. This recovery does **not** promote a semantic name for that helper call. Separately controlled class-0 Ratchet traces prove that every accepted item-10 use selects sequence 44 and that zero ammo does not enter sequence 44. Therefore sequence 44 belongs on the accepted-use result without making animation playback authoritative for fire cadence.

A rejected request consumes no ammo and starts no new 20/10-tick gates.

## Projectile staging and ownership

The item-10 weapon object owns staging rather than asking a host projectile service to invent it.

At `0x002c2420..0x002c2444`, when the class-`0xc0` weapon has no staged object and its rearm/ammo gates permit one, it calls constructor `0x002a9ed0`. The constructor:

- creates native class `0x4a`;
- initializes its native state byte to `0`;
- receives the class-`0xc0` weapon Moby as its first argument;
- stores that owner Moby pointer at projectile PVar `+0x30`.

The constructor result is stored by the owner at weapon PVar `+0x50`. Thus the retained ownership chain is explicit:

```text
class 0xc0 weapon Moby
  PVar +0x50 -> staged class 0x4a Moby
                   PVar +0x30 -> owner class 0xc0 Moby
```

State 3 later loads exactly that staged pointer from owner PVar `+0x50`, passes it to `0x002aa008`, clears `+0x50`, and the launch routine writes projectile state `1`. This is why `Rac1WeaponSpawnOwnership` is attached to the Bomb Glove projectile instead of represented as a game-wide projectile assumption.

After launch, the owner writes the time-helper result for 10 ticks to PVar `+0x54` and 20 ticks to PVar `+0x58`, then enters native weapon state 4. These are distinct gates: a replacement projectile may be staged before a new fire use is admitted.

## Origin, muzzle and facing boundary

Retail disproves the current temptation to reuse the wrench-facing helper for item 10, and the launch **origin** is now recovered numerically.

The class-`0xc0` update builds `sp+0x70` at `0x002c1db8..0x002c1e4c`. Its sources resolve against the already-recovered player-global base `P = 0x0013f350`:

- `P+0x80 = 0x0013f3d0` is Ratchet's native XYZ world position;
- `P+0x670/+0x674 = 0x0013f9c0/+4` is the planar heading pair;
- the fixed savestate has heading `(0.4437280595, 0.8961613774)` while live Ratchet `Moby+0x48 = 1.1110417843`, agreeing with `(cos(yaw), sin(yaw))` to below `1e-7`.

The update calls the retained quadrant-aware angle helper `0x001ff8b0` on that heading, wraps an added raw-f32 angle `0xbeb953df = -0.3619680107` through `0x002000e8`, then uses the retained cosine helper `0x001ff7e8` plus its adjacent complementary planar-trig helper to construct a radius-`0x3f5be9fb = 0.8590390086` XY offset. It vector-adds Ratchet's position through `0x001ff278`, then adds raw-f32 native-Z lift `0x3efb4cc2 = 0.4908199906`.

Thus the equivalent recovered origin law is:

```text
launchYaw = yaw - 0.3619680107
origin.x = player.x + cos(launchYaw) * 0.8590390086
origin.y = player.y + sin(launchYaw) * 0.8590390086
origin.z = player.z + 0.4908199906
```

For the retained Veldin witness `player=(154.4383087158,119.9104232788,29.484375)`, `yaw=1.1110417843`, this yields `(155.0674000825,120.4953951334,29.9751949906)`. `Rac1BombGlove.ResolveLaunchOrigin` freezes that formula and the three raw f32 constants. The Godot host now applies this recovered relative origin through the existing native-Z-up planar basis conversion instead of the former synthetic `+1.1/+0.8` muzzle offset.

State 3 then calls `0x002c1928` with `a0 = sp+0x70`, `a1 = sp+0x80`, and the projectile PVar. That routine writes the launch-direction fields in the projectile PVar before `0x002aa008` receives the origin, staged projectile, and PVar. The upstream `sp+0x80` aim point depends on another class-`0xc0` vector path through `sp+0x100`; that **direction/aim construction remains unresolved**. OBP therefore uses the recovered muzzle origin but keeps visible projectile direction as an explicit yaw-derived host fallback until the aim path or ballistics task recovers it.

The pre-arm constructor is separately called with the owner Moby, owner PVar `+0x40`, and `sp+0x100`. The core spawn contract retains `UsesDedicatedWeaponLaunchFrame = true` so later weapon work cannot collapse these separate origin/aim paths into the wrench-facing rule.

## Wrench contrast

Ordinary wrench use remains a player-action/contact contract:

- native action `0x13`, profile 0;
- admitted player sequence 23;
- first-swing contact age `17..23`;
- first-swing movement/facing evidence follows live Ratchet yaw;
- no item-10 ammo mutation, class-`0x4a` staging, or 10/20-tick ranged gate is promoted onto it.

`Rac1WrenchCombatController.AdmitOrdinaryUse` therefore returns the same semantic admission envelope but keeps all wrench timing in the existing action/contact controller.

## Movement and animation integration

Gameplay remains authoritative over presentation. A primary input request is not itself an R&C1 attack animation admission.

For wrench, the host opens the recovered ordinary swing and only then pulses the neutral `PrimaryAttack` presentation role, whose admitted R&C1 clip is sequence 23.

For item 10, accepted fire reports native selector 44 through `Rac1WeaponUseAdmission`. The current neutral avatar provider does not decode/play sequence 44 because its playback timing is not yet promoted. Rejected item-10 requests therefore do not fall through to the wrench sequence-23 animation, and accepted shots do not invent sequence-44 timing.

This keeps three clocks separate: movement/controller state, weapon-use/contact cadence, and presentation playback.

## Reproduction

The retained addresses can be reproduced by loading the authorized SCUS-97199 savestate EE memory and disassembling these ranges:

- `0x002c1ad0..0x002c2470` — class-`0xc0` update, admission, staging and state dispatch;
- `0x002a9ed0..0x002aa008` — class-`0x4a` constructor and owner link;
- `0x002aa008..0x002aa2a8` — state-3 launch handoff and projectile state 1;
- `0x00216de8..0x00216e44` — helper called by the accepted-fire branch.

Unit coverage freezes the promoted contract in `Rac1BombGloveTests` and `Rac1WrenchCombatTests`.
