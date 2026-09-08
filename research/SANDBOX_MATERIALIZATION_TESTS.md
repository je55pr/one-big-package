# Sandbox materialization probe log

Purpose: characterize ChatGPT sandbox failures around Library materialization while keeping OBP's multi-GB game inputs in Library and the sandbox as a tiny working set.

Detailed generation parameters and per-probe hashes for recent boundary tests live in `research/SANDBOX_PROBE_*.md`. This file is the canonical summary and next-step guide.

## Baseline / established behaviour

Fresh healthy chats have repeatedly shown:
- tiny container command/write: healthy;
- private Python tiny write/read: healthy;
- `/mnt/data` near baseline with ~30 GiB free.

Files materialization does not expose arbitrary binary byte-range selectors suitable for slicing ISO chunks. Therefore threshold testing uses whole disposable synthetic Library files. The real ~480 MiB ISO chunks remain in Library and are not used for routine boundary testing.

Disk exhaustion is strongly disfavoured as the cause: failures occur with ~30 GiB free and materially smaller files succeed in the same environment.

A Files materialization response reporting success does **not** guarantee that container/Python execution will remain alive afterward, nor that the path will remain a stable complete file in every execution backend.

## Historical results

| Size bytes | MiB | Result |
|---:|---:|---|
| 187,564,032 | 178.875 | PASS — container + Python survived; full file |
| 305,135,616 | 291.0 | PASS — container + Python survived; full file |
| 352,845,824 | 336.5 | PASS — container + Python survived; full file |
| 369,491,968 | 352.375 | PASS — container + Python survived; SHA-256 verified |
| 373,653,504 | 356.34375 | PASS — container + Python survived; SHA-256 verified |
| 374,173,696 | 356.83984375 | SPLIT — container died immediately; Python survived; full SHA-256 verified |
| 374,693,888 | 357.3359375 | SPLIT — container died immediately; Python survived; full SHA-256 verified |
| 375,734,272 | 358.328125 | FAIL — container + Python died |
| 377,815,040 | 360.3125 | FAIL — container + Python died |
| 386,138,112 | 368.25 | FAIL — container + Python died |
| 419,430,400 | 400 | FAIL — container + Python died |
| 503,316,480 | 480 | MIXED — earlier FAIL reproduced with container + Python; 2026-09-06 real ISO chunk **container-only PASS** and **private-Python-only PASS**, both through full SHA-256 |

The 400 MiB synthetic failure demonstrates the trigger is not PS2/ISO-specific. The later paired 480 MiB isolation passes demonstrate that large size alone is also not a deterministic immediate-container or private-Python kill condition across all fresh runtimes/test shapes.

## 2026-09-06 480 MiB real-chunk container-only isolation probe

Goal: isolate **container behaviour only** with one existing Library ISO chunk. No Python was invoked at any point in this probe.

Test file:
- Library path: `/Ratchet & Clank/One Big Package/Inputs/Going Commando/Ratchet & Clank - Going Commando (USA) (v1.01).iso.001`
- exact size: **503,316,480 bytes = 480 MiB**

Sequence and result:
1. Tiny pre-materialization container health check: **PASS** (`container-health-ok`).
2. Whole-file Library materialization: **PASS**; Files reported **503,316,480 bytes** at `/mnt/data/obp_480_probe/Ratchet & Clank - Going Commando (USA) (v1.01).iso.001`.
3. Trivial post-materialization container command: **PASS** (`echo post-materialize-alive`).
4. `stat`: **PASS**; exact size **503,316,480 bytes**, mode `644`.
5. 1-byte read via `dd ... bs=1 count=1 | od -An -tx1`: **PASS**; first byte `a5`.
6. Full sequential SHA-256 via `sha256sum`: **PASS**.
   - SHA-256: `f9fa389367912b30603f5e11b63dd862fc4872390edaff20046518b4c73b724b`

