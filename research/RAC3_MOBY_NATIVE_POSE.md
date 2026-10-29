# R&C3 Moby packet-cache and first-pose recovery

## Visual bug and cause

A visual bookmark of the UYA Tyhrranoid mech shows displaced, fragmented
model components while the level terrain and player remain intact. The
ordinary RAC3 Moby importer had not opted into the source-backed packet
cache and native first-frame pose already recovered for Going Commando.
That left duplicated VU1 packet vertices missing and skinned actors on the
legacy bind-pose approximation.

## Recovery

- The RAC3 asset reader now requests the recovered shared GC/UYA packet
  decoder only for classes marked with the compatible native format byte
  at header offset 0x0b.
- A complete, supported regular sequence-0 first frame supplies the native
  joint rotation, translation and scale pose. Skinned vertices and normals
  are posed from source-local geometry and inverse bind matrices.
- Invalid/unsupported packet decoding falls back to the previous path.
  A *decoded but unpresentable* skinned mesh also falls back whenever the
  legacy class could be drawn. Retail table 11, class 7158, is the
  regression witness for this second case.
- `OBP_RAC3_LEGACY_MOBY=1` restores the former importer for A/B testing.
  Tests may also explicitly select either path without mutating process
  environment variables.

## Retail validation

On Veldin, classes 6836/6306/5821/6886 each recover additional source
vertices and faces and admit a finite native first-frame pose.
In five representative tables, expanded Moby triangle counts change:

| Table | Previous | Recovered |
| --- | ---: | ---: |
| 1 | 221,349 | 277,164 |
| 8 | 231,198 | 271,872 |
| 11 | 250,454 | 276,014 |
| 20 | 77,960 | 88,044 |
| 50 | 66,802 | 75,122 |

The retained 51-table production census explicitly checks the legacy
control. A separate 51-table native import sweep confirms no missing
authored instances, no reduction in presented Moby triangles, and no
change to static render or collision triangle counts. The recovered Veldin
animation preview preserves its 45 surface tracks
with finite native poses (including inverse bind and optional scale/translation
channels), while the source-complete packet cache raises its presented
triangle total from 34,301 to 37,922. The source sequence spheres are
approximate: class 6317 reaches 1.086x its authored sequence radius, so
native preview validation admits up to 1.10x while legacy remains at 1.001x.
The 51-level sweep separately checks that the set of drawable legacy actor
identities remains present after recovery, rather than relying only on the
aggregate triangle count.

This establishes improved source geometry and pose correctness. Jess
visually confirmed the captured Tyhrranoid mech and one-eye enemy are fixed
in regular gameplay. This does not prove visual equivalence with the
original hardware renderer or fix every special animation/attachment variant.
