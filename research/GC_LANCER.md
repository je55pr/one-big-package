# Going Commando base Lancer

Authority: NTSC-U v1.01, `SCUS-97268`, `rac2-ntscu-v1.01`.
ISO SHA-256: `9db2e33e276133cc283647fa3279b37911955e123d6199d10065547eaa9b1ce5`.

This is a bounded GC weapon slice for issue #205. It recovers the ordinary
base Lancer's firing, ammo, projectile and presentation identities. World
collision queries and avatar attachment remain host inputs. Campaign grants,
XP/upgrades and Heavy Lancer are separate. No retail code or assets are stored
here. All addresses below are EE virtual addresses in the **loaded Oozla
overlay**, except explicitly identified boot data.

## Evidence and external leads

The observations come from the authorized disc's boot ELF, loaded
`/G/LEVEL1.WAD` sections, gameplay text bank, level-core model data and
`/G/GADGET.WAD`. They are static dataflow observations, not a claim of a live
PCSX2 timing/visual comparison. `GcLancerRetailTests` pins the instruction
arguments and complete function hashes; its normalized report is
[`generated/gc-lancer.json`](generated/gc-lancer.json).

The public [OpenRAC dispatch catalogue](https://github.com/OpenRAC/rac2-decomp/blob/2f5fe07d63ccf93e5387efea144e0b2755d701da/docs/moby-dispatch.tsv)
provided the class-1490 updater address as a research lead. Its
[function catalogue](https://github.com/OpenRAC/rac2-decomp/blob/2f5fe07d63ccf93e5387efea144e0b2755d701da/config/levels/functions.tsv)
helped delimit functions. The name is independently established below using
retail UI text. These GPL sources were not copied or translated into the
implementation. Existing OBP format readers perform the extraction.

## Native identity, ammo and animation

The boot byte map at `0x00139568 + index` selects a `0xE0`-byte descriptor at
`0x002637A0 + mappedIndex * 0xE0`. Slot **30** maps to record **30**, at
`0x002651E0`. The weapon allocator at `0x002B0608` uses descriptor `+0x14`
as the class argument to `0x00305EE8`, pairing the live Moby with effective
index `P+0x1248` and pointer `P+0x1220`.

| Field | Observation |
|---|---|
| `+0x08` | text id **2677**, whose US-English retail text is **Lancer** |
| `+0x14` | held class **1490** (`0x05D2`) |
| `+0x28` | grounded player firing/context sequence **56** |
| `+0x2C` / `+0x30` | `-1` / **62**; retained contextual animation fields, not universal clip choices |
| `+0x50` | speed **60.0** native units/second |
| `+0x54` | fire rate **7.0**; the updater uses its reciprocal |
| `+0x70` | projectile damage HP **0.5** |
| `+0x8E`, unsigned halfword | ammo capacity **200** |

`0x003A00C8..0x003A00D0` calls ammo admission `0x002C96B0(-1, 1)` and
branches on failure. That helper resolves `-1` to the selected native index,
looks up capacity, compares its count at boot/global `0x00139688 + index*4`,
and subtracts **one only on success**. `0x002C9738` clamps positive ammo
additions to descriptor capacity. The session's explicit initial-ammo argument
is an **OBP loadout choice**, not a reconstruction of starting inventory.

Class initialization at `0x003A0948` sets timer **0.1f**. The class updater
initially selects weapon state **2**. In that state it advances the timer at
`0x003A057C`, then changes to state **1**; it does not fall through into firing
in the same update. The ordinary trigger-held state-1 path calls the native
float countdown before checking trigger/ammo. Both successful fire
(`0x003A0290..0x003A02A0`) and failed ammo admission
(`0x003A0470..0x003A0498`) install **1.0f / 7.0f** as the next delay.

The float helper `0x002E3170` clamps to zero and returns a nonzero ready value
when the **old timer <= frame delta**; otherwise it subtracts the delta and
returns zero. The ordinary 60 Hz delta in boot data `0x001A7928` is the float
`0x3C888889`. Repeated binary32 subtraction matters: with this delta the
equip state consumes seven updates, first fire is update **8**, and a held
trigger fires again on **17, 26, 35, 44, 53**. These are deterministic
reductions of the native countdown at fixed 60 Hz, not emulator-measured
frame timestamps. Catch-up emission after a long host frame is not invented.
The session uses .NET binary32 arithmetic; exact EE FPU rounding and the
resulting startup-frame boundary have not been validated on hardware or in
PCSX2. The tested update numbers describe this portable realization, while
the 0.1 and 1/7 second scalars and countdown comparison are retail findings.

The global held model has sequences **0/1/2**, containing **11/1/5** frames.
The zero-initialized new Moby starts with sequence 0. The updater selects
sequence **2** on fire (`0x003A0178..0x003A0194`); after animation completion
it selects sequence **1** (`0x0039FFE0..0x003A0014`). The first-fire player
helper `0x002B0020` reads descriptor `+0x28` and uses a **five-frame**
transition for index 30 (`0x002B010C..0x002B0124`). This makes sequence 56 a
grounded firing/context observation, not a claim that all GC idle/equip,
crouch, airborne or upper-body states use it. The existing player base
selector still has default sequence **0**; its weapon-context path requires
the native context flag and reads descriptor `+0x28`.

## Projectile and damage

The base-class branch at `0x003A022C..0x003A0238` calls the projectile
allocator `0x003E38F8`. Its class argument is **3070** (`0x0BFE`).

* The origin comes from held-class joint **0**, transformed through the live
  Moby pose by `0x00307390`; the updater stores it at weapon PVar `+0x14`.
* Without a selected auto-aim target or a special aiming mode, allocation
  scales player aim vector `P+0x760` by descriptor speed **60 / 60 = 1**
  native unit per update (`0x003E39A0..0x003E39B8`,
  `0x003E3A88..0x003E3A94`). There is no ballistic gravity term in this path.
* Base Lancer sets range **20.0f** at `0x003A0A50..0x003A0A5C`, which is
  read into `f22` by its updater. Allocation divides this range by per-tick
  speed and converts to an integer lifetime: **20 travel updates**.
* The projectile updater `0x003E3C88` advances position, then calls the
  segment collision path `0x002D3C80` at `0x003E3ED8`. A collision stops
  gameplay travel. If clear, it decrements lifetime at `0x003E3F68`.
  Collision testing therefore precedes expiry on the final segment.
* Collision and expiry both enter state **1** with a **six-tick** effect tail
  (`0x003E3F44..0x003E3F98`); state 1 deletes the Moby after that tail. The
  portable session retires gameplay flight immediately and leaves this
  presentation tail to its consumer.

The travelling projectile constructs the following descriptor at
`0x003E3E50..0x003E3EDC`:

| Field | Value |
|---|---|
| damage flags | `0x00010000` |
| damage class / strength | **1 / 1** |
| damage index | live projectile class, **3070** |
| damage HP | descriptor `+0x70`, **0.5** |
| flags word | **1** |
| source | the projectile Moby, whose owner is the held weapon |

The native weapon updater also performs a separate near-muzzle overlap pass
through `0x002D68B8` with scalar **0.5**. That path writes the same flags,
class, strength and HP but damage index **1490**, because its source is the
held weapon (`0x003A02F0..0x003A03C8`). Both tuples are exposed in the profile;
the portable session emits the travelling-projectile tuple only. Native
near-muzzle query shape, damage-record merging/deduplication and the exact
role of that 0.5 scalar are **not fully recovered**. A host must not apply
both tuples to one contact without recovering that merge policy.

## Local assets and presentation requirements

The held model is **absent from Oozla's level-core class list**. It lives in
the global gadget container, independently of the destination world:

| `/G/GADGET.WAD` base Lancer row | Location |
|---|---|
| directory | header size `0x3C8`; row 15 at `8 + 15*20` |
| row fields | class **1490**, data sector **449**, data sectors **17**, sound-bank sector **466**, sound-bank sectors **9** |
| data header | two absolute byte-offset/size pairs, model WAD-LZ then texture WAD-LZ |
| decoded model | **1,325** triangles; GC Moby joint/sequence/packet layout |
| texture | one PIF2 **128 x 128**, native PSMT8 (**19**), header + 256-entry CLUT + pixels; held material slot **0** |
| gadget sound bank | 989snd **SBlk v1**, three sounds with decodable SPU ADPCM tones |

`GcLancerAssets.ReadHeld` reads these ranges using the directory identities,
validates nested bounds, decodes the model using the shared GC/UYA codec,
returns its native PIF2 texture and opens the separate sound bank. It takes
an `IRandomAccessReader`, not a machine path. Keep that reader alive while
decoding the sound bank. Retail assets must remain local/rebuildable.

The held mesh's stored vertices are in model bind space. `GcUyaGadgetPose`
reads the class joint table at header `+0x14` (0x40-byte stride), combines its
global 3x3 bind orientation and row-3 inverse translation, then applies inverse
bind followed by animated forward globals. The local translation column and
packed final word are not part of that inverse-bind affine matrix. The original
sequence-1 identity-quaternion pose preserves the stored vertex positions within
0.00002 native units, including the barrel extending to Y=-0.648397. Applying
animated inverse globals directly collapses those parts around their pivots.
This binds the rest geometry to retail matrices; extra animation channels and
an EE pose oracle are not claimed. Existing level-Moby decoding is unchanged.

The recovered fire call uses **class 1490, class-local sound 0** through
`0x00355828`; dry-fire uses **class-local sound 1** through `0x00355780`.
Projectile impact uses **class 3070, class-local sound 0**. These are native
source identities; a local sound id is not an arbitrary destination-level
bank index. Native dry-fire sound admission includes input/global gates not
represented by every `NoAmmo` result. The separate sound call at
`0x002CC168(0x00295C60)` and exact mix/pitch/envelope scheduling remain
unpromoted presentation work.

The projectile is in `/G/LEVEL1.WAD` lump 0, level-core class **3070**:
**24** mesh triangles, material mapped to **Oozla Moby texture 153**. Use
`GcMobyClasses.Read(core)` with `GcLevelTextures.Read(core, Table.Moby)`.
That texture index is local to this source level, not a universal texture
identity or a destination-level texture number.

GC held-to-avatar attachment and full GS/PIF material presentation are not
recovered by this slice. Hosts must label their attachment, material, flash,
trail, effect-tail and audio playback implementations as approximations until
verified. Existing decoders provide geometry; these identities do not claim
on-world rendering has been integrated or visually validated.

## Portable host usage and unresolved boundary

All source events carry `SourceGame == "rac2"` and native weapon index **30**.
The target retains its own source game. No `Rac1WeaponId` is used.

```csharp
var lancer = new GcLancerSession(initialAmmo: 200); // explicit OBP test loadout
lancer.Equip();

// Once per fixed 60 Hz tick, after converting host pose to native-unit scale:
GcLancerFireResult fire = lancer.Tick(triggerHeld, muzzleOrigin, conditionedAim);
foreach (GcLancerShot shot in lancer.ActiveShots)
{
    GcLancerSweep sweep = lancer.BeginProjectileTick(shot)!;
    GcLancerContact? contact = TraceFirstBlockingContact(sweep.From, sweep.To);
    if (lancer.TryCompleteProjectileTick(sweep, contact, out var impact)
        && impact?.Damage is { } damage)
        RouteSourceGameDamage(damage);
}

// Before travel/restart replaces the active world:
lancer.ResetWorld(ammo: 200);
```

`TraceFirstBlockingContact` and `RouteSourceGameDamage` are host adapter
placeholders. Sweep fractions are bounded to [0,1]; null contact is clear
travel, null target is blocking world geometry. The host decides which
objects block a shot. An entity hit emits intent; its target controller
decides consequences, including cross-game damage translation. A blocking
contact consumes the flight once. Stale/foreign shot and sweep objects are
rejected, even if numeric ids collide after a reset or in another session.
Weapon switching can leave existing flights alive in the same world; an OBP
world reset always invalidates them.

Native selected-target homing and special aiming modes are visible in
`0x003E39BC..0x003E3DC8` but **not implemented**. The session deliberately
supports the ordinary straight-aim branch. Aim acquisition, host collision
filtering, native overlap/merge semantics, special-mode animation,
progression, infinite-ammo cheats, Heavy/Mega variants, and on-world fusion
integration remain separate. The ordinary source-backed shot rules and
explicitly bounded host adaptations should not be described as a complete
GC combat controller.

## Reproduction

With the authorized source configured as `OBP_GC_ISO`:

```powershell
dotnet test tests/OBP.Tests/OBP.Tests.csproj -c Release --filter GcLancer
$env:OBP_UPDATE_GC_LANCER_EVIDENCE = '1'
dotnet test tests/OBP.Tests/OBP.Tests.csproj -c Release --filter GcLancerRetailTests
Remove-Item Env:OBP_UPDATE_GC_LANCER_EVIDENCE
./tools/test.ps1 -Configuration Release
./tools/test-retail.ps1
dotnet format OneBigPackage.sln --verify-no-changes --no-restore
```

The opt-in evidence regeneration writes only identities, derived numeric
observations, file-relative asset locations and hashes. It never writes
disassembly, source paths, models, textures or sample payloads. Normal retail
tests compare the checked-in report without modifying it. The full retail
suite separately verifies the GC disc against the exact authority manifest.
