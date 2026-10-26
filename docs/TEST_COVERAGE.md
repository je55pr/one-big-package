# Portable coverage and analysis policy (#310)

## Run and inspect the same report as CI

```powershell
./tools/test-coverage.ps1 -Configuration Release
# After an unchanged Release solution build:
./tools/test-coverage.ps1 -Configuration Release -NoBuild
```

Linux/macOS can use `bash tools/test-coverage.sh -Configuration Release` with
PowerShell 7 (`pwsh`) installed. CI uses the same PowerShell implementation.
The ordinary `tools/test.ps1` and authorised `tools/test-retail.ps1` remain
available; coverage does not replace retail validation.

The command checks Godot UID coverage and runs the complete portable xUnit
project with the pinned `coverlet.collector` VSTest collector. It temporarily
removes inherited `OBP_*` environment variables, including retail/oracle paths
and evidence-update switches, and restores them on return. It starts no Godot
window and needs no retail data. Collection uses two xUnit workers to bound
instrumentation overhead and avoid oversubscribing CI runners.

Read `.build/coverage/latest.json`. A successful run has `Status: measured` and
relative paths to its `summary.json` and `index.html`. Open that HTML file for
module and per-file line counts. Each run has a fresh GUID directory. CI adds
the Markdown summary to the job and uploads the `portable-coverage` artifact
for 14 days. It contains HTML, Markdown, JSON, Cobertura and OpenCover XML.

Raw collector output stays in `.build/coverage-raw/` and is **not uploaded**.
The report tool validates repository source paths, strips checkout/SourceLink
prefixes and assembly paths, and publishes no source text or test logs. Reports
contain no retail assets, captures, profiles or local source configuration.

## What is measured

| Assembly | Included execution |
|---|---|
| `OBP.Core`, `OBP.IO`, `OBP.Runtime`, `OBP.Fusion`, `OBP.PS2` | Managed production code reached by portable fixtures |
| `OBP.RAC1`, `OBP.RAC2`, `OBP.RAC3` | Managed source-library code reached without discs or external oracles |
| `OBP.Tests` | Linked production files from `game/scripts`, such as CLI parsing and session policy; actual tests are excluded by file path |

All nine assemblies must appear with instrumented source lines in both reports.
The tool compares their exact file/line execution sets before publishing.
Missing or ambiguous reports, an omitted assembly, invalid paths/counts, an
empty measurement or disagreement between formats fail the command. A module
with instrumented but unexecuted lines can correctly show zero. Failure writes
`Status: failed`; a previous success is never presented as the latest run.

`OBP.Game` and engine-dependent `OBP.Godot` execution are excluded. Linked host
source is explicitly labeled; it is not coverage of the full app, Godot scene
activation, input, physics, rendering or audio. Tools, tests and generated files
are excluded. No compiler-generated blanket exclusion hides async state-machine
execution. No whole-repository percentage or documentation quota gates merges.
Portable coverage shows execution, not native equivalence, assertions quality
or every branch. The separate headless Godot and authorised reconciliation gates
remain necessary.

## Measured baseline and critical gaps

The initial measurement uses production `dev@fc2f529` (26 October 2026), with
portable collector/report tooling added. It measured 20,454/30,325 production
lines across nine assemblies before the additional admission tests in this PR.
Module percentages were Core 96.3%, IO 92.5%, Runtime 86.6%, Fusion 93.8%, PS2
45.1%, Rac1 73.2%, Rac2 36.7%, Rac3 53.0%, linked host source 86.3%. This is a
dated baseline, not a target; each CI artifact records its actual revision's
measurement. Source decoders depending on authorised bytes explain substantial
gaps, but an uncovered path still needs evidence before being called correct.
The follow-up run with the admission regressions measures 20,471/30,325 lines;
the world-provider registry is now 35/35 (previously 18/35).

