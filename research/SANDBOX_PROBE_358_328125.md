# Sandbox probe — 358.328125 MiB

Status: IMMEDIATE FAIL; runtime still unavailable at first recovery check.

- exact size: 375,734,272 bytes (358.328125 MiB)
- Library path: `/Ratchet & Clank/One Big Package/Sandbox Tests/probe-358.328125MiB.bin`
- generator: AES-256-CTR over an all-zero stream
- key: `76150f7edde3d65762e08124eca3ed07ded4565c108d5b99303ed27613d165da`
- IV: `6a19a0f351850924c7d1c8acc55dd2f5`
- SHA-256: `8186423f2a6ac913707fbb5a2d00b018ee4045ecfc1cacb13425cc4a9595c555`
- upload to Library: succeeded
- local generating copy deleted before risky readback; `/mnt/data` returned to 4 KiB
- fresh container and private Python probes: healthy immediately before readback

Immediate materialization result:
- Files reported successful materialization of the exact 375,734,272-byte artifact.
- The immediate next container command failed with `caas.internal.errors.ClientError` before `stat` could complete.
- An independent immediate private Python execution failed with the same `ClientError`.

Recovery observation:
- after checkpointing the failure to GitLab, the first later tiny container retry still failed with `ClientError`.
- unlike the 360.3125 MiB probe, automatic same-turn recovery had not occurred by this first retry.

Updated immediate-execution boundary:
- safe: 373,653,504 bytes (356.34375 MiB)
- fail: 375,734,272 bytes (358.328125 MiB)
- interval: 2,080,768 bytes (1.984375 MiB)
- next midpoint: 374,693,888 bytes (357.3359375 MiB)
