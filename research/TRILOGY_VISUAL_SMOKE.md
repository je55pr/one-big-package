# Trilogy visual smoke pass

**Date:** 2026-09-19
**Revision under test:** `155ea05` (`Restore native UYA sky shell motion`)
**Renderer:** Godot 4.7.2, `gl_compatibility`, 1280x720
**Authority:** local NTSC-U retail inputs; no retail image or binary payload is stored here.

## Scope

This pass checks the representative deterministic shot lists after the native material, atmosphere, and simple world-effect changes. The risk set was deliberately narrow: broken alpha presentation/sorting, black or unlit-looking materials, missing sky/background/fog, obviously wrong emissive treatment, and clear material-state misclassification.

The exercised representatives were R&C1 `LEVEL0`, Going Commando Oozla, and UYA `TABLE1`. The existing `tools/shots/{rac1,rac2,rac3}.json` lists produced 13/13 captures successfully. Captures remain under the gitignored `captures/` tree and are not committed.

## Before / after observations

| Area | Before the smoke pass | Observation after the pass | Action |
| --- | --- | --- | --- |
| Native material state | Recent presentation work maps recovered filtering, wrap and blend state into Godot; accidental transparency or dark surfaces were the primary regression risk. | No representative normal view contains near-black pixels below 0.025 luminance. Kind-tint deltas isolate visible geometry without exposing a fully black material population. | No production material change justified. |
| Alpha | Native blend enable/equation handling now coexists with the established alpha-scissor fallback. Histogram-driven alpha remains intentionally disabled. | All 13 shots rendered successfully with stable geometry counts; no gross disappearance or whole-frame transparency failure was detected. This pass does not claim pixel-perfect translucent ordering. | Keep the current evidence-gated alpha policy. |
| Atmosphere | Trilogy native background/fog settings and GC region fog are now applied through `WorldPresentation` / `PresentationEnvironment`. | Every representative normal view retained non-black background coverage; no missing-sky/clear-colour failure occurred. Oozla is intentionally much darker than the other representatives, but its geometry-isolated pixels are not black. | No atmosphere correction needed. |
| Emission | Bright decoded textures use the existing luminance-derived presentation heuristic; this is not native material semantics. | Bright-pixel coverage remains small in all three normal establishing views (about 0.8-1.1%), with no frame-wide bloom/whiteout symptom. | Leave the heuristic unchanged. |
| UYA sky motion | Recovered UYA shell spin was added immediately before this pass. | `TABLE1` completed all four captures with the native ambient-animation path active and no runtime warning or render failure. The later kind-tint frame is intentionally not used as a static pixel baseline because the sky moves between shots. | Keep recovered motion; do not synthesize motion for R&C1/GC. |

## Representative observations

- **R&C1 LEVEL0:** 796 mesh instances, 899,439 rendered triangles, 1 collider. Establishing mean luminance was 0.2482, with 0% pixels below 0.025. Comparing establishing to the same-framing kind-tint shot changed 17.52% of pixels; those changed normal-render pixels averaged 0.1840 luminance and had 0% near-black or sub-0.08 pixels.
- **Going Commando / Oozla:** 492 mesh instances, 1,881,731 rendered triangles, 2 colliders. The importer reports 2,041 deliberately untextured Moby triangles using fallback tint. Establishing mean luminance was 0.1127 with 0% below 0.025. The kind-tint delta covered 8.43% of the frame; changed normal-render pixels averaged 0.2590 luminance, with 0% near-black and 1.789% below 0.08. The much darker full-frame average is therefore dominated by environment/background coverage rather than a black-material regression.
- **UYA TABLE1:** 925 mesh instances, 1,111,775 rendered triangles, 1 collider. Establishing mean luminance was 0.4977 with 0% below 0.025. All four deterministic captures completed while the recovered sky-shell animation advanced normally.

## Verification

The deterministic visual runs completed successfully for all three shot lists: 4/4 R&C1, 5/5 GC, and 4/4 UYA. A focused Release test run covering `WorldMaterialFactoryTests`, `MaterialModelTests`, `WorldPresentationTests`, and `AmbientAnimatorTests` passed 40/40. The repository Release suite then passed 460 tests with 97 retail-gated tests skipped, and `dotnet format OneBigPackage.sln --verify-no-changes --no-restore` exited cleanly.

No clear rendering regression crossed the evidence threshold for a production-code change in this pass. Subtle translucent ordering and exact PS2 GS fog remain fidelity questions, not regressions established by these representative shots.
