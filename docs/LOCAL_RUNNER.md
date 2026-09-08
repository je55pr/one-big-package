# Shared local runner / retail-authority workflow

OBP has a project-scoped self-hosted GitLab Runner on Jess's Windows laptop. Its purpose is to let development agents execute explicit, bounded archaeology jobs against the user's real retail images without uploading those images to GitLab, a ChatGPT sandbox, or another service.

This runner is now the preferred retail-authority bridge when it is online.

## Runner identity

- GitLab tag: `obp-local`
- Host reported by jobs: `JESS-LAPTOP`
- Executor: Windows shell / PowerShell 7 (`pwsh`)
- Project-scoped; does not accept untagged jobs
- Intentionally one-job-at-a-time for this workflow
- Availability depends on the runner process/service being online; agents must not assume the laptop is permanently reachable

The agreed local authority workspace is:

```text
C:\ChatGPT\
  ISOs\
  OBP-Reports\
  Scratch\
```

These paths are local infrastructure conventions, not portable parser API inputs. Do not leak unrelated personal paths, credentials, runner tokens, browser data or other machine state into research output.

## Why this exists

The old remote-agent workflow had to move hundreds of megabytes of split ISO data into disposable sandboxes merely to inspect small ranges. The local runner changes the default:

```text
ChatGPT/dev agent
    -> GitLab branch + explicit pipeline
    -> obp-local runner
    -> exact checked-out commit on Jess-Laptop
    -> bounded reads of verified local retail authority
    -> sanitized job trace / small derived evidence
    -> durable code/research committed to GitLab
```

Retail bytes never become repository artifacts.

## Shared CI entry points

`.gitlab-ci.yml` has a pipeline input named `run_mode`. The default is `none`, so ordinary pushes and merges create **no CI jobs** and consume no GitLab-hosted compute by default.

Merged shared modes are:

- `hosted-tests` — TypeScript reference checks plus .NET build/tests on GitLab-hosted runners. This consumes hosted compute allowance and should be selected deliberately.
- `local-smoke` — basic runner/toolchain health plus a write/read round trip under `C:\ChatGPT\Scratch`.
- `local-inventory` — recursive local ISO inventory, bounded 4 KiB start/middle/end samples, and ISO-9660 PVD identification. No full-image hashing.
- `local-stage0` — checkout + TypeScript reference build + trilogy disc archaeology against the three local retail images, with reports written to `C:\ChatGPT\OBP-Reports`.
- `local-full-hash` — streamed SHA-256 of all three local retail images against the pinned authority hashes.

Every `local-*` shared mode is tagged `obp-local`, so it runs on Jess's laptop rather than a hosted runner.

Specialist branches may add additional run modes/jobs for focused archaeology. Those jobs are branch-local infrastructure until merged.

## Verified retail authorities

The full local images were SHA-256 verified through the self-hosted runner on 2026-09-07.

| Game | Size | SHA-256 |
|---|---:|---|
| Ratchet & Clank | 4,214,095,872 | `ab849fe7cc9cc81c487d61b0d3ea15b5849943481b6a6ebf4d9aa9cf7bc40d9d` |
| Going Commando NTSC-U v1.01 | 3,828,350,976 | `9db2e33e276133cc283647fa3279b37911955e123d6199d10065547eaa9b1ce5` |
| Up Your Arsenal | 4,379,377,664 | `d2bb15c7c5b2205db868713fc0362c2b10e87751ca5bcc4e96c1e244a8c42444` |

All three matched the pre-existing OBP manifests exactly. A filename or sparse sample alone is not a substitute for this identity evidence when an experiment depends on the exact build.

## Proven capabilities

The workflow has demonstrated:

1. ChatGPT-triggered GitLab pipeline dispatch to `JESS-LAPTOP`.
2. PowerShell 7, Node.js, Git and .NET execution on the runner host.
3. Direct random reads from multi-gigabyte files, including offsets beyond 32-bit assumptions.
4. Full sustained hashing of the three retail disc images in one job.
5. Git checkout/build/test/probe work against an exact branch commit.
6. Existing OBP disc archaeology running directly against the full local authorities.
7. Specialist-branch focused probes that turn retail observations into small deterministic research artifacts without copying game payloads into Git.

## Contract for branch-specific authority probes

A specialist probe should be easy to audit. Prefer this pattern:

1. Commit the parser/probe/job definition to the specialist branch.
2. Keep the job behind an explicit `run_mode` or similarly narrow CI rule and tag it `obp-local`.
3. Run the pipeline on the exact branch/ref containing the probe.
4. Have the job log the relevant commit/build identity and fail loudly on unexpected source identity or structure.
5. Read only the ranges required by the question unless the task is intentionally a full-payload check.
6. Emit concise counts, offsets, hashes, classifications or small sanitized derived reports.
7. Commit durable conclusions/tests/research back to the branch; leave temporary payloads under `C:\ChatGPT`.
8. Remove or retire one-off CI modes when they stop being useful rather than accumulating accidental laptop jobs forever.

Do **not** make ordinary pushes execute retail jobs automatically.

## Agent separation

The runner is shared infrastructure; semantic ownership remains branch-specific.

- `chatgpt/rc1` owns R&C1 archaeology.
- `chatgpt/uya` owns UYA archaeology.
- GC/native runtime work lives on its designated GC branch(es).
- cross-game synthesis/docs work should not edit another specialist's active parser area merely because the same runner can access the files.

When a shared codec is reused across games, prove compatibility rather than weakening assumptions until another title happens to parse.

## Hosted CI policy

OBP intentionally defaults to zero hosted CI. Use `hosted-tests` only when hosted verification is actually valuable. Documentation-only changes normally use `[skip ci]`; code changes can use the local runner or explicitly requested hosted tests according to the branch workflow.

A successful previous pipeline is evidence for the commit it tested, not for later untested commits.

## Fallbacks

When `obp-local` is offline, Library/split inputs, Drive/range transport and agent sandboxes remain useful fallbacks for bounded investigation. They are no longer the preferred way to move retail authority into an agent's reach when the local runner is available.

See [`AGENT_SANDBOX_NOTES.md`](AGENT_SANDBOX_NOTES.md) for the constraints of the fallback sandbox path.
