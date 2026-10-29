# Maktar Galactic Gladiators arena: source-backed showcase

Authority: Going Commando NTSC-U v1.01 (SCUS-97268, `rac2-ntscu-v1.01`).
The existing authorised-source manifest supplies the exact disc hash. No
disc bytes, models, textures, screenshots or machine-local source paths belong
in this changeset.

## Recovered placement and import

`/G/LEVEL2.WAD` is Maktar Resort. Its native level header has present
lumps **0, 1, 2, 3, 4, 5, 7 and 8**. The existing R&C2 importer reads
level-core meshes/texture tables from slot 0, authored instances from
compressed slot 2, and chunked tfrag and collision from slots 4 and 5.
Slots 7 and 8 are audio containers and are not additional spatial chunks.
This feature does **not** manufacture replacement terrain or import art
from an outside project.

The original level starts at class-0 position approximately
`(499.9999, 319.6300, 116.6573)` in GC's **Z-up** axes; the
settings ship position is `(508.9314, 313.1104, 116.4543)`. Neither
is the Galactic Gladiators combat-floor entry. The raised stadium is
elsewhere in the *same level*.

Measured from the loaded R&C2 collision and converted to OBP's **Y-up**
world coordinates, the interior stage spans approximately X 475–520,
Z 580–620 at Y ~500. The validated floor samples at
`(X,Z)=(500,600), (505,600), (505,607)` have near-horizontal
triangles with interpolated height within one world unit of 500.
`Rac2MaktarArenaStaging` independently checks these samples before
admitting a debug player spawn at `(505,501.5,607)`. The extra height
is ordinary OBP placement clearance; this is **not** claimed to be an
authored GC campaign spawn or native arena entry animation.

In the nearby elevated volume (approx. X 450–565, Y 490–540,
Z 545–665) the existing retail world import includes native tfrag,
tie and moby render surfaces. A separate isolated collection of 260
placed Mobies near native `(X 315,Y 665,Z 355)` has no matching
floor support in the present imported collision. It must not be treated
as the walkable stage or given invented collision to make a screenshot.

## Bounded showcase staging

An opt-in test entry uses the normal world provider, world scene lease,
player, camera and existing R&C1-derived OBP common movement adapter:

```powershell
# With the authorised game sources already configured:
./tools/play.ps1 -Planet 2 -MaktarArena
# Equivalent Godot user arguments after its -- separator:
# --rac2-iso <authorised-disc> --planet 2 --test-scene player --maktar-arena-start
```

The `--maktar-arena-start` flag is accepted **only** with the explicit
diagnostic player scene and a LEVEL2 target. It fails if the loaded
world's game/build/level identity or its actual collision is wrong.
Normal Maktar entry, saved progress, arena opponents, Ratchet input
rules and all other planets remain unchanged. This staging is not
a newly recovered arena combat game mode.

For exactly reproducible source-backed **16:9** arena images:

```powershell
./tools/shots.ps1 -Game rac2 -List tools/shots/maktar-arena.json -Resolution 1600x900
```

The two capture-only cameras are explicitly authored OBP viewpoints
around the supported raised arena: neutral Y-up focus `(500,500,600)`,
one at 34 units/85° azimuth/13° elevation and the other at
38 units/280°/16°. Focus/distance are now opt-in validated fields
on the general shot-list schema, so ordinary world/showcase camera
placement is unaffected. The source data stays in the actual world:
there is no special-case renderer or synthetic floor. Captures omit
debug Moby placeholder markers and HUD overlays, which are not arena
scenery; other shot lists retain their previous defaults.

## Evidence and limits

On the current importer, an authorised LEVEL2 world has approximately
1,284,039 imported render triangles, 243,664 source collision
triangles, 449 native textures, 1,174 authored Moby instances and
1,818 authored tie instances (the exact Godot render/collision body
counts differ after welding/physics admission). The retail test checks
support, visible source mesh families, absence of floor under the
disjoint object cluster, source identity and failure without collision.

An actual Godot 4.7.2 .NET / GL Compatibility run entered Maktar via
the production provider, emitted 1600×900 screenshots and placed a
Ratchet avatar on the source stage. The ordinary PlayerHost reported
floor contact near `(-505,499.86,607)` in Godot scene coordinates,
consistent with the validated neutral source floor. The camera views
show the domed stands, native screen geometry, central round platform,
stage machinery and source-authored audience silhouettes.

Not yet claimed: native tournament progression, arena enemy AI, round
triggers, scorekeeping, full native audience motion, native player
weapon availability, exact camera track, full GS lighting/parity,
unresolved isolated-object collision, and original-game emulator
capture parity. These are separate gameplay or presentation tasks,
not licence to invent a simulation in #298.
