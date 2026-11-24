---
status: completed
complexity: complex
complexity_source: auto
development_mode: tdd
source_design: lua-structural-commands-design.md
design_revision: 14
plan_revision: 2
approved_plan_revision: 1
approved_design_revision: 14
---

# Добавить отложенное создание сущностей и структурные команды Lua

This ExecPlan is maintained according to `/home/artem/git/Karpik.Engine/plans/PLANS.md`. Its separate architectural source is `/home/artem/git/Karpik.Engine/plans/lua-structural-commands-design.md`, revision 14. Ready design does not authorize implementation.

## Purpose / Big Picture

Lua-система сможет создавать pending entity через `ctx.commands:create()` и ставить `ctx.commands:add(pool, target [, snapshot])` / `ctx.commands:del(pool, target)` в ограниченный буфер. Snapshot копируется при постановке; настоящий мир меняется после успешного callback и проверки всей очереди, до следующей системы. Существующие immediate pool APIs сохраняются. Полный поведенческий контракт D1-D15 и AC1-AC8 находится в source design; ниже приведены необходимые для исполнения условия и проверки.

Область: Shared common buffer/preflight/playback, Lua descriptors, callback/error integration, host options, allocation/lifecycle tests, API/ADR documentation. Вне области: direct destroy, persistent creation receipts, deferred set/upsert, individual FixedUpdate adapters, save/network/reload, изменение сторонних DragonECS/MoonSharp sources, глобальный запрет роста старых immediate APIs или переписывание scheduler.

## Progress

- [x] (2026-11-12) Прочитаны source design/spec/ADR, актуальные binding/runtime/backend исходники; D1-D13 выбраны пользователем.
- [x] (2026-11-12) Сохранён ready design revision 14; определены AC1-AC8, auto complex и TDD.
- [x] (2026-11-12) Проверена структура документов: ready design r14 без open questions, AC1-AC8 покрыты, WP-00..05 и зависимости корректны, обязательные ExecPlan sections присутствуют. Repository worktree по-прежнему clean; код/сборки/тесты не выполнялись.
- [x] (2026-11-13) Plan r1/design r14 и allocation gate утверждены; пользователь заменил dependent branch на текущую codex/modding-0.7.
- [x] (2026-11-13) WP-00: current branch authorized, docs moved; fresh build0errors/52existingwarnings, targeted baseline16/16.
- [x] (2026-11-13) WP-01: bounded common API, payload capture, owner/token/lifetime checks; fresh integrated57/57.
- [x] (2026-11-13) WP-02: FIFO/model/capacity/liveness/pointer checks, no-growth/fatal playback; fresh integrated57/57.
- [x] (2026-11-13) WP-03: real Lua API and policies/pcall/token cases; fresh integrated57/57.
- [x] (2026-11-13) WP-04: final feature57/57, full Runner640/640 and Generator78/78; common-only probe N0..4096 steady0B; old allocation gates remain green.
- [x] (2026-11-13) WP-05: independent reviewer no open actionable findings; docs/evidence/probe/PR text verified. Publication blocker recorded; no push or PR mutation.

## Surprises & Discoveries

- Immediate add/del уже реализованы; старый общий modding design предусматривал deferred buffer без окончательных точек применения. Не переносить старые неподтверждённые checklist статусы на новую задачу.
- Native `EcsPool<T>.Capacity` включает нулевой элемент. Проверять peak count против Capacity-1; dynamic dense capacity не имеет этого sentinel.
- `world.Count` не учитывает неосвобождённые deleted IDs. По текущему исходнику обычный dispenser занят `1 + Count + DelEntBufferCount` элементами; проверять эту формулу тестами на deferred deletions.
- `world.Entities` / `ToSpan()` может вызвать release/удаление пустых сущностей. Preflight не должен перечислять мир этим путём.
- Private DEBUG IdDispenser debugger proxy не является runtime API; reflection/Unsafe чтение private dispenser запрещено решением этого плана.
- Создание/удаление backend-компонентов синхронно вызывает native listeners. Нет обещания transactional observer delivery или no-GC для произвольного callback host. Неожиданные reentrant mutations — fatal infrastructure failure.
- Local main d8ee920 отстаёт от текущего prerequisite branch на 29 коммитов. HEAD codex/modding-0.7: 86a5ffaecc1b0d030d59bb3dbe5092a8e3052ebd, tracking ahead 1, worktree clean при проверке. Remote refs пока не обновлялись.

