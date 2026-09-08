# Going Commando planet hopping

The first native Going Commando showcase: launch OBP, open a supported GC retail
ISO, pick a planet from a list, and it is reconstructed straight from the disc
and rendered in Godot with a walkable debug player. Return to the selector, pick
another, and it loads without restarting the process.

Branch: `claude/gc-planet-hopping` (from `main` @ `5d211df`).

## Runtime pipeline

```
GC retail ISO  (ordinary seekable file, bounded reads)
    │
    ▼  OBP.RAC2.GcIsoLoad.Identify         boot serial → SCUS-97268 gate
    ▼  OBP.RAC2.GcWorldImport.Build(level) native decode: tfrag / tie / shrub /
    │                                      moby / sky / octree collision / settings
    ▼  OBP.Runtime.RuntimeWorld            neutral: welded per-material meshes,
    │                                      RGBA textures, triangle-soup collision,
    │                                      atmosphere, spawn — no GC or Godot types
    ▼  OBP.Godot.RuntimeWorldScene.Build   ArrayMesh / StaticBody3D / materials
    ▼  game/ OBPGame.EnterWorld            unload old WorldRoot, add new, reset
    │                                      environment + camera, spawn DebugPlayer
    ▼  play
```

`GcWorldImport` is where GC-specific conversion **terminates**: `RuntimeWorld`
carries no Ratchet or Godot type, so the same `RuntimeWorldScene` /
`OBPGame` path will serve the future R&C1 and Up Your Arsenal importers. No
planet has a hand-made `.tscn`; every level is the same generic runtime scene.

## Selector data — `OBP.RAC2.GcPlanetCatalogue`

Planet / location names are the decoded global-string-bank help messages
(`research/GC_PLANET_NAMES.md`): planet names at help id `2885 + planet_name_index`,
the "*&lt;location&gt;, Planet &lt;planet&gt;*" infobot pairs at id `4592+`, and
the level → name rule is `planet_name_index == level_id` for LEVEL0–LEVEL20.
`LEVEL21.WAD` self-reports engine id 30; the ELF special table maps id 30 → planet 0
(Aranos). The catalogue is the **selector's** data only — `GcWorldImport` takes
any level id and never consults it.

## Deterministic level probe (all 27 GC level WADs)

`dotnet run --project src/OBP.Cli -- test-import <iso> --level N` on every id.
All 27 import without an exception. `kind` is classified from the ship spawn,
`is_spherical_world`, and geometry / collision counts.

| id | planet | location | kind | render tris | coll tris | native ship | death Y |
|--:|---|---|---|--:|--:|---|--:|
| 0 | Aranos | Floating Prison | hub | 1,113,250 | 81,113 | (placeholder) | 0 |
| 1 | Oozla | The Megacorp Outlet | planet | 1,881,339 | 328,923 | 278,115,411 | 0 |
| 2 | Maktar Nebula | Maktar Resort | planet | 1,209,706 | 243,664 | 509,116,313 | 105 |
| 3 | Endako | Megapolis | planet | 1,529,172 | 154,643 | 300,129,208 | 110 |
| 4 | Barlow | Vukovar Canyon | planet | 2,545,844 | 372,727 | 319,51,309 | 40 |
| 5 | Feltzin System | Thug Rendezvous | space | 433,960 | 529,274 | (placeholder) | 0 |
| 6 | Notak | Canal City | planet | 1,734,222 | 272,577 | 274,60,167 | 0 |
| 7 | Siberius | Frozen Lab | planet | 2,518,165 | 741,940 | 501,219,224 | 0 |
| 8 | Tabora | Mining Area | planet | 1,728,227 | 358,373 | 574,170,305 | 0 |
| 9 | Dobbo | Testing Facility | planet | 1,352,236 | 257,861 | 494,409,239 | 0 |
| 10 | Hrugis Cloud | Deep Space Disposal | space | 439,984 | 539,799 | (placeholder) | 0 |
| 11 | Joba | Megacorp Games | planet | 1,597,520 | 238,004 | 422,103,317 | 80 |
| 12 | Todano | Megacorp Armory | planet | 1,899,834 | 202,170 | 344,137,111 | 130 |
| 13 | Boldan | Silver City | planet | 1,119,922 | 303,578 | 321,109,414 | 99 |
| 14 | Aranos | *(return — unresolved)* | unresolved | 1,361,542 | 124,101 | 383,142,315 | 0 |
| 15 | Gorn | Thug Fleet | space | 302,283 | 322,117 | (placeholder) | 0 |
| 16 | Snivelak | Thug Headquarters | planet | 871,706 | 205,080 | 355,107,274 | 0 |
| 17 | Smolg | Distribution Center | planet | 1,444,642 | 361,366 | 382,151,222 | 60 |
| 18 | Damosel | Allgon City | planet | 1,603,249 | 239,968 | 545,109,227 | 95 |
| 19 | Grelbin | Tundor Wastes | planet | 3,135,658 | 451,282 | 240,306,327 | 0 |
| 20 | Yeedil | Protopet Factory | planet | 1,583,437 | 238,586 | 512,103,171 | 85 |
| 21 (id 30) | Aranos | Floating Prison | hub | 387,287 | 108,868 | (placeholder) | 64 |
| 22 | Feltzin System | Space Arena | space (spherical) | 635,342 | 35,513 | (placeholder) | 0 |
| 23 | Hrugis Cloud | Space Arena | space (spherical) | 519,012 | 38,075 | (placeholder) | 0 |
| 24 | Ship Shack | Slim Cognito | vendor | 40,833 | 6,318 | 100,51,99 | 0 |
| 25 | Starfield | scene stub | scene | 96,490 | 17 | (placeholder) | 0 |
| 26 | Gorn | Space Arena | space (spherical) | 658,070 | 88,430 | (placeholder) | 105 |

