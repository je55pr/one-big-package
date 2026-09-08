# OBP research index

This directory records evidence about the retail games, source provenance and external/public cross-checks. It is not a second product roadmap.

## Evidence rule

1. **Primary retail bytes/executable behaviour** are authority for the selected build.
2. Other official regions/revisions are comparison evidence and possible sources of intentional fixes/content.
3. Public tools such as Wrench/noclip are useful cross-checks, not authority over contradictory retail evidence.
4. OBP-created behaviour belongs to game design, not archaeology.

When a document contains an unresolved hypothesis, it should say so explicitly.

## Start here

| Area | Main document | Status / purpose |
|---|---|---|
| Source/build identity | [`INPUT_PROVENANCE.md`](INPUT_PROVENANCE.md), [`BUILD_PROBING.md`](BUILD_PROBING.md) | Current authority/provenance rules |
| Retail trilogy disc structure | [`RETAIL_TRILOGY_DISC_LAYOUT.md`](RETAIL_TRILOGY_DISC_LAYOUT.md) | Bounded retail disc archaeology |
| Local retail-authority access | [`../docs/LOCAL_RUNNER.md`](../docs/LOCAL_RUNNER.md) | Self-hosted runner, verified local authorities and agent probe contract |
| GC level/file catalogue | [`GC_LEVEL_CATALOGUE.md`](GC_LEVEL_CATALOGUE.md), [`GC_PLANET_NAMES.md`](GC_PLANET_NAMES.md) | Retail-backed catalogue/name work |
| GC level loading / RC2.HDR | [`GC_LEVEL_LOADING.md`](GC_LEVEL_LOADING.md) | Retail evidence with active RC2.HDR reconciliation note |
| RC2.HDR reconciliation | [`GC_RC2_HDR_RECONCILIATION.md`](GC_RC2_HDR_RECONCILIATION.md) | **Verification target, not authority yet** |
| GC outer WAD | [`GC_LEVEL_WAD.md`](GC_LEVEL_WAD.md) | Retail-backed container/range layout |
| GC decompressed core | [`GC_LEVEL_CORE.md`](GC_LEVEL_CORE.md) | Retail-backed core header/section work |
| Coordinates/chunks | [`GC_COORDINATES_AND_CHUNKS.md`](GC_COORDINATES_AND_CHUNKS.md) | Coordinate rule plus unresolved chunk-plane equality detail |
| Collision | [`GC_COLLISION.md`](GC_COLLISION.md) | Retail-backed collision decoding |
| Tfrags | [`GC_TFRAG.md`](GC_TFRAG.md) | Retail-backed world geometry/VIF packet work |
| Textures | [`GC_TEXTURES.md`](GC_TEXTURES.md) | Retail-backed GC texture/CLUT decoding |
| TIEs | [`GC_TIES.md`](GC_TIES.md) | Retail-backed TIE classes/instances |
| Shrubs / Moby instances | [`GC_SHRUBS_MOBIES.md`](GC_SHRUBS_MOBIES.md) | Retail-backed instance/class work |
| Moby geometry/skinning | [`GC_MOBY.md`](GC_MOBY.md) | Retail-backed, still partial for variants/animation |
| GC gameplay Mobies / PVars | [`GC_PVARS.md`](GC_PVARS.md), [`GC_MOBY_CATALOGUE.md`](GC_MOBY_CATALOGUE.md) | Retail PVar structure, loaded `lvl.vtbl` behaviour and semantic class catalogue |
| GC crates / resource pickups | [`GC_CRATES.md`](GC_CRATES.md) | Loaded crate-family state machine and indexed resource-emission archaeology |
| Sky | [`GC_SKY.md`](GC_SKY.md) | Retail-backed shell geometry/settings; effects remain partial |
| Level settings | [`GC_LEVEL_SETTINGS.md`](GC_LEVEL_SETTINGS.md) | Retail-backed fixed settings fields |
| Public cross-source notes | [`PUBLIC_FORMAT_ARCHAEOLOGY.md`](PUBLIC_FORMAT_ARCHAEOLOGY.md) | Surviving Wrench/noclip findings and contradictions |
| Geometry architecture caveats | [`PS2_GEOMETRY_PIPELINE.md`](PS2_GEOMETRY_PIPELINE.md) | VIF/packet boundary and future caveats |
| Wrench format comparison | [`WRENCH_FORMAT_MATRIX.md`](WRENCH_FORMAT_MATRIX.md) | Public-tool comparison matrix |
| External-source policy | [`EXTERNAL_SOURCE_POLICY.md`](EXTERNAL_SOURCE_POLICY.md) | How non-retail sources may be used |

## Specialist-branch research

R&C1 and UYA archaeology currently advances on dedicated branches and can therefore contain research documents that do not yet exist on `main`. Treat a branch-local document as evidence for that branch/ref until it is reviewed/merged; do not silently copy changing specialist conclusions into unrelated branches.

The living cross-project snapshot in [`../docs/CURRENT_STATE.md`](../docs/CURRENT_STATE.md) separates merged capabilities from those active branch findings.

## Generated evidence

[`generated/`](generated/) contains deterministic reports/catalogues produced by tooling. Legacy filenames containing `stage0` describe the development stage in which those reports were introduced; they are not a claim that the whole repository is still at Stage 0.

[`manifests/`](manifests/) contains canonical authority/source manifests checked against importer constants.

## Sandbox/reliability notes

[`SANDBOX_MATERIALIZATION_TESTS.md`](SANDBOX_MATERIALIZATION_TESTS.md) is the canonical investigation log for ChatGPT sandbox/materialisation behaviour. Individual `SANDBOX_PROBE_*` / recovery files are narrow experimental records and should be read through that canonical log rather than treated as project architecture.

The concise fallback operational summary for agents lives in [`../docs/AGENT_SANDBOX_NOTES.md`](../docs/AGENT_SANDBOX_NOTES.md). When the local runner is available, [`../docs/LOCAL_RUNNER.md`](../docs/LOCAL_RUNNER.md) is the preferred retail-authority workflow.

## Keeping this index useful

When a new research document becomes the best entry point for a major subsystem, add it here. When a hypothesis is superseded by direct retail evidence, either update the old document prominently or remove it rather than leaving two apparently authoritative descriptions.