## Decision Log

- 2026-11-12 / user D1-D13: preserve immediate APIs; separate deferred API; apply after callback; discard on callback/command error; callback-local pending token; queries for later access; create and component add/del for pending/existing entities; enqueue-time snapshot copy; strict FIFO; full preflight; host fixed limits; reuse mutable query handles; reject final-component deletion/empty creation; caught deferred errors invalidate whole queue.
- 2026-11-12 / author: complex автоматически выбран из-за cross-layer lifecycle/preflight/capacity рисков, а не числа файлов. TDD для observable behaviors; docs/branch/research — direct checks. Complex execution использует динамическую делегацию там, где независимые пакеты полезны, и обязательное отдельное read-only review после интеграции; план не назначает агентов или волны.
- Предложение для единого утверждения: dependent task branch `codex/lua-structural-commands` от `codex/modding-0.7@86a5ffa`. Parent/reason записать после recheck. Если prerequisite уже вошёл в свежий main, использовать task branch от main после проверки наличия всех prerequisite APIs. Не ветвиться от посторонней task branch.
- Новый proposed experimental Lua canonical ceiling: `4096 + 1600*C + 1600*V + 544*O` B/callback; определения в source design. Existing f32/typed/composite gates неизменны. Common warmed successful enqueue/preflight/playback — 0 B. Это не performance SLA и не отмена VM experimental статуса.
- Утверждение этого плана не разрешает push непубликованного parent-коммита или расширение существующего PR. Новый изолированный task PR подготовить/создать по AGENTS.md, если parent prerequisites доступны remote; иначе точно сообщить publication blocker и предоставить title/body.

## Outcomes & Retrospective

Реализация принята по WP-00..04: fresh feature57/57, full Runner640/640, Generator78/78 и common probe N0..4096 steady0B. Независимое read-only review завершено без открытых actionable findings; все AC1–AC8 закрыты. Publication blocker подготовлен по разрешённой альтернативе AC8, push/PR не выполнялись. Подробные результаты и fingerprints — в lua-structural-commands-evidence.md. Notes в docs/knowledge автоматически не записывались.

## Context and Orientation

Workspace `/home/artem/git/Karpik.Engine`. Common layer: `Modules/Shared/Modding/Modding.Core/Bindings/{ComponentCallbackScope.cs,PreparedComponentHandle.cs,PreparedComponentQuery.cs,ComponentQueryPreparer.cs,IComponentPoolAccess.cs,ClrComponentPool.cs,DynamicComponentPool.cs,DynamicComponentStorage.cs,ComponentSnapshot.cs,ComponentPointerRegistry.cs}`. Add cohesive common buffer/options/target types under the same module; exact private filenames may adapt without changing public contracts.

Lua layer: `Modules/Shared/Modding/Modding.Lua/{LuaEcsContext.cs,LuaScriptRuntime.cs,LuaSimulationPreparation.cs}` and `Systems/LuaUpdateSystem.cs`. LuaPool now retains its specific prepared handle internally for command capability checks, in addition to existing callbacks. MoonSharp must remain absent from Modding.Core. Existing services use `[Export]` + `[ServiceRegistration]` or explicit installer for real factories, and constructor injection with Simulation ownership.

Scheduling accesses are currently inferred in `ComponentQueryPreparer.InferAccesses`; mutable dynamic handles also infer DynamicComponentAnchor Write. LuaUpdateSystem is SequentialSystem. Preserve that serialization and test real and synthetic dependency evidence so sequential behavior does not conceal incorrect accesses.

