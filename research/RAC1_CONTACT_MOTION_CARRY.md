# R&C1 contact-derived crouch carry

Authority: original NTSC-U Veldin overlay, independently mapped and hashed by
`tools/rac1-level-overlay-probe.py`. This follow-up uses static code recovery,
source-backed helper contracts and OBP host tests; no fresh PCSX2 trial.

## Fields and construction

Here `P = 0x13f350`. The historical movement notes' `G = P+0x80` use different
offsets. Keeping these bases distinct matters:

| Field | Role |
| --- | --- |
| P+0x80 | Current world position |
| P+0xe0 | Ordinary motion request, including terrain/downward terms |
| P+0x90 | Separately applied external translation |
| P+0x100 = G+0x80 | Final position delta, including the external pass |
| P+0x110 | Contact-derived controlled-motion vector |
| P+0x150 | Independent residual carry copied on crouch entry |
| P+0x160 / 0x164 | 3D / planar lengths of P+0x110 |

`0x213880` saves the initial position. It applies the ordinary request and runs
primary contact correction. At `0x213ae0..0x213b5c`, the helper constructs
P+0x110 from that primary position delta `d` and the original request `r`:

```text
u = setLength(d, 1)
k = max(dot(u, r), 0)
P+0x110 = setLength(r, k)
```

Zero-length setLength input returns zero. This is a directional projection
coefficient, not a clamp to actual distance. For example, request (3,4,0) and
actual primary direction (1,0,0) produce (1.8,2.4,0), not (3,0,0).

The source argument in the delay slot at `0x213b58` is **s4, the original
request**; s1 points at the normalized displacement. Missing this distinction
would give the right length but wrong direction. The source-backed test pins
that instruction and hashes the whole block plus the set-length, dot and
magnitude helpers. VU macro opcode interpretation was cross-checked against
[PCSX2's R5900 decoder](https://github.com/PCSX2/pcsx2/blob/master/pcsx2/DebugTools/DisR5900asm.cpp);
the recovered game behavior comes from the verified retail bytes.

`0x213c64..0x213d08` then applies P+0x90 in a separate contact pass and writes
the final position delta P+0x100. This proves that final world displacement,
controller target speed and P+0x110 are not interchangeable inputs.

## Crouch ownership

Ordinary action-4 entry copies P+0x110 into P+0x150 at `0x22394c..0x223958`.
`0x25f730` caps its full XYZ length at `7 * secondsPerUpdate`. At the original
60 Hz timing scale this is 7/60 units, above ordinary full-run displacement.
The subsequent carry helper removes vertical carry and applies its existing
planar decay. It does not keep copying new P+0x110 samples while crouch is held.

This differs from OBP's previous entry, which simply retained the controller's
planar momentum even if contact had deflected or stopped actual movement.

## Implementation boundary

`Rac1ContactMotionProjection` owns the recovered projection and full-length cap.
`Rac1RatchetMovementController` records completed contact feedback and consumes
it once, on grounded crouch entry. Reset and action cancellation clear it.
Later contact samples do not overwrite the active crouch carry.

The Godot adapter supplies its admitted ordinary request and actual position
delta after MoveAndSlide. Vectors are converted to the controller's planar X/Y
plus vertical Z order. The native geometric projection is rotation invariant,
so the host's mirrored planar basis does not change its dot/length result.
The prospective support-rise envelope is excluded from the request because it
is host placement policy. Recording does not replace the ordinary run scalar.

Feedback is currently admitted only for static ordinary motion without support
or conveyor translation. Godot combines collision passes, so subtracting external
transport from a combined result would not reproduce the recovered two-pass
construction. Other cases clear the sample and retain the existing movement
fallback. GC/UYA do not supply this R&C1-specific feedback.

This is a bounded adapter: Godot collision/depenetration still supplies the
primary delta, action-entry timing remains approximate, and double arithmetic
is not a full R5900/VU floating-point model. The projection contract is recovered;
the complete retail contact solver is not implemented.

## Distance and validation boundary

The change is not an arbitrary stronger brake. The existing measured crouch
decay remains .001802944 per update. Contact-aligned movement can retain almost
all of its momentum, so this correction need not substantially shorten every
clear-ground slide. Oblique or blocked motion can produce much less entry carry.

The subsequent [surface audit](RAC1_CROUCH_SURFACE_BRAKING.md) identifies the
native default as `6.48f` and the class-7 ice alternative as `1.7f`, both scaled
by squared update duration. OBP already uses the ordinary-ground rate. No extra
ordinary-floor multiplier was found in that branch, and distance parity stays
open.

Pure tests check the request-direction distinction, downward request with a
floor-cancelled vertical displacement, zero/opposed motion, full XYZ cap,
one-time entry ownership and cleared history. A synthetic wall in the headless
Godot smoke checks that blocked run momentum does not reappear after entering
crouch and removing the wall; that fixture validates the adapter, not native
wall collision. The real Veldin trials still record distance rather than
asserting an unverified retail target. Full distance parity remains open until
a matched retail trajectory or the remaining contact execution is available.

Final validation:

- Portable C#: 1011 passed, 122 retail-dependent skipped.
- Canonical retail-backed C#: 1133 passed, zero skipped. Source checks pin the
  projection, separate transport, helper blocks and request-source delay slot.
- Python tooling: 77 passed. Authority-verified overlay report regenerated.
- Godot Debug build: zero warnings/errors. Expanded R&C1 smoke passed, including
  zero post-obstruction travel in its synthetic wall-removal trial.
- Real Veldin trials reported 2.4512 units / 1.7142 standing heights with direction
  held and 2.2193 units / 1.5521 heights after direction release. These are host
  observations, not a recovered rule that stick release changes native carry;
  the trials do not pin identical contact state at the entry sample.
- Formatter verification and diff checks passed.

Logs remain local-only under ignored `captures/contact-carry-*.log`. No new
retail trajectory was captured. Changes remain uncommitted on `dev`.
