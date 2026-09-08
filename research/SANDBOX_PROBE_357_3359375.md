# Sandbox probe — 357.3359375 MiB

Status: SPLIT RESULT — container immediate probe failed, private Python remained healthy.

- exact size: 374,693,888 bytes (357.3359375 MiB)
- Library path: `/Ratchet & Clank/One Big Package/Sandbox Tests/probe-357.3359375MiB.bin`
- generator: AES-256-CTR over an all-zero stream
- key: `2180e3fc4d4dad4176b3adcf1edb57ed7923756880927c0598b1847e3717af4b`
- IV: `ae5d9a10c95a5b456739854909f4995d`
- SHA-256: `de72f8a75a361d985e0d5c8ce7f696c2ae803b98c9abae97c03c2c0b801ed122`
- upload to Library: succeeded
- local generating copy deleted before risky readback; `/mnt/data` returned to ~40 KiB and filesystem reported ~30 GiB free
- fresh container + private Python probes: healthy immediately before readback

Materialization result:
- Files reported successful materialization of the exact 374,693,888-byte artifact.
- The immediate next container command failed with `caas.internal.errors.ClientError` before `stat` could complete.
- Unlike the larger fatal probes, the immediate private Python probe **succeeded**.
- Python saw the complete 374,693,888-byte file.
- Python SHA-256 after materialization exactly matched the generating hash.

This is the first observed split-backend result at the materialization boundary: container execution failed immediately, but private Python stayed operational and the full byte stream was present and correct from Python's view.

Current interpretation:
- strict "container survives immediately" safe bound remains 373,653,504 bytes (356.34375 MiB).
- strict container-failure bound is now 374,693,888 bytes (357.3359375 MiB).
- interval for container survival is therefore 1,040,384 bytes (0.9921875 MiB).
- Python/runtime invalidation threshold is higher or otherwise distinct; this probe does **not** count as a full-runtime fatality.
- next midpoint for container-survival bisection: 374,173,696 bytes (356.83984375 MiB).