Backend is read for semantics, not edited: `third-parties/DragonECS/src/{EcsWorld.cs,EcsWorld.pools.cs,Pools/EcsPool.cs,Internal/IdDispenser.cs,entlong.cs}`. `GetEntityLong` wakes generation; `IsAliveSafe` checks liveness with bounds. Free-slot accounting, native sentinel capacity, empty cleanup and listeners are correctness-critical.

Tests live in `tests/Karpik.Engine.Core.Runner.Tests`, including `LuaBindingsTestScope.cs`, `LuaEcsBindingsTests.cs`, `LuaCatalogRunnerTests.cs`, `ComponentSnapshotTests.cs`, `DynamicComponentStorageTests.cs`, `Lua*AllocationTests.cs`, and `LuaSystemRegistrationTests.cs`. Generator tests live in `tests/Karpik.Engine.Core.Generator.Tests`. .NET target is net10.0/xUnit; existing test fixtures provide real VM/world/preparation instead of mock-only acceptance.

Terms: pending token = immutable identity of one not-yet-materialized entity in this callback; batch = all deferred commands of this invocation; payload = copied writable snapshot leaves; preflight = validation/simulation of FIFO effects without mutation of ECS world; playback = synchronous actual application. A logical create declares a token; physical NewEntity may occur at its first add. Backend event order and numeric IDs follow materialization, not token declaration order.

## Real-Time Assessment

Hot paths are existing declared callbacks and new enqueue/preflight/playback. Use dense fixed command/payload/scratch buffers owned by Simulation and reused across nonoverlapping callbacks. No per-command classes/delegates, boxing, LINQ, reflection, string resolution, locks, blocking I/O or array growth in common successful paths. Prepared references are acceptable metadata, not per-entity managed gameplay state. Component gameplay data remain unmanaged structs/typed SoA.

Host defaults: 1024 commands including create, 256 pending entities, 256 KiB payload. Limits validated with checked arithmetic at startup; zero disables submission; host may override. Validation scratch sized from bounded commands/targets without unbounded adversarial hash probing. Avoid maximum buffer duplication per system. Expected common steady-state allocation = 0 B. Immutable VM wrappers may allocate under new named Lua ceiling; setup/factory/error/formatting costs reported separately.

Use current reserved world/native/dynamic/anchor capacities. Insufficient capacity rejects the entire batch before new ECS mutation; do not grow silently during playback. Compute FIFO peak membership and occupancy, not just final state. Never call world.Entities during preflight. No full-game no-GC claim is made for immediate operations or arbitrary backend host listeners.

Preserve Shared/Client/Server boundaries and fixed-dt invariants. This adds no timing integration or new frame-delta gameplay examples; existing callback phase remains unchanged. No parallel VM execution or asynchronous cancellation mechanism. World and pointer policy must remain stable between validation and application except for validated playback changes; listener reentrancy cannot be tolerated as ordinary script behavior.

## Plan of Work

### WP-00 — Isolation, baseline and recovery anchors

- Status: completed
- Goal: isolate exactly this feature and establish trustworthy baseline.
- Scope: recheck git state/refs and parent prerequisite APIs; after explicit user approval, continue the current authorized branch (superseding the original dependent-branch proposal). Move this plan and its design to `plans/lua-structural-commands-execplan.md` / `plans/lua-structural-commands-design.md`, retaining one canonical copy of each and updating source links. Do not leave divergent /tmp copies; leave only redirects there if useful. Record exact parent and task diff base.
- Boundaries: no feature behavior edits or parent rewriting; no parent push.
- Depends on: none, plus explicit approval.
- Acceptance: AC4, AC8 prerequisites.
- Verification: clean/preserved worktree, exact branch/base recorded, document links valid; targeted fresh Runner Lua/bindings baseline and Generator baseline with actual counts and exit codes. Prior reported 583/583 and 78/78 are historical references, not fresh results.

### WP-01 — Common bounded queue, targets and typed payload capture

