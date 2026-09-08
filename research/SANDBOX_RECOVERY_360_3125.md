# Sandbox recovery addendum — 360.3125 MiB probe

This addendum supplements `research/SANDBOX_MATERIALIZATION_TESTS.md` because the connector blocked a whole-file rewrite of the canonical log after the recovery observation.

Probe: 377,815,040 bytes (360.3125 MiB) synthetic Library materialization.

Observed sequence:

1. Files reported successful materialization of the full 377,815,040-byte artifact.
2. The immediate next container command failed with `caas.internal.errors.ClientError`.
3. An independent private Python execution immediately failed with the same `ClientError`.
4. The FAIL result was checkpointed to GitLab using surviving connector access.
5. Later in the **same assistant turn**, without any new user message and without starting a new chat, a tiny container command succeeded.
6. The recovered artifact existed at the full expected 377,815,040 bytes.
7. Its SHA-256 matched the generating hash already recorded in the canonical log.
8. Private Python also recovered and saw the full expected artifact.

Conclusions:

- A new user message is **not required** for same-chat execution recovery.
- Some above-threshold Library materializations can cause a temporary execution-runtime invalidation while the full file transfer still completes durably.
- This differs from the earlier 480 MiB ISO incident, where the later recovered artifact was truncated.
- The useful boundary remains an **immediate-execution** boundary, not necessarily a permanent sandbox-death boundary.
- Current immediate-safe bound: 369,491,968 bytes (352.375 MiB).
- Current immediate-fail bound: 377,815,040 bytes (360.3125 MiB).
- Next midpoint: 373,653,504 bytes (356.34375 MiB).
