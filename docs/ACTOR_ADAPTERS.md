# Source-native actor adapter registration (#309)

This is OBP host composition, not newly recovered native behavior. The native
controllers, damage records, motion, random/reward rules and animation samplers
remain in `OBP.RAC1`, `OBP.RAC2` and `OBP.RAC3`. `ActorRuntimeSet` remains the one
live actor registry and owns deterministic ordering and contact receipts.

## Callsite census and ownership

The starting point is `82f1253` on `dev`. `OBPGame.Actors` owned world dictionaries,
background imports, decoded-source caches, construction, contact bodies, cleanup
and two native tick-group registrations. `OBPGame.ActorAdapters` contained three
family constructors and two animation caches. Its string-keyed factory map was
rebuilt on every lookup; each constructor separately checked its native recipe.

| Callsites / previous mapping | Current owner and remaining bridge |
|---|---|
| Three `CreatePlaced*` constructors / `ActorFactories` map | `Rac1ToadActorFactory`, `Rac2Msr1ActorFactory`, `Rac3PropActorFactory`; the composition list is `ConfigureActorFactories` and `ActorFactoryRegistrations` |
| Source/build/class/context admission | Fusion `ActorAdapterDescriptor` / `ActorAdapterRegistry`, reusing `ActorPlacement`; rejects before import or scene creation |
| Model selection, rigid placement, native target allocation, contacts | `ActorSceneFactory` / `ActorSceneBinding`; rollback disables/removes contacts and detaches/frees the child scene |
| Views, placements, activation jobs, bodies, rewards, audio, problems | `ActorWorldHost`, reset during world retirement; root polls readiness and renders status |
| Imported native libraries and baked animation data | `ActorSourceCache`, attached-source lifetime, keyed by game/build/path/length/write stamp plus level/family/asset kind |
| Background native imports | Attachment token linked to `HostOperationScope`; native reads finish before shutdown drain completes; abandoned results/failures are observed |
| Native source groups | Supplied by factories, still sorted and stepped once by `ActorRuntimeSet`; existing R&C1/GC source-host tick bridges retained |
| Original-world targets | `RegisterExistingNativeTargets` wraps existing source sessions; source-owned world construction and authored level predicates remain unchanged |
| Weapon rays / blasts / wrench | `OBPGame.ActorContacts`, R&C1 projectile exclusions and native GC movement use the world's body/view registry; source-qualified keys and damage admission unchanged |
| Player damage, rewards and sound | Existing native host bridges; retirement removes player attack registrations, native capture references, owned pickups and actor audio |
| Capture and diagnostics | `CaptureSession` observes the world owner; explicit replay-family admission remains deliberately narrower than general factory registration |

No destination origin grants a player capability. The factories receive narrow
source bindings, not the root node or a service locator. The extracted
`Rac1HostedEnemy` and `Rac2OpeningMsr1HostSession` are presentation/session holders
used by the existing original-world tick bridges as well as relocated actors.

## Admitted recipes

All builds are the corresponding source's accepted `Authority.Primary.BuildId`.
The following adapter/context strings are existing wire identities and are retained.

| Source / native class | Adapter / context | Native entry |
|---|---|---|
| R&C1 / 749 | `rac1/class749/v1` / `rac1/veldin-target-home/v1` | LEVEL0, target search state 5, genuine one-health authored toad, relocated native home |
| GC / 2827 | `rac2/class2827/v1` / `gc/aranos-chase-resume/v1` | LEVEL0, admitted opening MSR I, resumed chase state 12; existing approach/attack cycle |
| UYA / 500 | `rac3/class500/v1` / `uya/table1-class500-deactivate/v1` | TABLE1, active state, recovered deactivate route |

GC enemy audio/reward presentation and UYA break presentation remain explicitly
unimplemented capabilities with their original explanations. Registration does
not broaden native AI, class tables, audio or deterministic capture claims.

## Lifetimes and adding a family

All placement imports complete before activation in source/class/placement-id
order. Worker completion order cannot choose target identities or advance one
actor early. Rendering still observes the existing fixed 60-Hz native groups.

World retirement disposes every actor once and clears groups/receipts even if
one adapter fails to retire. `RetirementFailures` retains those errors for the
Godot host to report after cleanup; `Dispose` does not interrupt later teardown.
Factory construction failures release their native
bindings and scene before ownership transfer. A rejected registry transfer
disposes the rejected actor without retiring an existing target with the same id.
World transitions keep decoded data but never controllers, scenes or bodies.
Source invalidation cancels imports, drops decoded caches and retires relocated
actors and marks both live and pending placements unavailable; reset must
re-admit the current attachment. Batch retirement reports individual callback
failures only after invalidating every selected actor, then the host clears all
placement/preparation bindings. Stamps are checked before and
after import/baking and again before activation. Native importers currently lack
mid-read cancellation: their result is discarded on retirement and shutdown
awaits reader closure. A late failure stays observable without publishing to a
replacement world.

For another recovered family, implement `IActorFactory` beside its source
presentation, give it an exact descriptor, reuse `ActorSceneFactory` and source
controllers, then add it to the composition list. Supply a shared source group
only once, or a distinct source-qualified group if its native clock is separate.
No central placement switch changes. The portable registry example admits a
second family through registration alone; it deliberately supplies no fake AI.
Capture admission requires its own deterministic fixture before advertising
record/replay support.

Portable tests cover recipe mismatches/invalid identities, failed ownership
transfer, cleanup after retirement failure, cache reuse, changed provenance,
version replacement, failed-import retry and cancellation/drain. The headless
trilogy diagnostic exercises actual native motion, damage, animation, rewards,
independent reset, deliberate failure after contact construction, cache reuse
and world/source cleanup. The reconciliation gate adds original-world and
Fusion Lab record/replay checks.