- Status: completed
- Goal: provide Simulation-owned no-GC submission primitives and immutable identity/ownership semantics.
- Scope: common command/options/target types under `Modding.Core/Bindings`, constructor DI, lifecycle integration with ComponentCallbackScope, prepared-handle capability validation, copied typed payload access from snapshots. Preserve readonly/default behavior and pointer policy. Introduce invocation generation and permanent invalid-batch state after any rejected command; reset safely for next callback/dispose.
- Boundaries: no MoonSharp types, no world mutation at enqueue, no new persistent receipts or schema changes. Do not implement payload capture as retained snapshot/live-view reference.
- Depends on: WP-00.
- Acceptance: AC2, AC3 submission, AC4, AC5 invalidation, AC7 capture.
- Verification: first meaningful Red tests for snapshot mutation after enqueue, scratch reuse across commands, readonly/foreign/wrong-layout snapshots, expired/cross-owner/cross-Simulation tokens/handles, bounds/overflow caught errors, empty queues, host invalid settings and generation exhaustion. Then Green, reuse/disposal tests and common enqueue allocation measurement. No new test dependency without need.

### WP-02 — Full preflight and bounded playback

- Status: completed
- Goal: validate every predictable error before any deferred ECS mutation and apply successful batches without growing storage.
- Scope: prepared capacity metadata in `IComponentPoolAccess` and its native/dynamic implementations or companion internal capability; full logical native+dynamic counts in DynamicComponentStorage/prepared world access; generation checks; simulated FIFO membership/counts; no last-component del; every token initialized; peak capacity including anchor and free entity slots; pointer revalidation. Validate once, then materialize new targets at first add and execute add/del/default or writable-payload initialization synchronously.
- Boundaries: no third-party edits, raw-memory readonly bypass, fake world.Entities preflight, optimistic partial script validation, mid-batch asynchronous yield, or suppressed infrastructure failures. Readonly leaves default; unknown/internal CLR fields are not cloned. A newly created empty interval must not span another materialization or arbitrary host/script execution introduced by this feature.
- Depends on: WP-01.
- Acceptance: AC1 ordering, AC2 target validity, AC3 playback, AC5 preflight/fatal behavior, AC6, AC7.
- Verification: test strict duplicates/missing components and late-invalid command with unchanged whole deferred world; del-last using undeclared native and dynamic components; add-new->del-old; create-only rejection; interleaved pending tokens and mixed existing targets; native/dynamic/anchor peak saturation; null slot and unreleased deleted-ID accounting; reused IDs after enqueue; pointer revoke after capture. Deterministic seeded generated command sequences compare observable states to a simple reference model (100-1000 seeds), not private implementation structures. Fault injection demonstrates unexpected playback error propagation without promising rollback. Common full-cycle allocation tests at exact capacities; verify capacity/version counters and all payload kinds.
- Integration notes: payload/handle/capacity contracts touch WP-01 files; execution coordinates edits and builds rather than allowing conflicting writers.

### WP-03 — Lua descriptors and callback/error integration

- Status: completed
- Goal: expose the separate deferred API and enforce apply/discard at the exact system boundary.
- Scope: `LuaEcsContext` cached command table/descriptors and trusted pool extraction, immutable opaque pending wrappers, `LuaScriptRuntime.InvokeSystem` sequencing and contextual predictable error handling. Ensure zero-query contexts can use create but cannot mutate undeclared pools. No descriptors in gameplay components. Only valid owner can submit; callback exit always resets tokens/queue even when pcall caught an enqueue error. LogAndContinue continues to next system after a discarded batch; unexpected playback failures escape that catch boundary.
- Boundaries: immediate LuaPool Get/Add/Del semantics and query snapshot behavior unchanged; no Lua receipts/host addresses/reflection APIs, direct destroy, new legacy on_update context or individual FixedUpdate registration.
- Depends on: WP-01, WP-02.
- Acceptance: AC1, AC2, AC4, AC5, AC6 API, AC7 adapters.
- Verification: real scripts for create/add/del, reused snapshot input, foreign/read-only handles, token saved then reused in later callback, retained token not rebound, caught wrong payload/token/capacity errors, both error policies and existing immediate writes preserved. Verify reset after success/failure/cancellation and no recursive execution. Test all supported payload kinds through real MoonSharp, not only common helpers.

