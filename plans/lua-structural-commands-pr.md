# Prepared pull request — not published

## Title

feat: Add deferred Lua entity creation and component commands

## Body

Lua systems currently lack a way to create entities and defer structural changes without changing existing immediate pool behavior. Add `ctx.commands:create/add/del` backed by bounded Simulation-owned command, payload and validation arrays. Snapshot values are captured at enqueue; the complete batch is validated against FIFO membership, entity generations and reserved world/pool capacities, then applied before the next scheduled system.

Invalid callback/command/preflight batches are discarded even when Lua catches an enqueue error; immediate writes remain. Unexpected playback failures stop the Simulation regardless of LogAndContinue. Pending tokens are callback-local; later access uses queries. Creation requires a component, deletion of the final logical component is rejected, and internal anchor writes are forbidden.

Verification:
- Fresh Release Runner build: 0 errors, 7 existing warnings.
- New integrated StructuralCommand suite: 57/57 passed, including Static/Dynamic × Deterministic/Parallel runner visibility, fatal playback/reentrancy and cancellation poisoning.
- Seeded FIFO reference-model checks: 300 seeds.
- Warmed common C# full paths: 0 B for native/dynamic/scalar/pointer payloads and creation.
- Canonical Lua N=0/1/16/256/4096 probes pass their approved ceilings; N=4096 allocates 7,538,136 B/callback. Lua remains experimental; this is not a production real-time SLA.
- Fresh complete Runner: 640/640, no skips, exit0; Generator: 78/78, no skips, exit0.
- Standalone common-only probe at N=0/1/16/256/4096: 0 B steady state at every size; timings/cold costs retained in evidence.
- Independent read-only review: no open actionable findings. Recovery docs corrected and direct common nested/fixed/readonly probes added; all15 flat/dynamic/native workloads measure0B steady state.
- git diff --check and changed-document local links verified.

Compatibility: existing immediate APIs remain unchanged. New APIs have explicit capacity limits, owner/token lifetime, readonly/pointer rules and no-last-component-deletion semantics. No save/network/reload or new FixedUpdate integration. No NativeAOT/manual-game/editor-reload acceptance is claimed.

## Publication state

User authorized implementation on current `codex/modding-0.7` (no new branch). Task diff base: `86a5ffaecc1b0d030d59bb3dbe5092a8e3052ebd`.

Read-only GitHub verification on 2026-11-13: [existing draft PR #101](https://github.com/Karpiklalloly/Karpik.Engine/pull/101) is open, titled `fix: Restore cooked asset loading during modding refactor`, base main@d8ee92050999e57d87e1b45271f82e4891c5cecc, head codex/modding-0.7@4767dcad7529ff2cbf2d37475bcb6504a5e1b1b4. The previous type-catalog commit 86a5ffa is not in that remote head. Pushing this reused branch would publish that prerequisite and broaden the existing PR, which this plan explicitly does not authorize automatically. No push or PR mutation has been performed. A feature-only publication requires resolving the prior stage/branch scope explicitly; this file is the prepared review text, not a claim that a PR exists.
