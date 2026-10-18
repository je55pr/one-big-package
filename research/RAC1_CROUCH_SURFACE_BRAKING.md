# R&C1 crouch braking: ordinary ground and ice

Authority: original NTSC-U disc, `rac1-ntscu-original`. This is offline source
recovery from independently mapped Veldin and Hoven overlays. No new PCSX2
experiment or matched retail slide-distance measurement was performed.

The user reports that OBP's run-to-crouch slide still travels too far and asks
whether floor types could supply missing braking. The stopping-time feedback
does not establish that the distance is correct.

## Recovered surface flag

The collision-face byte's low five bits identify a surface class. The existing
decoder retains effect bits separately. Class 7 identifies ice; the retained
reachability test finds 2,777 class-7 triangles on Hoven. Class 0 is a water
classification, **not** the name of the ordinary-ground default below.

Here `P = 0x13f350`. Surface refresh saves the preceding P+0x12e0 class, clears
16 bytes beginning at that field, restores the separate P+0x12ed effect mode,
and sets P+0x12e0 to -1 for the subsequent contact query. It sets P+0x12e2 to 1
when the saved class equals 7. Thus P+0x12e2 is an ice flag, with zero as its
default; it is not an unspecified equipment flag or an arbitrary friction index.

| Source block | Veldin address | Hoven address |
| --- | --- | --- |
| Surface-flag refresh entry | `0x20b960` | `0x22de30` |
| Class-7 comparison | `0x20bb74` | `0x22df98` |
| Store 1 to P+0x12e2 | `0x20bb84` | `0x22dfa8` |
| Crouch coefficient selection | `0x217f4c..0x217fa0` | `0x238ed4..0x238f28` |
| Residual carry decay helper | `0x216710` | `0x2378a8` |

Hoven has a different surface-refresh implementation and different code
addresses. Its local producer was traced separately. The coefficient-selection
block is byte-identical at the two mapped addresses, SHA-256
`4d24b0a795c0c6bd58937b9cfcbe88ba6e65695d414c6e9e6a1b6135c63aa4cf`.
Reusing Veldin virtual addresses in Hoven would inspect unrelated instructions.

## Default versus ice braking

For action 4, the movement executor selects:

| Ice flag P+0x12e2 | Coefficient | Nominal decrement per update at 60 Hz |
| --- | --- | --- |
| Zero: ordinary default | `6.48f` | approximately `0.0018` |
| Nonzero: ice | `1.7f` | approximately `0.00047222` |

Both coefficients multiply the same squared-update scalar at GP-0x7e90. The
verified boot value is binary32 `0x3991a2b4`, approximately 1/3600. The selected
decrement is passed to the scalar approach-to-zero and residual-carry helpers.
The latter removes vertical carry, subtracts from planar magnitude with a zero
clamp, preserves heading and adds the resulting carry to the motion request.
Its existing small-magnitude snap is separate from surface selection.

Ordinary braking is about **3.81 times** the ice coefficient. This supports a
longer slide on ice under otherwise equal conditions; it is not a measured
3.81-times gameplay distance. Contact correction, entry state and action changes
also affect the trajectory. The nearby `12.6f` constant is a different-action
fallback, not another ordinary-floor crouch coefficient.

No additional material-indexed braking coefficient is loaded in this action-4
branch. The surface refresh maps other classes to flags, and water, mud and
Magneboots have distinct recovered action routes. This does not establish a
universal friction table or prove that all other surfaces behave identically.
The contact executor and alternate action controllers remain incomplete.

## OBP consequence and remaining distance question

OBP already uses the retained ordinary-ground decrement `0.001802944` per
update. It is close to the recovered nominal default, and substantially larger
than the ice decrement. The current long slide is therefore not explained by
accidentally selecting the ice braking coefficient. This finding does not
justify multiplying ordinary braking by another guessed floor factor.

The [contact-derived entry correction](RAC1_CONTACT_MOTION_CARRY.md) remains a
bounded Godot adapter. Complete native contact correction, terrain-dependent
requests and action-entry alignment still need scrutiny or a matched retail
trajectory before declaring the clear-ground coast correct. No braking value
or playable surface behavior was changed by this recovery. The ordinary OBP
controller still rejects the unsupported ice-slide action; recovery of this
coefficient alone does not implement Hoven's complete ice movement.

## Reproduction and checks

`tools/rac1-level-overlay-probe.py` first verifies the complete canonical ISO.
It resolves native level identities through the bounded disc directories,
verifies the complete overlay hashes, and maps each overlay's serialized
sections separately. Its schema-4 payload-free report retains both levels'
flag, coefficient and carry blocks, literal values, boot timing scalar and
scope limits in `generated/rac1-level-overlay-probe.json`.

The C# source checks independently load both overlays through `Rac1DiscIndex`
and `Rac1LevelData`, map their section headers, and pin the default flag clear,
class-7 predicate, ice store, normal/ice literals and carry-helper argument.
The existing special-surface reachability test independently checks Hoven's
class-7 geometry. Python tests cover the generalized level lookup, including
identity-versus-slot selection, missing/duplicate identities and bad bounds.

Use the reproduction command in [the overlay guide](RAC1_LEVEL_OVERLAY.md).
Raw bytes and local logs stay in ignored storage; the report exports no retail
payload or machine-specific source path.

Validation on 2026-10-18: portable C# 1,011 passed / 124 retail-dependent skipped;
full canonical retail-backed C# 1,135 passed / zero skipped; Python tooling 81
passed. The authority-verified report was regenerated. Formatter verification
and diff checks passed. Local logs use ignored `captures/crouch-surface-*.log`.
No gameplay smoke was repeated because no runtime behavior changed.