### WP-04 — Integration, lifecycle and performance acceptance

- Status: completed
- Goal: prove ACs through the production preparation, scheduler and Simulation paths.
- Scope: focused new test classes and existing fixture extensions in Runner.Tests, optional test fixture component additions under existing test modules; no production-only test hook API. Cover Lua A -> native B -> Lua C in Static/Dynamic x Deterministic/Parallel, actual Write inference and anchor dependencies, independent Simulation buffers, disposal/new Simulation and ModSet reuse.
- Boundaries: no manufactured manual-game/AOT acceptance or weakened historical allocation gates. Do not repeatedly run full suites without changed evidence.
- Depends on: WP-02, WP-03.
- Acceptance: AC1-AC8 integration.
- Verification: focused tests first; common warmed complete paths 0 B for native/dynamic/nested/fixed/exact/pointer cases with preallocated fixtures and representative N=0/1/16/256/4096 under configured test capacities. Lua canonical workload tests against new proposed ceiling, record raw bytes/time/Gen0/peak memory separately; benchmark first/cold/steady and max-accepted-batch preflight latency. Evidence reports user-configured limits and when configured memory is insufficient. Run fresh full Runner and Generator suites after integration, not old DLLs; record existing warnings/skips accurately.

### WP-05 — Independent review, API documentation and publication

- Status: completed
- Goal: deliver a reviewable isolated change with factual verification and recoverable plan state.
- Scope: independent read-only review subagent after integration compares implementation to source design revision 14 and AC1-AC8, focusing on last-component counts, capacity off-by-one, pending identity, caught-error invalidation, partial application/fatal routing, lifetime/DI and unexpected allocation. Main agent resolves actionable findings and reruns affected checks. Update `docs/modules/modding/typed-bindings.md`, `docs/02_ADR/lua-ecs-common-bindings.md` and umbrella modding plan status only where changed; add concise `plans/lua-structural-commands-evidence.md` with commands/results/limits and final fingerprints.
- Boundaries: reviewer does not edit plan/production sources; no unrelated changelog/release scope or automatic knowledge notes. Do not claim independent review without running it. Do not publish parent prerequisites without explicit instruction.
- Depends on: WP-04.
- Acceptance: AC4 documentation, AC8; final audit all AC1-AC8.
- Verification: all actionable findings closed or explicitly unresolved (task is incomplete if mandatory findings remain), targeted rechecks after fixes, fresh final results consistent with final source, `git diff --check`, documentation links valid, task diff against exact parent contains only feature. Push/create task PR against the approved parent only if remote prerequisites permit correct isolated diff; apply actual repository change-category labels after inspecting existing configuration. After parent merges retarget main and recheck inherited diff. If publication is blocked, provide concrete blocker and prepared title/body without claiming a PR exists.

## Milestones

WP-00 ends with actual baseline; WP-01 with tested submission/identity/payload; WP-02 with full validation and no-growth application; WP-03 with real Lua error/visibility scenarios; WP-04 with fresh end-to-end and measured allocation evidence; WP-05 with independent review, accurate docs and isolated PR or reported publication blocker. Record evidence in Progress before accepting each dependent milestone. Execution selects delegation dynamically; shared build outputs and overlapping source files are serialized as necessary.

## Concrete Steps

All repository commands use cwd `/home/artem/git/Karpik.Engine`. Commands below are planned, not executed in this task. Use available dotnet SDK; verify toolchain before building. For this environment use `DOTNET_CLI_HOME=/tmp/karpik-dotnet-cli` and `NUGET_PACKAGES=/home/artem/.nuget/packages` when existing cache/setup requires them. Preserve unrelated user environment and files.

