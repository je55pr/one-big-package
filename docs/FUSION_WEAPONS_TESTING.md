# Three-game weapons in Quick Select

The GC base Lancer and original UYA V5 Shock Cannon appear in ordinary R&C1
Quick Select by default, after the owned R&C1 arsenal. No testing-mode toggle is
required. This is deliberate OBP equipment availability, not recovered native
cross-game progression. R&C1 ownership, equipped gear, ammo and campaign saves
remain source-owned; foreign ammo stays session-only (Lancer 40, Cannon 20 on
world entry). Shared weapon models, textures and audio from all three attached
games prepare in the background during Game Sources setup, and remain available
across level changes. A shared status notice shows verification progress; gameplay
and menus remain responsive, and firing waits until preparation completes. Equipping
does not start a disc scan or decode. Changing a source cancels outdated work and
rebuilds the shared resources. Missing sources produce actionable status and prevent firing.

Attach the supported original NTSC-U sources in Game Sources. For an isolated
fresh Veldin fixture, choose Pause -> Testing Equipment -> Start Three-Game Test. This
reloads the ordinary starting point with a temporary campaign and finite ammo:
Bomb Glove 10, Lancer 40, Shock Cannon 20. Leave Three-Game Test reloads normal
Veldin with the prior campaign inventory, bolts and progression restored. Full
world unload also retires the test state. Normal launches retain the ordinary
retail R&C1 inventory alongside the two session-only foreign weapons; nothing
is granted to saved inventories.

For a direct fresh entry, append `--fusion-test` to the normal Godot arguments
after `--`. Its default destination is `rac1:LEVEL0`; other starting destinations,
compositions and R&C1-only movement-state loads are rejected. Attach sources
normally or pass `--rac1-iso`, `--gc-iso`, `--uya-iso` with authorised local paths.
First use verifies each attached disc's full SHA-256 against its original
authority, then saves a payload-free receipt in local app storage. Later launches
check file size, write/creation timestamps and 16 distributed 64 KiB samples
(at most 1 MiB per disc). Matching files reuse that successful verification;
changed metadata/samples or a missing/corrupt receipt require a full scan. This is
an explicit OBP trust policy, not a fresh full-file proof on every launch.
Preparation remains off the game thread. Recording waits for shared preparation before
level entry so the header identifies all three builds. Missing or modified
sources leave an actionable status in the selector/HUD and disable that weapon.

Hold Triangle / Q, select a direction and release to equip. On a fresh opening,
the positions are up (Bomb Glove), up-right (Lancer), right (Shock Cannon). Further
owned R&C1 weapons retain their ring order before the two additions. Movement is owned by
the live Quick Select overlay until release. Circle / E uses the selected weapon;
Square / X remains the wrench. Double-tap Triangle / Q swaps the last two source-
qualified selections. Keyboard `[` / `]` cycling follows the same three entries.
The HUD shows the original game, actual ammo and current charge/recovery status.

Global weapon skins use their class inverse bind matrices followed by the animated
forward joints. Their stored model-space vertices must not be transformed by
animated inverse joints alone, which folds the barrel and handle together.
The R&C1 avatar holds a gun pose while foreign equipment is selected. Rigid grip
alignment preserves source dimensions; the Cannon has an explicit OBP quarter-roll
and palm offset. The source games' Ratchet-to-weapon mounts remain unrecovered.
Visible barrel endpoints are OBP anchors measured from decoded rest geometry;
they move with the grip so projectiles and beam feedback follow the held model.

## Authority and deliberate OBP policies

| Weapon | Original source behavior | OBP target admission |
|---|---|---|
| Bomb Glove (`rac1/item:10`) | Existing R&C1 throw, projectile, blast and ammo session | Existing R&C1 target controllers |
| Lancer (`rac2/item:30`) | `GcLancerSession`: equip delay, 60-Hz cadence, one ammo, straight projectile sweep/lifetime, 0.5 damage | Ordinary bolt/ammo/health crates; Veldin class1440 robots retain fractional health and their own hit cooldown |
| Shock Cannon (`rac3/item:43`) | `UyaShockCannonSession`: V5 spread, charge, windup/recovery, query coalescing, 5 maximum spread damage with native falloff, charged beam 50 | The same crates/robots; lethal class749 toad contacts |