**LEVEL14** places tie instances ~21,000 units off the level (bounding box blows
up). `GcWorldImport` now derives the sky scale / death-plane / camera framing
from a *trusted extent* (tfrag + collision only), so a stray instance can't
inflate them; `world.Bounds` itself is still the full union. LEVEL14 is marked
`Unresolved` and kept out of the showcase.

## Showcase set (acceptance floor: 5 visually distinct worlds)

`GcPlanetCatalogue.ShowcaseLevelIds` = **Oozla (1), Endako (3), Tabora (8),
Siberius (7), Damosel (18)** — swamp / metal vertical city / rock desert / ice
lab / ornate domed canal city. Chosen for maximally distinct biomes that also
frame well from the native ship spawn. Boldan (sunset Silver City), Notak (storm
canal city), Grelbin (open tundra) and Barlow (cloud canyon) are strong extras
but spawn on the lip of a large open expanse, so the fixed showcase shot is
mostly sky.

## Atmosphere

`RuntimeWorldScene.ConfigureEnvironment` drives the Godot environment from
`RuntimeEnvironment`: the native background colour is the clear colour, and depth
fog uses the fog colour + near/far distances (÷1024, the TS `FOG_DISTANCE_SCALE`)
with density from `(1 − far visibility)` — the retail fog carries a far-plane
*intensity* (Oozla ≈ 0.70 visible, so its fog stays gentle) — and the end plane
stretched past the level so distant scenery still reads. Sky shells render under a
node the runtime re-centres on the camera each frame, drawn depthless so they are
a true backdrop. Exact PS2 GS fog/blend is not reproduced.

## Lifecycle

`--stress-switch oozla,endako,grelbin,oozla,boldan,…` loads each planet through
the generic path (bouncing through the selector every third hop), settles, and
logs node / object / orphan / memory counts. Verified: **0 orphan nodes** per
switch, `WorldRoot` descendant count returns to the exact per-planet value on
reload (no duplication), memory tracks the current planet and does not grow
across the run, player `IsOnFloor` after every load.

## Commands

```
tools/play.ps1                       # selector from $OBP_GC_ISO
tools/play.ps1 -Planet endako        # straight into one planet
tools/play.ps1 -Planet 19            # by level id
tools/capture-planets.ps1            # deterministic showcase capture set (scripted player)
tools/capture-planets.ps1 -Framed    # fixed overview camera instead
```

Godot user args: `--gc-iso <path>` (→ selector), `--planet <name|id>` /
`--direct` (skip the selector), `--test-scene player`, `--stress-switch <list>`,
`--capture-frame N`, `--capture-out <path>`, `--collision-debug`, `--verify-hash`.
In a loaded world, **Esc** returns to the selector; from the selector **Esc**
quits.

## Known visual gaps

- **Moby GS texture state** — *fixed* (commit 0ed4ffc, landed in the TS oracle
  too): a moby packet only emits an AD-GIF on a texture change, so AD-GIF-less
  strips now inherit the previous packet's `TEX0` instead of falling to
  untextured. Tabora's dune-floor moby went 85% untextured → 0.2%; Grelbin
  1.69M untextured tris → 94.
- **Moby per-vertex colour** — not decoded. A moby with a near-constant UV
  (Tabora's terrain samples one atlas texel) has no surface detail without it,
  so that dune still reads flat even though it is now textured. This is the next
  moby task.
- The sky is concentric shells parked on the camera each frame at a large fixed
  radius; they depth-test (never write) so buildings occlude them, and the
  textured cloud layers blend with their decoded edge alpha. The untextured
  gouraud backdrop shell is dropped (no per-vertex colour → solid blob) — the
  per-planet background clear colour is the backdrop behind the clouds.

## Acceptance

1. Launch OBP → 2. open the supported GC ISO (dialog or `--gc-iso`) → 3. the
selector lists every level → 4. pick Oozla → 5. walk / jump on reconstructed
collision → 6. Esc → selector → 7. pick Endako → 8. walk Endako → 9. load
Tabora / Siberius / Damosel / Boldan / Grelbin / … → 10. return to a
previously-loaded planet → 11. never restart the process. All worlds load from
the retail disc; no browser runtime, no pre-exported JSON, no hand-made
per-planet scene, no preconverted asset pack.