1. Read-only recheck: `git status --short --branch`, `git rev-parse HEAD`, `git log -1 --oneline main`, and relevant remote refs when available. Create the approved branch from the verified parent; never reset or discard changes to make it clean.
2. Small targeted fresh build: `dotnet build tests/Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj -c Release -m:1 -nr:false`. Use `--no-restore` only after successful restore or verified assets. If offline cache requires it, restore the same project explicitly with `-p:RestorePackagesPath=/home/artem/.nuget/packages -p:NuGetAudit=false`; record why, do not hide unrelated dependency failures.
3. Targeted baseline: `dotnet test tests/Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj -c Release --no-build --no-restore -m:1 -nr:false --filter 'FullyQualifiedName~Lua|FullyQualifiedName~ComponentSnapshot|FullyQualifiedName~DynamicComponentStorage|FullyQualifiedName~MixedComponentQuery' --logger trx`.
4. New behavior checks: `dotnet test tests/Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj -c Release -m:1 -nr:false --filter 'FullyQualifiedName~StructuralCommand' --logger trx`. Name new classes consistently so this filter actually discovers tests. Observe meaningful Red, then Green; discovery/build failures are not valid Red. Subsequent no-build runs must follow a build of the final changed source.
5. Generator refresh/check: `dotnet test tests/Karpik.Engine.Core.Generator.Tests/Karpik.Engine.Core.Generator.Tests.csproj -c Release -m:1 -nr:false --logger trx`.
6. Final fresh Runner after targeted checks: `dotnet test tests/Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj -c Release --no-build --no-restore -m:1 -nr:false --logger trx`; build again first if source changed. Rerun Generator only when changes since its last fresh run justify it. If VSTest loopback socket is sandbox-restricted, request the permitted execution escalation for that exact test command and report an actual rejection if one occurs.
7. `git diff --check` and explicit final source/test artifact fingerprints. Determine branch diff using the recorded full parent SHA, not integration history guessed from branch names.

Store TRX/probe/benchmark artifacts under an ignored task directory or /tmp; record durable summaries in evidence. Never commit binaries or substitute historical passing tests for a current run.

## Validation and Acceptance

AC1 -> WP-02/03/04; AC2 -> WP-01/02/03/04; AC3 -> WP-01/02/04; AC4 -> WP-00/01/03/05; AC5 -> WP-01/02/03/04; AC6 -> WP-02/03/04; AC7 -> WP-01/02/03/04; AC8 -> WP-00/04/05. Completion requires all of these plus exact fresh results and independent review.

Negative tests verify zero deferred mutation on predictable failure with existing immediate writes retained, rather than only asserting an exception. Capacity tests compare counters/capacities before/after and exercise exact boundary plus one overflow. Liveness tests include native-only, dynamic-only, mixed and components outside declared handles. Reference-model cases verify FIFO semantics and invalid-batch behavior. Do not claim raw int IDs become persistent handles. Faulted playback may leave partial mutation but must prevent subsequent systems and stop Simulation. Native host callbacks remain outside atomic/no-GC guarantees and must not reentrantly mutate during validation/playback.

No manual game run, NativeAOT, 32-bit, editor hot reload, save/network acceptance is inferred from these tests. If not run, record not run. They are outside this feature's completion gates rather than hidden passes.

## Idempotence and Recovery

Read-only exploration, builds and tests are repeatable; account for shared output directories with subagents. Branch creation is not repeated if already on the correct task branch; verify parent/base and preserve unrelated changes. After approval relocate the two canonical documents once and update links, not maintain duplicate state.

Failed/invalid callbacks clear their entire deferred batch and expire tokens. Disposal clears arena and detaches any Simulation listeners exactly once. Unexpected playback failure stops the Simulation; recovery creates a fresh Simulation rather than retries a partially applied batch. Development rollback uses narrowly scoped commits/reverts only with appropriate task context; never hard reset or clean the shared workspace. Material changes to contract/ACs reopen only affected design choices and require scoped reapproval; private implementation adaptation does not.

