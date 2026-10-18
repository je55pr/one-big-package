# R&C1 crate and bolt testing

Launch OBP from the desktop shortcut and enter R&C1 Veldin. Existing controls:
**X / west face** wrench, **C / right shoulder** plus wrench for Comet-Strike,
**E / east face** for the equipped Bomb Glove.

1. Break several wooden crates. The crate and its solid contact should disappear,
   fragments should scatter, and retail bolts should spin and home into Ratchet.
   The counter increases when they arrive. Check larger-reward crates too.
2. Throw the wrench at a distant crate. Its bolts should travel back while
   Ratchet remains where he threw it; contact should not credit them instantly.
3. Break the bottom of a stack. Upper crates should fall onto the remaining
   support. Jump onto an intact crate to check its solid contact.
4. Spend Bomb Glove ammo and break an ammo crate. Pieces should scatter, settle
   and refill the item. A piece should wait when that item is full.
5. Break a Nanotech crate at full health. Its cloud should wait. Take damage and
   approach: it should restore **one** point despite eight visible particles.
6. On Novalis or another level with metal crates, try wrench then Bomb Glove.
   Wrench should leave it intact; the Bomb Glove should emit metal debris/bolts.
7. Approach loose bolts already placed in the level. They should rotate at rest,
   then fly into Ratchet and disappear, adding their value on arrival. Try a
   distant loose bolt with Comet-Strike while standing still. Pokitaru also has
   five-bolt pieces; Batalia's intentionally floating pieces should stay raised.

Listen to crate breaks and bolt attraction. Samples and class associations come
from the retail bank; pitch conversion/mixing remain approximations. Report
sounds that seem too fast, low or loud.

The wallet survives level changes. Normal launches remain ephemeral; existing
`--rac1-campaign-persist` or `--rac1-campaign-save PATH` opt into disk saves.
Old saves remain compatible. Crate UID maps and authored loose-bolt collection
records are not written to that file. Loose bolts remain collected during
player restart in the current level; level reload currently restores them.

## Automated checks

With supported retail ISO environment variables configured:

```powershell
./tools/test.ps1 -Configuration Release
./tools/test-retail.ps1
dotnet format OneBigPackage.sln --verify-no-changes --no-restore
./tools/rac1-crate-smoke.ps1
./tools/rac1-placed-bolt-smoke.ps1
./tools/rac1-wrench-smoke.ps1
```

The crate gate stages actors on a synthetic floor in Veldin and Novalis. It is
an integration check, not a retail replay. `OBP_CRATE_CAPTURE_DIR` optionally
saves intact, burst, settled and homing PNGs. The separate Veldin play gate uses
ordinary player input.

The placed-bolt gate checks real authored nodes on Veldin, Batalia and Pokitaru,
with staged player/contact positions. It covers resting animation/scale,
preserved floating height, proximity and thrown-wrench homing, original-node
removal, sound and exact one/five-bolt credit. `OBP_CRATE_CAPTURE_DIR` also saves
these fixtures as `levelN-placed-rest.png`.

Native particle sprites, dust/sparkles, chrome passes, exact swept-sphere contact
moving-support attachment, cross-level loose-bolt removal records and some
fragment details remain incomplete. See
[`RAC1_CRATES_PICKUPS.md`](../research/RAC1_CRATES_PICKUPS.md).
