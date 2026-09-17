# UYA Ratchet movement witnesses

Authority: NTSC-U retail **Up Your Arsenal**, `SCUS-97353` v1.00 (`CRC 45FE0CC4`). These are bounded movement witnesses, not a completed controller recovery. Raw PINE captures and retail payloads stay outside Git; the payload-free distillation is `research/generated/rac3-ntscu-original.uya-movement-witnesses.json`.

## Live fields

The controlled setup samples player-global `0x001A4BE0`:

- `+0x25C4`: movement/state id;
- `+0x25C0`: live-player pointer;
- `+0x80/+0x84/+0x88`: position-like XYZ;
- `+0xF0/+0xF4/+0xF8`: velocity-like vector;
- `+0x2260/+0x2264`: conditioned planar input;
- `+0x2838`: shaped magnitude;
- `+0x2840`: shaped angle.

The pointed live-player object is sampled at `+0x10/+0x14/+0x18` for position and `+0xF8` for orientation. Settled position samples match the player-global position; occasional one-update disagreement is expected because the two PINE reads are separate and therefore not atomic.

The two `+0xF8` fields are different: player-global `+0xF8` follows vertical motion, while live-player `+0xF8` is facing yaw. Normal and L2 witnesses below disambiguate the latter directly.

## Ground acceleration and stop

A fixed-savestate full-forward witness enters state `2`. Conditioned input becomes `(0, 1)` with magnitude `1.0`; motion begins on the following sampled update.

The first planar displacement magnitudes are `0.01027679`, `0.02055359`, `0.03083038`, ... with a measured increment of about `0.01027679` source unit/update. The trace reaches `0.10277557` on the tenth moving update and settles at a repeated cap of `0.10282898`.

Release is not a symmetric reverse of acceleration. Once conditioned input clears, the first residual step is `0.09810638`, state returns to `0`, then the stop tail is approximately:

`0.01979065, 0.01487732, 0.00995636, 0.00504303, 0.00012207, 0`.

After the first sharp transition into the stop tail, its measured decrement is about `0.004917` per sampled update. These are witness values, not yet a claim that one scalar recurrence explains every ground state.

`+0xF0/+0xF4` remain zero through this planar witness, so the previously labelled velocity-like vector is not the complete planar motion state. Planar controller archaeology still has to find the owning recurrence rather than treating those two fields as velocity.

## Jump, fall and landing

Both controlled stationary jumps enter state `7`. Grounded player-global `+0xF8` is about `-0.015`; the first translated jump update carries about `+0.1393971`. After jump assistance ends, rising `+0xF8` decreases by `0.00825` per update. Once descending, the recurrence decreases by about `0.0096525` per update.

A six-sample Cross tap reaches `+1.42793274` above the starting floor. A 24-sample hold reaches `+1.88673401`, directly confirming variable jump height. While Cross remains held, the observed rise decrement evolves from about `0.00443646` to `0.00528619` per update before returning to the `0.00825` released-rise branch; this witness does not yet assign an exact formula to that assistance curve.

The tap first translates vertically at capture sample 18, touches the original floor at sample 54 and leaves state `7` for state `0` at sample 65. The long hold first translates at sample 18, contacts the floor at sample 61 and returns to state `0` at sample 73. Position therefore clamps to collision before the jump state itself finishes. On one long-hold contact sample `+0xF8` still contains the pre-contact downward value, then returns to the grounded bias on the next update.

## Turning and strafe basis

Ordinary right-stick locomotion rotates live-player `+0xF8` toward the planar travel heading. In the settled normal-right witness, the median wrapped yaw-minus-travel error is about `-0.000094 rad` at a median planar step of `0.1028268`.

Holding L2 with the same conditioned right input keeps essentially the same planar speed (`0.1028285` median) but settles facing roughly one quarter-turn away from travel: median wrapped yaw-minus-travel is `1.5707642 rad`. This is a concrete lock/strafe-basis witness, not merely an animation difference.

The reverse-input witness begins around yaw `+1.570761`. Eight consecutive large turn updates are approximately `-0.194895 rad` each before the steps taper; by capture sample 38 yaw and travel heading differ by only `0.000121 rad`. A camera-relative right-input witness also converges facing to travel, but the savestate camera basis means its world-space turn is not exactly a geometric 90 degrees.

These traces establish target alignment and easing behaviour, but not the exact yaw recurrence. Non-atomic live-object sampling can also combine two object updates into one observed yaw jump, so isolated maxima must not be promoted as constant turn rates.

## The static 0.25 value is not the locomotion dead zone

Executable context resolves the earlier `0.25` ambiguity. The pad-conditioning routine around `0x003A3158` centers each raw axis at `0x7f`, zeros absolute displacement below `0x30` raw counts, then scales `(abs(raw - 127) - 48) / 76` and clamps the result to `1.0`. Full digital-stick witnesses correspondingly produce conditioned `1.0`.

Later, `0x003A37C4` computes stick magnitude and `0x003A37D4` computes angle, storing them in a 30-sample history. The code first requires current magnitude greater than `0.9`; only on that transition does `0x003A3848` load the `0.25` constant and inspect earlier magnitude-history samples. A prior magnitude below `0.25` raises an input-event bit.

Therefore the static `0.25` is real, but it belongs to a rapid-stick/history detector. It is **rejected as the ordinary movement activation/dead-zone threshold**. The actual axis conditioner has the explicit raw-count dead zone above. Partial-stick locomotion response after conditioning still needs a controlled analogue sweep before a complete movement controller can be claimed.

## 60 Hz interpretation

The disposable authority profile is configured with `FramerateNTSC = 59.94`. Seven scheduled wall-clock captures measured mean sample rates from `59.8981` to `59.9368 Hz`. The task's “60 Hz” description should therefore be read as NTSC/game-math shorthand, not evidence for a literal `60.000 Hz` wall clock.

An experimental player-global `+0x1E8` counter increments once per update in the exact-update normal/jump/turn captures, but resets or stalls during the L2 witness. It was useful as local capture pacing only and is not promoted as a universal retail frame counter or retained in the committed capture contract.

## Evidence boundary

The committed `tools/uya-movement-witness.py` contains no retail payload. It drives only the disposable keyboard bindings and records the required live fields at the configured NTSC wall-clock cadence; raw output belongs under ignored `captures/`.

Still unresolved are the owning planar speed/acceleration fields and functions, exact normal-turn easing recurrence, partial-stick movement curve beyond the proven conditioner, and the complete L2/lock-strafe state machine/camera contract. Those are archaeology tasks, not gaps to fill with DebugPlayer constants.
