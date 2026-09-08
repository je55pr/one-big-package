# Sandbox probe — 356.34375 MiB

Status: PASS.

- exact size: 373,653,504 bytes (356.34375 MiB)
- Library path: `/Ratchet & Clank/One Big Package/Sandbox Tests/probe-356.34375MiB.bin`
- generator: AES-256-CTR over an all-zero stream
- key: `3b1d0e5db918a7005d9d74bf57fb87fd58b2ca1fc68646051fd876ac7ab7e47b`
- IV: `aaea4a881facf02d33ffc232c2ab4be8`
- SHA-256: `923e67665c698bfc85d7c6f8a450763559a8d631b59b8d5a3424386430dd2f0c`
- upload to Library: succeeded
- local generating copy deleted before risky readback; `/mnt/data` returned to 4 KiB
- fresh container and private Python probes: healthy before readback

Materialization result:
- Files reported successful materialization of the exact 373,653,504-byte artifact.
- The immediate container probe succeeded and `stat` reported the full expected size.
- The immediate private Python probe succeeded and saw the full expected size.
- SHA-256 after materialization matched the generating hash exactly.
- The materialized local copy was deleted and `/mnt/data` returned to 4 KiB.

Updated immediate-execution boundary:
- safe: 373,653,504 bytes (356.34375 MiB)
- fail: 377,815,040 bytes (360.3125 MiB)
- interval: 4,161,536 bytes (3.96875 MiB)
- next midpoint: 375,734,272 bytes (358.328125 MiB)
