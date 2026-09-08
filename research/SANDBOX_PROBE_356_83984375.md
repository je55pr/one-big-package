# Sandbox probe — 356.83984375 MiB

Status: SPLIT RESULT — container immediate probe failed, private Python remained healthy.

- exact size: 374,173,696 bytes (356.83984375 MiB)
- Library path: `/Ratchet & Clank/One Big Package/Sandbox Tests/probe-356.83984375MiB.bin`
- generator: AES-256-CTR over an all-zero stream
- deterministic seed: `OBP sandbox probe 374173696 bytes v1`
- key: `7a06410552668e11798e9c2bc63d026123d5e6e6eab965a3ec1379838fbca793`
- IV: `9fc4b7486d6c04c2b2e29f76e22ad801`
- SHA-256: `ee00dea6e65cd3c0572fa9f3fe16b25fed4b4f8369a372db8bb23e4a48a85f75`
- upload to Library: succeeded
- local generating copy deleted before risky readback
- `/mnt/data` after deletion: ~5.3 MiB used, ~30 GiB free
- fresh container + private Python probes: healthy immediately before readback

Materialization result:
- Files reported successful whole-file materialization of the exact 374,173,696-byte artifact at `/mnt/data/probe-356.83984375MiB.bin`.
- The immediate next container command failed with `caas.internal.errors.ClientError` before even `stat` could complete.
- The independent private Python probe **succeeded**.
- Python saw the complete 374,173,696-byte file.
- Python SHA-256 after materialization exactly matched the generating hash: `ee00dea6e65cd3c0572fa9f3fe16b25fed4b4f8369a372db8bb23e4a48a85f75`.

This is the second consecutive split-backend observation near the container boundary: container execution fails immediately while private Python stays operational and sees the complete, correct byte stream.

Post-readback cleanup anomaly:
- Python deleted the materialized file and immediately confirmed the path absent; `/mnt/data` returned to baseline.
- The container backend remained dead after that cleanup.
- While Python later generated the next midpoint probe, the supposedly deleted path reappeared at **374,145,024 bytes**, which is **28,672 bytes short** of the requested 374,173,696 bytes.
- Reappeared partial-file SHA-256: `71bdacb81971140fc2dce3d8f9e500fd72a2684922ca649801550bb880027d4e`.
- Reappeared file mtime/ctime: `2026-09-06T08:07:35.411192+00:00`.
- Python deleted the reappeared partial file a second time, then polled for 10 seconds; it stayed absent and `/mnt/data` returned to ~5.5 MiB used.

This strongly suggests that a Files materialization can continue or reassert a partially-written path after the execution-side consumer has already seen a complete copy and deleted it. It is consistent with the earlier post-crash 480 MiB observation where a path reported as successfully materialized later existed only partially.

Updated strict container-survival bisection bounds:
- pass: 373,653,504 bytes (356.34375 MiB)
- fail: 374,173,696 bytes (356.83984375 MiB)
- interval: 520,192 bytes (0.49609375 MiB)
- next midpoint: 373,913,600 bytes (356.591796875 MiB)

Python/runtime invalidation remains a distinct, higher, or otherwise different threshold from immediate container survival.
