# Agent sandbox notes for large OBP inputs

This document is fallback operational guidance for constrained ChatGPT development environments. It is **not** a production OBP runtime limit and should not be used to infer native local-machine capabilities.

The canonical historical experiment log is [`../research/SANDBOX_MATERIALIZATION_TESTS.md`](../research/SANDBOX_MATERIALIZATION_TESTS.md). For current retail-authority development, prefer the self-hosted workflow in [`LOCAL_RUNNER.md`](LOCAL_RUNNER.md) whenever the runner is online.

## Preferred path: do not move the retail image into the sandbox

OBP now has a project-scoped `obp-local` GitLab runner with direct access to the verified user-owned retail images on Jess's laptop. A development chat can commit a bounded probe, run it on the exact branch through the local runner, and receive only the small sanitized result.

That path is preferable to materialising large ISO chunks into a disposable ChatGPT runtime because it:

- operates directly on the full verified authority image;
- avoids large upload/materialisation transfers;
- survives agent sandbox lifetime changes;
- keeps copyrighted payloads local;
- produces auditable branch/job/test evidence.

Use the sandbox path below when the runner is offline or when the sandbox itself is the thing being tested.

## Library/local storage is the vault; the sandbox is a working set

The original PS2 inputs are multi-gigabyte. Keep pristine images/splits in user-controlled Library/local storage and treat an agent sandbox as disposable working space.

Observed sandbox behaviour has varied across runtimes. Earlier runs saw large materialisation followed by container/Python `ClientError` failures despite free disk. Later isolated tests successfully materialised and fully SHA-256 hashed the same 503,316,480-byte (480 MiB) real ISO chunk independently through container-only and private-Python-only paths.

Therefore:

- **480 MiB is not a demonstrated hard failure limit.**
- A successful 480 MiB test is also **not** proof that repeated or multi-part materialisation is safe in every runtime.
- Free filesystem capacity alone does not predict sandbox health.
- Materialisation reliability is an environment property, not part of the OBP file-format architecture.

A roughly 25-minute sandbox lifetime/failure pattern has also been repeatedly observed during development. Persist useful code/research checkpoints frequently rather than assuming an interactive runtime will remain available indefinitely.

## Fallback working rules

- Keep canonical multi-GB source images outside Git and outside the transient sandbox whenever possible.
- Prefer bounded reads, tiny derived probes and deterministic fixtures for questions that do not require whole source parts.
- Do not reconstruct a complete multi-gigabyte ISO in a ChatGPT sandbox merely to inspect a few headers or offsets.
- When a large real-input materialisation is genuinely necessary, isolate the experiment, avoid opening several huge working sets at once, and persist useful results before continuing.
- For native OBP itself, use seekable file/random-access readers; the production architecture has no browser `File`/`Blob` or OPFS requirement.
- The old browser/split reader implementation remains available under `reference-ts/` as reference code and can still be useful for transport experiments.
- When local reassembly is needed on an unconstrained machine, stream incrementally and verify size/hash before trusting the result.

If sandbox behaviour changes, update the canonical research log first and keep this file as a short fallback summary rather than letting sandbox quirks shape the production architecture.
