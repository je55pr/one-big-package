# Running jumps across ordinary slopes

## Reproduced host failure

The synthetic raw-class-11 descending-five-degree ramp in
`OBPGame.Rac1SlopeJumpSmoke` reproduces the reported running-jump failure.
`captures/movement-slope-jump-baseline.log` records a supported full run before
Cross, then no launch. The first observed failed update has zero vertical
request, no floor/support and a 0.0351-unit feet-to-plane separation. The
controller falls, recontacts the ramp and resumes running while Cross remains
held.

The ordinary controller's eight-update anticipation previously emitted zero
vertical motion. `Rac1OrdinaryGroundContactMotion` applied its supported
54/3600 ground request only in the grounded locomotion phase, so anticipation
removed the contact request. A descending ramp then moved away beneath the
horizontal request. The controller additionally aborted an already admitted
jump whenever its host floor bit disappeared during anticipation.

## Recovered contract and implementation boundary

The common native jump helper at `0x214574..0x2145a4` clears the gravity-axis
request, loads the exact `48f` literal at `0x214594`, multiplies it by the
squared update duration and supplies the downward prelaunch request. It does
not use ordinary locomotion's separate `54/3600` coefficient. The pinned
`0x214108..0x214658` source block and the independent literal assertion in
`Rac1AirborneNativeSourceTests` cover this contract.

The contact constructor now supplies that missing native request for the
ordinary zero-output anticipation path. The application marks profile-owned
prelaunch output explicitly, so existing negative special-profile requests are
not applied twice and the action-`0x1c` early zero-preserving exception at
`0x214544` remains intact. Launch motion is unaffected. The host's native
contact-feedback request uses the same distinction as the executed request.

No Godot floor snap, increased support distance, coyote-time window or new
anticipation slope tangent is introduced. Grounded locomotion retains its
existing recovered tangent construction. The source helper itself clears the
prelaunch gravity-axis component before adding the 48-coefficient request;
claiming ordinary ground tangency during that interval would require a
separate host approximation. An already admitted native jump may continue
through temporary loss of support before its launch age; admission from an
unsupported initial state is a separate rule.

## Integration fixture

The explicitly synthetic diagnostic contains five ordinary-material paths:
five- and twenty-degree descending ramps, their ascending counterparts and a
flat-to-five-degree descending seam. Each establishes a full run and holds
Cross through anticipation, then requires an upward launch and useful
clearance from the support plane. The fixture restores player pose and
equipment previews and removes all synthetic collision on completion.

`captures/movement-slope-jump-fixed.log` passes all five paths and the complete
existing movement/equipment/ledge/Swingshot smoke. Maximum measured clearances
were 2.2709 and 3.1116 for descending five/twenty degrees, 1.8845 and 1.3761 for
the ascending counterparts, and 2.2709 across the descending seam. These are
synthetic host observations, not replacement native jump-height constants.
