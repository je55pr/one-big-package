# Trilogy world atmosphere contract

This note records the bounded atmosphere semantics used by the native Godot
runtime. It deliberately separates decoded retail metadata from OBP presentation
fallbacks. It is not a claim that Godot reproduces the PS2 GS lighting pipeline.

## Sky / background

All three supported trilogy importers carry recovered sky shell geometry into
`RuntimeWorld`:

- R&C1: retail-validated RAC/GC sky block, see `RAC1_SKY.md`;
- Going Commando: retail sky shells and textures, see `GC_SKY.md`;
- UYA: retail-corroborated UYA shell prefix plus shared cluster payload, see
  `UYA_RETAIL_SKY_COMPATIBILITY.md`.

Godot keeps those shells camera-centred as presentation policy. The shell bytes
remain native evidence; camera-relative placement is not presented as native
world geometry. Synthetic sky drift is an explicit, opt-in presentation fallback
and is disabled for normal world loads.

R&C1, GC and sampled UYA levels all expose native level-settings background RGB.
When that value is absent, OBP may use the recovered fog colour as the clear
colour, but that clear-colour choice is tagged `PresentationFallback` because
native evidence only establishes the fog use of that RGB. When neither exists it
uses the dark blue `WorldPresentation.DefaultBackground`, also tagged
`PresentationFallback`.

## Fog

The shared level-settings prefix carries fog RGB, near/far distances and near/far
visibility intensities. R&C1 and GC importer evidence already established the
`/1024` world-position interpretation. UYA retail corroborates the same
0x5c settings layout and the runtime now applies the same `/1024` presentation
conversion so `RuntimeEnvironment` consistently stores world units.

The UYA unit conversion remains a presentation interpretation pending stronger
native executable provenance; the underlying UYA settings values themselves are
retail-backed. This distinction is important when comparing captures.

Going Commando also has native environment samples and transition volumes.
A sample fog override is tagged `NativeEnvironmentSample`; otherwise fog comes
from `NativeLevelSettings`.

Godot's built-in depth fog cannot express the PS2 formula's independent near and
far visibility endpoints exactly. The bounded mapping therefore preserves native
fog colour and near/far planes, uses a linear depth curve, and derives Godot
density from `1 - farVisibility`. It does not stretch the recovered far plane to
fit OBP world bounds.

## Ambient light

GC environment samples carry a recovered hero/ambient colour. OBP applies that
colour directly to the Godot scene ambient and tags it
`NativeEnvironmentSample`. The static world meshes are predominantly unshaded,
so this does not re-light their baked/decoded colour.

No equivalent scene-ambient source is currently promoted for R&C1 or UYA.
Those worlds use white ambient only as an explicit `PresentationFallback`.
The runtime no longer lifts dark recovered ambient colours toward white.

Atmosphere recovery intentionally keeps tone mapping and colour grading neutral.
Those are renderer presentation choices, not recovered trilogy metadata.

## Runtime provenance

`RuntimeEnvironment` carries independent source tags for background, fog and
ambient values:

- `NativeLevelSettings`
- `NativeEnvironmentSample`
- `PresentationFallback`

`WorldPresentation.Resolve` preserves those tags in `PresentationState`, and
the deterministic presentation tests verify native pass-through, explicit
fallbacks, neutral post-processing and exact fog planes.