**Failure point: none observed.** The container remained alive through materialization, metadata access, a 1-byte read, and a complete 480 MiB sequential read/hash.

Interpretation: the previously observed 480 MiB failures are **not deterministic from materialized size alone**. This run specifically rules out the claim that merely materializing and then reading a 480 MiB real chunk must kill the container. Plausible remaining variables include runtime/backend variability, prior chat/container state, interaction with private Python execution, or differences between synthetic and real-file materialization paths. This probe does **not** by itself prove that Python caused the earlier failures; it only isolates a successful no-Python case.

## 2026-09-06 480 MiB real-chunk private-Python-only isolation probe

Goal: isolate **private Python behaviour only** with one existing Library ISO chunk. The container/shell was not invoked at any point in this probe.

Test file:
- Library path: `/Ratchet & Clank/One Big Package/Inputs/Going Commando/Ratchet & Clank - Going Commando (USA) (v1.01).iso.001`
- exact size: **503,316,480 bytes = 480 MiB**

Sequence and result:
1. Tiny pre-materialization private Python health check: **PASS** (`print("PYTHON_HEALTH_OK")`; trivial expression returned `2`).
2. Whole-file Library materialization: **PASS**; Files reported **503,316,480 bytes** at `/mnt/data/files/Ratchet & Clank - Going Commando (USA) (v1.01).iso.001`.
3. Private Python `print()` immediately after materialization: **PASS** (`POST_MATERIALIZE_PRINT_OK`).
4. Private Python `os.stat()`: **PASS**; exact size **503,316,480 bytes**.
5. Private Python 1-byte read: **PASS**; first byte `0xa5`.
6. Full streaming SHA-256 in private Python: **PASS**; all **503,316,480 bytes** read.
   - SHA-256: `f9fa389367912b30603f5e11b63dd862fc4872390edaff20046518b4c73b724b`

**Failure point: none observed.** Private Python remained alive through materialization, metadata access, a 1-byte read, and a complete 480 MiB sequential read/hash.

Interpretation: 480 MiB is **not an unconditional private-Python materialization limit**. Paired with the independent container-only pass on the same real ISO chunk and matching SHA-256, this shows that each execution backend can individually survive this 480 MiB workload in a fresh isolated run. The earlier combined container + Python failures therefore require some additional variable or interaction; these isolation probes do **not** prove which variable is causal.

## Service/backend split findings

### Files can survive after execution dies

After fatal materializations, Files and GitLab connector operations have continued to work while container and private Python execution returned `caas.internal.errors.ClientError`.

A smaller Files materialization into an already-dead execution environment did not reliably revive container/Python.

### Same-chat recovery is unreliable

One earlier 480 MiB incident recovered execution after a later user turn, but later fatal probes did not recover after user messages or further connector activity. Therefore a new user message is **not** a reliable runtime reset.

Fresh chats are currently the reliable way to obtain a genuinely healthy execution runtime for the next container-survival probe.

### Container and private Python have distinct failure boundaries

The 374,173,696-byte and 374,693,888-byte probes both killed the immediate container command while private Python remained healthy and could see the complete materialized file with the exact expected SHA-256.

Therefore "container survives materialization" and "private Python survives materialization" must be treated as separate properties.

The paired 2026-09-06 480 MiB isolation passes further show that the earlier synthetic-file bisection cannot currently be treated as a universal size threshold across all runtime/test configurations: the same 503,316,480-byte real chunk completed full readback in a container-only run and independently in a private-Python-only run.

## Materialized-path stability anomaly

At 374,173,696 bytes / 356.83984375 MiB:

1. Files reported successful whole-file materialization.
2. Container immediately failed with `ClientError`.
3. Private Python remained healthy, saw all 374,173,696 bytes, and verified the expected SHA-256.
4. Python deleted that materialized file and immediately confirmed the path absent; `/mnt/data` returned near baseline.
5. The container backend remained dead.
6. While Python later prepared the next midpoint, the supposedly deleted path **reappeared** at 374,145,024 bytes — **28,672 bytes short** of the requested size.
7. Reappeared partial SHA-256: `71bdacb81971140fc2dce3d8f9e500fd72a2684922ca649801550bb880027d4e`.
8. Python deleted the reappeared partial path again and polled for 10 seconds; it stayed absent and disk usage returned near baseline.

