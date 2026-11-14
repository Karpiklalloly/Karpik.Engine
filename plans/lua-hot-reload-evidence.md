# Lua code-only hot reload — evidence

Date: 2026-11-14. Task base fe018fd, branch codex/modding-0.7 by explicit user exception. [Design r10](lua-hot-reload-design.md), [approved ExecPlan r1](lua-hot-reload-execplan.md). SDK 10.0.115, observed CLR 10.0.15, Release/net10.0, Linux x86_64 host. This document records actual checks; completion/review status is tracked in the ExecPlan.

## Current verification

- Fresh baseline Runner build:0 errors/52 warnings; baseline selected91/91, no skips.
- WP-01 test-first:7 failures from explicit CreateRevision stub, then feature7/7; binding/command/allocation regression selection35/35.
- Cross-package runtime Red:13 expected failures (3 scheduler-mode orchestration,1 worker lease,9 Lua lifecycle). Explicit stubs failed behavior assertions rather than missing-API compilation.
- First integrated selection:27 passed; ticker test intentionally Red (Expected1, Actual2 fixed ticks after pause).
- Follow-up selection:32 passed,2 new meaningful failures: host Dispose InterpreterException misclassified as harmless hook error, async no-scheduler pending runtime not disposed at owner teardown. Both corrected without weakening assertions.
- Fresh latest build:0 errors/7 existing warnings. New owner-thread scheduler tests suppress xUnit blocking warnings only where they explicitly pump the owning scheduler and read completed tasks.
- Latest new feature selection:62/62 passed, no skips, exit0,6s. TRX `tests/Karpik.Engine.Core.Runner.Tests/TestResults/_hp-artem_2026-11-14_11_42_53.trx`. Includes real ClientSimulationWorker boundary, shutdown/cancel/Busy, before-start/after-shutdown rejection, root orchestration, Lua lifecycle and actual file replacement.
- Full Runner final CI-serialized run:702/702 passed, no skips, exit0,6m24s. TRX `tests/Karpik.Engine.Core.Runner.Tests/TestResults/_hp-artem_2026-11-14_11_46_01.trx`. Existing `.github/runner-tests.xunit.json` copied into output; no test code/deadline was changed to hide failures. Initial60s watchdog aborted after498/0 during heavy MixedFive N4096;180s retry completed698passed/1failed with existing ProcessManager replacement-worker readiness10s timeout. That test passed alone1/1; final repository CI serialization passes it within the whole suite. Keep this timing flake visible rather than treating failed/aborted trials as passes.
- Generator final:80/80 passed, no skips, exit0,10s. TRX `tests/Karpik.Engine.Core.Generator.Tests/TestResults/_hp-artem_2026-11-14_11_55_10.trx`. First attempt77/78 exposed optional-null scheduler factory mismatch; RuntimeCompositionGenerator now matches Dynamic DI null-default behavior, and both registered/unregistered scheduler cases execute actual Lua preparation.
- ECS.Core final:67/67 passed, no skips, exit0,726ms. TRX `tests/ECS.Core.Tests/TestResults/_hp-artem_2026-11-14_11_56_22.trx`.
- Independent post-integration review completed by fresh read-only agent `/root/independent_review` (not an implementation agent): no actionable findings against design r10/AC1–AC8. Reviewer inspected production changes, new files, relevant tests and recorded evidence; no edits/builds/test reruns. Earlier implementation-agent usage-limit failures were real, but a subsequent independent reviewer was available. No self-review was substituted.

Additional test-first fixes: shutdown entry closure Red Expected1/Actual2, then explicit gate + in-flight frame drain Green; logging failure Red skipped old runtime unload, then best-effort cleanup with fatal host logging failure Green. `DrainCodeReloadForShutdownAsync` is permanent shutdown, ordinary cancellation uses the request token. Source/binary changes after early59 result are covered by final62/full702 above.

## Behavioral/resource evidence

Real-file tests cover Dynamic/Static × Deterministic/Parallel, plus SingleThread orchestration smoke. They retain world identity, entity ID/generation, native data and dynamic Temperature across changed behavior, system addition/removal and zero-system revisions. Schema/syntax/query/graph/on_reload rejection preserves old version and subsequent callbacks. Pointer/nested/fixed preservation and old handle/query/view/snapshot invalidation are additionally covered in common binding revision tests.

One test executes100 deterministic seeds ×3 operations:300 host requests,200 successful replacements interleaved with rejection or pre-cancel.600 retired owner/VM/context weak references are collected after fixture teardown. Separate probes run20 replacements each at N=0/1/16 systems over16 entities. Warmed no-Lua/common ticks before/after reload allocate0B; existing VM allocation ceilings are preserved, not replaced by these common measurements.

Full successful per-case probe output is saved in [results.txt](probes/lua-hot-reload/results.txt), including preparation/commit/retirement and allocation samples. [Harness/command](probes/lua-hot-reload/README.md), [source/DLL SHA256](probes/lua-hot-reload/fingerprints.json). These metrics are process-wide; sampled peaks at2ms are lower bounds and include CLR/test runner/sampler activity. They are not isolated worker counts or a production SLA. Lua remains experimental.

## Commands and limitations

Working directory `/home/artem/git/Karpik.Engine`. Environment: DOTNET_CLI_HOME=/tmp/karpik-dotnet-cli, NUGET_PACKAGES=/home/artem/.nuget/packages, VSTEST_CONNECTION_TIMEOUT=180. All builds use -m:1 -nr:false. Initial .NET default HOME sentinel write failed; rerun with writable CLI_HOME succeeded. Sandbox denied VSTest loopback socket before tests began; approved escalated local runs produced the results above. Aborted sandbox runs are not passes.

Latest feature command:

```bash
dotnet test tests/Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj -c Release --no-build --no-restore -m:1 -nr:false --filter 'FullyQualifiedName~LuaHotReload|FullyQualifiedName~ClientSimulationWorkerMaintenanceTests' --logger trx --blame-hang --blame-hang-timeout 30s --blame-hang-dump-type none
```

Build/test logs: `/tmp/karpik-reload-{baseline,wp01,integrated,followup,green,boundary}*.log`. Raw TRX/sequence attachments remain in Runner TestResults (ignored build artifacts); portable reports/fingerprints above are repository artifacts. Repeat commands in ExecPlan. No NativeAOT, manual game/editor reload, network/save integration, schema migration or file watcher acceptance is inferred.

## Publication

No push/PR mutation. D10 allows current branch implementation but not automatic publication of previous unpublished task commits or expansion of the existing unrelated PR. Prepared feature review text: [title/body](lua-hot-reload-pr.md); publication requires an isolated authorized remote scope. This is a branch-scope constraint, not a claim that host permission denied a push.

## Final acceptance

WP-00..06 accepted by main agent. API/ADR links and changed-file whitespace checked, current C# source/DLL fingerprints captured. Full regression results are current; earlier failed/aborted diagnostic trials remain explicitly documented above. Publication fallback is complete with a concrete prepared PR title/body and D10 branch-scope blocker; no remote mutation claimed.