Original damage amounts pass through without scaling. Foreign flags/classes are
retained on the neutral intent and explicitly translated to an ordinary R&C1
player-damage envelope; they never become same-numbered R&C1 identities. Native
R&C1 consumers decide acceptance, reactions, death and rewards. In particular,
metal crates are not admitted by the foreign-weapon adapter. Class749's current
consumer only supports a lethal consequence at health 1.0: a Lancer 0.5 contact
is a blocking contact without damage, and weak distant Shock rays are rejected.
No invented nonlethal toad behavior is supplied. Source-game inventories, weapon
XP, upgrades and vendors are outside this demo.

Host aim assistance, bounds/sphere/segment queries, occlusion, cross-game target
eligibility and the projection of Shock query angles are OBP policies. Spread
uses a separately seeded deterministic host RNG; it does not claim to reproduce
UYA's global RNG or the unresolved native Euler selector. The UYA beam's mode-bit
admission for a foreign target is an explicit bridge, not an R&C1 field reading.

Held models, skins, textures and audio come from each original local source. GC
also supplies the original class3070 projectile; UYA supplies its original beam
mesh projection and FX textures 10/23. No R&C1 gun or effect is substituted for
foreign assets. The GC/UYA guns are mounted on the R&C1 avatar's sampled hand
using an OBP transform and its existing gun pose. Source held-to-Ratchet mounts
and UYA held-clip selection remain unrecovered. UYA spread uses bounded blue
line feedback because its native spread/muzzle renderer is not yet decoded.
Lancer muzzle/impact particles and its separate projectile impact sound are not
implemented. Source sound programs are decoded with a fixed seed; scheduling,
gain and shared PCM synthesis retain the existing host approximations.

Unequip stops Cannon charge/beam/audio. Lancer shots already in flight keep their
source lifetime across weapon selection. Death, environmental restart, full level
reset, world exit and application/replay exit clear foreign flights, pending
contacts, effects and owned audio. The test's campaign remains isolated throughout.

## Recording and validation

```powershell
# Human input recording with default Quick Select; all sources must be attached.
./tools/record-playtest.ps1 -Destination rac1:LEVEL0
# Add -FusionTest for the separate isolated 10/40/20 fixture.
# F8 marks; F9 saves. Use the resulting local tape path:
./tools/replay-playtest.ps1 -Run <local-tape-path>

# Automated staged contacts, ordinary input tape and fresh-process replay:
./tools/fusion-weapons-smoke.ps1 -Rendered

./tools/test.ps1 -Configuration Release
./tools/test-retail.ps1
dotnet format OneBigPackage.sln --verify-no-changes --no-restore
```

Recordings include the versioned loadout (which fixes starting ammo), original-
damage policy, all three source build IDs and verified full disc hashes. Playback
automatically requests the recorded ordinary or isolated loadout and rejects different/missing sources or
unsupported configurations even with a build-change override. Checkpoints include
source-qualified selection, per-weapon ammo/state, charge/beam counters, flying
projectiles and accepted target damage. No model, executable, disc, texture,
audio, source path or capture payload is added to the tape or repository.

The automated gate separates two kinds of evidence. Its ordinary input recording
starts at the authored Veldin entry and demonstrates selecting/firing all three,
charge and movement recovery without staging. Its contact fixture moves original
crate/robot objects to a separate test floor to assert damage, occlusion and
cleanup deterministically. That fixture is not evidence of an ordinary played
route or retail trajectory. Local screenshots, tapes and reports stay under
ignored `captures/`; they must not be committed.