If a prerequisite/API/capacity formula proves wrong, stop dependent implementation, record the counterexample and revise the affected design, rather than relaxing no-GC, cloning arbitrary readonly memory or editing third-party code unnoticed. Parent publication is not an automatic workaround for a blocked task PR.

## Artifacts and Notes

- Canonical current design: `/home/artem/git/Karpik.Engine/plans/lua-structural-commands-design.md` revision 14.
- Canonical current plan: `/home/artem/git/Karpik.Engine/plans/lua-structural-commands-execplan.md` revision 2; approved baseline plan r1 / design r14.
- Current repository artifacts: `plans/lua-structural-commands-design.md`, `plans/lua-structural-commands-execplan.md`, `plans/lua-structural-commands-evidence.md`.
- Existing intent: `plans/script-modding-design.md`, `plans/lua-ecs-bindings-spec.md`; current type catalog evidence: `plans/modding-types-execplan.md` / `plans/modding-types-evidence.md`.
- Prepared PR title: `feat: Add deferred Lua entity creation and component commands`.
- Prepared PR body baseline: Lua systems currently lack deferred entity creation and structural edits. Add a bounded, Simulation-owned command API that copies snapshot input, validates the whole callback batch and applies before the next system while preserving immediate APIs. Invalid batches are discarded; unexpected playback failures stop the Simulation. Verification and limitations must be replaced with actual final results before publication. Compatibility: no change to existing immediate API; new command API has explicit limits, pending-token lifetime and no-last-component-deletion rules. Parent prerequisite and retargeting requirement must be explicit in a dependent PR.

## Risks / blockers

Implementation approved 2026-11-13 in current branch; dependent-branch proposal superseded. Publication remains constrained by inherited branch history and previous no-parent-push scope; do not broaden an existing PR or claim a new isolated PR without verifying remote state. Baseline VSTest socket restriction was handled by authorized exact-command escalation. New feature Red tests failed as expected before implementation; initial integration test compilation errors were fixture issues, not accepted runtime test results.

Largest technical risks: listener reentrancy, creation materialization versus logical FIFO, world free-slot accounting under deletion buffers, complete logical component counts, native sentinel capacity, bounded scratch cost at max limits, and exact typed payload copying without accidental readonly writes. Each has explicit tests in WP-02/04 and reviewer focus in WP-05.

## Adjustments

Plan revision 2: WP-01/02 implemented as one cohesive common backend assignment; Lua descriptors and runner acceptance tests were prepared in parallel against the stable public contract after meaningful feature/common Red. Package acceptance remains gated by actual dependent integration checks. This technical reordering preserves all public contracts/ACs, so approved r1/design r14 remain the authorization baseline. Generator baseline is consolidated with the final fresh Generator regression to avoid redundant shared-output builds; the targeted16-test baseline remains the runtime starting evidence.

## Execution authorization — 2026-11-13

User: «Согласен, делай в этой ветке». Plan r1/design r14 and allocation gate approved; current codex/modding-0.7 explicitly authorized instead of dependent branch. Task diff base 86a5ffaecc1b0d030d59bb3dbe5092a8e3052ebd. No branch switch. Existing parent publication restrictions remain. Earlier dependent-branch proposal is superseded by this authorization.

### WP-00 entry evidence — 2026-11-13

Current branch/HEAD confirmed, worktree initially clean. Canonical documents moved from /tmp to plans; /tmp contains redirects. First build could not write default CLI sentinel under read-only /home/artem/.dotnet; restarted with DOTNET_CLI_HOME=/tmp/karpik-dotnet-cli and NUGET_PACKAGES=/home/artem/.nuget/packages. This is environment setup, not a source/test failure. At this entry checkpoint the baseline build was running and no runtime edits existed; later checkpoints below record completion. Read-only capacity audit confirms normal-lifecycle free-slot formula and native sentinel capacity.

### Baseline and first Red — 2026-11-13