This is consistent with the earlier 480 MiB recovery observation where a path that Files had reported as successfully materialized later existed only partially. A plausible interpretation is that some Files/container handoff, staging, remount, or late write can reassert a partial path after execution has already observed a complete copy. The exact mechanism remains unknown.

## Historical synthetic-probe container-survival bound

The earlier synthetic bisection established, within those specific test runs:

Confirmed container PASS:
- **373,653,504 bytes = 356.34375 MiB**

Confirmed immediate container FAIL with Python still alive:
- **374,173,696 bytes = 356.83984375 MiB**

Historical interval:

`373,653,504 < threshold <= 374,173,696 bytes`

- **520,192 bytes**
- **0.49609375 MiB**

After the successful 503,316,480-byte isolated container and private-Python probes, this must **not** be interpreted as a universal sandbox materialization size limit. It remains useful evidence about one repeatable synthetic-probe/runtime regime only.

## Prepared next probe

The next synthetic midpoint has already been generated, uploaded to Library, locally cleaned up, and checkpointed to GitLab:

- exact size: **373,913,600 bytes**
- MiB: **356.591796875 MiB**
- Library path: `/Ratchet & Clank/One Big Package/Sandbox Tests/probe-356.591796875MiB.bin`
- SHA-256: `9da534616b8158b505ed593cb1e1e5d317dde77278d99c4837d125c414b3631a`
- detailed metadata: `research/SANDBOX_PROBE_356_591796875.md`

Its risky readback was intentionally **not** attempted in the chat that prepared it because the preceding probe had already killed that chat's container backend. Testing it in that state would not yield a valid container-survival result.

Given the paired 480 MiB isolation passes, further testing should preserve isolation variables explicitly rather than treating the synthetic midpoint as a simple universal size bisection.

## Working rule

Treat Library as the multi-GB vault and sandbox as a disposable tiny working set. Never reconstruct complete multi-GB ISOs in the sandbox while this failure mode remains unresolved.

## 2026-09-06 fresh-runtime 480 MiB container regression during development resume

Goal: resume OBP Stage 0 development in a fresh chat while following the container-only execution rule for large materialised inputs, not run another size bisection.

Test file:
- Library path: `/Ratchet & Clank/One Big Package/Inputs/Going Commando/Ratchet & Clank - Going Commando (USA) (v1.01).iso.001`
- exact size: **503,316,480 bytes = 480 MiB**

Sequence and result:
1. Fresh runtime tiny container health check before any materialisation: **PASS** (`OBP_RUNTIME_OK`); `/mnt/data` had ~30 GiB free.
2. Whole-file Library materialisation: **PASS**; Files reported the exact **503,316,480-byte** file at `/mnt/data/obp_real/Ratchet & Clank - Going Commando (USA) (v1.01).iso.001`.
3. First container execution after materialisation, intended to print a health marker, `stat` the file, and perform bounded ISO metadata reads: **FAIL** immediately with `caas.internal.errors.ClientError`; no command output was produced.
4. Private Python was intentionally **not invoked**, preserving the one-execution-backend-per-chat isolation rule.
5. No further bisection was attempted; development continued through GitLab with CI used for code execution/tests.

Interpretation: this directly reproduces the intermittent container-death behaviour on the same real 480 MiB chunk that independently passed a full container-only SHA-256 in another fresh chat. It further supports the conclusion that neither filesize nor the real ISO bytes alone determine survival, and that a successful Files materialisation response does not guarantee the next container execution will live. The failure is therefore recorded as runtime/backend-state variability rather than evidence for a universal 480 MiB limit.
