# R&C2 native Moby pose and packet-cache recovery

## Implementation

The GC/UYA packet decoder previously discarded persistent VU1 duplicate-cache slots between packets
and misread the seven-record pipelined index epilogue. The corrected decoder reconstructs those
source vertices and preserves topology and per-triangle GS material states.
Per-class format byte +0x0b selects eligible GC packets. Two embedded R&C1-format classes
across the 27 LEVEL files are excluded by their source format, not by class IDs.

Original regular animation frames include quaternion rotations, inherited and post-applied
scales, and per-joint translations. The decoder now retains those optional source fields,
along with frame time and sequence metadata. The new engine-neutral native pose evaluator
combines an animated forward skeleton pose P with original inverse bind matrix S (P x S),
using full source vertex skin weights. It evaluates one native keyframe at a time.

The R&C2 world importer uses native sequence-0 first-frame pose for classes with complete
decoded source skin bindings. Unsupported classes retain the older path. Legacy import
can be selected using OBP_RAC2_LEGACY_MOBY=1, for regression investigations.
This change does not opt R&C3 into the new default.

## Retail verification and visual sign-off

- 27 LEVEL files, 4,560 class records; 4,558 normal-format packet sources decode,
  756 supported initial poses change, and no other class meshes are affected.
- Independent source audit verifies all vertices and 4,543 source triangle triplets
  for the two damaged Oozla creatures (classes 2466 and 2859).
- 833 regular frames contain 1,687 scale and 2,582 translation records, all
  validated individually against retail source. All frames pose to finite vertices.
- Of 1,137,308 crab/snail positions, six exceed source sequence spheres by slightly
  more than 0.1%. For the smaller creature, zero of 374,945 exceed tolerance.
- The separate 32-class Oozla sample (316 poses, 469,822 vertex positions) had
  352 slight authored-bound outliers among four classes. Do not hide these.
- The 27-level differential test verifies matching geometry indices, textures,
  UVs, normals, GS states, skin bindings and class inventory in preview/control.
- Fusion Lab Aranos MSR I (class 2827) has 2,953 recovered vertices versus
  2,760 in the original decoder. Its chase/attack/recovery clips now use matching
  recovered source mesh data. The class-2755 opening door verifies 124 recovered
  triangles versus its preserved 120-triangle legacy witness, with matching clips.
- Jess visually confirmed Oozla enemies/NPCs/bosses and another R&C2 level
  display correctly. Fusion Lab MSR I looks retail-correct and its original
  animations play properly; all three source actors register.

## Limitations

This does not implement arbitrary native enemy AI state selection, general frame
interpolation, animation layers, attachments or unsupported special frame variants.
Existing specialized door and MSR I animations use the recovered source stream.
Source references: OpenRAC editor/moby_anim.py and port/assets/geometry/moby_animation.cpp;
Wrench GC Moby vertex decoder. Issue #434.