Fresh Release Runner build passed with 0errors/52 existing warnings. Targeted snapshot/query/end-to-end baseline16/16 exit0. VSTest required exact-command sandbox escalation for loopback SocketException13; escalation succeeded. Added LuaStructuralCommandTests before implementation: fresh build0errors/7existingwarnings;5 tests ran,3 failed/2 passed, confirming nil ctx.commands and absent created state (not compilation failures). Logs /tmp/karpik-structural-{baseline,lua-red}-*.log; TRX structural-baseline.trx and structural-lua-red.trx. Common core WP-01/02 delegated together due shared contracts; root owns Lua files; separate test-only agent drafts runner/performance acceptance. Builds serialized by lease.

### Common Red and integration preparation — 2026-11-13

Common stubs and tests compiled;3/3 tests failed at NotImplementedException as expected, not at build/discovery. Main then implemented Lua command descriptors, immutable token wrappers, bound-owner checks and callback Complete/discard integration with fatal exception exclusion. Common implementation and boundary/property/0B tests are being verified by its author; acceptance still pending. New runner test compile errors CS9007/CS9174 were corrected by test author. No runtime Green claimed yet.

### Integrated acceptance checkpoint — 2026-11-13

Final fresh Release Runner build0errors/7existingwarnings. All57 StructuralCommand tests pass exit0,4s (structural-integrated.trx). Source includes sticky poisoning for cancelled playback, direct anchor-write rejection, cheap empty-batch Complete and corrected dynamic fixture. Both real composition modes and schedulers, four fatal scheduler cases, all typed/pointer common0B probes and five Lua allocation workloads pass. Early compile-only failures were fixed; no old DLL substituted. Full fresh Runner regression now running; Generator/review still pending, WP-04/05 not accepted. Successful common packages accepted by root after actual source inspection and these integrated results.

### Global verification — 2026-11-13

Fresh full Runner640/640 exit0/no skips,7m16s. Fresh Generator78/78 exit0/no skips,12s. Standalone common-only probe on identical final DLLs completed exit0: N0/1/16/256/4096 all steady0B; cold and raw p50/p95/max recorded in evidence. [Evidence](lua-structural-commands-evidence.md) includes exact TRX totals/source hashes. Separate reviewer assigned after these global runs; review pending, WP-05 still in progress. Source unchanged after final build. Read-only GitHub PR101 check confirms remote head4767dca and old draft scope; prepared title/body saved, no push/PR mutation.

### Review documentation cleanup — 2026-11-13

Independent reviewer noted P3 stale planning-time state in design/Artifacts. Root normalized canonical paths, approved baseline, current branch decision and recovery text without changing behavior/source. Current docs distinguish historical checkpoints from present execution. Review runtime verdict still pending.

### Final acceptance — 2026-11-13

All WP-00..05 accepted. Final independent reviewer found no P1/P2 code defects; P3 stale recovery documentation corrected/rechecked, and direct common nested/fixed allocation evidence added via probe-only code. Reviewer confirmed all15 flat/dynamic-nested-fixed/native-nested-fixed workloads steady0B and closed the evidence note. Final review: no open actionable findings. Production/test source stayed unchanged after final fresh Runner build and 640/640 +78/78 regressions; only docs/probe artifacts changed. Durable probe build0errors0warnings/runexit0. Source hashes, actual TRX counts, cold/JIT/outliers and Lua experimental allocation limits are retained in evidence. No NativeAOT/32bit/manual/editor/save/network claims.

Publication limitation: read-only PR101 confirms reused branch head4767dca; pushing current branch would also publish preceding type-catalog stage and widen that old draft. The approved no-parent-push/no-automatic-PR-broadening scope prevents that action. Exact prepared feature PR title/body is in lua-structural-commands-pr.md; this is not a permissions/tool failure and no publication is claimed. User explicitly selected this branch instead of an isolated dependent branch.

Proposed learnings (not written to docs/knowledge):1) world.Count excludes unreleased deleted-ID occupancy;2) preflight must simulate peak FIFO membership and complete logical components, not final counts;3) caught command API failures must poison the whole batch;4) noGC common payload guarantees do not imply noGC Lua VM or native listener code.
