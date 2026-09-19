# Simple world presentation effects

This note records the deliberately small native-data effect boundary used by OBP.
It is not a general VFX, particle, bloom or post-processing system.

## Native material effects

Recovered GC/UYA GS material state already reaches `RuntimeMaterialPresentation`.
When a native primitive enables blending, decoded additive source-alpha and fixed-alpha
equations map to Godot additive blending. Unknown equations remain unknown.

Fields named `glow_rgba` or `mode_bits` are not treated as emissive evidence. Their
render semantics are still unresolved, and brightness-derived emission remains a
presentation heuristic rather than native behaviour.

## UYA looping sky shells

UYA retail sky data preserves a signed three-component angular-velocity vector per
shell. Retail sampling proves non-zero values on multiple levels, including Veldin.
OBP now keeps each UYA shell in an independent presentation group and emits a neutral
`RuntimeAmbientAnimation` spin only when that shell's native velocity is non-zero.

The raw velocity is native evidence. The conversion
`value * (60 * 2π / 32768)` radians/second follows the documented public Wrench
interpretation for the NTSC-U authority and is kept source-specific in
`UyaSkyPresentation`. The native Z-up to OBP Y-up Y/Z swap is an odd basis
change, so angular velocity uses the corresponding axial-vector transform rather
than the ordinary position transform. Godot applies the same rule for its X mirror.

`RotationRaw` is still retained by the decoder but is not applied: the exact native
rotation composition/order has not been recovered. The UYA sky bloom flag is likewise
preserved but not promoted into additive or post-process bloom. Sky sprites/fx remain
outside this bounded slice.

GC and R&C1 use the older shell prefix and declare no equivalent shell velocity, so
normal loads for those games gain no invented motion. The existing `AnimateSky`
option remains an explicit synthetic presentation fallback and stays disabled by
default.
