# Native special-surface presentation: evidence and limits

Tracking: [R&C1 Aridia mud #232](https://github.com/je55pr/one-big-package/issues/232).

## What this change fixes

The static tfrag renderer previously used a decoded native vertex alpha of `128/255`
directly in Godot, although the PS2 GS uses 128 as the full-opacity alpha
reference. The host now remaps *tfrag* vertex alpha to the native 128 reference.
The neutral tfrag decoder and source RGBA remain unchanged.

The Godot material factory also previously scissored any tfrag texture with
a single below-half alpha texel. It now uses the existing content-derived
`MaterialModel.ResolveAlpha` policy for *tfrag only*, distinguishing opaque,
cutout and genuine translucent source textures. Explicit recovered native
blend-enable continues to take precedence. TIE, shrubs, mobies and sky retain
their previous policies. No collision, movement or damage code changes.

Local retail-backed texture evidence (texture IDs are **level-local**, never
shared across planets):

| Level | Example source texture | Decoded texture alpha | Current host policy |
|---|---|---|---|
| Aridia, R&C1 LEVEL2 | tfrag 50 | cutout plus significant partial alpha | Blend rather than discard all low-alpha texels |
| Gaspar, R&C1 LEVEL9 | tfrag 43 | cutout plus partial alpha | Blend |
| Pokitaru, R&C1 LEVEL11 | tfrag 37 | largely partial alpha | Blend |
| Same levels | ordinary sand/ground textures | opaque | Opaque |

The change is generic to Godot's shared tfrag material path, including later
game imports where the source actually supplies comparable tfrag materials.
It does **not** infer liquid type from a texture ID or collision-material class.

## Still missing: native liquid surface geometry

The Aridia screenshot still shows a large dark gap between the terrain
platforms after this alpha-only change. A local geometry probe sampled the
centroids of all 1,975 collision references labelled class-3 mud on Aridia;
only 82 had a rendered tfrag triangle within 1 world unit in height at the same
horizontal position (a diagnostic sampling result, **not** an exhaustive
render-mesh proof). A large region therefore cannot be repaired simply by
selecting different tfrag alpha or textures.

An existing source survey in `research/EFFECT_SYSTEMS_AUDIT.md` separately
notes that the OpenRAC renderer has a depth-writing **liquid strip** pass
distinct from ordinary world meshes. This is a research lead rather than
proof of the exact original R&C1 mud, Gaspar lava, Pokitaru water, GC Oozla
ooze or UYA Florana water packet layout. The native visual geometry, texture
bindings, UVs, blend and depth semantics must be recovered for each game
before implementing a generic liquid surface host.

**Do not** build a visual polygon from collision class 3, globally recolour
terrain, or treat every shallow-water collision face as the same shader.
Visible material identity is independent of gameplay contact classification.
This issue remains open until the true mud surface is visibly reconstructed.

## Reproduction

With the legally obtained local source ISOs configured in `OBP_RAC1_ISO`,
`OBP_GC_ISO` and `OBP_UYA_ISO`:

```powershell
./tools/capture.ps1 -Destination rac1:LEVEL2 -Player -Frame 260
./tools/capture.ps1 -Destination rac1:LEVEL9 -Player -Frame 150
./tools/capture.ps1 -Destination rac1:LEVEL11 -Player -Frame 150
./tools/capture.ps1 -Destination rac2:LEVEL1 -Player -Frame 150
./tools/capture.ps1 -Destination rac3:TABLE2 -Player -Frame 150
```

The tfrag alpha tests cover portable 128-as-opaque conversion and retail-gated
material profiles without distributing source textures. The exact source
rendering of liquid strips remains unverified.
