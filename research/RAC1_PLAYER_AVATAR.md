# R&C1 Ratchet local-space player avatar

## Scope and authority

`OBP.RAC1.Player.Rac1RatchetAvatar` promotes the already recovered retail R&C1
Ratchet body into an engine-independent asset. It deliberately stops before
instance placement, OBP/Godot axis conversion, player-controller state, and the
post-sequence joint-controller overlay recovered around executable `0x2111c4`.

The primary authority is the user-supplied R&C1 NTSC-U retail image. The decoder
reuses `Rac1StaticClasses`, `Rac1MobyAnimation`, and `Rac1MobyPose` without
changing their evidence contracts. Class `0` remains the player body: 111 joints,
5,583 emitted vertices, 6,856 triangles, and a dedicated 256-slot sequence table.

## Exposed asset

The asset preserves the decoded class mesh, UV stream, skeleton, four texture
surfaces, and ten sequence-0 standing frames in native local coordinates. The
four retail Moby texture ids are `0, 1, 2, 3`; their triangle counts are
`5,356 + 966 + 404 + 130 = 6,856`. Each surface indexes the same 5,583-vertex
UV/frame streams, so an engine adapter does not need to duplicate or weld the
body to select materials.

Standing sequence `0` has ten frames and constant transition rate `0.125`.
The proven NTSC 60 Hz update path therefore gives `0.125 * 60 = 7.5 FPS`.
Sequence `122` remains a 21-frame, rate-0.5 bind-linear validation anchor; it is
not promoted as a player state or substituted for the standing loop.

## Native local frame, origin, and size

Coordinates remain the retail Moby coordinates: **Z-up**, with no `(x,z,y)`
world-import remap. The model origin is exactly native `(0,0,0)`; the decoder
does not recenter the body. Over the complete ten-frame standing loop, the local
AABB is:

- min `(-0.412822052533, -0.407482448369, -0.034460326260)`;
- max `( 0.362153156981,  0.305520219380,  1.395431938426)`;
- dimensions `(0.774975209514, 0.713002667749, 1.429892264686)` in X/Y/Z.

The standing-loop geometric base is therefore `Z = -0.03446032626043135`.
This is an evidence-backed **mesh/feet base reference**, not a claimed retail
collision capsule origin or semantic foot joint. Controller/collision placement
must not be inferred from it.

For comparison, the stored rest/body surface AABB is min
`(-0.791544569394, -0.906046518619, 0.001708984317)`, max
`(0.312174468534, 0.906046518619, 1.415893506462)`, with dimensions
`(1.103719037928, 1.812093037239, 1.414184522146)`. The class header's decoded
bounding radius is `1.748351732946`. The larger rest Y span is not used as a
standing collision shape.

The local convention exposed by the asset is `+X` right, `+Y` forward, `+Z` up.
The up axis is directly established by retail level/geometry coordinates and the
existing native placement transform. Forward remains a **model-local facing
convention**, not an OBP/Godot axis claim: native placement yaw is rotation about
Z, and the neutral body is left in its unrotated class orientation. As an
independent geometry check, sequence-0 frame 0's 130-triangle texture-3 surface
lies wholly on the positive-Y side (`Y = 0.216631..0.264068`). No engine-facing
correction is applied here.

## Local-versus-world validation boundary

`Rac1WorldImport.Moby.cs` is intentionally unchanged. Its existing path still
finds the single class-0 instance at index 0, poses sequence 0, applies
`Rac1Instances.TransformMobyPoint`, rounds to two decimals, and finally maps
native `(x,y,z)` to the current runtime `(x,z,y)` convention.

`Rac1RatchetAvatarTests.Level0_LocalFramesMapExactlyToExistingWorldRatchetPath`
proves the separation rather than assuming it: for every vertex of every one of
the ten standing frames, applying that existing placement/remap to the new local
frame reproduces the existing world-rendered frame exactly. Texture ids, UVs,
and per-surface indices also match the four world-rendered Ratchet surfaces.

The companion retail test pins the body counts, texture split, origin, axes,
rest/standing bounds, base height, frame count and 7.5 FPS timing. No retail
payload is committed; only these reproducible invariants are retained.