| Critical contract | Baseline / action |
|---|---|
| Preparation, commit, source rejection and persistent player | `WorldEntrySession` 21/21 lines and `WorldEntryContracts` 29/29; existing negative tests retain the session. #308 adds scene-resource rollback, cancellation and late-publication tests; those are outside this older baseline. |
| Missing/mismatched world provider | Registry 18/35. Added portable tests for missing/rejected admission before IO, source/build mismatches, duplicate registration and propagation of import failure. |
| Profile migration, unknown fields and atomic recovery | `ObpProfile` 77/87; existing tests cover future-schema rejection, opaque contributors, failed replacement, backup recovery and isolated native campaigns. Do not infer filesystem/UI integration coverage. |
| Replay and bug report persistence | `CrossWorldReplayTests`, `ReplayWorldTransitionStageTests`, profile/repro and inbox tests exercise portable schemas. Fresh-process replay, scene changes and report reproduction remain in the reconciliation gate; critical host async supervision belongs to #308. |
| Actor lifecycle, damage/provenance and duplicate shots | `ActorRuntimeSet` 91/95 and `FusionDamagePolicy` 11/11. Existing tests exercise retired leases, source-qualified targets, ordering and deduplication. Construction failure, attachment-stamped factories and late import cleanup are explicit #309 work. |
| Malformed CLI values / compatibility | Linked `CommandLineArgs` 160/212; strict malformed/unknown/duplicate/alias tests remain in CI. Unexecuted diagnostic selections are not proof of malformed-input gaps. |
| Actual pointer/input and presentation | Mostly engine-bound and intentionally excluded. The separate source-free headless smoke checks startup/menu/cursor policy without taking the user's mouse. |

The lower decoder/presentation percentages do not justify inventing synthetic
retail witnesses or generic native behavior. Add a focused regression when a
specific contract lacks one; retain authorised tests for byte-sensitive paths.

## Sonar coverage status

**Coverage import is currently unavailable in Sonar's automatic analysis.**
Its displayed missing/zero coverage must not be interpreted as measured zero.
The validated Actions artifact is the coverage authority for this setup.

Sonar [requires CI-based analysis to import coverage](https://docs.sonarsource.com/sonarqube-cloud/analyzing-source-code/test-coverage/overview).
The repository currently has no `SONAR_TOKEN` secret. Enabling a genuine import
requires a project token stored as that GitHub secret, CI-based SonarScanner for
.NET around the build, and `sonar.cs.opencover.reportsPaths` pointing at this
run's validated OpenCover report. Automatic analysis must first be disabled;
[running both modes fails analysis](https://docs.sonarsource.com/sonarqube-cloud/analyzing-source-code/ci-based-analysis/overview-of-integrated-cis).
Never upload a fabricated empty report, hide a missing collector behind zero,
or claim that generating OpenCover alone configures Sonar. External Sonar setup
remains an explicit integration decision, separate from portable collection.

## Calibrated analysis and advisory triage

The solution retains build `-warnaserror`, nullable analysis, formatting,
CodeQL and existing architecture/source-identity/ownership guards. Four SDK
analyzer rules are explicitly warnings (therefore errors in that build):

| Rule | Contract protected |
|---|---|
| CA2012 | Correct `ValueTask` consumption |
| CA2016 | Forward an available cancellation token |
| CA2200 | Preserve the original stack when rethrowing |
| CA2208 | Name an actual exception parameter |

The CA2208 finding in the private Rac2 destination projection now names `entry`
rather than `entry.Kind`. It changes error metadata, not native destination IDs
or import behavior. No broad file/directory suppressions are added.

A bounded review of the current Sonar backlog and recent CodeRabbit reports
on 26 October 2026 separates actionable evidence from advisory labels:

- Check null dereferences, lifecycle/async loss, stale publication, duplicate
  registration, insecure paths and malformed-byte handling first. A warning is
  a lead, not a verified defect. For example, the Frog/Peckbot payload warnings
  follow rejecting C# list patterns which establish non-null exact-size input.
- Exact floating-point comparisons in integer admission, native sentinels and
  replay/retail parity can be intentional. `PlayerHealthSession` rejects
  fractional native damage using `Truncate`; replacing that with an epsilon
  would change its contract. Native arithmetic needs native evidence and a
  focused regression before edits.
- `PlayerSkeletalSampler`'s two visited-byte assignments implement cycle
  detection and completion; the duplicate-assignment advisory is intentional.
  The sound renderer's seeded `Random` controls deterministic audio choices,
  not security credentials. Do not replace it with cryptographic randomness.
- Regex timeouts and `async void` findings need input/lifetime context. The UID
  pattern is anchored and linear; fixture preview code is not the production
  host. Production discarded async tasks still require #308's lifecycle audit.
- Complexity, naming, small duplicated declarations, suggested LINQ rewrites
  and docstring coverage are review advice. CodeRabbit #326 reported no
  actionable comments; #327's two small smoke-wrapper parameter blocks were
  explicitly accepted. Neither establishes a repository-wide rewrite mandate.

This is a sampled triage, not a claim that every open finding is harmless.
Do not bulk-disable analysis, bulk-insert comments or mutate recovered native
translations to satisfy style rules. A necessary narrow suppression must name
its rule and explain the native/fixture reason beside the code, with a focused
regression where behavior is at stake. Public API/ownership documentation is
useful when it explains a contract; no arbitrary XML/docstring quota applies.
